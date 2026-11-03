using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Runs before every action of a tenant controller. Replaces the per-action EnsureTenantAsync calls:
    /// no user -> challenge (login), no tenant/Business -> Settings/Business, otherwise continue.
    /// Authorization filters run first, so anonymous requests are already redirected to login.
    /// </summary>
    public sealed class RequireTenantFilter : IAsyncActionFilter
    {
        private readonly ITenantContext _tenantContext;

        public RequireTenantFilter(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var resolution = await _tenantContext.ResolveAsync(context.HttpContext.RequestAborted);

            switch (resolution.Status)
            {
                case TenantResolutionStatus.NotAuthenticated:
                    context.Result = new ChallengeResult();
                    return;

                case TenantResolutionStatus.NoTenant:
                    context.Result = new RedirectToActionResult("Business", "Settings", null);
                    return;

                default:
                    await next();
                    return;
            }
        }
    }
}
