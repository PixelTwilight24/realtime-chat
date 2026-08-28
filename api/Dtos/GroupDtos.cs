using System.ComponentModel.DataAnnotations;

namespace api.Dtos;

public record CreateGroupRequest(
    [Required, MinLength(1), MaxLength(100)] string Name,
    [Required] List<int> MemberIds
);

public record RenameGroupRequest([Required, MinLength(1), MaxLength(100)] string Name);

public record AddGroupMemberRequest([Required] int UserId);

public record GroupMemberDto(int UserId, string Name, string Avatar, bool IsOnline, bool IsAdmin, DateTime JoinedAt);

public record GroupDto(int Id, string Name, int CreatedById, DateTime CreatedAt, List<GroupMemberDto> Members);

public record GroupSummary(int Id, string Name, string LastMessagePreview, DateTime? LastMessageAt, int MemberCount);
