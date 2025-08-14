using Microsoft.AspNetCore.Mvc;
using TouristApp.Models;
using TouristApp.Services;

namespace TouristApp.Controllers
{
    [ApiController]
    [Route("api/pystravel")]
    public class PystravelCrawlController : ControllerBase
    {
        private readonly PystravelCrawlService _service = new PystravelCrawlService();

        /// <summary>
        /// Xem danh sách tour (mặc định kèm lịch trình chi tiết).
        /// /api/pystravel/tours?includeDetails=true|false
        /// </summary>
        [HttpGet("tours")]
        public async Task<ActionResult<List<StandardTourModel>>> GetTours([FromQuery] bool includeDetails = true)
        {
            var tours = await _service.CrawlListAsync(includeDetails);
            return Ok(tours);
        }

        /// <summary>
        /// Crawl & import DB (tours + schedules).
        /// /api/pystravel/import?includeDetails=true|false
        /// </summary>
        [HttpPost("import")]
        public async Task<ActionResult> Import([FromQuery] bool includeDetails = true)
        {
            try
            {
                var inserted = await _service.ImportAsync(includeDetails);
                return Ok(new { message = $"Đã import {inserted} tour từ Pystravel (kèm schedules={includeDetails})." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi khi import dữ liệu: {ex.Message}" });
            }
        }
    }
}
