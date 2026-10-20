using System.Text;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Services.Scanning;
using ContosoDashboard.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public class DocumentUploadTests
{
    private static readonly byte[] PdfContent = Encoding.ASCII.GetBytes("%PDF-1.7 sample");

    [Fact]
    public async Task UploadAsync_ValidPersonalUploadStoresFileMetadataTagsAndActivity()
    {
        await using var database = new SqliteTestDatabase();
        using var storage = new TempFileStorage();
        var scanner = new FakeMalwareScanner();
        var service = CreateService(database, storage, scanner);

        var result = await service.UploadAsync(4, Request(PdfContent, "Test Document", "Personal Files", ["Work", "Notes"]));

        Assert.True(result.Succeeded, result.Error);
        var document = await database.Context.Documents.Include(d => d.Tags).SingleAsync();
        Assert.Matches("^4/personal/[0-9a-f]{32}\\.pdf$", document.StoragePath);
        Assert.Equal("Test Document", document.Title);
        Assert.Equal("application/pdf", document.FileType);
        Assert.Equal(PdfContent.LongLength, document.FileSizeBytes);
        Assert.Equal(new[] { "notes", "work" }, document.Tags.Select(tag => tag.Tag).OrderBy(tag => tag).ToArray());
        Assert.Single(Directory.GetFiles(storage.RootPath, "*.pdf", SearchOption.AllDirectories));
        var activity = await database.Context.DocumentActivities.SingleAsync();
        Assert.Equal(DocumentActions.Uploaded, activity.Action);
        Assert.Equal(document.DocumentId, activity.DocumentId);
        Assert.Equal(1, scanner.CallCount);
    }

    [Fact]
    public async Task UploadAsync_RequiresProjectMembershipAndStoresUnderProjectPath()
    {
        await using var database = new SqliteTestDatabase();
        using var storage = new TempFileStorage();
        var service = CreateService(database, storage, new FakeMalwareScanner());

        var allowed = await service.UploadAsync(4, Request(PdfContent, "Project File", "Project Documents", [], projectId: 1));
        var outsider = await database.AddUserAsync("Outsider", UserRole.Employee, "Finance");
        var denied = await service.UploadAsync(outsider.UserId, Request(PdfContent, "Denied", "Project Documents", [], projectId: 1));

        Assert.True(allowed.Succeeded, allowed.Error);
        Assert.StartsWith("4/1/", (await database.Context.Documents.SingleAsync()).StoragePath);
        Assert.False(denied.Succeeded);
        Assert.Equal("You can only add documents to projects you belong to.", denied.Error);
        Assert.Single(database.Context.Documents);
    }

    [Theory]
    [InlineData(ScanOutcome.Infected)]
    [InlineData(ScanOutcome.Error)]
    public async Task UploadAsync_RejectsInfectedOrUnavailableScanWithoutWritingFileOrRow(ScanOutcome outcome)
    {
        await using var database = new SqliteTestDatabase();
        using var storage = new TempFileStorage();
        var scanner = new FakeMalwareScanner { Result = new ScanResult(outcome, "scan failure") };
        var service = CreateService(database, storage, scanner);

        var result = await service.UploadAsync(4, Request(PdfContent, "Rejected", "Personal Files", []));

        Assert.False(result.Succeeded);
        Assert.Equal(0, await database.Context.Documents.CountAsync());
        Assert.Empty(Directory.GetFiles(storage.RootPath));
        Assert.Empty(database.Context.DocumentActivities);
    }

    [Fact]
    public async Task UploadAsync_DatabaseFailureDeletesStoredFileAndLeavesNoRecord()
    {
        await using var database = new SqliteTestDatabase();
        using var storage = new TempFileStorage();
        var service = CreateService(database, storage, new FakeMalwareScanner());
        EventHandler<SavingChangesEventArgs> failSaving = (_, _) => throw new InvalidOperationException("simulated database failure");
        database.Context.SavingChanges += failSaving;

        var result = await service.UploadAsync(4, Request(PdfContent, "Rollback", "Personal Files", []));
        database.Context.SavingChanges -= failSaving;

        Assert.False(result.Succeeded);
        Assert.Equal(0, await database.Context.Documents.CountAsync());
        Assert.Empty(Directory.GetFiles(storage.RootPath, "*.pdf", SearchOption.AllDirectories));

        var retry = await service.UploadAsync(4, Request(PdfContent, "Retry", "Personal Files", []));

        Assert.True(retry.Succeeded, retry.Error);
        Assert.Equal(1, await database.Context.Documents.CountAsync());
        Assert.Equal("Retry", await database.Context.Documents.Select(document => document.Title).SingleAsync());
        Assert.Single(database.Context.DocumentActivities);
    }

    [Fact]
    public async Task UploadAsync_RejectsInvalidMetadataBeforeCallingScanner()
    {
        await using var database = new SqliteTestDatabase();
        using var storage = new TempFileStorage();
        var scanner = new FakeMalwareScanner();
        var service = CreateService(database, storage, scanner);

        var result = await service.UploadAsync(4, Request(PdfContent, " ", "Invalid", []));

        Assert.False(result.Succeeded);
        Assert.Equal(0, scanner.CallCount);
        Assert.Empty(database.Context.Documents);
    }

    private static DocumentService CreateService(SqliteTestDatabase database, TempFileStorage storage, FakeMalwareScanner scanner) =>
        new(
            database.Context,
            storage.Storage,
            scanner,
            new DocumentValidator(),
            new DocumentAccessPolicy(database.Context),
            new DocumentActivityService(database.Context),
            new NotificationService(database.Context),
            NullLogger<DocumentService>.Instance);

    private static UploadRequest Request(byte[] content, string title, string category, IReadOnlyCollection<string> tags, int? projectId = null) =>
        new(new MemoryStream(content), "report.pdf", content.LongLength, title, null, category, projectId, tags);
}