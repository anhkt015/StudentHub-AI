using StudentHub.API.Services.Verification;

namespace StudentHub.API.DTOs.Verification;

public class Layer3ImageVerifyRequest
{
    public string ImageBase64 { get; set; } = string.Empty;

    public string ContentType { get; set; } = "image/jpeg";

    public Layer2VerificationResult Layer2 { get; set; } = new(
        "UNKNOWN",
        0.0,
        "Missing Layer 2 result.",
        new List<StudentHub.API.Services.Verification.Layer2ProviderResult>()
    );
}

