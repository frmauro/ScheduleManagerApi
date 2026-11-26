using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Scheduling.Application.Abstractions;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinSigningKeyBytes = 32;

    public string Issuer { get; set; } = "ScheduleManager";

    public string Audience { get; set; } = "ScheduleManager.Api";

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;

    public SymmetricSecurityKey GetSecurityKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

/// <summary>Nomes das claims emitidas no token (research R-06).</summary>
public static class TokenClaims
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string Email = JwtRegisteredClaimNames.Email;
    public const string Role = "role";
    public const string ProviderId = "provider_id";
    public const string CustomerId = "customer_id";
}

internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Create(User user, Guid? providerId, Guid? customerId)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(TokenClaims.Subject, user.Id.ToString()),
            new(TokenClaims.Email, user.Email),
            new(TokenClaims.Role, user.Role.ToString()),
        };
        if (providerId is { } p)
        {
            claims.Add(new Claim(TokenClaims.ProviderId, p.ToString()));
        }

        if (customerId is { } c)
        {
            claims.Add(new Claim(TokenClaims.CustomerId, c.ToString()));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(settings.GetSecurityKey(), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}
