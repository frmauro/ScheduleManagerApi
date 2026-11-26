using System.Text.RegularExpressions;
using Scheduling.Domain.Common;

namespace Scheduling.Domain.Providers;

/// <summary>Endereço do prestador (value object). Todos os campos obrigatórios exceto complemento.</summary>
public sealed partial record Address
{
    private Address(string street, string number, string? complement, string district, string city, string state, string postalCode)
    {
        Street = street;
        Number = number;
        Complement = complement;
        District = district;
        City = city;
        State = state;
        PostalCode = postalCode;
    }

    public string Street { get; }

    public string Number { get; }

    public string? Complement { get; }

    public string District { get; }

    public string City { get; }

    public string State { get; }

    public string PostalCode { get; }

    public static Address Create(string street, string number, string? complement, string district, string city, string state, string postalCode)
    {
        var normalizedState = state?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!StateRegex().IsMatch(normalizedState))
        {
            throw Guard.Invalid("'address.state' deve ser a sigla da UF com 2 letras.");
        }

        var normalizedPostalCode = postalCode?.Trim() ?? string.Empty;
        if (!PostalCodeRegex().IsMatch(normalizedPostalCode))
        {
            throw Guard.Invalid("'address.postalCode' deve conter 8 dígitos.");
        }

        return new Address(
            Guard.Text(street, "address.street", 1, ValidationLimits.StreetMaxLength),
            Guard.Text(number, "address.number", 1, ValidationLimits.NumberMaxLength),
            Guard.OptionalText(complement, "address.complement", ValidationLimits.ComplementMaxLength),
            Guard.Text(district, "address.district", 1, ValidationLimits.DistrictMaxLength),
            Guard.Text(city, "address.city", 1, ValidationLimits.CityMaxLength),
            normalizedState,
            normalizedPostalCode);
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex StateRegex();

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex PostalCodeRegex();
}
