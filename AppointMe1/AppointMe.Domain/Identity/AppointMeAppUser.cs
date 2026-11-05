using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace AppointMe.Domain.Identity
{
    public class AppointMeAppUser : IdentityUser
    {

        /// <summary>
        /// Id of the Business this user belongs to. Nullable because it can be assigned after the
        /// account is created (see SettingsController.Business).
        /// </summary>
        public Guid? TenantId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>UTC. The last successful sign-in; set only by AppSignInManager, never by a cookie renewal.</summary>
        public DateTime? LastLoginAt { get; set; }
       




    }
}
