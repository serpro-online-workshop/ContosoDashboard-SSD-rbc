using ContosoDashboard.Models;
using ContosoDashboard.Services.Scanning;
using ContosoDashboard.Services.Storage;
using ContosoDashboard.Data;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public sealed record UploadRequest(
    Stream Content, string OriginalFileName, long SizeBytes,
    string Title, string? Description, string Category,
    int? ProjectId, IReadOnlyCollection<string> Tags, int? TaskId = null);

public sealed record DocumentOperationResult(bool Succeeded, int? DocumentId, string? Error);

public sealed record DocumentListItem(
    int DocumentId, string Title, string Category, string OriginalFileName, string FileType,
    long FileSizeBytes, DateTime UploadedDate, int UploadedByUserId, string UploadedByName,
    int? ProjectId, string? ProjectName, IReadOnlyList<string> Tags, bool CanPreview);

public sealed record DocumentDetail(
    DocumentListItem Summary, string? Description,
    bool CanEdit, bool CanReplace, bool CanDelete, bool CanShare,
    IReadOnlyList<DocumentShareInfo> Shares);

public sealed record DocumentShareInfo(int DocumentShareId, int? UserId, string? UserName, string? Department, DateTime SharedDate);

public sealed record DocumentQuery(
    string? Category = null, int? ProjectId = null, DateTime? FromDate = null, DateTime? ToDate = null,
    DocumentSort Sort = DocumentSort.UploadedDateDesc);

public enum DocumentSort
{
    TitleAsc,
    TitleDesc,
    UploadedDateAsc,
    UploadedDateDesc,
    CategoryAsc,
    CategoryDesc,
    SizeAsc,
    SizeDesc
}

public interface IDocumentService
{
    Task<DocumentOperationResult> UploadAsync(int userId, UploadRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentListItem>> GetMyDocumentsAsync(int userId, DocumentQuery query);
    Task<IReadOnlyList<DocumentListItem>> GetProjectDocumentsAsync(int userId, int projectId);
    Task<IReadOnlyList<DocumentListItem>> GetSharedWithMeAsync(int userId);
    Task<IReadOnlyList<DocumentListItem>> SearchAsync(int userId, string term);
    Task<IReadOnlyList<DocumentListItem>> GetRecentDocumentsAsync(int userId, int count = 5);
    Task<DocumentDetail?> GetDocumentAsync(int userId, int documentId);
    Task<(Stream Content, string FileType, string FileName)?> OpenForReadAsync(int userId, int documentId, bool preview, CancellationToken ct = default);
    Task<DocumentOperationResult> UpdateMetadataAsync(int userId, int documentId, string title, string? description, string category, IReadOnlyCollection<string> tags, int? projectId);
    Task<DocumentOperationResult> ReplaceFileAsync(int userId, int documentId, Stream content, string originalFileName, long sizeBytes, CancellationToken ct = default);
    Task<DocumentOperationResult> DeleteAsync(int userId, int documentId);
    Task<DocumentOperationResult> ShareWithUserAsync(int userId, int documentId, int recipientUserId);
    Task<DocumentOperationResult> ShareWithDepartmentAsync(int userId, int documentId, string department);
    Task<DocumentOperationResult> RemoveShareAsync(int userId, int documentShareId);
    Task<IReadOnlyList<DocumentListItem>> GetTaskDocumentsAsync(int userId, int taskId);
    Task<DocumentOperationResult> AttachToTaskAsync(int userId, int taskId, int documentId);
    Task<DocumentOperationResult> DetachFromTaskAsync(int userId, int taskId, int documentId);
    Task<int> CountMyDocumentsAsync(int userId);
}

public sealed class DocumentService : IDocumentService
{
    private const string NotAvailableMessage = "Document not available.";
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IMalwareScanner _scanner;
    private readonly DocumentValidator _validator;
    private readonly DocumentAccessPolicy _accessPolicy;
    private readonly IDocumentActivityService _activityService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService storage,
        IMalwareScanner scanner,
        DocumentValidator validator,
        DocumentAccessPolicy accessPolicy,
        IDocumentActivityService activityService,
        INotificationService notificationService,
        ILogger<DocumentService> logger)
    {
        _context = context;
        _storage = storage;
        _scanner = scanner;
        _validator = validator;
        _accessPolicy = accessPolicy;
        _activityService = activityService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<DocumentOperationResult> UploadAsync(int userId, UploadRequest request, CancellationToken ct = default)
    {
        var metadata = _validator.ValidateMetadata(request.Title, request.Description, request.Category, request.Tags);
        if (!metadata.IsValid)
            return new DocumentOperationResult(false, null, metadata.Error);

        var file = _validator.ValidateFile(request.OriginalFileName, request.SizeBytes, request.Content);
        if (!file.IsValid)
            return new DocumentOperationResult(false, null, file.Error);

        if (request.ProjectId is int projectId && !await HasProjectAccessAsync(projectId, userId, ct))
            return new DocumentOperationResult(false, null, "You can only add documents to projects you belong to.");

        Stream content = request.Content;
        MemoryStream? buffered = null;
        if (!content.CanSeek)
        {
            buffered = new MemoryStream();
            await content.CopyToAsync(buffered, ct);
            buffered.Position = 0;
            content = buffered;
        }

        var scan = await _scanner.ScanAsync(content, ct);
        if (scan.Outcome != ScanOutcome.Clean)
        {
            buffered?.Dispose();
            var message = scan.Outcome == ScanOutcome.Infected
                ? $"{request.OriginalFileName} was rejected for security reasons."
                : "Security scan unavailable; try again later.";
            return new DocumentOperationResult(false, null, message);
        }

        if (content.CanSeek)
            content.Position = 0;

        var relativeDirectory = request.ProjectId?.ToString() ?? "personal";
        var storageKey = $"{userId}/{relativeDirectory}/{Guid.NewGuid():N}{file.Extension}";
        string? storedKey = null;
        Document? pendingDocument = null;

        try
        {
            storedKey = await _storage.UploadAsync(content, storageKey, file.FileType!, ct);
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            pendingDocument = new Document
            {
                Title = metadata.Title,
                Description = metadata.Description,
                Category = request.Category,
                OriginalFileName = Path.GetFileName(request.OriginalFileName),
                StoragePath = storedKey,
                FileType = file.FileType!,
                FileSizeBytes = request.SizeBytes,
                UploadedByUserId = userId,
                ProjectId = request.ProjectId,
                UploadedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow,
                Tags = metadata.Tags.Select(tag => new DocumentTag { Tag = tag }).ToList()
            };

            _context.Documents.Add(pendingDocument);
            await _context.SaveChangesAsync(ct);
            await _activityService.RecordAsync(userId, DocumentActions.Uploaded, pendingDocument.DocumentId, pendingDocument.Title, pendingDocument.FileType);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new DocumentOperationResult(true, pendingDocument.DocumentId, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (pendingDocument is not null)
            {
                foreach (var tag in pendingDocument.Tags)
                    _context.Entry(tag).State = EntityState.Detached;
                _context.Entry(pendingDocument).State = EntityState.Detached;

                foreach (var activity in _context.ChangeTracker.Entries<DocumentActivity>()
                             .Where(entry => entry.State == EntityState.Added &&
                                             entry.Entity.DocumentId == pendingDocument.DocumentId &&
                                             entry.Entity.Action == DocumentActions.Uploaded)
                             .ToList())
                {
                    activity.State = EntityState.Detached;
                }
            }

            if (storedKey is not null)
            {
                try
                {
                    await _storage.DeleteAsync(storedKey, ct);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException, "Failed to remove a document file after a database failure.");
                }
            }

            _logger.LogError(ex, "Document upload failed for user {UserId}.", userId);
            return new DocumentOperationResult(false, null, "Upload failed. Please try again.");
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    public async Task<IReadOnlyList<DocumentListItem>> GetMyDocumentsAsync(int userId, DocumentQuery query)
    {
        var documents = _context.Documents
            .AsNoTracking()
            .Where(document => document.UploadedByUserId == userId);

        if (!string.IsNullOrWhiteSpace(query.Category))
            documents = documents.Where(document => document.Category == query.Category);
        if (query.ProjectId.HasValue)
            documents = documents.Where(document => document.ProjectId == query.ProjectId.Value);
        if (query.FromDate.HasValue)
        {
            var from = query.FromDate.Value.Date;
            documents = documents.Where(document => document.UploadedDate >= from);
        }
        if (query.ToDate.HasValue)
        {
            var toExclusive = query.ToDate.Value.Date.AddDays(1);
            documents = documents.Where(document => document.UploadedDate < toExclusive);
        }

        documents = query.Sort switch
        {
            DocumentSort.TitleAsc => documents.OrderBy(document => document.Title),
            DocumentSort.TitleDesc => documents.OrderByDescending(document => document.Title),
            DocumentSort.UploadedDateAsc => documents.OrderBy(document => document.UploadedDate),
            DocumentSort.UploadedDateDesc => documents.OrderByDescending(document => document.UploadedDate),
            DocumentSort.CategoryAsc => documents.OrderBy(document => document.Category),
            DocumentSort.CategoryDesc => documents.OrderByDescending(document => document.Category),
            DocumentSort.SizeAsc => documents.OrderBy(document => document.FileSizeBytes),
            DocumentSort.SizeDesc => documents.OrderByDescending(document => document.FileSizeBytes),
            _ => documents.OrderByDescending(document => document.UploadedDate)
        };

        var rows = await documents.Select(document => new
        {
            document.DocumentId,
            document.Title,
            document.Category,
            document.OriginalFileName,
            document.FileType,
            document.FileSizeBytes,
            document.UploadedDate,
            document.UploadedByUserId,
            UploadedByName = document.UploadedByUser.DisplayName,
            document.ProjectId,
            ProjectName = document.Project == null ? null : document.Project.Name,
            Tags = document.Tags.Select(tag => tag.Tag).ToList()
        }).ToListAsync();

        return rows.Select(row => new DocumentListItem(
            row.DocumentId,
            row.Title,
            row.Category,
            row.OriginalFileName,
            row.FileType,
            row.FileSizeBytes,
            row.UploadedDate,
            row.UploadedByUserId,
            row.UploadedByName,
            row.ProjectId,
            row.ProjectName,
            row.Tags,
            _validator.IsPreviewable(row.FileType))).ToList();
    }

    private Task<bool> HasProjectAccessAsync(int projectId, int userId, CancellationToken ct) =>
        _context.Projects.AnyAsync(project =>
            project.ProjectId == projectId &&
            (project.ProjectManagerId == userId || project.ProjectMembers.Any(member => member.UserId == userId)), ct);
    public Task<IReadOnlyList<DocumentListItem>> GetProjectDocumentsAsync(int userId, int projectId) => throw new NotImplementedException();
    public Task<IReadOnlyList<DocumentListItem>> GetSharedWithMeAsync(int userId) => throw new NotImplementedException();
    public Task<IReadOnlyList<DocumentListItem>> SearchAsync(int userId, string term) => throw new NotImplementedException();
    public Task<IReadOnlyList<DocumentListItem>> GetRecentDocumentsAsync(int userId, int count = 5) => throw new NotImplementedException();
    public Task<DocumentDetail?> GetDocumentAsync(int userId, int documentId) => throw new NotImplementedException();
    public Task<(Stream Content, string FileType, string FileName)?> OpenForReadAsync(int userId, int documentId, bool preview, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<DocumentOperationResult> UpdateMetadataAsync(int userId, int documentId, string title, string? description, string category, IReadOnlyCollection<string> tags, int? projectId) => throw new NotImplementedException();
    public Task<DocumentOperationResult> ReplaceFileAsync(int userId, int documentId, Stream content, string originalFileName, long sizeBytes, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<DocumentOperationResult> DeleteAsync(int userId, int documentId) => throw new NotImplementedException();
    public Task<DocumentOperationResult> ShareWithUserAsync(int userId, int documentId, int recipientUserId) => throw new NotImplementedException();
    public Task<DocumentOperationResult> ShareWithDepartmentAsync(int userId, int documentId, string department) => throw new NotImplementedException();
    public Task<DocumentOperationResult> RemoveShareAsync(int userId, int documentShareId) => throw new NotImplementedException();
    public Task<IReadOnlyList<DocumentListItem>> GetTaskDocumentsAsync(int userId, int taskId) => throw new NotImplementedException();
    public Task<DocumentOperationResult> AttachToTaskAsync(int userId, int taskId, int documentId) => throw new NotImplementedException();
    public Task<DocumentOperationResult> DetachFromTaskAsync(int userId, int taskId, int documentId) => throw new NotImplementedException();
    public Task<int> CountMyDocumentsAsync(int userId) => throw new NotImplementedException();
}