using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentHub.API.DTOs.Verification;
using StudentHub.API.Services.Verification;

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

        if (mode != "pro" && mode != "expert")
        {
            return BadRequest(new
            {
                message = "Mode must be pro or expert."
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
}
