namespace NeverBeen.API.Dtos;

public class NotificationDto
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public AuthorDto FromUser { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool IsRead { get; set; }
    public long? RequestId { get; set; }
    public string? Status { get; set; }
}

/// <summary>A browser / phone / tablet the member has signed in from.</summary>
public class DeviceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"Phone", "Tablet", "Laptop" or "Desktop".</summary>
    public string Type { get; set; } = "Desktop";
    public string Os { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime LastSeenUtc { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; }
    public bool Blocked { get; set; }
}

/// <summary>Body of POST /api/devices — register (or refresh) a login device.</summary>
public class SaveDeviceRequest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Desktop";
    public string? Os { get; set; }
    public string? Browser { get; set; }
    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public string? Location { get; set; }
    public bool IsCurrent { get; set; }
}
