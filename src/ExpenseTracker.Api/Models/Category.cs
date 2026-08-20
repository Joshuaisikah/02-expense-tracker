namespace ExpenseTracker.Api.Models;

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
