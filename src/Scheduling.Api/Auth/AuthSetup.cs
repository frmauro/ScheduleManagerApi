using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scheduling.Api.ErrorHandling;
using Scheduling.Application.Abstractions;
using Scheduling.Domain.Common;
using Scheduling.Domain.Users;
using Scheduling.Infrastructure.Identity;

namespace Scheduling.Api.Auth;

/// <summary>Políticas de autorização por perfil (FR-010).</summary>
public static class Policies
{
    public const string CustomerOnly = nameof(CustomerOnly);
    public const string ProviderOnly = nameof(ProviderOnly);
    public const string AdminOnly = nameof(AdminOnly);
    public const string ProviderOrAdmin = nameof(ProviderOrAdmin);
}

/// <summary>Usuário atual a partir das claims do JWT.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.TryParse(Claim(TokenClaims.Subject), out var id) ? id : Guid.Empty;

    public Role Role => Enum.TryParse<Role>(Claim(TokenClaims.Role), out var role) ? role : Role.Customer;

    public Guid? ProviderId => Guid.TryParse(Claim(TokenClaims.ProviderId), out var id) ? id : null;

    public Guid? CustomerId => Guid.TryParse(Claim(TokenClaims.CustomerId), out var id) ? id : null;

    private string? Claim(string type) => accessor.HttpContext?.User.FindFirst(type)?.Value;
}

public static class AuthSetup
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((options, jwt, timeProvider) =>
            {
                var settings = jwt.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = settings.GetSecurityKey(),
                    ValidateIssuerSigningKey = true,
                    NameClaimType = TokenClaims.Subject,
                    RoleClaimType = TokenClaims.Role,
                    ClockSkew = TimeSpan.Zero,

                    // Validação de validade pelo TimeProvider da aplicação (testável com FakeTimeProvider).
                    LifetimeValidator = (notBefore, expires, _, _) =>
                    {
                        var now = timeProvider.GetUtcNow().UtcDateTime;
                        return (notBefore is null || notBefore <= now) && expires is not null && expires > now;
                    },
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        var problem = Problems.Create(context.HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.AuthUnauthenticated,
                            "Não autenticado.", "Token de acesso ausente, inválido ou expirado.");
                        await Problems.WriteAsync(context.HttpContext, problem);
                    },
                    OnForbidden = async context =>
                    {
                        var problem = Problems.Create(context.HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.AuthForbidden,
                            "Acesso negado.", "O perfil autenticado não tem permissão para esta operação.");
                        await Problems.WriteAsync(context.HttpContext, problem);
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.CustomerOnly, p => p.RequireRole(nameof(Role.Customer)))
            .AddPolicy(Policies.ProviderOnly, p => p.RequireRole(nameof(Role.Provider)))
            .AddPolicy(Policies.AdminOnly, p => p.RequireRole(nameof(Role.Admin)))
            .AddPolicy(Policies.ProviderOrAdmin, p => p.RequireRole(nameof(Role.Provider), nameof(Role.Admin)));

        return services;
    }
}
