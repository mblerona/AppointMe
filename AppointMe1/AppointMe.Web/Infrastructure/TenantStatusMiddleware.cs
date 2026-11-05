using AppointMe.Service.Interface;
using Microsoft.Net.Http.Headers;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Stops a suspended tenant before any page runs: everything is blocked except the short list below,
    /// so a page added later is covered without doing anything. It runs on every signed-in request and
    /// reads from the database through ITenantContext, so a suspension applies on the very next request.
    /// </summary>
    public sealed class TenantStatusMiddleware
    {
        public const string SuspendedPath = "/Suspended";

        private static readonly PathString[] AllowedWhileSuspended =
        {
            SuspendedPath,
            "/Identity/Account/Logout",
            "/Identity/Account/Login",
            "/Home/Error",

            // The links in the admin's emails must work for someone who is still logged in. They need a valid token anyway.
            "/Identity/Account/ResetPassword",
            "/Identity/Account/ResetPasswordConfirmation",
            "/Identity/Account/ConfirmEmail"
        };

        private readonly RequestDelegate _next;

        public TenantStatusMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
        {
            if (context.User.Identity?.IsAuthenticated == true && !IsAllowed(context.Request.Path))
            {
                var tenant = await tenantContext.ResolveAsync(context.RequestAborted);
                if (tenant.Status == TenantResolutionStatus.Suspended)
                {
                    // The calendar and form scripts fetch JSON; a redirect would hand them an HTML page.
                    if (WantsJson(context.Request))
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    else
                        context.Response.Redirect(SuspendedPath);
                    return;
                }
            }

            await _next(context);
        }

        private static bool IsAllowed(PathString path) => AllowedWhileSuspended.Any(path.StartsWithSegments);

        private static bool WantsJson(HttpRequest request) =>
            request.Headers[HeaderNames.Accept].ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }
}
