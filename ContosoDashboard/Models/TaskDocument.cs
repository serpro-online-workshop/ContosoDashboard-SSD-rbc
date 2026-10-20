using System.ComponentModel.DataAnnotations;

namespace ContosoDashboard.Models;

public class TaskDocument
{
    [Key]
    public int TaskDocumentId { get; set; }

    public int TaskId { get; set; }
    public int DocumentId { get; set; }
    public int AttachedByUserId { get; set; }
    public DateTime AttachedDate { get; set; } = DateTime.UtcNow;

    public virtual TaskItem Task { get; set; } = null!;
    public virtual Document Document { get; set; } = null!;
    public virtual User AttachedByUser { get; set; } = null!;
}