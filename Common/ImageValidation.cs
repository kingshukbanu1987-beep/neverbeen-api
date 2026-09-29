using Microsoft.AspNetCore.Http;

namespace NeverBeen.API.Common;

/// <summary>Shared upload validation for profile pictures and gallery photos.</summary>
public static class ImageValidation
{
    public static string? ValidateRequired(IFormFile? file, int maxBytes)
    {
        if (file == null || file.Length == 0)
            return "A photo file is required.";
        return ValidateContent(file, maxBytes);
    }

    public static string? ValidateOptional(IFormFile? file, int maxBytes)
    {
        if (file == null || file.Length == 0)
            return null;
        return ValidateContent(file, maxBytes);
    }

    private static string? ValidateContent(IFormFile file, int maxBytes)
    {
        if (file.Length > maxBytes)
            return $"Photo is too large. The maximum allowed size is {maxBytes / (1024 * 1024)} MB.";

        var contentType = (file.ContentType ?? string.Empty).ToLowerInvariant();
        if (!AppConstants.AllowedImageTypes.Contains(contentType))
            return "Unsupported photo format. Please upload a JPEG, PNG, WebP or GIF image.";

        return null;
    }
}
