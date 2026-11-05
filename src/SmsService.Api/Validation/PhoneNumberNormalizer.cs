using PhoneNumbers;

namespace SmsService.Api.Validation;

public sealed record PhoneNormalizationResult(bool IsValid, string? E164, string? Error)
{
    public static PhoneNormalizationResult Valid(string e164) => new(true, e164, null);
    public static PhoneNormalizationResult Invalid(string error) => new(false, null, error);
}

/// <summary>Validates an Israeli mobile number and converts it to E.164 (+972501234567).</summary>
public static class PhoneNumberNormalizer
{
    public const string RequiredError = "מספר טלפון הוא שדה חובה";
    public const string InvalidError = "מספר טלפון לא תקין. יש להזין מספר סלולרי ישראלי";

    private const string DefaultRegion = "IL";
    private const int IsraelCountryCode = 972;
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    public static PhoneNormalizationResult Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return PhoneNormalizationResult.Invalid(RequiredError);

        var cleaned = new string(input.Where(c => c is not (' ' or '-' or '(' or ')')).ToArray());

        // Only digits with an optional leading '+'. libphonenumber would otherwise
        // silently convert letters to digits ("vanity numbers").
        var digits = cleaned.StartsWith('+') ? cleaned[1..] : cleaned;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return PhoneNormalizationResult.Invalid(InvalidError);

        // "972501234567" (international without '+'): Israeli national numbers start with 0,
        // so a leading 972 can only be the country code.
        if (!cleaned.StartsWith('+') && cleaned.StartsWith("972"))
            cleaned = "+" + cleaned;

        PhoneNumber number;
        try
        {
            number = Util.Parse(cleaned, DefaultRegion);
        }
        catch (NumberParseException)
        {
            return PhoneNormalizationResult.Invalid(InvalidError);
        }

        // Decision: check the Israeli mobile *range* (05X + 7 digits) instead of Util.IsValidNumber.
        // libphonenumber's allocation tables reject blocks it believes are unassigned
        // (e.g. 050-1xxxxxx, the sample number in the spec), and those tables lag behind reality.
        // Rejecting a real employee's number is worse than letting the provider reject a bad one.
        var isIsraeliMobile = number.CountryCode == IsraelCountryCode
                              && Util.IsPossibleNumberForType(number, PhoneNumberType.MOBILE)
                              && number.NationalNumber is >= 500_000_000 and <= 599_999_999;

        return isIsraeliMobile
            ? PhoneNormalizationResult.Valid(Util.Format(number, PhoneNumberFormat.E164))
            : PhoneNormalizationResult.Invalid(InvalidError);
    }
}
