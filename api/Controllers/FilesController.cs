using api.Dtos;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FilesController(IFileStorageService storage, ImageCompressionService imageCompression) : ControllerBase
{
    private const long MaxFileSizeBytes = 15 * 1024 * 1024; // 15 MB

    // Allowlist (not blocklist) for non-image uploads: anything that isn't a recognized,
    // inert document/media type is rejected. In particular this excludes .html/.htm/.svg/.xml/.js
    // and similar formats a browser could execute or render as active content if ever opened
    // inline from the same origin the API serves other uploads from.
    private static readonly HashSet<string> AllowedNonImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv", ".zip", ".mp4", ".mp3", ".mov", ".wav",
    };

    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<UploadedFile>> Upload(IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { message = "File exceeds the 15 MB limit." });
        }

        var originalFileName = Path.GetFileName(file.FileName);

        await using var stream = file.OpenReadStream();

        if (imageCompression.IsImage(file.ContentType))
        {
            byte[] compressed;
            try
            {
                compressed = imageCompression.CompressToJpeg(stream);
            }
            catch
            {
                return BadRequest(new { message = "The uploaded file is not a valid image." });
            }

            using var compressedStream = new MemoryStream(compressed);
            var url = await storage.SaveAsync(compressedStream, ".jpg");

            return Ok(new UploadedFile(url, originalFileName, "image/jpeg", compressed.Length));
        }

        var extension = Path.GetExtension(originalFileName);
        if (!AllowedNonImageExtensions.Contains(extension))
        {
            return BadRequest(new { message = $"Files of type '{extension}' are not allowed." });
        }

        var fileUrl = await storage.SaveAsync(stream, extension);

        return Ok(new UploadedFile(fileUrl, originalFileName, file.ContentType, file.Length));
    }
}
