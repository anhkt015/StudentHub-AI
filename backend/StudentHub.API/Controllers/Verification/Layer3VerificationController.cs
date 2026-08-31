using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentHub.API.DTOs.Verification;
using StudentHub.API.Services.Verification;

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
}