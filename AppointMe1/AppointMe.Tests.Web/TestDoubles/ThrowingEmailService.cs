using AppointMe.Service.Email;

namespace AppointMe.Tests.Web.TestDoubles;

public sealed class ThrowingEmailService : IEmailService
{
    /// <summary>Internal detail that must never reach the user.</summary>
    public const string SecretDetail = "SMTP auth failed for smtp.internal.example:587";

    public Task SendEmailAsync(EmailMessage message)
        => throw new InvalidOperationException(SecretDetail);

    public Task SendEmailWithAttachmentAsync(EmailMessage message, byte[] attachmentBytes, string attachmentFileName, string contentType)
        => throw new InvalidOperationException(SecretDetail);
}
