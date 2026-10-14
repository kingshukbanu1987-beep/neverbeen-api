using System.Text.Json;

namespace NeverBeen.API.Dtos;

/// <summary>
/// One turn of the AI model Live Chat, as the NeverBeen website sends it to
/// <c>POST /api/ai-chat</c>. The visitor's input travels in <see cref="Message"/> and the model's
/// character profile — her <c>personality/characterdatabase.json</c> — in
/// <see cref="CharacterProfile"/>; her biography — her <c>personality/biography.json</c> — travels
/// with them in <see cref="Biography"/>. Everything else is optional context for a better answer.
/// </summary>
public class AiChatRequest
{
    /// <summary>What the visitor just typed. Required.</summary>
    public string? Message { get; set; }

    /// <summary>
    /// The model's character profile: the contents of
    /// <c>public/NeverBeenModels/&lt;Album&gt;/personality/characterdatabase.json</c>.
    /// Required, and must be a JSON object.
    /// </summary>
    public JsonElement? CharacterProfile { get; set; }

    /// <summary>
    /// The model's biography: the contents of
    /// <c>public/NeverBeenModels/&lt;Album&gt;/personality/biography.json</c> — a <c>meta</c> block,
    /// her own <c>profile</c> (city, age, height, measurements, rate…) and the <c>biography</c> of her
    /// fictional NeverBeen life (family, studies, career, hobbies, memories…).
    /// Optional for older clients; when sent it must be a JSON object.
    /// </summary>
    public JsonElement? Biography { get; set; }

    /// <summary>
    /// The language the visitor has settled into, when the website read one — Romanised Bengali,
    /// Hindi, Urdu, Persian, Arabic, Turkish, Spanish or Malay, written in Latin letters. Optional:
    /// older clients do not send it, and she answers a language in English then.
    /// </summary>
    public AiChatLanguage? Language { get; set; }

    /// <summary>The model's public card (name, handle, city and age).</summary>
    public AiChatModelInfo? Model { get; set; }

    /// <summary>Who she is talking to.</summary>
    public AiChatVisitor? User { get; set; }

    /// <summary>The last messages of the session, oldest first.</summary>
    public List<AiChatHistoryItem>? History { get; set; }

    /// <summary>The website's facts about her, the other models and the studio.</summary>
    public string? Dossier { get; set; }

    /// <summary>What the reading desk made of this turn (meaning, language, tone).</summary>
    public AiChatReading? Reading { get; set; }

    /// <summary>The visitor's personal answers so far and the one question that is due.</summary>
    public AiChatPersonal? Personal { get; set; }
}

/// <summary>
/// The visitor's own language, as the website's reading desk read it: the code, its English name,
/// and whether it was written in Latin letters (Romanised) — which is the one case she answers in
/// that language rather than in English.
/// </summary>
public class AiChatLanguage
{
    /// <summary>The code the website read: <c>bn</c>, <c>hi</c>, <c>ur</c>, <c>fa</c>, <c>ar</c>, <c>tr</c>, <c>es</c> or <c>ms</c>.</summary>
    public string? Code { get; set; }

    /// <summary>The English name of that language: <c>Bengali</c>.</summary>
    public string? Label { get; set; }

    /// <summary>True when it was written in Latin letters, so her answer goes back the same way.</summary>
    public bool? Romanised { get; set; }
}

public class AiChatModelInfo
{
    public string? Slug { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Location { get; set; }
    public string? Age { get; set; }
}

public class AiChatVisitor
{
    public string? Name { get; set; }
    public string? FirstName { get; set; }
}

public class AiChatHistoryItem
{
    /// <summary><c>user</c> for the visitor, <c>model</c> for her.</summary>
    public string? Role { get; set; }
    public string? Text { get; set; }
}

public class AiChatReading
{
    public string? English { get; set; }
    public string? Language { get; set; }
    public string? LanguageName { get; set; }
    public bool? Romanised { get; set; }
    public string? Intent { get; set; }
    public string? Sentiment { get; set; }
    public string? Tone { get; set; }
    public string? About { get; set; }
    public string? Wants { get; set; }
    public string? Attachment { get; set; }
    public string? Advice { get; set; }
    public string? Provider { get; set; }
}

public class AiChatPersonal
{
    public Dictionary<string, string>? Answers { get; set; }
    public string? NextTopic { get; set; }
}

/// <summary>Her answer, as Gemini wrote it.</summary>
public class AiChatResponse
{
    public bool Ok { get; set; } = true;

    /// <summary>The finished answer. Blank lines separate the messages the window sends.</summary>
    public string Reply { get; set; } = string.Empty;

    /// <summary>Always <c>gemini</c>.</summary>
    public string Provider { get; set; } = "gemini";

    /// <summary>The Gemini model that answered (<c>gemini-2.5-flash</c>).</summary>
    public string Model { get; set; } = string.Empty;
}
