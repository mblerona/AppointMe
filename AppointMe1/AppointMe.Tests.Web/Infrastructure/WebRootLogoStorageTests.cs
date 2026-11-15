using AppointMe.Web.Infrastructure;
using AwesomeAssertions;
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
    public async Task Save_TwiceForTheSameBusiness_GivesTwoFiles_AndNeverOverwrites()
    {
        var id = Guid.NewGuid();

        var first = await _storage.SaveAsync(id, ".png", new MemoryStream([1, 1, 1]));
        var second = await _storage.SaveAsync(id, ".png", new MemoryStream([2, 2, 2]));

        second.Should().NotBe(first);
        first.Should().StartWith($"/uploads/logos/{id}-").And.EndWith(".png");
        (await _storage.ReadAsync(first)).Should().Equal(1, 1, 1);
        (await _storage.ReadAsync(second)).Should().Equal(2, 2, 2);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatFile()
    {
        var id = Guid.NewGuid();
        var keep = await _storage.SaveAsync(id, ".png", new MemoryStream([1]));
        var remove = await _storage.SaveAsync(id, ".png", new MemoryStream([2]));

        await _storage.DeleteAsync(remove);

        (await _storage.ReadAsync(remove)).Should().BeNull();
        (await _storage.ReadAsync(keep)).Should().Equal(1);
    }

    [Theory]
    [InlineData("/uploads/logos/../secret.txt")]
    [InlineData("/uploads/logos/sub/x.png")]
    [InlineData("https://example.com/uploads/logos/x.png")]
    [InlineData("/uploads/logos/")]
    [InlineData("/uploads/logos/missing.png")]
    public async Task Delete_AnythingButAStoredLogo_DeletesNothing_AndDoesNotThrow(string url)
    {
        Directory.CreateDirectory(Path.Combine(Logos, "sub"));
        var secret = Path.Combine(_root, "uploads", "secret.txt");
        File.WriteAllText(secret, "secret");
        File.WriteAllText(Path.Combine(Logos, "sub", "x.png"), "x");

        await _storage.DeleteAsync(url);

        File.Exists(secret).Should().BeTrue();
        File.Exists(Path.Combine(Logos, "sub", "x.png")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAll_RemovesTheOldAndTheNewStyleFilesOfThatBusiness_AndNothingElse()
    {
        // Tenant deletion: every upload of the business, including ones invoices still showed.
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        AddFile($"{id}.png");                                        // before S5
        await _storage.SaveAsync(id, ".png", new MemoryStream([1]));
        await _storage.SaveAsync(id, ".webp", new MemoryStream([2]));
        var othersLogo = await _storage.SaveAsync(other, ".png", new MemoryStream([3]));
        AddFile($"{other}.jpg");

        await _storage.DeleteAllAsync(id);

        Directory.GetFiles(Logos).Select(Path.GetFileName).Should().BeEquivalentTo(
            othersLogo.Substring("/uploads/logos/".Length), $"{other}.jpg");
    }

    [Fact]
    public async Task DeleteAll_WithNoLogoOrFolder_DoesNothing()
    {
        var act = () => _storage.DeleteAllAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Read_ReturnsTheBytesOfAnUploadedLogo_FromTheUrlSaveReturned()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var url = await _storage.SaveAsync(Guid.NewGuid(), ".png", new MemoryStream(bytes));

        (await _storage.ReadAsync(url)).Should().Equal(bytes);
    }

    [Theory]
    [InlineData("/uploads/logos/missing.png")]                   // no such file
    [InlineData("https://example.com/uploads/logos/x.png")]      // never downloads
    [InlineData("//example.com/uploads/logos/x.png")]
    [InlineData("/uploads/logos/../secret.txt")]                 // outside the folder
    [InlineData("/uploads/logos/..%2Fsecret.txt")]
    [InlineData("/uploads/logos/sub/x.png")]
    [InlineData("/uploads/logos/")]
    [InlineData("/css/site.css")]
    [InlineData("")]
    public async Task Read_AnythingButAStoredLogo_IsNull(string url)
    {
        Directory.CreateDirectory(Path.Combine(Logos, "sub"));
        File.WriteAllText(Path.Combine(_root, "uploads", "secret.txt"), "secret");
        File.WriteAllText(Path.Combine(Logos, "sub", "x.png"), "x");

        (await _storage.ReadAsync(url)).Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
