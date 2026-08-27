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
                u.Role,
                u.TrustScore,
                u.UniversityEmailVerified,
                u.CreatedAt
            ))
            .ToListAsync();

        return Ok(users);
    }
}
