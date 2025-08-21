using System.Threading.Tasks;
using System.Collections.Generic;

namespace TouristApp.Services
{
    public class PageRenderResult
    {
        public string FinalUrl { get; set; } = "";
        public string BaseDomain { get; set; } = "";
        public string Html { get; set; } = "";
        public string RenderModeUsed { get; set; } = "server_side"; // server_side | client_side
        public List<string> Logs { get; set; } = new();
    }

    public interface IPageRenderService
    {
        /// <param name="mode">server_side | client_side | auto</param>
        Task<PageRenderResult> RenderAsync(string url, string mode = "server_side",
            string? loadMoreSelector = null, int loadMoreClicks = 0);
    }
}
