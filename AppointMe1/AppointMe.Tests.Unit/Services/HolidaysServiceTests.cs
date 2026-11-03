using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;


using AppointMe.Service.Implementation;


using AppointMe.Domain.DTO;

namespace AppointMe.Tests.Unit.Services;

public class HolidaysServiceTests
{
    [Fact]
    public async Task GetHolidaysAsync_TrimsAndUppercasesCountryCode_AndBuildsCorrectUrl()
    {
        // Arrange
        var year = 2026;

        var handler = new CountingHandler(req =>
        {
            var json = BuildHolidayJson(year);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new HolidayService(http, cache, NullLogger<HolidayService>.Instance);

        // Act
        var result = await sut.GetHolidaysAsync(year, " mk ");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        handler.CallCount.Should().Be(1);
        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.ToString().Should().Contain($"/PublicHolidays/{year}/MK");
    }

    [Fact]
    public async Task GetHolidaysAsync_WhenCountryCodeNull_UsesMK()
    {
        // Arrange
        var year = 2026;

        var handler = new CountingHandler(req =>
        {
            var json = BuildHolidayJson(year);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new HolidayService(http, cache, NullLogger<HolidayService>.Instance);

        // Act
        var _ = await sut.GetHolidaysAsync(year, null!);

        // Assert
        handler.CallCount.Should().Be(1);
        handler.LastRequestUri!.ToString().Should().Contain($"/PublicHolidays/{year}/MK");
    }

    [Fact]
    public async Task GetHolidaysAsync_WhenCountryCodeEmpty_UsesMK()
    {
        // Arrange
        var year = 2026;

        var handler = new CountingHandler(req =>
        {
            var json = BuildHolidayJson(year);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new HolidayService(http, cache, NullLogger<HolidayService>.Instance);

        // Act
        var _ = await sut.GetHolidaysAsync(year, "   ");

        // Assert
        handler.CallCount.Should().Be(1);
        handler.LastRequestUri!.ToString().Should().Contain($"/PublicHolidays/{year}/MK");
    }

    [Fact]
    public async Task GetHolidaysAsync_CachesResult_SecondCallDoesNotHitHttpAgain()
    {
        // Arrange
        var year = 2026;

        var handler = new CountingHandler(req =>
        {
            var json = BuildHolidayJson(year);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new HolidayService(http, cache, NullLogger<HolidayService>.Instance);

        // Act
        var r1 = await sut.GetHolidaysAsync(year, "MK");
        var r2 = await sut.GetHolidaysAsync(year, "MK");

        // Assert
        r1.Should().HaveCount(2);
        r2.Should().HaveCount(2);

        handler.CallCount.Should().Be(1); // ✅ proves caching works
    }

    /// <summary>
    /// GetHolidayDatesAsync builds on GetHolidaysAsync and caches the date set separately.
    /// </summary>
    [Fact]
    public async Task GetHolidayDatesAsync_ReturnsDistinctHolidayDates()
    {
        // Arrange
        var year = 2026;

        var handler = new CountingHandler(req =>
        {
            var json = BuildHolidayJson(year);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new HolidayService(http, cache, NullLogger<HolidayService>.Instance);

        // Act
        var dates = await sut.GetHolidayDatesAsync(year, "MK");

        // Assert
        dates.Should().NotBeNull();
        dates.Should().Contain(new DateOnly(year, 1, 1));
        dates.Should().Contain(new DateOnly(year, 5, 1));
        dates.Count.Should().Be(2);
    }

    // ----------------- helpers -----------------

    private static string BuildHolidayJson(int year)
    {
        // HolidayDTO.Date in your project is DateTime (NOT DateOnly)
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

        return JsonSerializer.Serialize(list);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public int CallCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        public CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_responder(request));
        }
    }


    [Fact]
    public async Task GetHolidaysAsync_WhenCacheAlreadyHasValue_ReturnsCached_AndDoesNotCallHttp()
    {
        // Arrange
        var year = 2026;

        // If HTTP is called, we’ll notice via CallCount (or you can even throw)
        var handler = new CountingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });

        var http = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new HolidayService(http, cache, NullLogger<HolidayService>.Instance);

        // IMPORTANT: this key must match the service logic:
        // cacheKey = $"Holidays:list:{countryCode}:{year}"
        // and countryCode is normalized to MK when input is " mk "
        var cacheKey = $"holidays:list:MK:{year}";

        var cached = new List<HolidayDTO>
    {
        new HolidayDTO
        {
            Date = new DateTime(year, 1, 1),
            LocalName = "Cached Local",
            Name = "Cached Name"
        }
    };

        cache.Set(cacheKey, cached, TimeSpan.FromMinutes(5));

        // Act
        var result = await sut.GetHolidaysAsync(year, " mk ");

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Cached Name");

        handler.CallCount.Should().Be(0); 
    }
    [Fact]
    public async Task GetHolidaysAsync_WhenApiReturnsNullJson_ReturnsEmptyList_NotNull()
    {
        // Arrange
        var year = 2026;

        var handler = new CountingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                // JSON null -> GetFromJsonAsync<List<HolidayDTO>>() returns null
                Content = new StringContent("null", Encoding.UTF8, "application/json")
            });

        var http = new HttpClient(handler);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new HolidayService(http, cache, NullLogger<HolidayService>.Instance);

        // Act
        var result = await sut.GetHolidaysAsync(year, "MK");

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        handler.CallCount.Should().Be(1);
    }

    // ----------------- API failures (B4) -----------------

    public static TheoryData<string> FailureModes => new() { "network", "http500", "timeout", "badjson" };

    private static HttpResponseMessage Fail(string mode) => mode switch
    {
        "network" => throw new HttpRequestException("connection refused"),
        "timeout" => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
        "http500" => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        "badjson" => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>maintenance</html>", Encoding.UTF8, "application/json")
        },
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static HttpResponseMessage Ok(int year) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(BuildHolidayJson(year), Encoding.UTF8, "application/json")
    };

    [Theory]
    [MemberData(nameof(FailureModes))]
    public async Task GetHolidaysAsync_WhenApiFails_ReturnsEmptyList_AndLogsWarning(string mode)
    {
        var handler = new CountingHandler(_ => Fail(mode));
        var logger = new Mock<ILogger<HolidayService>>();
        var sut = new HolidayService(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()), logger.Object);

        var result = await sut.GetHolidaysAsync(2026, "MK");

        result.Should().NotBeNull().And.BeEmpty();
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsNotNull<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task GetHolidaysAsync_AfterFailure_DoesNotCacheEmptyList_AndRetriesNextCall()
    {
        var year = 2026;
        var calls = 0;
        var handler = new CountingHandler(_ => ++calls == 1 ? Fail("network") : Ok(year));
        var sut = new HolidayService(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<HolidayService>.Instance);

        var first = await sut.GetHolidaysAsync(year, "MK");
        var second = await sut.GetHolidaysAsync(year, "MK");

        first.Should().BeEmpty();
        second.Should().HaveCount(2, "the outage must not be remembered for 12 hours");
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetHolidayDatesAsync_WhenApiFails_ReturnsEmptySet_AndDoesNotCacheIt()
    {
        var year = 2026;
        var calls = 0;
        var handler = new CountingHandler(_ => ++calls == 1 ? Fail("timeout") : Ok(year));
        var sut = new HolidayService(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<HolidayService>.Instance);

        var first = await sut.GetHolidayDatesAsync(year, "MK");
        var second = await sut.GetHolidayDatesAsync(year, "MK");

        first.Should().BeEmpty();
        second.Should().Contain(new DateOnly(year, 1, 1));
        handler.CallCount.Should().Be(2);
    }
}
