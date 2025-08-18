using System;

namespace TouristApp.Models
{
    public class HistoryModel
    {
        public int Id { get; set; }
        public int ConfigId { get; set; }
        public DateTime CrawlDate { get; set; }
        public string Status { get; set; } = "pending"; // pending | done | failed
        public string Log { get; set; } = "";
    }
}
    