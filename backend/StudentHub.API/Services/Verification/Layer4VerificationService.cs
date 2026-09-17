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

    // Groq dùng cho TEXT / URL
    private const string GroqModel = "openai/gpt-oss-120b";

    // Groq Vision dùng cho IMAGE
    // qwen/qwen3.6-27b có thể không được account hiện tại cấp quyền.
    private const string GroqVisionModel = "qwen/qwen3.8-27b";

    public Layer4VerificationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }


    /*
     * =============================================================
     * TEXT / URL
     * =============================================================
     */

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
                        1500
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
                .Take(8)
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
         * KHÔNG gọi hai model trong cùng một request.
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
         * Chỉ Gemini.
         *
         * 3.7 -> fallback 3.6
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


            var query =
                type == "url"
                    ? $"fact check verify {claim}"
                    : $"fact check evidence verify \"{claim}\"";


            var requestBody = new
            {
                api_key = apiKey,
                query = query,
                search_depth = "advanced",
                max_results = 5,
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
                                1500
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
             * Research failure không làm Layer 4 chết.
             *
             * AI vẫn có thể suy luận từ Layer 3.
             */

            return result;
        }
    }


    /*
     * =============================================================
     * GEMINI TEXT / URL
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
     * GROQ TEXT / URL
     * =============================================================
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
            {
                throw new Exception(
                    $"Groq HTTP {(int)response.StatusCode} ({response.StatusCode}): {body}"
                );
            }


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
        catch (Exception ex)
        {
            throw new Exception(
                $"Groq request failed: {ex.GetType().Name}: {ex.Message}",
                ex
            );
        }
    }


    /*
     * =============================================================
     * IMAGE VERIFICATION
     * =============================================================
     *
     * USER
     *   -> Groq Vision ONLY
     *
     * EXPERT / PRO
     *   -> Gemini Vision ONLY
     *
     * Layer 2:
     *   Sightengine
     *
     * Layer 3:
     *   External contextual evidence
     *
     * Layer 4:
     *   AI final reasoning
     */

    public async Task<Layer4VerificationResult> VerifyImageAsync(
        IFormFile image,
        string mode,
        Layer2VerificationResult layer2,
        Layer4Layer3Input layer3)
    {
        if (image == null || image.Length == 0)
        {
            return Unknown("Image is required.");
        }

        if (layer2 == null)
        {
            return Unknown("Layer 2 image result is required.");
        }

        if (layer3 == null)
        {
            return Unknown("Layer 3 result is required.");
        }

        if (image.Length > 10 * 1024 * 1024)
        {
            return Unknown("Image exceeds the 10 MB limit.");
        }

        mode = NormalizeMode(mode);

        try
        {
            /*
             * =====================================================
             * READ IMAGE
             * =====================================================
             */

            using var memoryStream =
                new MemoryStream();

            await image.CopyToAsync(memoryStream);

            var imageBytes =
                memoryStream.ToArray();

            var imageBase64 =
                Convert.ToBase64String(
                    imageBytes
                );

            var mimeType =
                string.IsNullOrWhiteSpace(image.ContentType)
                    ? "image/jpeg"
                    : image.ContentType;


            /*
             * =====================================================
             * COMPACT LAYER 3 EVIDENCE
             * =====================================================
             */

            var evidence =
                layer3.Evidence
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Url))
                    .Select(x =>
                        new EvidenceItem
                        {
                            Title = x.Title,
                            Url = x.Url,
                            Content = LimitText(
                                x.Content,
                                1200
                            ),
                            Origin = "Layer 3"
                        })
                    .GroupBy(
                        x => x.Url,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Select(g => g.First())
                    .Take(6)
                    .ToList();


            var sources =
                layer3.Sources
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Url))
                    .GroupBy(
                        x => x.Url,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Select(g => g.First())
                    .Take(10)
                    .ToList();


            /*
             * =====================================================
             * USER -> GROQ VISION
             * =====================================================
             */

            if (mode == "user")
            {
                var groqKey =
                    _configuration["GROQ_API_KEY"];

                if (string.IsNullOrWhiteSpace(groqKey))
                {
                    return Unknown(
                        "GROQ_API_KEY is not configured."
                    );
                }


                var result =
                    await TryGroqImageAsync(
                        groqKey,
                        mode,
                        mimeType,
                        imageBase64,
                        layer2,
                        layer3,
                        evidence,
                        sources
                    );


                if (result == null)
                {
                    return Unknown(
                        "Groq Vision was unavailable for image verification."
                    );
                }


                /*
                 * GroqAnalysis không có Sources.
                 *
                 * Sources lấy từ Layer 3.
                 */

                return BuildResult(
                    result.Verdict,
                    result.Confidence,
                    result.EvidenceAgreement,
                    result.SourceQuality,
                    result.Reason,
                    result.ContradictoryEvidence,
                    sources,
                    mode,
                    "none",
                    result.Model
                );
            }


            /*
             * =====================================================
             * EXPERT / PRO -> GEMINI VISION
             * =====================================================
             */

            var geminiKey =
                _configuration["GEMINI_API_KEY"];

            if (string.IsNullOrWhiteSpace(geminiKey))
            {
                return Unknown(
                    "GEMINI_API_KEY is not configured."
                );
            }


            /*
             * Gemini 3.7
             */

            var resultGemini =
                await TryGeminiImageAsync(
                    geminiKey,
                    Gemini37,
                    mode,
                    mimeType,
                    imageBase64,
                    layer2,
                    layer3,
                    evidence,
                    sources
                );


            /*
             * Gemini 3.6 fallback
             */

            if (resultGemini == null)
            {
                resultGemini =
                    await TryGeminiImageAsync(
                        geminiKey,
                        Gemini36,
                        mode,
                        mimeType,
                        imageBase64,
                        layer2,
                        layer3,
                        evidence,
                        sources
                    );
            }


            if (resultGemini == null)
            {
                return Unknown(
                    "Gemini Vision was unavailable for image verification."
                );
            }


            return BuildResult(
                resultGemini.Verdict,
                resultGemini.Confidence,
                resultGemini.EvidenceAgreement,
                resultGemini.SourceQuality,
                resultGemini.Reason,
                resultGemini.ContradictoryEvidence,
                sources,
                mode,
                resultGemini.Model,
                null
            );
        }
        catch (Exception ex)
        {
            return Unknown(
                $"Layer 4 image verification failed: {ex.Message}"
            );
        }
    }


    /*
     * =============================================================
     * GROQ IMAGE
     * =============================================================
     *
     * USER MODE ONLY.
     *
     * Groq OpenAI-compatible multimodal API.
     *
     * Model:
     *   qwen/qwen3.8-27b
     *
     * Input:
     *   - Layer 2 Sightengine
     *   - Layer 3 evidence
     *   - Actual image
     *
     * Output:
     *   - TRUE
     *   - FAKE
     *   - MISLEADING
     *   - UNKNOWN
     */

    private async Task<GroqAnalysis?>
        TryGroqImageAsync(
            string apiKey,
            string mode,
            string mimeType,
            string imageBase64,
            Layer2VerificationResult layer2,
            Layer4Layer3Input layer3,
            List<EvidenceItem> evidence,
            List<Layer4Source> sources)
    {
        try
        {
            var client =
                _httpClientFactory.CreateClient();

            client.Timeout =
                TimeSpan.FromSeconds(90);


            var systemPrompt = """
You are Layer 4 of StudentHub AI Trust.

You are the FINAL verification model for an IMAGE.

You can directly inspect the supplied image.

Determine whether the image should be classified as:

TRUE
FAKE
MISLEADING
UNKNOWN

You must analyze:

1. The actual image.
2. Layer 2 provider results.
3. Layer 3 external evidence.
4. Visual inconsistencies.
5. Possible AI generation.
6. Possible deepfake or face manipulation.
7. Possible image editing or compositing.
8. Context supplied by external evidence.

Layer 2 is evidence, NOT absolute truth.

Layer 3 is evidence, NOT absolute truth.

Do NOT blindly follow Layer 2.

Do NOT blindly follow Layer 3.

You MUST independently inspect the image.

Look for:

- AI-generated artifacts
- unnatural faces
- duplicated objects
- distorted anatomy
- strange hands
- inconsistent lighting
- inconsistent shadows
- impossible reflections
- impossible geometry
- text rendering errors
- unnatural skin or hair
- blending artifacts
- face replacement artifacts
- compositing artifacts
- inconsistent perspective
- inconsistent image quality
- suspicious editing

IMPORTANT:

A real image can still be MISLEADING if it is used
with the wrong context.

A real image does NOT automatically mean the claim is TRUE.

A manipulated image does NOT automatically mean
the surrounding claim is completely FALSE.

Use the supplied evidence to evaluate context.

Do NOT invent facts.

Do NOT invent sources.

Do NOT invent URLs.

Do NOT claim that you personally browsed the internet.

Use ONLY:

- the supplied image
- Layer 2 result
- Layer 3 evidence
- supplied sources

Verdicts:

TRUE
FAKE
MISLEADING
UNKNOWN

TRUE:
The image appears authentic and the supplied context/evidence
supports the interpretation.

FAKE:
Strong visual evidence indicates AI generation,
deepfake, or material manipulation.

MISLEADING:
The image may be real or partly real, but the supplied
context or interpretation is misleading.

UNKNOWN:
Evidence is insufficient or genuinely contradictory.

Confidence must reflect uncertainty.

Do not give 0.99 confidence unless evidence is extremely strong.

Return ONLY valid JSON.

Required structure:

{
  "verdict": "TRUE",
  "confidence": 0.90,
  "evidenceAgreement": 0.85,
  "sourceQuality": 0.80,
  "reason": "Short evidence-based explanation",
  "contradictoryEvidence": []
}
""";


            /*
             * =====================================================
             * PAYLOAD
             * =====================================================
             */

            var payload =
                new
                {
                    mode,

                    layer2 = new
                    {
                        verdict = layer2.Verdict,
                        confidence = layer2.Confidence,
                        reason = layer2.Reason,

                        providers =
                            layer2.Providers
                                .Select(x => new
                                {
                                    provider = x.Provider,
                                    success = x.Success,
                                    verdict = x.Verdict,
                                    confidence = x.Confidence,
                                    message = x.Message
                                })
                                .ToList()
                    },

                    layer3 = new
                    {
                        verdict = layer3.Verdict,
                        confidence = layer3.Confidence,
                        reason = layer3.Reason
                    },

                    evidence,

                    sources
                };


            /*
             * =====================================================
             * GROQ VISION REQUEST
             * =====================================================
             *
             * OpenAI-compatible multimodal format:
             *
             * messages
             *   system
             *   user
             *      text
             *      image_url
             */

            var requestBody =
                new
                {
                    model = GroqVisionModel,

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
                                    new object[]
                                    {
                                        new
                                        {
                                            type = "text",

                                            text =
                                                JsonSerializer.Serialize(
                                                    payload
                                                )
                                        },

                                        new
                                        {
                                            type = "image_url",

                                            image_url =
                                                new
                                                {
                                                    url =
                                                        $"data:{mimeType};base64,{imageBase64}"
                                                }
                                        }
                                    }
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
                await response.Content
                    .ReadAsStringAsync();


            if (response.StatusCode ==
                HttpStatusCode.TooManyRequests)
            {
                return null;
            }


            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"Groq Vision HTTP {(int)response.StatusCode} ({response.StatusCode}): {body}"
                );
            }


            using var document =
                JsonDocument.Parse(
                    body
                );


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
                GroqVisionModel;


            return result;
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"Groq Vision request failed: {ex.GetType().Name}: {ex.Message}",
                ex
            );
        }
    }


    /*
     * =============================================================
     * GEMINI IMAGE
     * =============================================================
     *
     * EXPERT / PRO ONLY.
     */

    private async Task<GeminiAnalysis?>
        TryGeminiImageAsync(
            string apiKey,
            string model,
            string mode,
            string mimeType,
            string imageBase64,
            Layer2VerificationResult layer2,
            Layer4Layer3Input layer3,
            List<EvidenceItem> evidence,
            List<Layer4Source> sources)
    {
        try
        {
            var client =
                _httpClientFactory.CreateClient();

            client.Timeout =
                TimeSpan.FromSeconds(90);


            var systemPrompt = """
You are Layer 4 of StudentHub AI Trust.

You are the FINAL verification model for an IMAGE.

You can directly inspect the supplied image.

Determine whether the image should be classified as:

TRUE
FAKE
MISLEADING
UNKNOWN

For image verification, analyze:

1. Whether the image appears AI-generated.
2. Whether the image appears manipulated or deepfaked.
3. Visible inconsistencies, artifacts, impossible geometry,
   duplicated details, unnatural faces, hands, text, lighting,
   shadows, reflections, anatomy, or compositing.
4. The Layer 2 provider result.
5. Layer 3 external evidence.
6. Agreement and contradictions between evidence.

IMPORTANT:

Sightengine is evidence, not absolute truth.

Do NOT blindly follow Layer 2.

Do NOT blindly follow Layer 3.

Do NOT invent facts.

Do NOT invent sources.

Do NOT invent URLs.

If the visual evidence is insufficient, return UNKNOWN.

Return ONLY valid JSON.

Required structure:

{
  "verdict": "TRUE",
  "confidence": 0.95,
  "evidenceAgreement": 0.90,
  "sourceQuality": 0.80,
  "reason": "Short evidence-based explanation",
  "contradictoryEvidence": [],
  "sources": []
}

The sources field must ONLY contain URLs supplied in the input.

For an image:

TRUE means there is no strong evidence that the image is manipulated
or falsely represented.

FAKE means strong evidence indicates the image is AI-generated,
deepfaked, or materially manipulated.

MISLEADING means the image itself may be real or manipulated
but its available context/evidence makes the presented
interpretation misleading.

UNKNOWN means evidence is insufficient or contradictory.
""";


            var payload =
                new
                {
                    mode,

                    layer2 = new
                    {
                        verdict = layer2.Verdict,
                        confidence = layer2.Confidence,
                        reason = layer2.Reason,

                        providers =
                            layer2.Providers
                                .Select(x => new
                                {
                                    provider = x.Provider,
                                    success = x.Success,
                                    verdict = x.Verdict,
                                    confidence = x.Confidence,
                                    message = x.Message
                                })
                                .ToList()
                    },

                    layer3 = new
                    {
                        verdict = layer3.Verdict,
                        confidence = layer3.Confidence,
                        reason = layer3.Reason
                    },

                    evidence,

                    sources
                };


            var textPart =
                systemPrompt +
                "\n\nINPUT:\n" +
                JsonSerializer.Serialize(
                    payload
                );


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
                                    new object[]
                                    {
                                        new
                                        {
                                            text = textPart
                                        },

                                        new
                                        {
                                            inlineData =
                                                new
                                                {
                                                    mimeType,
                                                    data = imageBase64
                                                }
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
                await response.Content
                    .ReadAsStringAsync();


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
         * Backend quyết định STOP.
         *
         * Điều kiện:
         *
         * verdict != UNKNOWN
         * confidence >= 0.90
         * evidenceAgreement >= 0.85
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
                 * Nếu frontend chưa gửi mode:
                 * mặc định USER để không vô tình gọi Gemini.
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