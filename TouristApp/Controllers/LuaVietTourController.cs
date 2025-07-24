using TouristApp.Services.LuaViet;
using Microsoft.AspNetCore.Mvc;

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
    }
}
