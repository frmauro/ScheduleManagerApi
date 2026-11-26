using Scheduling.Domain.Common;

namespace Scheduling.Domain.Users;

/// <summary>Identidade de acesso. E-mail único, normalizado em minúsculas.</summary>
public sealed class User : AggregateRoot
{
    private User()
    {
        Name = string.Empty;
        Email = string.Empty;
        PasswordHash = string.Empty;
    }

    public string Name { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public Role Role { get; private set; }

    public bool IsActive { get; private set; }

    public static User Create(string name, string email, Role role)
    {
        return new User
        {
            Name = Guard.Text(name, "name", ValidationLimits.NameMinLength, ValidationLimits.NameMaxLength),
            Email = Guard.Email(email, "email"),
            Role = role,
            IsActive = true,
        };
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > ValidationLimits.PasswordHashMaxLength)
        {
            throw Guard.Invalid("Hash de senha inválido.");
        }

        PasswordHash = passwordHash;
    }

    public void UpdateProfile(string name, string email)
    {
        Name = Guard.Text(name, "name", ValidationLimits.NameMinLength, ValidationLimits.NameMaxLength);
        Email = Guard.Email(email, "email");
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
