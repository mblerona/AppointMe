using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DTO
{
    public class ServiceLineDto
    {
        public Guid ServiceId { get; set; }
        public string Name { get; set; } = "";
        public decimal PriceAtBooking { get; set; }
        public string? Category { get; set; }
    }
}
