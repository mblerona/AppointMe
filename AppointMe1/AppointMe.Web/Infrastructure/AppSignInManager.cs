using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Interface;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Records the last login. It overrides only the methods that complete a real login, so the built-in two-factor
    /// and recovery-code pages count too, while cookie renewals and RefreshSignInAsync (after a password change) don't.
    /// A password step that still needs a second factor returns RequiresTwoFactor, so it doesn't count either.
    /// No external login providers are configured; add ExternalLoginSignInAsync here if one ever is.
    /// </summary>
    public sealed class AppSignInManager : SignInManager<AppointMeAppUser>
    {
        private readonly IBusinessRepository _businessRepository;
        private readonly TimeProvider _timeProvider;

        public AppSignInManager(
            UserManager<AppointMeAppUser> userManager,
            IHttpContextAccessor contextAccessor,
            IUserClaimsPrincipalFactory<AppointMeAppUser> claimsFactory,
            IOptions<IdentityOptions> optionsAccessor,
            ILogger<SignInManager<AppointMeAppUser>> logger,
            IAuthenticationSchemeProvider schemes,
            IUserConfirmation<AppointMeAppUser> confirmation,
            IBusinessRepository businessRepository,
            TimeProvider timeProvider)
            : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
        {
            _businessRepository = businessRepository;
            _timeProvider = timeProvider;
        }

        public override async Task<SignInResult> PasswordSignInAsync(
            AppointMeAppUser user, string password, bool isPersistent, bool lockoutOnFailure)
        {
            var result = await base.PasswordSignInAsync(user, password, isPersistent, lockoutOnFailure);
            if (result.Succeeded)
                await RecordLoginAsync(user);
            return result;
        }

        public override Task<SignInResult> TwoFactorAuthenticatorSignInAsync(string code, bool isPersistent, bool rememberClient) =>
            CompleteSecondFactorAsync(() => base.TwoFactorAuthenticatorSignInAsync(code, isPersistent, rememberClient));

        public override Task<SignInResult> TwoFactorRecoveryCodeSignInAsync(string recoveryCode) =>
            CompleteSecondFactorAsync(() => base.TwoFactorRecoveryCodeSignInAsync(recoveryCode));

        public override Task<SignInResult> TwoFactorSignInAsync(string provider, string code, bool isPersistent, bool rememberClient) =>
            CompleteSecondFactorAsync(() => base.TwoFactorSignInAsync(provider, code, isPersistent, rememberClient));

        private async Task<SignInResult> CompleteSecondFactorAsync(Func<Task<SignInResult>> signIn)
        {
            // Read first: a successful sign-in clears the cookie that says who is in the middle of two-factor.
            var user = await GetTwoFactorAuthenticationUserAsync();

            var result = await signIn();
            if (result.Succeeded && user != null)
                await RecordLoginAsync(user);
            return result;
        }

        /// <summary>The login has already succeeded, so a problem here is only logged and never stops it.</summary>
        private async Task RecordLoginAsync(AppointMeAppUser user)
        {
            try
            {
                // A suspended tenant's password is correct, but TenantStatusMiddleware blocks them right after this.
                if (user.TenantId is { } tenantId
                    && await _businessRepository.GetByIdReadOnlyAsync(tenantId) is { Status: BusinessStatus.Suspended })
                {
                    return;
                }

                user.LastLoginAt = _timeProvider.GetUtcNow().UtcDateTime;

                var result = await UserManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    Logger.LogWarning("Could not record the last login of user {UserId}: {Errors}",
                        user.Id, string.Join(" ", result.Errors.Select(e => e.Code)));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Could not record the last login of user {UserId}.", user.Id);
            }
        }
    }
}
