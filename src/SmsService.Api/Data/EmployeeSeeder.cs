using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmsService.Api.Domain;
using SmsService.Api.Validation;

namespace SmsService.Api.Data;

/// <summary>One row of the seed file: one "instance" of an employee with one phone.</summary>
public sealed record EmployeeSeedRow(string FirstName, string LastName, string Address, string Phone);

/// <summary>
/// Loads employees from seed rows. Rows of the same employee (same name + address, after whitespace
/// normalization) are merged into one employee with several phones. Safe to run on every startup:
/// existing employees and phones are not duplicated.
/// </summary>
public class EmployeeSeeder(AppDbContext db, ILogger<EmployeeSeeder> logger)
{
    public static IReadOnlyList<EmployeeSeedRow> LoadFromFile(string path) =>
        JsonSerializer.Deserialize<List<EmployeeSeedRow>>(File.ReadAllText(path), JsonSerializerOptions.Web) ?? [];

    public async Task SeedAsync(IEnumerable<EmployeeSeedRow> rows, CancellationToken ct = default)
    {
        // The employee list is small, so loading it whole is simpler than per-row lookups.
        var employees = await db.Employees.Include(e => e.Phones).ToListAsync(ct);
        int addedEmployees = 0, addedPhones = 0;

        foreach (var row in rows)
        {
            var phone = PhoneNumberNormalizer.Normalize(row.Phone);
            if (!phone.IsValid)
            {
                logger.LogWarning("Skipping seed row for {FirstName} {LastName}: invalid phone", row.FirstName, row.LastName);
                continue;
            }

            var candidate = new Employee(row.FirstName, row.LastName, row.Address);
            var employee = employees.FirstOrDefault(e => IsSameEmployee(e, candidate));
            if (employee is null)
            {
                employee = candidate;
                employees.Add(employee);
                db.Employees.Add(employee);
                addedEmployees++;
            }

            if (employee.AddPhone(phone.E164!))
                addedPhones++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Employee seed done: {AddedEmployees} employees and {AddedPhones} phones added",
            addedEmployees, addedPhones);
    }

    // Case-insensitive, matching SQL Server's default collation used by the unique index.
    private static bool IsSameEmployee(Employee a, Employee b) =>
        string.Equals(a.FirstName, b.FirstName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.LastName, b.LastName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Address, b.Address, StringComparison.OrdinalIgnoreCase);
}
