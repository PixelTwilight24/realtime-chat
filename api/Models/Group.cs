namespace api.Models;

public class Group
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int CreatedById { get; set; }

    public User? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<GroupMember> Members { get; set; } = [];

    public List<GroupMessage> Messages { get; set; } = [];
}
