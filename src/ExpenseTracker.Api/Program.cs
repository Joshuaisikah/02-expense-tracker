using ExpenseTracker.Api.Dtos;
using ExpenseTracker.Api.Repository;
using ExpenseTracker.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register DbContext with Npgsql provider
builder.Services.AddDbContext<ExpenseTrackerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

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
    async (CreateCategoryRequest request, CategoryService categoryService, CancellationToken ct) =>
    {
        var category = await categoryService.CreateAsync(request, ct);
        return Results.Created($"/api/categories/{category.Id}", category);
    }
);
app.MapPost(
    "/api/expenses",
    async (CreateExpenseRequest request, ExpenseService expenseService, CancellationToken ct) =>
    {
        var expense = await expenseService.CreateAsync(request, ct);
        return expense is not null
            ? Results.Created($"/api/expenses/{expense.Id}", expense)
            : Results.BadRequest(new { message = "Invalid category." });
    }
);
app.MapGet(
    "/api/expenses",
    async (ExpenseFilter filter, ExpenseService expenseService, CancellationToken ct) =>
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
app.Run();
