using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DTO
{
    public class HolidayDTO
    {
        public DateTime Date { get; set; }          
        public string LocalName { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Fixed { get; set; }
        public bool Global { get; set; }
    }
}
