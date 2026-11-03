using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AppointMe.Web.Areas.Identity.Pages.Account
{
    /// <summary>
    /// "Check your email" page shown after registering. Replaces the default UI page, which shows the
    /// confirmation link on screen when no real email sender is configured.
    /// </summary>
    [AllowAnonymous]
    public class RegisterConfirmationModel : PageModel
    {
        public string? Email { get; private set; }

        /// <summary>True when the confirmation email could not be sent during registration.</summary>
        public bool EmailFailed { get; private set; }

        public IActionResult OnGet(string? email, bool emailFailed = false)
        {
            if (string.IsNullOrWhiteSpace(email))
                return RedirectToPage("./Login");

            Email = email;
            EmailFailed = emailFailed;
            return Page();
        }
    }
}
