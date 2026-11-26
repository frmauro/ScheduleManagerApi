using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Application.Auth;

namespace Scheduling.Api.Controllers;

/// <summary>Cadastro e autenticação (FR-008, FR-009).</summary>
[Route(RoutePrefix + "/auth")]
[Tags("Auth")]
public sealed class AuthController(AuthService auth) : ApiControllerBase
{
    /// <summary>Auto-cadastro de cliente; retorna o token de acesso.</summary>
    [HttpPost("register/customer")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<AuthResponse>> RegisterCustomer(RegisterCustomerRequest request, CancellationToken cancellationToken)
    {
        var response = await auth.RegisterCustomerAsync(request, cancellationToken);
        return Created($"/{RoutePrefix}/customers/me", response);
    }

    /// <summary>Auto-cadastro de prestador com perfil inicial (política de reservas padrão).</summary>
    [HttpPost("register/provider")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<AuthResponse>> RegisterProvider(RegisterProviderRequest request, CancellationToken cancellationToken)
    {
        var response = await auth.RegisterProviderAsync(request, cancellationToken);
        return Created($"/{RoutePrefix}/providers/me", response);
    }

    /// <summary>Autenticação por e-mail e senha. Falhas retornam 401 genérico.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken) =>
        auth.LoginAsync(request, cancellationToken);

    /// <summary>Dados do usuário autenticado.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public Task<CurrentUserDto> Me(CancellationToken cancellationToken) => auth.GetCurrentAsync(cancellationToken);
}
