namespace Supermarket.Domain;

/// <summary>ShiftAssignment.status: a Manager plans DRAFT rows, publishing makes them SCHEDULED (visible to Staff), CANCELLED ends one.</summary>
public static class ShiftAssignmentStatus
{
    public const string Draft = "DRAFT", Scheduled = "SCHEDULED", Cancelled = "CANCELLED";
}
public static class ShiftTemplateStatus
{
    public const string Active = "ACTIVE", Inactive = "INACTIVE";
}
public static class ShiftRules
{
    /// <summary>BR-33: shift templates are 4, 6 or 8 hours long.</summary>
    public static readonly int[] AllowedHours = [4, 6, 8];
    /// <summary>Longest range a schedule read or publish covers.</summary>
    public const int MaxRangeDays = 31;

    /// <summary>An end time at or before the start time means the shift ends the next day.</summary>
    public static double Hours(TimeOnly start, TimeOnly end)
    {
        var minutes = (end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        return (minutes <= 0 ? minutes + 24 * 60 : minutes) / 60;
    }
    public static string Template(string? name, TimeOnly start, TimeOnly end)
    {
        var clean = Rules.Text(name, 200, "Shift name");
        Rules.Require(start != end && AllowedHours.Contains((int)Hours(start, end)) && Hours(start, end) % 1 == 0, "INVALID_SHIFT_LENGTH",
            "A shift must last 4, 6 or 8 hours.");
        return clean;
    }
    public static void Range(DateOnly from, DateOnly to)
        => Rules.Require(to >= from && to.DayNumber - from.DayNumber < MaxRangeDays, "INVALID_RANGE", $"The date range must be ordered and at most {MaxRangeDays} days.");
}
