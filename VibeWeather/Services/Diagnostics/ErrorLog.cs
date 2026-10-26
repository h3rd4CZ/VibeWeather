using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeWeather.Services.Diagnostics;

public enum ErrorKind { Throttled, General }

/// <param name="Reference">Short id shown to the user, e.g. "E-3F9A2C", to find the entry again.</param>
/// <param name="Source">What the app was doing, e.g. "Načítání předpovědi".</param>
/// <param name="Detail">Full exception text including the stack trace.</param>
public sealed record ErrorLogEntry(string Reference, DateTimeOffset Time, ErrorKind Kind, string Source, string Message, string Detail);

/// <summary>
/// Errors kept in a JSON-lines file in the app's data folder, newest last. Writes are synchronous so an entry
/// survives a crash that follows it. The file is trimmed to the most recent entries when it grows too large.
/// </summary>
public sealed class ErrorLog
{
    const long MaxBytes = 512 * 1024;
    const int KeepAfterTrim = 200;

    readonly Lock gate = new();

    public static string FilePath { get; } = Path.Combine(FileSystem.AppDataDirectory, "logs", "errors.jsonl");

    public event Action? Changed;

    public ErrorLogEntry Write(ErrorKind kind, string source, Exception exception)
    {
        var entry = new ErrorLogEntry(
            $"E-{Random.Shared.Next(0x1000000):X6}",
            DateTimeOffset.Now,
            kind,
            source,
            exception.Message,
            exception.ToString());

        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(entry, ErrorLogJsonContext.Default.ErrorLogEntry) + "\n");
                if (new FileInfo(FilePath).Length > MaxBytes)
                    File.WriteAllLines(FilePath, File.ReadAllLines(FilePath).TakeLast(KeepAfterTrim));
            }
            catch (IOException)
            {
                // Logging must never become the next failure.
            }
        }
        Changed?.Invoke();
        return entry;
    }

    /// <summary>All entries, newest first. Unreadable lines are skipped.</summary>
    public IReadOnlyList<ErrorLogEntry> Read()
    {
        string[] lines;
        lock (gate)
        {
            if (!File.Exists(FilePath)) return [];
            lines = File.ReadAllLines(FilePath);
        }

        var entries = new List<ErrorLogEntry>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonSerializer.Deserialize(line, ErrorLogJsonContext.Default.ErrorLogEntry) is { } entry)
                    entries.Add(entry);
            }
            catch (JsonException) { }
        }
        entries.Reverse();
        return entries;
    }

    public void Clear()
    {
        lock (gate)
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        Changed?.Invoke();
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(ErrorLogEntry))]
internal sealed partial class ErrorLogJsonContext : JsonSerializerContext;
