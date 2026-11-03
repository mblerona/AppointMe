using AppointMe.Service.Email;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Sends ASP.NET Core Identity emails (confirmation, password reset) through our MailKit
    /// <see cref="IEmailService"/>. Replaces the default UI's no-op sender. Exceptions are not caught here;
    /// callers decide whether a failed send should stop the action.
    /// </summary>
    public sealed class IdentityEmailSender : IEmailSender
    {
        private readonly IEmailService _emailService;

        public IdentityEmailSender(IEmailService emailService)
        {
            _emailService = emailService;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
            _emailService.SendEmailAsync(new EmailMessage
            {
                MailTo = email,
                Subject = subject,
                Content = htmlMessage,
                IsHtml = true
            });
    }
}
