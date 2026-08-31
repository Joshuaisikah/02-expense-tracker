using ExpenseTracker.Api.Repository;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace ExpenseTracker.Api.Tests;

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

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresApiFixture> { }
