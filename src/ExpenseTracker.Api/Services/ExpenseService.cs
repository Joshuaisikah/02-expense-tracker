using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Repository;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public enum UpdateExpenseResult
{
    Updated,
    NotFound,
    CategoryNotFound,
}

public class ExpenseService
{
    private readonly ExpenseTrackerDbContext _context;

    public ExpenseService(ExpenseTrackerDbContext context)
    {
        _context = context;
    }

    public async Task<ExpenseDto?> CreateAsync(CreateExpenseRequest request, CancellationToken ct)
    {
        var expense = new Expense
        {
            Name = request.Name,
            Amount = request.Amount,
            Date = request.Date,
            CategoryId = request.CategoryId,
        };

        _context.Expenses.Add(expense);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            return null; // categoryId doesn't refer to an existing category — caught at the moment of truth, not a stale pre-check
        }

        return await GetByIdAsync(expense.Id, ct);
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
            .ThenBy(e => e.Id)
            .Skip((filter.EffectivePage - 1) * filter.EffectivePageSize)
            .Take(filter.EffectivePageSize)
            .Select(e => new ExpenseDto(
                e.Id,
                e.Name,
                e.Amount,
                e.Date,
                e.CategoryId,
                e.Category.Name
            ))
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling((double)total / filter.EffectivePageSize);

        return new ExpensePageResponse(
            expenses,
            new Meta(total, filter.EffectivePage, filter.EffectivePageSize, totalPages)
        );
    }

    public async Task<(UpdateExpenseResult Result, ExpenseDto? Expense)> UpdateExpenseAsync(
        int expenseId,
        UpdateExpenseRequest request,
        CancellationToken ct
    )
    {
        var expense = await _context.Expenses.FindAsync(new object[] { expenseId }, ct);
        if (expense is null)
        {
            return (UpdateExpenseResult.NotFound, null);
        }

        expense.Name = request.Name;
        expense.Amount = request.Amount;
        expense.Date = request.Date;
        expense.CategoryId = request.CategoryId;

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (UpdateExpenseResult.NotFound, null); // deleted between our read and our write
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            return (UpdateExpenseResult.CategoryNotFound, null); // categoryId invalid, caught at the write
        }

        var updated = await GetByIdAsync(expense.Id, ct);
        return (UpdateExpenseResult.Updated, updated);
    }

    public async Task<bool> DeleteAsync(int expenseId, CancellationToken ct)
    {
        var expense = await _context.Expenses.FindAsync(new object[] { expenseId }, ct);
        if (expense is null)
        {
            return false;
        }

        _context.Expenses.Remove(expense);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
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

    private static bool IsForeignKeyViolation(DbUpdateException ex)
    {
        return ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23503";
    }
}
