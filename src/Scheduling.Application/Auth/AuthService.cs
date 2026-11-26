using Scheduling.Application.Abstractions;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Domain.Common;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Users;

namespace Scheduling.Application.Auth;

/// <summary>Registro e autenticação (FR-008, FR-009, FR-011, FR-013).</summary>
public sealed class AuthService(
    IUserRepository users,
    ICustomerRepository customers,
    IProviderRepository providers,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    private const string TokenType = "Bearer";

    public async Task<AuthResponse> RegisterCustomerAsync(RegisterCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var user = await CreateUserAsync(request.Name, request.Email, request.Password, Role.Customer, cancellationToken);
        var customer = Customer.Create(user.Id, user.Name, user.Email, request.Phone);
        customers.Add(customer);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BuildResponse(user, null, customer.Id);
    }

    public async Task<AuthResponse> RegisterProviderAsync(RegisterProviderRequest request, CancellationToken cancellationToken = default)
    {
        var user = await CreateUserAsync(request.Name, request.Email, request.Password, Role.Provider, cancellationToken);
        var provider = Provider.Create(
            user.Id,
            request.DisplayName,
            request.Description,
            request.Category,
            request.ContactEmail ?? user.Email,
            request.ContactPhone,
            request.Address.ToDomain(),
            request.TimeZoneId);
        providers.Add(provider);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BuildResponse(user, provider.Id, null);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken);

        // Mensagem genérica: não revela se o e-mail existe nem se a conta está inativa (FR-013).
        if (user is null || !user.IsActive || !passwordHasher.Verify(user, request.Password))
        {
            throw new UnauthenticatedException(ErrorCodes.AuthInvalidCredentials, "E-mail ou senha inválidos.");
        }

        var (providerId, customerId) = await ResolveProfileIdsAsync(user, cancellationToken);
        return BuildResponse(user, providerId, customerId);
    }

    public async Task<CurrentUserDto> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new UnauthenticatedException(ErrorCodes.AuthUnauthenticated, "Usuário não encontrado.");
        return new CurrentUserDto(user.Id, user.Name, user.Email, user.Role, currentUser.ProviderId, currentUser.CustomerId);
    }

    private async Task<User> CreateUserAsync(string name, string email, string password, Role role, CancellationToken cancellationToken)
    {
        var user = User.Create(name, email, role);
        if (await users.EmailExistsAsync(user.Email, null, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.UserEmailTaken, "Já existe uma conta com este e-mail.");
        }

        user.SetPasswordHash(passwordHasher.Hash(user, password));
        users.Add(user);
        return user;
    }

    private async Task<(Guid? ProviderId, Guid? CustomerId)> ResolveProfileIdsAsync(User user, CancellationToken cancellationToken)
    {
        return user.Role switch
        {
            Role.Provider => ((await providers.GetByUserIdAsync(user.Id, cancellationToken))?.Id, null),
            Role.Customer => (null, (await customers.GetByUserIdAsync(user.Id, cancellationToken))?.Id),
            _ => (null, null),
        };
    }

    private AuthResponse BuildResponse(User user, Guid? providerId, Guid? customerId)
    {
        var token = tokenService.Create(user, providerId, customerId);
        return new AuthResponse(
            token.Token,
            TokenType,
            token.ExpiresAt,
            new CurrentUserDto(user.Id, user.Name, user.Email, user.Role, providerId, customerId));
    }
}
