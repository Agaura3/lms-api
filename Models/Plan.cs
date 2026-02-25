namespace lms_api.Models;

public class Plan
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int MaxEmployees { get; set; }

    public decimal PriceMonthly { get; set; }

    public decimal PriceYearly { get; set; }

    public string FeatureFlags { get; set; } = "{}";

    public bool IsActive { get; set; } = true;

    public ICollection<CompanySubscription>? Subscriptions { get; set; }
}