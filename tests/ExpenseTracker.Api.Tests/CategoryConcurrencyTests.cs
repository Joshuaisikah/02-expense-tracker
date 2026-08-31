using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Api.Dtos;
using Xunit;

namespace ExpenseTracker.Api.Tests;

[Collection("Postgres")]
public class CategoryConcurrencyTests
{
    private readonly PostgresApiFixture _fixture;

    public CategoryConcurrencyTests(PostgresApiFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// PROVES: DeleteCategoryAsync's fix for the delete-vs-delete race (DbUpdateConcurrencyException
    /// caught and mapped to NotFound) actually holds under real concurrent load, not just in a
    /// single manual curl.
    ///
    /// Given    an empty category with no expenses
    /// When     30 concurrent DELETE requests hit it at once
    /// Then     exactly one succeeds (204), the rest report 404, and — critically — none return 500.
    ///
    /// Before the fix (bare FindAsync + Remove + SaveChangesAsync, no catch for
    /// DbUpdateConcurrencyException), this test reproduced the failure documented earlier:
    /// 1 × 204, and the remaining 29 × 500 (DbUpdateConcurrencyException: expected to affect 1
    /// row(s), but actually affected 0).
    /// </summary>
    [Fact]
    public async Task ConcurrentDeletes_OnSameCategory_ExactlyOneSucceeds_NoneAre500()
    {
        var categoryId = await _fixture.SeedCategoryAsync($"Race-{Guid.NewGuid():N}");

        var tasks = Enumerable.Range(0, 30)
            .Select(_ => _fixture.Client.DeleteAsync($"/api/categories/{categoryId}"));

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.NoContent);
        var notFoundCount = responses.Count(r => r.StatusCode == HttpStatusCode.NotFound);
        var serverErrorCount = responses.Count(r => (int)r.StatusCode >= 500);

        Assert.Equal(1, successCount);
        Assert.Equal(29, notFoundCount);
        Assert.Equal(0, serverErrorCount);
    }

    /// <summary>
    /// PROVES: creating an expense against a category that gets deleted out from under the insert
    /// (the FK-violation TOCTOU path) never surfaces as a 500 — the write itself is the check.
    ///
    /// Given    a category that exists right now
    /// When     one request races to delete it while another races to insert an expense into it
    /// Then     every response is either a success or a clean 400/404 — never a 500.
    ///
    /// This test can't force the exact interleaving deterministically (that would require pausing
    /// EF Core mid-SaveChanges, out of scope for an integration test), so it fires a large batch of
    /// both operations concurrently and asserts on the aggregate outcome — no 500, ever — rather
    /// than on a specific single interleaving.
    /// </summary>
    [Fact]
    public async Task ConcurrentDeleteAndExpenseInsert_OnSameCategory_NeverProduces500()
    {
        var categoryId = await _fixture.SeedCategoryAsync($"Race-{Guid.NewGuid():N}");

        var deleteTasks = Enumerable.Range(0, 10)
            .Select(_ => _fixture.Client.DeleteAsync($"/api/categories/{categoryId}"));

        var insertTasks = Enumerable.Range(0, 10)
            .Select(_ => _fixture.Client.PostAsJsonAsync("/api/expenses", new
            {
                name = "Race expense",
                amount = 12.50m,
                date = DateOnly.FromDateTime(DateTime.UtcNow),
                categoryId
            }));
https://contabo.com/en/vps/
        var allResponses = await Task.WhenAll(deleteTasks.Concat(insertTasks));

        var serverErrorCount = allResponses.Count(r => (int)r.StatusCode >= 500);
        Assert.Equal(0, serverErrorCount);
    }

    /// <summary>
    /// PROVES: concurrent category creation with the same name never produces a 500 — exactly one
    /// insert should win (201), and every other should be told the name is taken (409), because
    /// IsUniqueViolation (SqlState 23505) catches the database's own unique-constraint rejection.
    ///
    /// This is the TOCTOU-shaped fix, proven under real concurrency rather than two sequential
    /// manual curls: no pre-check for an existing name happens anywhere in CreateAsync — the
    /// insert attempt is the check.
    /// </summary>
    [Fact]
    public async Task ConcurrentCreates_WithSameName_ExactlyOneSucceeds_RestAreConflict_NoneAre500()
    {
        var name = $"Duplicate-{Guid.NewGuid():N}";

        var tasks = Enumerable.Range(0, 25)
            .Select(_ => _fixture.Client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name)));

        var responses = await Task.WhenAll(tasks);

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        var serverErrorCount = responses.Count(r => (int)r.StatusCode >= 500);

        Assert.Equal(1, createdCount);
        Assert.Equal(24, conflictCount);
        Assert.Equal(0, serverErrorCount);
    }

    /// <summary>
    /// DOCUMENTS A KNOWN, DELIBERATELY UNFIXED GAP — see gaps.md and the README's
    /// "What I'd do next" section. This test is expected to demonstrate data loss, not prevent it.
    ///
    /// Given    a category that still exists (not deleted) throughout the whole test
    /// When     two concurrent PUT requests rename it to two different names
    /// Then     both requests currently report 200 — but only one name is actually stored, because
    ///          no concurrency token (xmin) is configured, so EF Core's row-count check is
    ///          satisfied by either writer (each UPDATE affects exactly 1 row, whether or not the
    ///          row's content had already changed underneath it).
    ///
    /// If this test starts failing its final assertion (i.e. both PUTs stop returning 200, or the
    /// stored name becomes something other than one of the two submitted values), that's a sign
    /// the concurrency-token fix (xmin + IsRowVersion()) has been applied — at which point this
    /// test should be rewritten to assert a 409/Conflict on the loser instead, and this comment
    /// block should be deleted along with the corresponding gaps.md entry.
    /// </summary>
    [Fact]
    public async Task ConcurrentRenames_OnSameCategory_CurrentlyLosesOneWriteSilently()
    {
        var categoryId = await _fixture.SeedCategoryAsync($"Original-{Guid.NewGuid():N}");

        var nameA = "Alice-" + Guid.NewGuid().ToString("N")[..8];
        var nameB = "Bob-" + Guid.NewGuid().ToString("N")[..8];

        var putA = _fixture.Client.PutAsJsonAsync($"/api/categories/{categoryId}", new UpdateCategoryRequest(nameA));
        var putB = _fixture.Client.PutAsJsonAsync($"/api/categories/{categoryId}", new UpdateCategoryRequest(nameB));

        var responses = await Task.WhenAll(putA, putB);

        // Known-gap behavior today: EF Core has no concurrency token configured for Category, so
        // both concurrent UPDATEs against an existing row succeed at the database level regardless
        // of write order — there is currently no mechanism that produces a 409 here.
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var finalResponse = await _fixture.Client.GetAsync($"/api/categories/{categoryId}");
        var final = await finalResponse.Content.ReadFromJsonAsync<CategoryDto>();

        // Exactly one of the two writes survives — never both, and never something else entirely.
        Assert.True(final!.Name == nameA || final.Name == nameB);
    }
}
