using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Supermarket.Application;
namespace Supermarket.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<object> hasher = new();
    public string Hash(string password) => hasher.HashPassword(this, password);
    public bool Verify(string hash, string password)
    {
        try
        {
            return hasher.VerifyHashedPassword(this, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException) { return false; }
    }
}
public sealed class CredentialProtector(IDataProtectionProvider provider) : ICredentialProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("FA26SE103.CameraCredentials.v1");
    public string Protect(string secret) => protector.Protect(secret);
    public string Unprotect(string secret) => protector.Unprotect(secret);
}
