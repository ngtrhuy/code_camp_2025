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

        /// <summary>
        /// API chỉ crawl dữ liệu từ trang web (không insert DB)
        /// </summary>
        [HttpGet("tours")]
        public async Task<ActionResult<List<StandardTourModel>>> GetTours()
        {
            var tours = await _crawlService.GetToursAsync();
            return Ok(tours);
        }

        /// <summary>
        /// API vừa crawl vừa insert vào database
        /// </summary>
        [HttpPost("insert")]
        public async Task<IActionResult> CrawlAndInsertTours()
        {
            await _crawlService.GetToursAsync(insertToDb: true);
            return Ok(new { message = "✅ Crawl & Insert thành công vào database." });
        }
    }
}
