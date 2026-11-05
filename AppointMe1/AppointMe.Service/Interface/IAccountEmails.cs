using AppointMe.Domain.Identity;

namespace AppointMe.Service.Interface
{
    /// <summary>
    /// The emails with a link for a login account. The implementation lives in the Web project, because building
    /// the links needs routing. Neither the link nor the token ever comes back to the caller.
    /// </summary>
    public interface IAccountEmails
    {
        /// <returns><c>true</c> if the email was handed to the mail server.</returns>
        Task<bool> TrySendConfirmationAsync(AppointMeAppUser user);

        /// <returns><c>true</c> if the email was handed to the mail server.</returns>
        Task<bool> TrySendPasswordResetAsync(AppointMeAppUser user);
    }
}
