using FluentAssertions;
using System.Net;
using System.Text.RegularExpressions;

namespace AppointMe.Tests.Web.Infrastructure;

public static class TestLoginHelper
{
    private static string ExtractToken(string html)
    {
        var match = Regex.Match(
            html,
            @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""",
            RegexOptions.IgnoreCase);

        match.Success.Should().BeTrue("Login page should contain __RequestVerificationToken");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string? ExtractReturnUrl(string html)
    {
        // Identity usually uses: <input type="hidden" name="Input.ReturnUrl" value="...">
        var match = Regex.Match(
            html,
            @"name=""Input\.ReturnUrl""\s+type=""hidden""\s+value=""([^""]*)""",
            RegexOptions.IgnoreCase);

        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    public static async Task LoginAsync(this HttpClient client, string email, string password)
    {
        // 1) GET login page to receive antiforgery cookie + token
        var get = await client.GetAsync("/Identity/Account/Login");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await get.Content.ReadAsStringAsync();

        var token = ExtractToken(html);
        var returnUrl = ExtractReturnUrl(html);

        // 2) POST login form
        var form = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = token
        };

        if (!string.IsNullOrWhiteSpace(returnUrl))
            form["Input.ReturnUrl"] = returnUrl;

        var post = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(form));

        // Success is usually 302
        if (post.StatusCode == HttpStatusCode.OK)
        {
            // Could be "invalid login attempt" page; fail with useful info
            var postHtml = await post.Content.ReadAsStringAsync();
            postHtml.Should().NotContain("Invalid login attempt", "login must succeed for web tests to work");
        }

        post.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.OK);

        // 3) IMPORTANT: Manually follow redirect once to complete sign-in when AllowAutoRedirect=false
        if (post.StatusCode == HttpStatusCode.Redirect && post.Headers.Location != null)
        {
            var next = post.Headers.Location.ToString();
            if (next.StartsWith("/"))
            {
                var follow = await client.GetAsync(next);
                // follow is often 200 OK dashboard page
                follow.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
            }
        }
    }
}
