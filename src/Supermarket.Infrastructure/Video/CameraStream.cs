using System.Diagnostics;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Infrastructure.Video;

public sealed class VideoOptions
{
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string RecordedRoot { get; set; } = ".local/videos";
    public int TimeoutSeconds { get; set; } = 10;
}
public sealed class CameraStream(ICredentialProtector secrets, IOptions<VideoOptions> options, IFrameHealthAnalyzer analyzer) : ICameraStream
{
    public async Task<ProbeResult> Test(CameraConnection connection, CancellationToken ct)
    {
        try
        {
            var frame = await Preview(connection, ct);
            var analysis = await analyzer.Analyze(frame, ct);
            return new(true, "FRAME_RECEIVED", analysis.ProcessingAvailable, analysis.Issues);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AppError e) when (CameraStreamFailureClassifier.IsDiagnosticCode(e.Code))
        {
            return new(false, e.Code, null, []);
        }
        catch (Exception e) when (e is AppError or System.ComponentModel.Win32Exception or IOException or OperationCanceledException or System.Security.Cryptography.CryptographicException)
        {
            return new(false, "STREAM_UNAVAILABLE", null, []);
        }
    }
    public async Task<PreviewFrame> Preview(CameraConnection connection, CancellationToken ct)
    {
        Rules.Connection(connection);
        if (connection.Protocol == "WEBRTC")
            throw new AppError("VIDEO_ADAPTER_REQUIRED", "WebRTC requires a media-gateway adapter.", 422);
        var uri = new Uri(connection.StreamUri);
        var input = connection.StreamUri;
        if (connection.SourceType == "RECORDED")
        {
            var root = Path.GetFullPath(options.Value.RecordedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(uri.LocalPath);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new AppError("INVALID_VIDEO_PATH", "Recorded video must exist under the configured video directory.", 422);
            input = path;
        }
        if (connection.Username is not null)
        {
            var builder = new UriBuilder(uri) { UserName = connection.Username, Password = connection.CredentialSecretRef is null ? "" : secrets.Unprotect(connection.CredentialSecretRef) };
            input = builder.Uri.AbsoluteUri;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        var start = new ProcessStartInfo(options.Value.FfmpegPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error" })
            start.ArgumentList.Add(argument);
        if (connection.Protocol == "RTSP")
        {
            start.ArgumentList.Add("-rtsp_transport");
            start.ArgumentList.Add("tcp");
        }
        foreach (var argument in new[] { "-i", input, "-frames:v", "1", "-vf", "scale=960:-2", "-f", "image2pipe", "-vcodec", "mjpeg", "pipe:1" })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new AppError("STREAM_UNAVAILABLE", "Camera preview could not start.");
            // Keep a small in-memory sample only for classification. It is never logged or returned
            // because FFmpeg diagnostics can contain the authenticated URI.
            var stderrTask = CaptureStandardError(process.StandardError, timeout.Token);
            using var frame = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (frame.Length + read > 4 * 1024 * 1024)
                    throw new AppError("FRAME_TOO_LARGE", "Preview frame exceeded its size limit.");
                frame.Write(buffer, 0, read);
            }
            await process.WaitForExitAsync(timeout.Token);
            var stderr = await stderrTask;
            if (process.ExitCode != 0 || frame.Length == 0)
                throw CameraStreamFailureClassifier.CreateError(stderr);
            return new(frame.ToArray(), "image/jpeg");
        }
        catch (System.ComponentModel.Win32Exception) { throw new AppError("VIDEO_ADAPTER_REQUIRED", "Configure an FFmpeg executable to use live or recorded video.", 422); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AppError("TIMEOUT", "Camera connection timed out.", 422); }
        finally { try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }

    private static async Task<string> CaptureStandardError(StreamReader reader, CancellationToken ct)
    {
        const int captureLimit = 8192;
        var captured = new System.Text.StringBuilder(captureLimit);
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            var remaining = captureLimit - captured.Length;
            if (remaining > 0)
                captured.Append(buffer, 0, Math.Min(read, remaining));
        }
        return captured.ToString();
    }
}

internal static class CameraStreamFailureClassifier
{
    private static readonly HashSet<string> DiagnosticCodes =
    [
        "AUTHENTICATION_FAILED",
        "TIMEOUT",
        "CONNECTION_REFUSED",
        "STREAM_UNAVAILABLE"
    ];

    public static bool IsDiagnosticCode(string code) => DiagnosticCodes.Contains(code);

    public static AppError CreateError(string stderr)
    {
        var code = Classify(stderr);
        return code switch
        {
            "AUTHENTICATION_FAILED" => new AppError(code, "Camera authentication failed.", 422),
            "TIMEOUT" => new AppError(code, "Camera connection timed out.", 422),
            "CONNECTION_REFUSED" => new AppError(code, "Camera refused the connection.", 422),
            _ => new AppError("STREAM_UNAVAILABLE", "No camera frame was received.", 422)
        };
    }

    public static string Classify(string stderr)
    {
        if (ContainsAny(stderr, "401 Unauthorized", "authorization failed", "authentication failed"))
            return "AUTHENTICATION_FAILED";
        if (ContainsAny(stderr, "Connection refused", "actively refused"))
            return "CONNECTION_REFUSED";
        if (ContainsAny(stderr, "Connection timed out", "Operation timed out", "I/O timeout"))
            return "TIMEOUT";
        return "STREAM_UNAVAILABLE";
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
