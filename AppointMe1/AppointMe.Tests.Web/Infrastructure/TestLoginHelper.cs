using FluentAssertions;
using System.Net;
using System.Text.RegularExpressions;

namespace AppointMe.Tests.Web.Infrastructure;

public static class TestLoginHelper
{
    public static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""",
            RegexOptions.IgnoreCase);

        match.Success.Should().BeTrue("the page should contain __RequestVerificationToken");
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

    /// <summary>
    /// GETs <paramref name="formPageUrl"/> to obtain the antiforgery cookie and token,
    /// then POSTs <paramref name="fields"/> (plus the token) to <paramref name="postUrl"/>.
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client,
        string formPageUrl,
        string postUrl,
        IDictionary<string, string> fields)
    {
        var get = await client.GetAsync(formPageUrl);
        get.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {formPageUrl} must render a form with a token");

        var token = ExtractAntiForgeryToken(await get.Content.ReadAsStringAsync());

        var form = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = token
        };

        return await client.PostAsync(postUrl, new FormUrlEncodedContent(form));
    }

    /// <summary>Submits the login form once and returns the raw response (no assertions).</summary>
    public static async Task<HttpResponseMessage> PostLoginAsync(this HttpClient client, string email, string password)
    {
        var get = await client.GetAsync("/Identity/Account/Login");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await get.Content.ReadAsStringAsync();

        var form = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(html)
        };

        var returnUrl = ExtractReturnUrl(html);
        if (!string.IsNullOrWhiteSpace(returnUrl))
            form["Input.ReturnUrl"] = returnUrl;

        return await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(form));
    }

    public static async Task LoginAsync(this HttpClient client, string email, string password)
    {
        var post = await client.PostLoginAsync(email, password);

        // Success is usually 302
        if (post.StatusCode == HttpStatusCode.OK)
        {
            // Could be "invalid login attempt" page; fail with useful info
            var postHtml = await post.Content.ReadAsStringAsync();
            postHtml.Should().NotContain("Invalid login attempt", "login must succeed for web tests to work");
        }

        post.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.OK);

        // Manually follow redirect once to complete sign-in when AllowAutoRedirect=false
        if (post.StatusCode == HttpStatusCode.Redirect && post.Headers.Location != null)
        {
            var next = post.Headers.Location.ToString();
            if (next.StartsWith("/"))
            {
                var follow = await client.GetAsync(next);
                follow.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);
            }
        }
    }
}
