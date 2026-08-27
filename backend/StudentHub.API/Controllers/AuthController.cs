using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentHub.API.Data;
using StudentHub.API.DTOs;
using StudentHub.API.Models;

namespace StudentHub.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;

    public AuthController(AppDbContext context)
    {
        _context = context;
    }

    // ========================================================
    // GET: api/auth/me
    //
    // Frontend:
    // Authorization: Bearer <SUPABASE_ACCESS_TOKEN>
    // ========================================================

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var supabaseUserId =
            User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supabaseUserId))
        {
            return Unauthorized(new
            {
                message = "Invalid Supabase token."
            });
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.SupabaseUserId == supabaseUserId);

        if (user == null)
        {
            return NotFound(new
            {
                message = "StudentHub user profile not found."
            });
        }

        return Ok(ToDto(user));
    }

    // ========================================================
    // POST: api/auth/sync
    //
    // Gọi sau khi frontend đăng nhập / đăng ký thành công.
    //
    // Supabase Auth:
    //   Email OTP
    //   Email Password
    //   Google
    //
    // StudentHub DB:
    //   User profile
    // ========================================================

    [HttpPost("sync")]
    [Authorize]
    public async Task<IActionResult> SyncProfile(
        SyncProfileRequest request)
    {
        var supabaseUserId =
            User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(supabaseUserId))
        {
            return Unauthorized(new
            {
                message = "Invalid Supabase token."
            });
        }

        // Email lấy từ JWT, KHÔNG tin email frontend gửi lên.
        var email =
            User.FindFirstValue("email")
            ?? string.Empty;

        // Email verification lấy từ token nếu Supabase cung cấp.
        var emailVerified =
            bool.TryParse(
                User.FindFirstValue("email_verified"),
                out var verified
            ) && verified;

        var user = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.SupabaseUserId == supabaseUserId);

        if (user == null)
        {
            user = new User
            {
                SupabaseUserId = supabaseUserId,
                Email = email,
                FullName = request.FullName ?? string.Empty,
                Role = "Student",
                TrustScore = 0,
                UniversityEmailVerified = emailVerified,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                user.Email = email;
            }

            if (!string.IsNullOrWhiteSpace(request.FullName))
            {
                user.FullName = request.FullName;
            }

            user.UniversityEmailVerified = emailVerified;
        }

        await _context.SaveChangesAsync();

        return Ok(ToDto(user));
    }

    // ========================================================
    // Convert User -> DTO
    // ========================================================

    private static UserResponseDto ToDto(User user)
    {
        return new UserResponseDto(
            user.Id,
            user.SupabaseUserId,
            user.Email,
            user.FullName,
            user.Role,
            user.TrustScore,
            user.UniversityEmailVerified,
            user.CreatedAt
        );
    }
}

// ============================================================
// Request dùng khi đồng bộ profile
// ============================================================

public record SyncProfileRequest(
    string? FullName
);
