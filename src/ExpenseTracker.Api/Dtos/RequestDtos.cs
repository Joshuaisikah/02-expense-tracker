namespace ExpenseTracker.Api.Dtos;

public record CreateCategoryRequest(string Name);

public record UpdateCategoryRequest(string Name);

public record CreateExpenseRequest(string Name, decimal Amount, DateOnly Date, int CategoryId);

public record UpdateExpenseRequest(string Name, decimal Amount, DateOnly Date, int CategoryId);
