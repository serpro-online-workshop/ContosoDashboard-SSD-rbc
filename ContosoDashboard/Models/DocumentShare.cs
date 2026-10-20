using System.ComponentModel.DataAnnotations;

namespace ContosoDashboard.Models;

public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    public int DocumentId { get; set; }
    public int? SharedWithUserId { get; set; }

    [MaxLength(100)]
    public string? SharedWithDepartment { get; set; }

    public int SharedByUserId { get; set; }
    public DateTime SharedDate { get; set; } = DateTime.UtcNow;

    public virtual Document Document { get; set; } = null!;
    public virtual User? SharedWithUser { get; set; }
    public virtual User SharedByUser { get; set; } = null!;
}