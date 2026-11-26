namespace Scheduling.Domain.Common;

/// <summary>Base de todas as entidades: identidade UUID v7 e carimbos de auditoria.</summary>
public abstract class Entity
{
    protected Entity()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; protected set; }

    /// <summary>Preenchido pela persistência no primeiro salvamento (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Atualizado pela persistência a cada salvamento (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetCreated(DateTimeOffset utcNow)
    {
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void SetUpdated(DateTimeOffset utcNow) => UpdatedAt = utcNow;
}

/// <summary>Raiz de agregado: único ponto de entrada para alterar as entidades internas.</summary>
public abstract class AggregateRoot : Entity
{
}
