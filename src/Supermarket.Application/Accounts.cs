using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class Accounts(ISetupStore store, ICurrentUser current, IPasswordService passwords, ITokenIssuer tokens)
{
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken ct)
    {
        string email = request.Email.Trim().ToLowerInvariant();
        var user = (await store.List<UserAccount>(u => u.Email == email, ct)).SingleOrDefault();
        if (user is null || user.Status != "ACTIVE" || !passwords.Verify(user.PasswordHash, request.Password))
            throw new ApplicationException("INVALID_CREDENTIALS", "Email or password is invalid.", 401);
        return tokens.Issue(await View(user, ct));
    }
    public async Task<UserView> Me(CancellationToken ct) => await View(UseCase.Found(await store.Find<UserAccount>(current.UserId, ct)), ct);
    public Task<List<Role>> Roles(CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.List<Role>(ct: ct);
    }
    public async Task<List<UserView>> List(CancellationToken ct)
    {
        UseCase.Admin(current);
        var roles = await store.List<Role>(ct: ct);
        return (await store.List<UserAccount>(ct: ct)).Select(u => new UserView(u.UserId, u.Email, u.FullName, u.RoleId, roles.Single(r => r.RoleId == u.RoleId).Name, u.Status)).ToList();
    }
    public Task<UserView> Create(CreateUserRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var email = Rules.Text(request.Email, 255, "Email").ToLowerInvariant();
            Rules.Require(new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email), "INVALID_EMAIL", "Email is invalid.");
            Rules.Require(request.Password.Length is >= 12 and <= 128, "INVALID_PASSWORD", "Password must contain 12 to 128 characters.");
            UseCase.Unique((await store.List<UserAccount>(u => u.Email == email, ct)).Count > 0);
            UseCase.Found(await store.Find<Role>(request.RoleId, ct));
            var user = new UserAccount { UserId = Guid.NewGuid(), Email = email, FullName = Rules.Text(request.FullName, 150, "FullName"), RoleId = request.RoleId, PasswordHash = passwords.Hash(request.Password), Status = "ACTIVE" };
            await store.Add(user, ct);
            return await View(user, ct);
        }, ct);
    }
    public Task<UserView> Update(Guid id, UpdateUserRequest request, CancellationToken ct) => Change(id, request, null, ct);
    public Task<UserView> Status(Guid id, bool enabled, CancellationToken ct) => Change(id, null, enabled ? "ACTIVE" : "DISABLED", ct);
    private Task<UserView> Change(Guid id, UpdateUserRequest? request, string? status, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var user = UseCase.Found(await store.Find<UserAccount>(id, ct));
            var oldRole = UseCase.Found(await store.Find<Role>(user.RoleId, ct));
            var newRole = UseCase.Found(await store.Find<Role>(request?.RoleId ?? user.RoleId, ct));
            var admin = (await store.List<Role>(r => r.Name == "ADMIN", ct)).Single();
            var activeCount = (await store.List<UserAccount>(u => u.RoleId == admin.RoleId && u.Status == "ACTIVE", ct)).Count;
            Rules.ProtectAdmin(oldRole.Name == "ADMIN" && user.Status == "ACTIVE", newRole.Name == "ADMIN" && (status ?? user.Status) == "ACTIVE", activeCount);
            user.RoleId = newRole.RoleId;
            user.Status = status ?? user.Status;
            if (request is not null)
                user.FullName = Rules.Text(request.FullName, 150, "FullName");
            await store.Update(user, ct);
            return await View(user, ct);
        }, ct);
    }
    private async Task<UserView> View(UserAccount u, CancellationToken ct) => new(u.UserId, u.Email, u.FullName, u.RoleId, UseCase.Found(await store.Find<Role>(u.RoleId, ct)).Name, u.Status);
}
