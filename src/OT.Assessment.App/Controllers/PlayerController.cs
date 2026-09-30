using Microsoft.AspNetCore.Mvc;
using OT.Assessment.App.Contracts;
using OT.Assessment.App.Services;

namespace OT.Assessment.App.Controllers
{
  
    [ApiController]
    [Route("api/player")]
    public sealed class PlayerController(ILogger<PlayerController> logger, IPlayerService playerService) : ControllerBase
    {
        //POST api/player/casinowager
        [HttpPost("casinowager")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> CasinoWager([FromBody] CasinoWagerRequest request,
            CancellationToken cancellationToken)
        {
            if (await playerService.PublishWagerAsync(request, cancellationToken))
                return Ok();

            return Problem(
                title: "The wager could not be queued for processing.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        //GET api/player/{playerId}/wagers

        //GET api/player/topSpenders?count=10        
    }
}