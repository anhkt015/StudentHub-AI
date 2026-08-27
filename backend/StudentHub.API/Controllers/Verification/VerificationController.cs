using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentHub.API.DTOs.Verification;
using StudentHub.API.Services.Verification;

namespace StudentHub.API.Controllers.Verification;

[ApiController]
[Route("api/verify")]
public class VerificationController : ControllerBase
{
    private readonly ILayer2VerificationService _layer2;

    public VerificationController(ILayer2VerificationService layer2)
    {
        _layer2 = layer2;
    }

    [HttpPost("layer2")]
    [AllowAnonymous]
    public async Task<IActionResult> Layer2(
        [FromBody] Layer2VerifyRequest request)
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
                message = "Type is required. Use url, image or text."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new
            {
                message = "Content is required."
            });
        }

        var type = request.Type.Trim().ToLowerInvariant();

        if (type != "url" &&
            type != "image" &&
            type != "text")
        {
            return BadRequest(new
            {
                message = "Invalid type. Use url, image or text."
            });
        }

        var result = await _layer2.VerifyAsync(
            type,
            request.Content.Trim()
        );

        return Ok(result);
    }
}