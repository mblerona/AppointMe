using AppointMe.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>Against a temporary folder, never the real wwwroot.</summary>
public sealed class WebRootLogoStorageTests : IDisposable
{
    private sealed class TempEnvironment(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Testing";
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"logos-{Guid.NewGuid():N}");
    private readonly WebRootLogoStorage _storage;

    public WebRootLogoStorageTests()
    {
        _storage = new WebRootLogoStorage(new TempEnvironment(_root));
    }

    private string Logos => Path.Combine(_root, "uploads", "logos");

    private void AddFile(string name)
    {
        Directory.CreateDirectory(Logos);
        File.WriteAllText(Path.Combine(Logos, name), "x");
    }

    [Fact]
    public async Task DeleteAll_RemovesEveryExtensionOfThatBusiness_AndNothingElse()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        AddFile($"{id}.png");
        AddFile($"{id}.webp");
        AddFile($"{other}.png");

        await _storage.DeleteAllAsync(id);

        Directory.GetFiles(Logos).Select(Path.GetFileName).Should().Equal($"{other}.png");
    }

    [Fact]
    public async Task DeleteAll_WithNoLogoOrFolder_DoesNothing()
    {
        var act = () => _storage.DeleteAllAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
