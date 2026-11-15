using System;
using AppointMe.Service.Implementation;
using AwesomeAssertions;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

/// <summary>S5: a logo is recognized by its first bytes, never by its file name.</summary>
public class LogoImageFormatTests
{
    // A real 1x1 PNG.
    private static readonly byte[] RealPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public void ARealPng_IsAPng() =>
        LogoImageFormat.DetectExtension(RealPng).Should().Be(".png");

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg")]                  // JFIF
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 }, ".jpg")]                  // Exif
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, ".png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, ".webp")] // RIFF....WEBP
    public void SupportedFormats_AreRecognizedByTheirSignature(byte[] header, string expected) =>
        LogoImageFormat.DetectExtension(header).Should().Be(expected);

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF, 0xD8 })]                                       // too short for JPEG
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A })]         // PNG signature cut off
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]               // GIF89a
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 })]                           // "<svg"
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 })] // RIFF....WAVE
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42 })]       // RIFF....WEB (short)
    [InlineData(new byte[] { 0x42, 0x4D })]                                       // BMP
    public void AnythingElse_IsNotRecognized(byte[] content) =>
        LogoImageFormat.DetectExtension(content).Should().BeNull();
}
