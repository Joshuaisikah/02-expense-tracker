namespace ExpenseTracker.Api.Dtos;

public record ExpenseDto(
    int Id,
    string Name,
    decimal Amount,
    DateOnly Date,
    int CategoryId,
    string CategoryName
);
