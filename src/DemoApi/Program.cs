using System.Security.Cryptography;
using System.Text.Json;

if (args.SequenceEqual(new[] { "--health-check" }))
{
    try
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        using var response = await http.GetAsync("http://127.0.0.1:5090/health/live");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch { Environment.ExitCode = 1; }
    return;
}
// Dokumentierte API-Form, ausschließlich synthetische Daten. Dieser Prozess kennt keine reale Geräteadresse.
// Der zufällige Simulationsschlüssel wird zur Laufzeit erzeugt und niemals im Repository oder Log abgelegt.
var builder = WebApplication.CreateBuilder(args);
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") == null) builder.WebHost.UseUrls("http://127.0.0.1:5090");
var app = builder.Build();
var keyFile = Path.GetFullPath(Environment.GetEnvironmentVariable("DEMO_KEY_FILE") ?? ".state/demo-api.key");
Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
if (!File.Exists(keyFile))
{
    File.WriteAllText(keyFile, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}
var key = File.ReadAllText(keyFile);
var sync = new object();
Scenario scenario = new();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/health/live")
    { await context.Response.WriteAsJsonAsync(new { process = "running", simulation = true }); return; }
    var supplied = context.Request.Headers["api-key"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied), System.Text.Encoding.UTF8.GetBytes(key)))
    { context.Response.StatusCode = 401; return; }
    Scenario current; lock (sync) current = scenario;
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        if (current.SlowMs > 0) await Task.Delay(Math.Clamp(current.SlowMs, 0, 20000), context.RequestAborted);
        var status = current.Status;
        if (int.TryParse(context.Request.Query["deviceId"], out var id) && current.FailedDevice == id) status = 503;
        if (status != 200)
        {
            context.Response.StatusCode = status;
            if (status == 429) context.Response.Headers.RetryAfter = current.RetryAfterSeconds.ToString();
            return;
        }
        if (current.InvalidJson)
        { context.Response.ContentType = "application/json"; await context.Response.WriteAsync("{invalid"); return; }
    }
    await next();
});
app.MapGet("/api/devices", () =>
{
    Scenario current; lock (sync) current = scenario;
    return Results.Json(new[] { 101, 102, 103 }.Where(id => current.MissingDevice != id).Select(id => new
    {
        id, name = "SIMULATION " + id, serialNumber = "DEMO-" + id, model = "DEMO", type = -999,
        energySource = -999, firmwareVersion = "DEMO", revision = "DEMO", active = true, available = true,
        connectState = true, error = false, regionId = 0, location = "Simulationslabor", address = "Keine reale Adresse",
        storeNr = "DEMO", position = "DEMO", cleaningStatus = false, cleaningInterval = "DEMO",
        latestFirmwareVersion = "DEMO", firmwareUpdateAvailable = false, firmwareUpdatePending = false,
        generation = 0, size = 0, operatingTotal = 0, operatingDaily = 0, uploadCookbooks = false,
        isWithSingleEnergySource = true, isWithWaterSupply = false, devicePowerModel = false, isNewCookbookJson = false,
        devicePosition = 0
    }));
});
app.MapGet("/api/devices/reporting/haccp/{kind}", (string kind, HttpRequest request) =>
{
    if (kind is not ("actualValues" or "setValues")) return Results.NotFound();
    if (!int.TryParse(request.Query["deviceId"], out var id) || !int.TryParse(request.Query["zone"], out var zone) ||
        !DateTimeOffset.TryParse(request.Query["from"], out var from) || !DateTimeOffset.TryParse(request.Query["to"], out var to))
        return Results.BadRequest();
    if (id is not (101 or 102 or 103) || zone < 0 || zone > (id == 101 ? 1 : 0)) return Results.Json(Array.Empty<object>());
    Scenario current; lock (sync) current = scenario;
    // Die API-Anfrage enthält bereits ihr "to". Ein Messpunkt darf nicht erst nach diesem Zeitpunkt entstehen.
    var stamp = current.FixedTimestamp ?? DateTimeOffset.UtcNow.AddSeconds(-1 - current.AgeSeconds);
    if (current.Empty || stamp < from || stamp > to) return Results.Json(Array.Empty<object>());
    var row = kind == "actualValues" ? new Dictionary<string, object?>
    {
        ["date"] = stamp.ToString("O"), ["actualTemperature"] = 170 + DateTime.UtcNow.Second % 10 + zone,
        ["temperature1"] = 171, ["coreTemperature"] = 72, ["bottomTemperature"] = 175, ["cupTemperature1"] = 173,
        ["timeRemaining"] = 90, ["actualDisplayedValues"] = 172
    } : new Dictionary<string, object?>
    {
        ["date"] = stamp.ToString("O"), ["setTemperatureValues"] = 180, ["setDisplayedValues"] = 180,
        ["setCoreTemperature"] = 75, ["setProcessTime"] = 300, ["programName"] = "SIMULATION",
        ["programUId"] = "11111111-1111-1111-1111-111111111111", ["programId"] = 1,
        ["setCookingStepEndCondition"] = 0, ["setCookingStepEndCondition1"] = 0, ["setCookingStepEndCondition2"] = 0,
        ["setCookingStepEndCondition3"] = 0, ["setCookingStepEndCondition4"] = 0,
        ["setPowerLevelMax"] = 100, ["nominalPower"] = 3000, ["demoMode"] = true
    };
    if (current.MissingField != null) row.Remove(current.MissingField);
    if (current.InvalidField != null) row[current.InvalidField] = "INVALID";
    return Results.Json(new[] { row });
});
// Separater Simulator-Steuerpfad; gehört NICHT zum ConnectedKitchen-Vertrag und wird vom Gateway nie verwendet.
app.MapPost("/__demo/scenario", (Scenario next) => { lock (sync) scenario = next; return Results.Ok(new { simulation = true }); });
app.MapGet("/__demo/scenario", () => { lock (sync) return Results.Json(scenario); });
app.Run();

/// <summary>Fehlerinjektion für lokale Abnahme; keine Aktionsweiterleitung zu physischen Geräten möglich.</summary>
public sealed record Scenario(int Status = 200, int AgeSeconds = 0, bool Empty = false, string? MissingField = null,
    string? InvalidField = null, int? MissingDevice = null, int? FailedDevice = null, bool InvalidJson = false,
    int RetryAfterSeconds = 2, int SlowMs = 0, DateTimeOffset? FixedTimestamp = null);
