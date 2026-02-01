using AppointMe.Domain.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Interface
{
    public  interface IHolidayService
    {
   
        Task<HashSet<DateOnly>> GetHolidayDatesAsync(int year, string countryCode);
        Task<List<HolidayDTO>> GetHolidaysAsync(int year, string countryCode);
    }
}
