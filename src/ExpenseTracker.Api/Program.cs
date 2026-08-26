using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Repository;
using ExpenseTracker.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register DbContext with Npgsql provider
builder.Services.AddDbContext<ExpenseTrackerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);
builder.Services.AddValidation();

builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<ExpenseService>();
var app = builder.Build();

app.UseHttpsRedirection();
app.MapGet(
    "/api/categories/{categoryId:int}/summary",
    async (int categoryId, CategoryService categoryService, CancellationToken ct) =>
    {
        var summary = await categoryService.GetCategorySummaryAsync(categoryId, ct);
        return summary is not null ? Results.Ok(summary) : Results.NotFound();
    }
);
app.MapGet(
    "/api/categories",
    async (CategoryService categoryService, CancellationToken ct) =>
    {
        var categories = await categoryService.GetAllAsync(ct);
        return Results.Ok(categories);
    }
);

app.MapGet(
    "/api/categories/{categoryId:int}",
    async (int categoryId, CategoryService categoryService, CancellationToken ct) =>
    {
        var category = await categoryService.GetByIdAsync(categoryId, ct);
        return category is not null ? Results.Ok(category) : Results.NotFound();
    }
);

app.MapPost(
    "/api/categories",
    async (CreateCategoryRequest request, CategoryService service, CancellationToken ct) =>
    {
        var (result, category) = await service.CreateAsync(request, ct);

        return result switch
        {
            CreateCategoryResult.Created => Results.Created(
                $"/api/categories/{category!.Id}",
                category
            ),
            CreateCategoryResult.DuplicateName => Results.Conflict(
                new { message = "A category with this name already exists." }
            ),
            _ => throw new InvalidOperationException(
                $"Unhandled {nameof(CreateCategoryResult)}: {result}"
            ),
        };
    }
);
app.MapPost(
    "/api/expenses",
    async (CreateExpenseRequest request, ExpenseService service, CancellationToken ct) =>
    {
        var expense = await service.CreateAsync(request, ct);
        return expense is not null
            ? Results.Created($"/api/expenses/{expense.Id}", expense)
            : Results.BadRequest(
                new { message = "categoryId does not refer to an existing category." }
            );
    }
);
app.MapGet(
    "/api/expenses",
    async (
        [AsParameters] ExpenseFilter filter,
        ExpenseService expenseService,
        CancellationToken ct
    ) =>
    {
        var expenses = await expenseService.GetPagedAsync(filter, ct);
        return Results.Ok(expenses);
    }
);
app.MapGet(
    "/api/expenses/{expenseId:int}",
    async (int expenseId, ExpenseService expenseService, CancellationToken ct) =>
    {
        var expense = await expenseService.GetByIdAsync(expenseId, ct);
        return expense is not null ? Results.Ok(expense) : Results.NotFound();
    }
);
app.MapDelete(
    "/api/expenses/{expenseId:int}",
    async (int expenseId, ExpenseService expenseService, CancellationToken ct) =>
    {
        var deleted = await expenseService.DeleteAsync(expenseId, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
);
app.MapDelete(
    "/api/categories/{categoryId:int}",
    async (int categoryId, CategoryService categoryService, CancellationToken ct) =>
    {
        var result = await categoryService.DeleteCategoryAsync(categoryId, ct);
        return result switch
        {
            DeleteCategoryResult.Deleted => Results.NoContent(),
            DeleteCategoryResult.NotFound => Results.NotFound(),
            DeleteCategoryResult.InUse => Results.Conflict(
                new { message = "Cannot delete category because it is in use." }
            ),
            _ => Results.StatusCode(500),
        };
    }
);
app.MapPut(
    "/api/categories/{id:int}",
    async (int id, UpdateCategoryRequest request, CategoryService service, CancellationToken ct) =>
    {
        var (result, category) = await service.UpdateCategoryAsync(id, request, ct);
        return result switch
        {
            UpdateCategoryResult.Updated => Results.Ok(category),
            UpdateCategoryResult.NotFound => Results.NotFound(),
            UpdateCategoryResult.DuplicateName => Results.Conflict(
                new { message = "A category with this name already exists." }
            ),
            _ => throw new InvalidOperationException(
                $"Unhandled {nameof(UpdateCategoryResult)}: {result}"
            ),
        };
    }
);

app.MapPut(
    "/api/expenses/{id:int}",
    async (int id, UpdateExpenseRequest request, ExpenseService service, CancellationToken ct) =>
    {
        var (result, expense) = await service.UpdateExpenseAsync(id, request, ct);
        return result switch
        {
            UpdateExpenseResult.Updated => Results.Ok(expense),
            UpdateExpenseResult.NotFound => Results.NotFound(),
            UpdateExpenseResult.CategoryNotFound => Results.BadRequest(
                new { message = "categoryId does not refer to an existing category." }
            ),
            _ => throw new InvalidOperationException(
                $"Unhandled {nameof(UpdateExpenseResult)}: {result}"
            ),
        };
    }
);
app.Run();
