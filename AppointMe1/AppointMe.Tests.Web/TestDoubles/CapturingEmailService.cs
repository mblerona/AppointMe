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

    public Task SendEmailAsync(EmailMessage message)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task SendEmailWithAttachmentAsync(EmailMessage message, byte[] attachmentBytes, string attachmentFileName, string contentType)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
