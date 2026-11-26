using FluentValidation;
using Scheduling.Application.Common.Validation;
using Scheduling.Application.Providers;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Users;

namespace Scheduling.Application.Auth;

public sealed record RegisterCustomerRequest(string Name, string Email, string Password, string Phone);

public sealed record RegisterProviderRequest(
    string Name,
    string Email,
    string Password,
    string DisplayName,
    string? Description,
    ServiceCategory Category,
    string? ContactEmail,
    string ContactPhone,
    AddressDto Address,
    string? TimeZoneId);

public sealed record LoginRequest(string Email, string Password);

public sealed record CurrentUserDto(Guid Id, string Name, string Email, Role Role, Guid? ProviderId, Guid? CustomerId);

public sealed record AuthResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, CurrentUserDto User);

public sealed class RegisterCustomerRequestValidator : AbstractValidator<RegisterCustomerRequest>
{
    public RegisterCustomerRequestValidator()
    {
        RuleFor(x => x.Name).PersonName();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).Password();
        RuleFor(x => x.Phone).E164Phone();
    }
}

public sealed class RegisterProviderRequestValidator : AbstractValidator<RegisterProviderRequest>
{
    public RegisterProviderRequestValidator()
    {
        RuleFor(x => x.Name).PersonName();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).Password();
        RuleFor(x => x.DisplayName).PersonName();
        RuleFor(x => x.Description).MaximumLength(ValidationLimits.ProviderDescriptionMaxLength);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.ContactEmail!).ValidEmail().When(x => x.ContactEmail is not null);
        RuleFor(x => x.ContactPhone).E164Phone();
        RuleFor(x => x.Address).NotNull().SetValidator(new AddressDtoValidator());
        RuleFor(x => x.TimeZoneId).IanaTimeZone();
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(ValidationLimits.EmailMaxLength);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(ValidationLimits.PasswordMaxLength);
    }
}
