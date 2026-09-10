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
