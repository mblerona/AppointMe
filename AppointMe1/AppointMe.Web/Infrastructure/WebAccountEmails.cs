using AppointMe.Domain.Identity;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>Lets the Service layer send the same emails as Register and Forgot password, for the current request.</summary>
    public sealed class WebAccountEmails : IAccountEmails
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IUrlHelperFactory _urlHelperFactory;
        private readonly AccountConfirmationEmail _confirmationEmail;
        private readonly PasswordResetEmail _resetEmail;

        public WebAccountEmails(
            IHttpContextAccessor httpContextAccessor,
            IUrlHelperFactory urlHelperFactory,
            AccountConfirmationEmail confirmationEmail,
            PasswordResetEmail resetEmail)
        {
            _httpContextAccessor = httpContextAccessor;
            _urlHelperFactory = urlHelperFactory;
            _confirmationEmail = confirmationEmail;
            _resetEmail = resetEmail;
        }

        public Task<bool> TrySendConfirmationAsync(AppointMeAppUser user)
        {
            var (url, scheme) = CurrentRequest();
            return _confirmationEmail.TrySendAsync(user, url, scheme);
        }

        public Task<bool> TrySendPasswordResetAsync(AppointMeAppUser user)
        {
            var (url, scheme) = CurrentRequest();
            return _resetEmail.TrySendAsync(user, url, scheme);
        }

        private (IUrlHelper Url, string Scheme) CurrentRequest()
        {
            var httpContext = _httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("These emails can only be sent during a request.");

            var actionContext = new ActionContext(httpContext, httpContext.GetRouteData(), new ActionDescriptor());
            return (_urlHelperFactory.GetUrlHelper(actionContext), httpContext.Request.Scheme);
        }
    }
}
