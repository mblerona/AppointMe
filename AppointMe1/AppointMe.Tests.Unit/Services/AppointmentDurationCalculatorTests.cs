using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Service.Calendar;
using AwesomeAssertions;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

public class AppointmentDurationCalculatorTests
{
    private static List<ServiceOffering> Services(params int[] minutes) =>
        // 0 stands for a service without a duration, because attributes can't hold a null in a params array.
        minutes.Select(m => new ServiceOffering { DurationMinutes = m == 0 ? null : m }).ToList();

    [Theory]
    [InlineData(30, 30, new[] { 30 })]             // exactly one slot
    [InlineData(30, 60, new[] { 30, 30 })]         // sum that is already on the grid
    [InlineData(30, 30, new[] { 5 })]              // shorter than a slot rounds up to one
    [InlineData(30, 60, new[] { 31 })]             // one minute over rounds up
    [InlineData(30, 90, new[] { 45, 30 })]         // sum rounds up, 75 -> 90
    [InlineData(30, 60, new[] { 25, 25 })]         // the total is rounded, not each service (each would give 60 too)
    [InlineData(15, 30, new[] { 10, 10 })]         // the total is rounded, not each service (each would be 15 + 15)
    [InlineData(20, 40, new[] { 7, 7, 7 })]        // 21 -> 40; rounding each would give 60
    [InlineData(30, 30, new[] { 0 })]              // no duration counts as one slot
    [InlineData(30, 90, new[] { 0, 45 })]          // 30 + 45 = 75 -> 90
    [InlineData(45, 495, new[] { 480 })]           // the largest service on a 45-minute grid
    public void Calculate_SumsAndRoundsUpToTheSlotLength(int slot, int expected, int[] minutes)
    {
        AppointmentDurationCalculator.Calculate(slot, Services(minutes)).Should().Be(expected);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void Calculate_WithoutServices_IsOneSlot(int slot)
    {
        AppointmentDurationCalculator.Calculate(slot, new List<ServiceOffering>()).Should().Be(slot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public void Calculate_WithAnInvalidSlotLength_FallsBackTo30(int slot)
    {
        AppointmentDurationCalculator.Calculate(slot, new List<ServiceOffering>()).Should().Be(30);
        AppointmentDurationCalculator.Calculate(slot, Services(0)).Should().Be(30);
        AppointmentDurationCalculator.Calculate(slot, Services(45)).Should().Be(60);
    }

    [Theory]
    [InlineData(5, "5 min")]
    [InlineData(59, "59 min")]
    [InlineData(60, "1 h")]
    [InlineData(90, "1 h 30 min")]
    [InlineData(120, "2 h")]
    [InlineData(495, "8 h 15 min")]
    public void DurationDisplay_ReadsNaturally(int minutes, string expected)
    {
        new AppointmentDTO { DurationMinutes = minutes }.DurationDisplay.Should().Be(expected);
    }
}
