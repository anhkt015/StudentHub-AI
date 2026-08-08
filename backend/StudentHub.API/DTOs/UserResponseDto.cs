namespace StudentHub.API.DTOs;

public record UserResponseDto(
    int Id,
    string Email,
    string FullName,
    string Role,
    int TrustScore,
    bool UniversityEmailVerified,
    DateTime CreatedAt
);
