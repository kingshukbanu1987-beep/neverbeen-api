namespace NeverBeen.API.Common;

public static class AppConstants
{
    public static readonly string[] Genders =
    {
        "Male", "Female", "Other"
    };

    /// <summary>Fixed profession drop-down list used on the registration page.</summary>
    public static readonly string[] Professions =
    {
        "Student",
        "Homemaker",
        "Govt. Service Professional",
        "Private Sector Professional",
        "Business",
        "Retired",
        "Freelancer",
        "Looking for a Job",
        "Blogger",
        "Content Creator",
        "Influencer",
        "Teacher",
        "Others"
    };

    public static readonly string[] Themes =
    {
        "light", "dark", "system"
    };

    public const int MaxProfilePhotoBytes = 5 * 1024 * 1024;   // 5 MB
    public const int MaxGalleryPhotoBytes = 8 * 1024 * 1024;   // 8 MB

    public static readonly string[] AllowedImageTypes =
    {
        "image/jpeg", "image/png", "image/webp", "image/gif"
    };
}
