using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Supermarket.Api;

public sealed class ReverseProxyOptions
{
    public bool Enabled { get; set; }
    public string[] KnownProxies { get; set; } = [];
}

public static class ReverseProxy
{
    public static IServiceCollection AddBackendReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReverseProxyOptions>().Bind(configuration.GetSection("ReverseProxy"))
            .Validate(o => !o.Enabled || (o.KnownProxies.Length > 0 && o.KnownProxies.All(ip => IPAddress.TryParse(ip, out _))),
                "Set ReverseProxy:KnownProxies to the IP addresses of trusted proxies before enabling forwarded headers.")
            .ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ReverseProxyOptions>>((headers, proxy) =>
        {
            if (!proxy.Value.Enabled) return;
            headers.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            headers.ForwardLimit = 1;
            headers.KnownProxies.Clear();
            headers.KnownIPNetworks.Clear();
            foreach (var ip in proxy.Value.KnownProxies) headers.KnownProxies.Add(IPAddress.Parse(ip));
        });
        return services;
    }

    public static IApplicationBuilder UseBackendReverseProxy(this IApplicationBuilder app)
    {
        if (app.ApplicationServices.GetRequiredService<IOptions<ReverseProxyOptions>>().Value.Enabled)
            app.UseForwardedHeaders();
        return app;
    }
}
