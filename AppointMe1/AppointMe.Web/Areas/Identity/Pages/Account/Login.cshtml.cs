// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AppointMe.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace AppointMe.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        public const string ConfirmEmailMessage =
            "You need to confirm your email before you can log in. Check your inbox for the confirmation link.";

        private const string AdminHomeUrl = "/Admin";

        private readonly SignInManager<AppointMeAppUser> _signInManager;
        private readonly UserManager<AppointMeAppUser> _userManager;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            SignInManager<AppointMeAppUser> signInManager,
            UserManager<AppointMeAppUser> userManager,
            ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        /// <summary>True when the password was correct but the email is not confirmed yet.</summary>
        public bool ShowResendConfirmation { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [Display(Name = "Remember me")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (!ModelState.IsValid)
                return Page();

            var result = await _signInManager.PasswordSignInAsync(
                Input.Email,
                Input.Password,
                Input.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in.");

                var user = await _userManager.FindByEmailAsync(Input.Email);
                if (user != null && await _userManager.IsInRoleAsync(user, PlatformRoles.Admin))
                    return LocalRedirect(IsAdminUrl(returnUrl) ? returnUrl : AdminHomeUrl);

                return LocalRedirect(returnUrl);
            }

            if (result.RequiresTwoFactor)
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });

            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout");
            }

            if (result.IsNotAllowed)
                return await HandleNotAllowedAsync();

            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return Page();
        }

        /// <summary>Admins land on /Admin; a return URL is kept only when it already points inside that area.</summary>
        private bool IsAdminUrl(string url) =>
            Url.IsLocalUrl(url)
            && (url.Equals(AdminHomeUrl, StringComparison.OrdinalIgnoreCase)
                || url.StartsWith(AdminHomeUrl + "/", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Identity returns NotAllowed for an unconfirmed account before it checks the password (and before
        /// lockout). So the "confirm your email" hint is shown only when the password is correct, otherwise
        /// anyone could find out which emails are registered. Wrong passwords still count towards lockout.
        /// </summary>
        private async Task<IActionResult> HandleNotAllowedAsync()
        {
            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user != null && !await _userManager.IsEmailConfirmedAsync(user))
            {
                if (await _userManager.IsLockedOutAsync(user))
                    return RedirectToPage("./Lockout");

                if (await _userManager.CheckPasswordAsync(user, Input.Password))
                {
                    ShowResendConfirmation = true;
                    ModelState.AddModelError(string.Empty, ConfirmEmailMessage);
                    return Page();
                }

                await _userManager.AccessFailedAsync(user);
                if (await _userManager.IsLockedOutAsync(user))
                {
                    _logger.LogWarning("User account locked out.");
                    return RedirectToPage("./Lockout");
                }
            }

            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return Page();
        }
    }
}





