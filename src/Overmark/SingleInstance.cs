using System.Diagnostics;
using System.IO.Pipes;

namespace Overmark;

/// <summary>
/// Makes sure only one Overmark runs per Windows session. A second launch passes its command line to the
/// first one (over a named pipe only this user can open) and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Overmark.SingleInstance";
    // Pipe names are machine-wide, so include the Windows session (each signed-in user has their own).
    private static readonly string PipeName = $"Overmark.Commands.{Process.GetCurrentProcess().SessionId}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _ = ListenAsync(_stop.Token);
    }

    /// <summary>Raised on a thread-pool thread with the arguments of each later launch.</summary>
    public event Action<string[]>? CommandReceived;

    /// <summary>Returns null if Overmark is already running.</summary>
    public static SingleInstance? TryClaim()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Hands <paramref name="args"/> to the running Overmark. Returns false if it couldn't be reached.</summary>
    public static bool SendToRunningInstance(IReadOnlyList<string> args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            writer.Write(string.Join("\n", args));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private async Task ListenAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop);
                using var reader = new StreamReader(pipe);
                var text = await reader.ReadToEndAsync(stop);
                CommandReceived?.Invoke(text.Length == 0 ? [] : text.Split('\n'));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                Log.Warn($"Couldn't read a command from another launch: {ex.Message}");
            }
        }
    }
}
