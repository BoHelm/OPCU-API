using KitchenGateway;
using Xunit;

namespace Gateway.Tests;

/// <summary>Prüft die Grenzen und Geheimnisbereinigung des vom Projektteam ergänzten Diagnoseprotokolls.</summary>
public sealed class DiagnosticsLogTests
{
    [Fact]
    public void RuntimeHistoryIsBoundedAndKeepsItsNewestEntries()
    {
        var log = new DiagnosticsLog();
        for (var i = 0; i < 650; i++) log.Add("Info", "Test", "Ereignis " + i);
        var snapshot = log.Snapshot();
        Assert.Equal(500, snapshot.Length);
        Assert.Equal("Ereignis 150", snapshot[0].Message);
        Assert.Equal("Ereignis 649", snapshot[^1].Message);
        Assert.True(snapshot.Zip(snapshot.Skip(1)).All(p => p.First.Id < p.Second.Id));
    }

    [Fact]
    public void NestedErrorDetailsRemoveCredentialsQueriesAndPrivateKeys()
    {
        var log = new DiagnosticsLog();
        log.AddError("Test", "Bereinigter Testfehler", new InvalidOperationException(
            "GET https://sample-user:sample-password@example.org/api?api-key=sample-query-secret#sample-fragment",
            new Exception("api-key=sample-header-secret token=sample-token\n-----BEGIN PRIVATE KEY-----\nsample-private-key\n-----END PRIVATE KEY-----")));
        var details = Assert.Single(log.Snapshot()).Details!;
        foreach (var secret in new[] { "sample-user", "sample-password", "sample-query-secret", "sample-fragment", "sample-header-secret", "sample-token", "sample-private-key" })
            Assert.DoesNotContain(secret, details);
        Assert.Contains("https://example.org/api", details);
        Assert.Contains("Innere Ursache", details);
    }
}
