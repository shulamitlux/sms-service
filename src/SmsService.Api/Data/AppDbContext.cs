using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SmsService.Api.Domain;

namespace SmsService.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeePhone> EmployeePhones => Set<EmployeePhone>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Employee>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300).IsRequired();
            // The natural key from the requirements (there is no employee number).
            e.HasIndex(x => new { x.FirstName, x.LastName, x.Address }).IsUnique();
            e.HasMany(x => x.Phones).WithOne().HasForeignKey(p => p.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<EmployeePhone>(e =>
        {
            e.Property(x => x.PhoneE164).HasMaxLength(16).IsUnicode(false).IsRequired();
            e.HasIndex(x => new { x.EmployeeId, x.PhoneE164 }).IsUnique();
            // Not unique: the same phone can belong to several employees.
            e.HasIndex(x => x.PhoneE164);
        });

        model.Entity<Message>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.To).HasMaxLength(16).IsUnicode(false).IsRequired();
            e.Property(x => x.Body).HasMaxLength(670).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.ProviderMessageId).HasMaxLength(100);
            e.Property(x => x.Attempts).HasDefaultValue(0);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();

            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt }).IsDescending(false, true);
            e.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        });
    }
}

/// <summary>Used only by `dotnet ef migrations add`; it never connects to a database.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=design-time-only;Database=SmsService")
            .Options);
}
