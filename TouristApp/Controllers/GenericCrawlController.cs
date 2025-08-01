using Microsoft.AspNetCore.Mvc;
using TouristApp.Services;

namespace TouristApp.Controllers
{
    [Route("api/crawl")]
    [ApiController]
    public class GenericCrawlController : ControllerBase
    {
        private readonly GenericCrawlService _service;

        public GenericCrawlController()
        {
            _service = new GenericCrawlService();
        }

        [HttpGet("{configId}")]
        public async Task<IActionResult> Crawl(int configId)
        {
            var data = await _service.CrawlFromPageConfigAsync(configId);
            if (data.Count == 0)
                return NotFound("Không tìm thấy hoặc không crawl được dữ liệu.");

            return Ok(data);
        }
    }
}
