namespace api.Models;

public class Message
{
    public int Id { get; set; }

    public int SenderId { get; set; }

    public User? Sender { get; set; }

    public int ReceiverId { get; set; }

    public User? Receiver { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public string? AttachmentUrl { get; set; }

    public string? AttachmentFileName { get; set; }

    public string? AttachmentContentType { get; set; }

    public long? AttachmentSize { get; set; }
}
