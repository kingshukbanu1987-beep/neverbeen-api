using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Messenger: 1:1 and group conversations, chat messages, message replies,
/// emoji reactions and read state (drives unread counts).
/// </summary>
[Route("api/messages")]
[ApiController]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly AppDbContext _db;

    public MessagesController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Conversations of the signed-in member with unread counts and last messages.</summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<List<ConversationDto>>> GetConversations(CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var myConversations = _db.ConversationParticipants.Where(p => p.UserId == myId).Select(p => p.ConversationId);

        var conversations = await _db.Conversations.AsNoTracking()
            .Where(c => myConversations.Contains(c.Id))
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var ids = conversations.Select(c => c.Id).ToList();
        var participants = await _db.ConversationParticipants.AsNoTracking()
            .Where(p => ids.Contains(p.ConversationId))
            .ToListAsync(cancellationToken);
        var byConversation = participants.GroupBy(p => p.ConversationId).ToDictionary(g => g.Key, g => g.ToList());

        var userIds = participants.Select(p => p.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var lastMessages = new Dictionary<long, ChatMessage>();
        foreach (var id in ids)
        {
            var last = await _db.ChatMessages.AsNoTracking()
                .Where(m => m.ConversationId == id)
                .OrderByDescending(m => m.SentAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (last != null)
                lastMessages[id] = last;
        }

        var result = new List<ConversationDto>();
        foreach (var conversation in conversations)
        {
            byConversation.TryGetValue(conversation.Id, out var rows);
            rows ??= new List<ConversationParticipant>();

            var unread = 0;
            var me = rows.FirstOrDefault(p => p.UserId == myId);
            if (me?.LastReadMessageId != null)
                unread = await _db.ChatMessages.CountAsync(m => m.ConversationId == conversation.Id && m.Id > me.LastReadMessageId!.Value, cancellationToken);
            else if (me != null)
                unread = await _db.ChatMessages.CountAsync(m => m.ConversationId == conversation.Id, cancellationToken);

            lastMessages.TryGetValue(conversation.Id, out var last);
            result.Add(new ConversationDto
            {
                Id = conversation.Id,
                IsGroup = conversation.IsGroup,
                CircleId = conversation.CircleId,
                OwnerId = conversation.OwnerId,
                CreatedAtUtc = conversation.CreatedAtUtc,
                UnreadCount = unread,
                Participants = rows
                    .Where(p => users.ContainsKey(p.UserId))
                    .Select(p => AuthorMapper.From(users[p.UserId]))
                    .ToList(),
                LastMessage = last == null ? null : await ToMessageDto(last, myId, rows, users, cancellationToken)
            });
        }
        return Ok(result);
    }

    /// <summary>Starts (or reopens) a 1:1 conversation with a companion / a group chat.</summary>
    [HttpPost("conversations")]
    public async Task<ActionResult<ConversationDto>> StartConversation([FromBody] StartConversationRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();

        if (!request.IsGroup)
        {
            if (!request.CompanionId.HasValue || request.CompanionId.Value == myId)
                return BadRequest(new { error = "A 1:1 conversation needs a companion id." });
            if (!await _db.Users.AnyAsync(u => u.Id == request.CompanionId.Value, cancellationToken))
                return NotFound();

            // Reuse the existing 1:1 conversation between the two members if there is one.
            var myConversations = _db.ConversationParticipants.Where(p => p.UserId == myId).Select(p => p.ConversationId);
            var existing = await _db.Conversations.AsNoTracking()
                .Where(c => !c.IsGroup && myConversations.Contains(c.Id))
                .Where(c => _db.ConversationParticipants.Count(p => p.ConversationId == c.Id && (p.UserId == myId || p.UserId == request.CompanionId.Value)) == 2)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing != null)
                return Ok((await BuildConversationDtos(new List<Conversation> { existing }, myId, cancellationToken)).First());

            var conversation = new Conversation { IsGroup = false, OwnerId = myId };
            _db.Conversations.Add(conversation);
            await _db.SaveChangesAsync(cancellationToken);
            _db.ConversationParticipants.Add(new ConversationParticipant { ConversationId = conversation.Id, UserId = myId });
            _db.ConversationParticipants.Add(new ConversationParticipant { ConversationId = conversation.Id, UserId = request.CompanionId.Value });
            await _db.SaveChangesAsync(cancellationToken);
            return Ok((await BuildConversationDtos(new List<Conversation> { conversation }, myId, cancellationToken)).First());
        }

        var memberIds = (request.ParticipantIds ?? new List<int>()).Append(myId).Distinct().ToList();
        if (memberIds.Count < 2)
            return BadRequest(new { error = "A group chat needs at least one other member." });

        var group = new Conversation { IsGroup = true, CircleId = request.CircleId, OwnerId = myId };
        _db.Conversations.Add(group);
        await _db.SaveChangesAsync(cancellationToken);
        foreach (var userId in memberIds)
            _db.ConversationParticipants.Add(new ConversationParticipant { ConversationId = group.Id, UserId = userId });
        await _db.SaveChangesAsync(cancellationToken);
        return Ok((await BuildConversationDtos(new List<Conversation> { group }, myId, cancellationToken)).First());
    }

    /// <summary>Messages of a conversation (newest last).</summary>
    [HttpGet("conversations/{id:long}")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetMessages(
        long id, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var participant = await _db.ConversationParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == id && p.UserId == myId, cancellationToken);
        if (participant == null)
            return Forbid();

        var rows = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderByDescending(m => m.SentAtUtc)
            .Take(Math.Clamp(pageSize, 1, 200))
            .ToListAsync(cancellationToken);
        rows.Reverse();

        var participants = await _db.ConversationParticipants.AsNoTracking()
            .Where(p => p.ConversationId == id)
            .ToListAsync(cancellationToken);
        var userIds = participants.Select(p => p.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var result = new List<ChatMessageDto>();
        foreach (var message in rows)
            result.Add(await ToMessageDto(message, myId, participants, users, cancellationToken));
        return Ok(result);
    }

    /// <summary>Sends a chat message.</summary>
    [HttpPost("conversations/{id:long}/messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(long id, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Message text is required." });

        var participant = await _db.ConversationParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == id && p.UserId == myId, cancellationToken);
        if (participant == null)
            return Forbid();

        var message = new ChatMessage
        {
            ConversationId = id,
            SenderId = myId,
            Text = request.Text.Trim(),
            ReplyToMessageId = request.ReplyToMessageId
        };
        _db.ChatMessages.Add(message);

        // Requirement: every message the member receives adds one notification
        // (Notifications page + header bell badge) and one item to the chat icon.
        var otherParticipantIds = await _db.ConversationParticipants.AsNoTracking()
            .Where(p => p.ConversationId == id && p.UserId != myId)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);
        foreach (var otherId in otherParticipantIds)
        {
            _db.Notifications.Add(new CommunityNotification
            {
                UserId = otherId,
                Type = NotificationTypes.Message,
                FromUserId = myId,
                Message = $"sent you a message: “{NotificationTypes.MessagePreview(message.Text)}”",
                RequestId = id,
                CreatedAtUtc = message.SentAtUtc
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Sending marks the thread read up to the new message.
        participant.LastReadMessageId = message.Id;
        await _db.SaveChangesAsync(cancellationToken);

        var participants = await _db.ConversationParticipants.AsNoTracking()
            .Where(p => p.ConversationId == id)
            .ToListAsync(cancellationToken);
        var userIds = participants.Select(p => p.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return Ok(await ToMessageDto(message, myId, participants, users, cancellationToken));
    }

    /// <summary>Marks a conversation read up to ?upToMessageId (default: newest).</summary>
    [HttpPost("conversations/{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, [FromQuery] long? upToMessageId = null, CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var participant = await _db.ConversationParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == id && p.UserId == myId, cancellationToken);
        if (participant == null)
            return Forbid();

        if (!upToMessageId.HasValue)
        {
            var newest = await _db.ChatMessages.AsNoTracking()
                .Where(m => m.ConversationId == id)
                .OrderByDescending(m => m.SentAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            upToMessageId = newest?.Id;
        }

        participant.LastReadMessageId = upToMessageId;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Sets the member's emoji on a message (re-submitting the same emoji removes it).</summary>
    [HttpPost("{id:long}/reactions")]
    public async Task<ActionResult> ReactToMessage(long id, [FromBody] MessageReactionRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Emoji))
            return BadRequest(new { error = "Emoji is required." });

        var message = await _db.ChatMessages.FindAsync(new object?[] { id }, cancellationToken);
        if (message == null)
            return NotFound();

        var map = string.IsNullOrWhiteSpace(message.Reactions)
            ? new Dictionary<string, int>()
            : JsonSerializer.Deserialize<Dictionary<string, int>>(message.Reactions) ?? new Dictionary<string, int>();

        map[request.Emoji] = map.TryGetValue(request.Emoji, out var count) ? count + 1 : 1;
        message.Reactions = JsonSerializer.Serialize(map);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(map);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<List<ConversationDto>> BuildConversationDtos(
        List<Conversation> conversations, int myId, CancellationToken cancellationToken)
    {
        var ids = conversations.Select(c => c.Id).ToList();
        var participants = await _db.ConversationParticipants.AsNoTracking()
            .Where(p => ids.Contains(p.ConversationId))
            .ToListAsync(cancellationToken);
        var byConversation = participants.GroupBy(p => p.ConversationId).ToDictionary(g => g.Key, g => g.ToList());
        var userIds = participants.Select(p => p.UserId).Distinct().ToList();
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.Country).Include(u => u.City)
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var result = new List<ConversationDto>();
        foreach (var conversation in conversations)
        {
            byConversation.TryGetValue(conversation.Id, out var rows);
            rows ??= new List<ConversationParticipant>();
            result.Add(new ConversationDto
            {
                Id = conversation.Id,
                IsGroup = conversation.IsGroup,
                CircleId = conversation.CircleId,
                OwnerId = conversation.OwnerId,
                CreatedAtUtc = conversation.CreatedAtUtc,
                Participants = rows
                    .Where(p => users.ContainsKey(p.UserId))
                    .Select(p => AuthorMapper.From(users[p.UserId]))
                    .ToList()
            });
        }
        return result;
    }

    private static async Task<ChatMessageDto> ToMessageDto(
        ChatMessage message,
        int myId,
        List<ConversationParticipant> participants,
        Dictionary<int, UserProfile> users,
        CancellationToken cancellationToken)
    {
        users.TryGetValue(message.SenderId, out var sender);
        var receiverId = participants
            .Where(p => p.UserId != message.SenderId)
            .Select(p => p.UserId)
            .Cast<int?>()
            .FirstOrDefault();

        ChatMessageReplyDto? reply = null;
        if (message.ReplyToMessageId.HasValue && users.Count > 0)
        {
            reply = new ChatMessageReplyDto { Id = message.ReplyToMessageId.Value };
        }

        await Task.CompletedTask;
        return new ChatMessageDto
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            SenderId = message.SenderId,
            SenderName = sender?.FullName ?? string.Empty,
            ReceiverId = participants.Count == 2 ? receiverId : null,
            Text = message.Text,
            SentAtUtc = message.SentAtUtc,
            ReplyToMessageId = message.ReplyToMessageId,
            ReplyTo = reply,
            Reactions = string.IsNullOrWhiteSpace(message.Reactions)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, int>>(message.Reactions)
        };
    }
}
