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
    /// Replaces the default UI page, so this page and the admin's button send the same email. The response is
    /// always the same, whether or not the email belongs to a confirmed account, so it can't be used to find
    /// out which emails are registered.
    /// </summary>
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<AppointMeAppUser> _userManager;
        private readonly PasswordResetEmail _resetEmail;

        public ForgotPasswordModel(UserManager<AppointMeAppUser> userManager, PasswordResetEmail resetEmail)
        {
            _userManager = userManager;
            _resetEmail = resetEmail;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = "";
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user != null && await _userManager.IsEmailConfirmedAsync(user))
            {
                // A failed send is logged inside TrySendAsync; the response stays the same on purpose.
                await _resetEmail.TrySendAsync(user, Url, Request.Scheme);
            }

            return RedirectToPage("./ForgotPasswordConfirmation");
        }
    }
}
