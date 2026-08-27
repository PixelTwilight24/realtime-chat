using SkiaSharp;

namespace api.Services;

// Every uploaded image gets transcoded to JPEG at this quality. Re-encoding (rather than
// trusting the client's content-type) also means a file merely labeled "image/*" that isn't
// actually a decodable raster image gets rejected instead of stored as-is.
public class ImageCompressionService
{
    private const int JpegQuality = 80;

    public bool IsImage(string? contentType) =>
        contentType is not null && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public byte[] CompressToJpeg(Stream input)
    {
        using var original = SKBitmap.Decode(input) ?? throw new InvalidOperationException("Unable to decode image.");
        using var image = SKImage.FromBitmap(original);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        return data.ToArray();
    }
}
