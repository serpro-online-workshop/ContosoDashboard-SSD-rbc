using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed record DocumentReport(
    IReadOnlyList<(string FileType, int Count)> TopUploadedTypes,
    IReadOnlyList<(int UserId, string UserName, int Count)> TopUploaders,
    IReadOnlyList<(DateOnly Day, int Downloads, int Previews)> AccessByDay,
    IReadOnlyList<(int? DocumentId, string Title, int AccessCount)> MostAccessedDocuments);

public interface IDocumentActivityService
{
    Task RecordAsync(int userId, string action, int? documentId, string documentTitle, string fileType, string? details = null);
    Task<DocumentReport?> GetReportAsync(int userId, DateTime fromUtc, DateTime toUtc);
}

public sealed class DocumentActivityService : IDocumentActivityService
{
    private readonly ApplicationDbContext _context;

    public DocumentActivityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task RecordAsync(int userId, string action, int? documentId, string documentTitle, string fileType, string? details = null)
    {
        _context.DocumentActivities.Add(new DocumentActivity
        {
            UserId = userId,
            Action = action,
            DocumentId = documentId,
            DocumentTitle = documentTitle,
            FileType = fileType,
            Details = details,
            OccurredDate = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public Task<DocumentReport?> GetReportAsync(int userId, DateTime fromUtc, DateTime toUtc)
    {
        throw new NotImplementedException();
    }
}