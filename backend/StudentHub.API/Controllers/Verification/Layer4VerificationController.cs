using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StudentHub.API.DTOs.Verification;
using StudentHub.API.Services.Verification;
using System.Text.Json;

namespace StudentHub.API.Controllers.Verification;

[ApiController]
[Route("api/verify")]
public class Layer4VerificationController : ControllerBase
{
    private readonly ILayer4VerificationService _layer4;

    public Layer4VerificationController(
        ILayer4VerificationService layer4)
    {
        _layer4 = layer4;
    }

    [HttpPost("layer4")]
    [AllowAnonymous]
    public async Task<IActionResult> Layer4(
        [FromBody] Layer4VerifyRequest request)
    {
        if (request == null)
        {
            return BadRequest(new
            {
                message = "Request is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return BadRequest(new
            {
                message = "Type is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new
            {
                message = "Content is required."
            });
        }

        if (request.Layer3 == null)
        {
            return BadRequest(new
            {
                message = "Layer 3 result is required."
            });
        }

        var mode =
            string.IsNullOrWhiteSpace(request.Mode)
                ? "pro"
                : request.Mode.Trim().ToLowerInvariant();

        if (mode != "user" && mode != "pro" && mode != "expert")
        {
            return BadRequest(new
            {
                message = "Mode must be user, pro or expert."
            });
        }

        var layer3 =
            new Layer4Layer3Input(
                request.Layer3.Verdict,
                request.Layer3.Confidence,
                request.Layer3.Reason,
                request.Layer3.Evidence
                    .Select(x =>
                        new Layer4Evidence(
                            x.Title,
                            x.Url,
                            x.Content
                        ))
                    .ToList(),
                request.Layer3.Sources
                    .Select(x =>
                        new Layer4Source(
                            x.Title,
                            x.Url
                        ))
                    .ToList()
            );

        var result =
            await _layer4.VerifyAsync(
                request.Type.Trim().ToLowerInvariant(),
                request.Content.Trim(),
                mode,
                layer3
            );

        return Ok(result);
    }

    // ============================================================
    // LAYER 4 IMAGE
    // POST /api/verify/layer4/image
    //
    // multipart/form-data
    // image      = image file
    // mode       = user / pro / expert
    // layer2Json = Layer 2 Image result
    // layer3Json = Layer 3 Image result
    // ============================================================

        // ============================================================
    // LAYER 4 IMAGE
    // POST /api/verify/layer4/image
    //
    // JSON BODY:
    // {
    //   "imageBase64": "...",
    //   "contentType": "image/jpeg",
    //   "mode": "pro",
    //   "layer2": { ... },
    //   "layer3": { ... }
    // }
    // ============================================================

    [HttpPost("layer4/image")]
    [AllowAnonymous]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Layer4Image(
        [FromBody] Layer4ImageVerifyRequest request)
    {
        if (request == null)
        {
            return BadRequest(new
            {
                message = "Request body is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            return BadRequest(new
            {
                message = "ImageBase64 is required."
            });
        }

        if (request.Layer2 == null)
        {
            return BadRequest(new
            {
                message = "Layer 2 result is required."
            });
        }

        if (request.Layer3 == null)
        {
            return BadRequest(new
            {
                message = "Layer 3 result is required."
            });
        }

        var mode =
            string.IsNullOrWhiteSpace(request.Mode)
                ? "pro"
                : request.Mode.Trim().ToLowerInvariant();

        if (mode != "user" &&
            mode != "pro" &&
            mode != "expert")
        {
            return BadRequest(new
            {
                message = "Mode must be user, pro or expert."
            });
        }

        byte[] imageBytes;

        try
        {
            imageBytes = Convert.FromBase64String(request.ImageBase64);
        }
        catch (FormatException)
        {
            return BadRequest(new
            {
                message = "ImageBase64 is invalid."
            });
        }

        if (imageBytes.Length == 0)
        {
            return BadRequest(new
            {
                message = "Image is empty."
            });
        }

        if (imageBytes.Length > 10 * 1024 * 1024)
        {
            return BadRequest(new
            {
                message = "Image must not exceed 10 MB."
            });
        }

        var allowedTypes = new[]
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        var contentType =
            string.IsNullOrWhiteSpace(request.ContentType)
                ? "image/jpeg"
                : request.ContentType.Trim().ToLowerInvariant();

        if (!allowedTypes.Contains(contentType))
        {
            return BadRequest(new
            {
                message = "Unsupported image content type."
            });
        }

        var layer3Input =
            new Layer4Layer3Input(
                request.Layer3.Verdict,
                request.Layer3.Confidence,
                request.Layer3.Reason,
                request.Layer3.Evidence
                    .Select(x =>
                        new Layer4Evidence(
                            x.Title,
                            x.Url,
                            x.Content
                        ))
                    .ToList(),
                request.Layer3.Sources
                    .Select(x =>
                        new Layer4Source(
                            x.Title,
                            x.Url
                        ))
                    .ToList()
            );

        await using var stream = new MemoryStream(imageBytes);

        var image = new FormFile(
            stream,
            0,
            imageBytes.Length,
            "image",
            "uploaded-image"
        )
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        var result =
            await _layer4.VerifyImageAsync(
                image,
                mode,
                request.Layer2,
                layer3Input
            );

        return Ok(result);
    }}




