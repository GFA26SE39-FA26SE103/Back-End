using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Supermarket.Application;
namespace Supermarket.Api;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "FA26SE103";
    public string Audience { get; set; } = "FA26SE103.Client";
    public string Key { get; set; } = "";
    public int LifetimeMinutes { get; set; } = 60;
}
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    public string Role => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ?? "";
}
public sealed class TokenIssuer(IOptions<JwtOptions> options, IClock clock) : ITokenIssuer
{
    public LoginResponse Issue(UserView user)
    {
        var o = options.Value;
        var expires = clock.UtcNow.AddMinutes(o.LifetimeMinutes);
        var token = new JwtSecurityToken(o.Issuer, o.Audience, [new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), new Claim(ClaimTypes.Role, user.Role)], clock.UtcNow, expires, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires, user);
    }
}
