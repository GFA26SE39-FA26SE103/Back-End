using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;

/// <summary>Database V2 made UserAccount.code NOT NULL and unique without a default.</summary>
public sealed class AccountCodeTests
{
    [Theory]
    // user_id / code pairs backfilled by FA26SE103_Database_V2.sql
    [InlineData("21C8C55F-6BA9-4502-A40A-1599D84FF7FF", "USR-21C8C55F6BA94502")]
    [InlineData("c951d4cc-dccc-4cb6-a38d-50b8fe534579", "USR-C951D4CCDCCC4CB6")]
    public void CodeMatchesTheDatabaseV2Backfill(string userId, string code) => Assert.Equal(code, UserAccount.CodeFor(Guid.Parse(userId)));

    [Fact]
    public async Task CreatedAccountsGetAUniqueCode()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var staff = new Role { RoleId = Guid.NewGuid(), Name = "STAFF" };
        store.Values.Add(staff);
        var accounts = new Accounts(store, new Admin(), new Passwords(), new Tokens());
        var first = await accounts.Create(new CreateUserRequest("a@test.local", "password-12345", "Staff A", staff.RoleId), default);
        var second = await accounts.Create(new CreateUserRequest("b@test.local", "password-12345", "Staff B", staff.RoleId), default);
        var users = store.Values.OfType<UserAccount>().ToDictionary(u => u.UserId);
        Assert.Equal(UserAccount.CodeFor(first.UserId), users[first.UserId].Code);
        Assert.NotEqual(users[first.UserId].Code, users[second.UserId].Code);
    }

    private sealed class Admin : ICurrentUser { public Guid UserId { get; } = Guid.NewGuid(); public string Role => "ADMIN"; }
    private sealed class Passwords : IPasswordService { public string Hash(string password) => "hash"; public bool Verify(string hash, string password) => false; }
    private sealed class Tokens : ITokenIssuer { public LoginResponse Issue(UserView user) => throw new NotSupportedException(); }
}
