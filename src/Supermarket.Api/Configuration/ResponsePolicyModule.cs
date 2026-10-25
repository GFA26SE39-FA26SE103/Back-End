using Supermarket.Application;

namespace Supermarket.Api.Configuration;

public static class ResponsePolicyModule
{
    public static IServiceCollection AddResponsePolicyManagement(this IServiceCollection services)
        => services.AddScoped<OperationalResponsePolicies>();
}
