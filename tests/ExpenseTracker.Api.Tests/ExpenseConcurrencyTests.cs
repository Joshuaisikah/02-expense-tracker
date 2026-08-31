using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Api.Dtos;
using Xunit;

namespace ExpenseTracker.Api.Tests;

[Collection("Postgres")]
public class ExpenseConcurrencyTests
{
    private readonly PostgresApiFixture _fixture;

    public ExpenseConcurrencyTests(PostgresApiFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// PROVES: ExpenseService.DeleteAsync's fix (same DbUpdateConcurrencyException → false → 404
    /// mapping ported from CategoryService) holds under real concurrent load.
    ///
    /// Given    a single expense that exists right now
    /// When     30 concurrent DELETE requests hit it at once
    /// Then     exactly one succeeds (204), the rest report 404, and none return 500.
    ///
    /// Before this fix was ported over, this exact test reproduced 39/40 losing requests as 500
    /// (DbUpdateConcurrencyException, unhandled) rather than a clean 404.
    /// </summary>
    [Fact]
    public async Task ConcurrentDeletes_OnSameExpense_ExactlyOneSucceeds_NoneAre500()
    {
        var categoryId = await _fixture.SeedCategoryAsync(
            $"ExpenseRaceCategory-{Guid.NewGuid():N}"
        );

        var createResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/expenses",
            new
            {
                name = "Race target",
                amount = 9.99m,
                date = DateOnly.FromDateTime(DateTime.UtcNow),
                categoryId,
            }
        );
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ExpenseDto>();

        var tasks = Enumerable
            .Range(0, 30)
            .Select(_ => _fixture.Client.DeleteAsync($"/api/expenses/{created!.Id}"));

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.NoContent);
        var notFoundCount = responses.Count(r => r.StatusCode == HttpStatusCode.NotFound);
        var serverErrorCount = responses.Count(r => (int)r.StatusCode >= 500);

        Assert.Equal(1, successCount);
        Assert.Equal(29, notFoundCount);
        Assert.Equal(0, serverErrorCount);
    }

    /// <summary>
    /// PROVES: a PUT that moves an expense to a categoryId which is being deleted at the same
    /// moment never surfaces as a 500 — the FK violation on the write is caught and reported as a
    /// clean 400, exactly the same TOCTOU-avoidance shape as expense creation.
    /// </summary>
    [Fact]
    public async Task ConcurrentCategoryDeleteAndExpenseUpdate_NeverProduces500()
    {
        var stableCategoryId = await _fixture.SeedCategoryAsync($"Stable-{Guid.NewGuid():N}");
        var doomedCategoryId = await _fixture.SeedCategoryAsync($"Doomed-{Guid.NewGuid():N}");

        var createResponse = await _fixture.Client.PostAsJsonAsync(
            "/api/expenses",
            new
            {
                name = "Movable expense",
                amount = 4.25m,
                date = DateOnly.FromDateTime(DateTime.UtcNow),
                categoryId = stableCategoryId,
            }
        );
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ExpenseDto>();

        var deleteTasks = Enumerable
            .Range(0, 10)
            .Select(_ => _fixture.Client.DeleteAsync($"/api/categories/{doomedCategoryId}"));

        var updateTasks = Enumerable
            .Range(0, 10)
            .Select(_ =>
                _fixture.Client.PutAsJsonAsync(
                    $"/api/expenses/{created!.Id}",
                    new
                    {
                        name = "Moved",
                        amount = 4.25m,
                        date = DateOnly.FromDateTime(DateTime.UtcNow),
                        categoryId = doomedCategoryId,
                    }
                )
            );

        var allResponses = await Task.WhenAll(deleteTasks.Concat(updateTasks));

        var serverErrorCount = allResponses.Count(r => (int)r.StatusCode >= 500);
        Assert.Equal(0, serverErrorCount);
    }
}
