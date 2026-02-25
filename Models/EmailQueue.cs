using System.ComponentModel.DataAnnotations;

namespace lms_api.Models;

public enum EmailStatus
{
    Pending,
    Sent,
    Failed
}

public class EmailQueue
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string ToEmail { get; set; } = string.Empty;

    [Required]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    public EmailStatus Status { get; set; } = EmailStatus.Pending;

    public int RetryCount { get; set; } = 0;

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SentAt { get; set; }
}