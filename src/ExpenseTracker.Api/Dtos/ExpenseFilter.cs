namespace ExpenseTracker.Api.Dtos;

public class ExpenseFilter
{
    public int? CategoryId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    private int _page { get; set; } = 1;
    public int Page
    {
        get => _page;
        set => _page = Math.Max(value, 1);
    }
    private int _pageSize { get; set; } = 10;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 100);
    }
}
