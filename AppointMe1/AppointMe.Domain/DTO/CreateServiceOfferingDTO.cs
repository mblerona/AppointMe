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

        [Range(DomainModels.ServiceOffering.MinDurationMinutes, DomainModels.ServiceOffering.MaxDurationMinutes,
            ErrorMessage = "Duration must be between {1} and {2} minutes.")]
        public int? DurationMinutes { get; set; }

        public bool IsActive { get; set; } = true;

       
        public Guid? CategoryId { get; set; }
    }

    public class UpdateServiceOfferingDTO : CreateServiceOfferingDTO
    {
        public Guid Id { get; set; }
    }
}

