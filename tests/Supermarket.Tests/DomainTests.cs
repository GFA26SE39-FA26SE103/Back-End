using Supermarket.Domain;
using Supermarket.Application;
using Supermarket.Infrastructure;
using Supermarket.Infrastructure.Video;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Xunit;
namespace Supermarket.Tests;

public sealed class DomainTests
{
    private static Point[] Triangle => [new(0, 0), new(1, 0), new(0, 1)];
    [Theory, InlineData(-.1), InlineData(1.1)] public void RejectsAbsoluteCoordinates(double value) => Assert.Throws<DomainException>(() => Rules.Coordinate((decimal)value));
    [Theory, InlineData(0), InlineData(.5), InlineData(1)] public void AcceptsNormalizedCoordinates(double value) => Rules.Coordinate((decimal)value);
    [Fact] public void AllowsMissingCoordinate() => Rules.Coordinate(null);
    [Fact] public void RejectsMissingPolygon() => Assert.Throws<DomainException>(() => Rules.Polygon(null));
    [Fact] public void RejectsTwoPointPolygon() => Assert.Throws<DomainException>(() => Rules.Polygon([new(0, 0), new(1, 1)]));
    [Fact] public void RejectsDuplicateVertices() => Assert.Throws<DomainException>(() => Rules.Polygon([new(0, 0), new(1, 0), new(0, 0)]));
    [Fact] public void RejectsCollinearPolygon() => Assert.Throws<DomainException>(() => Rules.Polygon([new(0, 0), new(.5m, .5m), new(1, 1)]));
    [Fact] public void RejectsSelfIntersection() => Assert.Throws<DomainException>(() => Rules.Polygon([new(0, 0), new(1, 1), new(0, 1), new(1, 0)]));
    [Fact]
    public void AcceptsBothPolygonOrientations()
    {
        Rules.Polygon(Triangle);
        Rules.Polygon(Triangle.Reverse().ToArray());
    }
    [Fact] public void RejectsPolygonOutsideFrame() => Assert.Throws<DomainException>(() => Rules.Polygon([new(0, 0), new(2, 0), new(0, 1)]));
    [Theory]
    [InlineData("#22C55E")]
    [InlineData("#abcdef")]
    public void AcceptsRgbZoneColors(string color) => Rules.ZoneColor(color);
    [Theory]
    [InlineData("green")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void RejectsInvalidZoneColors(string color) => Assert.Throws<DomainException>(() => Rules.ZoneColor(color));
    [Fact] public void AcceptsOptionalPositiveZoneArea() { Rules.ZoneArea(null); Rules.ZoneArea(125.50m); }
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.234")]
    [InlineData("10000000000")]
    public void RejectsInvalidZoneArea(string value) => Assert.Throws<DomainException>(() => Rules.ZoneArea(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    [Theory, InlineData(null, null), InlineData(100, 200)] public void ValidMapSize(int? w, int? h) => Rules.MapSize(w, h);
    [Theory, InlineData(0, 100), InlineData(100, null), InlineData(null, 100), InlineData(-1, 1)] public void InvalidMapSize(int? w, int? h) => Assert.Throws<DomainException>(() => Rules.MapSize(w, h));
    [Fact]
    public void SameFloorOnly()
    {
        var id = Guid.NewGuid();
        Rules.SameFloor(new Camera { FloorId = id }, new Zone { FloorId = id });
        Assert.Throws<DomainException>(() => Rules.SameFloor(new Camera { FloorId = id }, new Zone { FloorId = Guid.NewGuid() }));
    }
    [Theory, InlineData(-.01), InlineData(1.01)] public void InvalidConfidence(double value) => Assert.Throws<DomainException>(() => Rules.Confidence((decimal)value));
    [Theory, InlineData(0), InlineData(.5), InlineData(1)] public void ValidConfidence(double value) => Rules.Confidence((decimal)value);
    [Fact]
    public void ProtectsFinalAdmin()
    {
        Assert.Throws<DomainException>(() => Rules.ProtectAdmin(true, false, 1));
        Rules.ProtectAdmin(true, true, 1);
        Rules.ProtectAdmin(true, false, 2);
        Rules.ProtectAdmin(false, false, 1);
    }
    [Fact]
    public void EnableRequiresCurrentSuccessAndActiveCamera()
    {
        var c = new Camera { Status = "ACTIVE" };
        var connection = new CameraConnection();
        Assert.Throws<DomainException>(() => Rules.Enable(c, connection));
        connection.LastTestResult = "FAILED";
        connection.LastTestedAt = DateTime.UtcNow;
        Assert.Throws<DomainException>(() => Rules.Enable(c, connection));
        connection.LastTestResult = "SUCCESS";
        Rules.Enable(c, connection);
        c.Status = "DISABLED";
        Assert.Throws<DomainException>(() => Rules.Enable(c, connection));
    }
    [Theory, InlineData("rtsp://user:secret@camera/main"), InlineData("https://camera/main?token=secret"), InlineData("https://camera/main#secret"), InlineData("relative/path")]
    public void RejectsUriSecrets(string uri) => Assert.Throws<DomainException>(() => Rules.SafeUri(uri));
    [Theory, InlineData("LIVE", "RTSP", "rtsp://camera/main"), InlineData("LIVE", "HTTP", "https://camera/video"), InlineData("LIVE", "HLS", "https://camera/main.m3u8"), InlineData("RECORDED", "FILE", "file:///C:/videos/test.mp4")]
    public void ValidConnection(string source, string protocol, string uri) => Rules.Connection(new CameraConnection { SourceType = source, Protocol = protocol, StreamUri = uri });
    [Theory, InlineData("LIVE", "FILE", "file:///C:/video.mp4"), InlineData("RECORDED", "RTSP", "rtsp://camera/main"), InlineData("DEMO", "HTTP", "demo://camera/main"), InlineData("DEMO", "HTTP", "http://camera/main"), InlineData("LIVE", "RTSP", "https://camera/video"), InlineData("OTHER", "HTTP", "https://camera/video")]
    public void InvalidConnection(string source, string protocol, string uri) => Assert.Throws<DomainException>(() => Rules.Connection(new CameraConnection { SourceType = source, Protocol = protocol, StreamUri = uri }));
    [Fact] public void SnapshotMustBeHttp() => Assert.Throws<DomainException>(() => Rules.Connection(new CameraConnection { SourceType = "LIVE", Protocol = "RTSP", StreamUri = "rtsp://camera/main", SnapshotUri = "file:///C:/secret" }));
    [Fact]
    public void ValidatesWarrantyAndPlacement()
    {
        var c = new Camera { InstalledAt = DateTime.UtcNow, WarrantyExpiresAt = DateTime.UtcNow.AddYears(1) };
        Rules.Camera(c);
        c.MapX = .5m;
        Assert.Throws<DomainException>(() => Rules.Camera(c));
        c.MapY = .5m;
        c.MapRotationDeg = 360;
        Assert.Throws<DomainException>(() => Rules.Camera(c));
        c.MapRotationDeg = 0;
        c.WarrantyExpiresAt = c.InstalledAt.Value.AddDays(-1);
        Assert.Throws<DomainException>(() => Rules.Camera(c));
    }
    [Fact]
    public void ValidatesRequiredAndOptionalText()
    {
        Assert.Equal("hello", Rules.Text(" hello ", 10, "name"));
        Assert.Throws<DomainException>(() => Rules.Text(" ", 10, "name"));
        Assert.Throws<DomainException>(() => Rules.Text("long", 2, "name"));
        Rules.Optional(null, 1, "name");
        Assert.Throws<DomainException>(() => Rules.Optional("long", 2, "name"));
    }
    [Fact]
    public void HealthTransitionsRequireRecovery()
    {
        var e = new CameraHealthEvent();
        Rules.Investigate(e);
        e.Status = "INVESTIGATING";
        Assert.Throws<DomainException>(() => Rules.Investigate(e));
        Assert.Throws<DomainException>(() => Rules.Resolve(e, recovered: false));
        Rules.Resolve(e, recovered: true);
        e.Status = "RESOLVED";
        Assert.Throws<DomainException>(() => Rules.Resolve(e, recovered: true));
    }
    [Fact]
    public void HashesUserPassword()
    {
        var passwords = new PasswordService();
        var hash = passwords.Hash("a-long-password");
        Assert.NotEqual("a-long-password", hash);
        Assert.True(passwords.Verify(hash, "a-long-password"));
        Assert.False(passwords.Verify(hash, "incorrect"));
        Assert.False(passwords.Verify("invalid hash", "password"));
    }
    [Fact]
    public void EncryptsCameraCredentialRecoverably()
    {
        var secrets = new CredentialProtector(new EphemeralDataProtectionProvider());
        var value = secrets.Protect("camera-secret");
        Assert.DoesNotContain("camera-secret", value);
        Assert.Equal("camera-secret", secrets.Unprotect(value));
    }
    private sealed class ClearFrameAnalyzer : IFrameHealthAnalyzer
    {
        public Task<FrameAnalysisResult> Analyze(PreviewFrame frame, CancellationToken ct) => Task.FromResult(new FrameAnalysisResult(true, []));
    }
}
