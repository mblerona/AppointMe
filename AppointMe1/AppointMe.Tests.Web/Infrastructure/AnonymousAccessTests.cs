using System.Net;
using AppointMe.Tests.Web.Factories;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>
/// Every endpoint needs a logged-in user (the fallback policy) unless it is on this explicit allow-list.
/// Adding a public page means adding it here, on purpose. The second test doesn't trust the metadata:
/// it requests every other endpoint without logging in and expects to be sent to the login page.
/// </summary>
public class AnonymousAccessTests : IClassFixture<NoAuthWebAppFactory>
{
    /// <summary>Controller actions as "Area/Controller.Action" (no area: "/Controller.Action"), pages as "Area/Path".</summary>
    private static readonly string[] AllowedAnonymously =
    [
        // Start page (redirects to login or home), the template privacy page, and the error/status pages.
        "/Home.Index",
        "/Home.Privacy",
        "/Home.Error",
        "/Home.Status",

        // Our account pages.
        "Identity/Account/Login",
        "Identity/Account/Logout",
        "Identity/Account/Register",
        "Identity/Account/RegisterConfirmation",
        "Identity/Account/ConfirmEmail",
        "Identity/Account/ForgotPassword",
        "Identity/Account/ResendEmailConfirmation",

        // Built-in Identity pages that are public by design (most need a token or a pending login).
        // (AccessDenied is not: only a logged-in user is ever denied.)
        "Identity/Account/ConfirmEmailChange",
        "Identity/Account/ExternalLogin",
        "Identity/Account/ForgotPasswordConfirmation",
        "Identity/Account/Lockout",
        "Identity/Account/LoginWith2fa",
        "Identity/Account/LoginWithRecoveryCode",
        "Identity/Account/ResetPassword",
        "Identity/Account/ResetPasswordConfirmation",
        "Identity/Error",
    ];

    private readonly NoAuthWebAppFactory _factory;

    public AnonymousAccessTests(NoAuthWebAppFactory factory)
    {
        _factory = factory;
    }

    private IReadOnlyList<RouteEndpoint> Endpoints()
    {
        _factory.CreateClient(); // starts the server, so the endpoints exist
        return _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ActionDescriptor>() != null) // MVC actions and Razor Pages
            .ToList();
    }

    private static string KeyOf(RouteEndpoint endpoint) => endpoint.Metadata.GetMetadata<ActionDescriptor>() switch
    {
        ControllerActionDescriptor c => $"{(c.RouteValues.TryGetValue("area", out var area) ? area : null)}/{c.ControllerName}.{c.ActionName}",
        PageActionDescriptor p => $"{p.AreaName}{p.ViewEnginePath}",
        var other => other!.DisplayName ?? endpoint.DisplayName ?? "?"
    };

    private static bool IsAnonymous(RouteEndpoint endpoint) => endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null;

    [Fact]
    public async Task TheFallbackPolicy_RequiresALoggedInUser()
    {
        var policy = await _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();

        policy.Should().NotBeNull("without it, a controller that forgets [Authorize] would be public");
        policy!.Requirements.Should().ContainSingle(r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void ExactlyTheAllowListedEndpoints_AreMarkedAllowAnonymous()
    {
        var anonymous = Endpoints().Where(IsAnonymous).Select(KeyOf).Distinct().OrderBy(k => k).ToList();

        anonymous.Should().BeEquivalentTo(AllowedAnonymously,
            "a public endpoint must be added to the allow-list on purpose, and the list must not keep stale entries");
    }

    [Fact]
    public async Task EveryOtherEndpoint_SendsAnAnonymousVisitorToTheLoginPage()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var endpoint in Endpoints().Where(e => !IsAnonymous(e)))
        {
            var url = UrlOf(endpoint);
            if (url == null)
                continue;

            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? HttpMethods.Get;
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method != HttpMethods.Get)
                request.Content = new FormUrlEncodedContent([]);

            var response = await client.SendAsync(request);
            checkedCount++;

            var location = response.Headers.Location?.OriginalString ?? "";
            if (response.StatusCode != HttpStatusCode.Redirect || !location.Contains("/Identity/Account/Login"))
                failures.Add($"{method} {url} ({KeyOf(endpoint)}) -> {(int)response.StatusCode} {location}");
        }

        checkedCount.Should().BeGreaterThan(50, "the walk must actually reach the app's endpoints");
        failures.Should().BeEmpty("every endpoint that isn't on the allow-list must require a login");
    }

    /// <summary>A concrete URL for the endpoint's route: route values filled in, a Guid for ids.</summary>
    private static string? UrlOf(RouteEndpoint endpoint)
    {
        var segments = new List<string>();
        foreach (var segment in endpoint.RoutePattern.PathSegments)
        {
            var text = "";
            foreach (var part in segment.Parts)
            {
                switch (part)
                {
                    case RoutePatternLiteralPart literal:
                        text += literal.Content;
                        break;
                    case RoutePatternParameterPart parameter:
                        if (endpoint.RoutePattern.RequiredValues.TryGetValue(parameter.Name, out var required) && required is string value)
                            text += value;
                        else if (parameter.Name == "id")
                            text += Guid.NewGuid().ToString();
                        else if (!parameter.IsOptional)
                            return null; // a parameter this walk can't guess; none exist today
                        break;
                    case RoutePatternSeparatorPart separator:
                        text += separator.Content;
                        break;
                }
            }

            if (text.Length > 0)
                segments.Add(text);
        }

        return "/" + string.Join("/", segments);
    }
}
