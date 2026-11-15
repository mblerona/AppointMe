using AppointMe.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace AppointMe.Service.Interface
{
    /// <summary>
    /// The outcome of a registration. An email that already has an account is not an error the visitor
    /// may see (that would reveal who has an account): the page shows the same "check your email" result
    /// and tells the existing account's owner instead.
    /// </summary>
    public sealed class OwnerRegistrationResult
    {
        private OwnerRegistrationResult(IdentityResult identity, AppointMeAppUser? existingAccount)
        {
            Identity = identity;
            ExistingAccount = existingAccount;
        }

        /// <summary>Errors the visitor may see (password rules and the like); empty unless <see cref="Failed"/>.</summary>
        public IdentityResult Identity { get; }

        /// <summary>The account that already uses this email; set only when <see cref="EmailAlreadyRegistered"/>.</summary>
        public AppointMeAppUser? ExistingAccount { get; }

        /// <summary>A new Business and account were created.</summary>
        public bool Created => Identity.Succeeded && ExistingAccount == null;

        /// <summary>Nothing was created, and the visitor must see the same result as for <see cref="Created"/>.</summary>
        public bool EmailAlreadyRegistered => ExistingAccount != null;

        /// <summary>The visitor must fix the form; <see cref="Identity"/> has the errors.</summary>
        public bool Failed => !Identity.Succeeded;

        public static OwnerRegistrationResult NewAccount() => new(IdentityResult.Success, null);

        public static OwnerRegistrationResult AlreadyRegistered(AppointMeAppUser existing) => new(IdentityResult.Success, existing);

        public static OwnerRegistrationResult Invalid(IdentityResult errors) => new(errors, null);
    }
}
