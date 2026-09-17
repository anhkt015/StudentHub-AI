using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;

namespace StudentHub.API.Services.Verification;

public class Layer3VerificationService : ILayer3VerificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public Layer3VerificationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<Layer3VerificationResult> VerifyAsync(
        string type,
        string content)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return Unknown("Verification type is required.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Unknown("Content is required.");
        }

        type = type.Trim().ToLowerInvariant();
        content = content.Trim();

        if (type != "url" && type != "text")
        {
            return Unknown(
                $"Layer 3 currently supports url and text. Received: {type}");
        }

        var apiKey = _configuration["TAVILY_API_KEY"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unknown("TAVILY_API_KEY is not configured.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            /*
             * Layer 3 = Web Evidence Retrieval
             *
             * URL:
             * Search the submitted URL/domain together with fact-check terms.
             *
             * TEXT:
             * Search the exact claim together with fact-check/evidence terms.
             *
             * Layer 3 does NOT make the final truth decision.
             * Layer 4 will analyze the collected evidence.
             */

            var query = BuildQuery(type, content);

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

            var json = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.tavily.com/search"
            );

            request.Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            );

            using var response = await client.SendAsync(request);

            var responseBody =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Unknown(
                    $"Tavily request failed: HTTP {(int)response.StatusCode}.");
            }

            using var document =
                JsonDocument.Parse(responseBody);

            var root = document.RootElement;

            var answer = "";

            if (root.TryGetProperty(
                    "answer",
                    out var answerElement))
            {
                answer = answerElement.GetString() ?? "";
            }

            var evidence = new List<Layer3Evidence>();
            var sources = new List<Layer3Source>();

            if (root.TryGetProperty(
                    "results",
                    out var resultsElement) &&
                resultsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in resultsElement.EnumerateArray())
                {
                    var title =
                        result.TryGetProperty(
                            "title",
                            out var titleElement)
                            ? titleElement.GetString() ?? "Untitled"
                            : "Untitled";

                    var url =
                        result.TryGetProperty(
                            "url",
                            out var urlElement)
                            ? urlElement.GetString() ?? ""
                            : "";

                    var text =
                        result.TryGetProperty(
                            "content",
                            out var contentElement)
                            ? contentElement.GetString()
                            : null;

                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    sources.Add(
                        new Layer3Source(
                            title,
                            url
                        )
                    );

                    evidence.Add(
                        new Layer3Evidence(
                            title,
                            url,
                            text
                        )
                    );
                }
            }

            if (sources.Count == 0)
            {
                return new Layer3VerificationResult(
                    "UNKNOWN",
                    0,
                    false,
                    true,
                    "Tavily returned no usable web sources.",
                    evidence,
                    sources
                );
            }

            /*
             * IMPORTANT:
             *
             * Do not count words such as "false" or "true"
             * inside arbitrary web pages to determine truth.
             *
             * A web page can contain:
             * "This claim is NOT false..."
             * "Some people falsely believe..."
             *
             * Simple keyword counting can therefore produce
             * incorrect FAKE/TRUE decisions.
             *
             * Layer 3 only reports that evidence was found.
             * Layer 4 performs the final AI reasoning.
             */

            var reason =
                BuildReason(type, content, answer, sources.Count);

            return new Layer3VerificationResult(
                "UNKNOWN",
                0.50,
                false,
                true,
                reason,
                evidence,
                sources
            );
        }
        catch (TaskCanceledException)
        {
            return Unknown(
                "Tavily request timed out. Layer 3 is temporarily unavailable."
            );
        }
        catch (Exception ex)
        {
            return Unknown(
                $"Layer 3 verification failed: {ex.Message}"
            );
        }
    }


    public async Task<Layer3VerificationResult> VerifyImageAsync(
    IFormFile image,
    Layer2VerificationResult layer2)
{
    if (image == null || image.Length == 0)
    {
        return Unknown("Image is required.");
    }

    if (layer2 == null)
    {
        return Unknown("Layer 2 image result is required.");
    }

    var apiKey = _configuration["TAVILY_API_KEY"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Unknown("TAVILY_API_KEY is not configured.");
    }

    try
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        /*
         * IMPORTANT:
         *
         * Tavily cannot perform true reverse-image search from
         * raw image bytes.
         *
         * Therefore Layer 3 Image performs contextual web research:
         *
         * 1. original image / original post
         * 2. context / reuse / misleading
         * 3. fact-check / debunk
         *
         * Layer 4 still receives the actual image and performs
         * the final visual reasoning.
         */

        var layer2Context =
            $"Layer 2 image verdict: {layer2.Verdict}. " +
            $"Layer 2 confidence: {layer2.Confidence:0.###}. " +
            $"Provider reason: {layer2.Reason}";

        var queries = new[]
        {
            $"image origin original photo original post source {layer2Context}",

            $"photo image context reused misleading context fact check debunk {layer2Context}",

            $"image fact check debunk false context original source {layer2Context}"
        };

        var evidence = new List<Layer3Evidence>();
        var sources = new List<Layer3Source>();

        foreach (var query in queries)
        {
            var requestBody = new
            {
                api_key = apiKey,
                query,
                search_depth = "advanced",
                max_results = 4,
                include_answer = true,
                include_raw_content = false,
                include_images = false
            };

            var json =
                JsonSerializer.Serialize(requestBody);

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
                continue;

            using var document =
                JsonDocument.Parse(responseBody);

            var root =
                document.RootElement;

            if (!root.TryGetProperty(
                    "results",
                    out var resultsElement) ||
                resultsElement.ValueKind !=
                    JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in
                resultsElement.EnumerateArray())
            {
                var title =
                    result.TryGetProperty(
                        "title",
                        out var titleElement)
                        ? titleElement.GetString()
                          ?? "Untitled"
                        : "Untitled";

                var url =
                    result.TryGetProperty(
                        "url",
                        out var urlElement)
                        ? urlElement.GetString()
                          ?? ""
                        : "";

                var content =
                    result.TryGetProperty(
                        "content",
                        out var contentElement)
                        ? contentElement.GetString()
                        : null;

                if (string.IsNullOrWhiteSpace(url))
                    continue;

                /*
                 * Compact ngay tại Layer 3.
                 *
                 * Layer 4 không cần nhận nguyên bài viết.
                 */
                content =
                    CompactImageEvidence(
                        content,
                        500
                    );

                evidence.Add(
                    new Layer3Evidence(
                        title,
                        url,
                        content
                    )
                );

                sources.Add(
                    new Layer3Source(
                        title,
                        url
                    )
                );
            }
        }

        /*
         * Remove duplicate URLs.
         */
        var uniqueEvidence =
            evidence
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Url))
                .GroupBy(
                    x => x.Url,
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(g => g.First())
                .Take(4)
                .ToList();

        var uniqueSources =
            sources
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.Url))
                .GroupBy(
                    x => x.Url,
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(g => g.First())
                .Take(4)
                .ToList();

        if (uniqueEvidence.Count == 0)
        {
            return new Layer3VerificationResult(
                "UNKNOWN",
                0.50,
                false,
                true,
                "Layer 3 could not find usable web evidence for image origin, context, or fact-checking.",
                new List<Layer3Evidence>(),
                new List<Layer3Source>()
            );
        }

        return new Layer3VerificationResult(
            "UNKNOWN",
            0.50,
            false,
            true,
            $"Layer 3 performed contextual image research for origin, original source, reuse/misleading context, and fact-checking. " +
            $"Layer 2 verdict: {layer2.Verdict} ({layer2.Confidence:0.###}). " +
            $"Only {uniqueEvidence.Count} compact evidence item(s) will be forwarded to Layer 4.",
            uniqueEvidence,
            uniqueSources
        );
    }
    catch (TaskCanceledException)
    {
        return Unknown(
            "Tavily image research timed out."
        );
    }
    catch (Exception ex)
    {
        return Unknown(
            $"Layer 3 image research failed: {ex.Message}"
        );
    }
}

private static string? CompactImageEvidence(
    string? text,
    int maxLength)
{
    if (string.IsNullOrWhiteSpace(text))
        return null;

    text = text.Trim();

    /*
     * Ưu tiên những câu có thông tin:
     *
     * original
     * source
     * published
     * context
     * fact check
     * debunk
     * misleading
     * false
     * reused
     */

    var sentences =
        text.Split(
            new[] { '.', '!', '?' },
            StringSplitOptions.RemoveEmptyEntries
        )
        .Select(x => x.Trim())
        .Where(x => x.Length > 20)
        .ToList();

    var keywords = new[]
    {
        "original",
        "source",
        "published",
        "context",
        "fact check",
        "fact-check",
        "debunk",
        "misleading",
        "false",
        "reused",
        "photo",
        "image"
    };

    var important =
        sentences
            .Where(sentence =>
                keywords.Any(keyword =>
                    sentence.Contains(
                        keyword,
                        StringComparison.OrdinalIgnoreCase)))
            .Take(3)
            .ToList();

    var result =
        important.Count > 0
            ? string.Join(". ", important)
            : text;

    if (result.Length <= maxLength)
        return result;

    return result[..maxLength] + "...";
}
    private static string BuildQuery(
        string type,
        string content)
    {
        if (type == "url")
        {
            return
                $"\"{content}\" fact check scam malware security credibility";
        }

        return
            $"\"{content}\" fact check evidence verified false true debunked";
    }

    private static string BuildReason(
        string type,
        string content,
        string answer,
        int sourceCount)
    {
        var target =
            type == "url"
                ? "submitted URL"
                : "submitted claim";

        if (!string.IsNullOrWhiteSpace(answer))
        {
            return
                $"Tavily collected {sourceCount} web source(s) for the {target}. " +
                $"Layer 3 will defer the final judgment to Layer 4. " +
                $"Web summary: {answer}";
        }

        return
            $"Tavily collected {sourceCount} web source(s) for the {target}. " +
            "Layer 3 cannot confidently determine whether the information is true or false. " +
            "The collected evidence will be passed to Layer 4 for final analysis.";
    }

    private static Layer3VerificationResult Unknown(
        string reason)
    {
        return new Layer3VerificationResult(
            "UNKNOWN",
            0,
            false,
            true,
            reason,
            new List<Layer3Evidence>(),
            new List<Layer3Source>()
        );
    }
}


