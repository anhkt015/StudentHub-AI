namespace StudentHub.API.DTOs;

public record ExpertProfileResponseDto(
    int Id,
    int UserId,
    string Email,
    string Bio,
    string Expertise,
    int TrustScore,
    int StarLevel,
    DateTime CreatedAt
);

public record UpdateExpertProfileRequest(
    string? Bio,
    string? Expertise
);
