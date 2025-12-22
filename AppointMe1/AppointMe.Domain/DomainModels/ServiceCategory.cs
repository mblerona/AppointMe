using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DomainModels
{
    public class ServiceCategory
    {
        public Guid Id { get; set; }
        public Guid BusinessId { get; set; }   

        public string Name { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual Business? Business { get; set; }
        public virtual ICollection<ServiceOffering> Services { get; set; } = new List<ServiceOffering>();
    }
}
