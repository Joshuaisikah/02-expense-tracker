namespace ExpenseTracker.Api.Dtos;

public class ExpenseFilter
{
    public int? CategoryId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public int EffectivePage => Math.Max(Page ?? 1, 1);
    public int EffectivePageSize => Math.Clamp(PageSize ?? 10, 1, 100);
}
