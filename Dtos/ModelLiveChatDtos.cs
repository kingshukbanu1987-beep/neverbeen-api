namespace NeverBeen.API.Dtos;

/// <summary>One model's Live Chat count, as GET /api/models/live-chats returns it.</summary>
public class ModelLiveChatCountDto
{
    public string ModelSlug { get; set; } = string.Empty;
    public int LiveChatCount { get; set; }
}

/// <summary>The answer of POST /api/models/{modelSlug}/live-chats: the count after the new chat.</summary>
public class ModelLiveChatStartDto
{
    public string ModelSlug { get; set; } = string.Empty;
    public int LiveChatCount { get; set; }
}
