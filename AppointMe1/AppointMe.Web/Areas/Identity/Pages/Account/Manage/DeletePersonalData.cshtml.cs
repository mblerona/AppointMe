using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AppointMe.Web.Areas.Identity.Pages.Account.Manage
{
    /// <summary>
    /// Replaces the default UI page, which deletes the login straight away. That would skip the review and leave
    /// a Business with no users, and it would let the admin delete their own account. Tenants use the deletion
    /// request in Business Settings instead.
    /// </summary>
    public class DeletePersonalDataModel : PageModel
    {
        public IActionResult OnGet() => NotFound();

        public IActionResult OnPost() => NotFound();
    }
}
