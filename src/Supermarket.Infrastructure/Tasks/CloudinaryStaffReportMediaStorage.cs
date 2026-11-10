using Supermarket.Application;
using Microsoft.Extensions.Options;

namespace Supermarket.Infrastructure.Tasks;

/// <summary>Report media uses the existing authenticated Cloudinary upload/download implementation.</summary>
public sealed class StaffReportMediaOptions
{
    public string CloudinaryFolder { get; set; } = "fa26se103/staff-reports";
}

public sealed class CloudinaryStaffReportMediaStorage(CloudinaryTaskEvidenceStorage cloud, IOptions<StaffReportMediaOptions> options) : IStaffReportMediaStorage
{
    public Task<StoredEvidence> Save(Guid reportId, Stream content, string filename, string contentType, long length, CancellationToken ct)
        => cloud.SaveInFolder(reportId, content, filename, contentType, length, options.Value.CloudinaryFolder, ct);
    public Task<EvidenceFile?> Open(Guid reportId, string token, CancellationToken ct) => cloud.Open(reportId, token, ct);
    public Task Delete(Guid reportId, string token, CancellationToken ct) => cloud.Delete(reportId, token, ct);
}
