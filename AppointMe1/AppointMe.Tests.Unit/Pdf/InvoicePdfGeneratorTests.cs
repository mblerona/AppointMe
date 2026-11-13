using System;
using System.Collections.Generic;
using AppointMe.Domain.DTO;
using AppointMe.Service.Pdf;
using AwesomeAssertions;
using Xunit;

namespace AppointMe.Tests.Unit.Pdf;

/// <summary>The real QuestPDF generator: the logo it is given ends up in the PDF, and bad image data doesn't break it.</summary>
public class InvoicePdfGeneratorTests
{
    // A valid 1x1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static readonly DateTime GeneratedAt = new(2026, 11, 13, 10, 0, 0);

    private static InvoiceDTO Invoice() => new()
    {
        Id = Guid.NewGuid(),
        InvoiceNumber = "INV-2026-0001",
        BusinessName = "Test Business",
        CustomerName = "Ana Doe",
        IssuedAt = GeneratedAt,
        AppointmentDate = GeneratedAt,
        Lines = new List<InvoiceLineDTO>()
    };

    private readonly InvoicePdfGenerator _sut = new();

    [Fact]
    public void Generate_WithALogo_EmbedsTheImage()
    {
        var invoice = Invoice();

        var withoutLogo = _sut.Generate(invoice, GeneratedAt, logo: null);
        var withLogo = _sut.Generate(invoice, GeneratedAt, Png);

        StartsLikeAPdf(withLogo);
        withLogo.Length.Should().BeGreaterThan(withoutLogo.Length, "the image is added to the document");
    }

    [Theory]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E })] // "<html>"
    [InlineData(new byte[0])]
    public void Generate_WithUnreadableLogoData_StillProducesThePdf_WithoutTheLogo(byte[] logo)
    {
        var invoice = Invoice();

        var result = _sut.Generate(invoice, GeneratedAt, logo);

        StartsLikeAPdf(result);
        result.Length.Should().Be(_sut.Generate(invoice, GeneratedAt, logo: null).Length);
    }

    private static void StartsLikeAPdf(byte[] bytes) =>
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
}
