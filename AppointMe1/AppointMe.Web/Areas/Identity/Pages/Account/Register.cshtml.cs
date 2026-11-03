// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using AppointMe.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace AppointMe.Web.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<AppointMeAppUser> _signInManager;
        private readonly UserManager<AppointMeAppUser> _userManager;
        private readonly IUserStore<AppointMeAppUser> _userStore;
        private readonly IUserEmailStore<AppointMeAppUser> _emailStore;
        private readonly IBusinessService _businessService;
        private readonly AccountConfirmationEmail _confirmationEmail;
        private readonly ILogger<RegisterModel> _logger;

        public RegisterModel(
            UserManager<AppointMeAppUser> userManager,
            IUserStore<AppointMeAppUser> userStore,
            SignInManager<AppointMeAppUser> signInManager,
            IBusinessService businessService,
            AccountConfirmationEmail confirmationEmail,
            ILogger<RegisterModel> logger)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _businessService = businessService;
            _confirmationEmail = confirmationEmail;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }
        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            [Required]
            [StringLength(BusinessService.MaxBusinessNameLength)]
            [Display(Name = "Business name")]
            public string BusinessName { get; set; }

            [Required]
            [Display(Name = "First name")]
            public string FirstName { get; set; }

            [Required]
            [Display(Name = "Last name")]
            public string LastName { get; set; }

            [Required]
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (!ModelState.IsValid)
                return Page();

            var user = CreateUser();

            // Username + Email (default Identity behavior)
            await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
            await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);

            user.FirstName = Input.FirstName.Trim();
            user.LastName = Input.LastName.Trim();

            IdentityResult result;
            try
            {
                // Creates the Business and the user together and sets user.TenantId.
                result = await _businessService.RegisterOwnerAsync(user, Input.Password, Input.BusinessName);
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.BusinessName)}", ex.Message);
                return Page();
            }

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);

                return Page();
            }

            _logger.LogInformation("User {UserId} registered with business {TenantId}.", user.Id, user.TenantId);

            // A failed email must not undo the registration; the confirmation page offers a resend.
            var emailSent = await _confirmationEmail.TrySendAsync(user, Url, Request.Scheme);

            if (_userManager.Options.SignIn.RequireConfirmedAccount)
            {
                return RedirectToPage("RegisterConfirmation", new
                {
                    email = Input.Email,
                    emailFailed = emailSent ? (bool?)null : true
                });
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl);
        }

        private AppointMeAppUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<AppointMeAppUser>();
            }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(AppointMeAppUser)}'. " +
                    $"Ensure that '{nameof(AppointMeAppUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
            }
        }

        private IUserEmailStore<AppointMeAppUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
                throw new NotSupportedException("The default UI requires a user store with email support.");

            return (IUserEmailStore<AppointMeAppUser>)_userStore;
        }
    }
}
