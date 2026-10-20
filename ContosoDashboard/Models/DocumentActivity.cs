using System.ComponentModel.DataAnnotations;

namespace ContosoDashboard.Models;

public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    public int? DocumentId { get; set; }

    [Required]
    [MaxLength(200)]
    public string DocumentTitle { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    public int UserId { get; set; }

    [Required]
    [MaxLength(30)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Details { get; set; }

    public DateTime OccurredDate { get; set; } = DateTime.UtcNow;

    public virtual Document? Document { get; set; }
    public virtual User User { get; set; } = null!;
}

public static class DocumentActions
{
    public const string Uploaded = "Uploaded";
    public const string Downloaded = "Downloaded";
    public const string Previewed = "Previewed";
    public const string MetadataUpdated = "MetadataUpdated";
    public const string Replaced = "Replaced";
    public const string Shared = "Shared";
    public const string Unshared = "Unshared";
    public const string Deleted = "Deleted";
}