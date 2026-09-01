namespace StudentHub.API.DTOs.Verification;

public record Layer4VerifyRequest(
    string Type,
    string Content,
    string Mode,
    Layer3VerificationInput Layer3
);

public record Layer3VerificationInput(
    string Verdict,
    double Confidence,
    string Reason,
    List<Layer3EvidenceInput> Evidence,
    List<Layer3SourceInput> Sources
);

public record Layer3EvidenceInput(
    string Title,
    string Url,
    string? Content
);

public record Layer3SourceInput(
    string Title,
    string Url
);
