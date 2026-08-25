using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Repository;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public class ExpenseService
{
    private readonly ExpenseTrackerDbContext _context;

    public ExpenseService(ExpenseTrackerDbContext context)
    {
        _context = context;
    }

    public async Task<ExpensePageResponse> GetPagedAsync(ExpenseFilter filter, CancellationToken ct)
    {
        var query = _context.Expenses.AsNoTracking().AsQueryable();
        if (filter.CategoryId.HasValue)
        {
            query = query.Where(e => e.CategoryId == filter.CategoryId.Value);
        }
        if (filter.From is not null)
        {
            query = query.Where(e => e.Date >= filter.From.Value);
        }
        if (filter.To is not null)
        {
            query = query.Where(e => e.Date <= filter.To.Value);
        }
        if (filter.MinAmount is not null)
        {
            query = query.Where(e => e.Amount >= filter.MinAmount.Value);
        }
        if (filter.MaxAmount is not null)
        {
            query = query.Where(e => e.Amount <= filter.MaxAmount.Value);
        }
        var total = await query.CountAsync(ct);
        var expenses = await query
            .OrderByDescending(e => e.Date)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(e => new ExpenseDto(
                e.Id,
                e.Name,
                e.Amount,
                e.Date,
                e.CategoryId,
                e.Category.Name
            ))
            .ToListAsync(ct);
        var totalPages = (int)Math.Ceiling((double)total / filter.PageSize);
        return new ExpensePageResponse(
            expenses,
            new Meta(total, filter.Page, filter.PageSize, totalPages)
        );
    }

    public async Task<ExpenseDto?> CreateAsync(CreateExpenseRequest request, CancellationToken ct)
    {
        var categoryExists = await _context.Categories.AnyAsync(
            c => c.Id == request.CategoryId,
            ct
        );
        if (!categoryExists)
        {
            return null;
        }
        var expense = new Expense
        {
            Name = request.Name,
            Amount = request.Amount,
            Date = request.Date,
            CategoryId = request.CategoryId,
        };
        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync(ct);
        return await GetByIdAsync(expense.Id, ct);
    }

    public async Task<bool> DeleteAsync(int expenseId, CancellationToken ct)
    {
        var expense = await _context.Expenses.FindAsync(new object[] { expenseId }, ct);
        if (expense is null)
        {
            return false;
        }
        _context.Expenses.Remove(expense);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ExpenseDto?> GetByIdAsync(int expenseId, CancellationToken ct)
    {
        return await _context
            .Expenses.AsNoTracking()
            .Where(e => e.Id == expenseId)
            .Select(e => new ExpenseDto(
                e.Id,
                e.Name,
                e.Amount,
                e.Date,
                e.CategoryId,
                e.Category.Name
            ))
            .FirstOrDefaultAsync(ct);
    }
}
