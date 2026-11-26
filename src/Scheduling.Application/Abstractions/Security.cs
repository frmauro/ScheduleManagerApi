using Scheduling.Domain.Users;

namespace Scheduling.Application.Abstractions;

/// <summary>Usuário da requisição atual (derivado das claims do token).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    Role Role { get; }

    Guid? ProviderId { get; }

    Guid? CustomerId { get; }
}

public interface IPasswordHasher
{
    string Hash(User user, string password);

    bool Verify(User user, string password);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken Create(User user, Guid? providerId, Guid? customerId);
}
