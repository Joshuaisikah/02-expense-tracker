using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Repository;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Services;

public enum DeleteCategoryResult
{
    Deleted,
    NotFound,
    InUse,
}

public class CategoryService
{
    private readonly ExpenseTrackerDbContext _context;

    public CategoryService(ExpenseTrackerDbContext context)
    {
        _context = context;
    }

    public async Task<List<CategoryDto>> GetAllAsync(CancellationToken ct)
    {
        return await _context.Categories.Select(c => new CategoryDto(c.Id, c.Name)).ToListAsync(ct);
    }

    public async Task<CategoryDto?> GetByIdAsync(int categoryId, CancellationToken ct)
    {
        return await _context
            .Categories.Where(c => c.Id == categoryId)
            .Select(c => new CategoryDto(c.Id, c.Name))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var category = new Category { Name = request.Name };
        _context.Categories.Add(category);
        await _context.SaveChangesAsync(ct);
        return new CategoryDto(category.Id, category.Name);
    }

    public async Task<CategorySummaryDto?> GetCategorySummaryAsync(
        int categoryId,
        CancellationToken ct
    )
    {
        var category = await _context
            .Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null)
        {
            return null;
        }
        var stats = await _context
            .Expenses.Where(e => e.CategoryId == categoryId)
            .GroupBy(e => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(e => e.Amount) })
            .FirstOrDefaultAsync(ct);
        var count = stats?.Count ?? 0;
        var total = stats?.Total ?? 0m;
        var average = count > 0 ? total / count : 0m;
        return new CategorySummaryDto(category.Id, category.Name, count, total, average);
    }

    public async Task<DeleteCategoryResult> DeleteCategoryAsync(
        int categoryId,
        CancellationToken ct
    )
    {
        var category = await _context
            .Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == categoryId, ct);

        if (category is null)
        {
            return DeleteCategoryResult.NotFound;
        }

        _context.Categories.Attach(category);
        _context.Categories.Remove(category);

        try
        {
            await _context.SaveChangesAsync(ct);
            return DeleteCategoryResult.Deleted;
        }
        catch (DbUpdateConcurrencyException)
        {
            return DeleteCategoryResult.NotFound;
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            return DeleteCategoryResult.InUse;
        }
    }

    private static bool IsForeignKeyViolation(DbUpdateException ex)
    {
        return ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23503";
    }
}
