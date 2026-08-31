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

            var query = type == "url"
                ? $"{content} fact check"
                : $"{content} fact check";

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

            using var response =
                await client.SendAsync(request);

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
                answer =
                    answerElement.GetString() ?? "";
            }

            var evidence =
                new List<Layer3Evidence>();

            var sources =
                new List<Layer3Source>();

            if (root.TryGetProperty(
                    "results",
                    out var resultsElement) &&
                resultsElement.ValueKind ==
                    JsonValueKind.Array)
            {
                foreach (var result in
                         resultsElement.EnumerateArray())
                {
                    var title =
                        result.TryGetProperty(
                            "title",
                            out var titleElement)
                            ? titleElement.GetString() ??
                              "Untitled"
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

                    if (!string.IsNullOrWhiteSpace(url))
                    {
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

            var combinedText =
                $"{answer} {string.Join(
                    " ",
                    evidence.Select(x => x.Content ?? "")
                )}"
                .ToLowerInvariant();

            var falseSignals = new[]
            {
                "false",
                "fake",
                "hoax",
                "misleading",
                "debunked",
                "not true",
                "untrue",
                "fabricated",
                "incorrect",
                "false claim",
                "claim is false",
                "information is false"
            };

            var trueSignals = new[]
            {
                "true",
                "accurate",
                "verified",
                "confirmed",
                "correct",
                "supported by evidence"
            };

            var falseCount =
                falseSignals.Count(
                    x => combinedText.Contains(x)
                );

            var trueCount =
                trueSignals.Count(
                    x => combinedText.Contains(x)
                );

            /*
             * Layer 3 is deliberately conservative.
             *
             * We only stop automatically when the web evidence
             * strongly indicates that the claim is false.
             *
             * Otherwise Layer 4 receives the evidence and
             * performs the final AI analysis.
             */

            if (falseCount >= 3 &&
                falseCount > trueCount)
            {
                return new Layer3VerificationResult(
                    "FAKE",
                    0.90,
                    true,
                    false,
                    "Multiple web-search signals indicate that the information is false or has been debunked.",
                    evidence,
                    sources
                );
            }

            if (falseCount >= 2 &&
                falseCount > trueCount &&
                sources.Count >= 3)
            {
                return new Layer3VerificationResult(
                    "FAKE",
                    0.85,
                    true,
                    false,
                    "Multiple web sources contain strong indicators that the information is false.",
                    evidence,
                    sources
                );
            }

            return new Layer3VerificationResult(
                "UNKNOWN",
                0.50,
                false,
                true,
                string.IsNullOrWhiteSpace(answer)
                    ? "Web evidence was collected, but Layer 3 cannot confidently determine whether the information is true or false."
                    : $"Tavily web search returned evidence. Layer 3 will defer the final judgment to Layer 4. Web summary: {answer}",
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