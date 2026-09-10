using System.Net.Http.Json;
using System.Text.Json;

namespace StudentHub.API.Services.Verification;

public class Layer2VerificationService : ILayer2VerificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public Layer2VerificationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<Layer2VerificationResult> VerifyAsync(
        string type,
        string content)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return Unknown(
                "Layer 2 verification type is required.",
                "Layer 2");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Unknown(
                "Layer 2 verification content is required.",
                "Layer 2");
        }

        type = type.Trim().ToLowerInvariant();

        return type switch
        {
            "url" => await VerifyUrlWithGoogleSafeBrowsingAsync(content),
            "text" => await VerifyTextWithGoogleFactCheckAsync(content),
            "image" => Unknown(
                "Image verification is not implemented yet.",
                "Layer 2 Image"),
            _ => Unknown(
                $"Unsupported Layer 2 verification type: {type}",
                "Layer 2")
        };
    }

    // ============================================================
    // LAYER 2 - URL
    // Google Safe Browsing
    // ============================================================

    private async Task<Layer2VerificationResult>
        VerifyUrlWithGoogleSafeBrowsingAsync(string url)
    {
        var apiKey = _configuration["GoogleSafeBrowsing:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unknown(
                "Google Safe Browsing API key is not configured.",
                "Google Safe Browsing");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            var endpoint =
                "https://safebrowsing.googleapis.com/v4/threatMatches:find" +
                $"?key={Uri.EscapeDataString(apiKey)}";

            var requestBody = new
            {
                client = new
                {
                    clientId = "StudentHub-AI",
                    clientVersion = "1.0"
                },
                threatInfo = new
                {
                    threatTypes = new[]
                    {
                        "MALWARE",
                        "SOCIAL_ENGINEERING",
                        "UNWANTED_SOFTWARE",
                        "POTENTIALLY_HARMFUL_APPLICATION"
                    },
                    platformTypes = new[]
                    {
                        "ANY_PLATFORM"
                    },
                    threatEntryTypes = new[]
                    {
                        "URL"
                    },
                    threatEntries = new[]
                    {
                        new
                        {
                            url
                        }
                    }
                }
            };

            var response = await client.PostAsJsonAsync(
                endpoint,
                requestBody);

            var rawJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new Layer2VerificationResult(
                    "UNKNOWN",
                    0.0,
                    "Google Safe Browsing request failed.",
                    new List<Layer2ProviderResult>
                    {
                        new(
                            "Google Safe Browsing",
                            false,
                            "UNKNOWN",
                            0.0,
                            rawJson)
                    });
            }

            using var document = JsonDocument.Parse(rawJson);

            var root = document.RootElement;

            if (root.TryGetProperty(
                    "matches",
                    out var matches) &&
                matches.ValueKind == JsonValueKind.Array &&
                matches.GetArrayLength() > 0)
            {
                var firstMatch = matches[0];

                string? threatType = null;

                if (firstMatch.TryGetProperty(
                        "threatType",
                        out var threatTypeElement))
                {
                    threatType =
                        threatTypeElement.GetString();
                }

                return new Layer2VerificationResult(
                    "DANGEROUS",
                    0.99,
                    "Google Safe Browsing reported this URL as a known threat.",
                    new List<Layer2ProviderResult>
                    {
                        new(
                            "Google Safe Browsing",
                            true,
                            "DANGEROUS",
                            0.99,
                            $"Threat type: {threatType ?? "Unknown"}")
                    });
            }

            return new Layer2VerificationResult(
                "SAFE",
                0.95,
                "Google Safe Browsing did not report this URL as a known threat.",
                new List<Layer2ProviderResult>
                {
                    new(
                        "Google Safe Browsing",
                        true,
                        "SAFE",
                        0.95,
                        "No known Safe Browsing threat was returned.")
                });
        }
        catch (Exception ex)
        {
            return new Layer2VerificationResult(
                "UNKNOWN",
                0.0,
                "Google Safe Browsing verification failed.",
                new List<Layer2ProviderResult>
                {
                    new(
                        "Google Safe Browsing",
                        false,
                        "UNKNOWN",
                        0.0,
                        ex.Message)
                });
        }
    }

    // ============================================================
    // LAYER 2 - TEXT
    // Google Fact Check Tools API
    //
    // Layer 2 KHÔNG tự kết luận TRUE/FALSE.
    // Chỉ lấy evidence từ Google Fact Check.
    //
    // Thay đổi:
    // - Lấy tối đa 3 review usable.
    // - Giữ nguyên thứ tự Google trả về.
    // - Không tự tạo similarity score.
    // ============================================================

    private async Task<Layer2VerificationResult>
        VerifyTextWithGoogleFactCheckAsync(string text)
    {
        var apiKey =
            _configuration["GoogleFactCheck:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unknown(
                "Google Fact Check API key is not configured.",
                "Google Fact Check");
        }

        try
        {
            var client =
                _httpClientFactory.CreateClient();

            var endpoint =
                "https://factchecktools.googleapis.com/v1alpha1/claims:search" +
                $"?query={Uri.EscapeDataString(text)}" +
                "&pageSize=10" +
                $"&key={Uri.EscapeDataString(apiKey)}";

            var response =
                await client.GetAsync(endpoint);

            var rawJson =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new Layer2VerificationResult(
                    "UNKNOWN",
                    0.0,
                    "Google Fact Check request failed.",
                    new List<Layer2ProviderResult>
                    {
                        new(
                            "Google Fact Check",
                            false,
                            "UNKNOWN",
                            0.0,
                            rawJson)
                    });
            }

            using var document =
                JsonDocument.Parse(rawJson);

            var root =
                document.RootElement;

            if (!root.TryGetProperty(
                    "claims",
                    out var claims) ||
                claims.ValueKind != JsonValueKind.Array ||
                claims.GetArrayLength() == 0)
            {
                return new Layer2VerificationResult(
                    "UNKNOWN",
                    0.0,
                    "Google Fact Check did not find a matching claim review.",
                    new List<Layer2ProviderResult>
                    {
                        new(
                            "Google Fact Check",
                            true,
                            "UNKNOWN",
                            0.0,
                            "No claim review was returned.")
                    });
            }

            // Google đã xếp hạng kết quả.
            // Không tự tạo similarity score.
            var evidence = new List<string>();

            foreach (var claim in claims.EnumerateArray())
            {
                if (!claim.TryGetProperty(
                        "claimReview",
                        out var reviews) ||
                    reviews.ValueKind != JsonValueKind.Array ||
                    reviews.GetArrayLength() == 0)
                {
                    continue;
                }

                foreach (var review in reviews.EnumerateArray())
                {
                    var rating =
                        GetStringProperty(
                            review,
                            "textualRating");

                    var title =
                        GetStringProperty(
                            review,
                            "title");

                    var reviewUrl =
                        GetStringProperty(
                            review,
                            "url");

                    var reviewDate =
                        GetStringProperty(
                            review,
                            "reviewDate");

                    string? publisherName = null;

                    if (review.TryGetProperty(
                            "publisher",
                            out var publisher) &&
                        publisher.ValueKind ==
                            JsonValueKind.Object)
                    {
                        publisherName =
                            GetStringProperty(
                                publisher,
                                "name");
                    }

                    var claimText =
                        GetStringProperty(
                            claim,
                            "text");

                    var message =
                        BuildFactCheckMessage(
                            claimText,
                            rating,
                            title,
                            publisherName,
                            reviewDate,
                            reviewUrl);

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        evidence.Add(message);
                    }

                    // Tối đa 3 evidence.
                    if (evidence.Count >= 3)
                    {
                        break;
                    }
                }

                if (evidence.Count >= 3)
                {
                    break;
                }
            }

            if (evidence.Count == 0)
            {
                return new Layer2VerificationResult(
                    "UNKNOWN",
                    0.0,
                    "Google Fact Check returned claims, but no usable claimReview was found.",
                    new List<Layer2ProviderResult>
                    {
                        new(
                            "Google Fact Check",
                            true,
                            "UNKNOWN",
                            0.0,
                            "Claims were returned but no usable claimReview was found.")
                    });
            }

            var combinedMessage =
                string.Join(
                    Environment.NewLine +
                    Environment.NewLine,
                    evidence.Select(
                        (item, index) =>
                            $"Evidence #{index + 1}:{Environment.NewLine}{item}"));

            return new Layer2VerificationResult(
                "UNKNOWN",
                0.5,
                $"Google Fact Check found {evidence.Count} fact-check review(s). Layer 2 returns evidence only; final truth assessment is deferred to later layers.",
                new List<Layer2ProviderResult>
                {
                    new(
                        "Google Fact Check",
                        true,
                        "UNKNOWN",
                        0.5,
                        combinedMessage)
                });
        }
        catch (Exception ex)
        {
            return new Layer2VerificationResult(
                "UNKNOWN",
                0.0,
                "Google Fact Check verification failed.",
                new List<Layer2ProviderResult>
                {
                    new(
                        "Google Fact Check",
                        false,
                        "UNKNOWN",
                        0.0,
                        ex.Message)
                });
        }
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static string? GetStringProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String =>
                property.GetString(),

            JsonValueKind.Number =>
                property.ToString(),

            JsonValueKind.True =>
                "true",

            JsonValueKind.False =>
                "false",

            _ => property.ToString()
        };
    }

    private static string BuildFactCheckMessage(
        string? claim,
        string? rating,
        string? title,
        string? publisher,
        string? reviewDate,
        string? url)
    {
        var parts =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(claim))
            parts.Add($"Claim: {claim}");

        if (!string.IsNullOrWhiteSpace(rating))
            parts.Add($"Rating: {rating}");

        if (!string.IsNullOrWhiteSpace(title))
            parts.Add($"Review: {title}");

        if (!string.IsNullOrWhiteSpace(publisher))
            parts.Add($"Publisher: {publisher}");

        if (!string.IsNullOrWhiteSpace(reviewDate))
            parts.Add($"Review date: {reviewDate}");

        if (!string.IsNullOrWhiteSpace(url))
            parts.Add($"Source: {url}");

        return string.Join(" | ", parts);
    }

    private static Layer2VerificationResult Unknown(
        string reason,
        string provider)
    {
        return new Layer2VerificationResult(
            "UNKNOWN",
            0.0,
            reason,
            new List<Layer2ProviderResult>
            {
                new(
                    provider,
                    false,
                    "UNKNOWN",
                    0.0,
                    reason)
            });
    }
}
