using AppointMe.Service.Interface;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Saves logos as wwwroot/uploads/logos/{businessId}{ext} (same layout as before).
    /// Audit item S5 is still open: the old file isn't deleted when the extension changes, and files
    /// are stored inside wwwroot.
    /// </summary>
    public sealed class WebRootLogoStorage : ILogoStorage
    {
        private const string UrlPrefix = "/uploads/logos/";

        private readonly IWebHostEnvironment _env;

        public WebRootLogoStorage(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task<string> SaveAsync(Guid businessId, string extension, Stream content)
        {
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "logos");
            Directory.CreateDirectory(uploadsDir);

            var fileName = $"{businessId}{extension}";
            await using (var file = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create))
            {
                await content.CopyToAsync(file);
            }

            return UrlPrefix + fileName;
        }

        public async Task<byte[]?> ReadAsync(string logoUrl)
        {
            if (string.IsNullOrWhiteSpace(logoUrl) || !logoUrl.StartsWith(UrlPrefix, StringComparison.Ordinal))
                return null;

            // Only a plain file name directly in the logos folder: no sub-folders, no "..", no query string.
            var fileName = logoUrl.Substring(UrlPrefix.Length);
            if (fileName.Length == 0
                || fileName != Path.GetFileName(fileName)
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || fileName.Contains("..")
                || fileName.Contains('?') || fileName.Contains('#'))
                return null;

            var path = Path.Combine(_env.WebRootPath, "uploads", "logos", fileName);
            return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        }

        public Task DeleteAllAsync(Guid businessId)
        {
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "logos");
            if (Directory.Exists(uploadsDir))
            {
                foreach (var file in Directory.EnumerateFiles(uploadsDir, $"{businessId}.*"))
                    File.Delete(file);
            }

            return Task.CompletedTask;
        }
    }
}
