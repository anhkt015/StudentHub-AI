namespace StudentHub.API.Services.Verification;

public interface ILayer4VerificationService
{
    Task<Layer4VerificationResult> VerifyAsync(
        string type,
        string content,
        string mode,
        Layer4Layer3Input layer3);
}

public record Layer4VerificationResult(
    string Verdict,
    double Confidence,
    double EvidenceAgreement,
    double SourceQuality,
    bool Stop,
    bool CanContinueToLayer4,
    string Mode,
    string GeminiModel,
    string? GroqModel,
    string Reason,
    List<string> ContradictoryEvidence,
    List<Layer4Source> Sources
);

public record Layer4Layer3Input(
    string Verdict,
    double Confidence,
    string Reason,
    List<Layer4Evidence> Evidence,
    List<Layer4Source> Sources
);

public record Layer4Evidence(
    string Title,
    string Url,
    string? Content
);

public record Layer4Source(
    string Title,
    string Url
);
