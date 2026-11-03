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

            return $"/uploads/logos/{fileName}";
        }
    }
}
