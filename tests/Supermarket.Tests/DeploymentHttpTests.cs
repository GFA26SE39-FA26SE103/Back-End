using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Supermarket.Infrastructure.Ai;
using Xunit;

namespace Supermarket.Tests;

public sealed class DeploymentHttpTests
{
    [Fact]
    public void AiMonitoringBindsThePersonClassOnce()
    {
        using var factory = new DeploymentFactory();
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<AiPreviewOptions>>().Value;
        Assert.Equal([0], options.Classes);
    }

    [Theory]
    [InlineData("192.168.1.243", true, "https", "203.0.113.10")]
    [InlineData("192.168.1.99", true, "http", "192.168.1.99")]
    [InlineData("192.168.1.243", false, "http", "192.168.1.243")]
    [InlineData("::ffff:192.168.1.243", true, "https", "203.0.113.10")]
    public async Task OnlyConfiguredProxyCanSetClientIpAndScheme(string peer, bool enabled, string expectedScheme, string expectedIp)
    {
        await using var factory = new DeploymentFactory(enabled);
        using var client = factory.CreateClient();
        var context = await factory.Server.SendAsync(ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new("supermarket-api-dev.kitsuracloud.com");
            ctx.Request.Path = "/health/live";
            ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
            ctx.Request.Headers["X-Forwarded-Host"] = "attacker.example";
        });
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(expectedScheme, context.Request.Scheme);
        Assert.Equal(expectedIp, context.Connection.RemoteIpAddress!.ToString());
        Assert.Equal("supermarket-api-dev.kitsuracloud.com", context.Request.Host.Value);
    }

    [Fact]
    public async Task LoginRateLimitSeparatesClientsBehindTheTrustedProxy()
    {
        await using var factory = new DeploymentFactory();
        using var client = factory.CreateClient();
        async Task<int> Login(string ip)
        {
            var context = await factory.Server.SendAsync(ctx =>
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.243");
                ctx.Request.Host = new("localhost");
                ctx.Request.Method = "POST";
                ctx.Request.Path = "/api/auth/login";
                ctx.Request.ContentType = "application/json";
                // Invalid input is rejected before account/SQL access.
                var body = System.Text.Encoding.UTF8.GetBytes("{\"email\":\"invalid\",\"password\":\"\"}");
                ctx.Request.Body = new MemoryStream(body);
                ctx.Request.ContentLength = body.Length;
                ctx.Request.Headers["X-Forwarded-For"] = ip;
                ctx.Request.Headers["X-Forwarded-Proto"] = "https";
            });
            return context.Response.StatusCode;
        }
        for (var i = 0; i < 10; i++) Assert.Equal(400, await Login("203.0.113.10"));
        Assert.Equal(429, await Login("203.0.113.10"));
        Assert.Equal(400, await Login("203.0.113.11"));
    }

    [Fact]
    public async Task OneProxyHopDoesNotTrustEarlierForwardedAddresses()
    {
        await using var factory = new DeploymentFactory();
        using var client = factory.CreateClient();
        var context = await factory.Server.SendAsync(ctx =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.243");
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new("localhost");
            ctx.Request.Path = "/health/live";
            ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.99, 203.0.113.10";
            ctx.Request.Headers["X-Forwarded-Proto"] = "http, https";
        });
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("203.0.113.10", context.Connection.RemoteIpAddress!.ToString());
        Assert.Equal("https", context.Request.Scheme);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-ip")]
    public void EnabledProxyRequiresExplicitValidTrustedAddress(string proxy)
    {
        using var factory = new DeploymentFactory(knownProxy: proxy);
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("ReverseProxy:KnownProxies", exception.Message);
    }

    [Theory]
    [InlineData("Staging", true, HttpStatusCode.OK)]
    [InlineData("Staging", false, HttpStatusCode.NotFound)]
    [InlineData("Production", false, HttpStatusCode.NotFound)]
    [InlineData("Development", false, HttpStatusCode.OK)]
    public async Task HostedDevCanEnableSwaggerWithoutDevelopmentDemoRoutes(string environment, bool swagger, HttpStatusCode expected)
    {
        await using var factory = new DeploymentFactory(swagger: swagger, environment: environment);
        using var client = factory.CreateClient();
        Assert.Equal(expected, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        if (environment != "Development")
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/demo/cameras/{Guid.NewGuid()}/state?online=false", null)).StatusCode);
    }

    private sealed class DeploymentFactory(bool enabled = true, string knownProxy = "192.168.1.243", bool swagger = false, string environment = "Staging") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment).ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=127.0.0.1,1;Database=unused;User Id=unused;Password=unused;Connect Timeout=1;TrustServerCertificate=True",
                ["Jwt:Key"] = new string('x', 48),
                ["Bootstrap:Enabled"] = "false",
                ["CameraHealth:Enabled"] = "false",
                ["Monitoring:Enabled"] = "false",
                ["FloorPlan:Provider"] = "Local",
                ["ReverseProxy:Enabled"] = enabled.ToString(),
                ["ReverseProxy:KnownProxies:0"] = knownProxy,
                ["Swagger:Enabled"] = swagger.ToString()
            }));
        }
    }
}
