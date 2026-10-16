using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using Overmark.Capture;

namespace Overmark.Editor;

/// <summary>An editable copy of a capture: its original pixels, and the marks and crop on top of them.</summary>
/// <param name="Draft">True for marks that were never saved to the capture file (left behind by switching captures).</param>
internal sealed record KeptEdit(BitmapSource Original, Int32Rect Crop, IReadOnlyList<Mark> Marks, bool Draft);

/// <summary>
/// Keeps captures editable for a day after they were last edited. Saving a capture flattens its marks into the PNG,
/// so alongside it this keeps the original pixels and the marks, and reopening the capture brings them back as marks
/// that can still be moved, erased and recoloured. Each capture can have a saved state (matching its file) and a
/// draft (unsaved marks, kept when switching to another capture); the draft wins while it lasts.
/// <para>
/// A copy is only used while the capture file is exactly as Overmark left it, so a file changed by anything else
/// opens as it is. Everything here is a convenience: failures are logged and never get in the way of editing.
/// </para>
/// </summary>
internal sealed class EditStore(string folder, Func<DateTime>? utcNow = null)
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    // The originals already on disk this run, so each is only encoded once (it's the slow part).
    private readonly Dictionary<string, WeakReference<BitmapSource>> _written = [];
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    public string Folder { get; } = folder;

    /// <summary>The capture's editable copy (its draft if it has one), or null if there's none it can use.</summary>
    public KeptEdit? Find(string capture)
    {
        try
        {
            var file = new FileInfo(capture);
            if (!file.Exists)
            {
                return null;
            }

            var key = Key(capture);
            foreach (var draft in new[] { true, false })
            {
                if (ReadSlot(SlotPath(key, draft)) is not { } slot || !IsCurrent(slot, file))
                {
                    continue;
                }

                var original = LoadOriginal(key);
                if (original is null)
                {
                    return null;
                }

                _written[key] = new WeakReference<BitmapSource>(original);
                var crop = slot.Crop is [var x, var y, var width, var height]
                    && x >= 0 && y >= 0 && width > 0 && height > 0 && x + width <= original.PixelWidth && y + height <= original.PixelHeight
                    ? new Int32Rect(x, y, width, height)
                    : new Int32Rect(0, 0, original.PixelWidth, original.PixelHeight);
                var marks = slot.Marks.Select(MarkFormat.FromData).OfType<Mark>().ToList();
                return new KeptEdit(original, crop, marks, draft);
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Log.Warn($"Couldn't read the editable copy of {capture}: {ex.Message}");
        }

        return null;
    }

    /// <summary>True if the capture has unsaved marks kept from an earlier editor (checked without loading the picture).</summary>
    public bool HasDraft(string capture)
    {
        try
        {
            var file = new FileInfo(capture);
            return file.Exists && ReadSlot(SlotPath(Key(capture), draft: true)) is { } slot && IsCurrent(slot, file);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return false;
        }
    }

    /// <summary>Keeps the document as it was just saved to the capture file, replacing any draft.</summary>
    public void KeepSaved(string capture, EditorDocument document) => Keep(capture, document, draft: false);

    /// <summary>Keeps the document's unsaved marks, without touching the capture file.</summary>
    public void KeepDraft(string capture, EditorDocument document) => Keep(capture, document, draft: true);

    /// <summary>Throws away unsaved marks, so the capture opens as it was last saved.</summary>
    public void DiscardDraft(string capture) => Delete(SlotPath(Key(capture), draft: true));

    /// <summary>Throws away everything kept for the capture (when it's deleted, or saved with no marks left).</summary>
    public void Forget(string capture)
    {
        var key = Key(capture);
        Delete(SlotPath(key, draft: true));
        Delete(SlotPath(key, draft: false));
        Delete(OriginalPath(key));
        _written.Remove(key);
    }

    /// <summary>Deletes copies last edited more than <see cref="KeepFor"/> ago, and anything left over.</summary>
    public void CleanUp()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
            {
                if (ReadSlot(path) is { } slot && _utcNow() - slot.KeptAt <= KeepFor)
                {
                    live.Add(Path.GetFileName(path).Split('.')[0]);
                }
                else
                {
                    Delete(path);
                }
            }

            foreach (var path in Directory.EnumerateFiles(Folder))
            {
                var name = Path.GetFileName(path);
                var leftOver = name.EndsWith(".saving", StringComparison.OrdinalIgnoreCase); // from a write that never finished
                if (leftOver || (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !live.Contains(name.Split('.')[0])))
                {
                    Delete(path);
                }
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Log.Warn($"Couldn't tidy the editable copies in {Folder}: {ex.Message}");
        }
    }

    private void Keep(string capture, EditorDocument document, bool draft)
    {
        try
        {
            var file = new FileInfo(capture);
            if (!file.Exists)
            {
                return;
            }

            Directory.CreateDirectory(Folder);
            var key = Key(capture);

            // The original pixels never change for a capture, so they're only written when they're new.
            var original = OriginalPath(key);
            if (!File.Exists(original) || !_written.TryGetValue(key, out var known) || !known.TryGetTarget(out var image) || !ReferenceEquals(image, document.Image))
            {
                WriteAtomically(original, CaptureOutput.EncodePng(document.Image));
                _written[key] = new WeakReference<BitmapSource>(document.Image);
            }

            var crop = document.Crop;
            var slot = new Slot
            {
                Capture = capture,
                KeptAt = _utcNow(),
                FileWritten = file.LastWriteTimeUtc,
                FileLength = file.Length,
                Crop = [crop.X, crop.Y, crop.Width, crop.Height],
                Marks = [.. document.Marks.Select(MarkFormat.ToData)],
            };
            WriteAtomically(SlotPath(key, draft), JsonSerializer.SerializeToUtf8Bytes(slot, JsonOptions));
            if (!draft)
            {
                Delete(SlotPath(key, draft: true));
            }
        }
        catch (Exception ex) when (IsExpected(ex) || ex is NotSupportedException)
        {
            Log.Warn($"Couldn't keep an editable copy of {capture}: {ex.Message}");
        }

        CleanUp();
    }

    /// <summary>A copy is only good while it's recent and the capture file is exactly as it was when the copy was kept.</summary>
    private bool IsCurrent(Slot slot, FileInfo file) =>
        _utcNow() - slot.KeptAt <= KeepFor && slot.FileWritten == file.LastWriteTimeUtc && slot.FileLength == file.Length;

    private static Slot? ReadSlot(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Slot>(File.ReadAllBytes(path), JsonOptions) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private BitmapSource? LoadOriginal(string key)
    {
        var path = OriginalPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>A short name for the capture's files here, from its full path (case doesn't matter on Windows).</summary>
    private static string Key(string capture)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(capture).ToUpperInvariant()));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    private string OriginalPath(string key) => Path.Combine(Folder, key + ".png");

    private string SlotPath(string key, bool draft) => Path.Combine(Folder, key + (draft ? ".draft.json" : ".saved.json"));

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var temporary = path + ".saving";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Log.Warn($"Couldn't delete {path}: {ex.Message}");
        }
    }

    private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException or FileFormatException or JsonException;

    private sealed class Slot
    {
        public string Capture { get; set; } = "";

        public DateTime KeptAt { get; set; }

        /// <summary>When the capture file was last written, and its length, as it was when this was kept.</summary>
        public DateTime FileWritten { get; set; }

        public long FileLength { get; set; }

        /// <summary>x, y, width, height in image pixels.</summary>
        public int[] Crop { get; set; } = [];

        public List<MarkData> Marks { get; set; } = [];
    }
}
