using System.Collections.Generic;
using System.Threading.Tasks;

namespace StudentHub.API.Services.Verification;

public interface ILayer3VerificationService
{
    Task<Layer3VerificationResult> VerifyAsync(
        string type,
        string content);
}

public record Layer3VerificationResult(
    string Verdict,
    double Confidence,
    bool Stop,
    bool CanContinueToLayer4,
    string Reason,
    List<Layer3Evidence> Evidence,
    List<Layer3Source> Sources
);

public record Layer3Evidence(
    string Title,
    string Url,
    string? Content
);

public record Layer3Source(
    string Title,
    string Url
);