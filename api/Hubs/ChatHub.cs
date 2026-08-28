using api.Data;
using api.Dtos;
using api.Extensions;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace api.Hubs;

[Authorize]
public class ChatHub(ChatDbContext db, CryptoHelper crypto) : Hub
{
    // Public so GroupsController (REST-triggered group mutations that still need realtime
    // fan-out) can push to the same per-user personal group via IHubContext<ChatHub>.
    public static string UserGroup(int userId) => $"user-{userId}";

    // The connection is authenticated (see Program.cs), so the caller's identity comes from
    // their JWT's encrypted "sub" claim — never from a value the client passes in directly.
    private int GetUserId()
    {
        if (Context.User is null) throw new HubException("Unauthorized.");

        return Context.User.GetUserId(crypto);
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

        var user = await db.Users.FindAsync(userId);
        if (user is not null && !user.IsOnline)
        {
            user.IsOnline = true;
            await db.SaveChangesAsync();
            await Clients.All.SendAsync("UserPresenceChanged", userId, true);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();

        var user = await db.Users.FindAsync(userId);
        if (user is not null)
        {
            user.IsOnline = false;
            await db.SaveChangesAsync();
            await Clients.All.SendAsync("UserPresenceChanged", userId, false);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(int receiverId, string? text, MessageAttachment? attachment)
    {
        if (string.IsNullOrWhiteSpace(text) && attachment is null)
        {
            throw new HubException("Message must include text or an attachment.");
        }

        var senderId = GetUserId();

        var message = new Message
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Text = text ?? string.Empty,
            SentAt = DateTime.UtcNow,
            AttachmentUrl = attachment?.Url,
            AttachmentFileName = attachment?.FileName,
            AttachmentContentType = attachment?.ContentType,
            AttachmentSize = attachment?.Size,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync();

        await Clients.Group(UserGroup(receiverId)).SendAsync("ReceiveMessage", message);
        await Clients.Group(UserGroup(senderId)).SendAsync("ReceiveMessage", message);
    }

    public async Task SendGroupMessage(int groupId, string? text, MessageAttachment? attachment)
    {
        if (string.IsNullOrWhiteSpace(text) && attachment is null)
        {
            throw new HubException("Message must include text or an attachment.");
        }

        var senderId = GetUserId();

        var memberIds = await db.GroupMembers
            .Where(gm => gm.GroupId == groupId)
            .Select(gm => gm.UserId)
            .ToListAsync();

        if (!memberIds.Contains(senderId))
        {
            throw new HubException("You are not a member of this group.");
        }

        var message = new GroupMessage
        {
            GroupId = groupId,
            SenderId = senderId,
            Text = text ?? string.Empty,
            SentAt = DateTime.UtcNow,
            AttachmentUrl = attachment?.Url,
            AttachmentFileName = attachment?.FileName,
            AttachmentContentType = attachment?.ContentType,
            AttachmentSize = attachment?.Size,
        };

        db.GroupMessages.Add(message);
        await db.SaveChangesAsync();

        foreach (var memberId in memberIds)
        {
            await Clients.Group(UserGroup(memberId)).SendAsync("ReceiveGroupMessage", message);
        }
    }
}
