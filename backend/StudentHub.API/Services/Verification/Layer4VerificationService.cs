using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StudentHub.API.Services.Verification;

public class Layer4VerificationService : ILayer4VerificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    private const string Gemini37 = "gemini-3.7-flash";
    private const string Gemini36 = "gemini-3.6-flash";

    private const string GroqModel = "openai/gpt-oss-120b";

    public Layer4VerificationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<Layer4VerificationResult> VerifyAsync(
        string type,
        string content,
        string mode,
        Layer4Layer3Input layer3)
    {
        if (string.IsNullOrWhiteSpace(type))
            return Unknown("Verification type is required.");

        if (string.IsNullOrWhiteSpace(content))
            return Unknown("Content is required.");

        if (layer3 == null)
            return Unknown("Layer 3 result is required.");

        mode = NormalizeMode(mode);

        var geminiKey =
            _configuration["GEMINI_API_KEY"];

        var groqKey =
            _configuration["GROQ_API_KEY"];

        if (string.IsNullOrWhiteSpace(geminiKey))
        {
            return Unknown(
                "GEMINI_API_KEY is not configured."
            );
        }

        /*
         * =========================================================
         * 1. GROQ
         * =========================================================
         *
         * Groq KHÔNG nhận toàn bộ Layer 3.
         *
         * Chỉ lấy các evidence quan trọng nhất.
         */

        var filteredEvidence =
            SelectImportantEvidence(
                layer3.Evidence,
                6,
                1600
            );

        var filteredSources =
            SelectImportantSources(
                layer3.Sources,
                6
            );

        GroqAnalysis? groqResult = null;

        if (!string.IsNullOrWhiteSpace(groqKey))
        {
            groqResult =
                await TryGroqAsync(
                    groqKey,
                    type,
                    content,
                    layer3,
                    filteredEvidence,
                    filteredSources
                );
        }

        /*
         * =========================================================
         * 2. GEMINI
         * =========================================================
         *
         * Gemini nhận FULL evidence từ Layer 3.
         *
         * Model 3.7 -> nếu quota/rate limit
         * Model 3.6 -> fallback.
         */

        var fullEvidence =
            layer3.Evidence
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new
                {
                    title = x.Title,
                    url = x.Url,
                    content = x.Content
                })
                .ToList();

        var fullSources =
            layer3.Sources
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new
                {
                    title = x.Title,
                    url = x.Url
                })
                .ToList();

        var gemini =
            await TryGeminiWithFallbackAsync(
                geminiKey,
                mode,
                type,
                content,
                layer3,
                fullEvidence,
                fullSources,
                groqResult
            );

        if (gemini == null)
        {
            return Unknown(
                "Both Gemini 3.7 Flash and Gemini 3.6 Flash were unavailable."
            );
        }

        var verdict =
            NormalizeVerdict(
                gemini.Verdict
            );

        var confidence =
            Clamp(
                gemini.Confidence
            );

        var agreement =
            Clamp(
                gemini.EvidenceAgreement
            );

        var sourceQuality =
            Clamp(
                gemini.SourceQuality
            );

        /*
         * Backend tự quyết định STOP.
         */

        var stop =
            verdict != "UNKNOWN" &&
            confidence >= 0.90 &&
            agreement >= 0.85;

        var validSources =
            gemini.Sources
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Url) &&
                    layer3.Sources.Any(
                        s =>
                            s.Url.Equals(
                                x.Url,
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
                )
                .Take(10)
                .ToList();

        return new Layer4VerificationResult(
            verdict,
            confidence,
            agreement,
            sourceQuality,
            stop,
            !stop,
            mode,
            gemini.Model,
            groqResult?.Model,
            gemini.Reason ??
                "Layer 4 analysis completed.",
            gemini.ContradictoryEvidence ??
                new List<string>(),
            validSources
        );
    }

    /*
     * =============================================================
     * GEMINI
     * =============================================================
     */

    private async Task<GeminiAnalysis?> TryGeminiWithFallbackAsync(
        string apiKey,
        string mode,
        string type,
        string claim,
        Layer4Layer3Input layer3,
        object fullEvidence,
        object fullSources,
        GroqAnalysis? groq)
    {
        var models = new[]
        {
            Gemini37,
            Gemini36
        };

        foreach (var model in models)
        {
            var result =
                await TryGeminiAsync(
                    apiKey,
                    model,
                    mode,
                    type,
                    claim,
                    layer3,
                    fullEvidence,
                    fullSources,
                    groq
                );

            if (result != null)
                return result;
        }

        return null;
    }

    private async Task<GeminiAnalysis?> TryGeminiAsync(
        string apiKey,
        string model,
        string mode,
        string type,
        string claim,
        Layer4Layer3Input layer3,
        object fullEvidence,
        object fullSources,
        GroqAnalysis? groq)
    {
        try
        {
            var client =
                _httpClientFactory.CreateClient();

            client.Timeout =
                TimeSpan.FromSeconds(60);

            var systemPrompt = """
You are Layer 4 of StudentHub AI Trust.

You are the final expert verification model.

You receive:
1. The user's submitted claim.
2. FULL evidence collected by Layer 3.
3. FULL source list collected by Layer 3.
4. An optional secondary Groq analysis.

IMPORTANT:

Use ONLY the supplied evidence and sources.

Do NOT invent sources.
Do NOT invent URLs.
Do NOT claim that you browsed the internet.
Do NOT fabricate facts.

Gemini has priority over Groq.

Groq is only a secondary cross-check.
Do not blindly follow Groq.

Verdicts:

TRUE
FAKE
MISLEADING
UNKNOWN

TRUE:
The supplied evidence strongly supports the claim.

FAKE:
The supplied evidence strongly contradicts the claim.

MISLEADING:
The claim contains partially true information,
important missing context, or a mixture of true and false elements.

UNKNOWN:
Evidence is insufficient or contradictory.

Evaluate:

confidence:
0 to 1

evidenceAgreement:
0 to 1

sourceQuality:
0 to 1

Prefer sources from:
- government institutions
- universities
- scientific organizations
- official organizations
- established reputable news organizations

Return ONLY valid JSON.

Required structure:

{
  "verdict": "TRUE",
  "confidence": 0.95,
  "evidenceAgreement": 0.92,
  "sourceQuality": 0.90,
  "reason": "Short evidence-based explanation",
  "contradictoryEvidence": [],
  "sources": [
    {
      "title": "Source title",
      "url": "https://example.com"
    }
  ]
}

The sources array may ONLY contain URLs supplied by Layer 3.
""";

            var userPayload =
                new
                {
                    mode,
                    type,
                    claim,

                    layer3 = new
                    {
                        verdict = layer3.Verdict,
                        confidence = layer3.Confidence,
                        reason = layer3.Reason,

                        /*
                         * FULL evidence.
                         */

                        evidence = fullEvidence,
                        sources = fullSources
                    },

                    /*
                     * Groq is secondary evidence only.
                     */

                    groq
                };

            var requestBody =
                new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[]
                            {
                                new
                                {
                                    text =
                                        systemPrompt +
                                        "\n\nINPUT:\n" +
                                        JsonSerializer.Serialize(
                                            userPayload
                                        )
                                }
                            }
                        }
                    },

                    generationConfig = new
                    {
                        temperature = 0.1,
                        responseMimeType =
                            "application/json"
                    }
                };

            var json =
                JsonSerializer.Serialize(
                    requestBody
                );

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
                );

            request.Headers.Add(
                "x-goog-api-key",
                apiKey
            );

            request.Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                );

            using var response =
                await client.SendAsync(
                    request
                );

            var responseBody =
                await response.Content.ReadAsStringAsync();

            /*
             * 429 = quota/rate limit.
             *
             * Caller sẽ thử Gemini 3.6.
             */

            if (response.StatusCode ==
                HttpStatusCode.TooManyRequests)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document =
                JsonDocument.Parse(
                    responseBody
                );

            var root =
                document.RootElement;

            if (!root.TryGetProperty(
                    "candidates",
                    out var candidates) ||
                candidates.ValueKind !=
                    JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var text =
                candidates[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

            if (string.IsNullOrWhiteSpace(text))
                return null;

            var result =
                JsonSerializer.Deserialize<GeminiAnalysis>(
                    text,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

            if (result == null)
                return null;

            result.Model = model;

            return result;
        }
        catch
        {
            return null;
        }
    }

    /*
     * =============================================================
     * GROQ
     * =============================================================
     */

    private async Task<GroqAnalysis?> TryGroqAsync(
        string apiKey,
        string type,
        string claim,
        Layer4Layer3Input layer3,
        List<object> evidence,
        List<object> sources)
    {
        try
        {
            var client =
                _httpClientFactory.CreateClient();

            client.Timeout =
                TimeSpan.FromSeconds(45);

            var systemPrompt = """
You are the secondary verification model of StudentHub AI Trust.

Analyze ONLY the selected evidence supplied to you.

Do not browse.
Do not invent sources.
Do not invent URLs.

Return ONLY JSON.

{
  "verdict": "TRUE",
  "confidence": 0.90,
  "evidenceAgreement": 0.90,
  "sourceQuality": 0.85,
  "reason": "Short explanation"
}

Possible verdicts:
TRUE
FAKE
MISLEADING
UNKNOWN
""";

            var payload =
                new
                {
                    type,
                    claim,

                    /*
                     * IMPORTANT:
                     * Groq receives FILTERED evidence only.
                     */

                    evidence,
                    sources
                };

            var requestBody =
                new
                {
                    model = GroqModel,

                    messages = new object[]
                    {
                        new
                        {
                            role = "system",
                            content = systemPrompt
                        },
                        new
                        {
                            role = "user",
                            content =
                                JsonSerializer.Serialize(
                                    payload
                                )
                        }
                    },

                    temperature = 0.1,
                    max_completion_tokens = 500,

                    response_format = new
                    {
                        type = "json_object"
                    }
                };

            var json =
                JsonSerializer.Serialize(
                    requestBody
                );

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.groq.com/openai/v1/chat/completions"
                );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    apiKey
                );

            request.Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                );

            using var response =
                await client.SendAsync(
                    request
                );

            var body =
                await response.Content.ReadAsStringAsync();

            if (response.StatusCode ==
                HttpStatusCode.TooManyRequests)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            using var document =
                JsonDocument.Parse(body);

            var root =
                document.RootElement;

            if (!root.TryGetProperty(
                    "choices",
                    out var choices) ||
                choices.GetArrayLength() == 0)
            {
                return null;
            }

            var output =
                choices[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

            if (string.IsNullOrWhiteSpace(output))
                return null;

            var result =
                JsonSerializer.Deserialize<GroqAnalysis>(
                    output,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }
                );

            if (result == null)
                return null;

            result.Model = GroqModel;

            return result;
        }
        catch
        {
            return null;
        }
    }

    /*
     * =============================================================
     * SOURCE SELECTION
     * =============================================================
     */

    private static List<object> SelectImportantEvidence(
        List<Layer4Evidence> evidence,
        int maxItems,
        int maxContentLength)
    {
        return evidence
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Url))
            .OrderByDescending(
                x => SourcePriority(x.Url)
            )
            .Take(maxItems)
            .Select(
                x => (object)new
                {
                    title = x.Title,
                    url = x.Url,
                    content =
                        LimitText(
                            x.Content,
                            maxContentLength
                        )
                }
            )
            .ToList();
    }

    private static List<object> SelectImportantSources(
        List<Layer4Source> sources,
        int maxItems)
    {
        return sources
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Url))
            .OrderByDescending(
                x => SourcePriority(x.Url)
            )
            .Take(maxItems)
            .Select(
                x => (object)new
                {
                    title = x.Title,
                    url = x.Url
                }
            )
            .ToList();
    }

    private static int SourcePriority(
        string url)
    {
        try
        {
            var host =
                new Uri(url)
                    .Host
                    .ToLowerInvariant();

            if (host.EndsWith(".gov") ||
                host.EndsWith(".gov.vn") ||
                host.Contains(".gov."))
                return 100;

            if (host.EndsWith(".edu") ||
                host.EndsWith(".edu.vn"))
                return 90;

            if (host.Contains("who.int") ||
                host.Contains("un.org") ||
                host.Contains("nih.gov"))
                return 95;

            if (host.Contains("reuters.com") ||
                host.Contains("apnews.com") ||
                host.Contains("bbc.com"))
                return 80;

            return 50;
        }
        catch
        {
            return 0;
        }
    }

    private static string NormalizeMode(
        string? mode)
    {
        return
            mode?.Trim().ToLowerInvariant()
            switch
            {
                "expert" => "expert",
                _ => "pro"
            };
    }

    private static string NormalizeVerdict(
        string? verdict)
    {
        return
            (verdict ?? "UNKNOWN")
                .Trim()
                .ToUpperInvariant()
            switch
            {
                "TRUE" => "TRUE",
                "FAKE" => "FAKE",
                "MISLEADING" => "MISLEADING",
                _ => "UNKNOWN"
            };
    }

    private static double Clamp(
        double value)
    {
        return Math.Clamp(
            value,
            0.0,
            1.0
        );
    }

    private static string? LimitText(
        string? text,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        return text.Length <= maxLength
            ? text
            : text[..maxLength];
    }

    private static Layer4VerificationResult Unknown(
        string reason)
    {
        return new Layer4VerificationResult(
            "UNKNOWN",
            0,
            0,
            0,
            false,
            false,
            "unknown",
            "none",
            null,
            reason,
            new List<string>(),
            new List<Layer4Source>()
        );
    }

    private sealed class GeminiAnalysis
    {
        public string? Verdict { get; set; }

        public double Confidence { get; set; }

        public double EvidenceAgreement { get; set; }

        public double SourceQuality { get; set; }

        public string? Reason { get; set; }

        public List<string>? ContradictoryEvidence
        {
            get;
            set;
        }

        public List<Layer4Source> Sources
        {
            get;
            set;
        } = new();

        public string Model { get; set; } = "";
    }

    private sealed class GroqAnalysis
    {
        public string? Verdict { get; set; }

        public double Confidence { get; set; }

        public double EvidenceAgreement { get; set; }

        public double SourceQuality { get; set; }

        public string? Reason { get; set; }

        public string Model { get; set; } = "";
    }
}
