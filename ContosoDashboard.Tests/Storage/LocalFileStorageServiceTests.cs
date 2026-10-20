using ContosoDashboard.Tests.TestInfrastructure;

namespace ContosoDashboard.Tests.Storage;

public class LocalFileStorageServiceTests
{
    [Fact]
    public async Task UploadAndDownload_RoundTripsAndCreatesDirectories()
    {
        using var storage = new TempFileStorage();
        var content = new MemoryStream([1, 2, 3, 4]);

        var key = await storage.Storage.UploadAsync(content, "4/personal/unique.pdf", "application/pdf");
        await using var downloaded = await storage.Storage.DownloadAsync(key);
        using var result = new MemoryStream();
        await downloaded.CopyToAsync(result);

        Assert.Equal("4/personal/unique.pdf", key);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.ToArray());
        Assert.True(File.Exists(Path.Combine(storage.RootPath, "4", "personal", "unique.pdf")));
    }

    [Fact]
    public async Task DeleteMissing_IsNoOpAndDownloadMissingThrows()
    {
        using var storage = new TempFileStorage();

        await storage.Storage.DeleteAsync("4/personal/missing.pdf");
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.Storage.DownloadAsync("4/personal/missing.pdf"));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/tmp/outside.txt")]
    public async Task UploadRejectsPathsOutsideRoot(string key)
    {
        using var storage = new TempFileStorage();

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.Storage.UploadAsync(new MemoryStream([1]), key, "text/plain"));
    }

    [Fact]
    public async Task GetUrlIsNotSupportedForLocalStorage()
    {
        using var storage = new TempFileStorage();
        await Assert.ThrowsAsync<NotSupportedException>(() => storage.Storage.GetUrlAsync("4/personal/a.pdf", TimeSpan.FromMinutes(1)));
    }
}