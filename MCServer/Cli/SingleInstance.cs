using System.IO;

namespace MCServer.Cli;

/// <summary>
/// Prevents a second MCServer host from starting against the same data root.
/// Uses an exclusive lock file so the check works on Windows, Linux and Docker.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string LockFileName = ".mcserver.lock";
    private readonly FileStream _lock;

    private SingleInstance(FileStream stream)
    {
        _lock = stream;
    }

    public static SingleInstance? TryAcquire()
    {
        try
        {
            Directory.CreateDirectory(Program.RootPath);
            var path = Path.Combine(Program.RootPath, LockFileName);
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.WriteByte(1);
            stream.Flush();
            return new SingleInstance(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _lock.Flush();
        _lock.Dispose();
    }
}
