using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Lab.Shared.IO;

public sealed class SafeFileAppender
{
    private static readonly ConcurrentDictionary<string, LockState> Locks = new(StringComparer.Ordinal);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task AppendLineAsync(string path, string line, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(line);

        string fullPath = Path.GetFullPath(path);
        EnsureParentDirectory(fullPath);

        LockState lockState = Locks.GetOrAdd(fullPath, CreateLockState);
        await lockState.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool mutexHeld = false;

        try
        {
            AcquireNamedMutex(lockState.ProcessMutex, cancellationToken);
            mutexHeld = true;

            File.AppendAllText(fullPath, line + Environment.NewLine, Utf8NoBom);
        }
        finally
        {
            if (mutexHeld)
            {
                lockState.ProcessMutex.ReleaseMutex();
            }

            lockState.Gate.Release();
        }
    }

    public void AppendLine(string path, string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(line);

        string fullPath = Path.GetFullPath(path);
        EnsureParentDirectory(fullPath);

        LockState lockState = Locks.GetOrAdd(fullPath, CreateLockState);
        lockState.Gate.Wait();
        bool mutexHeld = false;

        try
        {
            AcquireNamedMutex(lockState.ProcessMutex, CancellationToken.None);
            mutexHeld = true;
            File.AppendAllText(fullPath, line + Environment.NewLine, Utf8NoBom);
        }
        finally
        {
            if (mutexHeld)
            {
                lockState.ProcessMutex.ReleaseMutex();
            }

            lockState.Gate.Release();
        }
    }

    private static void AcquireNamedMutex(Mutex mutex, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (mutex.WaitOne(TimeSpan.FromMilliseconds(250)))
                {
                    return;
                }
            }
            catch (AbandonedMutexException)
            {
                return;
            }
        }
    }

    private static LockState CreateLockState(string fullPath) =>
        new(new SemaphoreSlim(1, 1), new Mutex(false, BuildMutexName(fullPath)));

    private static string BuildMutexName(string fullPath)
    {
        byte[] hash = SHA256.HashData(Utf8NoBom.GetBytes(fullPath));
        return $"lab-safe-file-appender-{Convert.ToHexString(hash)}";
    }

    private static void EnsureParentDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private sealed record LockState(SemaphoreSlim Gate, Mutex ProcessMutex);
}
