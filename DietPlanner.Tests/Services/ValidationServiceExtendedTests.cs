using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class ValidationServiceExtendedTests
{
    private readonly ValidationService _service = new(new StubLocalizationService());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ValidateRequiredText_RejectsNullEmptyAndWhitespace(string? value)
    {
        Assert.NotEmpty(_service.ValidateRequiredText(value, "Name", 10));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("1234567890")]
    [InlineData("  abc  ")]
    [InlineData("ї" )]
    public void ValidateRequiredText_AcceptsValuesAtOrBelowLimit(string value)
    {
        Assert.Empty(_service.ValidateRequiredText(value, "Name", 10));
    }

    [Theory]
    [InlineData("12345678901")]
    [InlineData("abcdefghijk")]
    [InlineData("          abcdefghijk")]
    public void ValidateRequiredText_RejectsValuesAboveLimit(string value)
    {
        Assert.NotEmpty(_service.ValidateRequiredText(value, "Name", 10));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("12.5")]
    [InlineData("12,5")]
    [InlineData("0.001")]
    [InlineData("999999.99")]
    [InlineData("1e2")]
    public void ValidateDecimal_AcceptsValidRepresentations(string value)
    {
        Assert.Empty(_service.ValidateDecimal(value, "Value", 0m, 1_000_000m));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateDecimal_RejectsMissingValue(string? value)
    {
        Assert.NotEmpty(_service.ValidateDecimal(value, "Value", 0m, 100m));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12abc")]
    [InlineData("1,2,3")]
    [InlineData("1..2")]
    [InlineData("--1")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("1/2")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,000,000")]
    public void ValidateDecimal_RejectsMalformedValues(string value)
    {
        Assert.NotEmpty(_service.ValidateDecimal(value, "Value", 0m, 100m));
    }

    [Theory]
    [InlineData("-0.01", 0d, 100d)]
    [InlineData("100.01", 0d, 100d)]
    [InlineData("-100", -50d, 50d)]
    [InlineData("50.01", -50d, 50d)]
    [InlineData("1001", 0d, 1000d)]
    public void ValidateDecimal_RejectsValuesOutsideInclusiveRange(string value, double min, double max)
    {
        Assert.NotEmpty(_service.ValidateDecimal(value, "Value", (decimal)min, (decimal)max));
    }

    [Theory]
    [InlineData("0", 0d, 100d)]
    [InlineData("100", 0d, 100d)]
    [InlineData("-50", -50d, 50d)]
    [InlineData("50", -50d, 50d)]
    public void ValidateDecimal_AcceptsRangeBoundaries(string value, double min, double max)
    {
        Assert.Empty(_service.ValidateDecimal(value, "Value", (decimal)min, (decimal)max));
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name@example.com")]
    [InlineData("user+tag@example.co.uk")]
    [InlineData("user_name@example.io")]
    [InlineData("user-name@example.org")]
    [InlineData("  test@example.com  ")]
    public void ValidateEmail_AcceptsCommonValidAddresses(string value)
    {
        Assert.Empty(_service.ValidateEmail(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateEmail_RejectsMissingValue(string? value)
    {
        Assert.NotEmpty(_service.ValidateEmail(value));
    }

    [Fact]
    public void ValidateEmail_RejectsDisplayNameInsteadOfMailbox()
    {
        Assert.NotEmpty(_service.ValidateEmail("John Doe <user@example.com>"));
    }

    [Fact]
    public void ValidateEmail_RejectsConsecutiveDotsInLocalPart()
    {
        Assert.NotEmpty(_service.ValidateEmail("user..name@example.com"));
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@@example.com")]
    [InlineData("user example@example.com")]
    [InlineData("user.@example.com")]
    [InlineData("user@ example.com")]
    [InlineData("user@example,com")]
    [InlineData("user<>@example.com")]
    public void ValidateEmail_RejectsKnownMalformedAddresses(string value)
    {
        Assert.NotEmpty(_service.ValidateEmail(value));
    }

    [Theory]
    [InlineData("aaaaaaaa", true)]
    [InlineData("12345678", true)]
    [InlineData("123456789", true)]
    [InlineData("one-two-three", true)]
    [InlineData("short", false)]
    [InlineData("1234567", false)]
    public void ValidatePassword_EnforcesMinimumLength(string value, bool valid)
    {
        var errors = _service.ValidatePassword(value);
        Assert.Equal(valid, errors.Count == 0);
    }

    [Fact]
    public void ValidatePassword_RejectsMissingValue()
    {
        Assert.NotEmpty(_service.ValidatePassword(null));
        Assert.NotEmpty(_service.ValidatePassword(""));
        Assert.NotEmpty(_service.ValidatePassword("   "));
    }

    [Fact]
    public void ValidatePassword_RejectsMoreThan128Characters()
    {
        var password = new string('x', 129);
        Assert.NotEmpty(_service.ValidatePassword(password));
    }

    [Fact]
    public void ValidatePassword_AcceptsExactly128Characters()
    {
        var password = new string('x', 128);
        Assert.Empty(_service.ValidatePassword(password));
    }
}
