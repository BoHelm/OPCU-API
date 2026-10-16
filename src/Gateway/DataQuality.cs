using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Opc.Ua;

namespace KitchenGateway;

/// <summary>Messwert und Herkunft. SourceTime ist Messzeit; FetchedAt ist Abrufzeit, niemals Ersatz-Messzeit.</summary>
public sealed record Sample(object? Value, uint Status, DateTime? SourceTime, DateTime FetchedAt, string Reason)
{
    public static Sample Missing(uint status, string reason) => new(null, status, null, DateTime.UtcNow, reason);
    /// <summary>Ein erfolgreich wiederholter Abruf verjüngt einen alten Messwert ausdrücklich nicht.</summary>
    public Sample At(DateTime now, int staleAfterSeconds)
    {
        if (Status == StatusCodes.Good && SourceTime is { } source && now - source > TimeSpan.FromSeconds(staleAfterSeconds))
            return this with { Status = StatusCodes.UncertainLastUsableValue, Reason = "Messzeit überschreitet Veraltungsgrenze." };
        return this;
    }
}

/// <summary>Thread-sicherer zentraler Datenbestand; OPC-UA-Reads führen keinerlei HTTP aus.</summary>
public sealed class GatewayCache
{
    private readonly ConcurrentDictionary<string, Sample> values = new();
    private readonly ConcurrentDictionary<string, SourceHealth> health = new();
    public static string Key(Guid device, int zone, string name) => $"{device:D}/{zone}/{name}";
    public Sample Get(string key, int stale) => values.GetValueOrDefault(key,
        Sample.Missing(StatusCodes.BadWaitingForInitialData, "Noch kein Wert empfangen.")).At(DateTime.UtcNow, stale);
    public void Set(string key, Sample value) => values[key] = value;
    public void Fail(string key, uint status, string reason)
    {
        values.AddOrUpdate(key, Sample.Missing(status, reason),
            (_, previous) => previous with
            {
                // Part 8: BadNoCommunication bedeutet ausdrücklich, dass kein letzter Wert verfügbar ist.
                // Ein bekannter letzter Wert erhält daher den dafür vorgesehenen Uncertain-Status.
                Status = previous.Value != null && status is StatusCodes.BadNoCommunication or StatusCodes.BadTimeout ?
                    StatusCodes.UncertainNoCommunicationLastUsableValue : status,
                Reason = reason, FetchedAt = DateTime.UtcNow
            });
    }
    public void Source(string key, bool success, string message)
    {
        health.AddOrUpdate(key, new SourceHealth(success, success ? DateTime.UtcNow : null, message),
            (_, previous) => new(success, success ? DateTime.UtcNow : previous.LastSuccess, message));
    }
    public IReadOnlyDictionary<string, SourceHealth> Health => health;
    public object Diagnostics(int stale) => values.Select(p => new
    {
        key = p.Key, p.Value.Value, status = StatusCode.LookupSymbolicId(p.Value.At(DateTime.UtcNow, stale).Status),
        sourceTime = p.Value.SourceTime, fetchedAt = p.Value.FetchedAt, reason = p.Value.At(DateTime.UtcNow, stale).Reason
    }).ToArray();
}
public sealed record SourceHealth(bool Reachable, DateTime? LastSuccess, string Message);

/// <summary>Konservative Übersetzung ausschließlich bekannter Schemafelder. Ohne Gerätebeleg keine aktivierte Bindung.</summary>
public static class Mapping
{
    public static readonly IReadOnlyDictionary<string, HashSet<string>> KnownFields = new Dictionary<string, HashSet<string>>
    {
        ["Actual"] = ["temperature1", "coreTemperature", "bottomTemperature", "cupTemperature1", "timeRemaining", "actualTemperature", "actualDisplayedValues"],
        ["Set"] = ["setDisplayedValues", "setTemperatureValues", "setCoreTemperature", "setProcessTime", "programName", "programUId", "programId", "setPowerLevelMax", "nominalPower"]
    };
    public static bool IsDynamic(string name) => name is "ProgramMode" or "SignalMode" or "CurrentState" or
        "PlatenPositionState" or "IsLiftUp" or "IsPanDetected" or "ProgramId" or "ProgramName" or "ProgramUId" ||
        name.StartsWith("Actual", StringComparison.Ordinal) || name.StartsWith("Set", StringComparison.Ordinal) ||
        name is "TimeRemaining" or "RemainingProcessTime";
    public static string Unit(string name) => name.Contains("Temperature") ? "°C" :
        name.Contains("Time") ? "s" : name is "NominalPower" or "ActualPower" ? "W" :
        name == "NominalVoltage" ? "V" : name == "SetPowerValue" ? "%" : "";
    public static bool ValidUnit(ModelNode n, string unit) => Unit(n.Name) switch
    {
        "°C" => unit is "°C" or "K" or "°F",
        "s" => unit is "s" or "ms",
        "W" => unit == "W",
        "V" => unit == "V",
        "%" => unit == "%",
        _ => n.DataTypeName is "String" or "LocalizedText" or "Guid" ? unit == "text" :
             n.Name == "ProgramId" && unit == "1"
    };
    /// <summary>Keine stillen Null-/Bool-Defaults; JSON null, NaN und Überläufe werden verworfen.</summary>
    public static object? ConvertScalar(JsonElement value, string type) => type switch
    {
        "String" => value.GetString() ?? throw new FormatException(),
        "LocalizedText" => new LocalizedText(value.GetString() ?? throw new FormatException()),
        "Boolean" => value.GetBoolean(),
        "Float" => FiniteFloat(value.GetDouble()),
        "Double" => double.IsFinite(value.GetDouble()) ? value.GetDouble() : throw new FormatException(),
        "Guid" => Guid.Parse(value.GetString()!),
        "UInt16" => value.GetUInt16(),
        "UInt32" => value.GetUInt32(),
        _ => value.GetInt32()
    };
    private static float FiniteFloat(double value)
    {
        if (!double.IsFinite(value) || value > float.MaxValue || value < -float.MaxValue) throw new OverflowException();
        return (float)value;
    }
    public static Sample Translate(JsonElement row, FieldBinding b, ModelNode node, DateTime fetchedAt)
    {
        if (!row.TryGetProperty("date", out var date) || date.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp) ||
            !HasOffset(date.GetString()!) || stamp.UtcDateTime > fetchedAt.AddMinutes(2))
            return Sample.Missing(StatusCodes.BadDecodingError, "Messzeit fehlt, ist ungültig, zeitzonenlos oder liegt in der Zukunft.");
        if (!row.TryGetProperty(b.Field, out var field) || field.ValueKind == JsonValueKind.Null)
            return new(null, StatusCodes.BadNoData, stamp.UtcDateTime, fetchedAt, "API-Feld fehlt; kein Geräte-Nichtunterstützungsnachweis.");
        try
        {
            object? result;
            if (field.ValueKind == JsonValueKind.Number)
            {
                double v = field.GetDouble();
                v = b.Unit switch { "°F" => (v - 32) * 5 / 9, "K" => v - 273.15, "ms" => v / 1000, _ => v };
                if (!double.IsFinite(v)) throw new FormatException();
                result = node.DataTypeName == "Float" ? FiniteFloat(v) :
                    node.DataTypeName == "Double" ? v :
                    v == Math.Truncate(v) && v >= int.MinValue && v <= int.MaxValue ? (object)(int)v : throw new OverflowException();
            }
            else result = ConvertScalar(field, node.DataTypeName);
            return new(result, StatusCodes.Good, stamp.UtcDateTime, fetchedAt, "Belegte administrative Feldzuordnung.");
        }
        catch (OverflowException) { return new(null, StatusCodes.BadOutOfRange, stamp.UtcDateTime, fetchedAt, "Wert außerhalb des HKI-Datentyps."); }
        catch (Exception e) when (e is FormatException or InvalidOperationException or JsonException)
        { return new(null, StatusCodes.BadDecodingError, stamp.UtcDateTime, fetchedAt, "Ungültiges Datenformat."); }
    }
    public static bool HasOffset(string value) => value.EndsWith('Z') ||
        value.Length >= 6 && value[^3] == ':' && value[^6] is '+' or '-';
}
