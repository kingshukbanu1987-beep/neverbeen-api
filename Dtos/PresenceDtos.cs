namespace NeverBeen.API.Dtos;

/// <summary>
/// Sent by the website while the signed-in member is using the community (about once a minute,
/// on their first input after a pause, and when they leave the page).
/// </summary>
public class PresenceHeartbeatRequest
{
    /// <summary>
    /// When the member last used the community on the page (UTC). Omitted means now. The API
    /// never records a time in the future or an earlier one than the last recorded use.
    /// </summary>
    public DateTime? LastActivityUtc { get; set; }
}

/// <summary>The member's presence after a presence call.</summary>
public class PresenceDto
{
    /// <summary>The member's own stored status (Active, Busy, Don't Disturb, Inactive or Custom).</summary>
    public string ActiveStatus { get; set; } = "Active";

    /// <summary>When the member was last using the community (UTC).</summary>
    public DateTime? LastSeenUtc { get; set; }
}
