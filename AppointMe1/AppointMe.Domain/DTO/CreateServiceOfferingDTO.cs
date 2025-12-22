using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DTO
{
    public class CreateServiceOfferingDTO
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = "";

        [Range(0, 1000000)]
        public decimal Price { get; set; }

        public bool IsActive { get; set; } = true;

       
        public Guid? CategoryId { get; set; }
    }

    public class UpdateServiceOfferingDTO : CreateServiceOfferingDTO
    {
        public Guid Id { get; set; }
    }
}

