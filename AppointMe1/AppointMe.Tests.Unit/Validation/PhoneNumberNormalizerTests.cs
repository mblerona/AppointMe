using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Validation;
using AwesomeAssertions;
using Xunit;

namespace AppointMe.Tests.Unit.Validation;

public class PhoneNumberNormalizerTests
{
    // ---------------- TryNormalize ----------------

    [Theory]
    [InlineData("070 123 456")]
    [InlineData("070123456")]
    [InlineData("070-123-456")]
    [InlineData("(070) 123 456")]
    [InlineData("070/123-456")]
    [InlineData("  070 123 456  ")]
    [InlineData("+389 70 123 456")]
    [InlineData("+38970123456")]
    [InlineData("00389 70 123 456")]
    public void MacedonianMobile_InAnyCommonFormat_BecomesE164(string input)
    {
        PhoneNumberNormalizer.TryNormalize(input, out var e164).Should().BeTrue();
        e164.Should().Be("+38970123456");
    }

    [Theory]
    [InlineData("02 3123 456", "+38923123456")]      // Skopje landline
    [InlineData("075 555 444", "+38975555444")]      // another mobile prefix
    [InlineData("+49 30 1234567", "+49301234567")]   // Germany
    [InlineData("0049 30 1234567", "+49301234567")]  // 00 = international prefix in MK
    [InlineData("+44 20 7946 0958", "+442079460958")] // United Kingdom
    public void OtherValidNumbers_BecomeE164(string input, string expected)
    {
        PhoneNumberNormalizer.TryNormalize(input, out var e164).Should().BeTrue();
        e164.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("070 12")]
    [InlineData("070 123 456 789 000")]
    [InlineData("not a phone")]
    [InlineData("+999 123 456 789")]
    public void InvalidInput_IsRejected(string? input)
    {
        PhoneNumberNormalizer.TryNormalize(input, out var e164).Should().BeFalse();
        e164.Should().BeEmpty();
    }

    // ---------------- NormalizeRequired / NormalizeOptional ----------------

    [Fact]
    public void NormalizeRequired_Valid_ReturnsE164()
    {
        PhoneNumberNormalizer.NormalizeRequired("070 123 456", "PhoneNumber").Should().Be("+38970123456");
    }

    [Theory]
    [InlineData(null, "Phone number is required.")]
    [InlineData("  ", "Phone number is required.")]
    [InlineData("123", PhoneNumberNormalizer.InvalidMessage)]
    public void NormalizeRequired_Missing_OrInvalid_ThrowsForTheField(string? input, string message)
    {
        var act = () => PhoneNumberNormalizer.NormalizeRequired(input, "PhoneNumber");

        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Message.Should().Be(message);
        ex.PropertyName.Should().Be("PhoneNumber");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeOptional_Empty_ReturnsNull(string? input)
    {
        PhoneNumberNormalizer.NormalizeOptional(input, "SecondPhoneNumber").Should().BeNull();
    }

    [Fact]
    public void NormalizeOptional_Invalid_ThrowsForTheField()
    {
        var act = () => PhoneNumberNormalizer.NormalizeOptional("12", "SecondPhoneNumber");

        act.Should().Throw<BusinessRuleException>().Which.PropertyName.Should().Be("SecondPhoneNumber");
    }

    // ---------------- FormatForDisplay ----------------

    [Theory]
    [InlineData("+38970123456", "070 123 456")]
    [InlineData("+38975555444", "075 555 444")]
    [InlineData("070123456", "070 123 456")]          // older data without country code
    public void MacedonianNumbers_AreShownInNationalFormat(string stored, string expected)
    {
        PhoneNumberNormalizer.FormatForDisplay(stored).Should().Be(expected);
    }

    [Theory]
    [InlineData("+49301234567", "+49 30 1234567")]
    [InlineData("+442079460958", "+44 20 7946 0958")]
    public void ForeignNumbers_AreShownInInternationalFormat(string stored, string expected)
    {
        PhoneNumberNormalizer.FormatForDisplay(stored).Should().Be(expected);
    }

    [Fact]
    public void SkopjeLandline_IsShownInNationalFormat_StartingWithZero()
    {
        var shown = PhoneNumberNormalizer.FormatForDisplay("+38923123456");

        shown.Should().StartWith("02 ");
        shown.Replace(" ", "").Should().Be("023123456");
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("123", "123")]            // invalid older data is shown unchanged
    [InlineData(" call me ", "call me")]
    public void EmptyOrUnparsable_IsShownAsIs(string? stored, string expected)
    {
        PhoneNumberNormalizer.FormatForDisplay(stored).Should().Be(expected);
    }

    [Fact]
    public void DisplayFormat_RoundTripsToTheSameE164()
    {
        foreach (var stored in new[] { "+38970123456", "+38923123456", "+49301234567", "+442079460958" })
        {
            PhoneNumberNormalizer.TryNormalize(PhoneNumberNormalizer.FormatForDisplay(stored), out var again).Should().BeTrue();
            again.Should().Be(stored, "editing a number shown in display format must not change it");
        }
    }

    // ---------------- ValidPhoneNumberAttribute ----------------

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("070 123 456", true)]
    [InlineData("+49 30 1234567", true)]
    [InlineData("123", false)]
    [InlineData("abc", false)]
    public void Attribute_AcceptsEmptyAndValidNumbers(string? value, bool expected)
    {
        new ValidPhoneNumberAttribute().IsValid(value).Should().Be(expected);
    }

    [Fact]
    public void Attribute_RejectsNonStringValues_AndUsesTheUserMessage()
    {
        var attribute = new ValidPhoneNumberAttribute();

        attribute.IsValid(70123456).Should().BeFalse();
        attribute.FormatErrorMessage("PhoneNumber").Should().Be(PhoneNumberNormalizer.InvalidMessage);
    }
}
