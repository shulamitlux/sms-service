namespace SmsService.Api.Validation;

/// <summary>Message body length rules and SMS segment calculation.</summary>
public static class MessageBodyRules
{
    public const int MaxLength = 670; // 10 Unicode segments of 67 chars
    public const string RequiredError = "תוכן ההודעה הוא שדה חובה";
    public const string TooLongError = "ההודעה ארוכה מדי (מקסימום 670 תווים)";

    // GSM 03.38 basic character set (1 septet each).
    private const string GsmBasic =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞ\u001BÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?" +
        "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

    // GSM 03.38 extension table: each costs 2 septets (escape + char).
    private const string GsmExtended = "^{}\\[~]|€\f";

    /// <summary>Returns the validation error for the (already trimmed) body, or null if valid.</summary>
    public static string? Validate(string body)
    {
        if (body.Length == 0) return RequiredError;
        if (body.Length > MaxLength) return TooLongError;
        return null;
    }

    public static int CountSegments(string body)
    {
        if (body.Length == 0) return 0;

        var gsmLength = GsmSeptetLength(body);
        if (gsmLength is { } septets)
            return septets <= 160 ? 1 : (int)Math.Ceiling(septets / 153.0);

        // UCS-2 (e.g. Hebrew). string.Length counts UTF-16 code units, which is exactly what UCS-2/UTF-16 SMS uses.
        return body.Length <= 70 ? 1 : (int)Math.Ceiling(body.Length / 67.0);
    }

    /// <summary>Length in GSM-7 septets, or null if the text contains a non-GSM character.</summary>
    private static int? GsmSeptetLength(string body)
    {
        var septets = 0;
        foreach (var c in body)
        {
            if (GsmBasic.Contains(c)) septets += 1;
            else if (GsmExtended.Contains(c)) septets += 2;
            else return null;
        }
        return septets;
    }
}
