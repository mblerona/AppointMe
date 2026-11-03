using System.Text;
using AppointMe.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace AppointMe.Web.Areas.Identity.Pages.Account
{
    /// <summary>Target of the link in the confirmation email.</summary>
    [AllowAnonymous]
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<AppointMeAppUser> _userManager;

        public ConfirmEmailModel(UserManager<AppointMeAppUser> userManager)
        {
            _userManager = userManager;
        }

        public bool Succeeded { get; private set; }

        public async Task<IActionResult> OnGetAsync(string? userId, string? code)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(code))
                return RedirectToPage("./Login");

            // Unknown user, bad encoding and invalid/expired token all show the same failure message.
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Page();

            string token;
            try
            {
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
                return Page();
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            Succeeded = result.Succeeded;
            return Page();
        }
    }
}
