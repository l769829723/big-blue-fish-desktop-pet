using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigBlueFish.Windows;
internal sealed class CompanionPreferences
{
    public bool ModelEnabled { get; set; } = true;
    public string ModelBackend { get; set; } = "builtin";
    public string LocalLlamaApiUrl { get; set; } = "http://127.0.0.1:8080/v1";
    public bool GpuEnabled { get; set; } = true;
    public bool PublicInfo { get; set; } = true;
    public bool WeatherEnabled { get; set; } = true;
    public bool NewsEnabled { get; set; } = true;
    public bool Chatty { get; set; }
    public string SpeakerRole { get; set; } = "assistant";
    internal void SetPublicInfo(bool enabled)
    {
        PublicInfo = enabled;
        WeatherEnabled = NewsEnabled = enabled;
    }

    internal void NormalizePublicInfo()
    {
        if (!PublicInfo) WeatherEnabled = NewsEnabled = false;
    }
    public string NewsSource { get; set; } = "builtin";
    public string NewsFilePath { get; set; } = "";
    public string NewsApiUrl { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public string NewsLocation { get; set; } = "";
    [JsonIgnore] public string ActiveNewsLocation => NewsSource switch { "file" => NewsFilePath, "api" => NewsApiUrl, _ => "https://www.ithome.com/rss/" };
    public bool ForegroundEnabled { get; set; } = true;
    public bool StopModelOnBattery { get; set; } = true;
    public bool HideOnFullscreen { get; set; } = true;
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public bool AgentEnabled { get; set; }
    public string AgentHarnessId { get; set; } = "codex";
    public bool AgentDailySpeech { get; set; }
    public bool AgentRespondToClick { get; set; }
    public bool AgentRespondToDrag { get; set; }
    // Old UI/animation helpers use this internal alias. Legacy MCP settings do not turn on a new CLI connection.
    [JsonIgnore] public bool McpEnabled { get => AgentEnabled; set => AgentEnabled = value; }
    public bool PuppetMode { get; set; }
    [JsonIgnore] public bool McpChatEnabled { get => AgentEnabled; set { } }
    [JsonIgnore] public bool McpWakeEnabled { get => false; set { } }
    [JsonIgnore] public bool McpWakeOnText { get => true; set { } }
    [JsonIgnore] public bool McpWakeOnClick { get => AgentRespondToClick; set => AgentRespondToClick = value; }
    [JsonIgnore] public bool McpWakeOnDrag { get => AgentRespondToDrag; set => AgentRespondToDrag = value; }
    public string WeatherRegionCode { get; set; } = CompanionRegions.DefaultCode;
    public double Scale { get; set; } = 1;
    public double BubbleScale { get; set; } = 1;
    public int SpeechMinMinutes { get; set; } = 10;
    public string SpeechInterval { get; set; } = "10m";
    internal static readonly (string Id, string Label)[] SpeechIntervals =
    [ ("10s", "10–30 秒"), ("30s", "30–60 秒"), ("2m", "2–3 分钟"), ("3m", "3–5 分钟"), ("5m", "5–8 分钟"), ("10m", "10–16 分钟"), ("15m", "15–23 分钟"), ("20m", "20–30 分钟") ];
    internal string EffectiveSpeechInterval => SpeechInterval == "10m" && NormalizeSpeechMinimum(SpeechMinMinutes) != 10
        ? NormalizeSpeechMinimum(SpeechMinMinutes) switch { 2 => "2m", 3 => "3m", 5 => "5m", 15 => "15m", 20 => "20m", _ => "10m" }
        : SpeechInterval;
    internal int SpeechIntervalIndex => Math.Max(0, Array.FindIndex(SpeechIntervals, x => x.Id == EffectiveSpeechInterval));
    internal void SetSpeechInterval(string id)
    {
        SpeechInterval = SpeechIntervals.Any(x => x.Id == id) ? id : "10m";
        SpeechMinMinutes = SpeechInterval switch { "2m" => 2, "3m" => 3, "5m" => 5, "15m" => 15, "20m" => 20, _ => 10 };
    }
    internal static readonly (int Min, int Max)[] SpeechRanges = [(2, 3), (3, 5), (5, 8), (10, 16), (15, 23), (20, 30)];
    internal int SpeechRangeIndex => Array.FindIndex(SpeechRanges, r => r.Min == NormalizeSpeechMinimum(SpeechMinMinutes));
    internal static int NormalizeSpeechMinimum(int value) => value switch
    {
        2 or 3 or 5 or 10 or 15 or 20 => value,
        <= 2 => 2,
        <= 4 => 3,
        <= 6 => 5,
        <= 12 => 10,
        <= 17 => 15,
        _ => 20
    };
    internal TimeSpan NextSpeechDelay(Random random)
    {
        string interval = EffectiveSpeechInterval;
        if (interval == "10s") return TimeSpan.FromSeconds(10 + random.NextDouble() * 20);
        if (interval == "30s") return TimeSpan.FromSeconds(30 + random.NextDouble() * 30);
        var range = SpeechRanges[Array.FindIndex(SpeechRanges, r => r.Min == (interval switch { "2m" => 2, "3m" => 3, "5m" => 5, "15m" => 15, "20m" => 20, _ => 10 }))];
        return TimeSpan.FromMinutes(range.Min + random.NextDouble() * (range.Max - range.Min));
    }
    public int BubbleDurationIndex { get; set; } = 3;
    public int AnimationIntervalSeconds { get; set; } = 40;
    public int BreakfastHour { get; set; } = 8;
    public int LunchHour { get; set; } = 12;
    public int DinnerHour { get; set; } = 18;
    public string? LastBrushDate { get; set; }

    internal static readonly int[] BubbleDurations =
    {
        5,
        10,
        15,
        30,
        60,
        120,
        180,
        0
    };
    internal static readonly string[] BubbleDurationLabels =
    {
        "5秒",
        "10秒",
        "15秒",
        "30秒",
        "1分钟",
        "2分钟",
        "3分钟",
        "直至下一句"
    };
    public double? Left { get; set; }
    public double? Top { get; set; }

    public static CompanionPreferences Read()
    {
        foreach (var p in new[]
        {
            "preferences.json",
            "preferences.json.bak"
        }

        )
            try
            {
                var s = JsonSerializer.Deserialize<CompanionPreferences>(File.ReadAllText(p));
                if (s != null)
                {
                    using (var json = JsonDocument.Parse(File.ReadAllText(p)))
                    {
                        if (!json.RootElement.TryGetProperty("SpeechInterval", out _))
                            s.SpeechInterval = NormalizeSpeechMinimum(s.SpeechMinMinutes) switch { 2 => "2m", 3 => "3m", 5 => "5m", 10 => "10m", 15 => "15m", _ => "20m" };
                    }
                    if (s.SpeechInterval is not ("10s" or "30s" or "2m" or "3m" or "5m" or "10m" or "15m" or "20m")) s.SpeechInterval = "10m";
                    s.SetSpeechInterval(s.SpeechInterval);
                    s.NormalizePublicInfo();
                    s.ModelBackend = s.ModelBackend == "api" ? "api" : "builtin";
                    if (s.ModelBackend == "api") s.GpuEnabled = s.StopModelOnBattery = false;
                    s.NewsSource = s.NewsSource is "file" or "api" ? s.NewsSource : "builtin";
                    s.SpeakerRole = s.SpeakerRole == "owner" ? "owner" : "assistant";
                    if (string.IsNullOrWhiteSpace(s.NewsFilePath) && s.NewsSource == "file") s.NewsFilePath = s.NewsLocation;
                    if (string.IsNullOrWhiteSpace(s.NewsApiUrl) && s.NewsSource == "api") s.NewsApiUrl = s.NewsLocation;
#if COMPANION_SAFE
                    s.McpEnabled = false;
                    s.PuppetMode = false;
                    s.McpChatEnabled = false;
                    s.McpWakeEnabled = false;
                    s.AgentDailySpeech = false;
#endif
                    if (!s.McpEnabled) s.PuppetMode = false;
                    s.Scale = double.IsFinite(s.Scale) ? Math.Clamp(s.Scale, .5, 2.0) : 1;
                    if (s.Left.HasValue && !double.IsFinite(s.Left.Value)) s.Left = null;
                    if (s.Top.HasValue && !double.IsFinite(s.Top.Value)) s.Top = null;
                    s.BubbleScale = double.IsFinite(s.BubbleScale) ? Math.Clamp(s.BubbleScale, .5, 2.0) : 1;
                    s.AnimationIntervalSeconds = Math.Clamp(s.AnimationIntervalSeconds, 30, 300);
                    s.BreakfastHour = Math.Clamp(s.BreakfastHour, 0, 23);
                    s.LunchHour = Math.Clamp(s.LunchHour, 0, 23);
                    s.DinnerHour = Math.Clamp(s.DinnerHour, 0, 23);
                    s.BubbleDurationIndex = Math.Clamp(s.BubbleDurationIndex, 0, BubbleDurations.Length - 1);
                    s.WeatherRegionCode = CompanionRegions.District(s.WeatherRegionCode).Code;
                    return s;
                }
            }
            catch
            {
            }

        return new();
    }

    public void Save()
    {
        NormalizePublicInfo();
        var t = "preferences.json.tmp";
        File.WriteAllText(t, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists("preferences.json"))
            File.Replace(t, "preferences.json", "preferences.json.bak");
        else
            File.Move(t, "preferences.json");
    }
}
