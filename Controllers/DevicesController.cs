using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Common;
using NeverBeen.API.Data;
using NeverBeen.API.Dtos;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Controllers;

/// <summary>Login devices: the phones / laptops a member has signed in from.</summary>
[Route("api/devices")]
[ApiController]
[Authorize]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;

    public DevicesController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Login devices of the signed-in member.</summary>
    [HttpGet]
    public async Task<ActionResult<List<DeviceDto>>> GetDevices(CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var rows = await _db.LoginDevices.AsNoTracking()
            .Where(d => d.UserId == myId)
            .OrderByDescending(d => d.LastSeenUtc)
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(d => new DeviceDto
        {
            Id = d.Id,
            Name = d.Name,
            Type = d.Type,
            Os = d.Os ?? string.Empty,
            Browser = d.Browser ?? string.Empty,
            IpAddress = d.IpAddress ?? string.Empty,
            MacAddress = d.MacAddress ?? string.Empty,
            Location = d.Location ?? string.Empty,
            Model = d.Model,
            Country = d.Country,
            City = d.City,
            Locality = d.Locality,
            Latitude = d.Latitude,
            Longitude = d.Longitude,
            LastSeenUtc = d.LastSeenUtc,
            IsCurrent = d.IsCurrent,
            IsActive = d.IsActive,
            Blocked = d.Blocked
        }).ToList());
    }

    /// <summary>Registers (or refreshes) a login device.</summary>
    [HttpPost]
    public async Task<ActionResult<DeviceDto>> Register([FromBody] SaveDeviceRequest request, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        if (string.IsNullOrWhiteSpace(request.Id))
            return BadRequest(new { error = "Device id is required." });

        var device = await _db.LoginDevices
            .FirstOrDefaultAsync(d => d.Id == request.Id && d.UserId == myId, cancellationToken);

        if (device == null)
        {
            device = new LoginDevice { Id = request.Id, UserId = myId };
            _db.LoginDevices.Add(device);
        }

        device.Name = string.IsNullOrWhiteSpace(request.Name) ? device.Name : request.Name.Trim();
        device.Type = request.Type;
        device.Os = request.Os;
        device.Browser = request.Browser;
        // The browser cannot see its own public IP, so the trusted request address
        // is the fallback when the client did not report one itself.
        device.IpAddress = string.IsNullOrWhiteSpace(request.IpAddress) ? RemoteIp() : request.IpAddress;
        device.MacAddress = request.MacAddress;
        device.Location = request.Location;
        device.Model = request.Model;
        device.Country = request.Country;
        device.City = request.City;
        device.Locality = request.Locality;
        device.Latitude = request.Latitude;
        device.Longitude = request.Longitude;
        device.IsCurrent = request.IsCurrent;
        device.LastSeenUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new DeviceDto
        {
            Id = device.Id,
            Name = device.Name,
            Type = device.Type,
            Os = device.Os ?? string.Empty,
            Browser = device.Browser ?? string.Empty,
            IpAddress = device.IpAddress ?? string.Empty,
            MacAddress = device.MacAddress ?? string.Empty,
            Location = device.Location ?? string.Empty,
            Model = device.Model,
            Country = device.Country,
            City = device.City,
            Locality = device.Locality,
            Latitude = device.Latitude,
            Longitude = device.Longitude,
            LastSeenUtc = device.LastSeenUtc,
            IsCurrent = device.IsCurrent,
            IsActive = device.IsActive,
            Blocked = device.Blocked
        });
    }

    /// <summary>Signs a device out (removes it).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(string id, CancellationToken cancellationToken)
    {
        var myId = User.GetUserId();
        var device = await _db.LoginDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == myId, cancellationToken);
        if (device == null)
            return NotFound();

        _db.LoginDevices.Remove(device);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Blocks / unblocks a device (?blocked=false unblocks).</summary>
    [HttpPost("{id}/block")]
    public async Task<IActionResult> Block(string id, [FromQuery] bool blocked = true, CancellationToken cancellationToken = default)
    {
        var myId = User.GetUserId();
        var device = await _db.LoginDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == myId, cancellationToken);
        if (device == null)
            return NotFound();

        device.Blocked = blocked;
        device.IsActive = !blocked;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>The caller's public IP as this server sees it (behind a proxy the X-Forwarded-For chain).</summary>
    private string RemoteIp()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
            return forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }
}
