using System.Text;
using System.Text.Encodings.Web;
using AppointMe.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Builds and sends the "confirm your account" email (used by Register and ResendEmailConfirmation).
    /// A failed send is logged and reported as <c>false</c>; it never undoes the registration.
    /// </summary>
    public sealed class AccountConfirmationEmail
    {
        public const string Subject = "Confirm your AppointMe account";

        private readonly UserManager<AppointMeAppUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AccountConfirmationEmail> _logger;

        public AccountConfirmationEmail(
            UserManager<AppointMeAppUser> userManager,
            IEmailSender emailSender,
            ILogger<AccountConfirmationEmail> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        /// <returns><c>true</c> if the email was handed to the mail server.</returns>
        public async Task<bool> TrySendAsync(AppointMeAppUser user, IUrlHelper url, string scheme)
        {
            try
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                var link = url.Page(
                    "/Account/ConfirmEmail",
                    pageHandler: null,
                    values: new { area = "Identity", userId = user.Id, code },
                    protocol: scheme)
                    ?? throw new InvalidOperationException("Could not build the confirmation link.");

                var name = HtmlEncoder.Default.Encode(user.FirstName ?? "");
                var body =
                    $"<p>Hi {name},</p>" +
                    "<p>Thanks for registering with AppointMe. Please confirm your account by " +
                    $"<a href=\"{HtmlEncoder.Default.Encode(link)}\">clicking here</a>.</p>" +
                    "<p>If you didn't create this account, you can ignore this email.</p>";

                await _emailSender.SendEmailAsync(user.Email!, Subject, body);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sending the confirmation email to user {UserId} failed.", user.Id);
                return false;
            }
        }
    }
}
