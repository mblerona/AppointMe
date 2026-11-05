using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Exceptions;
using AppointMe.Service.Interface;
using AppointMe.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Areas.Admin.Controllers
{
    /// <summary>
    /// The buttons for a tenant's login accounts. Each GET is the confirmation step and each POST acts. The id in
    /// the route is the tenant; the service rejects an account that doesn't belong to it. No password, token or
    /// link ever reaches this controller.
    /// </summary>
    public class TenantAccountsController : BaseAdminController
    {
        private readonly IPlatformAdminService _adminService;

        public TenantAccountsController(IPlatformAdminService adminService)
        {
            _adminService = adminService;
        }

        // ---------------- resend confirmation ----------------

        [HttpGet]
        public Task<IActionResult> ResendConfirmation(Guid id, string? accountId) =>
            ConfirmAsync(id, accountId, nameof(ResendConfirmation),
                account => account.EmailConfirmed ? "This account's email is already confirmed." : null,
                heading: "Resend the confirmation email?",
                explanation: "This account gets the same confirmation email as at registration.",
                button: "Resend confirmation email");

        [HttpPost, ActionName(nameof(ResendConfirmation))]
        public Task<IActionResult> ResendConfirmationPost([FromRoute] Guid id, [FromForm] string? accountId) =>
            ActAsync(id, async () => ReportEmail(
                await _adminService.ResendConfirmationAsync(id, accountId, AdminEmail), "The confirmation email"));

        // ---------------- unlock ----------------

        [HttpGet]
        public Task<IActionResult> Unlock(Guid id, string? accountId) =>
            ConfirmAsync(id, accountId, nameof(Unlock),
                account => account.IsLockedOut ? null : "This account isn't locked out.",
                heading: "Unlock this account?",
                explanation: "This clears the lockout and resets the failed-attempt count, so they can try to log in again straight away.",
                button: "Unlock account");

        [HttpPost, ActionName(nameof(Unlock))]
        public Task<IActionResult> UnlockPost([FromRoute] Guid id, [FromForm] string? accountId) =>
            ActAsync(id, async () =>
            {
                await _adminService.UnlockAccountAsync(id, accountId, AdminEmail);
                TempData["SuccessMessage"] = "The account has been unlocked.";
            });

        // ---------------- password reset ----------------

        [HttpGet]
        public Task<IActionResult> SendPasswordReset(Guid id, string? accountId) =>
            ConfirmAsync(id, accountId, nameof(SendPasswordReset),
                account => account.EmailConfirmed ? null : "This account's email isn't confirmed yet, so resend the confirmation email instead.",
                heading: "Send a password reset email?",
                explanation: "This account gets the same email as the Forgot password page. You won't see or set the password.",
                button: "Send password reset email");

        [HttpPost, ActionName(nameof(SendPasswordReset))]
        public Task<IActionResult> SendPasswordResetPost([FromRoute] Guid id, [FromForm] string? accountId) =>
            ActAsync(id, async () => ReportEmail(
                await _adminService.SendPasswordResetAsync(id, accountId, AdminEmail), "The password reset email"));

        // ---------------- helpers ----------------

        private async Task<IActionResult> ConfirmAsync(
            Guid tenantId, string? accountId, string action, Func<TenantAccountDTO, string?> whyNot,
            string heading, string explanation, string button)
        {
            TenantDetailsDTO tenant;
            try
            {
                tenant = await _adminService.GetTenantAsync(tenantId);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }

            // The list holds only this tenant's accounts, so this also rejects another tenant's account id.
            var account = tenant.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null)
                return NotFound();

            if (whyNot(account) is { } problem)
            {
                TempData["ErrorMessage"] = problem;
                return BackToTenant(tenantId);
            }

            return View("Confirm", new TenantAccountActionVm
            {
                TenantId = tenantId,
                BusinessName = tenant.Name,
                AccountId = account.Id,
                AccountEmail = account.Email,
                Action = action,
                Heading = heading,
                Explanation = explanation,
                Button = button
            });
        }

        private async Task<IActionResult> ActAsync(Guid tenantId, Func<Task> act)
        {
            try
            {
                await act();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (BusinessRuleException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return BackToTenant(tenantId);
        }

        /// <summary>The action counts either way; a problem with the email is only a warning.</summary>
        private void ReportEmail(bool sent, string email)
        {
            if (sent)
                TempData["SuccessMessage"] = $"{email} has been sent.";
            else
                TempData["WarningMessage"] = $"{email} could not be sent. The action was still recorded in the log.";
        }

        private IActionResult BackToTenant(Guid tenantId) =>
            RedirectToAction("Details", "Tenants", new { id = tenantId });
    }
}
