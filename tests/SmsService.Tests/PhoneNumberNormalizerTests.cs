using FluentAssertions;
using SmsService.Api.Validation;

namespace SmsService.Tests;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("050-1234567")]
    [InlineData("0501234567")]
    [InlineData("+972501234567")]
    [InlineData("972501234567")]
    [InlineData("972 50 123 4567")]
    [InlineData("(050) 123-4567")]
    public void Valid_israeli_mobile_formats_are_normalized_to_E164(string input)
    {
        var result = PhoneNumberNormalizer.Normalize(input);

        result.IsValid.Should().BeTrue();
        result.E164.Should().Be("+972501234567");
    }

    [Fact]
    public void Landline_is_rejected()
    {
        var result = PhoneNumberNormalizer.Normalize("02-6543210");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(PhoneNumberNormalizer.InvalidError);
    }

    [Theory]
    [InlineData(null, PhoneNumberNormalizer.RequiredError)]
    [InlineData("", PhoneNumberNormalizer.RequiredError)]
    [InlineData("   ", PhoneNumberNormalizer.RequiredError)]
    [InlineData("abcdefghij", PhoneNumberNormalizer.InvalidError)]
    [InlineData("050ABCDEFG", PhoneNumberNormalizer.InvalidError)]
    [InlineData("050123", PhoneNumberNormalizer.InvalidError)]
    public void Empty_letters_and_too_short_are_rejected(string? input, string expectedError)
    {
        var result = PhoneNumberNormalizer.Normalize(input);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(expectedError);
    }

    [Fact]
    public void Foreign_number_is_rejected()
    {
        PhoneNumberNormalizer.Normalize("+14155550100").IsValid.Should().BeFalse();
    }
}
