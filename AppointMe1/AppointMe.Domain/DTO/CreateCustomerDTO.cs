using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AppointMe.Domain.Validation;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DTO
{
    public class CreateCustomerDTO
    {
        [Required]
        [StringLength(50)]
        public string FirstName { get; set; }

        [Required]
        [StringLength(50)]
        public string LastName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [ValidPhoneNumber]
        public string PhoneNumber { get; set; }

        [ValidPhoneNumber]
        public string? SecondPhoneNumber { get; set; }

        [Required]
        public string State { get; set; }
        [Required]
        public string City { get; set; }
        public string? Notes { get; set; }
    }
}
