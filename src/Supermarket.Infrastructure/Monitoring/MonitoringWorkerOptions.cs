namespace Supermarket.Infrastructure.Monitoring;
public sealed class MonitoringWorkerOptions
{
    public bool Enabled { get; set; }=true;
    public int PollIntervalMs { get; set; }=250;
    public int MaxBatch { get; set; }=64;
    public int MaxObservationGapMs { get; set; }=2000;
}
