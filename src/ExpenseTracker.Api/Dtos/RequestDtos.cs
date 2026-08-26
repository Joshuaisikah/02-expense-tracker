using System.ComponentModel.DataAnnotations;

namespace ExpenseTracker.Api.Dtos;

public record CreateCategoryRequest(
    [property: Required(ErrorMessage = "Name is required.")]
    [property: MaxLength(100, ErrorMessage = "Name must be at most 100 characters.")]
        string Name
);

public record UpdateCategoryRequest(
    [property: Required(ErrorMessage = "Name is required.")]
    [property: MaxLength(100, ErrorMessage = "Name must be at most 100 characters.")]
        string Name
);

public record CreateExpenseRequest(
    [property: Required(ErrorMessage = "Name is required.")]
    [property: MaxLength(200, ErrorMessage = "Name must be at most 200 characters.")]
        string Name,
    [property: Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        decimal Amount,
    DateOnly Date,
    [property: Range(
        1,
        int.MaxValue,
        ErrorMessage = "CategoryId must be a valid category reference."
    )]
        int CategoryId
);

public record UpdateExpenseRequest(
    [property: Required(ErrorMessage = "Name is required.")]
    [property: MaxLength(200, ErrorMessage = "Name must be at most 200 characters.")]
        string Name,
    [property: Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        decimal Amount,
    DateOnly Date,
    [property: Range(
        1,
        int.MaxValue,
        ErrorMessage = "CategoryId must be a valid category reference."
    )]
        int CategoryId
);
