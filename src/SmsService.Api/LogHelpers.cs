namespace SmsService.Api;

public static class LogHelpers
{
    /// <summary>Privacy: logs show only the last 3 digits of a phone number.</summary>
    public static string PhoneSuffix(string phone) =>
        phone.Length <= 3 ? "***" : "***" + phone[^3..];
}
