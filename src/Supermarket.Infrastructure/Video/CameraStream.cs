using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Infrastructure.Video;

public sealed class VideoOptions
{
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string RecordedRoot { get; set; } = ".local/videos";
    public bool AllowDemo
    {
        get; set;
    }
    public int TimeoutSeconds { get; set; } = 10;
}
public sealed class DemoCameraState
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> states = new();
    public bool Online(Guid id) => !states.TryGetValue(id, out var online) || online;
    public void Set(Guid id, bool online) => states[id] = online;
}
public sealed class CameraStream(ICredentialProtector secrets, IOptions<VideoOptions> options, DemoCameraState demo) : ICameraStream
{
    public async Task<ProbeResult> Test(CameraConnection connection, CancellationToken ct)
    {
        try
        {
            await Preview(connection, ct);
            return new(true, "FRAME_RECEIVED");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is AppError or System.ComponentModel.Win32Exception or IOException or OperationCanceledException or System.Security.Cryptography.CryptographicException)
        {
            return new(false, "STREAM_UNAVAILABLE");
        }
    }
    public async Task<PreviewFrame> Preview(CameraConnection connection, CancellationToken ct)
    {
        Rules.Connection(connection);
        if (connection.SourceType == "DEMO")
        {
            if (!options.Value.AllowDemo || !demo.Online(connection.CameraId))
                throw new AppError("STREAM_UNAVAILABLE", "Demo camera is unavailable.");
            return new(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='960' height='540' viewBox='0 0 960 540'><rect width='960' height='540' fill='#152435'/><path d='M0 400H960M320 0V540M640 0V540' stroke='#38566e' stroke-width='3'/><text x='40' y='70' fill='white' font-size='30'>MF-01 controlled camera preview</text><rect x='350' y='180' width='240' height='200' fill='#1c8067'/><text x='380' y='290' fill='white' font-size='24'>ONLINE</text></svg>"), "image/svg+xml");
        }
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
            // Discard stderr; ffmpeg diagnostics can contain the authenticated URI.
            var drain = Drain(process.StandardError.BaseStream, timeout.Token);
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
            await drain;
            if (process.ExitCode != 0 || frame.Length == 0)
                throw new AppError("STREAM_UNAVAILABLE", "No camera frame was received.");
            return new(frame.ToArray(), "image/jpeg");
        }
        catch (System.ComponentModel.Win32Exception) { throw new AppError("VIDEO_ADAPTER_REQUIRED", "Configure an FFmpeg executable to use live or recorded video.", 422); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AppError("STREAM_TIMEOUT", "Camera preview timed out.", 422); }
        finally { try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    }
    private static async Task Drain(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (await stream.ReadAsync(buffer, ct) > 0)
        {
        }
    }
}
