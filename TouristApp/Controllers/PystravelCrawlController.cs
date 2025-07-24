using Microsoft.AspNetCore.Mvc;
using TouristApp.Models;
using TouristApp.Services;

namespace TouristApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PystravelCrawlController : ControllerBase
    {
        private readonly PystravelCrawlService _crawlService;

        public PystravelCrawlController(PystravelCrawlService crawlService)
        {
            _crawlService = crawlService;
        }

        [HttpGet("tours")]
        public async Task<ActionResult<List<PystravelTourModel>>> GetTours()
        {
            var tours = await _crawlService.GetToursAsync();
            return Ok(tours);
        }
    }
}
