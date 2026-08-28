using api.Data;
using api.Dtos;
using api.Extensions;
using api.Hubs;
using api.Models;
using api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GroupsController(ChatDbContext db, CryptoHelper crypto, IHubContext<ChatHub> hub) : ControllerBase
{
    private int CallerId => User.GetUserId(crypto);

    private Task<GroupMember?> FindMembership(int groupId, int userId) =>
        db.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId);

    // Groups the caller belongs to, each with a last-message preview — mirrors
    // UsersController.GetConversations' in-memory "latest per key" grouping pattern.
    [HttpGet]
    public async Task<ActionResult<List<GroupSummary>>> GetGroups()
    {
        var callerId = CallerId;

        var groupIds = await db.GroupMembers
            .Where(gm => gm.UserId == callerId)
            .Select(gm => gm.GroupId)
            .ToListAsync();

        var groups = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync();

        var memberCounts = await db.GroupMembers
            .Where(gm => groupIds.Contains(gm.GroupId))
            .GroupBy(gm => gm.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);

        var recentMessages = await db.GroupMessages
            .Where(m => groupIds.Contains(m.GroupId))
            .OrderByDescending(m => m.SentAt)
            .Select(m => new { m.GroupId, m.SenderId, m.Text, m.AttachmentFileName, m.SentAt })
            .ToListAsync();

        var latestPerGroup = recentMessages
            .GroupBy(m => m.GroupId)
            .Select(g => g.First()) // already sorted by SentAt desc above
            .ToDictionary(m => m.GroupId);

        var summaries = groups
            .Select(g =>
            {
                latestPerGroup.TryGetValue(g.Id, out var last);
                var preview = last is null
                    ? string.Empty
                    : !string.IsNullOrEmpty(last.Text)
                        ? last.Text
                        : last.AttachmentFileName is not null ? $"\U0001F4CE {last.AttachmentFileName}" : string.Empty;

                return new GroupSummary(
                    g.Id, g.Name, g.Avatar, preview, last?.SentAt,
                    last is not null && last.SenderId == callerId,
                    memberCounts.GetValueOrDefault(g.Id)
                );
            })
            .OrderByDescending(s => s.LastMessageAt ?? DateTime.MinValue)
            .ToList();

        return summaries;
    }

    // 404 (not 403) when the caller isn't a member — avoids confirming a private group's
    // existence to non-members.
    [HttpGet("{groupId:int}")]
    public async Task<ActionResult<GroupDto>> GetGroup(int groupId)
    {
        var membership = await FindMembership(groupId, CallerId);
        if (membership is null) return NotFound();

        return Ok(await BuildGroupDto(groupId));
    }

    [HttpGet("{groupId:int}/messages")]
    public async Task<ActionResult<List<GroupMessage>>> GetMessages(int groupId)
    {
        var membership = await FindMembership(groupId, CallerId);
        if (membership is null) return NotFound();

        var messages = await db.GroupMessages
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        return Ok(messages);
    }

    [HttpPost]
    public async Task<ActionResult<GroupDto>> CreateGroup(CreateGroupRequest request)
    {
        var callerId = CallerId;
        var memberIds = request.MemberIds.Distinct().Where(id => id != callerId).ToList();

        if (memberIds.Count == 0)
        {
            return BadRequest(new { message = "Select at least one other member." });
        }

        var validCount = await db.Users.CountAsync(u => memberIds.Contains(u.Id));
        if (validCount != memberIds.Count)
        {
            return BadRequest(new { message = "One or more selected members do not exist." });
        }

        var now = DateTime.UtcNow;
        var group = new Group { Name = request.Name.Trim(), CreatedById = callerId, CreatedAt = now };
        group.Members.Add(new GroupMember { UserId = callerId, IsAdmin = true, JoinedAt = now });
        foreach (var id in memberIds)
        {
            group.Members.Add(new GroupMember { UserId = id, IsAdmin = false, JoinedAt = now });
        }

        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var dto = await BuildGroupDto(group.Id);

        // Push to every initial member (including the caller, for their other open tabs/devices).
        foreach (var member in dto.Members)
        {
            await hub.Clients.Group(ChatHub.UserGroup(member.UserId)).SendAsync("GroupCreated", dto);
        }

        return CreatedAtAction(nameof(GetGroup), new { groupId = group.Id }, dto);
    }

    [HttpPost("{groupId:int}/members")]
    public async Task<ActionResult<GroupDto>> AddMember(int groupId, AddGroupMemberRequest request)
    {
        var callerMembership = await FindMembership(groupId, CallerId);
        if (callerMembership is null) return NotFound();
        if (!callerMembership.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only group admins can add members." });
        }

        var userExists = await db.Users.AnyAsync(u => u.Id == request.UserId);
        if (!userExists) return BadRequest(new { message = "User not found." });

        var alreadyMember = await db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == request.UserId);
        if (alreadyMember) return Conflict(new { message = "User is already a member of this group." });

        db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = request.UserId, IsAdmin = false, JoinedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var dto = await BuildGroupDto(groupId);
        foreach (var member in dto.Members)
        {
            await hub.Clients.Group(ChatHub.UserGroup(member.UserId)).SendAsync("GroupMemberAdded", dto);
        }

        return Ok(dto);
    }

    // Removing someone else. Self-removal goes through /leave instead, since it has different
    // rules (a sole member leaving deletes the group; a lone admin can't just be "removed").
    [HttpDelete("{groupId:int}/members/{userId:int}")]
    public async Task<ActionResult> RemoveMember(int groupId, int userId)
    {
        var callerId = CallerId;
        if (userId == callerId)
        {
            return BadRequest(new { message = "Use the leave endpoint to remove yourself." });
        }

        var callerMembership = await FindMembership(groupId, callerId);
        if (callerMembership is null) return NotFound();
        if (!callerMembership.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only group admins can remove members." });
        }

        var target = await FindMembership(groupId, userId);
        if (target is null) return NotFound(new { message = "User is not a member of this group." });

        if (target.IsAdmin)
        {
            var adminCount = await db.GroupMembers.CountAsync(m => m.GroupId == groupId && m.IsAdmin);
            if (adminCount <= 1)
            {
                return Conflict(new { message = "Cannot remove the last admin. Promote another member first." });
            }
        }

        var memberIdsBeforeRemoval = await db.GroupMembers
            .Where(m => m.GroupId == groupId)
            .Select(m => m.UserId)
            .ToListAsync();

        db.GroupMembers.Remove(target);
        await db.SaveChangesAsync();

        foreach (var memberId in memberIdsBeforeRemoval)
        {
            await hub.Clients.Group(ChatHub.UserGroup(memberId)).SendAsync("GroupMemberRemoved", groupId, userId);
        }

        return NoContent();
    }

    [HttpPost("{groupId:int}/leave")]
    public async Task<ActionResult> LeaveGroup(int groupId)
    {
        var callerId = CallerId;
        var membership = await FindMembership(groupId, callerId);
        if (membership is null) return NotFound();

        var allMemberships = await db.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync();

        if (allMemberships.Count == 1)
        {
            // Sole remaining member is always the last admin too — leaving deletes the group.
            var group = await db.Groups.FirstAsync(g => g.Id == groupId);
            db.Groups.Remove(group); // cascades GroupMembers + GroupMessages
            await db.SaveChangesAsync();

            await hub.Clients.Group(ChatHub.UserGroup(callerId)).SendAsync("GroupDeleted", groupId);
            return NoContent();
        }

        if (membership.IsAdmin && allMemberships.Count(m => m.IsAdmin) == 1)
        {
            return Conflict(new { message = "You are the last admin. Promote another member before leaving." });
        }

        db.GroupMembers.Remove(membership);
        await db.SaveChangesAsync();

        foreach (var m in allMemberships)
        {
            await hub.Clients.Group(ChatHub.UserGroup(m.UserId)).SendAsync("GroupMemberRemoved", groupId, callerId);
        }

        return NoContent();
    }

    [HttpPost("{groupId:int}/members/{userId:int}/promote")]
    public async Task<ActionResult> PromoteMember(int groupId, int userId)
    {
        var callerMembership = await FindMembership(groupId, CallerId);
        if (callerMembership is null) return NotFound();
        if (!callerMembership.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only group admins can promote members." });
        }

        var target = await FindMembership(groupId, userId);
        if (target is null) return NotFound(new { message = "User is not a member of this group." });

        target.IsAdmin = true;
        await db.SaveChangesAsync();

        var memberIds = await db.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();
        foreach (var memberId in memberIds)
        {
            await hub.Clients.Group(ChatHub.UserGroup(memberId)).SendAsync("GroupMemberRoleChanged", groupId, userId, true);
        }

        return NoContent();
    }

    [HttpPost("{groupId:int}/members/{userId:int}/demote")]
    public async Task<ActionResult> DemoteMember(int groupId, int userId)
    {
        var callerMembership = await FindMembership(groupId, CallerId);
        if (callerMembership is null) return NotFound();
        if (!callerMembership.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only group admins can demote members." });
        }

        var target = await FindMembership(groupId, userId);
        if (target is null) return NotFound(new { message = "User is not a member of this group." });

        if (target.IsAdmin)
        {
            var adminCount = await db.GroupMembers.CountAsync(m => m.GroupId == groupId && m.IsAdmin);
            if (adminCount <= 1)
            {
                return Conflict(new { message = "Cannot demote the last admin." });
            }
        }

        target.IsAdmin = false;
        await db.SaveChangesAsync();

        var memberIds = await db.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();
        foreach (var memberId in memberIds)
        {
            await hub.Clients.Group(ChatHub.UserGroup(memberId)).SendAsync("GroupMemberRoleChanged", groupId, userId, false);
        }

        return NoContent();
    }

    [HttpPut("{groupId:int}")]
    public async Task<ActionResult<GroupDto>> RenameGroup(int groupId, RenameGroupRequest request)
    {
        var callerMembership = await FindMembership(groupId, CallerId);
        if (callerMembership is null) return NotFound();
        if (!callerMembership.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only group admins can rename the group." });
        }

        var group = await db.Groups.FirstAsync(g => g.Id == groupId);
        group.Name = request.Name.Trim();
        await db.SaveChangesAsync();

        var memberIds = await db.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();
        foreach (var memberId in memberIds)
        {
            await hub.Clients.Group(ChatHub.UserGroup(memberId)).SendAsync("GroupRenamed", groupId, group.Name);
        }

        return Ok(await BuildGroupDto(groupId));
    }

    [HttpPut("{groupId:int}/avatar")]
    public async Task<ActionResult<GroupDto>> UpdateGroupAvatar(int groupId, UpdateGroupAvatarRequest request)
    {
        var callerMembership = await FindMembership(groupId, CallerId);
        if (callerMembership is null) return NotFound();
        if (!callerMembership.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only group admins can change the group photo." });
        }

        var group = await db.Groups.FirstAsync(g => g.Id == groupId);
        group.Avatar = request.Avatar.Trim();
        await db.SaveChangesAsync();

        var memberIds = await db.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();
        foreach (var memberId in memberIds)
        {
            await hub.Clients.Group(ChatHub.UserGroup(memberId)).SendAsync("GroupAvatarChanged", groupId, group.Avatar);
        }

        return Ok(await BuildGroupDto(groupId));
    }

    [HttpDelete("{groupId:int}")]
    public async Task<ActionResult> DeleteGroup(int groupId)
    {
        var callerMembership = await FindMembership(groupId, CallerId);
        if (callerMembership is null) return NotFound();
        if (!callerMembership.IsAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only group admins can delete the group." });
        }

        var memberIds = await db.GroupMembers.Where(m => m.GroupId == groupId).Select(m => m.UserId).ToListAsync();

        var group = await db.Groups.FirstAsync(g => g.Id == groupId);
        db.Groups.Remove(group);
        await db.SaveChangesAsync();

        foreach (var memberId in memberIds)
        {
            await hub.Clients.Group(ChatHub.UserGroup(memberId)).SendAsync("GroupDeleted", groupId);
        }

        return NoContent();
    }

    private async Task<GroupDto> BuildGroupDto(int groupId)
    {
        var group = await db.Groups.AsNoTracking().FirstAsync(g => g.Id == groupId);

        var members = await db.GroupMembers
            .AsNoTracking()
            .Where(gm => gm.GroupId == groupId)
            .Join(db.Users, gm => gm.UserId, u => u.Id,
                (gm, u) => new GroupMemberDto(u.Id, u.Name, u.Avatar, u.IsOnline, gm.IsAdmin, gm.JoinedAt))
            .ToListAsync();

        return new GroupDto(group.Id, group.Name, group.Avatar, group.CreatedById, group.CreatedAt, members);
    }
}
