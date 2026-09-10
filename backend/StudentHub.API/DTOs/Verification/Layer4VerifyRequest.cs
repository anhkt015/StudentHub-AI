namespace StudentHub.API.DTOs.Verification;

public class Layer4VerifyRequest
{
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public Layer3VerificationInput Layer3 { get; set; } = new();
}

public class Layer3VerificationInput
{
    public string Verdict { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<Layer3EvidenceInput> Evidence { get; set; } = new();
    public List<Layer3SourceInput> Sources { get; set; } = new();
}

public class Layer3EvidenceInput
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Content { get; set; }
}

public class Layer3SourceInput
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
