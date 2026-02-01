using System.Text;
using AppointMe.Service.Email;
using AppointMe.Service.Email.Infrastructure;
using FluentAssertions;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Xunit;

namespace AppointMe.Tests.Unit.Email;

public class EmailServiceTests
{
    [Fact]
    public async Task SendEmailAsync_SendsPlainTextEmail_WithExpectedHeadersAndBody()
    {
        // Arrange
        var settings = Options.Create(new MailSettings
        {
            SmtpServer = "smtp.test.local",
            SmtpServerPort = 587,
            EnableSsl = true,
            EmailDisplayName = "AppointMe",
            SmtpUserName = "sender@test.local",
            SmtpPassword = "secret"
        });

        var fakeSmtp = new FakeSmtpClient();
        var factory = new FakeSmtpClientFactory(fakeSmtp);

        var sut = new EmailService(settings, factory);

        var msg = new EmailMessage
        {
            MailTo = "user@example.com",
            Subject = "Hello",
            Content = "This is the body"
        };

        // Act
        await sut.SendEmailAsync(msg);

        // Assert (SMTP flow)
        fakeSmtp.ConnectCalled.Should().BeTrue();
        fakeSmtp.AuthCalled.Should().BeTrue();
        fakeSmtp.SendCalled.Should().BeTrue();
        fakeSmtp.DisconnectCalled.Should().BeTrue();

        fakeSmtp.LastSecureSocketOptions.Should().Be(SecureSocketOptions.StartTls);

        // Assert (message content)
        fakeSmtp.LastMessage.Should().NotBeNull();
        var m = fakeSmtp.LastMessage!;

        m.Subject.Should().Be("Hello");
        m.To.ToString().Should().Contain("user@example.com");
        m.From.ToString().Should().Contain("sender@test.local");

        m.Body.Should().BeOfType<TextPart>();
        var body = (TextPart)m.Body!;
        body.Text.Should().Be("This is the body");
    }

    [Fact]
    public async Task SendEmailWithAttachmentAsync_SendsEmail_WithAttachment()
    {
        // Arrange
        var settings = Options.Create(new MailSettings
        {
            SmtpServer = "smtp.test.local",
            SmtpServerPort = 25,
            EnableSsl = false,
            EmailDisplayName = "AppointMe",
            SmtpUserName = "sender@test.local",
            SmtpPassword = "secret"
        });

        var fakeSmtp = new FakeSmtpClient();
        var factory = new FakeSmtpClientFactory(fakeSmtp);

        var sut = new EmailService(settings, factory);

        var msg = new EmailMessage
        {
            MailTo = "user@example.com",
            Subject = "With attachment",
            Content = "Body text"
        };

        var bytes = Encoding.UTF8.GetBytes("file-content");

        // Act
        await sut.SendEmailWithAttachmentAsync(msg, bytes, "test.txt", "text/plain");

        // Assert: SSL false -> None
        fakeSmtp.LastSecureSocketOptions.Should().Be(SecureSocketOptions.None);

        // Assert: message contains multipart with attachment
        fakeSmtp.LastMessage.Should().NotBeNull();
        var m = fakeSmtp.LastMessage!;

        m.Subject.Should().Be("With attachment");
        m.Body.Should().BeOfType<Multipart>();

        var multipart = (Multipart)m.Body!;
        multipart.Count.Should().BeGreaterThan(1); // text + attachment

        multipart.OfType<MimePart>()
            .Any(p => p.FileName == "test.txt")
            .Should().BeTrue();
    }

    // ---------------- fakes ----------------

    private sealed class FakeSmtpClientFactory : ISmtpClientFactory
    {
        private readonly ISmtpClientAdapter _client;
        public FakeSmtpClientFactory(ISmtpClientAdapter client) => _client = client;
        public ISmtpClientAdapter Create() => _client;
    }

    private sealed class FakeSmtpClient : ISmtpClientAdapter
    {
        public bool CheckCertificateRevocation { get; set; }

        public bool ConnectCalled { get; private set; }
        public bool AuthCalled { get; private set; }
        public bool SendCalled { get; private set; }
        public bool DisconnectCalled { get; private set; }

        public SecureSocketOptions? LastSecureSocketOptions { get; private set; }
        public MimeMessage? LastMessage { get; private set; }

        public Task ConnectAsync(string host, int port, SecureSocketOptions options)
        {
            ConnectCalled = true;
            LastSecureSocketOptions = options;
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(string userName, string password)
        {
            AuthCalled = true;
            return Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message)
        {
            SendCalled = true;
            LastMessage = message;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(bool quit)
        {
            DisconnectCalled = true;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
