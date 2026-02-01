using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;

namespace AppointMe.Tests.Web.TestDoubles
{
    public sealed class FakeHolidayService : IHolidayService
    {
    public Task<List<HolidayDTO>> GetHolidaysAsync(int year, string countryCode)
    {
        // deterministic data for assertions
        var list = new List<HolidayDTO>
        {
            new HolidayDTO
            {
                Date = new DateTime(year, 1, 1),
                LocalName = "Нова Година",
                Name = "New Year's Day"
            },
            new HolidayDTO
            {
                Date = new DateTime(year, 5, 1),
                LocalName = "Ден на трудот",
                Name = "Labour Day"
            }
        };

        return Task.FromResult(list);
    }

    public Task<HashSet<DateOnly>> GetHolidayDatesAsync(int year, string countryCode)
        => Task.FromResult(new HashSet<DateOnly>
        {
            new DateOnly(year, 1, 1),
            new DateOnly(year, 5, 1)
        });

}
}
