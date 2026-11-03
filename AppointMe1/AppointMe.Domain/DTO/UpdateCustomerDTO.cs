using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AppointMe.Domain.Validation;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DTO
{
    public  class UpdateCustomerDTO
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        [ValidPhoneNumber]
        public string PhoneNumber { get; set; }

        [ValidPhoneNumber]
        public string? SecondPhoneNumber { get; set; }
        public string State { get; set; }
 
        public string City { get; set; }
        public string? Notes { get; set; }
    }
}
