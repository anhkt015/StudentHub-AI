using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StudentHub.API.DTOs.Verification;
using StudentHub.API.Services.Verification;
using System.Text.Json;

namespace StudentHub.API.Controllers.Verification;

[ApiController]
[Route("api/verify")]
public class Layer3VerificationController : ControllerBase
{
    private readonly ILayer3VerificationService _layer3;

    public Layer3VerificationController(
        ILayer3VerificationService layer3)
    {
        _layer3 = layer3;
    }

    [HttpPost("layer3")]
    [AllowAnonymous]
    public async Task<IActionResult> Layer3(
        [FromBody] Layer3VerifyRequest request)
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
                message = "Type is required. Use url or text."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new
            {
                message = "Content is required."
            });
        }

        var type =
            request.Type.Trim().ToLowerInvariant();

        if (type != "url" && type != "text")
        {
            return BadRequest(new
            {
                message = "Layer 3 currently supports url or text."
            });
        }

        var result =
            await _layer3.VerifyAsync(
                type,
                request.Content.Trim()
            );

        return Ok(result);
    }

    // ============================================================
    // LAYER 3 IMAGE
    // POST /api/verify/layer3/image
    //
    // multipart/form-data
    // image      = image file
    // layer2Json = Layer 2 Image result
    // ============================================================

        // ============================================================
    // LAYER 3 IMAGE
    // POST /api/verify/layer3/image
    //
    // JSON BODY:
    // {
    //   "imageBase64": "...",
    //   "contentType": "image/jpeg",
    //   "layer2": { ... }
    // }
    // ============================================================

    [HttpPost("layer3/image")]
    [AllowAnonymous]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Layer3Image(
        [FromBody] Layer3ImageVerifyRequest request)
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
            await _layer3.VerifyImageAsync(
                image,
                request.Layer2
            );

        return Ok(result);
    }}


