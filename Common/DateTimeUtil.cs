namespace lms_api.Common;

/// <summary>
/// PostgreSQL (Npgsql) requires UTC for timestamp with time zone.
/// Query-string dates arrive as DateTimeKind.Unspecified and must be normalized.
/// </summary>
public static class DateTimeUtil
{
    public static DateTime ToUtcStartOfDay(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
    }

    public static DateTime ToUtcEndOfDay(DateTime value)
    {
        return ToUtcStartOfDay(value).AddDays(1).AddTicks(-1);
    }

    public static DateTime ToUtcInstant(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    public static (DateTime Start, DateTime End) NormalizeReportRange(DateTime? start, DateTime? end)
    {
        var startDate = start.HasValue
            ? ToUtcStartOfDay(start.Value)
            : new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var endDate = end.HasValue
            ? ToUtcEndOfDay(end.Value)
            : DateTime.UtcNow;

        if (endDate.Kind != DateTimeKind.Utc)
            endDate = ToUtcInstant(endDate);

        return (startDate, endDate);
    }
}
