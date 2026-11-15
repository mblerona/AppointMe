namespace AppointMe.Web.ViewModels
{
    /// <summary>What the friendly status page says. Plain words only: no paths, ids or technical details.</summary>
    public sealed record StatusPageVm(int Code, string Icon, string Title, string Message)
    {
        public static StatusPageVm For(int code) => code switch
        {
            StatusCodes.Status404NotFound => new(code, "bi-signpost-split", "Page not found",
                "This page doesn't exist, or it isn't available to you. Check the address, or go back to the start page."),

            StatusCodes.Status403Forbidden => new(code, "bi-shield-lock", "No access",
                "You don't have permission to open this page."),

            StatusCodes.Status429TooManyRequests => new(code, "bi-hourglass-split", "Too many attempts",
                "You've tried this a few times in a row. Please wait a minute, then try again."),

            // The exception handler's page (Home/Error). The details are in the application log only.
            StatusCodes.Status500InternalServerError => new(code, "bi-exclamation-triangle", "Something went wrong",
                "Sorry, something went wrong on our side. Please try again in a moment."),

            _ => new(code, "bi-exclamation-circle", "Something went wrong",
                "The request couldn't be completed. Please go back and try again.")
        };
    }
}
