using AppointMe.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AppointMe.Web.Areas.Identity.Pages.Account
{
    /// <summary>
    /// Replaces the built-in page: where cookie authentication sends a logged-in user who isn't allowed in
    /// (Forbid(), the admin area). It shows the same friendly "No access" card as the status pages and
    /// answers with a real 403. Like the built-in page, it needs a logged-in user.
    /// </summary>
    public class AccessDeniedModel : PageModel
    {
        public StatusPageVm Status { get; } = StatusPageVm.For(StatusCodes.Status403Forbidden);

        public IActionResult OnGet()
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Page();
        }
    }
}
