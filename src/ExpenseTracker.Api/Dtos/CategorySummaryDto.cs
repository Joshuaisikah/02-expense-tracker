namespace ExpenseTracker.Api.Dtos;

public record CategorySummaryDto(
    int CategoryId,
    string CategoryName,
    int Count,
    decimal Total,
    decimal Average
);
