using System.Text;

namespace uTracerProManager.Services;

public sealed class AppLogService
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppLogService(string path) => _path = path;

    public async Task WriteAsync(string message, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var stream = new FileStream(
                _path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteLineAsync($"{DateTimeOffset.Now:O} {message}");
        }
        finally
        {
            _gate.Release();
        }
    }
}
