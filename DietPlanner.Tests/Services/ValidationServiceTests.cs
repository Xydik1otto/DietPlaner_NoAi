using DietPlanner.Services;
using DietPlanner.Tests.Infrastructure;

namespace DietPlanner.Tests.Services;

public sealed class ValidationServiceTests
{
    private readonly ValidationService _service = new(new StubLocalizationService());

    [Fact]
    public void ValidateRequiredText_RejectsBlankAndTooLong()
    {
        Assert.NotEmpty(_service.ValidateRequiredText("  ", "Name", 10));
        Assert.NotEmpty(_service.ValidateRequiredText("12345678901", "Name", 10));
        Assert.Empty(_service.ValidateRequiredText("Valid", "Name", 10));
    }

    [Fact]
    public void ValidateDecimal_AcceptsCommaAndRejectsRange()
    {
        Assert.Empty(_service.ValidateDecimal("12,5", "Calories", 0m, 20m));
        Assert.NotEmpty(_service.ValidateDecimal("21", "Calories", 0m, 20m));
        Assert.NotEmpty(_service.ValidateDecimal("abc", "Calories", 0m, 20m));
    }

    [Fact]
    public void ValidateEmail_RejectsMalformedAddress()
    {
        Assert.Empty(_service.ValidateEmail("test@example.com"));
        Assert.NotEmpty(_service.ValidateEmail("not-an-email"));
    }

    [Fact]
    public void ValidatePassword_RequiresAtLeastEightCharacters()
    {
        Assert.NotEmpty(_service.ValidatePassword("short"));
        Assert.Empty(_service.ValidatePassword("long-enough"));
    }
}
