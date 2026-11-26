namespace Scheduling.Domain.Common;

/// <summary>Violação de invariante ou regra de negócio do domínio. <see cref="Code"/> vem de <see cref="ErrorCodes"/>.</summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
