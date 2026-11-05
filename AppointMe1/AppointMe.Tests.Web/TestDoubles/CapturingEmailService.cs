using System.Collections.Concurrent;
using AppointMe.Service.Email;

namespace AppointMe.Tests.Web.TestDoubles;

/// <summary>Records every email instead of sending it, so tests can read the confirmation link.</summary>
public sealed class CapturingEmailService : IEmailService
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => _sent.ToArray();

    public IReadOnlyList<EmailMessage> SentTo(string email) =>
        _sent.Where(m => string.Equals(m.MailTo, email, StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>While true, sending throws, as it does when the mail server is down.</summary>
    public bool Fail { get; set; }

    public Task SendEmailAsync(EmailMessage message)
    {
        if (Fail)
            throw new InvalidOperationException("The mail server is down.");

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task SendEmailWithAttachmentAsync(EmailMessage message, byte[] attachmentBytes, string attachmentFileName, string contentType)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
