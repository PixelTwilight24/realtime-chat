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

public record UpdateProfileRequest(
    [Required, MinLength(1)] string Name,
    string Gender,
    [Required, Url] string Avatar
);

public record ConversationSummary(UserDto User, string LastMessagePreview, DateTime LastMessageAt);
