using Microsoft.AspNetCore.Mvc;
using TouristApp.Services;
using TouristApp.Models;

namespace TouristApp.Controllers
{
    [Route("api/crawl")]
    [ApiController]
    public class GenericCrawlController : ControllerBase
    {
        [HttpGet("{configId}")]
        public async Task<IActionResult> Crawl(int configId)
        {
            try
            {
                // Load config để xác định loại crawl
                var tempService = new GenericCrawlServiceServerSide(); // Tạm thời để load config
                var config = await tempService.LoadPageConfig(configId);
                
                if (config == null)
                    return NotFound("Không tìm thấy cấu hình crawl.");

                // Tạo service phù hợp dựa trên crawl_type
                var crawlService = CrawlServiceFactory.CreateCrawlService(config);
                var data = await crawlService.CrawlFromPageConfigAsync(configId);
                
                if (data.Count == 0)
                    return NotFound("Không tìm thấy hoặc không crawl được dữ liệu.");

                return Ok(new
                {
                    message = $"Crawl thành công {data.Count} tour từ {config.BaseDomain}",
                    crawl_type = config.CrawlType,
                    data = data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi crawl: {ex.Message}" });
            }
        }

        [HttpGet("server-side/{configId}")]
        public async Task<IActionResult> CrawlServerSide(int configId)
        {
            try
            {
                var service = new GenericCrawlServiceServerSide();
                var data = await service.CrawlFromPageConfigAsync(configId);
                
                if (data.Count == 0)
                    return NotFound("Không tìm thấy hoặc không crawl được dữ liệu.");

                return Ok(new
                {
                    message = $"Crawl server-side thành công {data.Count} tour",
                    data = data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi crawl server-side: {ex.Message}" });
            }
        }

        [HttpGet("client-side/{configId}")]
        public async Task<IActionResult> CrawlClientSide(int configId)
        {
            try
            {
                var service = new GenericCrawlServiceClientSide();
                var data = await service.CrawlFromPageConfigAsync(configId);
                
                if (data.Count == 0)
                    return NotFound("Không tìm thấy hoặc không crawl được dữ liệu.");

                return Ok(new
                {
                    message = $"Crawl client-side thành công {data.Count} tour",
                    data = data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi crawl client-side: {ex.Message}" });
            }
        }
    }
}
