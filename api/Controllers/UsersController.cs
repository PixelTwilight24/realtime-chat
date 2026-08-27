using api.Data;
using api.Dtos;
using api.Extensions;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UsersController(ChatDbContext db, CryptoHelper crypto) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<User>>> GetUsers()
    {
        return await db.Users.AsNoTracking().ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<User>> GetUser(int id)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        return user is null ? NotFound() : user;
    }

    // Only users the caller has actually exchanged a message with — not the full directory —
    // each paired with the text/attachment of their most recent message, so the sidebar preview
    // survives a page reload instead of only ever being filled in live over SignalR.
    [HttpGet("conversations")]
    public async Task<ActionResult<List<ConversationSummary>>> GetConversations()
    {
        var userId = User.GetUserId(crypto);

        // Grouping/picking "latest per partner" is done in memory rather than as a single
        // EF query — that pattern (GroupBy + OrderBy + First) is unreliable to translate to SQL.
        var relevantMessages = await db.Messages
            .Where(m => m.SenderId == userId || m.ReceiverId == userId)
            .OrderByDescending(m => m.SentAt)
            .Select(m => new
            {
                PartnerId = m.SenderId == userId ? m.ReceiverId : m.SenderId,
                m.Text,
                m.AttachmentFileName,
                m.SentAt,
            })
            .ToListAsync();

        var latestPerPartner = relevantMessages
            .GroupBy(m => m.PartnerId)
            .Select(g => g.First()) // already sorted by SentAt desc above
            .ToList();

        var partnerIds = latestPerPartner.Select(m => m.PartnerId).ToList();

        var users = await db.Users
            .AsNoTracking()
            .Where(u => partnerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        var summaries = latestPerPartner
            .Select(m =>
            {
                var user = users[m.PartnerId];
                var preview = !string.IsNullOrEmpty(m.Text)
                    ? m.Text
                    : m.AttachmentFileName is not null ? $"\U0001F4CE {m.AttachmentFileName}" : string.Empty;

                return new ConversationSummary(
                    new UserDto(user.Id, user.Name, user.Email, user.Avatar, user.Gender, user.IsOnline),
                    preview,
                    m.SentAt
                );
            })
            .OrderByDescending(s => s.LastMessageAt)
            .ToList();

        return summaries;
    }

    // Always operates on the caller's own account (id comes from the validated JWT,
    // never from the request) so a user can't edit anyone else's profile.
    [HttpPut("me")]
    public async Task<ActionResult<UserDto>> UpdateMe(UpdateProfileRequest request)
    {
        var userId = User.GetUserId(crypto);
        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        user.Name = request.Name.Trim();
        user.Gender = request.Gender?.Trim() ?? string.Empty;
        user.Avatar = request.Avatar.Trim();

        await db.SaveChangesAsync();

        return Ok(new UserDto(user.Id, user.Name, user.Email, user.Avatar, user.Gender, user.IsOnline));
    }
}
