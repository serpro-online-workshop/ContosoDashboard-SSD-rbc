using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.Tests.Services;

public class MyDocumentsQueryTests
{
    [Fact]
    public async Task GetMyDocumentsAsync_ReturnsOnlyOwnUploads()
    {
        await using var database = new SqliteTestDatabase();
        using var storage = new TempFileStorage();
        var service = CreateService(database, storage);
        AddDocument(database, 4, "Mine", "Personal Files", 10, DateTime.UtcNow.AddDays(-1));
        AddDocument(database, 3, "Theirs", "Team Resources", 20, DateTime.UtcNow);
        await database.Context.SaveChangesAsync();

        var results = await service.GetMyDocumentsAsync(4, new DocumentQuery());

        Assert.Single(results);
        Assert.Equal("Mine", results[0].Title);
    }

    [Theory]
    [InlineData(DocumentSort.TitleAsc)]
    [InlineData(DocumentSort.TitleDesc)]
    [InlineData(DocumentSort.UploadedDateAsc)]
    [InlineData(DocumentSort.UploadedDateDesc)]
    [InlineData(DocumentSort.CategoryAsc)]
    [InlineData(DocumentSort.CategoryDesc)]
    [InlineData(DocumentSort.SizeAsc)]
    [InlineData(DocumentSort.SizeDesc)]
    public async Task GetMyDocumentsAsync_SortsByEverySupportedField(DocumentSort sort)
    {
        await using var database = new SqliteTestDatabase();
        using var storage = new TempFileStorage();
        var service = CreateService(database, storage);
        AddDocument(database, 4, "Alpha", "Reports", 20, DateTime.UtcNow.AddDays(-3));
        AddDocument(database, 4, "Charlie", "Personal Files", 30, DateTime.UtcNow.AddDays(-1));
        AddDocument(database, 4, "Bravo", "Project Documents", 10, DateTime.UtcNow.AddDays(-2));
        await database.Context.SaveChangesAsync();

        var results = await service.GetMyDocumentsAsync(4, new DocumentQuery(Sort: sort));
        var expectedTitles = sort switch
        {
            DocumentSort.TitleAsc => new[] { "Alpha", "Bravo", "Charlie" },
            DocumentSort.TitleDesc => new[] { "Charlie", "Bravo", "Alpha" },
            DocumentSort.UploadedDateAsc => new[] { "Alpha", "Bravo", "Charlie" },
            DocumentSort.UploadedDateDesc => new[] { "Charlie", "Bravo", "Alpha" },
            DocumentSort.CategoryAsc => new[] { "Charlie", "Bravo", "Alpha" },
            DocumentSort.CategoryDesc => new[] { "Alpha", "Bravo", "Charlie" },
            DocumentSort.SizeAsc => new[] { "Bravo", "Alpha", "Charlie" },
            DocumentSort.SizeDesc => new[] { "Charlie", "Alpha", "Bravo" },
            _ => throw new ArgumentOutOfRangeException(nameof(sort))
        };

        Assert.Equal(expectedTitles, results.Select(item => item.Title));
    }

    [Fact]
    public async Task GetMyDocumentsAsync_CombinesCategoryProjectAndInclusiveDateFilters()
    {
        await using var database = new SqliteTestDatabase();
        using var storage = new TempFileStorage();
        var service = CreateService(database, storage);
        var firstDay = DateTime.UtcNow.Date.AddDays(-2);
        AddDocument(database, 4, "Inside", "Reports", 10, firstDay.AddHours(12), 1);
        AddDocument(database, 4, "Outside date", "Reports", 10, firstDay.AddDays(-1), 1);
        AddDocument(database, 4, "Outside project", "Reports", 10, firstDay.AddHours(12), null);
        AddDocument(database, 4, "Outside category", "Personal Files", 10, firstDay.AddHours(12), 1);
        await database.Context.SaveChangesAsync();

        var results = await service.GetMyDocumentsAsync(4, new DocumentQuery(
            Category: "Reports", ProjectId: 1, FromDate: firstDay, ToDate: firstDay));

        Assert.Single(results);
        Assert.Equal("Inside", results[0].Title);
    }

    [Fact]
    public async Task GetMyDocumentsAsync_SetsCanPreviewOnlyForPdfJpegAndPng()
    {
        await using var database = new SqliteTestDatabase();
        using var storage = new TempFileStorage();
        var service = CreateService(database, storage);
        AddDocument(database, 4, "PDF", "Reports", 10, DateTime.UtcNow, fileType: "application/pdf");
        AddDocument(database, 4, "JPEG", "Reports", 10, DateTime.UtcNow, fileType: "image/jpeg");
        AddDocument(database, 4, "PNG", "Reports", 10, DateTime.UtcNow, fileType: "image/png");
        AddDocument(database, 4, "DOCX", "Reports", 10, DateTime.UtcNow, fileType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        await database.Context.SaveChangesAsync();

        var results = await service.GetMyDocumentsAsync(4, new DocumentQuery());

        Assert.Equal(3, results.Count(item => item.CanPreview));
        Assert.False(results.Single(item => item.Title == "DOCX").CanPreview);
    }

    private static DocumentService CreateService(SqliteTestDatabase database, TempFileStorage storage) =>
        new(
            database.Context,
            storage.Storage,
            new FakeMalwareScanner(),
            new DocumentValidator(),
            new DocumentAccessPolicy(database.Context),
            new DocumentActivityService(database.Context),
            new NotificationService(database.Context),
            NullLogger<DocumentService>.Instance);

    private static void AddDocument(
        SqliteTestDatabase database,
        int userId,
        string title,
        string category,
        long size,
        DateTime uploaded,
        int? projectId = null,
        string fileType = "application/pdf")
    {
        database.Context.Documents.Add(new Document
        {
            Title = title,
            Category = category,
            OriginalFileName = title + ".pdf",
            StoragePath = $"{userId}/{projectId?.ToString() ?? "personal"}/{Guid.NewGuid():N}.pdf",
            FileType = fileType,
            FileSizeBytes = size,
            UploadedByUserId = userId,
            ProjectId = projectId,
            UploadedDate = uploaded,
            UpdatedDate = uploaded
        });
    }
}