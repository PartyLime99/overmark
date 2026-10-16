using System.Text.Json;
using System.Text.Json.Serialization;

namespace Overmark.Capture;

/// <summary>One saved screenshot or recording, as remembered for the editor's list of today's captures.</summary>
/// <param name="Seconds">How long a recording is; null for a screenshot.</param>
/// <param name="Closed">Taken off the list with its close button (it comes back if it's opened again).</param>
internal sealed record CaptureRecord(string Path, CaptureKind Kind, DateTime TakenAt, int Width, int Height, double? Seconds = null, bool Closed = false)
{
    [JsonIgnore]
    public bool IsVideo => Seconds is not null;
}

/// <summary>
/// The captures Overmark has saved recently, kept in history.json next to the settings so the list survives
/// restarts. Only what Overmark itself saved is listed; files that have since been deleted or moved are left out
/// when it's read. Used on the UI thread only.
/// </summary>
internal sealed class CaptureHistory(string filePath)
{
    /// <summary>Plenty for a busy day; older entries are dropped as new ones come in.</summary>
    private const int MaxRecords = 500;

    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private List<CaptureRecord>? _records;

    /// <summary>Raised after a capture is added.</summary>
    public event Action? Changed;

    public string FilePath { get; } = filePath;

    public void Add(CaptureRecord record)
    {
        var records = Records();
        records.RemoveAll(r => string.Equals(r.Path, record.Path, StringComparison.OrdinalIgnoreCase));
        records.Add(record);

        var cutoff = record.TakenAt - KeepFor;
        records.RemoveAll(r => r.TakenAt < cutoff);
        if (records.Count > MaxRecords)
        {
            records.RemoveRange(0, records.Count - MaxRecords);
        }

        Write(records);
        Changed?.Invoke();
    }

    /// <summary>The captures taken on <paramref name="day"/> whose files are still there and that haven't been closed, newest first.</summary>
    public IReadOnlyList<CaptureRecord> TakenOn(DateTime day) =>
        Records()
            .Where(r => r.TakenAt.Date == day.Date && !r.Closed && File.Exists(r.Path))
            .OrderByDescending(r => r.TakenAt)
            .ToList();

    /// <summary>Takes a capture off the list (the file stays where it is).</summary>
    public void Close(string path) => SetClosed(path, closed: true);

    /// <summary>Puts a closed capture back on the list, when it's opened again.</summary>
    public void Reopen(string path) => SetClosed(path, closed: false);

    private void SetClosed(string path, bool closed)
    {
        var records = Records();
        var index = records.FindIndex(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || records[index].Closed == closed)
        {
            return;
        }

        records[index] = records[index] with { Closed = closed };
        Write(records);
        Changed?.Invoke();
    }

    /// <summary>The most recent screenshot (not recording) whose file is still there.</summary>
    public CaptureRecord? LatestScreenshot() =>
        Records().Where(r => !r.IsVideo && File.Exists(r.Path)).MaxBy(r => r.TakenAt);

    private List<CaptureRecord> Records() => _records ??= Read();

    private List<CaptureRecord> Read()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<List<CaptureRecord>>(File.ReadAllText(FilePath), JsonOptions)?
                    .Where(r => !string.IsNullOrEmpty(r.Path))
                    .ToList() ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Only a convenience: start a fresh list rather than get in the way.
            Log.Warn($"Couldn't read {FilePath}, starting a new capture history: {ex.Message}");
        }

        return [];
    }

    private void Write(List<CaptureRecord> records)
    {
        try
        {
            // Write next to the file and swap it in, so it's never left half-written.
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temporary = FilePath + ".saving";
            File.WriteAllText(temporary, JsonSerializer.Serialize(records, JsonOptions));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save the capture history to {FilePath}: {ex.Message}");
        }
    }
}
