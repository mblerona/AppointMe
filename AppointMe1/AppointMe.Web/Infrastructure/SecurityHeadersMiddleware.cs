namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Adds the basic security headers to every response, static files and error pages included:
    /// no MIME sniffing (uploaded logos are served from wwwroot), no framing (clickjacking), and no full
    /// URL in the Referer header to other sites (password reset links carry a token). The headers are set
    /// when the response starts, because the exception handler and the status code pages clear the headers
    /// before they re-execute. Deliberately no Content-Security-Policy yet: the views still use inline
    /// scripts and styles.
    /// </summary>
    public sealed class SecurityHeadersMiddleware
    {
        public static readonly IReadOnlyDictionary<string, string> Headers = new Dictionary<string, string>
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["X-Frame-Options"] = "DENY",
            ["Referrer-Policy"] = "strict-origin-when-cross-origin"
        };

        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            context.Response.OnStarting(() =>
            {
                foreach (var (name, value) in Headers)
                    context.Response.Headers[name] = value;
                return Task.CompletedTask;
            });

            return _next(context);
        }
    }
}
