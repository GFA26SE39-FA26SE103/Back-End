namespace Supermarket.Infrastructure.Ai;

public sealed class StaffReportAiOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}
