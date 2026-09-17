using StudentHub.API.Services.Verification;

namespace StudentHub.API.DTOs.Verification;

public class Layer4ImageVerifyRequest
{
    public string ImageBase64 { get; set; } = string.Empty;

    public string ContentType { get; set; } = "image/jpeg";

    public string Mode { get; set; } = "pro";

    public Layer2VerificationResult Layer2 { get; set; } = new(
        "UNKNOWN",
        0.0,
        "Missing Layer 2 result.",
        new List<StudentHub.API.Services.Verification.Layer2ProviderResult>()
    );

    public Layer3VerificationResult Layer3 { get; set; } = new(
        "UNKNOWN",
        0.0,
        false,
        true,
        "Missing Layer 3 result.",
        new List<Layer3Evidence>(),
        new List<Layer3Source>()
    );
}

