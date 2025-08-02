using Microsoft.AspNetCore.Mvc;
using TouristApp.Models;
using TouristApp.Services;

namespace TouristApp.Controllers
{
    [ApiController]
    [Route("api/luaviet/tours")]
    public class LuaVietTourController : ControllerBase
    {
        private readonly LuaVietTourCrawler _crawler;

        public LuaVietTourController()
        {
            _crawler = new LuaVietTourCrawler();
        }

        [HttpGet]
        public async Task<IActionResult> GetLuaVietTours()
        {
            var tours = await _crawler.CrawlAllCategoriesAsync();
            return Ok(tours);
        }
        [HttpPost("import")]
        public async Task<ActionResult<List<StandardTourModel>>> ImportToursToDatabase()
        {
            try
            {
                var tours = await _crawler.CrawlAndInsertToDatabaseAsync();
                return Ok(new
                {
                    message = $"Đã import {tours.Count} tour thành công!",
                    data = tours
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi khi import dữ liệu: {ex.Message}" });
            }
        }
    }
}
