using System.Text.RegularExpressions;

namespace RequestGuardMcp.Tools;

/// <summary>
/// Compatibility subset of Woothee 0.13.0 used by the Rust server. The MCP contract exposes only
/// name, category, and operating system, so version/vendor parsing is intentionally omitted while
/// preserving Woothee's challenge order and classification decisions for those exposed fields.
/// Derived from woothee-rust 0.13.0 under Apache-2.0; see THIRD-PARTY-NOTICES.md.
/// </summary>
internal static partial class WootheeUaParser
{
    internal sealed record Result(string Name, string Category, string Os);

    private sealed class MutableResult
    {
        public string Name { get; set; } = Unknown;
        public string Category { get; set; } = Unknown;
        public string Os { get; set; } = Unknown;

        public Result Freeze() => new(Name, Category, Os);
    }

    private sealed record DatasetValue(string Name, string Category = "", string Os = "");

    private const string Unknown = "UNKNOWN";

    private static readonly Dictionary<string, DatasetValue> Dataset =
        new Dictionary<string, DatasetValue>(StringComparer.Ordinal)
        {
            ["MSIE"] = new("Internet Explorer"),
            ["Edge"] = new("Edge"),
            ["Chrome"] = new("Chrome"),
            ["Safari"] = new("Safari"),
            ["Firefox"] = new("Firefox"),
            ["Opera"] = new("Opera"),
            ["Vivaldi"] = new("Vivaldi"),
            ["Sleipnir"] = new("Sleipnir"),
            ["GSA"] = new("Google Search App"),
            ["Webview"] = new("Webview"),
            ["YaBrowser"] = new("Yandex Browser"),
            ["SamsungBrowser"] = new("SamsungBrowser"),
            ["Win"] = new("Windows UNKNOWN Ver", "pc"),
            ["Win10"] = new("Windows 10", "pc"),
            ["Win8.1"] = new("Windows 8.1", "pc"),
            ["Win8"] = new("Windows 8", "pc"),
            ["Win7"] = new("Windows 7", "pc"),
            ["WinVista"] = new("Windows Vista", "pc"),
            ["WinXP"] = new("Windows XP", "pc"),
            ["Win2000"] = new("Windows 2000", "pc"),
            ["WinNT4"] = new("Windows NT 4.0", "pc"),
            ["WinMe"] = new("Windows Me", "pc"),
            ["Win98"] = new("Windows 98", "pc"),
            ["Win95"] = new("Windows 95", "pc"),
            ["WinPhone"] = new("Windows Phone OS", "smartphone"),
            ["WinCE"] = new("Windows CE", "smartphone"),
            ["OSX"] = new("Mac OSX", "pc"),
            ["MacOS"] = new("Mac OS Classic", "pc"),
            ["Linux"] = new("Linux", "pc"),
            ["BSD"] = new("BSD", "pc"),
            ["ChromeOS"] = new("ChromeOS", "pc"),
            ["Android"] = new("Android", "smartphone"),
            ["iPhone"] = new("iPhone", "smartphone"),
            ["iPad"] = new("iPad", "smartphone"),
            ["iPod"] = new("iPod", "smartphone"),
            ["iOS"] = new("iOS", "smartphone"),
            ["FirefoxOS"] = new("Firefox OS", "smartphone"),
            ["BlackBerry"] = new("BlackBerry", "smartphone"),
            ["BlackBerry10"] = new("BlackBerry 10", "smartphone"),
            ["docomo"] = new("docomo", "mobilephone", "docomo"),
            ["au"] = new("au by KDDI", "mobilephone", "au"),
            ["SoftBank"] = new("SoftBank Mobile", "mobilephone", "SoftBank"),
            ["willcom"] = new("WILLCOM", "mobilephone", "WILLCOM"),
            ["jig"] = new("jig browser", "mobilephone", "jig"),
            ["emobile"] = new("emobile", "mobilephone", "emobile"),
            ["SymbianOS"] = new("SymbianOS", "mobilephone", "SymbianOS"),
            ["MobileTranscoder"] = new("Mobile Transcoder", "mobilephone", "Mobile Transcoder"),
            ["Nintendo3DS"] = new("Nintendo 3DS", "appliance", "Nintendo 3DS"),
            ["NintendoDSi"] = new("Nintendo DSi", "appliance", "Nintendo DSi"),
            ["NintendoWii"] = new("Nintendo Wii", "appliance", "Nintendo Wii"),
            ["NintendoWiiU"] = new("Nintendo Wii U", "appliance", "Nintendo Wii U"),
            ["PSP"] = new("PlayStation Portable", "appliance", "PlayStation Portable"),
            ["PSVita"] = new("PlayStation Vita", "appliance", "PlayStation Vita"),
            ["PS3"] = new("PlayStation 3", "appliance", "PlayStation 3"),
            ["PS4"] = new("PlayStation 4", "appliance", "PlayStation 4"),
            ["Xbox360"] = new("Xbox 360", "appliance", "Xbox 360"),
            ["XboxOne"] = new("Xbox One", "appliance", "Xbox One"),
            ["DigitalTV"] = new("InternetTVBrowser", "appliance", "DigitalTV"),
            ["SafariRSSReader"] = new("Safari RSSReader", "misc"),
            ["GoogleDesktop"] = new("Google Desktop", "misc"),
            ["WindowsRSSReader"] = new("Windows RSSReader", "misc"),
            ["VariousRSSReader"] = new("RSSReader", "misc"),
            ["HTTPLibrary"] = new("HTTP Library", "misc"),
            ["GoogleBot"] = new("Googlebot", "crawler"),
            ["GoogleBotMobile"] = new("Googlebot Mobile", "crawler"),
            ["GoogleMediaPartners"] = new("Google Mediapartners", "crawler"),
            ["GoogleFeedFetcher"] = new("Google Feedfetcher", "crawler"),
            ["GoogleAppEngine"] = new("Google AppEngine", "crawler"),
            ["GoogleWebPreview"] = new("Google Web Preview", "crawler"),
            ["YahooSlurp"] = new("Yahoo! Slurp", "crawler"),
            ["YahooJP"] = new("Yahoo! Japan", "crawler"),
            ["YahooPipes"] = new("Yahoo! Pipes", "crawler"),
            ["Baiduspider"] = new("Baiduspider", "crawler"),
            ["msnbot"] = new("msnbot", "crawler"),
            ["bingbot"] = new("bingbot", "crawler"),
            ["BingPreview"] = new("BingPreview", "crawler"),
            ["Yeti"] = new("Naver Yeti", "crawler"),
            ["FeedBurner"] = new("Google FeedBurner", "crawler"),
            ["facebook"] = new("facebook", "crawler"),
            ["twitter"] = new("twitter", "crawler"),
            ["trendictionbot"] = new("trendiction", "crawler"),
            ["mixi"] = new("mixi", "crawler"),
            ["IndyLibrary"] = new("Indy Library", "crawler"),
            ["ApplePubSub"] = new("Apple iCloud", "crawler"),
            ["Genieo"] = new("Genieo Web Filter", "crawler"),
            ["topsyButterfly"] = new("topsy Butterfly", "crawler"),
            ["rogerbot"] = new("SeoMoz rogerbot", "crawler"),
            ["AhrefsBot"] = new("ahref AhrefsBot", "crawler"),
            ["radian6"] = new("salesforce radian6", "crawler"),
            ["Hatena"] = new("Hatena", "crawler"),
            ["goo"] = new("goo", "crawler"),
            ["livedoorFeedFetcher"] = new("livedoor FeedFetcher", "crawler"),
            ["VariousCrawler"] = new("misc crawler", "crawler"),
            ["AdsBotGoogleMobile"] = new("AdsBot-Google-Mobile", "crawler"),
            ["AdsBotGoogle"] = new("AdsBot-Google", "crawler"),
        };

    public static Result? Parse(string agent)
    {
        var result = new MutableResult();
        if (agent.Length == 0 || agent == "-")
        {
            return result.Freeze();
        }

        if (ChallengeGoogle(agent, result) || ChallengeCrawlers(agent, result))
        {
            return result.Freeze();
        }

        if (ChallengeBrowser(agent, result))
        {
            _ = ChallengeOs(agent, result);
            return result.Freeze();
        }

        if (ChallengeMobilePhone(agent, result) || ChallengeAppliance(agent, result) || ChallengeMisc(agent, result) || ChallengeOs(agent, result) || ChallengeRare(agent, result))
        {
            return result.Freeze();
        }

        return null;
    }

    private static bool ChallengeBrowser(string agent, MutableResult result) =>
        ChallengeMsie(agent, result) || ChallengeEdge(agent, result) || ChallengeVivaldi(agent, result) ||
        ChallengeFirefoxIos(agent, result) || ChallengeYandex(agent, result) || ChallengeSamsung(agent, result) ||
        ChallengeSafariChrome(agent, result) || ChallengeFirefox(agent, result) || ChallengeOpera(agent, result) ||
        ChallengeWebview(agent, result);

    private static bool ChallengeOs(string agent, MutableResult result) =>
        ChallengeWindows(agent, result) || ChallengeOsx(agent, result) || ChallengeLinux(agent, result) ||
        ChallengeSmartphone(agent, result) || ChallengeMobilePhoneOs(agent, result) || ChallengeApplianceOs(agent, result) ||
        ChallengeMiscOs(agent, result);

    private static bool ChallengeRare(string agent, MutableResult result) =>
        ChallengeSmartphonePattern(agent, result) || ChallengeSleipnir(agent, result) || ChallengeHttpLibrary(agent, result) ||
        ChallengeMaybeRss(agent, result) || ChallengeMaybeCrawler(agent, result);

    private static bool Apply(MutableResult result, string label)
    {
        if (!Dataset.TryGetValue(label, out var value))
        {
            return false;
        }

        if (value.Name.Length > 0)
        {
            result.Name = value.Name;
        }

        if (value.Category.Length > 0)
        {
            result.Category = value.Category;
        }

        if (value.Os.Length > 0)
        {
            result.Os = value.Os;
        }

        return true;
    }

    private static void ApplyOs(MutableResult result, DatasetValue value)
    {
        if (value.Category.Length > 0)
        {
            result.Category = value.Category;
        }

        result.Os = value.Os.Length > 0 ? value.Os : value.Name;
    }

    private static bool ChallengeGoogle(string agent, MutableResult result)
    {
        if (!agent.Contains("Google", StringComparison.Ordinal))
        {
            return false;
        }

        if (agent.Contains("compatible; Googlebot", StringComparison.Ordinal))
        {
            return Apply(result, agent.Contains("compatible; Googlebot-Mobile", StringComparison.Ordinal) ? "GoogleBotMobile" : "GoogleBot");
        }

        if (agent.Contains("compatible; AdsBot-Google-Mobile;", StringComparison.Ordinal))
        {
            return Apply(result, "AdsBotGoogleMobile");
        }

        if (agent.StartsWith("AdsBot-Google", StringComparison.Ordinal))
        {
            return Apply(result, "AdsBotGoogle");
        }

        if (agent.Contains("Googlebot-Image/", StringComparison.Ordinal))
        {
            return Apply(result, "GoogleBot");
        }

        if (agent.Contains("Mediapartners-Google", StringComparison.Ordinal) && (agent.Contains("compatible; Mediapartners-Google", StringComparison.Ordinal) || agent == "Mediapartners-Google"))
        {
            return Apply(result, "GoogleMediaPartners");
        }

        if (agent.Contains("Feedfetcher-Google;", StringComparison.Ordinal))
        {
            return Apply(result, "GoogleFeedFetcher");
        }

        if (agent.Contains("AppEngine-Google", StringComparison.Ordinal))
        {
            return Apply(result, "GoogleAppEngine");
        }

        return agent.Contains("Google Web Preview", StringComparison.Ordinal) && Apply(result, "GoogleWebPreview");
    }

    private static bool ChallengeCrawlers(string agent, MutableResult result)
    {
        if (agent.Contains("Yahoo", StringComparison.Ordinal) || agent.Contains("help.yahoo.co.jp/help/jp/", StringComparison.Ordinal) || agent.Contains("listing.yahoo.co.jp/support/faq/", StringComparison.Ordinal))
        {
            if (agent.Contains("compatible; Yahoo! Slurp", StringComparison.Ordinal))
            {
                return Apply(result, "YahooSlurp");
            }

            if (ContainsAny(agent, "YahooFeedSeekerJp", "YahooFeedSeekerBetaJp", "crawler (http://listing.yahoo.co.jp/support/faq/", "crawler (http://help.yahoo.co.jp/help/jp/", "Y!J-BRZ/YATSHA crawler", "Y!J-BRY/YATSH crawler"))
            {
                return Apply(result, "YahooJP");
            }

            if (agent.Contains("Yahoo Pipes", StringComparison.Ordinal))
            {
                return Apply(result, "YahooPipes");
            }
        }

        if (agent.Contains("msnbot", StringComparison.Ordinal))
        {
            return Apply(result, "msnbot");
        }

        if (agent.Contains("bingbot", StringComparison.Ordinal) && agent.Contains("compatible; bingbot", StringComparison.Ordinal))
        {
            return Apply(result, "bingbot");
        }

        if (agent.Contains("BingPreview", StringComparison.Ordinal))
        {
            return Apply(result, "BingPreview");
        }

        if (agent.Contains("Baidu", StringComparison.Ordinal) && ContainsAny(agent, "compatible; Baiduspider", "Baiduspider+", "Baiduspider-image+"))
        {
            return Apply(result, "Baiduspider");
        }

        if (agent.Contains("Yeti", StringComparison.Ordinal) && ContainsAny(agent, "http://help.naver.com/robots", "http://help.naver.com/support/robots.html", "http://naver.me/bot"))
        {
            return Apply(result, "Yeti");
        }

        foreach (var (marker, label) in new (string, string)[]
                 {
                     ("FeedBurner/", "FeedBurner"), ("facebookexternalhit", "facebook"), ("Twitterbot/", "twitter"),
                     ("gooblogsearch/", "goo"), ("Apple-PubSub", "ApplePubSub"), ("(www.radian6.com/crawler)", "radian6"),
                     ("Genieo/", "Genieo"), ("labs.topsy.com/butterfly/", "topsyButterfly"),
                     ("rogerbot/1.0 (http://www.seomoz.org/dp/rogerbot", "rogerbot"), ("compatible; AhrefsBot/", "AhrefsBot"),
                     ("livedoor FeedFetcher", "livedoorFeedFetcher"), ("Fastladder FeedFetcher", "livedoorFeedFetcher"),
                     ("Hatena Antenna", "Hatena"), ("Hatena Pagetitle Agent", "Hatena"), ("Hatena Diary RSS", "Hatena"),
                     ("mixi-check", "mixi"), ("mixi-crawler", "mixi"), ("mixi-news-crawler", "mixi"),
                     ("trendictionbot", "trendictionbot"),
                 })
        {
            if (agent.Contains(marker, StringComparison.Ordinal))
            {
                return Apply(result, label);
            }
        }

        if (agent.Contains("ichiro", StringComparison.Ordinal) && ContainsAny(agent, "http://help.goo.ne.jp/door/crawler.html", "compatible; ichiro/mobile goo;"))
        {
            return Apply(result, "goo");
        }

        return agent.Contains("Indy Library", StringComparison.Ordinal) && agent.Contains("compatible; Indy Library", StringComparison.Ordinal) && Apply(result, "IndyLibrary");
    }

    private static bool ChallengeMsie(string agent, MutableResult result) =>
        ContainsAny(agent, "compatible; MSIE", "Trident/", "IEMobile") && Apply(result, "MSIE");

    private static bool ChallengeEdge(string agent, MutableResult result) => EdgeRegex().IsMatch(agent) && Apply(result, "Edge");
    private static bool ChallengeVivaldi(string agent, MutableResult result) => VivaldiRegex().IsMatch(agent) && Apply(result, "Vivaldi");
    private static bool ChallengeFirefoxIos(string agent, MutableResult result) => FirefoxIosRegex().IsMatch(agent) && Apply(result, "Firefox");
    private static bool ChallengeYandex(string agent, MutableResult result) => agent.Contains("YaBrowser/", StringComparison.Ordinal) && YandexRegex().IsMatch(agent) && Apply(result, "YaBrowser");
    private static bool ChallengeSamsung(string agent, MutableResult result) => agent.Contains("SamsungBrowser/", StringComparison.Ordinal) && Apply(result, "SamsungBrowser");

    private static bool ChallengeSafariChrome(string agent, MutableResult result)
    {
        if (agent.Contains("Chrome", StringComparison.Ordinal) && agent.Contains("wv", StringComparison.Ordinal))
        {
            return false;
        }

        if (!agent.Contains("Safari/", StringComparison.Ordinal))
        {
            return false;
        }

        if (ChromeRegex().IsMatch(agent))
        {
            return Apply(result, OperaBlinkRegex().IsMatch(agent) ? "Opera" : "Chrome");
        }

        if (agent.Contains("GSA", StringComparison.Ordinal) && GsaRegex().IsMatch(agent))
        {
            return Apply(result, "GSA");
        }

        return Apply(result, "Safari");
    }

    private static bool ChallengeFirefox(string agent, MutableResult result) => agent.Contains("Firefox/", StringComparison.Ordinal) && Apply(result, "Firefox");
    private static bool ChallengeOpera(string agent, MutableResult result) => agent.Contains("Opera", StringComparison.Ordinal) && Apply(result, "Opera");

    private static bool ChallengeWebview(string agent, MutableResult result)
    {
        if (agent.Contains("Chrome", StringComparison.Ordinal) && agent.Contains("wv", StringComparison.Ordinal))
        {
            return Apply(result, "Webview");
        }

        return WebviewRegex().IsMatch(agent) && !agent.Contains("Safari/", StringComparison.Ordinal) && Apply(result, "Webview");
    }

    private static bool ChallengeMobilePhone(string agent, MutableResult result)
    {
        if (ContainsAny(agent, "DoCoMo", ";FOMA;"))
        {
            return Apply(result, "docomo");
        }

        if (agent.Contains("KDDI-", StringComparison.Ordinal))
        {
            return Apply(result, "au");
        }

        if (ContainsAny(agent, "SoftBank", "Vodafone", "J-PHONE"))
        {
            return Apply(result, "SoftBank");
        }

        if (ContainsAny(agent, "WILLCOM", "DDIPOCKET"))
        {
            return Apply(result, "willcom");
        }

        if (agent.Contains("jig browser", StringComparison.Ordinal))
        {
            return Apply(result, "jig");
        }

        if (ContainsAny(agent, "emobile/", "OpenBrowser", "Browser/Obigo-Browser"))
        {
            return Apply(result, "emobile");
        }

        if (agent.Contains("SymbianOS", StringComparison.Ordinal))
        {
            return Apply(result, "SymbianOS");
        }

        if (agent.Contains("Hatena-Mobile-Gateway/", StringComparison.Ordinal) || agent.Contains("livedoor-Mobile-Gateway/", StringComparison.Ordinal))
        {
            return Apply(result, "MobileTranscoder");
        }

        return false;
    }

    private static bool ChallengeAppliance(string agent, MutableResult result)
    {
        if (agent.Contains("PSP (PlayStation Portable)", StringComparison.Ordinal))
        {
            return Apply(result, "PSP");
        }

        if (agent.Contains("PlayStation Vita", StringComparison.Ordinal))
        {
            return Apply(result, "PSVita");
        }

        if (ContainsAny(agent, "PLAYSTATION 3 ", "PLAYSTATION 3;"))
        {
            return Apply(result, "PS3");
        }

        if (agent.Contains("PlayStation 4 ", StringComparison.Ordinal))
        {
            return Apply(result, "PS4");
        }

        if (agent.Contains("Nintendo 3DS;", StringComparison.Ordinal))
        {
            return Apply(result, "Nintendo3DS");
        }

        if (agent.Contains("Nintendo DSi;", StringComparison.Ordinal))
        {
            return Apply(result, "NintendoDSi");
        }

        if (agent.Contains("Nintendo Wii;", StringComparison.Ordinal))
        {
            return Apply(result, "NintendoWii");
        }

        if (agent.Contains("(Nintendo WiiU)", StringComparison.Ordinal))
        {
            return Apply(result, "NintendoWiiU");
        }

        return agent.Contains("InettvBrowser/", StringComparison.Ordinal) && Apply(result, "DigitalTV");
    }

    private static bool ChallengeMisc(string agent, MutableResult result)
    {
        if (agent.Contains("AppleSyndication/", StringComparison.Ordinal))
        {
            return Apply(result, "SafariRSSReader");
        }

        if (agent.Contains("compatible; Google Desktop/", StringComparison.Ordinal))
        {
            return Apply(result, "GoogleDesktop");
        }

        return agent.Contains("Windows-RSS-Platform", StringComparison.Ordinal) && Apply(result, "WindowsRSSReader");
    }

    private static bool ChallengeWindows(string agent, MutableResult result)
    {
        if (!agent.Contains("Windows", StringComparison.Ordinal))
        {
            return false;
        }

        if (agent.Contains("Xbox", StringComparison.Ordinal))
        {
            return Apply(result, agent.Contains("Xbox; Xbox One)", StringComparison.Ordinal) ? "XboxOne" : "Xbox360");
        }

        var match = WindowsRegex().Match(agent);
        if (!match.Success)
        {
            ApplyOs(result, Dataset["Win"]);
            return true;
        }

        var label = match.Groups[1].Value switch
        {
            "NT 10.0" => "Win10",
            "NT 6.3" => "Win8.1",
            "NT 6.2" => "Win8",
            "NT 6.1" => "Win7",
            "NT 6.0" => "WinVista",
            "NT 5.1" => "WinXP",
            "NT 5.0" => "Win2000",
            "NT 4.0" => "WinNT4",
            "98" => "Win98",
            "95" => "Win95",
            "CE" => "WinCE",
            _ => null,
        };
        if (label is null && WindowsPhoneRegex().IsMatch(match.Groups[1].Value))
        {
            label = "WinPhone";
        }

        if (label is null)
        {
            return false;
        }

        ApplyOs(result, Dataset[label]);
        return true;
    }

    private static bool ChallengeOsx(string agent, MutableResult result)
    {
        if (!agent.Contains("Mac OS X", StringComparison.Ordinal))
        {
            return false;
        }

        var label = "OSX";
        if (agent.Contains("like Mac OS X", StringComparison.Ordinal))
        {
            if (agent.Contains("iPhone;", StringComparison.Ordinal))
            {
                label = "iPhone";
            }
            else if (agent.Contains("iPad;", StringComparison.Ordinal))
            {
                label = "iPad";
            }
            else if (agent.Contains("iPod", StringComparison.Ordinal))
            {
                label = "iPod";
            }
        }
        ApplyOs(result, Dataset[label]);
        return true;
    }

    private static bool ChallengeLinux(string agent, MutableResult result)
    {
        if (!agent.Contains("Linux", StringComparison.Ordinal))
        {
            return false;
        }

        ApplyOs(result, Dataset[agent.Contains("Android", StringComparison.Ordinal) ? "Android" : "Linux"]);
        return true;
    }

    private static bool ChallengeSmartphone(string agent, MutableResult result)
    {
        string? label = agent.Contains("iPhone", StringComparison.Ordinal) ? "iPhone" :
            agent.Contains("iPad", StringComparison.Ordinal) ? "iPad" :
            agent.Contains("iPod", StringComparison.Ordinal) ? "iPod" :
            agent.Contains("Android", StringComparison.Ordinal) ? "Android" :
            agent.Contains("CFNetwork", StringComparison.Ordinal) ? "iOS" :
            agent.Contains("BB10", StringComparison.Ordinal) ? "BlackBerry10" :
            agent.Contains("BlackBerry", StringComparison.Ordinal) ? "BlackBerry" : null;

        if (result.Name == Dataset["Firefox"].Name && FirefoxOsRegex().IsMatch(agent))
        {
            label = "FirefoxOS";
        }

        if (label is null)
        {
            return false;
        }

        ApplyOs(result, Dataset[label]);
        return true;
    }

    private static bool ChallengeMobilePhoneOs(string agent, MutableResult result)
    {
        foreach (var (marker, label) in new[] { ("KDDI-", "au"), ("WILLCOM", "willcom"), ("DDIPOCKET", "willcom"), ("SymbianOS", "SymbianOS") })
        {
            if (agent.Contains(marker, StringComparison.Ordinal))
            {
                ApplyOs(result, Dataset[label]);
                return true;
            }
        }
        if (agent.Contains("Google Wireless Transcoder", StringComparison.Ordinal) || agent.Contains("Naver Transcoder", StringComparison.Ordinal))
        {
            return Apply(result, "MobileTranscoder");
        }

        return false;
    }

    private static bool ChallengeApplianceOs(string agent, MutableResult result)
    {
        var label = agent.Contains("Nintendo DSi;", StringComparison.Ordinal) ? "NintendoDSi" : agent.Contains("Nintendo Wii;", StringComparison.Ordinal) ? "NintendoWii" : null;
        if (label is null)
        {
            return false;
        }

        ApplyOs(result, Dataset[label]);
        return true;
    }

    private static bool ChallengeMiscOs(string agent, MutableResult result)
    {
        var label = agent.Contains("(Win98;", StringComparison.Ordinal) ? "Win98" :
            ContainsAny(agent, "Macintosh; U; PPC;", "Mac_PowerPC") ? "MacOS" :
            agent.Contains("X11; FreeBSD ", StringComparison.Ordinal) ? "BSD" :
            agent.Contains("X11; CrOS ", StringComparison.Ordinal) ? "ChromeOS" : null;
        if (label is null)
        {
            return false;
        }

        ApplyOs(result, Dataset[label]);
        return true;
    }

    private static bool ChallengeSmartphonePattern(string agent, MutableResult result)
    {
        if (!agent.Contains("CFNetwork/", StringComparison.Ordinal))
        {
            return false;
        }

        ApplyOs(result, Dataset["iOS"]);
        return true;
    }

    private static bool ChallengeSleipnir(string agent, MutableResult result)
    {
        if (!agent.Contains("Sleipnir/", StringComparison.Ordinal))
        {
            return false;
        }

        _ = Apply(result, "Sleipnir");
        ApplyOs(result, Dataset["Win"]);
        return true;
    }

    private static bool ChallengeHttpLibrary(string agent, MutableResult result) =>
        (HttpClientRegex().IsMatch(agent) || HttpClientOtherRegex().IsMatch(agent) || agent.Contains("Java(TM) 2 Runtime Environment,", StringComparison.Ordinal) ||
         agent.StartsWith("Wget/", StringComparison.Ordinal) || ContainsPrefix(agent, "libwww-perl", "WWW-Mechanize", "LWP::Simple", "LWP ", "lwp-trivial", "Ruby", "feedzirra", "Typhoeus", "Python-urllib/", "Twisted ", "curl/") ||
         PhpRegex().IsMatch(agent) || PearRegex().IsMatch(agent)) && Apply(result, "HTTPLibrary");

    private static bool ChallengeMaybeRss(string agent, MutableResult result) =>
        (MaybeRssRegex().IsMatch(agent) || agent.Contains("headline-reader", StringComparison.OrdinalIgnoreCase) || agent.Contains("cococ/", StringComparison.Ordinal)) && Apply(result, "VariousRSSReader");

    private static bool ChallengeMaybeCrawler(string agent, MutableResult result) =>
        (MaybeCrawlerRegex().IsMatch(agent) || MaybeCrawlerOtherRegex().IsMatch(agent) || agent.Contains("ASP-Ranker Feed Crawler", StringComparison.Ordinal) ||
         MaybeFeedParserRegex().IsMatch(agent) || MaybeWatchdogRegex().IsMatch(agent)) && Apply(result, "VariousCrawler");

    private static bool ContainsAny(string value, params string[] candidates) => candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));
    private static bool ContainsPrefix(string value, params string[] prefixes) => prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));

    [GeneratedRegex("(?:Edge|Edg|EdgiOS|EdgA)/([.0-9]+)")] private static partial Regex EdgeRegex();
    [GeneratedRegex("Vivaldi/([.0-9]+)")] private static partial Regex VivaldiRegex();
    [GeneratedRegex("FxiOS/([.0-9]+)")] private static partial Regex FirefoxIosRegex();
    [GeneratedRegex("YaBrowser/(\\d+\\.\\d+\\.\\d+\\.\\d+)")] private static partial Regex YandexRegex();
    [GeneratedRegex("(?:Chrome|CrMo|CriOS)/([.0-9]+)")] private static partial Regex ChromeRegex();
    [GeneratedRegex("OPR/([.0-9]+)")] private static partial Regex OperaBlinkRegex();
    [GeneratedRegex("GSA/([.0-9]+)")] private static partial Regex GsaRegex();
    [GeneratedRegex("iP(hone;|ad;|od) .*like Mac OS X")] private static partial Regex WebviewRegex();
    [GeneratedRegex("Windows ([ .a-zA-Z0-9]+)[;\\)]")] private static partial Regex WindowsRegex();
    [GeneratedRegex("^Phone(?: OS)? ([.0-9]+)")] private static partial Regex WindowsPhoneRegex();
    [GeneratedRegex("^Mozilla/[.0-9]+ \\((?:Mobile|Tablet);(?:.*;)? rv:([.0-9]+)\\) Gecko/[.0-9]+ Firefox/[.0-9]+$")] private static partial Regex FirefoxOsRegex();
    [GeneratedRegex("^(?:Apache-HttpClient/|Jakarta Commons-HttpClient/|Java/)")] private static partial Regex HttpClientRegex();
    [GeneratedRegex("[- ]HttpClient(/|$)")] private static partial Regex HttpClientOtherRegex();
    [GeneratedRegex("^(?:PHP|WordPress|CakePHP|PukiWiki|PECL::HTTP)(?:/| |$)")] private static partial Regex PhpRegex();
    [GeneratedRegex("(?:PEAR HTTP_Request|HTTP_Request)(?: class|2)")] private static partial Regex PearRegex();
    [GeneratedRegex("rss(?:reader|bar|[-_ /;()]|[ +]*/)", RegexOptions.IgnoreCase)] private static partial Regex MaybeRssRegex();
    [GeneratedRegex("(?:bot|crawler|spider)(?:[-_ ./;@()]|$)", RegexOptions.IgnoreCase)] private static partial Regex MaybeCrawlerRegex();
    [GeneratedRegex("(?:Rome Client |UnwindFetchor/|ia_archiver |Summify |PostRank/)")] private static partial Regex MaybeCrawlerOtherRegex();
    [GeneratedRegex("(?:feed|web) ?parser", RegexOptions.IgnoreCase)] private static partial Regex MaybeFeedParserRegex();
    [GeneratedRegex("watch ?dog", RegexOptions.IgnoreCase)] private static partial Regex MaybeWatchdogRegex();
}
