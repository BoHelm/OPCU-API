using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KitchenGateway;

/// <summary>PBKDF2 mit individuellem Salt; Vergleiche erfolgen zeitkonstant. Keine Passwörter im Konfigurationsexport.</summary>
public static class Passwords
{
    public static string Hash(string password)
    {
        if (password == null || password.Length is < 12 or > 256) throw new ArgumentException("Passwort muss 12–256 Zeichen haben.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return $"210000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split(':');
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[1]), int.Parse(parts[0]), HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }
}

/// <summary>API-Schlüssel ist AES-GCM-verschlüsselt. Master-Key und Web-Schlüssel liegen im getrennten Key-Verzeichnis.</summary>
/// <remarks>Der Containerbetreiber muss Daten- und Key-Volume getrennt sichern und schützen; Verschlüsselung ersetzt keine Dateirechte.</remarks>
public sealed class GatewayStore
{
    public string DataDirectory { get; }
    public string KeyDirectory { get; }
    private readonly byte[] key;
    private readonly object sync = new();
    public GatewayStore(string dataDirectory, string keyDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        KeyDirectory = Path.GetFullPath(keyDirectory);
        if (KeyDirectory.StartsWith(DataDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal) || KeyDirectory == DataDirectory)
            throw new InvalidOperationException("Key-Verzeichnis muss außerhalb des Datenverzeichnisses liegen.");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(KeyDirectory);
        RestrictDirectory(DataDirectory); RestrictDirectory(KeyDirectory);
        var keyFile = Path.Combine(KeyDirectory, "master.key");
        if (!File.Exists(keyFile))
        {
            if (File.Exists(Path.Combine(DataDirectory, "api-key.enc")))
                throw new InvalidOperationException("Master-Key fehlt zu bestehenden verschlüsselten Daten. Backup wiederherstellen.");
            AtomicWrite(keyFile, RandomNumberGenerator.GetBytes(32));
        }
        key = File.ReadAllBytes(keyFile);
        if (key.Length != 32) throw new InvalidOperationException("Master-Key muss 32 Bytes enthalten.");
    }
    public bool ApiKeyConfigured => File.Exists(Path.Combine(DataDirectory, "api-key.enc"));
    public void SetApiKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("Ungültiger API-Key.");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(value);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, data, cipher, tag);
        lock (sync) AtomicWrite(Path.Combine(DataDirectory, "api-key.enc"), nonce.Concat(tag).Concat(cipher).ToArray());
        CryptographicOperations.ZeroMemory(data);
    }
    /// <summary>Nur der HTTP-Adapter darf den Klartext kurzzeitig für den vorgesehenen api-key-Header abrufen.</summary>
    public string? ReadApiKey()
    {
        lock (sync)
        {
            var path = Path.Combine(DataDirectory, "api-key.enc");
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            var plain = new byte[bytes.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
            var value = Encoding.UTF8.GetString(plain); CryptographicOperations.ZeroMemory(plain); return value;
        }
    }
    public T? Read<T>(string name)
    {
        lock (sync)
        {
            var path = Path.Combine(DataDirectory, name);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.Options) : default;
        }
    }
    public void Save<T>(string name, T value)
    {
        lock (sync) AtomicWrite(Path.Combine(DataDirectory, name), JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options));
    }
    public string BootstrapToken()
    {
        var path = Path.Combine(KeyDirectory, "bootstrap-token");
        lock (sync)
        {
            if (!File.Exists(path)) AtomicWrite(path, Encoding.UTF8.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
            return File.ReadAllText(path);
        }
    }
    public void ClearBootstrap() => File.Delete(Path.Combine(KeyDirectory, "bootstrap-token"));
    public static void AtomicWrite(string path, byte[] data)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temporary, data);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, true);
    }
    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
public sealed record WebAdmin(string Username, string PasswordHash);
public sealed record UaAccount(string Username, string PasswordHash, string Role);

/// <summary>Vorbereiteter Rückkanal mit zentraler Freigabe. Kein HKI-Basisaufruf ist durch diese API belegt.</summary>
public sealed class WriteDispatcher
{
    public uint Authorize(string operation, string role) => Opc.Ua.StatusCodes.BadNotSupported;
    // Erst nach API-Vertragserweiterung: Typ/Grenze/Fähigkeit/Rolle prüfen, persistente Command-ID,
    // genau einmal absenden, Annahme und bestätigte Geräteausführung als getrennte Zustände verfolgen.
    // Es gibt absichtlich keinen spekulativen HTTP-POST oder automatischen Write-Retry.
}
