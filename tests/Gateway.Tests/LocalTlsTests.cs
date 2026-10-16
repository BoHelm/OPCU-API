using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KitchenGateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Gateway.Tests;

/// <summary>Prüft den echten REST-Adapter gegen einen lokalen TLS-Server, nicht gegen ein Mock-HttpMessageHandler.</summary>
public sealed class LocalTlsTests
{
    [Fact]
    public async Task CustomLocalCaIsAcceptedButMissingTrustAndWrongHostnameAreRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kitchen-tls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var certificateFile = Path.Combine(directory, "ca.pem");
        await File.WriteAllTextAsync(certificateFile, certificate.ExportCertificatePem());
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        await using var app = builder.Build();
        app.MapGet("/api/devices", (HttpRequest incoming) =>
            CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(secret),
                System.Text.Encoding.UTF8.GetBytes(incoming.Headers["api-key"].ToString())) ?
                Results.Json(Array.Empty<object>()) : Results.Unauthorized());
        try
        {
            await app.StartAsync();
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var port = new Uri(url).Port;
            var store = new GatewayStore(Path.Combine(directory, "data"), Path.Combine(directory, "keys"));
            store.SetApiKey(secret);
            var valid = new GatewayConfig { ApiBaseUrl = $"https://localhost:{port}", ApiCaFile = certificateFile };
            using (var client = new ConnectedKitchenClient(valid, store))
                Assert.Equal(0, (await client.GetAsync("api/devices", CancellationToken.None)).GetArrayLength());
            using (var client = new ConnectedKitchenClient(valid with { ApiCaFile = null }, store))
                await Assert.ThrowsAsync<SourceFailure>(() => client.GetAsync("api/devices", CancellationToken.None));
            using (var client = new ConnectedKitchenClient(valid with { ApiBaseUrl = $"https://127.0.0.1:{port}" }, store))
                await Assert.ThrowsAsync<SourceFailure>(() => client.GetAsync("api/devices", CancellationToken.None));
        }
        finally
        {
            await app.StopAsync();
            Directory.Delete(directory, true);
        }
    }
}
