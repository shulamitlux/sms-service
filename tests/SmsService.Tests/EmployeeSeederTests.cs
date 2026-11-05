using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmsService.Api.Data;
using SmsService.Tests.Infrastructure;

namespace SmsService.Tests;

[Collection(SqlServerCollection.Name)]
public class EmployeeSeederTests(SqlServerFixture sql)
{
    private static async Task SeedAsync(string connectionString, params EmployeeSeedRow[] rows)
    {
        await using var db = SqlServerFixture.CreateContext(connectionString);
        await new EmployeeSeeder(db, NullLogger<EmployeeSeeder>.Instance).SeedAsync(rows);
    }

    [Fact]
    public async Task Three_instances_of_same_employee_with_a_duplicate_phone_become_one_employee_with_two_phones()
    {
        var cs = await sql.CreateDatabaseAsync();

        await SeedAsync(cs,
            new("ישראל", "כהן", "הרצל 10, ראשון לציון", "050-1234567"),
            new("ישראל", "כהן", "הרצל 10, ראשון לציון", "052-7654321"),
            new(" ישראל ", "כהן", "הרצל  10,  ראשון לציון", "+972501234567")); // same person, same phone, messy spacing

        await using var db = SqlServerFixture.CreateContext(cs);
        var employees = await db.Employees.Include(e => e.Phones).ToListAsync();
        employees.Should().ContainSingle();
        employees[0].Phones.Select(p => p.PhoneE164).Should()
            .BeEquivalentTo(["+972501234567", "+972527654321"]);
    }

    [Fact]
    public async Task Same_phone_for_two_different_employees_is_kept_for_both()
    {
        var cs = await sql.CreateDatabaseAsync();

        await SeedAsync(cs,
            new("משה", "לוי", "הנשיא 3, בית דגן", "053-3333333"),
            new("יעקב", "לוי", "הנשיא 3, בית דגן", "053-3333333"));

        await using var db = SqlServerFixture.CreateContext(cs);
        (await db.EmployeePhones.CountAsync(p => p.PhoneE164 == "+972533333333")).Should().Be(2);
        (await db.Employees.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Running_the_seed_twice_does_not_duplicate_data()
    {
        var cs = await sql.CreateDatabaseAsync();
        EmployeeSeedRow[] rows = [new("דוד", "מזרחי", "דרך המכבים 40, מודיעין", "058-4444444")];

        await SeedAsync(cs, rows);
        await SeedAsync(cs, rows);

        await using var db = SqlServerFixture.CreateContext(cs);
        (await db.Employees.CountAsync()).Should().Be(1);
        (await db.EmployeePhones.CountAsync()).Should().Be(1);
    }
}
