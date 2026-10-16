using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Opc.Ua;

namespace KitchenGateway;

/// <summary>Einziger REST-Zugang: GET, fester Ursprung, keine Redirects, begrenzte Parallelität und verifiziertes TLS.</summary>
public sealed class ConnectedKitchenClient : IDisposable
{
    private readonly GatewayConfig config;
    private readonly GatewayStore store;
    private readonly HttpClient http;
    private readonly SemaphoreSlim slots;
    private readonly X509Certificate2? rootCa;
    private long blockedUntil;
    public ConnectedKitchenClient(GatewayConfig config, GatewayStore store)
    {
        this.config = config; this.store = store;
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (!string.IsNullOrWhiteSpace(config.ApiCaFile))
        {
            rootCa = X509CertificateLoader.LoadCertificateFromFile(config.ApiCaFile);
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate == null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(rootCa);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Lokales CA-Profil ohne externe CRL-Dienste; CA-Betreiber muss Rotation organisieren.
                return chain.Build(certificate);
            };
        }
        http = new HttpClient(handler) { BaseAddress = new Uri(config.ApiBaseUrl.TrimEnd('/') + "/"), Timeout = Timeout.InfiniteTimeSpan };
        slots = new SemaphoreSlim(config.MaxParallelRequests);
    }
    public async Task<JsonElement> GetAsync(string path, CancellationToken ct)
    {
        var delay = new DateTime(Interlocked.Read(ref blockedUntil), DateTimeKind.Utc) - DateTime.UtcNow;
        if (delay > TimeSpan.Zero) throw new SourceFailure("429: gemeinsame Retry-After-Sperre.", StatusCodes.BadServerTooBusy, delay);
        var apiKey = store.ReadApiKey();
        if (apiKey == null) throw new SourceFailure("API-Key ist nicht eingerichtet.", StatusCodes.BadUserAccessDenied, TimeSpan.FromSeconds(60));
        await slots.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(config.HttpTimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("api-key", apiKey);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var wait = response.Headers.RetryAfter?.Delta ??
                    (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.Zero);
                if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                if (wait > TimeSpan.FromDays(1)) wait = TimeSpan.FromDays(1);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    Interlocked.Exchange(ref blockedUntil, DateTime.UtcNow.Add(wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(30)).Ticks);
                throw new SourceFailure($"API antwortet HTTP {(int)response.StatusCode}.",
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? StatusCodes.BadUserAccessDenied :
                    response.StatusCode == HttpStatusCode.NotFound ? StatusCodes.BadNotFound :
                    response.StatusCode == HttpStatusCode.TooManyRequests ? StatusCodes.BadServerTooBusy : StatusCodes.BadNoCommunication,
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? TimeSpan.FromSeconds(60) : wait);
            }
            if (response.Content.Headers.ContentLength > 16 * 1024 * 1024)
                throw new SourceFailure("API-Antwort überschreitet 16 MiB.", StatusCodes.BadEncodingLimitsExceeded);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            // Auch chunked-Antworten sind begrenzt; ein Fremdserver darf den Speicher nicht unbeschränkt füllen.
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, timeout.Token)) > 0)
            {
                if (buffer.Length + read > 16 * 1024 * 1024) throw new SourceFailure("API-Antwort zu groß.", StatusCodes.BadEncodingLimitsExceeded);
                buffer.Write(bytes, 0, read);
            }
            buffer.Position = 0;
            using var document = await JsonDocument.ParseAsync(buffer, cancellationToken: timeout.Token);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new SourceFailure("API-Antwort muss eine Datenreihe sein.", StatusCodes.BadDecodingError);
            return document.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new SourceFailure("HTTP-Zeitüberschreitung.", StatusCodes.BadTimeout); }
        catch (HttpRequestException) { throw new SourceFailure("Verbindungs- oder TLS-Fehler; Zertifikatsvertrauen prüfen.", StatusCodes.BadNoCommunication); }
        catch (JsonException) { throw new SourceFailure("API liefert ungültiges JSON.", StatusCodes.BadDecodingError); }
        finally { slots.Release(); }
    }
    public void Dispose() { http.Dispose(); slots.Dispose(); rootCa?.Dispose(); }
}

/// <summary>Bereinigter Fehler ohne Response-Body, Header oder Schlüssel. RetryAfter steuert den Scheduler.</summary>
public sealed class SourceFailure(string message, uint status, TimeSpan retryAfter = default) : Exception(message)
{
    public uint Status { get; } = status;
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>Asynchroner Poller mit unabhängigem Backoff je Gerät/Zone/Datengruppe und einem globalen HTTP-Limit.</summary>
/// <remarks>Historische actualValues/setValues liefern höchstens den neuesten Eintrag im konfigurierten Fenster.
/// Es gibt keine dokumentierte Garantie, dass das ein aktueller Livewert ist. Die Messzeit entscheidet über Qualität.</remarks>
public sealed class PollingService(GatewayConfig config, GatewayCache cache, ConnectedKitchenClient client, ModelCatalog catalog) : BackgroundService
{
    private sealed class Job(string key, int interval, DeviceConfig? device = null, ZoneConfig? zone = null, string? group = null)
    {
        public string Key = key;
        public int Interval = interval;
        public DeviceConfig? Device = device;
        public ZoneConfig? Zone = zone;
        public string? Group = group;
        public DateTime Next;
        public int Failures;
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> present = new();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jobs = new List<Job> { new("inventory", config.InventoryIntervalSeconds) };
        foreach (var d in config.Devices.Where(d => d.Enabled))
            foreach (var z in d.Zones)
                foreach (var group in z.Bindings.Select(b => b.Group).Distinct())
                    jobs.Add(new($"{d.ApiId}/{z.Id}/{group}", group == "Actual" ? config.ActualIntervalSeconds : config.SetIntervalSeconds, d, z, group));
        // Je Job eine unabhängige asynchrone Schleife: ein langsames Gerät hält nicht
        // den gesamten Scheduler an. Nur die explizite globale HTTP-Parallelität begrenzt gemeinsame Ressourcen.
        await Task.WhenAll(jobs.Select(j => Loop(j, stoppingToken)));
    }
    private async Task Loop(Job job, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Run(job, stoppingToken);
            var delay = job.Next - DateTime.UtcNow;
            try { await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
    private async Task Run(Job job, CancellationToken ct)
    {
        try
        {
            if (job.Device == null)
            {
                var rows = await client.GetAsync("api/devices", ct);
                var ids = new HashSet<int>();
                foreach (var row in rows.EnumerateArray())
                    if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("id", out var id) && id.TryGetInt32(out var value)) ids.Add(value);
                foreach (var d in config.Devices.Where(d => d.Enabled)) present[d.ApiId] = ids.Contains(d.ApiId);
                cache.Source(job.Key, true, $"{ids.Count} Geräte im Inventar; Profilzuordnung ausschließlich administrativ.");
            }
            else
            {
                var now = DateTime.UtcNow;
                var from = Uri.EscapeDataString(now.AddSeconds(-config.HistoryWindowSeconds).ToString("O"));
                var to = Uri.EscapeDataString(now.ToString("O"));
                var rows = await client.GetAsync($"api/devices/reporting/haccp/{(job.Group == "Actual" ? "actualValues" : "setValues")}?deviceId={job.Device.ApiId}&zone={job.Zone!.Id}&from={from}&to={to}", ct);
                var fetchedAt = DateTime.UtcNow;
                var bindings = job.Zone.Bindings.Where(b => b.Group == job.Group).ToArray();
                if (present.TryGetValue(job.Device.ApiId, out var found) && !found)
                {
                    foreach (var b in bindings) cache.Fail(GatewayCache.Key(job.Device.Identity, job.Zone.Id, b.Target), StatusCodes.BadNotConnected, "Gerät fehlt im API-Inventar.");
                }
                else if (rows.GetArrayLength() == 0)
                {
                    foreach (var b in bindings) cache.Fail(GatewayCache.Key(job.Device.Identity, job.Zone.Id, b.Target), StatusCodes.BadNoData, "Leere historische Datenreihe; kein aktueller Messwert belegbar.");
                }
                else
                {
                    var row = rows.EnumerateArray().OrderByDescending(r =>
                        r.ValueKind == JsonValueKind.Object && r.TryGetProperty("date", out var date) &&
                        date.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(date.GetString(), out var stamp) ? stamp.UtcDateTime : DateTime.MaxValue).First();
                    foreach (var b in bindings)
                    {
                        var key = GatewayCache.Key(job.Device.Identity, job.Zone.Id, b.Target);
                        cache.Set(key, row.ValueKind != JsonValueKind.Object ? Sample.Missing(StatusCodes.BadDecodingError, "Ungültiger Datensatz.") :
                            Mapping.Translate(row, b, catalog.ResolveDeclaration(Profiles.All[job.Device.Profile].ZoneType, b.Target)!, fetchedAt));
                    }
                }
                cache.Source(job.Key, true, "Historische Zeitreihe abgerufen; Messzeit und Qualität separat prüfen.");
            }
            job.Failures = 0;
            job.Next = DateTime.UtcNow.AddSeconds(job.Interval);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e)
        {
            var failure = e as SourceFailure ?? new SourceFailure("Ungültige API-Struktur oder interner Abruffehler.", StatusCodes.BadDecodingError);
            cache.Source(job.Key, false, failure.Message);
            if (job.Device != null)
                foreach (var b in job.Zone!.Bindings.Where(b => b.Group == job.Group))
                    cache.Fail(GatewayCache.Key(job.Device.Identity, job.Zone.Id, b.Target), failure.Status, failure.Message);
            job.Failures = Math.Min(job.Failures + 1, 8);
            var seconds = Math.Max(Math.Min(300, job.Interval * Math.Pow(2, job.Failures - 1)), failure.RetryAfter.TotalSeconds);
            job.Next = DateTime.UtcNow.AddSeconds(Math.Clamp(seconds, 1, 86400));
        }
    }
}
