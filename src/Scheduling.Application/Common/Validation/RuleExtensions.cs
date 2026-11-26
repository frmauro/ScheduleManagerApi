using System.Globalization;
using FluentValidation;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;

namespace Scheduling.Application.Common.Validation;

/// <summary>Regras reutilizáveis de validação de entrada.</summary>
public static class RuleExtensions
{
    public const string LocalTimeFormat = "HH:mm";

    public static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Length(ValidationLimits.NameMinLength, ValidationLimits.NameMaxLength);

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(ValidationLimits.EmailMaxLength).Must(Guard.IsValidEmail).WithMessage("'{PropertyName}' deve ser um e-mail válido.");

    public static IRuleBuilderOptions<T, string> E164Phone<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(ValidationLimits.PhoneMaxLength).Must(Guard.IsValidPhone)
            .WithMessage("'{PropertyName}' deve estar no formato E.164 (ex.: +5511999998888).");

    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Length(ValidationLimits.PasswordMinLength, ValidationLimits.PasswordMaxLength)
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage("'{PropertyName}' deve conter ao menos uma letra e um dígito.");

    public static IRuleBuilderOptions<T, string?> IanaTimeZone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(ValidationLimits.TimeZoneIdMaxLength)
            .Must(tz => tz is null || Provider.IsValidTimeZone(tz))
            .WithMessage("'{PropertyName}' deve ser um fuso IANA válido (ex.: America/Sao_Paulo).");

    public static IRuleBuilderOptions<T, string?> LocalTime<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => v is null || TryParseLocalTime(v, out _))
            .WithMessage("'{PropertyName}' deve estar no formato HH:mm.");

    public static bool TryParseLocalTime(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, LocalTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static TimeOnly ParseLocalTime(string value) => TimeOnly.ParseExact(value, LocalTimeFormat, CultureInfo.InvariantCulture);

    public static string FormatLocalTime(TimeOnly time) => time.ToString(LocalTimeFormat, CultureInfo.InvariantCulture);
}
