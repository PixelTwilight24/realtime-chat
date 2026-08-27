namespace api.Services;

// Local-disk implementation for now. Swapping to a bucket (S3/Azure Blob/etc.) later just
// means adding a new implementation of this interface — callers only ever see a URL.
public interface IFileStorageService
{
    Task<string> SaveAsync(Stream content, string fileExtension, CancellationToken ct = default);
}
