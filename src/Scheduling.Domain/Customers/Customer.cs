using Scheduling.Domain.Common;

namespace Scheduling.Domain.Customers;

/// <summary>Perfil do cliente final (1:1 com User). Nome e e-mail espelham o User.</summary>
public sealed class Customer : AggregateRoot
{
    private Customer()
    {
        Name = string.Empty;
        Email = string.Empty;
        Phone = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string Name { get; private set; }

    public string Email { get; private set; }

    public string Phone { get; private set; }

    public static Customer Create(Guid userId, string name, string email, string phone)
    {
        var customer = new Customer { UserId = userId };
        customer.Update(name, email, phone);
        return customer;
    }

    public void Update(string name, string email, string phone)
    {
        Name = Guard.Text(name, "name", ValidationLimits.NameMinLength, ValidationLimits.NameMaxLength);
        Email = Guard.Email(email, "email");
        Phone = Guard.Phone(phone, "phone");
    }
}
