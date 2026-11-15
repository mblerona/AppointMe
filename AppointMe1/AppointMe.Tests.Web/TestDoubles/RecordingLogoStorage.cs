using System.Collections.Concurrent;
using AppointMe.Service.Interface;

namespace AppointMe.Tests.Web.TestDoubles;

/// <summary>Records which logos were deleted, so tests never touch the real wwwroot.</summary>
public sealed class RecordingLogoStorage : ILogoStorage
{
    private readonly ConcurrentQueue<Guid> _deleted = new();

    public IReadOnlyList<Guid> Deleted => _deleted.ToArray();

    /// <summary>While true, deleting throws, as it does when a file is in use.</summary>
    public bool Fail { get; set; }

    public Task<string> SaveAsync(Guid businessId, string extension, Stream content) =>
        Task.FromResult($"/uploads/logos/{businessId}{extension}");

    public Task DeleteAllAsync(Guid businessId)
    {
        if (Fail)
            throw new IOException("The file is in use.");

        _deleted.Enqueue(businessId);
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(string logoUrl) => Task.FromResult<byte[]?>(null);

    public Task DeleteAsync(string logoUrl) => Task.CompletedTask;
}
