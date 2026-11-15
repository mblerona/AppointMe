namespace AppointMe.Service.Implementation
{
    /// <summary>
    /// Recognizes a logo by its content (the file's first bytes), not by its name, so a renamed
    /// HTML, SVG or executable file can't be stored as a "PNG" (audit item S5).
    /// </summary>
    public static class LogoImageFormat
    {
        /// <summary>Enough bytes to recognize every supported format.</summary>
        public const int HeaderLength = 12;

        private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        private static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];
        private static ReadOnlySpan<byte> Riff => "RIFF"u8;
        private static ReadOnlySpan<byte> Webp => "WEBP"u8;

        /// <returns>
        /// The extension to store the file with (".png", ".jpg" or ".webp"), taken from the content,
        /// or null if the content is none of the supported formats.
        /// </returns>
        public static string? DetectExtension(ReadOnlySpan<byte> content)
        {
            if (content.StartsWith(Png))
                return ".png";

            if (content.StartsWith(Jpeg))
                return ".jpg";

            // "RIFF", a 4-byte size, then "WEBP".
            if (content.Length >= HeaderLength && content.StartsWith(Riff) && content.Slice(8, 4).SequenceEqual(Webp))
                return ".webp";

            return null;
        }
    }
}
