using System.Text.Json.Serialization;

namespace api.Models;

public class User
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [JsonIgnore]
    public string PasswordHash { get; set; } = string.Empty;

    public string Avatar { get; set; } = string.Empty;

    public string Gender { get; set; } = string.Empty;

    public bool IsOnline { get; set; }

    [JsonIgnore]
    public List<Message> SentMessages { get; set; } = [];

    [JsonIgnore]
    public List<Message> ReceivedMessages { get; set; } = [];
}
