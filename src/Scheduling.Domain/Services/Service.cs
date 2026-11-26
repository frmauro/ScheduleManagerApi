using Scheduling.Domain.Common;

namespace Scheduling.Domain.Services;

/// <summary>Serviço oferecido por um prestador. Nome único por prestador (case-insensitive).</summary>
public sealed class Service : AggregateRoot
{
    private Service()
    {
        Name = string.Empty;
    }

    public Guid ProviderId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public int DurationMinutes { get; private set; }

    public decimal Price { get; private set; }

    public bool IsActive { get; private set; }

    public static Service Create(Guid providerId, string name, string? description, int durationMinutes, decimal price)
    {
        var service = new Service { ProviderId = providerId, IsActive = true };
        service.Update(name, description, durationMinutes, price);
        return service;
    }

    public void Update(string name, string? description, int durationMinutes, decimal price)
    {
        if (price < ValidationLimits.ServicePriceMin || price > ValidationLimits.ServicePriceMax || decimal.Round(price, 2) != price)
        {
            throw Guard.Invalid($"'price' deve estar entre {ValidationLimits.ServicePriceMin} e {ValidationLimits.ServicePriceMax} com até 2 casas decimais.");
        }

        Name = Guard.Text(name, "name", ValidationLimits.ServiceNameMinLength, ValidationLimits.ServiceNameMaxLength);
        Description = Guard.OptionalText(description, "description", ValidationLimits.ServiceDescriptionMaxLength);
        DurationMinutes = Guard.Range(durationMinutes, "durationMinutes", ValidationLimits.ServiceDurationMin, ValidationLimits.ServiceDurationMax, ValidationLimits.ServiceDurationMultiple);
        Price = price;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
