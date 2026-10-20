using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services.Storage;

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;

    public LocalFileStorageService(IOptions<FileStorageOptions> options, IHostEnvironment environment)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath));
        _rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;
    }

    public async Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType, CancellationToken ct = default)
    {
        var destination = ResolvePath(storageKey);
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await fileStream.CopyToAsync(output, 81920, ct);

            File.Move(temporaryPath, destination);
            return storageKey.Replace(Path.DirectorySeparatorChar, '/');
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = ResolvePath(storageKey);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = ResolvePath(storageKey);
        if (!File.Exists(path))
            throw new FileNotFoundException("The stored document was not found.", path);

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<string> GetUrlAsync(string storageKey, TimeSpan expiration, CancellationToken ct = default) =>
        throw new NotSupportedException("Local files do not have public URLs.");

    private string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey))
            throw new InvalidOperationException("The storage key is invalid.");

        var segments = storageKey.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw new InvalidOperationException("The storage key is invalid.");

        var path = Path.GetFullPath(Path.Combine(_rootPath, Path.Combine(segments)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(_rootPrefix, comparison))
            throw new InvalidOperationException("The storage key resolves outside the storage root.");

        return path;
    }
}