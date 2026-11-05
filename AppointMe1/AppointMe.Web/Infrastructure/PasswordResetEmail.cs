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
    /// Builds and sends the "reset your password" email (used by Forgot password and by the admin). A failed send
    /// is logged and reported as <c>false</c>. The link and token never leave this class except in the email.
    /// </summary>
    public sealed class PasswordResetEmail
    {
        public const string Subject = "Reset your password";

        private readonly UserManager<AppointMeAppUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<PasswordResetEmail> _logger;

        public PasswordResetEmail(
            UserManager<AppointMeAppUser> userManager,
            IEmailSender emailSender,
            ILogger<PasswordResetEmail> logger)
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
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                var link = url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code },
                    protocol: scheme)
                    ?? throw new InvalidOperationException("Could not build the password reset link.");

                var body = $"Please reset your password by <a href='{HtmlEncoder.Default.Encode(link)}'>clicking here</a>.";

                await _emailSender.SendEmailAsync(user.Email!, Subject, body);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sending the password reset email to user {UserId} failed.", user.Id);
                return false;
            }
        }
    }
}
