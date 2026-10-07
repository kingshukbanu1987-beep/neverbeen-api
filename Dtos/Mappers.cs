using NeverBeen.API.Entities;

namespace NeverBeen.API.Dtos;

/// <summary>Maps a <see cref="UserSettings"/> row to the public <see cref="SettingsDto"/>.</summary>
public static class SettingsDtoMapper
{
    public static SettingsDto FromEntity(UserSettings s) => new()
    {
        EmailNotificationsEnabled = s.EmailNotificationsEnabled,
        PhoneNotificationsEnabled = s.PhoneNotificationsEnabled,
        PublicProfileEnabled = s.PublicProfileEnabled,
        Theme = s.Theme,
        Timezone = s.Timezone,
        IsProfileLocked = s.IsProfileLocked,
        WhoCanMessage = s.WhoCanMessage,
        SearchVisibility = s.SearchVisibility,
        JourneyVisibility = s.JourneyVisibility,
        SoundNotificationsEnabled = s.SoundNotificationsEnabled,
        TwoFactorEnabled = s.TwoFactorEnabled,
        TravelStyles = s.TravelStyles,
        PreferredSeason = s.PreferredSeason,
        WhoCanConnect = s.WhoCanConnect,
        WhoCanVisitProfile = s.WhoCanVisitProfile,
        ShowActiveStatusTo = s.ShowActiveStatusTo,
        WhoCanSeeCompanionsList = s.WhoCanSeeCompanionsList,
        AllowCompanionTagging = s.AllowCompanionTagging,
        ApproveTagsBeforePost = s.ApproveTagsBeforePost,
        IsVerified = s.IsVerified,
        VerificationEmail = s.VerificationEmail,
        VerificationType = s.VerificationType,
        VerifiedAtUtc = s.VerifiedAtUtc
    };
}

public static class ProfileMapper
{
    public static ProfileDto ToDto(UserProfile user, List<GalleryPhotoDto> gallery, int commentCount)
    {
        return new ProfileDto
        {
            Id = user.Id,
            FullName = user.FullName,
            // Own columns on the member row; older rows only have the full name, so the
            // first / last name is split out of it when the column is empty.
            FirstName = SplitFirstName(user),
            LastName = SplitLastName(user),
            Email = user.Email,
            Gender = user.Gender,
            DateOfBirth = user.DateOfBirth,
            Age = CalculateAge(user.DateOfBirth),
            CountryId = user.CountryId,
            CountryName = user.Country?.Name,
            CityId = user.CityId,
            CityName = user.City?.Name,
            State = user.State,
            Pincode = user.Pincode,
            ContactNumber = user.ContactNumber,
            PostalAddress = user.PostalAddress,
            AboutMe = user.AboutMe,
            Profession = user.Profession,
            Status = user.Status,
            ProfilePhotoUrl = user.ProfilePhotoData != null
                ? $"/api/profile/{user.Id}/photo"
                : user.ExternalProfilePictureUrl,
            ExternalProfilePictureUrl = user.ExternalProfilePictureUrl,
            CreatedAtUtc = user.CreatedAtUtc,
            Settings = user.Settings == null
                ? new SettingsDto()
                : SettingsDtoMapper.FromEntity(user.Settings),
            Gallery = gallery,
            CommentCount = commentCount
        };
    }

    /// <summary>
    /// First name of a member: the stored <c>Users.FirstName</c> column when it is filled,
    /// otherwise the first word of the full name (rows created before the column was
    /// populated).
    /// </summary>
    private static string? SplitFirstName(UserProfile user)
    {
        if (!string.IsNullOrWhiteSpace(user.FirstName))
            return user.FirstName.Trim();
        var words = NameWords(user.FullName);
        return words.Length > 0 ? words[0] : null;
    }

    /// <summary>
    /// Last name of a member: the stored <c>Users.LastName</c> column when it is filled,
    /// otherwise everything after the first word of the full name.
    /// </summary>
    private static string? SplitLastName(UserProfile user)
    {
        if (!string.IsNullOrWhiteSpace(user.LastName))
            return user.LastName.Trim();
        var words = NameWords(user.FullName);
        return words.Length > 1 ? string.Join(' ', words.Skip(1)) : null;
    }

    private static string[] NameWords(string? fullName) =>
        (fullName ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static int? CalculateAge(DateTime? dateOfBirth)
    {
        if (dateOfBirth == null)
            return null;

        var today = DateTime.Today;
        var age = today.Year - dateOfBirth.Value.Year;
        if (dateOfBirth.Value.Date > today.AddYears(-age))
            age--;
        return age;
    }
}

/// <summary>Maps user rows to the compact <see cref="AuthorDto"/> used by feeds and chats.</summary>
public static class AuthorMapper
{
    public static AuthorDto From(UserProfile u) => new()
    {
        Id = u.Id,
        UniqueId = u.UniqueId ?? UserProfileStatus.GenerateUniqueId(u.Id),
        FullName = u.FullName,
        ProfilePhotoUrl = u.ProfilePhotoData != null
            ? $"/api/profile/{u.Id}/photo"
            : u.ProfilePhotoUrl ?? u.ExternalProfilePictureUrl ?? string.Empty,
        Profession = u.Profession,
        Country = u.Country?.Name,
        City = u.City?.Name,
        IsVerified = u.IsVerified
    };
}

public static class CommentMapper
{
    public static CommentDto Map(CommunityComment comment, Dictionary<int, int> myReactions)
    {
        return new CommentDto
        {
            Id = comment.Id,
            Text = comment.Text,
            CreatedAtUtc = comment.CreatedAtUtc,
            LikeCount = comment.LikeCount,
            DislikeCount = comment.DislikeCount,
            Author = new AuthorInfoDto
            {
                Id = comment.Author.Id,
                FullName = comment.Author.FullName,
                ProfilePhotoUrl = comment.Author.ProfilePhotoData != null
                    ? $"/api/profile/{comment.Author.Id}/photo"
                    : comment.Author.ExternalProfilePictureUrl,
                Profession = comment.Author.Profession
            },
            MyReaction = myReactions.TryGetValue(comment.Id, out var reaction)
                ? ReactionTypes.TypeToName.GetValueOrDefault(reaction, "Like")
                : null
        };
    }
}
