using Microsoft.AspNetCore.Mvc;
using TouristApp.Services;

namespace TouristApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TourController : ControllerBase
    {
        private readonly TourScraperService _scraper;

        public TourController(TourScraperService scraper)
        {
            _scraper = scraper;
        }

 /*       [HttpGet("html")]
        public async Task<IActionResult> GetToursHtml([FromQuery] string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                url = "https://www.bestprice.vn/tour";

            var tours = await _scraper.GetToursAsync(url);
            return Ok(tours);
        }*/

        [HttpGet("selenium")]
        public async Task<IActionResult> GetToursSelenium([FromQuery] string url, [FromServices] TourSeleniumService seleniumService)
        {
            if (string.IsNullOrWhiteSpace(url))
                url = "https://www.bestprice.vn/tour";

            var tours = await _scraper.GetToursUsingSeleniumAsync(seleniumService, url);
            return Ok(tours);
        }
    }
}
