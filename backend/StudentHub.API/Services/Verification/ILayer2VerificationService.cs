namespace StudentHub.API.Services.Verification;

public interface ILayer2VerificationService
{
    Task<Layer2VerificationResult> VerifyAsync(
        string type,
        string content);
}

public record Layer2VerificationResult(
    string Verdict,
    double Confidence,
    string Reason,
    List<Layer2ProviderResult> Providers
);

public record Layer2ProviderResult(
    string Provider,
    bool Success,
    string Verdict,
    double Confidence,
    string? Message
);