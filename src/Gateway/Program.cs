using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using KitchenGateway;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

if (args.SequenceEqual(new[] { "--health-check" }))
{
    try
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        using var response = await http.GetAsync(Environment.GetEnvironmentVariable("GATEWAY_HEALTH_URL") ?? "http://127.0.0.1:8080/health/ready");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch { Environment.ExitCode = 1; }
    return;
}
// Composition Root: Die aktive Konfiguration ist ein unveränderlicher Snapshot.
// Web-Änderungen werden atomar gespeichert und explizit als "Neustart erforderlich" bezeichnet.
var builder = WebApplication.CreateBuilder(args);
if (Environment.GetEnvironmentVariable("GATEWAY_WEB_CERT_PASSWORD_FILE") is { } webPasswordFile)
    builder.Configuration["Kestrel:Certificates:Default:Password"] = File.ReadAllText(webPasswordFile).TrimEnd('\r', '\n');
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2 * 1024 * 1024);
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") == null) builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonDefaults.Options.PropertyNamingPolicy;
    o.SerializerOptions.UnmappedMemberHandling = JsonDefaults.Options.UnmappedMemberHandling;
});
var dataDir = Environment.GetEnvironmentVariable("GATEWAY_DATA_DIR") ?? Path.Combine(builder.Environment.ContentRootPath, ".state");
var keyDir = Environment.GetEnvironmentVariable("GATEWAY_KEY_DIR") ?? Path.GetFullPath(dataDir) + "-keys";
var store = new GatewayStore(dataDir, keyDir);
var catalog = new ModelCatalog(Path.Combine(AppContext.BaseDirectory, "Models"));
var config = store.Read<GatewayConfig>("gateway.json");
if (config == null)
{
    var demo = Environment.GetEnvironmentVariable("GATEWAY_DEMO") == "true";
    config = demo ? DemoConfiguration.Create(Environment.GetEnvironmentVariable("GATEWAY_DEMO_API_URL") ?? "http://127.0.0.1:5090",
        Environment.GetEnvironmentVariable("GATEWAY_DEMO_INSECURE_UA") == "true") : new GatewayConfig();
    if (int.TryParse(Environment.GetEnvironmentVariable("GATEWAY_INITIAL_UA_PORT"), out var initialPort))
        config = config with { UaPort = initialPort, UaEndpointUrl = $"opc.tcp://localhost:{initialPort}/kitchen" };
    if (Environment.GetEnvironmentVariable("GATEWAY_INITIAL_UA_BIND") is { } initialBind)
        config = config with { UaBindAddress = initialBind };
    store.Save("gateway.json", config);
}
if (config.Demo && !store.ApiKeyConfigured && Environment.GetEnvironmentVariable("GATEWAY_DEMO_KEY_FILE") is { } demoKeyFile)
    store.SetApiKey(File.ReadAllText(demoKeyFile).Trim());
var errors = ConfigValidation.Validate(config, catalog);
if (errors.Count > 0) throw new InvalidOperationException("Gespeicherte Konfiguration ungültig: " + string.Join("; ", errors));
if (store.Read<WebAdmin>("web-admin.json") == null) _ = store.BootstrapToken();
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton<GatewayCache>();
builder.Services.AddSingleton<WriteDispatcher>();
builder.Services.AddSingleton<ConnectedKitchenClient>();
builder.Services.AddSingleton<UaService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UaService>());
builder.Services.AddHostedService<PollingService>();
builder.Services.AddDataProtection().SetApplicationName("ConnectedKitchenGateway")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(store.KeyDirectory, "web")));
builder.Services.AddAntiforgery(o =>
{ o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/login"; o.Cookie.Name = "KitchenGateway.Admin";
    o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401;
        else context.Response.Redirect("/login");
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("authentication", limits =>
{
    limits.PermitLimit = 10; limits.Window = TimeSpan.FromMinutes(1);
    limits.QueueLimit = 0; limits.AutoReplenishment = true;
}).RejectionStatusCode = 429);
var app = builder.Build();
var setupLock = new object();
app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    var remote = context.Connection.RemoteIpAddress;
    var local = remote != null && IPAddress.IsLoopback(remote);
    if (!context.Request.IsHttps && !local && Environment.GetEnvironmentVariable("GATEWAY_ALLOW_HTTP") != "true" &&
        !context.Request.Path.StartsWithSegments("/health"))
    { context.Response.StatusCode = 400; await context.Response.WriteAsync("HTTPS für Webadministration erforderlich."); return; }
    try { await next(); }
    catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsync("CSRF-Prüfung fehlgeschlagen."); }
    catch (Exception e) when (e is ArgumentException or JsonException or BadHttpRequestException or System.Security.Cryptography.CryptographicException)
    { context.Response.StatusCode = 400; await context.Response.WriteAsync("Ungültige Eingabe oder Zertifikat. Konfiguration prüfen."); }
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next();
});
app.MapGet("/health/live", () => Results.Ok(new { process = "running" }));
app.MapGet("/health/ready", (UaService ua) => Results.Json(new { ready = ua.Ready, message = ua.StartupError }, statusCode: ua.Ready ? 200 : 503));
app.MapGet("/health/source", (GatewayCache cache) => Results.Json(new
{
    reachable = cache.Health.Count > 0 && cache.Health.All(h => h.Value.Reachable)
}, statusCode: cache.Health.Count > 0 && cache.Health.All(h => h.Value.Reachable) ? 200 : 503));
app.MapGet("/login", (HttpContext context, IAntiforgery csrf) =>
    store.Read<WebAdmin>("web-admin.json") == null ? Results.Redirect("/setup") :
    Results.Content(LoginPage(false, csrf.GetAndStoreTokens(context).RequestToken!), "text/html"));
app.MapGet("/setup", (HttpContext context, IAntiforgery csrf) =>
    store.Read<WebAdmin>("web-admin.json") != null ? Results.Redirect("/login") :
    Results.Content(LoginPage(true, csrf.GetAndStoreTokens(context).RequestToken!), "text/html"));
app.MapPost("/setup", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    lock (setupLock)
    {
        if (store.Read<WebAdmin>("web-admin.json") != null) return Results.Conflict();
        var expected = Encoding.UTF8.GetBytes(store.BootstrapToken());
        if (!CryptographicOperations.FixedTimeEquals(expected, Encoding.UTF8.GetBytes(form["token"].ToString())))
            return Results.Unauthorized();
        var username = form["username"].ToString().Trim();
        if (username.Length is < 3 or > 64) return Results.BadRequest("Benutzername: 3–64 Zeichen.");
        store.Save("web-admin.json", new WebAdmin(username, Passwords.Hash(form["password"].ToString())));
        store.ClearBootstrap();
    }
    return Results.Redirect("/login");
}).RequireRateLimiting("authentication");
app.MapPost("/login", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    var admin = store.Read<WebAdmin>("web-admin.json");
    if (admin == null) return Results.Unauthorized();
    var correct = Passwords.Verify(form["password"].ToString(), admin.PasswordHash);
    if (!correct || form["username"] != admin.Username) return Results.Unauthorized();
    await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.Name, admin.Username), new Claim(ClaimTypes.Role, "Administrator")
    }, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Redirect("/");
}).RequireRateLimiting("authentication");
app.MapGet("/", () => Results.File(Path.Combine(app.Environment.WebRootPath, "index.html"), "text/html")).RequireAuthorization();
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/session", (HttpContext context, IAntiforgery csrf) =>
    Results.Ok(new { username = context.User.Identity!.Name, csrfToken = csrf.GetAndStoreTokens(context).RequestToken }));
api.MapPost("/logout", async (HttpContext context) => { await context.SignOutAsync(); return Results.Ok(); });
api.MapGet("/config", () => Results.Ok(new { configuration = store.Read<GatewayConfig>("gateway.json"),
    apiKeyConfigured = store.ApiKeyConfigured, restartRequired =
        JsonSerializer.Serialize(store.Read<GatewayConfig>("gateway.json"), JsonDefaults.Options) != JsonSerializer.Serialize(config, JsonDefaults.Options) }));
api.MapGet("/config/export", () => Results.File(JsonSerializer.SerializeToUtf8Bytes(store.Read<GatewayConfig>("gateway.json"), JsonDefaults.Options),
    "application/json", "gateway-configuration.json"));
api.MapPost("/config", ([FromBody] GatewayConfig next) =>
{
    var problems = ConfigValidation.Validate(next, catalog);
    if (next.GatewayId != config.GatewayId) problems.Add("GatewayId ist unveränderlich; Import muss die Identität dieser Installation erhalten.");
    var existing = store.Read<GatewayConfig>("gateway.json")!;
    foreach (var device in next.Devices)
        if (existing.Devices.Any(d => d.ApiId == device.ApiId && d.Identity != device.Identity))
            problems.Add("Bestehende Geräteidentität darf beim Import nicht geändert werden.");
    if (problems.Count > 0) return Results.BadRequest(new { errors = problems });
    store.Save("gateway.json", next);
    return Results.Ok(new { restartRequired = true });
});
api.MapPost("/secret", (SecretInput input) => { store.SetApiKey(input.Value); return Results.Ok(new { configured = true, effective = "sofort beim nächsten Abruf" }); });
api.MapPost("/connection-test", async (ConnectedKitchenClient client, CancellationToken ct) =>
{
    try { var list = await client.GetAsync("api/devices", ct); return Results.Ok(new { connected = true, deviceCount = list.GetArrayLength(), configuration = "aktive Konfiguration" }); }
    catch (SourceFailure e) { return Results.Json(new { connected = false, message = e.Message }, statusCode: 502); }
});
api.MapGet("/inventory", async (ConnectedKitchenClient client, CancellationToken ct) =>
{
    try { return Results.Json(await client.GetAsync("api/devices", ct)); }
    catch (SourceFailure e) { return Results.Json(new { message = e.Message }, statusCode: 502); }
});
api.MapGet("/model", () => Results.Ok(Profiles.All.ToDictionary(p => p.Key, p => new
{
    profile = p.Value,
    deviceFields = catalog.Declarations(p.Value.DeviceType).Values.Where(n => n.IsVariable).Select(n => new { n.Name, n.DataTypeName, n.Mandatory, dynamic = Mapping.IsDynamic(n.Name), unit = Mapping.Unit(n.Name) }),
    zoneFields = catalog.Declarations(p.Value.ZoneType).Values.Where(n => n.IsVariable).Select(n => new { n.Name, n.DataTypeName, n.Mandatory, dynamic = Mapping.IsDynamic(n.Name), unit = Mapping.Unit(n.Name) })
})));
api.MapGet("/diagnostics", (GatewayCache cache, UaService ua) => Results.Ok(new
{
    simulation = config.Demo, opcUaReady = ua.Ready, uaError = ua.StartupError,
    endpoint = config.UaEndpointUrl, source = cache.Health, samples = cache.Diagnostics(config.StaleAfterSeconds),
    nodes = ua.Nodes?.Diagnostics() ?? [], limitations = new[] {
        "Keine OPC-Zertifizierung und kein vollständiger HKI-Konformitätsnachweis.",
        "Keine belegten Geräte-Schreiboperationen, Zustands-/Signalwerte und aktiven Gerätealarme in dieser API.",
        "Fryer nur als unvollständige Simulation; produktiv gesperrt.",
        "HACCP-Historienparameter unbekannt (BadNoData); kein HistoryRead/Historian."
    }
}));
api.MapGet("/ua/users", () => Results.Ok((store.Read<List<UaAccount>>("ua-users.json") ?? []).Select(a => new { a.Username, a.Role })));
api.MapPost("/ua/users", (UaUserInput input) =>
{
    if (input.Role is not ("Observer" or "Operator" or "Administrator") || string.IsNullOrWhiteSpace(input.Username) || input.Username.Length is < 3 or > 64)
        return Results.BadRequest("Benutzername/Rolle ungültig.");
    var list = store.Read<List<UaAccount>>("ua-users.json") ?? [];
    list.RemoveAll(a => a.Username == input.Username);
    list.Add(new(input.Username, Passwords.Hash(input.Password), input.Role));
    store.Save("ua-users.json", list);
    return Results.Ok(new { effective = "sofort bei nächster OPC-UA-Anmeldung", note = "Alle Rollen bleiben im lesenden MVP ohne Geräte-Schreibrechte." });
});
api.MapDelete("/ua/users/{username}", (string username) =>
{
    var list = store.Read<List<UaAccount>>("ua-users.json") ?? [];
    list.RemoveAll(a => a.Username == username); store.Save("ua-users.json", list);
    return Results.Ok();
});
api.MapGet("/ua/certificate", async (UaService ua) =>
    await ua.ServerCertificateAsync() is { } certificate ? Results.File(certificate, "application/pkix-cert", "gateway.der") : Results.NotFound());
api.MapGet("/ua/trust", (UaService ua) =>
{
    object[] List(string name)
    {
        var path = Path.Combine(ua.Pki, name, "certs");
        return !Directory.Exists(path) ? [] : Directory.GetFiles(path).Select(p =>
        {
            using var c = X509CertificateLoader.LoadCertificateFromFile(p);
            return (object)new { thumbprint = c.Thumbprint, c.Subject, c.NotAfter };
        }).ToArray();
    }
    return Results.Ok(new { trusted = List("trusted"), rejected = List("rejected") });
});
api.MapPost("/ua/trust", async (CertificateInput input, UaService ua) =>
{
    if (string.IsNullOrWhiteSpace(input.DerBase64) || input.DerBase64.Length > 90000) return Results.BadRequest("Zertifikat fehlt oder ist zu groß.");
    using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(input.DerBase64));
    if (certificate.HasPrivateKey) return Results.BadRequest("Nur öffentliche DER-Zertifikate.");
    var dir = Path.Combine(ua.Pki, "trusted", "certs"); Directory.CreateDirectory(dir);
    GatewayStore.AtomicWrite(Path.Combine(dir, certificate.Thumbprint + ".der"), certificate.RawData);
    await ua.ReloadTrustAsync();
    return Results.Ok(new { certificate.Thumbprint, effective = "sofort" });
});
api.MapDelete("/ua/trust/{thumbprint}", async (string thumbprint, UaService ua) =>
{
    if (thumbprint.Length != 40 || !thumbprint.All(Uri.IsHexDigit)) return Results.BadRequest();
    var dir = Path.Combine(ua.Pki, "trusted", "certs");
    if (Directory.Exists(dir))
        foreach (var file in Directory.GetFiles(dir))
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(file);
            if (certificate.Thumbprint.Equals(thumbprint, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
        }
    await ua.ReloadTrustAsync(); return Results.Ok(new { note = "Neue Sitzungen verwenden aktualisiertes Vertrauen; vorhandene Sitzungen bei Sicherheitsvorfällen separat beenden." });
});
app.Run();

static string LoginPage(bool setup, string token)
{
    var title = setup ? "Ersteinrichtung" : "Administrativ anmelden";
    var extra = setup ? "<label>Einmaliger Einrichtungstoken aus dem getrennten Key-Verzeichnis<input name=\"token\" type=\"password\" required autocomplete=\"off\"></label><p>Passwort: mindestens 12 Zeichen. Der Token steht in der Datei bootstrap-token; er wird nicht über diese Website ausgegeben.</p>" : "";
    return "<!doctype html><html lang=\"de\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><link rel=\"stylesheet\" href=\"/style.css\"><title>" +
        title + "</title><main class=\"login\"><h1>ConnectedKitchen Gateway</h1><h2>" + title + "</h2><form method=\"post\" action=\"" +
        (setup ? "/setup" : "/login") + "\"><input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"" +
        HtmlEncoder.Default.Encode(token) + "\"><label>Benutzername<input name=\"username\" required minlength=\"3\" maxlength=\"64\" autocomplete=\"username\"></label>" +
        extra + "<label>Passwort<input type=\"password\" name=\"password\" required minlength=\"12\" maxlength=\"256\" autocomplete=\"" +
        (setup ? "new-password" : "current-password") + "\"></label><button>Fortfahren</button></form></main></html>";
}
public sealed record SecretInput(string Value);
public sealed record UaUserInput(string Username, string Password, string Role);
public sealed record CertificateInput(string DerBase64);
