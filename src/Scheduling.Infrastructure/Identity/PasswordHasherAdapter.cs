using Microsoft.AspNetCore.Identity;
using Scheduling.Domain.Users;
using IAppPasswordHasher = Scheduling.Application.Abstractions.IPasswordHasher;

namespace Scheduling.Infrastructure.Identity;

/// <summary>PBKDF2 versionado do ASP.NET Core Identity, sem adotar o Identity completo (research R-06).</summary>
internal sealed class PasswordHasherAdapter : IAppPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
