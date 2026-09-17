using System.ComponentModel.DataAnnotations;

namespace StudentHub.API.Models;

public class ExpertProfile
{
    public int Id { get; set; }

    // Liên k?t v?i Users.Id
    public int UserId { get; set; }

    [MaxLength(500)]
    public string Bio { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Expertise { get; set; } = string.Empty;

    // 0 - 5 sao
    public int StarLevel { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
