using Supermarket.Application;
using Supermarket.Domain;
namespace Supermarket.Api;

public static class Bootstrap
{
    public static async Task Run(WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Bootstrap:Enabled"))
            return;
        using var scope = app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISetupStore>();
        var email = app.Configuration["Bootstrap:Email"] ?? throw new InvalidOperationException("Set Bootstrap:Email.");
        var password = app.Configuration["Bootstrap:Password"] ?? throw new InvalidOperationException("Set Bootstrap:Password.");
        Rules.Require(new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email), "INVALID_EMAIL", "Bootstrap email is invalid.");
        Rules.Require(password.Length is >= 12 and <= 128, "INVALID_PASSWORD", "Bootstrap password must be 12 to 128 characters.");
        await store.Transaction(async () =>
        {
            if ((await store.List<UserAccount>()).Count > 0)
                return false;
            var roles = await store.List<Role>();
            var admin = roles.SingleOrDefault(r => r.Name == "ADMIN") ?? throw new InvalidOperationException("Import the approved schema with roles before bootstrap.");
            await store.Add(new UserAccount { UserId = Guid.NewGuid(), RoleId = admin.RoleId, Email = email.Trim().ToLowerInvariant(), FullName = "System Admin", Status = "ACTIVE", PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordService>().Hash(password) });
            return true;
        });
    }
}
