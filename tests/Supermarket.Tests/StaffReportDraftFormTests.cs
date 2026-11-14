using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Supermarket.Tests;

public sealed class StaffReportDraftFormTests
{
    // An empty note must reach the controller rules, not fail implicit [Required] model validation (400).
    [Fact]
    public async Task EmptyNoteIsNotAModelValidationError()
    {
        await using var factory = new SetupOverviewTests.Factory(new MonitoringSetupTests.MemoryStore());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "STAFF");
        using var form = new MultipartFormDataContent { { new StringContent(""), "note" } };
        var response = await client.PostAsync("/api/staff-reports/draft", form);
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("REPORT_INPUT_REQUIRED", problem.GetProperty("code").GetString());
    }
}
