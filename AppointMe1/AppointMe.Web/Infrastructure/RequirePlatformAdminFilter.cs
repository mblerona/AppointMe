using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Runs after [Authorize(Roles = ...)], which reads the role from the cookie (it can be up to 30 minutes
    /// old). This filter checks the database on every request, so removing the role applies immediately.
    /// </summary>
    public sealed class RequirePlatformAdminFilter : IAsyncActionFilter
    {
        private readonly ITenantContext _tenantContext;

        public RequirePlatformAdminFilter(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var resolution = await _tenantContext.ResolveAsync(context.HttpContext.RequestAborted);

            switch (resolution.Status)
            {
                case TenantResolutionStatus.PlatformAdmin:
                    await next();
                    return;

                case TenantResolutionStatus.NotAuthenticated:
                    context.Result = new ChallengeResult();
                    return;

                default:
                    context.Result = new ForbidResult();
                    return;
            }
        }
    }
}
