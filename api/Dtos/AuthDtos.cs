using System.ComponentModel.DataAnnotations;

namespace api.Dtos;

public record SignupRequest(
    [Required, MinLength(1)] string Name,
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password
);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record UserDto(int Id, string Name, string Email, string Avatar, string Gender, bool IsOnline);

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

// Avatar accepts either an absolute URL (external images, e.g. seeded pravatar.cc avatars)
// or a relative /uploads/... path from POST /api/files/upload — relative on purpose, so the
// stored value stays valid if the site's domain ever changes.
public record UpdateProfileRequest(
    [Required, MinLength(1)] string Name,
    string Gender,
    [Required] string Avatar
);

public record ConversationSummary(UserDto User, string LastMessagePreview, DateTime LastMessageAt, bool IsLastMessageMine);
