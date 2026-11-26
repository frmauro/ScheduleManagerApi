using FluentValidation;

namespace Scheduling.Application.Common.Paging;

/// <summary>Parâmetros padronizados de paginação (FR-029).</summary>
public class PageQuery
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public int Page { get; set; } = DefaultPage;

    public int PageSize { get; set; } = DefaultPageSize;

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>Envelope de listagens paginadas.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) => new(Items.Select(selector).ToList(), Page, PageSize, TotalCount);
}

/// <summary>Regras de paginação reutilizadas por todos os validators de consulta.</summary>
public abstract class PageQueryValidator<T> : AbstractValidator<T>
    where T : PageQuery
{
    protected PageQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageQuery.MaxPageSize);
    }
}

public sealed class PageQueryDefaultValidator : PageQueryValidator<PageQuery>;
