using AppointMe.Service.Interface;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Saves logos as wwwroot/uploads/logos/{businessId}-{random}{ext}: every upload gets a new name, so a
    /// file that existing invoices show is never overwritten. The business id prefix lets tenant deletion
    /// find all of a business's files, including the older {businessId}{ext} ones. BusinessService decides
    /// when a previous logo is unused and can be deleted. Files are still stored inside wwwroot.
    /// </summary>
    public sealed class WebRootLogoStorage : ILogoStorage
    {
        private const string UrlPrefix = "/uploads/logos/";

        private readonly IWebHostEnvironment _env;

        public WebRootLogoStorage(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string UploadsDir => Path.Combine(_env.WebRootPath, "uploads", "logos");

        public async Task<string> SaveAsync(Guid businessId, string extension, Stream content)
        {
            Directory.CreateDirectory(UploadsDir);

            var fileName = $"{businessId}-{Guid.NewGuid():N}{extension}";
            // CreateNew: fails rather than overwrite, should a name ever repeat.
            await using (var file = new FileStream(Path.Combine(UploadsDir, fileName), FileMode.CreateNew))
            {
                await content.CopyToAsync(file);
            }

            return UrlPrefix + fileName;
        }

        public async Task<byte[]?> ReadAsync(string logoUrl)
        {
            var path = PathOf(logoUrl);
            return path != null && File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        }

        public Task DeleteAsync(string logoUrl)
        {
            var path = PathOf(logoUrl);
            if (path != null && File.Exists(path))
                File.Delete(path);

            return Task.CompletedTask;
        }

        public Task DeleteAllAsync(Guid businessId)
        {
            if (Directory.Exists(UploadsDir))
            {
                // New names ({id}-{random}.ext) and the older ones ({id}.ext).
                var files = Directory.EnumerateFiles(UploadsDir, $"{businessId}-*")
                    .Concat(Directory.EnumerateFiles(UploadsDir, $"{businessId}.*"))
                    .ToList();

                foreach (var file in files)
                    File.Delete(file);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// The file behind a URL this storage returned, or null for anything else: only a plain file name
        /// directly in the logos folder, so no sub-folders, "..", query strings or other hosts.
        /// </summary>
        private string? PathOf(string? logoUrl)
        {
            if (string.IsNullOrWhiteSpace(logoUrl) || !logoUrl.StartsWith(UrlPrefix, StringComparison.Ordinal))
                return null;

            var fileName = logoUrl.Substring(UrlPrefix.Length);
            if (fileName.Length == 0
                || fileName != Path.GetFileName(fileName)
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.Contains("..")
                || fileName.Contains('?') || fileName.Contains('#'))
                return null;

            return Path.Combine(UploadsDir, fileName);
        }
    }
}
