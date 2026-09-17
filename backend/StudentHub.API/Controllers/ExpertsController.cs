using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentHub.API.Data;
using StudentHub.API.DTOs;
using StudentHub.API.Models;

namespace StudentHub.API.Controllers;

[ApiController]
[Route("api/experts")]
[Authorize]
public class ExpertsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ExpertsController(AppDbContext context)
    {
        _context = context;
    }

    // GET: api/experts/me
    [HttpGet("me")]
    public async Task<ActionResult<ExpertProfileResponseDto>> GetMyProfile()
    {
        var supabaseUserId = User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supabaseUserId))
            return Unauthorized(new { message = "Invalid Supabase token." });

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            return NotFound(new { message = "StudentHub user profile not found." });

        var profile = await _context.ExpertProfiles
            .FirstOrDefaultAsync(e => e.UserId == user.Id);

        if (profile == null)
            return NotFound(new { message = "Expert profile not found." });

        return Ok(ToDto(profile, user));
    }

    // PUT: api/experts/me
    [HttpPut("me")]
    public async Task<ActionResult<ExpertProfileResponseDto>> UpdateMyProfile(
        [FromBody] UpdateExpertProfileRequest request)
    {
        var supabaseUserId = User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supabaseUserId))
            return Unauthorized(new { message = "Invalid Supabase token." });

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            return NotFound(new { message = "StudentHub user profile not found." });

        var profile = await _context.ExpertProfiles
            .FirstOrDefaultAsync(e => e.UserId == user.Id);

        if (profile == null)
        {
            profile = new ExpertProfile
            {
                UserId = user.Id,
                Bio = request.Bio?.Trim() ?? string.Empty,
                Expertise = request.Expertise?.Trim() ?? string.Empty,
                StarLevel = 0,
                CreatedAt = DateTime.UtcNow
            };

            _context.ExpertProfiles.Add(profile);
        }
        else
        {
            profile.Bio = request.Bio?.Trim() ?? string.Empty;
            profile.Expertise = request.Expertise?.Trim() ?? string.Empty;
        }

        await _context.SaveChangesAsync();

        return Ok(ToDto(profile, user));
    }

    private static ExpertProfileResponseDto ToDto(
        ExpertProfile profile,
        User user)
    {
        return new ExpertProfileResponseDto(
            profile.Id,
            profile.UserId,
            user.Email,
            profile.Bio,
            profile.Expertise,
            user.TrustScore,
            profile.StarLevel,
            profile.CreatedAt
        );
    }
}
