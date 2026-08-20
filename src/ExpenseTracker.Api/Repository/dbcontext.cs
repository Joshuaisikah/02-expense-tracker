using ExpenseTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Repository;

public class ExpenseTrackerDbContext(DbContextOptions<ExpenseTrackerDbContext> options)
    : DbContext(options)
{
    public DbSet<Expense> Expenses { get; set; }
    public DbSet<Category> Categories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---------- Category ----------
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(c => c.Id);

            // Optional but recommended
            entity.HasIndex(c => c.Name).IsUnique();
        });

        // ---------- Expense ----------
        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasKey(e => e.Id);

            // ---- THE RELATIONSHIP ----
            entity
                .HasOne(e => e.Category) // Expense has one Category
                .WithMany(c => c.Expenses) // Category has many Expenses
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict); // ← prevents cascade delete
            //   (so you can return 409 yourself)
        });
    }
}
