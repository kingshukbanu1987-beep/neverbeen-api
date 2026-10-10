namespace NeverBeen.API.Common;

/// <summary>
/// Presence of a member (Users.ActiveStatus and Users.LastSeenUtc):
///  - signing in sets the status to Active (AuthController.OauthLogin);
///  - signing out sets it to Inactive, whatever status the member had (PresenceController.SignOut);
///  - a member who has not used the community for more than <see cref="AwayAfter"/> is Away.
///    Away is automatic: it is never stored as a choice, and when the member uses the community
///    again the status they chose is shown again;
///  - every status except Inactive counts as online (<see cref="IsOnline"/>).
/// </summary>
public static class PresenceRules
{
    /// <summary>How long a member can be away from the community before they are shown as Away.</summary>
    public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The status other members see. Inactive is never overridden. A stored "Away" (from before
    /// Away became automatic) reads as Active. Any other status becomes Away when the member's
    /// last use is more than <see cref="AwayAfter"/> ago, or when it has never been recorded.
    /// </summary>
    public static string Effective(string? storedStatus, DateTime? lastSeenUtc, DateTime nowUtc)
    {
        var status = string.IsNullOrWhiteSpace(storedStatus) ? "Active" : storedStatus.Trim();
        if (status == "Inactive")
            return "Inactive";
        if (status == "Away")
            status = "Active";
        if (lastSeenUtc == null)
            return "Away";
        return nowUtc - AsUtc(lastSeenUtc.Value) > AwayAfter ? "Away" : status;
    }

    /// <summary>
    /// Whether a member belongs in the community's "Online Now" / "Online Companions" lists:
    /// every status except Inactive does — Active, Busy, Don't Disturb, Away and any Custom
    /// status. Only Inactive, chosen by the member or set when they sign out, moves them to
    /// "Offline Companions". The website applies the same rule to the statuses it is given, so
    /// the two lists agree whatever the last heartbeat said.
    /// </summary>
    public static bool IsOnline(string effectiveStatus) => effectiveStatus != "Inactive";

    /// <summary>
    /// The last-seen time a presence report records. A report without a time counts as now; a
    /// time in the future is cut back to now (a clock ahead of ours cannot make the member later
    /// than now); and last seen never moves backwards.
    /// </summary>
    public static DateTime LastSeenFromReport(DateTime? reportedUtc, DateTime? recordedUtc, DateTime nowUtc)
    {
        var at = reportedUtc.HasValue ? AsUtc(reportedUtc.Value) : nowUtc;
        if (at > nowUtc)
            at = nowUtc;
        if (recordedUtc.HasValue && at < AsUtc(recordedUtc.Value))
            at = AsUtc(recordedUtc.Value);
        return at;
    }

    /// <summary>Stored timestamps are UTC (timestamptz); a value without a zone is taken as UTC.</summary>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
