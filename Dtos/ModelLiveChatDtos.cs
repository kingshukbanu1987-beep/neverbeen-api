namespace NeverBeen.API.Dtos;

/// <summary>One model's chat counters, as GET /api/models/live-chats returns them.</summary>
public class ModelLiveChatCountDto
{
    public string ModelSlug { get; set; } = string.Empty;

    /// <summary>How many chat sessions are live with her right now.</summary>
    public int LiveNowCount { get; set; }

    /// <summary>How many chats are already over — ended, or run out of their two hours.</summary>
    public int PastChatCount { get; set; }
}

/// <summary>The body of POST .../live-chats and .../live-chats/end: the session's own key.</summary>
public class ModelLiveChatSessionRequest
{
    public string? SessionKey { get; set; }
}

/// <summary>The answer of POST .../live-chats and .../live-chats/end: the fresh counts.</summary>
public class ModelLiveChatStartDto
{
    public string ModelSlug { get; set; } = string.Empty;
    public string? SessionKey { get; set; }
    public int LiveNowCount { get; set; }
    public int PastChatCount { get; set; }
}
