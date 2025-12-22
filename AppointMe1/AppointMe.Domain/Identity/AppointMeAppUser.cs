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

        //WHY? make tentantID nullable, i will assign it later once business is created 
        public Guid? TenantId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
       




    }
}
