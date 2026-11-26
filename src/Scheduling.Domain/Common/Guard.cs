using System.Text.RegularExpressions;

namespace Scheduling.Domain.Common;

/// <summary>Verificações defensivas de invariantes (a validação de entrada principal fica nos validators da Application).</summary>
public static partial class Guard
{
    public static string Text(string? value, string field, int minLength, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length < minLength || trimmed.Length > maxLength)
        {
            throw Invalid($"'{field}' deve ter entre {minLength} e {maxLength} caracteres.");
        }

        return trimmed;
    }

    public static string? OptionalText(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw Invalid($"'{field}' deve ter no máximo {maxLength} caracteres.");
        }

        return trimmed;
    }

    public static string Email(string? value, string field)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length is 0 or > ValidationLimits.EmailMaxLength || !EmailRegex().IsMatch(normalized))
        {
            throw Invalid($"'{field}' deve ser um e-mail válido.");
        }

        return normalized;
    }

    public static string Phone(string? value, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (!PhoneRegex().IsMatch(trimmed))
        {
            throw Invalid($"'{field}' deve estar no formato E.164 (ex.: +5511999998888).");
        }

        return trimmed;
    }

    public static int Range(int value, string field, int min, int max, int multipleOf = 1)
    {
        if (value < min || value > max || value % multipleOf != 0)
        {
            var multiple = multipleOf > 1 ? $", múltiplo de {multipleOf}" : string.Empty;
            throw Invalid($"'{field}' deve estar entre {min} e {max}{multiple}.");
        }

        return value;
    }

    public static DomainException Invalid(string message) => new(ErrorCodes.ValidationFailed, message);

    public static bool IsValidPhone(string? value) => value is not null && PhoneRegex().IsMatch(value);

    public static bool IsValidEmail(string? value) => value is not null && EmailRegex().IsMatch(value);

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
