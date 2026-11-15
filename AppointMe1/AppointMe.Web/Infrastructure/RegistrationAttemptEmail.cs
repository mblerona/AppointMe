using System.Text.Encodings.Web;
using AppointMe.Domain.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Sent to the owner of an existing account when someone registers with its email. Registration shows
    /// the visitor the same "check your email" result either way, so the page never reveals who has an
    /// account; the owner learns about it here instead. It contains only links to pages the owner can use
    /// themselves (log in, forgot password, resend confirmation), never a token: the request didn't come
    /// from them.
    /// </summary>
    public sealed class RegistrationAttemptEmail
    {
        public const string Subject = "Someone tried to register with your email";

        private readonly IEmailSender _emailSender;
        private readonly ILogger<RegistrationAttemptEmail> _logger;

        public RegistrationAttemptEmail(IEmailSender emailSender, ILogger<RegistrationAttemptEmail> logger)
        {
            _emailSender = emailSender;
            _logger = logger;
        }

        /// <returns><c>true</c> if the email was handed to the mail server.</returns>
        public async Task<bool> TrySendAsync(AppointMeAppUser existing, IUrlHelper url, string scheme)
        {
            try
            {
                string Link(string page) =>
                    HtmlEncoder.Default.Encode(
                        url.Page(page, pageHandler: null, values: new { area = "Identity" }, protocol: scheme)
                        ?? throw new InvalidOperationException($"Could not build the link to {page}."));

                var name = HtmlEncoder.Default.Encode(existing.FirstName ?? "");
                var greeting = string.IsNullOrWhiteSpace(name) ? "Hello," : $"Hello {name},";

                var whatToDo = existing.EmailConfirmed
                    ? $"<p>You can <a href=\"{Link("/Account/Login")}\">log in</a> as usual. " +
                      $"If you've forgotten your password, <a href=\"{Link("/Account/ForgotPassword")}\">reset it here</a>.</p>"
                    : "<p>Your account's email address isn't confirmed yet. " +
                      $"<a href=\"{Link("/Account/ResendEmailConfirmation")}\">Request a new confirmation link</a>, " +
                      $"then <a href=\"{Link("/Account/Login")}\">log in</a>.</p>";

                var body =
                    $"<p>{greeting}</p>" +
                    "<p>Someone just tried to create a new AppointMe account with this email address. " +
                    "You already have an account, so no new account was created.</p>" +
                    whatToDo +
                    "<p>If this wasn't you, you can ignore this email. Your account hasn't been changed.</p>";

                await _emailSender.SendEmailAsync(existing.Email!, Subject, body);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sending the registration attempt email to user {UserId} failed.", existing.Id);
                return false;
            }
        }
    }
}
