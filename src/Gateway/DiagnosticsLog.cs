using System.Text;
using System.Text.RegularExpressions;

namespace KitchenGateway;

/// <summary>Begrenzter Laufzeitverlauf für die angemeldete Webadministration. Nur bereinigte Meldungen übergeben.</summary>
/// <remarks>Keine API-Antworten, Zugangsdaten oder ungefilterten Exception-Texte aufnehmen. Ein Neustart leert den Verlauf.</remarks>
public sealed class DiagnosticsLog
{
    public const int Capacity = 500;
    private readonly object sync = new();
    private readonly Queue<DiagnosticEntry> entries = new();
    private long sequence;

    public void Add(string level, string component, string message, string? details = null)
    {
        lock (sync)
        {
            entries.Enqueue(new(++sequence, DateTime.UtcNow, level, component, message, details));
            while (entries.Count > Capacity) entries.Dequeue();
        }
    }

    /// <summary>Erfasst begrenzte Exception-Details ohne Exception.Data; innere Ursachen bleiben einzeln erkennbar.</summary>
    public void AddError(string component, string message, Exception exception)
    {
        var text = new StringBuilder();
        var pending = new Queue<Exception>();
        pending.Enqueue(exception);
        var count = 0;
        while (pending.Count > 0 && count++ < 16 && text.Length < 16384)
        {
            var current = pending.Dequeue();
            text.AppendLine(count == 1 ? "Fehler:" : "Innere Ursache:");
            text.AppendLine(current.GetType().FullName);
            text.AppendLine(current.Message.Length > 4096 ? current.Message[..4096] : current.Message);
            if (current is Opc.Ua.ServiceResultException ua)
                text.AppendLine($"OPC-UA-Status: {Opc.Ua.StatusCode.LookupSymbolicId(ua.StatusCode)} (0x{ua.StatusCode:X8})");
            if (current.StackTrace is { } stack)
                text.AppendLine(stack.Length > 4096 ? stack[..4096] : stack);
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.Take(16)) pending.Enqueue(inner);
            }
            else if (current.InnerException is { } inner) pending.Enqueue(inner);
            text.AppendLine();
        }
        if (pending.Count > 0) text.AppendLine("Weitere Fehlerdetails wurden gekürzt.");
        Add("Fehler", component, message, Sanitize(text.ToString()));
    }

    private static string Sanitize(string text)
    {
        var timeout = TimeSpan.FromMilliseconds(100);
        try
        {
            text = Regex.Replace(text, @"-----BEGIN [^-]*PRIVATE KEY-----[\s\S]*?(?:-----END [^-]*PRIVATE KEY-----|\z)",
                "[Privater Schlüssel entfernt]", RegexOptions.IgnoreCase, timeout);
            text = Regex.Replace(text, """\b(?:https?|opc\.tcp)://[^\s<>"']+""", match =>
            {
                if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return "[URL entfernt]";
                var clean = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
                return clean.Uri.AbsoluteUri;
            }, RegexOptions.IgnoreCase, timeout);
            text = Regex.Replace(text,
                """\b(password|passwort|api[-_]?key|token|secret|authorization)\b["']?\s*[:=]\s*(?:(?:Bearer|Basic)\s+)?(?:"[^"\r\n]*"|'[^'\r\n]*'|[^\s,;]+)""",
                "$1=[entfernt]", RegexOptions.IgnoreCase, timeout);
            return text.Length > 16384 ? text[..16384] + "\n[Details gekürzt]" : text;
        }
        catch (RegexMatchTimeoutException)
        {
            return "Fehlerdetails konnten nicht sicher bereinigt werden. Bitte das lokale Serverlog prüfen.";
        }
    }

    public DiagnosticEntry[] Snapshot()
    {
        lock (sync) return entries.ToArray();
    }
}

public sealed record DiagnosticEntry(long Id, DateTime Timestamp, string Level, string Component, string Message, string? Details = null);
