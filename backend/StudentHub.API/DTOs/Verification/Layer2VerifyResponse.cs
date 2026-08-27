namespace StudentHub.API.DTOs.Verification;

public record Layer2ProviderResult(
    string Provider,
    string Verdict,
    double Confidence,
    string? Reason
);

public record Layer2VerifyResponse(
    string Verdict,
    double Confidence,
    bool Stop,
    bool CanContinueToLayer3,
    List<Layer2ProviderResult> Results
);
