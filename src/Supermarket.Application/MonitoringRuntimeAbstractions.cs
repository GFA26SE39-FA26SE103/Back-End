namespace Supermarket.Application;
public interface IAiMonitoringClient
{
    Task<AiPreviewStatusView> Start(MonitoringCameraSnapshot snapshot,Guid ownerId,CancellationToken ct);
    Task<AiMeasurementPage> Read(Guid cameraId,Guid ownerId,Guid? afterSessionId,long afterSequence,int limit,CancellationToken ct);
    Task Stop(Guid cameraId,Guid ownerId,CancellationToken ct);
}
public interface IMonitoringSessionOwnership { bool IsMonitoringOwned(Guid cameraId); }
public interface IMonitoringRuntimeState { MonitoringCameraRuntimeView? Get(Guid cameraId); }
public interface IMonitoringSnapshotReader
{
    Task<MonitoringCameraSnapshot[]> ReadAll(CancellationToken ct);
    Task<bool> IsCurrent(MonitoringCameraSnapshot snapshot,Guid zoneId,CancellationToken ct);
}
public interface IMonitoringIncidentQueries
{
    Task EnsureSchema(CancellationToken ct);
    Task<DateTime?> LatestEndedAt(Guid zoneId,Guid typeId,CancellationToken ct);
    Task<IncidentFeedView> PageForCamera(Guid cameraId,IncidentFeedQuery query,CancellationToken ct);
}
