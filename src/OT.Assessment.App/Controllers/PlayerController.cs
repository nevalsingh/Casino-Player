using Microsoft.AspNetCore.Mvc;
using OT.Assessment.App.Contracts;
using OT.Assessment.App.Services;

namespace OT.Assessment.App.Controllers;

[ApiController]
[Route("api/player")]
public sealed class PlayerController(IPlayerService playerService) : ControllerBase
{
    /// <summary>Queues a casino wager for processing.</summary>
    /// <remarks>Returns 200 once the message broker has durably accepted the wager.</remarks>
    [HttpPost("casinowager")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CasinoWager([FromBody] CasinoWagerRequest request, CancellationToken cancellationToken)
    {
        if (await playerService.PublishWagerAsync(request, cancellationToken))
            return Ok();

        return Problem(
            title: "The wager could not be queued for processing.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>Returns a page of a player's casino wager history, newest first.</summary>
    [HttpGet("{playerId:guid}/casino")]
    [ProducesResponseType(typeof(CasinoWagerHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCasinoHistory(
        Guid playerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
            return Problem(title: "Invalid page.", detail: "page must be at least 1.", statusCode: StatusCodes.Status400BadRequest);
        if (pageSize is < 1 or > 100)
            return Problem(title: "Invalid pageSize.", detail: "pageSize must be between 1 and 100.", statusCode: StatusCodes.Status400BadRequest);

        return Ok(await playerService.GetCasinoHistoryAsync(playerId, page, pageSize, cancellationToken));
    }

    /// <summary>Returns the top spending players, highest total spend first.</summary>
    [HttpGet("topSpenders")]
    [ProducesResponseType(typeof(IReadOnlyList<TopSpenderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTopSpenders([FromQuery] int count = 10, CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > 100)
            return Problem(title: "Invalid count.", detail: "count must be between 1 and 100.", statusCode: StatusCodes.Status400BadRequest);

        return Ok(await playerService.GetTopSpendersAsync(count, cancellationToken));
    }
}
