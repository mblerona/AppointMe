using System.Reflection;
using AppointMe.Repository.Interface;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using FluentAssertions;
using Xunit;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>
/// Only the dedicated admin service may read across tenants, and only the /Admin area may use it.
/// These tests fail as soon as other code takes a dependency on either, including through @inject in a view.
/// </summary>
public class PlatformAdminBoundaryTests
{
    private const BindingFlags AllMembers =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerable<Type> AppTypes() =>
        new[] { typeof(PlatformAdminService).Assembly, typeof(Program).Assembly }.SelectMany(a => a.GetTypes());

    private static bool DependsOn(Type type, Type dependency) =>
        type.GetConstructors(AllMembers).SelectMany(c => c.GetParameters()).Any(p => p.ParameterType == dependency)
        || type.GetFields(AllMembers).Any(f => f.FieldType == dependency)
        || type.GetProperties(AllMembers).Any(p => p.PropertyType == dependency);

    [Fact]
    public void OnlyThePlatformAdminService_UsesTheCrossTenantRepository()
    {
        var users = AppTypes().Where(t => DependsOn(t, typeof(IPlatformAdminRepository)));

        users.Should().ContainSingle().Which.Should().Be(typeof(PlatformAdminService));
    }

    [Fact]
    public void OnlyTheAdminArea_UsesThePlatformAdminService()
    {
        var users = AppTypes().Where(t => DependsOn(t, typeof(IPlatformAdminService))).ToList();

        users.Should().NotBeEmpty();
        users.Should().OnlyContain(t => t.Namespace == "AppointMe.Web.Areas.Admin.Controllers");
    }
}
