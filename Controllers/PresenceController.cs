using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;

namespace NeverBeen.API.Controllers;

/// <summary>
/// Presence of the signed-in member: the website reports that they are using the community
/// (POST /api/presence/heartbeat) and that they signed out (POST /api/presence/sign-out).
/// Signing in is recorded by POST /api/auth/oauth/login. The rules are in
/// <see cref="PresenceRules"/>: Away after 15 minutes without use, Inactive after sign-out.
/// </summary>
[ApiController]
[Authorize]
[Route("api/presence")]
public class PresenceController : ControllerBase
{
    private readonly AppDbContext _db;

    public PresenceController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Records when the member last used the community. Never changes their chosen status.</summary>
    [HttpPost("heartbeat")]
    public async Task<ActionResult<PresenceDto>> Heartbeat([FromBody] PresenceHeartbeatRequest? request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user == null)
            return NotFound();

        var lastSeen = PresenceRules.LastSeenFromReport(request?.LastActivityUtc, user.LastSeenUtc, DateTime.UtcNow);
        if (user.LastSeenUtc != lastSeen)
        {
            user.LastSeenUtc = lastSeen;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new PresenceDto { ActiveStatus = user.ActiveStatus, LastSeenUtc = user.LastSeenUtc });
    }

    /// <summary>
    /// Signs the member out: their status becomes Inactive whatever it was, and the time they
    /// signed out is kept as their last seen.
    /// </summary>
    [HttpPost("sign-out")]
    public async Task<ActionResult<PresenceDto>> SignOut(CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == User.GetUserId(), cancellationToken);
        if (user == null)
            return NotFound();

        user.ActiveStatus = "Inactive";
        user.LastSeenUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new PresenceDto { ActiveStatus = user.ActiveStatus, LastSeenUtc = user.LastSeenUtc });
    }
}
