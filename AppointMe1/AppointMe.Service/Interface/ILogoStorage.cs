namespace AppointMe.Service.Interface
{
    /// <summary>An uploaded logo file, independent of ASP.NET's IFormFile.</summary>
    public sealed record LogoUpload(Stream Content, string FileName, long Length);

    /// <summary>
    /// Stores business logos. The implementation lives in the Web project (it writes under wwwroot).
    /// Validation (size, file type) happens in BusinessService before this is called.
    /// </summary>
    public interface ILogoStorage
    {
        /// <param name="extension">Lower-case extension including the dot, e.g. ".png".</param>
        /// <returns>The public URL of the stored logo.</returns>
        Task<string> SaveAsync(Guid businessId, string extension, Stream content);
    }
}
