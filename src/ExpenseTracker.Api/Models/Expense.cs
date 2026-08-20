namespace ExpenseTracker.Api.Models;

//one cartegory can have many expenses
//one expense can have one category

public class Expense
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}
