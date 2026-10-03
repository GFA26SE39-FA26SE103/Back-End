using System.Linq.Expressions;
using Supermarket.Domain;
namespace Supermarket.Application;

public interface ISetupStore
{
    Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new();
    Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new();
    Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new();
    Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new();
    Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new();
    Task<TResult> Transaction<TResult>(Func<Task<TResult>> work, CancellationToken ct = default);
}
public interface IClock
{
    DateTime UtcNow
    {
        get;
    }
}
public interface ICurrentUser
{
    Guid UserId
    {
        get;
    }
    string Role
    {
        get;
    }
}
public interface IPasswordService
{
    string Hash(string password); bool Verify(string hash, string password);
}
public interface ITokenIssuer
{
    LoginResponse Issue(UserView user);
}
public interface ICredentialProtector
{
    string Protect(string secret); string Unprotect(string secret);
}
public sealed record ProbeResult(bool Success, string Code);
public sealed record PreviewFrame(byte[] Bytes, string ContentType);
public interface ICameraStream
{
    Task<ProbeResult> Test(CameraConnection connection, CancellationToken ct);
    Task<PreviewFrame> Preview(CameraConnection connection, CancellationToken ct);
}
public sealed class ApplicationException(string code, string message, int status = 409) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
public static class UseCase
{
    public static void Admin(ICurrentUser user)
    {
        if (user.Role != "ADMIN")
            throw new ApplicationException("FORBIDDEN", "Admin permission is required.", 403);
    }
    /// <summary>Read-only store, floor, zone and camera data used by operational screens.</summary>
    public static void Viewer(ICurrentUser user)
    {
        if (user.Role is not ("ADMIN" or "OPERATOR" or "MANAGER"))
            throw new ApplicationException("FORBIDDEN", "Operational read access is required.", 403);
    }
    /// <summary>Live AI camera view; it uses a limited AI-service session, so only Admin and Operator.</summary>
    public static void LiveView(ICurrentUser user)
    {
        if (user.Role is not ("ADMIN" or "OPERATOR"))
            throw new ApplicationException("FORBIDDEN", "Live camera access is required.", 403);
    }
    public static T Found<T>(T? entity) where T : class => entity ?? throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
    public static void Unique(bool duplicate)
    {
        if (duplicate)
            throw new ApplicationException("DUPLICATE", "A resource with these identifiers already exists.");
    }
}
