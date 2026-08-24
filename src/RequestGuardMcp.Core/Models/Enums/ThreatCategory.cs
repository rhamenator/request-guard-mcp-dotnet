namespace RequestGuardMcp.Core.Models.Enums;

/// <summary>Category of potential threat. Ports src/models/enums.rs's <c>ThreatCategory</c>.</summary>
public enum ThreatCategory
{
    AiScraping,
    BotTraffic,
    Crawling,
    Scraping,
    DataExtraction,
    BruteForce,
    Spam,
    Unknown,
    None,
}
