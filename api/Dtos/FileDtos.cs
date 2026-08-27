namespace api.Dtos;

public record UploadedFile(string Url, string FileName, string ContentType, long Size);

public record MessageAttachment(string Url, string FileName, string ContentType, long Size);
