namespace lms_api.Models;

public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public CompanySubscription? Subscription { get; set; }
    public string Status { get; set; } = "Trial";
}