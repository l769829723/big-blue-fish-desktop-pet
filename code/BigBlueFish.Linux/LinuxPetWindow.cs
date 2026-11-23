using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow : Window
{
    internal readonly CompanionPreferences Preferences = CompanionPreferences.Read();
    readonly CompanionAnimationPolicy policy = CompanionAnimationPolicy.Load(Path.Combine(AppContext.BaseDirectory, "assets/fish"));
    readonly CompanionBrain brain;
    readonly Random random = new();
    readonly Canvas petCanvas = new();
    readonly Image sprite = new() { Stretch = Stretch.Fill };
    readonly LinuxVideoPlayer player;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly TextBlock bubbleText = new() { TextWrapping = TextWrapping.Wrap };
    readonly LinuxBubble bubbleFrame;
    readonly LinuxLoadingRing loading = new() { IsVisible = false };
    readonly Stopwatch typingClock = new();
    readonly Queue<string> externalActions = new();
    readonly Mutex mutex;
    string currentAction = "", phase = "Startup", fullText = "";
    string? nextExternalText;
    DateTime nextSpeech = DateTime.UtcNow.AddSeconds(45), nextEvent = DateTime.MaxValue, nextWarmup;
    DateTime bubbleUntil = DateTime.MaxValue;
    IntPtr lastForeground;
    bool closed, exitReady, hidden, fullscreenHidden, dragging, dragged, generating, streamed, externalSpeech, typing, modelReply, greetingNoticeActive;
    long speechVersion;
    int[] textBoundaries = [];
    PixelPoint dragOrigin, windowOrigin, moveOrigin;
    PixelPoint lastClickPoint;
    long lastClickAt;
    double moveStart, moveEnd;
    double bubbleReservedHeight;
    bool updatingBubbleLayout, bubbleReady;
    byte[]? lastPresentedPixels;
    int lastPresentedWidth, lastPresentedHeight;
    bool lastPresentedMirror;
    CompanionAnimationRule? moveRule;
    bool moveMirrored;
    CompanionRoaming? roam;
    CompanionRoamingAction? roamAction;
    IReadOnlyList<CompanionSurface> roamSurfaces = Array.Empty<CompanionSurface>();
    DateTime nextRoamScan = DateTime.MinValue;
    bool suspendBubbleLayout;
    Window? settings;
    bool modelInstalling;
    bool agentWeatherPending;
    static readonly IBrush Hair = new SolidColorBrush(Color.FromRgb(80, 96, 152));
    internal bool Puppet => McpAvailable && Preferences.McpEnabled && Preferences.PuppetMode;
    double SpriteSize => 280 * Preferences.Scale;

    internal LinuxPetWindow()
    {
        mutex = new Mutex(true, "BigBlueFish.Linux." + Environment.UserName, out var first);
        if (!first) Environment.Exit(0);
        DiagnosticLog.ConfigureDirectories(AppContext.BaseDirectory, Environment.CurrentDirectory);
        DiagnosticLog.Write(DiagnosticEvent.AppStarted, kind: "Linux", directory: Environment.CurrentDirectory);
        DiagnosticLog.ObserveSettings(Preferences);
        brain = new() { UseGpu = Preferences.GpuEnabled, UseExternalApi = Preferences.ModelBackend == "api", ExternalApiUrl = Preferences.LocalLlamaApiUrl, ExternalApiModel = Preferences.LocalLlamaApiModel,
            StopOnBattery = Preferences.StopModelOnBattery, WeatherRegionCode = Preferences.WeatherRegionCode,
            NewsSource = () => Preferences.NewsSource, NewsLocation = () => Preferences.ActiveNewsLocation,
            NewsFileReadOrder = () => Preferences.NewsFileReadOrder, NewsFileLinesPerRead = () => Preferences.NewsFileLinesPerRead,
        };
        Preferences.ApplySpeakerRole(brain);
        Title = "蓝色大肥鱼"; WindowDecorations = WindowDecorations.None; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; CanResize = false; Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = petCanvas;
        player = new(sprite); player.FramePresented += MoveWithAnimation; player.FramePresented += AdvanceRoamingFrame;
        PositionChanged += (_, _) => { if (!suspendBubbleLayout) MoveBubble(); };
        player.PixelsPresented += (pixels,width,height,mirror) => {
            lastPresentedPixels = pixels; lastPresentedWidth = width; lastPresentedHeight = height; lastPresentedMirror = mirror; UpdateInputShape();
        };
        player.Failed += error => { DiagnosticLog.Write(DiagnosticEvent.AnimationFailed, state: "failed"); if (phase == "Exit") CloseImmediately(); };
        bubbleText.FontFamily = new FontFamily("avares://BigBlueFish.Linux/Assets#LemiXiaoNaiPaoTi");
        bubbleText.Foreground = Hair;
        var close = new Button { Content = "×", Padding = new Thickness(3), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => { typing = false; HideBubble(); };
        var bubbleGrid = new Grid(); bubbleGrid.Children.Add(bubbleText); bubbleGrid.Children.Add(close); bubbleText.Margin = new Thickness(0, 0, 24, 0);
        bubbleFrame = new LinuxBubble { Child = bubbleGrid, Padding = new Thickness(16,16,16,30) };
        bubbleFrame.IsVisible = false;
        petCanvas.Children.Add(sprite); petCanvas.Children.Add(loading); petCanvas.Children.Add(bubbleFrame);
        bubbleReady = true;
        Rescale();
        var screen = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        Position = new((int)(Preferences.Left ?? screen.Right - Width * DesktopScaling), (int)(Preferences.Top ?? screen.Bottom - Height * DesktopScaling)); Clamp();
        roam = new CompanionRoaming(random); roam.Sync(Body()); roam.ResetSchedule(DateTime.UtcNow);
        Opened += async (_, _) => {
            if (Puppet) Play(policy.Find("待机呼吸休闲"), "PuppetIdle");
            else { Play(policy.Find("女仆屈膝礼仪"), "Startup"); Welcome(); }
            InitializeMcp(); InitializeTray(); timer.Start();
            if (!File.Exists("model-selection.json") && !brain.ModelAvailable && !App.Args.Contains("--test-mode")) await InstallModel();
            if (Preferences.CheckUpdatesOnStartup && !App.Args.Contains("--test-mode")) _ = CheckUpdates(false);
        };
        PointerPressed += (_, e) => {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed) { PublishInteraction("click", new { button = "right", Position.X, Position.Y }); OpenMenu(); e.Handled = true; return; }
            if (!point.Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2 && TryOpenMcpInput()) { lastClickAt = 0; PublishInteraction("click", new { button = "left", count = 2, Position.X, Position.Y }); e.Handled = true; return; }
            dragging = true; dragged = false; moveRule = null; CancelRoam(); windowOrigin = Position;
            var screenPoint = this.PointToScreen(point.Position); dragOrigin = screenPoint;
            e.Pointer.Capture(this); e.Handled = true;
        };
        PointerMoved += (_, e) => {
            if (!dragging) return;
            var screenPoint = this.PointToScreen(e.GetPosition(this)); int dx = screenPoint.X - dragOrigin.X, dy = screenPoint.Y - dragOrigin.Y;
            if (!dragged && Math.Abs(dx) + Math.Abs(dy) > 4 * DesktopScaling) {
                dragged = true; PublishInteraction("drag_start", new { Position.X, Position.Y }); if (!Puppet) Play(policy.Find("被鼠标拖拽悬空反馈"), "Drag");
            }
            Position = new(windowOrigin.X + dx, windowOrigin.Y + dy); MoveBubble();
        };
        PointerReleased += (_, e) => {
            if (!dragging) return; dragging = false; e.Pointer.Capture(null); Clamp(); Save();
            if (dragged) { lastClickAt = 0; PublishInteraction("drag_end", new { Position.X, Position.Y, cancelled = false }); if (!Puppet && !PlayQueued()) Idle(); }
            else {
                PublishInteraction("click", new { button = "left", Position.X, Position.Y });
                var click = this.PointToScreen(e.GetPosition(this)); long at = Environment.TickCount64;
                bool twice = lastClickAt != 0 && at - lastClickAt <= 500 && Math.Abs(click.X-lastClickPoint.X)+Math.Abs(click.Y-lastClickPoint.Y) <= 8;
                lastClickAt = at; lastClickPoint = click;
                if (twice && TryOpenMcpInput()) lastClickAt = 0;
                else if (!Puppet) Play(policy.Choose("Click", Context(), random), "Click");
            }
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) => { if (dragging) { dragging = false; if (dragged) PublishInteraction("drag_end", new { Position.X, Position.Y, cancelled = true }); if (!Puppet) Idle(); } };
        Closing += (_, e) => {
            Save();
            CloseModelInstaller();
            if (exitReady || closed || Puppet) return;
            e.Cancel = true; if (phase == "Exit") return;
            speechVersion++; brain.Stop(); moveRule = null; settings?.Close(); hidden = false; fullscreenHidden = false; Show(); player.Paused = false;
            Play(policy.Find("女仆屈膝礼仪"), "Exit"); ShowText("主人再见~鱼鱼喜欢主人哦", false, true);
        };
        Closed += (_, _) => { DiagnosticLog.Write(DiagnosticEvent.AppStopped, kind: "Linux"); closed = true; timer.Stop(); StopMcp(); brain.Dispose(); player.Dispose(); settings?.Close(); Save(); mutex.Dispose(); (Avalonia.Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(); };
        timer.Tick += async (_, _) => await Tick();
    }
    CompanionAnimationContext Context() => new(DateTime.Now, BreakfastHour: Preferences.BreakfastHour, LunchHour: Preferences.LunchHour, DinnerHour: Preferences.DinnerHour);
    void Rescale()
    {
        Width = SpriteSize * 640 / 360; Height = SpriteSize + bubbleReservedHeight; sprite.Width = Width; sprite.Height = SpriteSize;
        petCanvas.Width = Width; petCanvas.Height = Height;
        Canvas.SetLeft(sprite, 0); Canvas.SetTop(sprite, bubbleReservedHeight);
        Canvas.SetLeft(loading, SpriteSize * 412 / 360); Canvas.SetTop(loading, bubbleReservedHeight + SpriteSize * 50 / 360);
        MoveBubble();
    }
    PixelPoint Clamp(PixelPoint? requested = null)
    {
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        double s = DesktopScaling, left = SpriteSize * 208 / 360 * s, top = (bubbleReservedHeight + SpriteSize * 58 / 360) * s,
            right = SpriteSize * 428 / 360 * s, bottom = (bubbleReservedHeight + SpriteSize * 336 / 360) * s;
        var target = requested ?? Position;
        double minimumTop = bubbleReservedHeight > 0 ? area.Y : area.Y - top;
        var result = new PixelPoint((int)Math.Clamp(target.X, area.X - left, Math.Max(area.X - left, area.Right - right)),
            (int)Math.Clamp(target.Y, minimumTop, Math.Max(minimumTop, area.Bottom - bottom)));
        Position = result; MoveBubble(); return result;
    }
    void Save() { if (closed) return; Preferences.Left = Position.X; Preferences.Top = Position.Y + (int)Math.Round(bubbleReservedHeight * DesktopScaling); Preferences.Save(); DiagnosticLog.ObserveSettings(Preferences); }
    void Play(CompanionAnimationRule rule, string nextPhase, bool mirror = false)
    {
        if (closed) return;
        if (phase == "Startup" && nextPhase != "Startup") EndWelcome();
        DiagnosticLog.Write(DiagnosticEvent.AnimationStarted, kind: nextPhase, action: rule.Id);
        currentAction = rule.Id; phase = nextPhase;
        player.Play(Path.Combine(AppContext.BaseDirectory, "assets/fish", rule.File), mirror, Finished,
            nextPhase == "Drag" ? 1.5 : 0, nextPhase == "Drag" ? 6 : null, rule.Pool == "Greeting" ? 1.5 : 1);
    }
    void Idle() => Play(Puppet ? policy.Find("待机呼吸休闲") : policy.Choose("Idle", Context(), random), Puppet ? "PuppetIdle" : "Idle");
    bool PlayQueued() { if (externalActions.Count == 0) return false; moveRule = null; Play(policy.Find(externalActions.Dequeue()), "External"); return true; }
    void Finished()
    {
        DiagnosticLog.Write(DiagnosticEvent.AnimationCompleted, kind: phase, action: currentAction);
        if (closed) return;
        if (phase == "Exit") { CloseImmediately(); return; }
        if (phase == "Roam") { FinishRoam(); return; }
        if (!dragging && !hidden && !fullscreenHidden && PlayQueued()) return;
        if (Puppet) { Idle(); return; }
        if (phase == "Drag") return;
        if (phase == "Outbound" && moveRule != null) { Position = new((int)moveEnd, Position.Y); Play(policy.Choose("Idle", Context(), random), "BetweenMoves"); return; }
        if (phase == "BetweenMoves" && moveRule != null) { moveStart = Position.X; moveEnd = moveOrigin.X; Play(moveRule, "Return", !moveMirrored); return; }
        if (phase == "Return") { Position = new(moveOrigin.X, Position.Y); moveRule = null; }
        if (phase == "Startup") { EndWelcome(); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); }
        if (dragging || hidden || fullscreenHidden || settings != null) { Idle(); return; }
        var now = DateTime.Now;
        if (now.Hour == 23 && Preferences.LastBrushDate != now.ToString("yyyy-MM-dd")) { Preferences.LastBrushDate = now.ToString("yyyy-MM-dd"); Save(); Play(policy.Find("晨间刷牙"), "Daily"); return; }
        if (DateTime.UtcNow >= nextEvent) { nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); Event(policy.Choose("Event", Context(), random)); } else Idle();
    }
    void Event(CompanionAnimationRule rule)
    {
        if (rule.MoveKind == null) { Play(rule, "Event"); return; }
        moveRule = rule; moveOrigin = Position; moveStart = Position.X;
        Position = new(Position.X - (int)(70 * Preferences.Scale * DesktopScaling), Position.Y); Clamp(); moveEnd = Position.X;
        if (Math.Abs(moveEnd - moveStart) < 15 * DesktopScaling) { Position = new((int)(moveStart + 70 * Preferences.Scale * DesktopScaling), Position.Y); Clamp(); moveEnd = Position.X; }
        Position = moveOrigin; moveMirrored = moveEnd > moveStart; Play(rule, "Outbound", moveMirrored);
    }
    CompanionRect Body()
    {
        double s = DesktopScaling;
        return new(Position.X + SpriteSize * 208 / 360 * s,
            Position.Y + (bubbleReservedHeight + SpriteSize * 58 / 360) * s,
            SpriteSize * 220 / 360 * s, SpriteSize * 278 / 360 * s);
    }
    CompanionRect WorkArea()
    {
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        return new CompanionRect(area.X, area.Y, area.Width, area.Height);
    }
    void PlaceBody(CompanionRect body)
    {
        double s = DesktopScaling;
        Position = new PixelPoint((int)Math.Round(body.X - SpriteSize * 208 / 360 * s),
            (int)Math.Round(body.Y - (bubbleReservedHeight + SpriteSize * 58 / 360) * s));
    }
    bool RoamingAllowed() => Preferences.RoamingEnabled && !CompanionNative.Wayland && !closed && phase != "Exit"
        && !hidden && !fullscreenHidden && !dragging && settings == null && !Puppet && !modelInstalling
        // A cancelled trip leaves its clip playing until it ends; that parked clip
        // must not block the next trip.
        && (roam?.IsRoaming == true || roamAction == null && phase is "Idle" or "BetweenMoves" or "Roam");
    void TickRoaming(DateTime now)
    {
        if (roam == null) return;
        if (!RoamingAllowed()) { CancelRoam(); return; }
        // Window enumeration costs a round trip per window, so idle time only pays
        // for it when the next trip is actually due.
        if (now >= nextRoamScan && (roam.State != CompanionRoamState.Idle || now >= roam.NextRoamAt))
        {
            nextRoamScan = now.AddMilliseconds(200);
            roamSurfaces = CompanionNative.Surfaces(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        }
        roam.Sync(Body());
        if (roamAction != null && !roam.IsRoaming) { roamAction = null; suspendBubbleLayout = false; }
        roam.Tick(now, WorkArea(), roamSurfaces, true, CompanionNative.IdleSeconds);
        if (roamAction != null) return;
        if (roam.TakeAction() is { } action) PlayRoamAction(action);
        else if (roam.IsRoaming) PlaceBody(roam.Body);
    }
    void CancelRoam()
    {
        if (roamAction == null && roam is not { IsRoaming: true }) return;
        roamAction = null; roam?.Cancel(); suspendBubbleLayout = false;
    }
    // Switching roaming off mid-perch must not leave her floating in the air with
    // no trip left to bring her down.
    void CancelRoamToGround()
    {
        CancelRoam();
        if (roam == null || !roam.MoveToGround()) return;
        PlaceBody(roam.Body); Clamp(); roam.Sync(Body()); Save();
    }
    void PlayRoamAction(CompanionRoamingAction action)
    {
        if (closed) return;
        var rule = policy.Find(action.Id);
        roamAction = action;
        if (phase == "Startup") EndWelcome();
        DiagnosticLog.Write(DiagnosticEvent.AnimationStarted, kind: "Roam", action: rule.Id);
        currentAction = rule.Id; phase = "Roam";
        // The layered window moves every frame; the bubble is anchored inside the
        // window, so only its own size changes need a layout pass.
        suspendBubbleLayout = true;
        player.Play(Path.Combine(AppContext.BaseDirectory, "assets/fish", rule.File), action.Mirror, Finished,
            0, action.EndSeconds, action.PlaybackRate);
    }
    void AdvanceRoamingFrame(double seconds)
    {
        if (roamAction == null || roam == null) return;
        roam.Advance(seconds);
        PlaceBody(roam.Body);
    }
    void FinishRoam()
    {
        var action = roamAction; roamAction = null;
        if (roam == null) { Idle(); return; }
        roam.ClipFinished(DateTime.UtcNow, action?.Token ?? 0);
        // Hand over to the next clip before snapping the window to the work area:
        // an action queued mid-clip (for example a settling fall) must keep its
        // planned start point instead of jumping to the floor first.
        if (roam.TakeAction() is { } next) { PlayRoamAction(next); return; }
        suspendBubbleLayout = false;
        Clamp();
        roam.Sync(Body());
        if (roam.IsRoaming) { PlaceBody(roam.Body); return; }
        Save();
        Idle();
    }
    void MoveWithAnimation(double seconds)
    {
        if (moveRule == null || phase is not ("Outbound" or "Return")) return;
        double lead = moveRule.MoveKind == "Run" ? 1.75 : 2, tail = moveRule.MoveKind == "Run" ? 4.8 : 2;
        double fraction = Math.Clamp((seconds - lead) / (10 - lead - tail), 0, 1);
        Position = new((int)(moveStart + (moveEnd - moveStart) * fraction), Position.Y); MoveBubble();
    }
    void Welcome()
    {
        string period = DateTime.Now.Hour switch { >= 5 and < 11 => "早上", >= 11 and < 14 => "中午", >= 14 and < 18 => "下午", _ => "晚上" };
        ShowText($"主人{period}好，鱼鱼给主人请安啦~", false, true);
        greetingNoticeActive = true;
    }
    void EndWelcome() { if (greetingNoticeActive) HideBubble(); greetingNoticeActive = false; }
    void ShowText(string text, bool external, bool immediate = false, bool fromModel = false)
    {
        greetingNoticeActive = false;
        DiagnosticLog.Write(DiagnosticEvent.BubbleShown, kind: external ? "agent" : fromModel ? "local" : "notice", chars: text.Length);
        fullText = text; textBoundaries = StringInfo.ParseCombiningCharacters(text); externalSpeech = external; modelReply = fromModel;
        if (external || fromModel) nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random));
        typing = !immediate && textBoundaries.Length > 1; typingClock.Restart(); bubbleUntil = DateTime.MaxValue;
        bubbleText.Text = typing ? text[..textBoundaries[1]] : text;
        MoveBubble(); if (!hidden && !fullscreenHidden) ShowBubble(); if (!typing && phase is not ("Startup" or "Exit")) ResetDwell();
    }
    void Partial(string text, long expectedVersion, bool agentTriggered = false)
    {
        Dispatcher.UIThread.Post(() => {
            if (expectedVersion != speechVersion || Puppet && !agentTriggered || closed || hidden || fullscreenHidden || !Preferences.ModelEnabled || !brain.CanRunOnCurrentPower) return;
            greetingNoticeActive = false; streamed = true; fullText = text; typing = false; externalSpeech = false; modelReply = true; bubbleText.Text = text; bubbleUntil = DateTime.MaxValue;
            MoveBubble(); ShowBubble();
        });
    }
    void ShowBubble()
    {
        if (hidden || fullscreenHidden || bubbleFrame.IsVisible) return;
        bubbleFrame.IsVisible = true;
        MoveBubble();
    }
    void HideBubble()
    {
        if (!bubbleFrame.IsVisible) return;
        bubbleFrame.IsVisible = false;
        MoveBubble();
    }
    void ResetDwell() { int duration = CompanionPreferences.BubbleDurations[Preferences.BubbleDurationIndex]; bubbleUntil = duration == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(duration); }
    void MoveBubble()
    {
        if (!bubbleReady || updatingBubbleLayout) return;
        updatingBubbleLayout = true;
        try {
            double scale = Preferences.BubbleScale;
            bubbleText.FontSize = 17 * scale; bubbleText.Margin = new(0, 0, 24 * scale, 0);
            bubbleFrame.Padding = new(16 * scale, 16 * scale, 16 * scale, 30 * scale);
            var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
            double maxWidth = Math.Max(1, Math.Min(300 * scale, Width - 16));
            double maxHeight = Math.Max(1, area.Height / DesktopScaling - SpriteSize - 16);
            bubbleFrame.MaxWidth = maxWidth; bubbleFrame.MaxHeight = maxHeight;
            string prefix = bubbleText.Text ?? ""; bubbleText.Text = fullText;
            bool hiddenForMeasure = !bubbleFrame.IsVisible;
            bubbleFrame.Width = double.NaN; bubbleFrame.Height = double.NaN;
            if (hiddenForMeasure) bubbleFrame.IsVisible = true;
            bubbleFrame.Measure(new Size(maxWidth, maxHeight)); var size = bubbleFrame.DesiredSize;
            if (hiddenForMeasure) bubbleFrame.IsVisible = false;
            bubbleText.Text = prefix;
            bubbleFrame.Width = Math.Min(size.Width, maxWidth); bubbleFrame.Height = Math.Min(size.Height, maxHeight);
            Canvas.SetLeft(bubbleFrame, Math.Max(0, (Width - bubbleFrame.Width) / 2)); Canvas.SetTop(bubbleFrame, 0);
            double newReservedHeight = bubbleFrame.IsVisible ? bubbleFrame.Height + 10 : 0;
            double oldReservedHeight = bubbleReservedHeight;
            if (Math.Abs(newReservedHeight - oldReservedHeight) > .5) {
                bubbleReservedHeight = newReservedHeight;
                Height = SpriteSize + bubbleReservedHeight;
                petCanvas.Width = Width; petCanvas.Height = Height;
                Canvas.SetTop(sprite, bubbleReservedHeight);
                Canvas.SetTop(loading, bubbleReservedHeight + SpriteSize * 50 / 360);
                // Move the window in one step: reading Position back after a set can
                // still return the previous value on X11, which would swallow the shift.
                Position = Clamp(new PixelPoint(Position.X,
                    Position.Y + (int)Math.Round((oldReservedHeight - bubbleReservedHeight) * DesktopScaling)));
            }
            UpdateInputShape();
        } finally { updatingBubbleLayout = false; }
    }
    void UpdateInputShape()
    {
        if (lastPresentedPixels == null || lastPresentedWidth <= 0) return;
        double scale = Width * DesktopScaling / lastPresentedWidth;
        CompanionNative.SetInputShape(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, lastPresentedPixels,
            lastPresentedWidth, lastPresentedHeight, scale, DesktopScaling, lastPresentedMirror,
            bubbleFrame.IsVisible ? Canvas.GetLeft(bubbleFrame) : 0,
            bubbleFrame.IsVisible ? Canvas.GetTop(bubbleFrame) : 0,
            bubbleFrame.IsVisible ? bubbleFrame.Width : 0,
            bubbleFrame.IsVisible ? bubbleFrame.Height : 0, bubbleReservedHeight);
    }
    async Task Tick()
    {
        if (closed || phase == "Exit") return;
        if (!Topmost) { Topmost = true; DiagnosticLog.Write(DiagnosticEvent.VisibilityChanged, kind: "topmost", state: "restored"); }
        await TestCommands(); if (closed || phase == "Exit") return;
        var now = DateTime.UtcNow;
        lastForeground = CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground);
        if (Preferences.PublicInfo && Preferences.WeatherEnabled) brain.RefreshWeather();
        if (!brain.CanRunOnCurrentPower) {
            if (brain.RunnerPid != null || brain.Busy) { speechVersion++; brain.Stop(); loading.IsVisible = false; }
            if (modelReply) HideBubble();
        }
        else if (!brain.UseExternalApi && !AgentDailyActive && !modelInstalling && Preferences.ModelEnabled && brain.ModelAvailable && !generating && !brain.Busy && brain.RunnerPid == null && now >= nextWarmup) { nextWarmup = now.AddMinutes(2); _ = brain.WarmUp(); }
        bool full = Preferences.HideOnFullscreen && CompanionNative.IsFullscreen();
        if (full && !fullscreenHidden && !hidden) { fullscreenHidden = true; HideBubble(); Hide(); speechVersion++; brain.CancelGeneration(); }
        else if (!full && fullscreenHidden) { fullscreenHidden = false; if (!hidden) { Show(); Clamp(); } }
        player.Paused = hidden || fullscreenHidden;
        TickAgent(now);
        TickRoaming(now);
        if (hidden || fullscreenHidden) return;
        if (typing)
        {
            int count = Math.Min(textBoundaries.Length, 1 + (int)(typingClock.Elapsed.TotalSeconds * 28));
            bubbleText.Text = count >= textBoundaries.Length ? fullText : fullText[..textBoundaries[count]];
            if (count == textBoundaries.Length)
            {
                typing = false; ResetDwell();
                // Wait until the current reply is fully visible before starting the next interval.
                if (modelReply || externalSpeech) nextSpeech = now.Add(Preferences.NextSpeechDelay(random));
            }
        }
        if (now >= bubbleUntil) HideBubble();
        if (Puppet || dragging || settings != null || modelInstalling || typing || now < nextSpeech) return;
        if (nextExternalText == null && !AgentDailyActive && (generating || brain.Busy || !Preferences.ModelEnabled || !brain.ModelAvailable || !brain.CanRunOnCurrentPower)) return;
        if (CompanionNative.IdleSeconds < 8 || CompanionNative.IdleSeconds > 600) { nextSpeech = now.AddSeconds(30); return; }
        if (nextExternalText != null) { string text = nextExternalText; nextExternalText = null; SayExternal(text); nextSpeech = now.Add(Preferences.NextSpeechDelay(random)); }
        else if (!DispatchAgentDaily()) await Speak(false);
    }
    async Task Speak(bool weather, bool agentTriggered = false)
    {
        DiagnosticLog.Write(DiagnosticEvent.SpeechRequested, kind: weather ? "weather" : "local", state: agentTriggered ? "manual" : "automatic");
        if (Puppet && !agentTriggered || generating || closed || phase == "Exit" || modelInstalling) { DiagnosticLog.Write(DiagnosticEvent.SpeechSkipped, state: "blocked"); return; }
        if (!brain.ModelAvailable) { await InstallModel(); return; }
        if (!Preferences.ModelEnabled) { ShowText("主人，请先在设置里启用本地大模型哦~", false); return; }
        if (!brain.CanRunOnCurrentPower) { ShowText("主人，当前设置为笔记本离电暂停模型，可以在设置里调节哦。", false); return; }
        generating = true; long version = ++speechVersion; streamed = false; loading.IsVisible = true;
        try
        {
            var until = DateTime.UtcNow.AddSeconds(100);
            while (brain.Busy && DateTime.UtcNow < until && version == speechVersion) await Task.Delay(100);
            if (brain.Busy || version != speechVersion || Puppet && !agentTriggered || hidden || fullscreenHidden) return;
            var text = weather ? await brain.GenerateWeather(p => Partial(p, version, agentTriggered)) : await brain.Generate(
                Preferences.PublicInfo && Preferences.NewsEnabled, Preferences.PublicInfo && Preferences.WeatherEnabled,
                Preferences.ForegroundEnabled, lastForeground, p => Partial(p, version, agentTriggered), chatty: Preferences.Chatty);
            if (version != speechVersion || Puppet && !agentTriggered || hidden || fullscreenHidden || closed || !Preferences.ModelEnabled || !brain.CanRunOnCurrentPower) return;
            if (text.Length > 0) { if (streamed) { fullText = text; bubbleText.Text = text; ResetDwell(); nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); } else ShowText(text, false, fromModel: true); }
            else if (streamed && !externalSpeech) HideBubble();
        }
        finally
        {
            generating = false; loading.IsVisible = false;
            // Start the interval after generation instead of at request time, so a slow model
            // cannot make the next request due while its reply is still arriving.
            if (version == speechVersion) nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random));
        }
    }
    void SayExternal(string text) { speechVersion++; brain.CancelGeneration(); loading.IsVisible = false; ShowText(text, true); }
    async Task SpeakManual(bool weather)
    {
        if (closed || phase == "Exit") return;
        if (weather && agentWeatherPending) return;
        DiagnosticLog.Write(DiagnosticEvent.SpeechRequested, kind: weather ? "weather" : "test");
        if (hidden || fullscreenHidden) { hidden = false; fullscreenHidden = false; Show(); }
#if !COMPANION_SAFE
        if (AgentDailyActive)
        {
            try
            {
                if (weather) { agentWeatherPending = true; loading.IsVisible = true; }
                else ShowText("主人，正在请 Agent 回应这次吐字测试～", false);
                string context = weather ? await brain.GetWeatherContext(CancellationToken.None) : "";
                bool accepted = agent?.ManualSpeech(weather, context) == true;
                DiagnosticLog.Write(DiagnosticEvent.AgentMaterials, kind: weather ? "weather" : "test", state: accepted ? "success" : "blocked");
                if (!accepted && !weather) ShowText("主人，Agent 请求尚未就绪或队列已满，请查看连接状态后重试～", false);
            }
            catch (Exception error) { DiagnosticLog.Write(DiagnosticEvent.AgentFailed, kind: weather ? "weather" : "test", error: error); if (!weather) ShowText("主人，" + error.Message, false); }
            finally { agentWeatherPending = false; if (agent?.AwaitingSpeech != true) loading.IsVisible = false; }
            return;
        }
#endif
        await Speak(weather, true);
    }
    void OpenMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string title, Action action, bool available = true) { var item = new MenuItem { Header = title, IsEnabled = available }; item.Click += (_, _) => action(); return item; }
        menu.ItemsSource = new[] { Item("设置", OpenSettings), Item("天气", async () => await SpeakManual(true)), Item("吐字测试", async () => await SpeakManual(false)),
            Item(hidden ? "显示桌宠" : "隐藏桌宠", () => { if (hidden) { hidden = false; Show(); Clamp(); } else { hidden = true; HideBubble(); Hide(); speechVersion++; brain.CancelGeneration(); } }), Item("关闭桌宠", Close) };
        ContextMenu = menu; menu.Open(this);
    }
    void CloseImmediately() { exitReady = true; Close(); }
}
