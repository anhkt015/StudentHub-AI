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

        type = type.Trim().ToLowerInvariant();
        content = content.Trim();
        mode = NormalizeMode(mode);

        /*
         * =========================================================
         * API KEYS
         * =========================================================
         */

        var tavilyKey =
            _configuration["TAVILY_API_KEY"];

        var geminiKey =
            _configuration["GEMINI_API_KEY"];

        var groqKey =
            _configuration["GROQ_API_KEY"];


        /*
         * =========================================================
         * LAYER 4 RESEARCH
         * =========================================================
         *
         * Layer 3 dã search m?t l?n.
         *
         * Layer 4 KHÔNG ch? tin Layer 3.
         *
         * Nó th?c hi?n m?t research riêng b?ng Tavily.
         *
         * Ðây là research d?c l?p tru?c khi AI suy lu?n.
         */

        var research =
            await ResearchAsync(
                tavilyKey,
                type,
                content
            );


        /*
         * =========================================================
         * COMBINE EVIDENCE
         * =========================================================
         *
         * AI nh?n:
         *
         * 1. Layer 3 evidence
         * 2. Layer 3 sources
         * 3. Layer 4 research evidence
         * 4. Layer 4 research sources
         *
         * AI không t? b?a ngu?n.
         */

        var layer3Evidence =
            layer3.Evidence
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new EvidenceItem
                {
                    Title = x.Title,
                    Url = x.Url,
                    Content = LimitText(
                        x.Content,
                        5000
                    ),
                    Origin = "Layer 3"
                })
                .ToList();

        var researchEvidence =
            research.Evidence;

        var allEvidence =
            layer3Evidence
                .Concat(researchEvidence)
                .GroupBy(
                    x => x.Url,
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(g => g.First())
                .Take(16)
                .ToList();


        var allSources =
            layer3.Sources
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Url))
                .Select(x => new Layer4Source(
                    $"[Layer 3] {x.Title}",
                    x.Url))
                .Concat(
                    research.Sources
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.Url))
                        .Select(x => new Layer4Source(
                            x.Title.StartsWith(
                                "[Layer 4 Research]",
                                StringComparison.OrdinalIgnoreCase)
                                ? x.Title
                                : $"[Layer 4 Research] {x.Title}",
                            x.Url))
                )
                .GroupBy(
                    x => x.Url,
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(g => g.First())
                .Take(20)
                .ToList();


        /*
         * =========================================================
         * MODEL SELECTION
         * =========================================================
         *
         * USER
         *   -> Groq ONLY
         *
         * EXPERT / PRO
         *   -> Gemini ONLY
         *
         * KHÔNG g?i hai model trong cùng m?t request.
         */

        if (mode == "user")
        {
            if (string.IsNullOrWhiteSpace(groqKey))
            {
                return Unknown(
                    "GROQ_API_KEY is not configured."
                );
            }

            var groq =
                await TryGroqAsync(
                    groqKey,
                    type,
                    content,
                    layer3,
                    allEvidence,
                    allSources
                );

            if (groq == null)
            {
                return Unknown(
                    "Groq was unavailable."
                );
            }

            return BuildResult(
                groq.Verdict,
                groq.Confidence,
                groq.EvidenceAgreement,
                groq.SourceQuality,
                groq.Reason,
                groq.ContradictoryEvidence,
                allSources,
                mode,
                "none",
                GroqModel
            );
        }


        /*
         * =========================================================
         * EXPERT / PRO
         * =========================================================
         *
         * Ch? Gemini.
         *
         * 3.7 -> fallback 3.6
         *
         * Không g?i Groq.
         */

        if (string.IsNullOrWhiteSpace(geminiKey))
        {
            return Unknown(
                "GEMINI_API_KEY is not configured."
            );
        }

        var gemini =
            await TryGeminiWithFallbackAsync(
                geminiKey,
                type,
                content,
                mode,
                layer3,
                allEvidence,
                allSources
            );

        if (gemini == null)
        {
            return Unknown(
                "Gemini 3.7 Flash and Gemini 3.6 Flash were unavailable."
            );
        }

        return BuildResult(
            gemini.Verdict,
            gemini.Confidence,
            gemini.EvidenceAgreement,
            gemini.SourceQuality,
            gemini.Reason,
            gemini.ContradictoryEvidence,
            allSources,
            mode,
            gemini.Model,
            null
        );
    }


    /*
     * =============================================================
     * LAYER 4 RESEARCH
     * =============================================================
     *
     * Ðây là research m?i c?a Layer 4.
     *
     * Layer 3 search #1
     * Layer 4 search #2
     *
     * Không ph?i AI search.
     * Tavily th?c hi?n web research.
     *
     * Sau dó M?T AI s? suy lu?n t? toàn b? evidence.
     */

    private async Task<ResearchResult> ResearchAsync(
        string? apiKey,
        string type,
        string claim)
    {
        var result =
            new ResearchResult();

        if (string.IsNullOrWhiteSpace(apiKey))
            return result;

        try
        {
            var client =
                _httpClientFactory.CreateClient();

            client.Timeout =
                TimeSpan.FromSeconds(30);


            /*
             * Query khác Layer 3 m?t chút d? tang
             * kh? nang tìm evidence d?c l?p.
             */

            var query =
                type == "url"
                    ? $"fact check verify {claim}"
                    : $"fact check evidence verify \"{claim}\"";


            var requestBody = new
            {
                api_key = apiKey,
                query = query,
                search_depth = "advanced",
                max_results = 8,
                include_answer = true,
                include_raw_content = false,
                include_images = false
            };


            var json =
                JsonSerializer.Serialize(
                    requestBody
                );


            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.tavily.com/search"
                );


            request.Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                );


            using var response =
                await client.SendAsync(request);


            var responseBody =
                await response.Content.ReadAsStringAsync();


            if (!response.IsSuccessStatusCode)
                return result;


            using var document =
                JsonDocument.Parse(
                    responseBody
                );


            var root =
                document.RootElement;


            if (root.TryGetProperty(
                    "results",
                    out var resultsElement) &&
                resultsElement.ValueKind ==
                    JsonValueKind.Array)
            {
                foreach (
                    var item in
                    resultsElement.EnumerateArray())
                {
                    var title =
                        item.TryGetProperty(
                            "title",
                            out var titleElement)
                            ? titleElement.GetString()
                            ?? "Untitled"
                            : "Untitled";


                    var url =
                        item.TryGetProperty(
                            "url",
                            out var urlElement)
                            ? urlElement.GetString()
                            ?? ""
                            : "";


                    var text =
                        item.TryGetProperty(
                            "content",
                            out var contentElement)
                            ? contentElement.GetString()
                            : null;


                    if (string.IsNullOrWhiteSpace(url))
                        continue;


                    result.Evidence.Add(
                        new EvidenceItem
                        {
                            Title = title,
                            Url = url,
                            Content = LimitText(
                                text,
                                5000
                            ),
                            Origin =
                                "Layer 4 Research"
                        }
                    );


                    result.Sources.Add(
                        new Layer4Source(
                            $"[Layer 4 Research] {title}",
                            url
                        )
                    );
                }
            }


            return result;
        }
        catch
        {
            /*
             * Research failure KHÔNG làm Layer 4 ch?t.
             *
             * AI v?n có th? suy lu?n t? Layer 3.
             */

            return result;
        }
    }


    /*
     * =============================================================
     * GEMINI
     * =============================================================
     */

    private async Task<GeminiAnalysis?>
        TryGeminiWithFallbackAsync(
            string apiKey,
            string type,
            string claim,
            string mode,
            Layer4Layer3Input layer3,
            List<EvidenceItem> evidence,
            List<Layer4Source> sources)
    {
        var models =
            new[]
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
                    type,
                    claim,
                    mode,
                    layer3,
                    evidence,
                    sources
                );


            if (result != null)
                return result;
        }


        return null;
    }


    private async Task<GeminiAnalysis?>
        TryGeminiAsync(
            string apiKey,
            string model,
            string type,
            string claim,
            string mode,
            Layer4Layer3Input layer3,
            List<EvidenceItem> evidence,
            List<Layer4Source> sources)
    {
        try
        {
            var client =
                _httpClientFactory.CreateClient();

            client.Timeout =
                TimeSpan.FromSeconds(60);


            var systemPrompt = """
You are Layer 4 of StudentHub AI Trust.

You are the FINAL verification model.

Your task is to independently determine whether a claim is TRUE,
FAKE, MISLEADING, or UNKNOWN.

You receive:

1. The original user claim.
2. The result produced by Layer 3.
3. Evidence collected by Layer 3.
4. Additional independent web research performed by Layer 4.

IMPORTANT:

Layer 3 is NOT automatically correct.

Layer 3 may say UNKNOWN.
That does NOT mean you must return UNKNOWN.

You must independently reason over ALL supplied evidence.

Compare:
- Layer 3 evidence
- Layer 4 research evidence
- source quality
- agreement between sources
- contradictions
- wording of the claim

Do NOT blindly follow Layer 3.

Do NOT invent facts.

Do NOT invent sources.

Do NOT invent URLs.

Do NOT claim that you personally browsed the internet.

Use only the supplied evidence.

Verdicts:

TRUE
FAKE
MISLEADING
UNKNOWN

TRUE:
Evidence strongly supports the claim.

FAKE:
Evidence strongly contradicts the claim.

MISLEADING:
The claim contains some truth but omits important context,
uses misleading wording, exaggerates, or combines true and false elements.

UNKNOWN:
Available evidence is insufficient or genuinely contradictory.

Important:

A claim containing words such as:
"always"
"never"
"exactly"
"100%"
"guaranteed"
"proves"

requires especially strong evidence.

Do not treat weak evidence as sufficient for absolute claims.

Return ONLY valid JSON.

Required structure:

{
  "verdict": "TRUE",
  "confidence": 0.95,
  "evidenceAgreement": 0.92,
  "sourceQuality": 0.90,
  "reason": "Short evidence-based explanation",
  "contradictoryEvidence": [],
  "sources": []
}

The sources field must ONLY contain URLs supplied in the input.
""";


            var payload =
                new
                {
                    mode,
                    type,
                    claim,

                    layer3 = new
                    {
                        verdict = layer3.Verdict,
                        confidence = layer3.Confidence,
                        reason = layer3.Reason
                    },

                    evidence,

                    sources
                };


            var requestBody =
                new
                {
                    contents =
                        new[]
                        {
                            new
                            {
                                role = "user",
                                parts =
                                    new[]
                                    {
                                        new
                                        {
                                            text =
                                                systemPrompt +
                                                "\n\nINPUT:\n" +
                                                JsonSerializer.Serialize(
                                                    payload
                                                )
                                        }
                                    }
                            }
                        },

                    generationConfig =
                        new
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


            if (response.StatusCode ==
                HttpStatusCode.TooManyRequests)
            {
                return null;
            }


            if (!response.IsSuccessStatusCode)
                return null;


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


            result.Model =
                model;


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
     *
     * USER MODE:
     *
     * Tavily research
     *      ?
     * Groq
     *
     * Không Gemini.
     */

    private async Task<GroqAnalysis?>
        TryGroqAsync(
            string apiKey,
            string type,
            string claim,
            Layer4Layer3Input layer3,
            List<EvidenceItem> evidence,
            List<Layer4Source> sources)
    {
        try
        {
            var client =
                _httpClientFactory.CreateClient();

            client.Timeout =
                TimeSpan.FromSeconds(45);


            var systemPrompt = """
You are Layer 4 of StudentHub AI Trust.

You are the final verification model.

Independently evaluate the user's claim using:
1. Layer 3 result
2. Layer 3 evidence
3. Additional Layer 4 web research

Layer 3 is NOT automatically correct.

If Layer 3 says UNKNOWN, you MUST still analyze the supplied
Layer 3 evidence and Layer 4 research.

Do NOT simply repeat Layer 3.

Do NOT invent facts.

Do NOT invent sources.

Do NOT invent URLs.

Do NOT claim to have browsed the internet yourself.

Use ONLY the supplied evidence.

Verdicts:

TRUE
FAKE
MISLEADING
UNKNOWN

TRUE:
Strong evidence supports the claim.

FAKE:
Strong evidence contradicts the claim.

MISLEADING:
The claim is partly true, exaggerated, incomplete,
or misleadingly worded.

UNKNOWN:
Evidence is insufficient or genuinely contradictory.

Absolute wording such as:
always, never, exactly, guaranteed, 100%

requires especially strong evidence.

Return ONLY JSON.

{
  "verdict": "TRUE",
  "confidence": 0.95,
  "evidenceAgreement": 0.92,
  "sourceQuality": 0.90,
  "reason": "Short explanation",
  "contradictoryEvidence": []
}
""";


            var payload =
                new
                {
                    type,
                    claim,

                    layer3 = new
                    {
                        verdict = layer3.Verdict,
                        confidence = layer3.Confidence,
                        reason = layer3.Reason
                    },

                    evidence,

                    sources
                };


            var requestBody =
                new
                {
                    model = GroqModel,

                    messages =
                        new object[]
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

                    max_completion_tokens = 700,

                    response_format =
                        new
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
                choices.ValueKind !=
                    JsonValueKind.Array ||
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


            result.Model =
                GroqModel;


            return result;
        }
        catch
        {
            return null;
        }
    }


    /*
     * =============================================================
     * RESULT
     * =============================================================
     */

    private static Layer4VerificationResult BuildResult(
        string? verdict,
        double confidence,
        double evidenceAgreement,
        double sourceQuality,
        string? reason,
        List<string>? contradictoryEvidence,
        List<Layer4Source> sources,
        string mode,
        string geminiModel,
        string? groqModel)
    {
        var normalizedVerdict =
            NormalizeVerdict(verdict);

        var finalConfidence =
            Clamp(confidence);

        var finalAgreement =
            Clamp(evidenceAgreement);

        var finalSourceQuality =
            Clamp(sourceQuality);


        /*
         * Backend quy?t d?nh STOP.
         */

        var stop =
            normalizedVerdict != "UNKNOWN" &&
            finalConfidence >= 0.90 &&
            finalAgreement >= 0.85;


        return new Layer4VerificationResult(
            normalizedVerdict,
            finalConfidence,
            finalAgreement,
            finalSourceQuality,
            stop,
            !stop,
            mode,
            geminiModel,
            groqModel,
            reason ??
                "Layer 4 analysis completed.",
            contradictoryEvidence ??
                new List<string>(),
            sources
                .Take(20)
                .ToList()
        );
    }


    /*
     * =============================================================
     * HELPERS
     * =============================================================
     */

    private static string NormalizeMode(
        string? mode)
    {
        return
            mode?.Trim().ToLowerInvariant()
            switch
            {
                "user" => "user",
                "expert" => "expert",
                "pro" => "pro",

                /*
                 * Gi? tuong thích v?i request cu.
                 *
                 * N?u frontend chua g?i mode:
                 * m?c d?nh user d? không vô tình dùng Gemini.
                 */

                _ => "user"
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

        text =
            text.Trim();

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


    /*
     * =============================================================
     * INTERNAL MODELS
     * =============================================================
     */

    private sealed class EvidenceItem
    {
        public string Title { get; set; } = "";

        public string Url { get; set; } = "";

        public string? Content { get; set; }

        public string Origin { get; set; } = "";
    }


    private sealed class ResearchResult
    {
        public List<EvidenceItem> Evidence { get; } =
            new();

        public List<Layer4Source> Sources { get; } =
            new();
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

        public List<string>? ContradictoryEvidence
        {
            get;
            set;
        }

        public string Model { get; set; } = "";
    }
}

