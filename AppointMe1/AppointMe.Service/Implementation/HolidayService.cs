using System.Net.Http.Json;
using System.Text.Json;
using AppointMe.Domain.DTO;
using AppointMe.Service.Interface;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AppointMe.Service.Implementation
{
    public class HolidayService : IHolidayService
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);

        private readonly HttpClient _http;
        private readonly IMemoryCache _cache;
        private readonly ILogger<HolidayService> _logger;

        public HolidayService(HttpClient http, IMemoryCache cache, ILogger<HolidayService> logger)
        {
            _http = http;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Public holidays for the country. If the holiday API is unreachable, slow or returns
        /// garbage, this returns an empty list (bookings are allowed) instead of throwing, and
        /// the failure is not cached so the next call tries again.
        /// </summary>
        public async Task<List<HolidayDTO>> GetHolidaysAsync(int year, string countryCode)
        {
            return await TryGetHolidaysAsync(year, countryCode) ?? new List<HolidayDTO>();
        }

        public async Task<HashSet<DateOnly>> GetHolidayDatesAsync(int year, string countryCode)
        {
            countryCode = NormalizeCountry(countryCode);
            var cacheKey = $"holidays:dates:{countryCode}:{year}";

            if (_cache.TryGetValue(cacheKey, out HashSet<DateOnly>? cached) && cached != null)
                return cached;

            var list = await TryGetHolidaysAsync(year, countryCode);
            if (list == null)
                return new HashSet<DateOnly>();

            var dates = list
                .Select(h => DateOnly.FromDateTime(h.Date))
                .ToHashSet();

            _cache.Set(cacheKey, dates, CacheDuration);
            return dates;
        }

        /// <summary>Returns the (cached) list, or null when the API call failed.</summary>
        private async Task<List<HolidayDTO>?> TryGetHolidaysAsync(int year, string countryCode)
        {
            countryCode = NormalizeCountry(countryCode);
            var cacheKey = $"holidays:list:{countryCode}:{year}";

            if (_cache.TryGetValue(cacheKey, out List<HolidayDTO>? cached) && cached != null)
                return cached;

            var url = $"https://date.nager.at/api/v3/PublicHolidays/{year}/{countryCode}";

            List<HolidayDTO> list;
            try
            {
                list = await _http.GetFromJsonAsync<List<HolidayDTO>>(url) ?? new List<HolidayDTO>();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // TaskCanceledException = HttpClient timeout (no caller cancellation token is passed).
                _logger.LogWarning(ex,
                    "Holiday API call failed for {CountryCode}/{Year}; continuing without holidays.",
                    countryCode, year);
                return null;
            }

            _cache.Set(cacheKey, list, CacheDuration);
            return list;
        }

        private static string NormalizeCountry(string countryCode)
            => string.IsNullOrWhiteSpace(countryCode) ? "MK" : countryCode.Trim().ToUpperInvariant();
    }
}
