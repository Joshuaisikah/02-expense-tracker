using ExpenseTracker.Api.Repository;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// Shared fixture for concurrency tests. Boots the real app in-process (WebApplicationFactory)
/// against a real, throwaway Postgres container (Testcontainers) — never against the EF Core
/// in-memory provider, which has no unique constraints, no transactions, and no way to reproduce
/// any of the races this suite exists to catch.
///
/// One fixture instance is shared across all tests in a collection (see [CollectionDefinition]
/// below) so the container only starts/stops once per test run, not once per test — starting a
/// container is slow, and these tests need many concurrent requests, not many containers.
/// </summary>
public class PostgresApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                _postgres.GetConnectionString()
            );
        });

        // Concurrency races that end in a redirect (e.g. the click-count test) must not have
        // HttpClient silently follow that redirect to a real external URL. We only care about
        // the status code and the side effect (ClickCount), never the destination.
        Client = Factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpenseTrackerDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        Factory.Dispose();
    }

    /// <summary>
    /// Every concurrency test needs a clean, known starting row to race against. Deliberately not
    /// shared setup at the fixture level — each test creates its own category/expense so tests can
    /// run in parallel within the collection without racing each other by accident.
    /// </summary>
    public async Task<int> SeedCategoryAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExpenseTrackerDbContext>();
        var category = new Models.Category { Name = name };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }
}

/// <summary>
/// xUnit collection definition — every test class marked [Collection("Postgres")] shares one
/// PostgresApiFixture instance (one container, one factory) instead of each getting its own.
/// </summary>
[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresApiFixture> { }
