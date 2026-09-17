using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentHub.API.Data;
using StudentHub.API.DTOs;

namespace StudentHub.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    // GET: api/users
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponseDto>>> GetUsers()
    {
        var users = await _context.Users
            .Select(u => new UserResponseDto(
                u.Id,
                u.SupabaseUserId,
                u.Email,
                u.FullName,
                u.AvatarUrl,
                u.Role,
                u.TrustScore,
                u.UniversityEmailVerified,
                u.CreatedAt
            ))
            .ToListAsync();

        return Ok(users);
    }

    // ========================================================
    // USER PROFILE
    // GET: api/users/me
    // ========================================================

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponseDto>> GetMyProfile()
    {
        var supabaseUserId = User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supabaseUserId))
            return Unauthorized(new { message = "Invalid Supabase token." });

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            return NotFound(new { message = "User profile not found." });

        return Ok(new UserResponseDto(
            user.Id,
            user.SupabaseUserId,
            user.Email,
            user.FullName,
            user.AvatarUrl,
            user.Role,
            user.TrustScore,
            user.UniversityEmailVerified,
            user.CreatedAt
        ));
    }

    // ========================================================
    // PUT: api/users/me
    // ========================================================

    public record UpdateUserProfileRequest(
        string? FullName,
        string? AvatarUrl
    );

    [HttpPut("me")]
    [Authorize]
    public async Task<ActionResult<UserResponseDto>> UpdateMyProfile(
        [FromBody] UpdateUserProfileRequest request)
    {
        var supabaseUserId = User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supabaseUserId))
            return Unauthorized(new { message = "Invalid Supabase token." });

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId);

        if (user == null)
            return NotFound(new { message = "User profile not found." });

        if (request.FullName != null)
            user.FullName = request.FullName.Trim();

        if (request.AvatarUrl != null)
            user.AvatarUrl = request.AvatarUrl.Trim();

        await _context.SaveChangesAsync();

        return Ok(new UserResponseDto(
            user.Id,
            user.SupabaseUserId,
            user.Email,
            user.FullName,
            user.AvatarUrl,
            user.Role,
            user.TrustScore,
            user.UniversityEmailVerified,
            user.CreatedAt
        ));
    }
}

