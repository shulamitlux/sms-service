using System.Text.RegularExpressions;

namespace SmsService.Api.Domain;

/// <summary>
/// An employee. There is no employee number; the natural key is FirstName + LastName + Address
/// (unique index in the DB). Id is an internal surrogate key only.
/// </summary>
public partial class Employee
{
    public int Id { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public List<EmployeePhone> Phones { get; private set; } = [];

    private Employee() { } // for EF Core

    public Employee(string firstName, string lastName, string address)
    {
        FirstName = NormalizeText(firstName);
        LastName = NormalizeText(lastName);
        Address = NormalizeText(address);
    }

    /// <summary>Adds a phone (already in E.164). Returns false if the employee already has it.</summary>
    public bool AddPhone(string phoneE164)
    {
        if (Phones.Any(p => p.PhoneE164 == phoneE164))
            return false;

        Phones.Add(new EmployeePhone(phoneE164));
        return true;
    }

    /// <summary>Trim and collapse inner whitespace, so "ישראל  כהן" and "ישראל כהן" are the same.</summary>
    public static string NormalizeText(string value) =>
        MultipleWhitespace().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleWhitespace();
}
