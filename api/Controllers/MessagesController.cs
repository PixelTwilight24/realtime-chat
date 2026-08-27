using api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MessagesController(ChatDbContext db) : ControllerBase
{
    // Message history between the current user and another user.
    [HttpGet("with/{userId:int}/{otherUserId:int}")]
    public async Task<ActionResult> GetConversation(int userId, int otherUserId)
    {
        var messages = await db.Messages
            .AsNoTracking()
            .Where(m =>
                (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                (m.SenderId == otherUserId && m.ReceiverId == userId))
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        return Ok(messages);
    }
}
