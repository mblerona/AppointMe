using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AppointMe.Web.Infrastructure
{
    public enum BusinessFeature
    {
        Services,

        /// <summary>Needs services on as well (BusinessFeatures.InvoicesEnabled).</summary>
        Invoices
    }

    /// <summary>
    /// Answers 404 while the business has this feature switched off, so a feature that is off
    /// can't be reached by typing its URL. Only hides: the data stays and is back when the feature is.
    /// Runs after RequireTenantFilter (Order 1 vs. 0), so users without a Business are still sent
    /// to Settings instead of getting a 404.
    /// </summary>
    public sealed class RequireFeatureAttribute : TypeFilterAttribute
    {
        public RequireFeatureAttribute(BusinessFeature feature) : base(typeof(RequireFeatureFilter))
        {
            Arguments = new object[] { feature };
            Order = 1;
        }

        public BusinessFeature Feature => (BusinessFeature)Arguments![0];
    }

    public sealed class RequireFeatureFilter : IAsyncActionFilter
    {
        private readonly BusinessFeature _feature;
        private readonly CurrentBusiness _currentBusiness;

        public RequireFeatureFilter(BusinessFeature feature, CurrentBusiness currentBusiness)
        {
            _feature = feature;
            _currentBusiness = currentBusiness;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Cached per request: the layout reads the same Business afterwards.
            var business = await _currentBusiness.GetAsync();

            var enabled = business != null && _feature switch
            {
                BusinessFeature.Services => business.EnableServices,
                BusinessFeature.Invoices => business.EnableInvoices, // already "services and invoices"
                _ => false
            };

            if (!enabled)
            {
                context.Result = new NotFoundResult();
                return;
            }

            await next();
        }
    }
}
