using Supermarket.Infrastructure.Video;
using Xunit;

namespace Supermarket.Tests;

public sealed class CameraStreamFailureClassifierTests
{
    [Theory]
    [InlineData("method DESCRIBE failed: 401 Unauthorized", "AUTHENTICATION_FAILED")]
    [InlineData("Server returned 401 Unauthorized (authorization failed)", "AUTHENTICATION_FAILED")]
    [InlineData("Connection to tcp://192.168.1.198:554 failed: Connection refused", "CONNECTION_REFUSED")]
    [InlineData("Connection timed out", "TIMEOUT")]
    [InlineData("Invalid data found when processing input", "STREAM_UNAVAILABLE")]
    [InlineData("", "STREAM_UNAVAILABLE")]
    public void ClassifiesSanitizedFfmpegDiagnostics(string stderr, string expected)
    {
        Assert.Equal(expected, CameraStreamFailureClassifier.Classify(stderr));
    }

    [Theory]
    [InlineData("AUTHENTICATION_FAILED")]
    [InlineData("TIMEOUT")]
    [InlineData("CONNECTION_REFUSED")]
    [InlineData("STREAM_UNAVAILABLE")]
    public void RecognizesCodesSafeForConnectionResponses(string code)
    {
        Assert.True(CameraStreamFailureClassifier.IsDiagnosticCode(code));
    }
}
