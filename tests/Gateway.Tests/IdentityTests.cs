using System.Text.Json;
using System.Text.Json.Nodes;
using KitchenGateway;
using Xunit;

namespace Gateway.Tests;

/// <summary>Schützt die herstellerneutrale Ersteinrichtung und die Identität bestehender Clientbindungen.</summary>
public sealed class IdentityTests
{
    private static readonly ModelCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Models"));

    [Fact]
    public void NewInstallationUsesNeutralIdentityAndAcceptsIndependentSoftwareProvider()
    {
        var config = new GatewayConfig { NamespaceUri = "urn:example:customer:kitchen", ManufacturerName = "Example Integrator" };
        Assert.StartsWith("urn:kitchen-gateway:application:", config.EffectiveApplicationUri);
        Assert.Equal("urn:kitchen-gateway:diagnostics", new GatewayConfig().NamespaceUri);
        Assert.Empty(ConfigValidation.Validate(config, Catalog));
        Assert.Contains(ConfigValidation.Validate(config with { NamespaceUri = ModelCatalog.Hki }, Catalog),
            e => e.Contains("außerhalb"));
        Assert.Contains(ConfigValidation.Validate(config with { ApplicationUri = "relative/path" }, Catalog),
            e => e.Contains("ApplicationUri"));
    }

    [Fact]
    public void LegacyConfigurationKeepsNamespaceApplicationAndDeviceIdentityAfterMigration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gateway-identity-" + Guid.NewGuid());
        try
        {
            var store = new GatewayStore(Path.Combine(directory, "data"), Path.Combine(directory, "keys"));
            var original = DemoConfiguration.Create("http://localhost:5090");
            var json = JsonSerializer.SerializeToNode(original, JsonDefaults.Options)!.AsObject();
            json.Remove("namespaceUri"); json.Remove("applicationUri");
            json.Remove("manufacturerName"); json.Remove("productName");
            store.Save("gateway.json", json);
            var migrated = GatewayIdentity.Load(store)!;
            Assert.Equal("urn:bohelm:connectedkitchen:gateway", migrated.NamespaceUri);
            Assert.Equal($"urn:bohelm:kitchen:{original.GatewayId:D}", migrated.EffectiveApplicationUri);
            Assert.Equal(original.GatewayId, migrated.GatewayId);
            Assert.Equal(original.Devices.Select(d => d.Identity), migrated.Devices.Select(d => d.Identity));
            var afterFirstLoad = File.ReadAllBytes(Path.Combine(directory, "data", "gateway.json"));
            _ = GatewayIdentity.Load(store);
            Assert.Equal(afterFirstLoad, File.ReadAllBytes(Path.Combine(directory, "data", "gateway.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExplicitCustomIdentitySurvivesReloadWithoutMigration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gateway-identity-" + Guid.NewGuid());
        try
        {
            var store = new GatewayStore(Path.Combine(directory, "data"), Path.Combine(directory, "keys"));
            var original = new GatewayConfig { NamespaceUri = "urn:example:custom:model", ApplicationUri = "urn:example:custom:application" };
            store.Save("gateway.json", original);
            var loaded = GatewayIdentity.Load(store)!;
            Assert.Equal(original.NamespaceUri, loaded.NamespaceUri);
            Assert.Equal(original.EffectiveApplicationUri, loaded.EffectiveApplicationUri);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
