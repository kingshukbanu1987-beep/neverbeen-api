namespace NeverBeen.API.Common;

/// <summary>Raised when an OAuth code exchange or profile fetch fails for a known reason (bad code, missing config, ...).</summary>
public class OauthException : Exception
{
    public OauthException(string message) : base(message)
    {
    }
}
