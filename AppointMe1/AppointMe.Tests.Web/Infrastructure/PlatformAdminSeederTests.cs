using AppointMe.Domain.Identity;
using AppointMe.Service.Admin;
using AppointMe.Tests.Web.Factories;
using AppointMe.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>Each test builds its own app, because it asserts on the role and account tables as a whole.</summary>
public class PlatformAdminSeederTests
{
    private const string Email = "seeded-admin@test.com";
    private const string Password = "Seeded123!Aa";

    private sealed class ListLogger : ILogger<PlatformAdminSeeder>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class Harness : IDisposable
    {
        private readonly CustomWebAppFactory _factory = new();
        private readonly IServiceScope _scope;

        public Harness()
        {
            _scope = _factory.Services.CreateScope();
            Users = _scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            Roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        }

        public UserManager<AppointMeAppUser> Users { get; }
        public RoleManager<IdentityRole> Roles { get; }
        public ListLogger Logger { get; } = new();

        public Task SeedAsync(string? email = Email, string? password = Password) =>
            new PlatformAdminSeeder(Users, Roles,
                Options.Create(new PlatformAdminOptions { Email = email, Password = password }), Logger).SeedAsync();

        public void Dispose()
        {
            _scope.Dispose();
            _factory.Dispose();
        }
    }

    [Fact]
    public async Task CreatesAConfirmedAdmin_WithTheRole_AndNoTenant()
    {
        using var h = new Harness();

        await h.SeedAsync();

        var admin = await h.Users.FindByEmailAsync(Email);
        admin.Should().NotBeNull();
        admin!.EmailConfirmed.Should().BeTrue();
        admin.TenantId.Should().BeNull();
        (await h.Users.IsInRoleAsync(admin, PlatformRoles.Admin)).Should().BeTrue();
        (await h.Users.CheckPasswordAsync(admin, Password)).Should().BeTrue();
    }

    [Fact]
    public async Task RunningTwice_LeavesOneAdminWithTheRole()
    {
        using var h = new Harness();

        await h.SeedAsync();
        await h.SeedAsync();

        (await h.Users.GetUsersInRoleAsync(PlatformRoles.Admin)).Should().ContainSingle()
            .Which.Email.Should().Be(Email);
        h.Roles.Roles.Count(r => r.Name == PlatformRoles.Admin).Should().Be(1);
        h.Logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task SecondRun_NeverResetsAnything_EvenWhenThePasswordSettingChanged()
    {
        using var h = new Harness();
        await h.SeedAsync();
        var before = await h.Users.FindByEmailAsync(Email);
        var (hash, stamp) = (before!.PasswordHash, before.SecurityStamp);

        await h.SeedAsync(password: "Different123!Aa");

        var after = await h.Users.FindByEmailAsync(Email);
        after!.PasswordHash.Should().Be(hash);
        after.SecurityStamp.Should().Be(stamp);
        (await h.Users.CheckPasswordAsync(after, Password)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, Password)]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData(Email, null)]
    [InlineData(Email, "")]
    public async Task MissingSettings_SkipWithAWarning_AndCreateNothing(string? email, string? password)
    {
        using var h = new Harness();

        await h.SeedAsync(email, password);

        h.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
        (await h.Users.FindByEmailAsync(Email)).Should().BeNull();
        (await h.Roles.RoleExistsAsync(PlatformRoles.Admin)).Should().BeFalse();
    }

    [Fact]
    public async Task EmailHeldByATenant_IsNotPromoted_AndIsLeftUnchanged()
    {
        using var h = new Harness();
        var tenantUser = await h.Users.FindByEmailAsync(CustomWebAppFactory.DefaultUserEmail);
        var hash = tenantUser!.PasswordHash;

        await h.SeedAsync(email: CustomWebAppFactory.DefaultUserEmail);

        var after = await h.Users.FindByEmailAsync(CustomWebAppFactory.DefaultUserEmail);
        (await h.Users.IsInRoleAsync(after!, PlatformRoles.Admin)).Should().BeFalse();
        after!.PasswordHash.Should().Be(hash);
        after.TenantId.Should().Be(CustomWebAppFactory.DefaultTenantId);
        h.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task UnconfirmedAccountWithoutTenant_IsNotPromoted()
    {
        using var h = new Harness();
        await h.Users.CreateAsync(
            new AppointMeAppUser { UserName = Email, Email = Email, FirstName = "A", LastName = "B", EmailConfirmed = false },
            Password);

        await h.SeedAsync();

        var user = await h.Users.FindByEmailAsync(Email);
        (await h.Users.IsInRoleAsync(user!, PlatformRoles.Admin)).Should().BeFalse();
        user!.EmailConfirmed.Should().BeFalse();
        h.Logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ARunThatStoppedBeforeAddingTheRole_IsFinished_WithoutChangingThePassword()
    {
        using var h = new Harness();
        await h.Users.CreateAsync(
            new AppointMeAppUser { UserName = Email, Email = Email, FirstName = "A", LastName = "B", EmailConfirmed = true },
            Password);

        await h.SeedAsync(password: "Different123!Aa");

        var user = await h.Users.FindByEmailAsync(Email);
        (await h.Users.IsInRoleAsync(user!, PlatformRoles.Admin)).Should().BeTrue();
        (await h.Users.CheckPasswordAsync(user!, Password)).Should().BeTrue();
    }

    [Fact]
    public async Task PasswordThatBreaksTheRules_LogsAnError_WithoutThePassword_AndCreatesNoAccount()
    {
        using var h = new Harness();
        const string weak = "weakpass";

        await h.SeedAsync(password: weak);

        (await h.Users.FindByEmailAsync(Email)).Should().BeNull();
        h.Logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
        h.Logger.Entries.Should().NotContain(e => e.Message.Contains(weak));
    }

    [Fact]
    public async Task StartupExtension_WhenSeedingFails_DoesNotStopTheApp()
    {
        // The seeder isn't registered here, so resolving it throws inside the extension.
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();

        var act = () => services.SeedPlatformAdminAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Startup_SeedsTheAdminFromConfiguration()
    {
        using var factory = new PlatformAdminWebAppFactory();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();

        var admin = await users.FindByEmailAsync(PlatformAdminWebAppFactory.AdminEmail);

        admin.Should().NotBeNull();
        (await users.IsInRoleAsync(admin!, PlatformRoles.Admin)).Should().BeTrue();
    }
}
