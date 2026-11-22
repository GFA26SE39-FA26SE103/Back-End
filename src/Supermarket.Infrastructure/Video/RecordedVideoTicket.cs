using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Supermarket.Application;

namespace Supermarket.Infrastructure.Video;

public sealed class RecordedVideoTicket(IDataProtectionProvider provider) : IRecordedVideoTicket
{
    private readonly ITimeLimitedDataProtector protector = provider.CreateProtector("FA26SE103.RecordedVideoPlayback.v1").ToTimeLimitedDataProtector();

    public string Issue(Guid cameraId, Guid assetId) => protector.Protect(Scope(cameraId, assetId), TimeSpan.FromHours(1));

    public bool IsValid(string? ticket, Guid cameraId, Guid assetId)
    {
        if (string.IsNullOrEmpty(ticket)) return false;
        try { return protector.Unprotect(ticket) == Scope(cameraId, assetId); }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
    }

    private static string Scope(Guid cameraId, Guid assetId) => $"{cameraId:N}:{assetId:N}";
}
