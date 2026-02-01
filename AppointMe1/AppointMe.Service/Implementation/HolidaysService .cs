using AppointMe.Repository.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using AppointMe.Domain.DTO;
using AppointMe.Service.Interface;

namespace AppointMe.Service.Implementation
{
    public class HolidaysService : IHolidayService
    {
        private readonly HttpClient _http;
        private readonly IMemoryCache _cache;

        public HolidaysService(HttpClient http, IMemoryCache cache)
        {
            _http = http;
            _cache = cache;
        }

        public async Task<List<HolidayDTO>> GetHolidaysAsync(int year, string countryCode)
        {
            countryCode = string.IsNullOrWhiteSpace(countryCode) ? "MK" : countryCode.Trim().ToUpperInvariant();
            var cacheKey = $"holidays:list:{countryCode}:{year}";

            if (_cache.TryGetValue(cacheKey, out List<HolidayDTO>? cached) && cached != null)
                return cached;

            var url = $"https://date.nager.at/api/v3/PublicHolidays/{year}/{countryCode}";
            var list = await _http.GetFromJsonAsync<List<HolidayDTO>>(url) ?? new List<HolidayDTO>();

            _cache.Set(cacheKey, list, TimeSpan.FromHours(12));
            return list;
        }

       
        public async Task<HashSet<DateOnly>> GetHolidayDatesAsync(int year, string countryCode)
        {
            countryCode = string.IsNullOrWhiteSpace(countryCode) ? "MK" : countryCode.Trim().ToUpperInvariant();
            var cacheKey = $"holidays:dates:{countryCode}:{year}";

            if (_cache.TryGetValue(cacheKey, out HashSet<DateOnly>? cached) && cached != null)
                return cached;

         
            var list = await GetHolidaysAsync(year, countryCode);

            var dates = list
                .Select(h => DateOnly.FromDateTime(h.Date))
                .ToHashSet();

            _cache.Set(cacheKey, dates, TimeSpan.FromHours(12));
            return dates;
        }
    }
}
