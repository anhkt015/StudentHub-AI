namespace StudentHub.API.Models;

public class User
{
    public int Id { get; set; }

    // ID của user trong Supabase Auth
    public string SupabaseUserId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Role { get; set; } = "Student";

    public int TrustScore { get; set; } = 0;

    public bool UniversityEmailVerified { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
