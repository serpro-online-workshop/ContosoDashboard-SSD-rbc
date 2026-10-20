namespace ContosoDashboard.Services.Storage;

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream fileStream, string storageKey, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default);
    Task<string> GetUrlAsync(string storageKey, TimeSpan expiration, CancellationToken ct = default);
}