using System.ComponentModel.DataAnnotations;
using AppointMe.Domain.Identity;
using AppointMe.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AppointMe.Web.Areas.Identity.Pages.Account
{
    /// <summary>
    /// Sends a new confirmation link. The page always shows the same message, whether or not the email
    /// belongs to an unconfirmed account, so it can't be used to find out which emails are registered.
    /// </summary>
    [AllowAnonymous]
    public class ResendEmailConfirmationModel : PageModel
    {
        public const string SentMessage =
            "If an unconfirmed account exists for this email, we've sent a new confirmation link. Please check your inbox.";

        private readonly UserManager<AppointMeAppUser> _userManager;
        private readonly AccountConfirmationEmail _confirmationEmail;

        public ResendEmailConfirmationModel(
            UserManager<AppointMeAppUser> userManager,
            AccountConfirmationEmail confirmationEmail)
        {
            _userManager = userManager;
            _confirmationEmail = confirmationEmail;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public bool Submitted { get; private set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = "";
        }

        public void OnGet(string? email)
        {
            Input.Email = email ?? "";
            ModelState.Clear();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
            {
                // A failed send is logged inside TrySendAsync; the message stays the same on purpose.
                await _confirmationEmail.TrySendAsync(user, Url, Request.Scheme);
            }

            Submitted = true;
            return Page();
        }
    }
}
