using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using api.Data;
using api.Dtos;
using api.Extensions;
using api.Models;
using api.Options;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(ChatDbContext db, CryptoHelper crypto, IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    [HttpPost("signup")]
    public async Task<ActionResult<AuthResponse>> Signup(SignupRequest request)
    {
        var emailNormalized = request.Email.Trim().ToLowerInvariant();

        var emailTaken = await db.Users.AnyAsync(u => u.Email == emailNormalized);
        if (emailTaken)
        {
            return Conflict(new { message = "An account with this email already exists." });
        }

        var user = new User
        {
            Name = request.Name.Trim(),
            Email = emailNormalized,
            PasswordHash = crypto.HashPassword(request.Password),
            Avatar = $"https://i.pravatar.cc/150?u={Uri.EscapeDataString(emailNormalized)}",
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return Ok(BuildAuthResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var emailNormalized = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == emailNormalized);
        if (user is null || !crypto.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(BuildAuthResponse(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var userId = User.GetUserId(crypto);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return NotFound();

        return Ok(new UserDto(user.Id, user.Name, user.Email, user.Avatar, user.Gender, user.IsOnline));
    }

    private AuthResponse BuildAuthResponse(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwt.ExpiryMinutes);

        var claims = new[]
        {
            // Claim values are encrypted (not just signed) so the JWT payload doesn't expose PII in plaintext.
            new Claim(JwtRegisteredClaimNames.Sub, crypto.Encrypt(user.Id.ToString())),
            new Claim(JwtRegisteredClaimNames.Name, crypto.Encrypt(user.Name)),
            new Claim(JwtRegisteredClaimNames.Email, crypto.Encrypt(user.Email)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var signingKey = new SymmetricSecurityKey(Convert.FromBase64String(_jwt.Key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        var userDto = new UserDto(user.Id, user.Name, user.Email, user.Avatar, user.Gender, user.IsOnline);

        return new AuthResponse(tokenString, expiresAt, userDto);
    }
}
