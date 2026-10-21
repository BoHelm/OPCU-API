using System.Text.Json;

namespace KitchenGateway;

/// <summary>Neutrale Softwareidentität mit einmaliger, identitätserhaltender Migration älterer Installationen.</summary>
/// <remarks>
/// Eine Namespace-URI ist Teil jeder ExpandedNodeId. Eine ApplicationUri ist Teil des
/// OPC-UA-Anwendungszertifikats. Beide dürfen bei einem Update nicht stillschweigend wechseln:
/// sonst verlieren Clients ihre Knotenbindungen beziehungsweise ihr Zertifikatsvertrauen.
/// Die alten URIs kommen deshalb ausschließlich bei der Migration vorhandener Konfigurationen vor.
/// </remarks>
public static class GatewayIdentity
{
    public const string DefaultNamespaceUri = "urn:kitchen-gateway:diagnostics";
    public const string ProductUri = "urn:kitchen-gateway:product";

    /// <summary>Lädt eine vorhandene Konfiguration und ergänzt fehlende Identitätsfelder ohne PKI-/NodeId-Wechsel.</summary>
    public static GatewayConfig? Load(GatewayStore store)
    {
        var document = store.Read<JsonElement>("gateway.json");
        if (document.ValueKind == JsonValueKind.Undefined) return null;
        var config = document.Deserialize<GatewayConfig>(JsonDefaults.Options)
            ?? throw new InvalidOperationException("Gespeicherte Gateway-Konfiguration ist leer.");
        var changed = false;
        if (!document.TryGetProperty("namespaceUri", out _))
        {
            config = config with { NamespaceUri = "urn:bohelm:connectedkitchen:gateway" };
            changed = true;
        }
        if (!document.TryGetProperty("applicationUri", out _))
        {
            config = config with { ApplicationUri = $"urn:bohelm:kitchen:{config.GatewayId:D}" };
            changed = true;
        }
        if (changed) store.Save("gateway.json", config);
        return config;
    }

    /// <summary>Verhindert leere, relative und von URI-Parsern stillschweigend umgeschriebene Bezeichner.</summary>
    public static bool ValidUri(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512 &&
        value == value.Trim() && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsWellFormedOriginalString();
}
