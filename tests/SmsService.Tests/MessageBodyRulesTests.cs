using FluentAssertions;
using SmsService.Api.Validation;

namespace SmsService.Tests;

public class MessageBodyRulesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_body_is_rejected(string body)
    {
        MessageBodyRules.Validate(body.Trim()).Should().Be(MessageBodyRules.RequiredError);
    }

    [Fact]
    public void Body_of_670_chars_is_valid_and_671_is_rejected()
    {
        MessageBodyRules.Validate(new string('א', 670)).Should().BeNull();
        MessageBodyRules.Validate(new string('א', 671)).Should().Be(MessageBodyRules.TooLongError);
    }

    [Theory]
    [InlineData(160, 1)]
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    public void Gsm_text_segments(int length, int expectedSegments)
    {
        MessageBodyRules.CountSegments(new string('a', length)).Should().Be(expectedSegments);
    }

    [Theory]
    [InlineData(70, 1)]
    [InlineData(71, 2)]
    [InlineData(134, 2)]
    [InlineData(670, 10)]
    public void Hebrew_text_segments(int length, int expectedSegments)
    {
        MessageBodyRules.CountSegments(new string('ש', length)).Should().Be(expectedSegments);
    }

    [Fact]
    public void Gsm_extended_chars_count_as_two()
    {
        // 80 x '€' = 160 septets -> 1 segment; one more char -> 2 segments
        MessageBodyRules.CountSegments(new string('€', 80)).Should().Be(1);
        MessageBodyRules.CountSegments(new string('€', 80) + "a").Should().Be(2);
    }
}
