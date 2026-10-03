using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
namespace Supermarket.Tests;
[Collection("SqlApi")]
[Trait("Category", "SqlIntegration")]
public sealed class PublicMonitoringContractApiFlowTests(SqlApiFixture fixture,ITestOutputHelper output)
{
    [Fact]
    public async Task SwaggerContainsPublicMonitoringContractsWithoutPrivateOwnerOrConnection()
    {
        using var client=fixture.Factory.CreateClient();
        using var document=JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var root=document.RootElement;
        var paths=root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/cameras/{id}/monitoring-runtime",out var runtime));
        Assert.True(runtime.GetProperty("get").GetProperty("responses").TryGetProperty("503",out _));
        Assert.True(paths.TryGetProperty("/api/cameras/{id}/incidents",out var feed));
        Assert.Contains(feed.GetProperty("get").GetProperty("parameters").EnumerateArray(),p=>p.GetProperty("name").GetString()=="afterIncidentId");
        var schemas=root.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("MonitoringCameraRuntimeView",out var view));
        Assert.DoesNotContain("ownerId",view.GetRawText()); Assert.DoesNotContain("streamUri",view.GetRawText());
        Assert.True(schemas.GetProperty("AiPreviewStatusView").GetProperty("properties").TryGetProperty("purpose",out _));
        Assert.True(schemas.GetProperty("IncidentTypeView").GetProperty("properties").TryGetProperty("measurementOptions",out _));
        // Opt-in generated documentation capture; normal tests emit no document.
        if(Environment.GetEnvironmentVariable("MF02_PRINT_OPENAPI")=="1") output.WriteLine("MF02_OPENAPI:"+JsonSerializer.Serialize(root));
    }
}
