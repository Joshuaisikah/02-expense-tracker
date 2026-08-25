namespace ExpenseTracker.Api.Dtos;

public record ExpensePageResponse(IEnumerable<ExpenseDto> Expenses, Meta Meta);

public record Meta(int Total, int Page, int PageSize, int TotalPages);
