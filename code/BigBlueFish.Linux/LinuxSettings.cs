using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
#if !COMPANION_SAFE
using BigBlueFish.Control;
#endif

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    bool consentOpen;
    Window? consentWindow;
    Action? refreshLinuxSettings;
    Action? disposeLogConsole;
    static string AutostartFile => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart/bigbluefish.desktop");
    bool StartupEnabled => File.Exists(AutostartFile);
    void SetStartup(bool on)
    {
        if (!on) { File.Delete(AutostartFile); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
        File.WriteAllText(AutostartFile, "[Desktop Entry]\nType=Application\nName=蓝色大肥鱼\nExec=\"" + Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Linux").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\nTerminal=false\n");
    }
    void ApplySettings(PixelPoint? requestedPosition = null)
    {
        Preferences.NormalizePublicInfo();
        if (Preferences.ModelBackend == "api") Preferences.GpuEnabled = Preferences.StopModelOnBattery = false;
        speechVersion++; brain.Stop(); brain.UseGpu = Preferences.GpuEnabled; brain.UseExternalApi = Preferences.ModelBackend == "api"; brain.ExternalApiUrl = Preferences.LocalLlamaApiUrl;
        brain.RefreshModelAvailability();
        brain.StopOnBattery = Preferences.StopModelOnBattery; brain.WeatherRegionCode = Preferences.WeatherRegionCode;
        Preferences.ApplySpeakerRole(brain);
        ConfigureAgent();
        Rescale(); var appliedPosition = Clamp(requestedPosition); nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); nextWarmup = DateTime.MinValue;
        if (!Preferences.HideOnFullscreen && fullscreenHidden) { fullscreenHidden = false; if (!hidden) Show(); }
        if (bubbleFrame.IsVisible) ResetDwell();
        Preferences.Left = appliedPosition.X; Preferences.Top = appliedPosition.Y + (int)Math.Round(bubbleReservedHeight * DesktopScaling); Preferences.Save(); DiagnosticLog.ObserveSettings(Preferences);
    }
    static Button ActionButton(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(18, 9), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 4, 10, 4), Background = Brushes.White, Foreground = Hair, BorderBrush = new SolidColorBrush(Color.FromRgb(222,226,237)), BorderThickness = new Thickness(1) };
        button.Click += (_, _) => action(); return button;
    }
    static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12), Foreground = Brushes.SlateGray };
    static Window Dialog(string title, Avalonia.Controls.Control content, double width = 630, double height = 460) => new() {
        Title = title, Width = width, Height = height, MinWidth = 480, MinHeight = 360,
        FontFamily = new FontFamily("Noto Sans CJK SC"), FontSize = 14,
        WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(247, 248, 251)),
        Content = new Border { Padding = new Thickness(28), Child = content } };
    async Task<bool> Confirm(string title, string message, string accept = "确定", CancellationToken token = default, string reject = "取消")
    {
        if (consentOpen) throw new InvalidOperationException("请先处理已打开的提示窗口。");
        consentOpen = true; bool accepted = false;
        try {
            var panel = new StackPanel { Spacing = 15 };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeight.SemiBold }); panel.Children.Add(Paragraph(message));
            var dialog = Dialog(title, panel); var buttons = new WrapPanel();
            buttons.Children.Add(ActionButton(accept, () => { accepted = true; dialog.Close(); })); buttons.Children.Add(ActionButton(reject, dialog.Close)); panel.Children.Add(buttons);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { accepted = false; dialog.Close(); }));
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => finished.TrySetResult(accepted && !token.IsCancellationRequested);
            consentWindow = dialog;
            if (settings?.IsVisible == true) dialog.Show(settings); else dialog.Show();
            return await finished.Task;
        } finally { consentWindow = null; consentOpen = false; }
    }
    void OpenSettings()
    {
        if (settings != null) { if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal; settings.Activate(); return; }
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*") };
        var navigation = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 20, 0) };
        var body = new StackPanel { Spacing = 8 }; var scroll = new ScrollViewer { Content = body }; Grid.SetColumn(scroll, 1); root.Children.Add(navigation); root.Children.Add(scroll);
        settings = Dialog("蓝色大肥鱼 · 设置", root, 880, 700); settings.MinWidth = 780; settings.MinHeight = 600;
        settings.Closed += (_, _) => { disposeLogConsole?.Invoke(); disposeLogConsole = null; settings = null; refreshLinuxSettings = null; DiagnosticLog.Write(DiagnosticEvent.SettingsClosed); Save(); nextEvent = DateTime.UtcNow.AddSeconds(Preferences.AnimationIntervalSeconds); };
        Action selectedPage = General;
        void Navigate(Action page) { selectedPage = page; page(); }
        void Title(string text) { disposeLogConsole?.Invoke(); disposeLogConsole = null; body.Children.Clear(); body.Children.Add(new TextBlock { Text = text, FontSize = 25, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 16) }); }
        CheckBox Toggle(string text, bool value, Action<bool> set) {
            var box = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 4) };
            box.IsCheckedChanged += (_, _) => {
                bool enabled = box.IsChecked == true;
                BigBlueFish.Diagnostics.DiagnosticLog.Write(BigBlueFish.Diagnostics.DiagnosticEvent.SettingsChanged, kind: "setting", state: "start", enabled: enabled);
                try {
                    set(enabled); Save();
                    BigBlueFish.Diagnostics.DiagnosticLog.Write(BigBlueFish.Diagnostics.DiagnosticEvent.SettingsChanged, kind: "setting", state: "success", enabled: enabled);
                } catch (Exception error) {
                    BigBlueFish.Diagnostics.DiagnosticLog.Write(BigBlueFish.Diagnostics.DiagnosticEvent.SettingsChanged, kind: "setting", state: "failed", enabled: enabled, error: error);
                }
            };
            body.Children.Add(box); return box;
        }
        void Slider(string text, double value, double low, double high, Action<double> set, double tick = 1) {
            var label = new TextBlock { Text = text + "：" + value.ToString("0.##"), Margin = new Thickness(0, 12, 0, 0) };
            var slider = new Avalonia.Controls.Slider { Minimum = low, Maximum = high, Value = value, TickFrequency = tick, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => { label.Text = text + "：" + slider.Value.ToString("0.##"); set(slider.Value); Save(); }; body.Children.Add(label); body.Children.Add(slider);
        }
        ComboBox Select(string label, string[] choices, int selected, Action<int> set) {
            body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 4) }); var combo = new ComboBox { ItemsSource = choices, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) { set(combo.SelectedIndex); Save(); } }; body.Children.Add(combo); return combo;
        }
        void General() {
            Title("桌宠与显示"); Slider("桌宠大小（%）", Preferences.Scale * 100, 50, 200, value => { Preferences.Scale = value / 100; Rescale(); Clamp(); }, 5);
            Slider("文字气泡大小（%）", Preferences.BubbleScale * 100, 50, 200, value => { Preferences.BubbleScale = value / 100; MoveBubble(); }, 5);
            Toggle("开机启动", StartupEnabled, SetStartup); Toggle("全屏时自动隐藏桌宠", Preferences.HideOnFullscreen, value => { Preferences.HideOnFullscreen = value; ApplySettings(); });
            Select("气泡驻留时间", CompanionPreferences.BubbleDurationLabels, Preferences.BubbleDurationIndex, value => { Preferences.BubbleDurationIndex = value; ResetDwell(); });
        }
        void Model() {
            Title("模型与信息"); Toggle("启用本地大模型", Preferences.ModelEnabled, value => { Preferences.ModelEnabled = value; ApplySettings(); });
            TextBlock? modelApiLabel = null; TextBox? modelApiUrl = null; CheckBox? gpuToggle = null, batteryToggle = null; Button? modelInstallerButton = null;
            void RefreshBackendControls() {
                bool builtin = Preferences.ModelBackend != "api";
                if (!builtin) Preferences.GpuEnabled = Preferences.StopModelOnBattery = false;
                if (modelApiLabel != null) modelApiLabel.IsVisible = !builtin;
                if (modelApiUrl != null) modelApiUrl.IsVisible = !builtin;
                if (gpuToggle != null) { gpuToggle.IsEnabled = builtin; gpuToggle.IsChecked = Preferences.GpuEnabled; }
                if (batteryToggle != null) { batteryToggle.IsEnabled = builtin; batteryToggle.IsChecked = Preferences.StopModelOnBattery; }
                if (modelInstallerButton != null) modelInstallerButton.IsVisible = builtin;
            }
            Select("本地模型调用方式", new[] { "内置模型（默认）", "本地 llama API" }, Preferences.ModelBackend == "api" ? 1 : 0,
                value => { Preferences.ModelBackend = value == 1 ? "api" : "builtin"; if (value == 1) Preferences.GpuEnabled = Preferences.StopModelOnBattery = false; ApplySettings(); RefreshBackendControls(); });
            modelApiLabel = new TextBlock { Text = "llama API 地址（OpenAI 兼容 /v1）", Margin = new Thickness(0, 4, 0, 2), IsVisible = Preferences.ModelBackend == "api" }; body.Children.Add(modelApiLabel);
            modelApiUrl = new TextBox { Text = Preferences.LocalLlamaApiUrl, IsVisible = Preferences.ModelBackend == "api" };
            modelApiUrl.LostFocus += (_, _) => { Preferences.LocalLlamaApiUrl = modelApiUrl.Text?.Trim() ?? ""; ApplySettings(); };
            body.Children.Add(modelApiUrl);
            gpuToggle = Toggle("使用 GPU 加速", Preferences.GpuEnabled, value => { Preferences.GpuEnabled = value; ApplySettings(); });
            var deviceStatus = Paragraph(""); body.Children.Add(deviceStatus);
            var deviceClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            void UpdateDevice() => deviceStatus.Text = "推理设备：" + brain.ExecutionDevice;
            deviceClock.Tick += (_, _) => UpdateDevice();
            deviceStatus.AttachedToVisualTree += (_, _) => { UpdateDevice(); deviceClock.Start(); };
            deviceStatus.DetachedFromVisualTree += (_, _) => deviceClock.Stop();
            batteryToggle = Toggle("笔记本离电时暂停模型", Preferences.StopModelOnBattery, value => { Preferences.StopModelOnBattery = value; ApplySettings(); });
            RefreshBackendControls();
            CheckBox? weatherEnabled = null, newsEnabled = null;
            bool synchronizingPublicInfo = false;
            int publicInfoIndex = body.Children.Count;
            int RoleIndex()
            {
                if (Preferences.SpeakerRole == "assistant") return 0;
                if (Preferences.SpeakerRole == "owner") return 1;
                int roleIndex = Preferences.CustomRoles.FindIndex(role => role.Id == Preferences.SpeakerRole);
                return roleIndex >= 0 ? roleIndex + 2 : 0;
            }
            body.Children.Add(new TextBlock { Text = "角色定位", Margin = new Thickness(0, 10, 0, 4) });
            var roleSelect = new ComboBox
            {
                ItemsSource = new[] { "助手（默认）", "主人" }.Concat(Preferences.CustomRoles.Select(role => role.Name)).ToArray(),
                SelectedIndex = RoleIndex(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            body.Children.Add(roleSelect);
            var roleName = new TextBox { Text = "新角色", MaxLength = 40, Width = 220 };
            var roleError = Paragraph("");
            Button addRole = null!, removeRole = null!;
            var rolePromptEditors = new TextBox[3];
            var rolePromptHeaders = new TextBlock[3];
            var rolePromptResets = new Button[3];
            bool updatingRoleControls = false;
            string displayedRoleId = Preferences.SpeakerRole;
            var roleActions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            addRole = ActionButton("新增角色", () => {
                try { CommitRolePrompts(); Preferences.AddCustomRole(roleName.Text ?? ""); roleError.Text = ""; ApplyRoleChanges(); RefreshRoleControls(); }
                catch (Exception error) { roleError.Text = error.Message; }
            });
            removeRole = ActionButton("删除角色", () => {
                var profile = Preferences.SelectedCustomRole;
                if (profile == null) return;
                CommitRolePrompts();
                Preferences.CustomRoles.Remove(profile); Preferences.SpeakerRole = "assistant"; ApplyRoleChanges(); RefreshRoleControls();
            });
            roleActions.Children.Add(roleName); roleActions.Children.Add(addRole); roleActions.Children.Add(removeRole);
            body.Children.Add(roleActions); body.Children.Add(roleError);
            body.Children.Add(Paragraph("可为不同角色分别设置普通说话、天气和新闻提示词；时间、屏幕与公共信息由程序单独提供，无需模板变量。"));
            void ApplyRoleChanges()
            {
                speechVersion++;
                brain.CancelGeneration();
                loading.IsVisible = false;
                Preferences.ApplySpeakerRole(brain);
                Save();
            }
            CompanionRoleProfile? RoleProfile(string roleId) => Preferences.CustomRoles.FirstOrDefault(role => role.Id == roleId);
            bool IsRolePromptEditable(string roleId) => roleId != "assistant";
            string RolePrompt(string roleId, int index) {
                if (roleId == "assistant") return index switch {
                    0 => CompanionBrain.FixedSpeechInstructions(CompanionBrain.InputMode == "OCR" ? CompanionBrain.OcrPersona : CompanionBrain.Persona),
                    1 => CompanionBrain.WeatherPersona,
                    _ => CompanionBrain.NewsPersona
                };
                var profile = RoleProfile(roleId);
                if (profile != null) return index switch { 0 => profile.Prompt, 1 => profile.WeatherPrompt, _ => profile.NewsPrompt };
                return index switch { 0 => Preferences.OwnerPrompt, 1 => Preferences.OwnerWeatherPrompt, _ => Preferences.OwnerNewsPrompt };
            }
            string DefaultRolePrompt(string roleId, int index) {
                var profile = RoleProfile(roleId);
                if (profile != null) {
                    var defaults = CompanionRoleProfile.Create(profile.Name);
                    return index switch { 0 => defaults.Prompt, 1 => defaults.WeatherPrompt, _ => defaults.NewsPrompt };
                }
                return index switch { 0 => CompanionDialogue.OwnerPromptDefault, 1 => CompanionDialogue.OwnerWeatherPromptDefault, _ => CompanionDialogue.OwnerNewsPromptDefault };
            }
            void SaveRolePrompt(string roleId, int index, string text) {
                if (!IsRolePromptEditable(roleId)) return;
                var profile = RoleProfile(roleId);
                if (profile != null) {
                    if (index == 0) profile.Prompt = text; else if (index == 1) profile.WeatherPrompt = text; else profile.NewsPrompt = text;
                } else {
                    if (index == 0) Preferences.OwnerPrompt = text; else if (index == 1) Preferences.OwnerWeatherPrompt = text; else Preferences.OwnerNewsPrompt = text;
                }
                ApplyRoleChanges();
            }
            void CommitRolePrompts() {
                if (!IsRolePromptEditable(displayedRoleId)) return;
                bool changed = false;
                var profile = RoleProfile(displayedRoleId);
                for (int i = 0; i < rolePromptEditors.Length; i++) {
                    string text = rolePromptEditors[i].Text ?? "";
                    if (string.IsNullOrWhiteSpace(text) && i != 1) text = DefaultRolePrompt(displayedRoleId, i);
                    if (text == RolePrompt(displayedRoleId, i)) continue;
                    if (profile != null) {
                        if (i == 0) profile.Prompt = text; else if (i == 1) profile.WeatherPrompt = text; else profile.NewsPrompt = text;
                    } else {
                        if (i == 0) Preferences.OwnerPrompt = text; else if (i == 1) Preferences.OwnerWeatherPrompt = text; else Preferences.OwnerNewsPrompt = text;
                    }
                    changed = true;
                }
                if (changed) ApplyRoleChanges();
            }
            void RefreshRoleEditors() {
                var profile = Preferences.SelectedCustomRole;
                displayedRoleId = Preferences.SpeakerRole;
                roleName.Text = profile?.Name ?? "";
                roleName.IsEnabled = profile != null;
                removeRole.IsEnabled = profile != null;
                addRole.IsEnabled = Preferences.CustomRoles.Count < 20;
                for (int i = 0; i < rolePromptEditors.Length; i++) {
                    string suffix = i switch { 0 => "普通说话提示词", 1 => "天气提示词", _ => "科技新闻提示词" };
                    rolePromptHeaders[i].Text = displayedRoleId == "assistant" ? "鱼鱼助手（内置）" + suffix : profile == null ? "主人" + suffix : profile.Name + suffix;
                    rolePromptEditors[i].Text = RolePrompt(displayedRoleId, i);
                    rolePromptEditors[i].IsEnabled = IsRolePromptEditable(displayedRoleId);
                    rolePromptResets[i].IsEnabled = IsRolePromptEditable(displayedRoleId);
                }
            }
            void RefreshRoleControls() {
                updatingRoleControls = true;
                try {
                    roleSelect.ItemsSource = new[] { "助手（默认）", "主人" }.Concat(Preferences.CustomRoles.Select(role => role.Name)).ToArray();
                    roleSelect.SelectedIndex = RoleIndex();
                    RefreshRoleEditors();
                }
                finally { updatingRoleControls = false; }
            }
            roleSelect.SelectionChanged += (_, _) => {
                if (updatingRoleControls || roleSelect.SelectedIndex < 0) return;
                int value = roleSelect.SelectedIndex;
                if (value >= 2 && value - 2 >= Preferences.CustomRoles.Count) { RefreshRoleControls(); return; }
                try {
                    CommitRolePrompts();
                    Preferences.SpeakerRole = value switch { 0 => "assistant", 1 => "owner", _ => Preferences.CustomRoles[value - 2].Id };
                    ApplyRoleChanges();
                    RefreshRoleEditors();
                } catch (Exception error) {
                    DiagnosticLog.Write(DiagnosticEvent.SettingsChanged, kind: "speaker-role", state: "failed", error: error);
                    roleError.Text = "身份设定未能应用：" + error.Message;
                    RefreshRoleControls();
                }
            };
            roleName.LostFocus += (_, _) => {
                var profile = Preferences.SelectedCustomRole;
                if (profile == null || string.IsNullOrWhiteSpace(roleName.Text)) { RefreshRoleControls(); return; }
                string name = roleName.Text.Trim();
                if (Preferences.CustomRoles.Any(role => role != profile && string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase))) {
                    roleError.Text = "角色名称已存在，请使用不同的名称。"; RefreshRoleControls(); return;
                }
                if (profile.Name != name) { CommitRolePrompts(); profile.Name = name; roleError.Text = ""; ApplyRoleChanges(); RefreshRoleControls(); }
            };
            void RolePromptEditor(int index) {
                string suffix = index switch { 0 => "普通说话提示词", 1 => "天气提示词", _ => "科技新闻提示词" };
                var editor = new TextBox { Text = RolePrompt(displayedRoleId, index), AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Height = index == 0 ? 210 : 120, MaxLength = 16000 };
                rolePromptEditors[index] = editor;
                var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 2) };
                var heading = rolePromptHeaders[index] = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                header.Children.Add(heading);
                var reset = ActionButton("恢复默认", () => { string roleId = displayedRoleId; string value = DefaultRolePrompt(roleId, index); editor.Text = value; SaveRolePrompt(roleId, index, value); });
                rolePromptResets[index] = reset;
                Grid.SetColumn(reset, 1); header.Children.Add(reset); body.Children.Add(header);
                editor.LostFocus += (_, _) => {
                    string roleId = displayedRoleId;
                    var text = editor.Text ?? "";
                    if (string.IsNullOrWhiteSpace(text) && index != 1) text = DefaultRolePrompt(roleId, index);
                    editor.Text = text;
                    if (text != RolePrompt(roleId, index)) SaveRolePrompt(roleId, index, text);
                };
                body.Children.Add(editor);
            }
            RolePromptEditor(0); RolePromptEditor(1); RolePromptEditor(2); RefreshRoleControls();
            newsEnabled = Toggle("主题灵感", Preferences.NewsEnabled, value => { Preferences.NewsEnabled = value; if (!synchronizingPublicInfo) ApplySettings(); });
            newsEnabled.IsEnabled = Preferences.PublicInfo;
            var chatty = Toggle("随聊", Preferences.Chatty, value => { Preferences.Chatty = value; ApplySettings(); });
            var source = Select("主题灵感来源", new[] { "科技新闻", "从文件读取", "从 API 读取" },
                Preferences.NewsSource switch { "file" => 1, "api" => 2, _ => 0 }, value => { Preferences.NewsSource = value switch { 1 => "file", 2 => "api", _ => "builtin" }; brain.RefreshNews(); });
            var newsLocationLabel = new TextBlock { Margin = new Thickness(0, 4, 0, 2) };
            body.Children.Add(newsLocationLabel);
            string CurrentNewsLocation() => Preferences.NewsSource switch { "file" => Preferences.NewsFilePath, "api" => Preferences.NewsApiUrl, _ => "" };
            var newsLocation = new TextBox { Text = CurrentNewsLocation() };
            void RefreshNewsLocation() {
                newsLocationLabel.Text = Preferences.NewsSource switch { "file" => "新闻文件路径（RSS/Atom 或 JSON）", "api" => "新闻 API 地址（RSS/Atom 或 JSON）", _ => "使用内置科技新闻" };
                bool customLocation = Preferences.NewsSource is "file" or "api";
                newsLocationLabel.IsVisible = customLocation;
                newsLocation.Text = CurrentNewsLocation();
                newsLocation.IsVisible = customLocation;
                newsLocation.IsEnabled = Preferences.PublicInfo && Preferences.NewsEnabled && customLocation;
                source.IsEnabled = Preferences.PublicInfo && Preferences.NewsEnabled;
                chatty.IsEnabled = Preferences.PublicInfo && Preferences.NewsEnabled;
                if (weatherEnabled != null) weatherEnabled.IsEnabled = Preferences.PublicInfo;
            }
            var publicInfoToggle = Toggle("自动获取天气和主题灵感", Preferences.PublicInfo, value => {
                Preferences.SetPublicInfo(value);
                synchronizingPublicInfo = true;
                try {
                    if (weatherEnabled != null) weatherEnabled.IsChecked = value;
                    if (newsEnabled != null) newsEnabled.IsChecked = value;
                } finally { synchronizingPublicInfo = false; }
                RefreshNewsLocation();
                ApplySettings();
            });
            body.Children.Remove(publicInfoToggle);
            body.Children.Insert(publicInfoIndex, publicInfoToggle);
            source.SelectionChanged += (_, _) => RefreshNewsLocation();
            publicInfoToggle.IsCheckedChanged += (_, _) => RefreshNewsLocation();
            newsEnabled.IsCheckedChanged += (_, _) => RefreshNewsLocation();
            newsLocation.LostFocus += (_, _) => { var value = newsLocation.Text?.Trim() ?? ""; switch (Preferences.NewsSource) { case "file": Preferences.NewsFilePath = value; break; case "api": Preferences.NewsApiUrl = value; break; } brain.RefreshNews(); Save(); };
            body.Children.Add(newsLocation);
            RefreshNewsLocation();
            Toggle("感知当前前台应用", Preferences.ForegroundEnabled, value => { Preferences.ForegroundEnabled = value; ApplySettings(); });
#if !COMPANION_SAFE
            body.Children.Add(Paragraph("本地吐字在本机读取屏幕；由 Agent 编写日常关心时，会向它提供前台截图。"));
#endif
            modelInstallerButton = ActionButton(brain.ModelAvailable ? "管理本地模型" : "安装模型", async () => await InstallModel()); body.Children.Add(modelInstallerButton);
            var frequency = Select("自动说话间隔", CompanionPreferences.SpeechIntervals.Select(r => r.Label).ToArray(), Preferences.SpeechIntervalIndex, value => { Preferences.SetSpeechInterval(CompanionPreferences.SpeechIntervals[value].Id); nextSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); });
            frequency.IsEnabled = !Puppet;
            body.Children.Add(Paragraph(Puppet ? "日常关心的触发时机已交给 Agent，此时说话间隔不可调节。关闭接管触发后恢复使用此设置。" : AgentDailyActive ? "鱼鱼按此间隔触发日常关心，由 Agent 编写吐字。" : "鱼鱼按此间隔触发日常关心，由本地模型编写吐字。"));
            body.Children.Add(new TextBlock { Text = "天气地区", Margin = new Thickness(0,12,0,4) });
            weatherEnabled = Toggle("自动获取天气", Preferences.WeatherEnabled, value => { Preferences.WeatherEnabled = value; if (!synchronizingPublicInfo) ApplySettings(); });
            weatherEnabled.IsEnabled = Preferences.PublicInfo;
            var regions = new StackPanel { Spacing = 8 }; body.Children.Add(regions);
            var country = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = CompanionRegions.Countries.Select(c => c.Name).ToArray() };
            regions.Children.Add(country);
            var levels = Enumerable.Range(0, 3).Select(_ => new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch }).ToArray();
            foreach (var level in levels) regions.Children.Add(level);
            var options = new CompanionRegion[levels.Length][];
            var selected = CompanionRegions.District(Preferences.WeatherRegionCode); var current = CompanionRegions.Country(selected); bool changing = true;
            void Fill(int from, string parentCode, CompanionRegion[] keep) {
                for (int i = from; i < levels.Length; i++) {
                    levels[i].IsVisible = i <= current.LeafLevel;
                    options[i] = i <= current.LeafLevel ? CompanionRegions.Children(parentCode) : [];
                    levels[i].ItemsSource = options[i].Select(r => r.Name).ToArray();
                    levels[i].SelectedIndex = options[i].Length == 0 ? -1 : Math.Max(0, Array.FindIndex(options[i], r => i < keep.Length && r.Code == keep[i].Code));
                    if (levels[i].SelectedIndex >= 0) parentCode = options[i][levels[i].SelectedIndex].Code;
                }
            }
            country.SelectedIndex = Array.IndexOf(CompanionRegions.Countries, current); Fill(0, current.Root, CompanionRegions.Chain(selected)); changing = false;
            void RegionChanged() { var leaf = levels[current.LeafLevel]; if (leaf.SelectedIndex >= 0 && leaf.SelectedIndex < options[current.LeafLevel].Length) { Preferences.WeatherRegionCode = options[current.LeafLevel][leaf.SelectedIndex].Code; ApplySettings(); } }
            country.SelectionChanged += (_,_) => { if (changing || country.SelectedIndex < 0 || CompanionRegions.Countries[country.SelectedIndex] == current) return; changing = true; current = CompanionRegions.Countries[country.SelectedIndex]; Fill(0, current.Root, current == CompanionRegions.China ? CompanionRegions.Chain(CompanionRegions.District(CompanionRegions.DefaultCode)) : []); changing = false; RegionChanged(); };
            for (int level = 0; level < levels.Length; level++) { var at = level; levels[at].SelectionChanged += (_,_) => { if (changing || levels[at].SelectedIndex < 0) return; changing = true; if (at < current.LeafLevel) Fill(at + 1, options[at][levels[at].SelectedIndex].Code, []); changing = false; RegionChanged(); }; }
            body.Children.Add(Paragraph("天气来自 Open-Meteo（Weather data by Open-Meteo.com）；美国地点使用华氏度和英里/小时。海外城市数据来自 GeoNames（CC BY 4.0）。"));
        }
        void Animation() {
            Title("动作与作息"); Slider("动作触发间隔（秒）", Preferences.AnimationIntervalSeconds, 30, 300, value => { Preferences.AnimationIntervalSeconds = (int)value; nextEvent = DateTime.UtcNow.AddSeconds(value); });
            foreach (var meal in new[] { ("早餐", "BreakfastHour"), ("午餐", "LunchHour"), ("晚餐", "DinnerHour") }) {
                var property = typeof(CompanionPreferences).GetProperty(meal.Item2)!;
                Slider(meal.Item1 + "时间（时）", (int)property.GetValue(Preferences)!, 0, 23, value => property.SetValue(Preferences, (int)value));
            }
        }
        void About() {
            Title("关于与更新"); body.Children.Add(Paragraph("蓝色大肥鱼 " + typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3) + (McpAvailable ? "" : " · 无MCP版")));
            Toggle("每次启动自动检查更新", Preferences.CheckUpdatesOnStartup, value => Preferences.CheckUpdatesOnStartup = value);
            body.Children.Add(ActionButton("检查更新", async () => await CheckUpdates(true)));
        }
        navigation.Children.Add(ActionButton("桌宠与显示", () => Navigate(General))); navigation.Children.Add(ActionButton("模型与信息", () => Navigate(Model))); navigation.Children.Add(ActionButton("动作与作息", () => Navigate(Animation)));
#if !COMPANION_SAFE
        void Agent() { Title("连接 Agent"); BuildAgentSettings(body); }
        navigation.Children.Add(ActionButton("连接 Agent", () => Navigate(Agent)));
#endif
        navigation.Children.Add(ActionButton("关于与更新", () => Navigate(About)));
        refreshLinuxSettings = () => { var offset = scroll.Offset; selectedPage(); scroll.Offset = offset; };
        void Logs() { Title("日志控制台"); disposeLogConsole = BuildLogConsole(body); }
        navigation.Children.Add(ActionButton("日志控制台", () => Navigate(Logs)));
        DiagnosticLog.Write(DiagnosticEvent.SettingsOpened);
        General(); settings.Show(); settings.Activate();
    }
}
