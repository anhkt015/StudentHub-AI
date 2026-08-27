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
            return new Layer2VerificationResult(
                "UNKNOWN",
                0,
                "Verification type is required.",
                new List<Layer2ProviderResult>()
            );
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new Layer2VerificationResult(
                "UNKNOWN",
                0,
                "Content is required.",
                new List<Layer2ProviderResult>()
            );
        }

        type = type.Trim().ToLowerInvariant();

        // Layer 2 hiện tại:
        // - URL  -> Google Safe Browsing
        // - IMAGE -> chưa tích hợp provider
        // - TEXT -> chưa tích hợp provider

        if (type != "url")
        {
            return new Layer2VerificationResult(
                "UNKNOWN",
                0,
                $"No Layer 2 provider is configured for type '{type}'.",
                new List<Layer2ProviderResult>()
            );
        }

        return await VerifyUrlWithGoogleSafeBrowsingAsync(content.Trim());
    }

    private async Task<Layer2VerificationResult>
        VerifyUrlWithGoogleSafeBrowsingAsync(string url)
    {
        var apiKey = _configuration["GoogleSafeBrowsing:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new Layer2VerificationResult(
                "UNKNOWN",
                0,
                "Google Safe Browsing API key is not configured.",
                new List<Layer2ProviderResult>
                {
                    new Layer2ProviderResult(
                        "Google Safe Browsing",
                        false,
                        "UNKNOWN",
                        0,
                        "API key is missing."
                    )
                }
            );
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            return new Layer2VerificationResult(
                "UNKNOWN",
                0,
                "Invalid URL.",
                new List<Layer2ProviderResult>
                {
                    new Layer2ProviderResult(
                        "Google Safe Browsing",
                        false,
                        "UNKNOWN",
                        0,
                        "URL must start with http:// or https://."
                    )
                }
            );
        }

        try
        {
            var client = _httpClientFactory.CreateClient();

            var endpoint =
                $"https://safebrowsing.googleapis.com/v4/threatMatches:find?key={Uri.EscapeDataString(apiKey)}";

            var requestBody = new
            {
                client = new
                {
                    clientId = "StudentHub-AI",
                    clientVersion = "1.0.0"
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
                            url = uri.ToString()
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);

            using var content = new StringContent(
                json,
                System.Text.Encoding.UTF8,
                "application/json"
            );

            using var response = await client.PostAsync(
                endpoint,
                content
            );

            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new Layer2VerificationResult(
                    "UNKNOWN",
                    0,
                    "Google Safe Browsing request failed.",
                    new List<Layer2ProviderResult>
                    {
                        new Layer2ProviderResult(
                            "Google Safe Browsing",
                            false,
                            "UNKNOWN",
                            0,
                            $"HTTP {(int)response.StatusCode}: {responseBody}"
                        )
                    }
                );
            }

            // Google Safe Browsing normally returns:
            // {} when no threat is found.
            //
            // If a threat is found:
            // {
            //   "matches": [...]
            // }

            using var document =
                JsonDocument.Parse(responseBody);

            var root = document.RootElement;

            var hasMatches =
                root.TryGetProperty("matches", out var matches) &&
                matches.ValueKind == JsonValueKind.Array &&
                matches.GetArrayLength() > 0;

            if (hasMatches)
            {
                var firstMatch = matches[0];

                var threatType = "UNKNOWN";

                if (firstMatch.TryGetProperty(
                        "threatType",
                        out var threatTypeElement))
                {
                    threatType =
                        threatTypeElement.GetString() ?? "UNKNOWN";
                }

                return new Layer2VerificationResult(
                    "DANGEROUS",
                    0.99,
                    $"Google Safe Browsing detected a threat: {threatType}.",
                    new List<Layer2ProviderResult>
                    {
                        new Layer2ProviderResult(
                            "Google Safe Browsing",
                            true,
                            "DANGEROUS",
                            0.99,
                            $"Threat detected: {threatType}."
                        )
                    }
                );
            }

            return new Layer2VerificationResult(
                "SAFE",
                0.95,
                "Google Safe Browsing did not report this URL as a known threat.",
                new List<Layer2ProviderResult>
                {
                    new Layer2ProviderResult(
                        "Google Safe Browsing",
                        true,
                        "SAFE",
                        0.95,
                        "No known Safe Browsing threat was returned."
                    )
                }
            );
        }
        catch (Exception ex)
        {
            return new Layer2VerificationResult(
                "UNKNOWN",
                0,
                "Google Safe Browsing verification failed.",
                new List<Layer2ProviderResult>
                {
                    new Layer2ProviderResult(
                        "Google Safe Browsing",
                        false,
                        "UNKNOWN",
                        0,
                        ex.Message
                    )
                }
            );
        }
    }
}