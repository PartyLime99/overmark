using Microsoft.VisualBasic.FileIO;

namespace Overmark.Platform;

/// <summary>Moves files to the Recycle Bin, so something deleted by mistake can be put back.</summary>
internal static class RecycleBin
{
    /// <summary>
    /// Recycles <paramref name="path"/>. Windows only shows a dialog if something goes wrong (e.g. the file is open in
    /// another app). Returns false with a reason if the file is still there.
    /// </summary>
    public static bool TrySend(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            return true;
        }
        catch (OperationCanceledException)
        {
            problem = "Deleting was cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            problem = ex.Message;
        }

        return false;
    }
}
