using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;
using AppointMe.Tests.Web.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AppointMe.Tests.Web;
namespace AppointMe.Tests.Web.Factories;


public sealed class HolidaysWebAppFactory :   CustomWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHolidayService>();
            services.AddSingleton<IHolidayService, FakeHolidayService>();
        });
    }
}