namespace SmsService.Api.Domain;

/// <summary>A phone number of an employee. The same number may belong to several employees.</summary>
public class EmployeePhone
{
    public int Id { get; private set; }
    public int EmployeeId { get; private set; }
    public string PhoneE164 { get; private set; } = null!;

    private EmployeePhone() { } // for EF Core

    public EmployeePhone(string phoneE164) => PhoneE164 = phoneE164;
}
