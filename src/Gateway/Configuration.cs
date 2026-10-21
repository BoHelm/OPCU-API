using System.Text.Json;
using System.Text.Json.Serialization;

namespace KitchenGateway;

/// <summary>Persistierbare, geheimnisfreie Konfiguration. Änderungen werden erst nach Neustart aktiv.</summary>
/// <remarks>Die Identität gehört zur Geräteinstanz, nicht zu Anzeigename, Namespace-Index oder API-Adresse.</remarks>
public sealed record GatewayConfig
{
    public int SchemaVersion { get; init; } = 1;
    public Guid GatewayId { get; init; } = Guid.NewGuid();
    /// <summary>Eigener Instanz-/Diagnosenamensraum; HKI und DI bleiben unveränderte offizielle Namespaces.</summary>
    public string NamespaceUri { get; init; } = GatewayIdentity.DefaultNamespaceUri;
    /// <summary>Installationsidentität im OPC-UA-Zertifikat. Null erzeugt eine neutrale URI aus der dauerhaften GatewayId.</summary>
    public string? ApplicationUri { get; init; }
    [JsonIgnore]
    public string EffectiveApplicationUri => ApplicationUri ?? $"urn:kitchen-gateway:application:{GatewayId:D}";
    /// <summary>Softwareanbieter des Gateways, unabhängig vom Hersteller der angebundenen Küchengeräte.</summary>
    public string ManufacturerName { get; init; } = "Kitchen Gateway Project";
    public string ProductName { get; init; } = "ConnectedKitchen HKI Gateway";
    public bool Demo { get; init; }
    public string ApiBaseUrl { get; init; } = "https://connectedkitchen-eu.cloud";
    public string? ApiCaFile { get; init; }
    public int InventoryIntervalSeconds { get; init; } = 300;
    public int ActualIntervalSeconds { get; init; } = 10;
    public int SetIntervalSeconds { get; init; } = 30;
    public int StaleAfterSeconds { get; init; } = 120;
    public int HistoryWindowSeconds { get; init; } = 3600;
    public int HttpTimeoutSeconds { get; init; } = 15;
    public int MaxParallelRequests { get; init; } = 4;
    public string UaBindAddress { get; init; } = "127.0.0.1";
    public int UaPort { get; init; } = 4840;
    public string UaEndpointUrl { get; init; } = "opc.tcp://localhost:4840/kitchen";
    public bool AllowInsecureUaForDemo { get; init; }
    public List<DeviceConfig> Devices { get; init; } = [];
}

/// <summary>Explizite fachliche Profilwahl. Numerische API-Typen lösen niemals eine automatische Wahl aus.</summary>
public sealed record DeviceConfig
{
    public Guid Identity { get; init; } = Guid.NewGuid();
    public int ApiId { get; init; }
    public string Name { get; init; } = "";
    public string Profile { get; init; } = "CookingZone";
    public bool Enabled { get; init; }
    public string Evidence { get; init; } = "";
    public Dictionary<string, JsonElement> StaticValues { get; init; } = [];
    public Dictionary<string, ValueRange> Ranges { get; init; } = [];
    public List<ZoneConfig> Zones { get; init; } = [];
}

/// <summary>Ein Becken/eine Zone mit belegter API-Zonennummer; HKI-Namenssuffix und Identität bleiben stabil.</summary>
public sealed record ZoneConfig
{
    public int Id { get; init; }
    public Dictionary<string, JsonElement> StaticValues { get; init; } = [];
    public Dictionary<string, ValueRange> Ranges { get; init; } = [];
    public List<FieldBinding> Bindings { get; init; } = [];
    public List<string> DeviceUnsupported { get; init; } = [];
}

/// <summary>Eine freizugebende Feldzuordnung. Einheit und Beleg sind Pflicht, da das OpenAPI Schema keine Einheiten definiert.</summary>
public sealed record FieldBinding(string Target, string Group, string Field, string Unit, string Evidence);
public sealed record ValueRange(double Low, double High);

/// <summary>Zentrale JSON-Konvention für Dateien, API und Export; Secrets sind eigene Dateitypen.</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value);
}

/// <summary>Validierung vor Persistenz und Start. Insbesondere werden dynamische Scheindaten und fremde API-Felder abgewiesen.</summary>
public static class ConfigValidation
{
    public static List<string> Validate(GatewayConfig c, ModelCatalog catalog)
    {
        var errors = new List<string>();
        if (c.Devices == null || c.Devices.Any(d => d == null || d.StaticValues == null || d.Ranges == null ||
            d.Zones == null || d.Zones.Any(z => z == null || z.StaticValues == null || z.Ranges == null ||
                z.Bindings == null || z.DeviceUnsupported == null || z.Bindings.Any(b => b == null))))
            return ["Geräte, Zonen und Zuordnungslisten dürfen nicht null sein."];
        if (c.SchemaVersion != 1 || c.GatewayId == Guid.Empty) errors.Add("SchemaVersion oder GatewayId ungültig.");
        if (!GatewayIdentity.ValidUri(c.NamespaceUri) ||
            new[] { Opc.Ua.Namespaces.OpcUa, ModelCatalog.Di, ModelCatalog.Hki }.Contains(c.NamespaceUri))
            errors.Add("Eigener Namespace muss eine gültige absolute URI außerhalb der offiziellen OPC-UA-/DI-/HKI-Namensräume sein.");
        if (!GatewayIdentity.ValidUri(c.EffectiveApplicationUri)) errors.Add("ApplicationUri muss eine gültige absolute URI sein.");
        if (string.IsNullOrWhiteSpace(c.ManufacturerName) || c.ManufacturerName.Length > 160 ||
            string.IsNullOrWhiteSpace(c.ProductName) || c.ProductName.Length > 160)
            errors.Add("Softwareanbieter und Produktname benötigen 1–160 Zeichen.");
        if (!string.IsNullOrWhiteSpace(c.ApiCaFile))
        {
            try { using var ca = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(c.ApiCaFile); }
            catch { errors.Add("Die konfigurierte lokale CA-Datei ist nicht als öffentliches Zertifikat lesbar."); }
        }
        if (c.Devices.Any(d => d.Profile == null || !Profiles.All.ContainsKey(d.Profile)))
            errors.Add("Auch deaktivierte Geräte benötigen einen bekannten Profilnamen.");
        if (!Uri.TryCreate(c.ApiBaseUrl, UriKind.Absolute, out var api) || api.Scheme is not ("https" or "http") ||
            !string.IsNullOrEmpty(api.UserInfo) || api.Query != "" || api.Fragment != "")
            errors.Add("API-Adresse muss eine HTTP(S)-URL ohne Zugangsdaten, Query oder Fragment sein.");
        else if (api.Scheme == "http" && !c.Demo && !api.IsLoopback)
            errors.Add("Produktive API-Verbindungen benötigen HTTPS (lokales Loopback ausgenommen).");
        if (c.AllowInsecureUaForDemo && !c.Demo) errors.Add("Unsicheres OPC UA ist ausschließlich im Demo-Modus erlaubt.");
        if (string.IsNullOrWhiteSpace(c.UaBindAddress) || Uri.CheckHostName(c.UaBindAddress) == UriHostNameType.Unknown ||
            c.UaPort is < 1024 or > 65535) errors.Add("OPC-UA-Bind-Adresse/Port ungültig.");
        if (!Uri.TryCreate(c.UaEndpointUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "opc.tcp" ||
            endpoint.Port != c.UaPort || !string.IsNullOrEmpty(endpoint.UserInfo) || endpoint.AbsolutePath != "/kitchen" ||
            endpoint.Query != "" || endpoint.Fragment != "" || endpoint.Host is "0.0.0.0" or "[::]")
            errors.Add("Angekündigter OPC-UA-Endpoint/Port ungültig; erreichbaren Host und Pfad /kitchen verwenden.");
        if (c.MaxParallelRequests is < 1 or > 16 || c.HttpTimeoutSeconds is < 1 or > 120 ||
            c.ActualIntervalSeconds is < 1 or > 86400 || c.SetIntervalSeconds is < 1 or > 86400 ||
            c.InventoryIntervalSeconds is < 1 or > 86400 || c.StaleAfterSeconds is < 1 or > 604800 ||
            c.HistoryWindowSeconds < c.StaleAfterSeconds || c.HistoryWindowSeconds > 604800)
            errors.Add("Intervalle, Veraltungsgrenze, Zeitfenster oder Parallelität außerhalb der zulässigen Grenzen.");
        if (c.Devices.Count > 100 || c.Devices.Select(d => d.Identity).Distinct().Count() != c.Devices.Count ||
            c.Devices.Select(d => d.ApiId).Distinct().Count() != c.Devices.Count)
            errors.Add("Maximal 100 eindeutige Geräteidentitäten/API-IDs.");
        foreach (var d in c.Devices.Where(d => d.Enabled))
        {
            var label = $"Gerät {d.ApiId}";
            if (d.Identity == Guid.Empty || d.ApiId < 0 || string.IsNullOrWhiteSpace(d.Evidence) || string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 200)
                errors.Add($"{label}: stabile Identität und Beleg für die administrative Profilwahl erforderlich.");
            if (!Profiles.All.TryGetValue(d.Profile, out var profile)) { errors.Add($"{label}: unbekanntes Profil."); continue; }
            foreach (var name in d.StaticValues.Keys)
                if (!catalog.Declarations(profile.DeviceType).TryGetValue(name, out var definition) || !definition.IsVariable || Mapping.IsDynamic(name))
                    errors.Add($"{label}: unbekanntes oder dynamisches Stammdatum {name}.");
            if (d.Profile == "Fryer" && !c.Demo)
                errors.Add($"{label}: Fritteuse produktiv gesperrt: API liefert keine belegbaren Pflichtwerte ProgramMode/SignalMode.");
            if (d.Zones.Count is < 1 or > 32 || d.Zones.Any(z => z.Id < 0) ||
                d.Zones.Select(z => z.Id).Distinct().Count() != d.Zones.Count)
                errors.Add($"{label}: mindestens eine und höchstens 32 eindeutige API-Zonen erforderlich.");
            ValidateStatic(d.StaticValues, catalog.MandatoryVariables(profile.DeviceType), c.Demo, label, errors);
            foreach (var range in d.Ranges.Concat(d.Zones.SelectMany(z => z.Ranges)))
                if (range.Value == null || !double.IsFinite(range.Value.Low) || !double.IsFinite(range.Value.High) || range.Value.Low >= range.Value.High)
                    errors.Add($"{label}: ungültiger EURange für {range.Key}.");
            foreach (var z in d.Zones)
            {
                ValidateStatic(z.StaticValues, catalog.MandatoryVariables(profile.ZoneType), c.Demo, label, errors);
                if (z.Bindings.Select(b => b.Target).Distinct().Count() != z.Bindings.Count)
                    errors.Add($"{label}: doppelte Zielzuordnung.");
                var declarations = catalog.Declarations(profile.ZoneType);
                foreach (var b in z.Bindings)
                {
                    var declaration = string.IsNullOrWhiteSpace(b.Target) ? null : catalog.ResolveDeclaration(profile.ZoneType, b.Target);
                    if (string.IsNullOrWhiteSpace(b.Target) || string.IsNullOrWhiteSpace(b.Group) || string.IsNullOrWhiteSpace(b.Field) ||
                        declaration == null || !declaration.IsVariable ||
                        !Mapping.KnownFields.TryGetValue(b.Group, out var fields) || !fields.Contains(b.Field) ||
                        string.IsNullOrWhiteSpace(b.Evidence) || !Mapping.ValidUnit(declaration, b.Unit) ||
                        z.StaticValues.ContainsKey(b.Target) || z.DeviceUnsupported.Contains(b.Target))
                        errors.Add($"{label}/{z.Id}: unzulässige, unbelegte oder doppelte Zuordnung {b.Target}.");
                    else if (catalog.IsAnalog(declaration) && !z.Ranges.ContainsKey(b.Target))
                        errors.Add($"{label}/{z.Id}: gerätespezifischer EURange für {b.Target} fehlt.");
                }
                foreach (var name in z.StaticValues.Keys)
                    if (!declarations.TryGetValue(name, out var n) || Mapping.IsDynamic(name))
                        errors.Add($"{label}/{z.Id}: {name} darf kein statischer Mess-/Zustandswert sein.");
            }
            if (!c.Demo)
            {
                foreach (var n in catalog.MandatoryVariables(profile.DeviceType).Where(catalog.IsAnalog))
                    if (!d.Ranges.ContainsKey(n.Name)) errors.Add($"{label}: EURange für {n.Name} fehlt.");
                foreach (var z in d.Zones)
                    foreach (var n in catalog.MandatoryVariables(profile.ZoneType).Where(catalog.IsAnalog))
                        if (!z.Ranges.ContainsKey(n.Name)) errors.Add($"{label}/{z.Id}: EURange für {n.Name} fehlt.");
            }
        }
        return errors;
    }

    private static void ValidateStatic(Dictionary<string, JsonElement> values, IEnumerable<ModelNode> mandatory,
        bool demo, string label, List<string> errors)
    {
        foreach (var n in mandatory)
        {
            if (n.Name == "DeviceClass" || Mapping.IsDynamic(n.Name)) continue;
            if (!values.TryGetValue(n.Name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            { if (!demo) errors.Add($"{label}: Pflicht-Stammdatum {n.Name} fehlt."); continue; }
            try
            {
                var converted = Mapping.ConvertScalar(value, n.DataTypeName);
                if (converted is string s && string.IsNullOrWhiteSpace(s)) throw new FormatException();
                if (converted is Opc.Ua.LocalizedText t && string.IsNullOrWhiteSpace(t.Text)) throw new FormatException();
                if (n.Name == "EnergySource" && (int)converted! is < 0 or > 2) throw new FormatException();
                if (n.Name == "NumberOfPhases" && (int)converted! is < 1 or > 3) throw new FormatException();
            }
            catch { errors.Add($"{label}: Stammdatum {n.Name} hat einen unzulässigen Typ/Wert."); }
        }
    }
}

/// <summary>Die drei Profile teilen Infrastruktur, nicht ungeprüfte Gerätezustände. Neue Klassen benötigen ein eigenes Fachreview.</summary>
public sealed record Profile(string DeviceType, string ZoneType, string ZonePrefix, string DeviceClass);
public static class Profiles
{
    public static readonly IReadOnlyDictionary<string, Profile> All = new Dictionary<string, Profile>
    {
        ["CookingZone"] = new("CookingZoneDeviceType", "CookingZoneParameterType", "CookingZone_", "Cooking Zone"),
        ["Fryer"] = new("FryerDeviceType", "FryerParameterType", "FryerCup_", "Fryer"),
        ["Grill"] = new("FryingAndGrillingDeviceType", "FryingAndGrillingParameterType", "GrillingZone_", "Frying And Grilling Appliance")
    };
}
