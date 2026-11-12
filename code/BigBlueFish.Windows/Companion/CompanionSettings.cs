using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BigBlueFish.Windows;
public partial class MainWindow
{
    private Action? refreshCompanionSettings;
    private Action? disposeCompanionSettingsContent;
    private void OpenCompanionSettings()
    {
        if (exitAnimation || companionClosed || exitReady) return;
        if (companionSettings != null)
        {
            ActivateCompanionSettings(companionSettings);
            return;
        }

        CancelMovement();
        var w = new Window
        {
            Title = "大肥鱼设置",
            Width = Math.Min(860, SystemParameters.WorkArea.Width - 32),
            Height = Math.Min(690, SystemParameters.WorkArea.Height - 32),
            MinWidth = Math.Min(680, SystemParameters.WorkArea.Width - 32),
            MinHeight = Math.Min(450, SystemParameters.WorkArea.Height - 32),
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 252)),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            ShowInTaskbar = true,
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "BigBlueFish.ico"))),
            ShowActivated = true,
            Topmost = false
        };
        w.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/BigBlueFish;component/Companion/CompanionSettingsStyles.xaml", UriKind.Relative) });
        companionSettings = w;
        DiagnosticLog.Write(DiagnosticEvent.SettingsOpened);
        BuildCompanionSettingsContent(w);
        w.Closed += (_, _) =>
        {
            disposeCompanionSettingsContent?.Invoke();
            disposeCompanionSettingsContent = refreshCompanionSettings = null;
            companionSettings = null;
            DiagnosticLog.Write(DiagnosticEvent.SettingsClosed);
            SaveCompanion();
        };
        ActivateCompanionSettings(w);
    }

    private void BuildCompanionSettingsContent(Window w, int selectedPage = 0, double[]? scrollOffsets = null)
    {
        disposeCompanionSettingsContent?.Invoke();
        var shell = new Grid { Background = w.Background };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        w.Content = shell;
        var sidebar = new StackPanel { Margin = new Thickness(18, 24, 18, 18) };
        var sidebarFrame = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 233, 243)), BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar };
        shell.Children.Add(sidebarFrame);
        sidebar.Children.Add(new TextBlock { Text = "蓝色大肥鱼", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(53, 76, 129)), Margin = new Thickness(5, 0, 0, 8) });
        var version = typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion.Split('+')[0];
#if COMPANION_SAFE
        version += " · 无MCP版";
#endif
        sidebar.Children.Add(new TextBlock { Text = version + " · 陪伴设置", Foreground = Brushes.SlateGray, Margin = new Thickness(5, 0, 0, 28) });
#if COMPANION_SAFE
        var titles = new[] { "陪伴与外观", "说话与天气", "模型与供电", "日志控制台" };
#else
        var titles = new[] { "陪伴与外观", "说话与天气", "模型与供电", "连接 Agent", "日志控制台" };
#endif
        var pages = new StackPanel[titles.Length];
        var views = new ScrollViewer[titles.Length];
        var navigation = new Button[titles.Length];
        var pageHost = new ContentControl();
        Grid.SetColumn(pageHost, 1);
        shell.Children.Add(pageHost);
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i] = new StackPanel { Margin = new Thickness(26, 24, 26, 24) };
            pages[i].Children.Add(new TextBlock { Text = titles[i], FontSize = 25, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(42, 56, 83)), Margin = new Thickness(0, 0, 0, 20) });
            views[i] = new ScrollViewer { Content = pages[i], VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            int selected = i;
            var button = navigation[i] = new Button { Content = titles[i], HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = i == selectedPage ? new SolidColorBrush(Color.FromRgb(220, 234, 255)) : Brushes.Transparent };
            button.Click += (_, _) =>
            {
                DiagnosticLog.Write(DiagnosticEvent.SettingsPageChanged, state: "start", count: selected);
                try
                {
                    CloseDropDowns(pageHost);
                    selectedPage = selected;
                    pageHost.Content = views[selected];
                    DiagnosticLog.Write(DiagnosticEvent.SettingsPageChanged, state: "success", count: selected);
                    for (int n = 0; n < views.Length; n++)
                        navigation[n].Background = n == selected ? new SolidColorBrush(Color.FromRgb(220, 234, 255)) : Brushes.Transparent;
                }
                catch (Exception e)
                {
                    DiagnosticLog.Write(DiagnosticEvent.SettingsPageChanged, state: "failed", count: selected, error: e);
                    BigBlueFish.Deployment.ModernUI.Error(w, "切换设置页面失败", e);
                }
            };
            sidebar.Children.Add(button);
        }
        pageHost.Content = views[selectedPage];
        void Card(StackPanel panel)
        {
            var children = panel.Children.Cast<UIElement>().Skip(1).ToArray();
            foreach (var child in children) panel.Children.Remove(child);
            var inner = new StackPanel { Margin = new Thickness(20) };
            foreach (var child in children) inner.Children.Add(child);
            panel.Children.Add(new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 233, 243)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Child = inner });
        }
        var p = pages[2];
        CheckBox Toggle(string label, bool value, Action<bool> change)
        {
            var c = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Margin = new Thickness(0, 7, 0, 7)
            };
            c.Click += (_, _) =>
            {
                bool enabled = c.IsChecked == true;
                BigBlueFish.Diagnostics.DiagnosticLog.Write(BigBlueFish.Diagnostics.DiagnosticEvent.SettingsChanged, kind: "setting", state: "start", enabled: enabled);
                try
                {
                    change(enabled);
                    BigBlueFish.Diagnostics.DiagnosticLog.Write(BigBlueFish.Diagnostics.DiagnosticEvent.SettingsChanged, kind: "setting", state: "success", enabled: enabled);
                    SaveCompanion();
                }
                catch (Exception error) { BigBlueFish.Diagnostics.DiagnosticLog.Write(BigBlueFish.Diagnostics.DiagnosticEvent.SettingsChanged, kind: "setting", state: "failed", enabled: enabled, error: error); BigBlueFish.Deployment.ModernUI.Error(w, "设置未能应用", error); }
            };
            p.Children.Add(c);
            return c;
        }

        Toggle("启用本地大模型", preferences.ModelEnabled, v =>
        {
            preferences.ModelEnabled = v;
            if (!v)
                InvalidateSpeechRequests(true);
            else
            {
                nextWarmup = DateTime.MinValue;
                nextSpeech = DateTime.UtcNow.AddSeconds(20);
            }
        });
        p.Children.Add(new TextBlock { Text = "本地模型调用方式", Margin = new Thickness(0, 8, 0, 4) });
        var modelBackend = new ComboBox { ItemsSource = new[] { "内置模型（默认）", "本地 llama API" }, SelectedIndex = preferences.ModelBackend == "api" ? 1 : 0 };
        p.Children.Add(modelBackend);
        var modelApiLabel = new TextBlock { Text = "本地 API 地址（OpenAI 兼容；可填写任意路径前缀，如 /api）", Margin = new Thickness(0, 8, 0, 4) };
        p.Children.Add(modelApiLabel);
        var modelApiUrl = new TextBox { Text = preferences.LocalLlamaApiUrl, MinWidth = 300 };
        p.Children.Add(modelApiUrl);
        CheckBox? gpuToggle = null, batteryToggle = null;
        Button? installModel = null;
        void RefreshBackendControls()
        {
            bool builtin = preferences.ModelBackend != "api";
            if (!builtin) preferences.GpuEnabled = preferences.StopModelOnBattery = false;
            modelApiLabel.Visibility = builtin ? Visibility.Collapsed : Visibility.Visible;
            modelApiUrl.Visibility = builtin ? Visibility.Collapsed : Visibility.Visible;
            if (gpuToggle != null) { gpuToggle.IsEnabled = builtin; gpuToggle.IsChecked = preferences.GpuEnabled; }
            if (batteryToggle != null) { batteryToggle.IsEnabled = builtin; batteryToggle.IsChecked = preferences.StopModelOnBattery; }
            if (installModel != null) installModel.Visibility = builtin ? Visibility.Visible : Visibility.Collapsed;
            modelBackend.SelectedIndex = builtin ? 0 : 1;
        }
        modelBackend.SelectionChanged += (_, _) =>
        {
            if (modelBackend.SelectedIndex < 0) return;
            InvalidateSpeechRequests(true);
            preferences.ModelBackend = modelBackend.SelectedIndex == 1 ? "api" : "builtin";
            if (preferences.ModelBackend == "api") preferences.GpuEnabled = preferences.StopModelOnBattery = false;
            brain.UseExternalApi = preferences.ModelBackend == "api"; brain.UseGpu = preferences.GpuEnabled; brain.StopOnBattery = preferences.StopModelOnBattery;
            brain.RefreshModelAvailability(); nextWarmup = DateTime.MinValue; RefreshBackendControls(); SaveCompanion();
        };
        modelApiUrl.LostKeyboardFocus += (_, _) => { preferences.LocalLlamaApiUrl = modelApiUrl.Text.Trim(); brain.ExternalApiUrl = preferences.LocalLlamaApiUrl; SaveCompanion(); };
        batteryToggle = Toggle("拔电时暂停并卸载模型", preferences.StopModelOnBattery, v =>
        {
            preferences.StopModelOnBattery = v;
            ApplyCompanionPowerPolicy();
        });
        gpuToggle = Toggle("使用显卡加速（关闭则使用 CPU）", preferences.GpuEnabled, v =>
        {
            preferences.GpuEnabled = v;
            InvalidateSpeechRequests(true);
            brain.UseGpu = v;
            nextWarmup = DateTime.MinValue;
        });
        RefreshBackendControls();
        p = pages[0];
        Toggle("全屏时自动隐藏桌宠", preferences.HideOnFullscreen, v =>
        {
            preferences.HideOnFullscreen = v;
            UpdateCompanionVisibility();
        });
        Toggle("开机自动启动", StartupEnabled(), SetCompanionStartup);
        Toggle("启动时检查更新", preferences.CheckUpdatesOnStartup, v => preferences.CheckUpdatesOnStartup = v);
        p = pages[1];
        CheckBox? weatherEnabled = null, newsEnabled = null;
        int publicInfoIndex = p.Children.Count;
        p.Children.Add(new TextBlock { Text = "角色定位", Margin = new Thickness(0, 0, 0, 4) });
        var speakerRole = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        p.Children.Add(speakerRole);
        var roleActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var roleName = new TextBox { Text = "新角色", Width = 220, MaxLength = 40, VerticalContentAlignment = VerticalAlignment.Center };
        var addRole = new Button { Content = "新增角色", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
        var removeRole = new Button { Content = "删除角色", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
        roleActions.Children.Add(roleName);
        roleActions.Children.Add(addRole);
        roleActions.Children.Add(removeRole);
        p.Children.Add(roleActions);
        bool updatingRoleControls = false;
        string displayedRoleId = preferences.SpeakerRole;
        var rolePromptEditors = new TextBox[3];
        var rolePromptHeaders = new TextBlock[3];
        var rolePromptResets = new Button[3];
        int SelectedRoleIndex() => speakerRole.SelectedIndex;
        CompanionRoleProfile? ActiveRole() => SelectedRoleIndex() >= 2 && SelectedRoleIndex() - 2 < preferences.CustomRoles.Count
            ? preferences.CustomRoles[SelectedRoleIndex() - 2] : null;
        string[] RoleOptions() => new[] { "助手（默认）", "主人" }.Concat(preferences.CustomRoles.Select(role => role.Name)).ToArray();
        bool IsRolePromptEditable(string roleId) => roleId != "assistant";
        int RoleIndex() => preferences.SpeakerRole == "assistant" ? 0
            : preferences.SpeakerRole == "owner" ? 1
            : Math.Max(0, preferences.CustomRoles.FindIndex(role => role.Id == preferences.SpeakerRole) + 2);
        void RefreshRoleEditors()
        {
            var profile = ActiveRole();
            displayedRoleId = preferences.SpeakerRole;
            roleName.Text = profile?.Name ?? "";
            roleName.IsEnabled = profile != null;
            removeRole.IsEnabled = profile != null;
            addRole.IsEnabled = preferences.CustomRoles.Count < 20;
            for (int i = 0; i < rolePromptEditors.Length; i++)
            {
                string suffix = i switch { 0 => "普通说话提示词", 1 => "天气提示词", _ => "科技新闻提示词" };
                string label = displayedRoleId == "assistant" ? "鱼鱼助手（内置）" + suffix : profile == null ? "主人" + suffix : profile.Name + suffix;
                rolePromptHeaders[i].Text = label;
                rolePromptEditors[i].Text = RolePrompt(displayedRoleId, i);
                rolePromptEditors[i].IsEnabled = IsRolePromptEditable(displayedRoleId);
                rolePromptResets[i].IsEnabled = IsRolePromptEditable(displayedRoleId);
                System.Windows.Automation.AutomationProperties.SetName(rolePromptEditors[i], label);
            }
        }
        void RefreshRoleControls()
        {
            updatingRoleControls = true;
            try
            {
                speakerRole.ItemsSource = RoleOptions();
                speakerRole.SelectedIndex = RoleIndex();
                RefreshRoleEditors();
            }
            finally { updatingRoleControls = false; }
        }
        CompanionRoleProfile? RoleProfile(string roleId) => preferences.CustomRoles.FirstOrDefault(role => role.Id == roleId);
        string RolePrompt(string roleId, int index)
        {
            if (roleId == "assistant")
                return index switch
                {
                    0 => CompanionBrain.FixedSpeechInstructions(CompanionBrain.InputMode == "OCR" ? CompanionBrain.OcrPersona : CompanionBrain.Persona),
                    1 => CompanionBrain.WeatherPersona,
                    _ => CompanionBrain.NewsPersona
                };
            var profile = RoleProfile(roleId);
            if (profile != null) return index switch { 0 => profile.Prompt, 1 => profile.WeatherPrompt, _ => profile.NewsPrompt };
            return index switch { 0 => preferences.OwnerPrompt, 1 => preferences.OwnerWeatherPrompt, _ => preferences.OwnerNewsPrompt };
        }
        string DefaultRolePrompt(string roleId, int index)
        {
            var profile = RoleProfile(roleId);
            if (profile != null)
            {
                var defaults = CompanionRoleProfile.Create(profile.Name);
                return index switch { 0 => defaults.Prompt, 1 => defaults.WeatherPrompt, _ => defaults.NewsPrompt };
            }
            return index switch { 0 => CompanionDialogue.OwnerPromptDefault, 1 => CompanionDialogue.OwnerWeatherPromptDefault, _ => CompanionDialogue.OwnerNewsPromptDefault };
        }
        void SaveRolePrompt(string roleId, int index, string text)
        {
            if (!IsRolePromptEditable(roleId)) return;
            var profile = RoleProfile(roleId);
            if (profile != null)
            {
                if (index == 0) profile.Prompt = text;
                else if (index == 1) profile.WeatherPrompt = text;
                else profile.NewsPrompt = text;
            }
            else
            {
                if (index == 0) preferences.OwnerPrompt = text;
                else if (index == 1) preferences.OwnerWeatherPrompt = text;
                else preferences.OwnerNewsPrompt = text;
            }
            preferences.ApplySpeakerRole(brain);
            InvalidateSpeechRequests();
            SaveCompanion();
        }
        void CommitRolePrompts()
        {
            if (!IsRolePromptEditable(displayedRoleId)) return;
            bool changed = false;
            var profile = RoleProfile(displayedRoleId);
            for (int i = 0; i < rolePromptEditors.Length; i++)
            {
                string text = rolePromptEditors[i].Text;
                if (string.IsNullOrWhiteSpace(text) && i != 1) text = DefaultRolePrompt(displayedRoleId, i);
                if (text == RolePrompt(displayedRoleId, i)) continue;
                if (profile != null)
                {
                    if (i == 0) profile.Prompt = text;
                    else if (i == 1) profile.WeatherPrompt = text;
                    else profile.NewsPrompt = text;
                }
                else
                {
                    if (i == 0) preferences.OwnerPrompt = text;
                    else if (i == 1) preferences.OwnerWeatherPrompt = text;
                    else preferences.OwnerNewsPrompt = text;
                }
                changed = true;
            }
            if (changed)
            {
                preferences.ApplySpeakerRole(brain);
                InvalidateSpeechRequests();
                SaveCompanion();
            }
        }
        speakerRole.SelectionChanged += (_, _) =>
        {
            if (updatingRoleControls || speakerRole.SelectedIndex < 0) return;
            int index = speakerRole.SelectedIndex;
            if (index >= 2 && index - 2 >= preferences.CustomRoles.Count)
            {
                RefreshRoleControls();
                return;
            }
            try
            {
                CommitRolePrompts();
                preferences.SpeakerRole = index switch { 0 => "assistant", 1 => "owner", _ => preferences.CustomRoles[index - 2].Id };
                preferences.ApplySpeakerRole(brain);
                RefreshRoleEditors();
                InvalidateSpeechRequests();
                SaveCompanion();
            }
            catch (Exception error)
            {
                BigBlueFish.Diagnostics.DiagnosticLog.Write(BigBlueFish.Diagnostics.DiagnosticEvent.SettingsChanged, kind: "speaker-role", state: "failed", error: error);
                RefreshRoleControls();
                BigBlueFish.Deployment.ModernUI.Error(w, "身份设定未能应用", error);
            }
        };
        addRole.Click += (_, _) =>
        {
            try
            {
                CommitRolePrompts();
                preferences.AddCustomRole(roleName.Text);
                preferences.ApplySpeakerRole(brain);
                RefreshRoleControls();
                InvalidateSpeechRequests();
                SaveCompanion();
            }
            catch (Exception error) { BigBlueFish.Deployment.ModernUI.Error(w, "新增角色失败", error); }
        };
        removeRole.Click += (_, _) =>
        {
            var profile = ActiveRole();
            if (profile == null) return;
            CommitRolePrompts();
            preferences.CustomRoles.Remove(profile);
            preferences.SpeakerRole = "assistant";
            preferences.ApplySpeakerRole(brain);
            RefreshRoleControls();
            InvalidateSpeechRequests();
            SaveCompanion();
        };
        roleName.LostKeyboardFocus += (_, _) =>
        {
            var profile = ActiveRole();
            if (profile == null || string.IsNullOrWhiteSpace(roleName.Text)) { RefreshRoleControls(); return; }
            string name = roleName.Text.Trim();
            if (preferences.CustomRoles.Any(role => role != profile && string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                BigBlueFish.Deployment.ModernUI.Error(w, "角色名称已存在", new ArgumentException("请使用不同的角色名称。"));
                RefreshRoleControls();
                return;
            }
            if (name != profile.Name)
            {
                CommitRolePrompts();
                profile.Name = name;
                preferences.ApplySpeakerRole(brain);
                RefreshRoleControls();
                SaveCompanion();
            }
        };
        p.Children.Add(new TextBlock
        {
            Text = "可为不同角色分别设置普通说话、天气和新闻提示词；时间、屏幕与公共信息由程序单独提供，无需模板变量。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 6),
            Foreground = Brushes.SlateGray
        });
        void RolePromptEditor(int index, string suffix)
        {
            var header = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
            var reset = new Button { Content = "恢复默认", Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Right };
            rolePromptResets[index] = reset;
            reset.ToolTip = suffix;
            DockPanel.SetDock(reset, Dock.Right);
            header.Children.Add(reset);
            var heading = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            rolePromptHeaders[index] = heading;
            header.Children.Add(heading);
            p.Children.Add(header);
            var editor = new TextBox
            {
                Text = RolePrompt(displayedRoleId, index),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = index == 0 ? 210 : 120,
                MaxLength = 16000
            };
            rolePromptEditors[index] = editor;
            System.Windows.Automation.AutomationProperties.SetName(editor, heading.Text);
            p.Children.Add(editor);
            void SavePrompt(string roleId, string text)
            {
                if (!IsRolePromptEditable(roleId)) return;
                if (string.IsNullOrWhiteSpace(text) && index != 1) text = DefaultRolePrompt(roleId, index);
                editor.Text = text;
                SaveRolePrompt(roleId, index, text);
            }
            editor.LostKeyboardFocus += (_, _) =>
            {
                string roleId = displayedRoleId;
                string text = editor.Text;
                if (string.IsNullOrWhiteSpace(text) && index != 1) text = DefaultRolePrompt(roleId, index);
                if (text != RolePrompt(roleId, index)) SavePrompt(roleId, text);
            };
            reset.Click += (_, _) => SavePrompt(displayedRoleId, DefaultRolePrompt(displayedRoleId, index));
        }
        RolePromptEditor(0, "普通说话提示词");
        RolePromptEditor(1, "天气提示词");
        RolePromptEditor(2, "科技新闻提示词");
        RefreshRoleControls();
        newsEnabled = Toggle("主题灵感", preferences.NewsEnabled, v =>
        {
            preferences.NewsEnabled = v;
            if (!v) InvalidateSpeechRequests();
        });
        newsEnabled.IsEnabled = preferences.PublicInfo;
        var chatty = Toggle("随聊", preferences.Chatty, v => { preferences.Chatty = v; InvalidateSpeechRequests(); });
        p.Children.Add(new TextBlock { Text = "主题灵感来源", Margin = new Thickness(0, 10, 0, 4) });
        var newsSource = new ComboBox { ItemsSource = new[] { "科技新闻", "从文件读取", "从 API 读取" }, SelectedIndex = preferences.NewsSource switch { "file" => 1, "api" => 2, _ => 0 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        p.Children.Add(newsSource);
        var newsLocationLabel = new TextBlock { Margin = new Thickness(0, 8, 0, 4) };
        p.Children.Add(newsLocationLabel);
        string CurrentNewsLocation() => preferences.NewsSource switch { "file" => preferences.NewsFilePath, "api" => preferences.NewsApiUrl, _ => "" };
        void StoreNewsLocation(string value)
        {
            switch (preferences.NewsSource)
            {
                case "file": preferences.NewsFilePath = value; break;
                case "api": preferences.NewsApiUrl = value; break;
            }
        }
        var newsLocation = new TextBox { Text = CurrentNewsLocation(), MinWidth = 300 };
        p.Children.Add(newsLocation);
        var newsFileSettings = new StackPanel();
        newsFileSettings.Children.Add(new TextBlock { Text = "文件读取顺序", Margin = new Thickness(0, 8, 0, 4) });
        var newsFileReadOrder = new ComboBox
        {
            ItemsSource = new[] { "正序", "倒序", "随机" },
            SelectedIndex = preferences.NewsFileReadOrder switch { "reverse" => 1, "random" => 2, _ => 0 },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        newsFileSettings.Children.Add(newsFileReadOrder);
        newsFileSettings.Children.Add(new TextBlock { Text = "每次读取行数", Margin = new Thickness(0, 8, 0, 4) });
        var newsFileLinesPerRead = new ComboBox
        {
            ItemsSource = Enumerable.Range(1, 20).Select(value => value.ToString()).ToArray(),
            SelectedIndex = Math.Clamp(preferences.NewsFileLinesPerRead, 1, 20) - 1,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        newsFileSettings.Children.Add(newsFileLinesPerRead);
        p.Children.Add(newsFileSettings);
        void RefreshNewsLocation()
        {
            newsLocationLabel.Text = preferences.NewsSource switch { "file" => "主题灵感文件路径（每行一条，也支持 RSS/Atom 或 JSON）", "api" => "新闻 API 地址（RSS/Atom 或 JSON）", _ => "使用内置科技新闻" };
            newsLocation.Text = CurrentNewsLocation();
            bool customLocation = preferences.NewsSource is "file" or "api";
            newsLocation.Visibility = customLocation ? Visibility.Visible : Visibility.Collapsed;
            newsLocation.IsEnabled = preferences.PublicInfo && preferences.NewsEnabled && customLocation;
            bool fileSource = preferences.NewsSource == "file";
            newsFileSettings.Visibility = fileSource ? Visibility.Visible : Visibility.Collapsed;
            newsFileReadOrder.IsEnabled = newsFileLinesPerRead.IsEnabled = preferences.PublicInfo && preferences.NewsEnabled && fileSource;
            newsSource.IsEnabled = preferences.PublicInfo && preferences.NewsEnabled;
            chatty.IsEnabled = preferences.PublicInfo && preferences.NewsEnabled;
            if (weatherEnabled != null) weatherEnabled.IsEnabled = preferences.PublicInfo;
        }
        var publicInfoEnabled = Toggle("自动获取天气和主题灵感", preferences.PublicInfo, v =>
        {
            preferences.SetPublicInfo(v);
            InvalidateSpeechRequests();
            if (weatherEnabled != null) weatherEnabled.IsChecked = v;
            if (newsEnabled != null) newsEnabled.IsChecked = v;
            RefreshNewsLocation();
        });
        p.Children.Remove(publicInfoEnabled);
        p.Children.Insert(publicInfoIndex, publicInfoEnabled);
        RefreshNewsLocation();
        newsSource.SelectionChanged += (_, _) => { StoreNewsLocation(newsLocation.Text.Trim()); preferences.NewsSource = newsSource.SelectedIndex switch { 1 => "file", 2 => "api", _ => "builtin" }; brain.RefreshNews(); RefreshNewsLocation(); SaveCompanion(); };
        newsLocation.LostKeyboardFocus += (_, _) => { StoreNewsLocation(newsLocation.Text.Trim()); brain.RefreshNews(); SaveCompanion(); };
        newsFileReadOrder.SelectionChanged += (_, _) =>
        {
            preferences.NewsFileReadOrder = newsFileReadOrder.SelectedIndex switch { 1 => "reverse", 2 => "random", _ => "forward" };
            brain.RefreshNews();
            SaveCompanion();
        };
        newsFileLinesPerRead.SelectionChanged += (_, _) =>
        {
            if (newsFileLinesPerRead.SelectedIndex < 0) return;
            preferences.NewsFileLinesPerRead = newsFileLinesPerRead.SelectedIndex + 1;
            brain.RefreshNews();
            SaveCompanion();
        };
        publicInfoEnabled.Click += (_, _) => RefreshNewsLocation();
        newsEnabled.Click += (_, _) => RefreshNewsLocation();
        Toggle("感知当前应用", preferences.ForegroundEnabled, v =>
        {
            preferences.ForegroundEnabled = v;
            InvalidateSpeechRequests();
        });
#if !COMPANION_SAFE
        p.Children.Add(new TextBlock { Text = "本地吐字在本机读取屏幕；由 Agent 编写日常关心时，会向它提供前台截图。", FontSize = 11,
            Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
#endif
        p.Children.Add(new TextBlock { Text = "天气地点与自动更新", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 4) });
        AddCompanionRegionSelectors(p);
        weatherEnabled = Toggle("自动获取天气", preferences.WeatherEnabled, v =>
        {
            preferences.WeatherEnabled = v;
            if (!v) InvalidateSpeechRequests();
        });
        weatherEnabled.IsEnabled = preferences.PublicInfo;
        p = pages[0];
        var label = new TextBlock
        {
            Text = $"桌宠大小（%）：{preferences.Scale:P0}",
            Margin = new Thickness(0, 10, 0, 4)
        };
        p.Children.Add(label);
        var scale = new Slider
        {
            Minimum = .5,
            Maximum = 2.0,
            Value = preferences.Scale,
            TickFrequency = .05,
            IsSnapToTickEnabled = true
        };
        scale.ValueChanged += (_, _) =>
        {
            preferences.Scale = scale.Value;
            label.Text = $"桌宠大小（%）：{scale.Value:P0}";
            ApplyCompanionScale();
        };
        p.Children.Add(scale);
        p = pages[1];
        var bubbleScaleLabel = new TextBlock
        {
            Text = $"文字气泡大小：{preferences.BubbleScale:P0}",
            Margin = new Thickness(0, 18, 0, 4)
        };
        p.Children.Add(bubbleScaleLabel);
        var bubbleScale = new Slider
        {
            Minimum = .5, Maximum = 2.0, Value = preferences.BubbleScale,
            TickFrequency = .05, IsSnapToTickEnabled = true
        };
        System.Windows.Automation.AutomationProperties.SetName(bubbleScale, "文字气泡大小");
        bubbleScale.ValueChanged += (_, _) =>
        {
            preferences.BubbleScale = bubbleScale.Value;
            bubbleScaleLabel.Text = $"文字气泡大小：{bubbleScale.Value:P0}";
            ApplyCompanionBubbleScale();
        };
        p.Children.Add(bubbleScale);
        p = pages[0];
        var intervalLabel = new TextBlock { Text = $"动作触发间隔：{preferences.AnimationIntervalSeconds} 秒", Margin = new Thickness(0, 18, 0, 4) };
        p.Children.Add(intervalLabel);
        var interval = new Slider { Minimum = 30, Maximum = 300, Value = preferences.AnimationIntervalSeconds, TickFrequency = 10, IsSnapToTickEnabled = true };
        System.Windows.Automation.AutomationProperties.SetName(interval, "动作触发间隔");
        interval.ValueChanged += (_, _) =>
        {
            preferences.AnimationIntervalSeconds = (int)Math.Round(interval.Value);
            intervalLabel.Text = $"动作触发间隔：{preferences.AnimationIntervalSeconds} 秒";
            nextAction = DateTime.UtcNow.AddSeconds(preferences.AnimationIntervalSeconds);
            SaveCompanion();
        };
        p.Children.Add(interval);
        var meals = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 12) };
        void MealTime(string labelText, int initialHour, Action<int> change)
        {
            var column = new StackPanel { Margin = new Thickness(0, 0, 14, 0), Width = 110 };
            column.Children.Add(new TextBlock { Text = labelText, Margin = new Thickness(0, 0, 0, 6) });
            var selector = new ComboBox();
            for (int hour = 0; hour < 24; hour++) selector.Items.Add($"{hour:00}:00");
            selector.SelectedIndex = initialHour;
            selector.SelectionChanged += (_, _) => { if (selector.SelectedIndex >= 0) { change(selector.SelectedIndex); SaveCompanion(); } };
            column.Children.Add(selector); meals.Children.Add(column);
        }
        MealTime("早餐时间", preferences.BreakfastHour, h => preferences.BreakfastHour = h);
        MealTime("午餐时间", preferences.LunchHour, h => preferences.LunchHour = h);
        MealTime("晚餐时间", preferences.DinnerHour, h => preferences.DinnerHour = h);
        p.Children.Add(meals);
        p = pages[1];
        var freq = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 8)
        };
        foreach (var speechInterval in CompanionPreferences.SpeechIntervals)
            freq.Items.Add("说话间隔：" + speechInterval.Label);
        freq.SelectedIndex = preferences.SpeechIntervalIndex;
        freq.SelectionChanged += (_, _) =>
        {
            if (freq.SelectedIndex < 0) return;
            preferences.SetSpeechInterval(CompanionPreferences.SpeechIntervals[freq.SelectedIndex].Id);
            nextSpeech = DateTime.UtcNow.Add(preferences.NextSpeechDelay(companionRandom));
            SaveCompanion();
        };
        p.Children.Add(freq);
        var frequencyHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 8) };
        p.Children.Add(frequencyHint);
        var dwellLabel = new TextBlock
        {
            Text = "气泡驻留时间：" + CompanionPreferences.BubbleDurationLabels[preferences.BubbleDurationIndex],
            Margin = new Thickness(0, 8, 0, 4)
        };
        p.Children.Add(dwellLabel);
        var dwell = new Slider
        {
            Minimum = 0,
            Maximum = 7,
            Value = preferences.BubbleDurationIndex,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            SmallChange = 1,
            LargeChange = 1,
            TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
            ToolTip = dwellLabel.Text
        };
        System.Windows.Automation.AutomationProperties.SetName(dwell, "气泡驻留时间");
        dwell.ValueChanged += (_, _) =>
        {
            preferences.BubbleDurationIndex = (int)Math.Round(dwell.Value);
            dwellLabel.Text = "气泡驻留时间：" + CompanionPreferences.BubbleDurationLabels[preferences.BubbleDurationIndex];
            dwell.ToolTip = dwellLabel.Text;
            if (bubble.Visibility == Visibility.Visible)
                ResetBubbleDeadline();
            SaveCompanion();
        };
        p.Children.Add(dwell);
        p = pages[2];
        installModel = new Button { Content = "安装模型", Margin = new Thickness(0, 8, 0, 8) };
        installModel.Click += (_, _) => OpenModelInstaller();
        p.Children.Add(installModel);
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.SlateGray,
            Margin = new Thickness(0, 8, 0, 8)
        };
        p.Children.Add(status);
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        void UpdateStatus()
        {
            freq.IsEnabled = !PuppetActive;
            frequencyHint.Text = PuppetActive ? "日常关心的触发时机已交给 Agent，此时说话间隔不可调节。关闭接管触发后恢复使用此设置。"
                : AgentDailyActive ? "鱼鱼仍按此间隔触发日常关心，由 Agent 编写吐字。" : "鱼鱼按此间隔触发日常关心，由本地模型编写吐字。";
            bool available = brain.ModelAvailable;
            installModel.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            status.Text = (CompanionNative.OnAC ? "已插电" :
                preferences.StopModelOnBattery ? "电池供电 · 已按设置暂停模型" : "电池供电 · 允许模型运行")
                + "\n" + (AgentDailyActive ? "日常吐字由 Agent 编写，本地模型不自动加载。" : PuppetActive ? "Agent 决定日常关心的时机，本地模型负责吐字。\n" : "")
                + (available ? brain.Status : "尚未安装模型")
                + "\n推理设备：" + brain.ExecutionDevice;
        }
        timer.Tick += (_, _) => UpdateStatus();
        UpdateStatus();
        timer.Start();
        void Button(string text, Action action)
        {
            var b = new Button
            {
                Content = text,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 4, 0, 4)
            };
            b.Click += (_, _) => action();
            p.Children.Add(b);
        }

        p = pages[0];
        Button("检查更新", async () => await CheckForUpdates(true));
        var updates = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
        p.Children.Add(updates);
        timer.Tick += (_, _) => updates.Text = updateStatus;
        Button("隐藏到任务栏通知区", HideCompanion);
        Button("回到右下角", () =>
        {
            var a = SystemParameters.WorkArea;
            Left = a.Right - Width - 8;
            Top = a.Bottom - Height + 12;
            ClampCompanion();
            anchor = new Point(Left, Top);
            SaveCompanion();
        });
        Button("关闭桌宠", () =>
        {
            w.Close();
            base.Close();
        });
        Action? disposeMcpSettings = titles.Contains("连接 Agent") ? AddMcpSettings(pages[3], w) : null;
        Action disposeLog = BuildLogConsole(pages[^1], w, () => ReferenceEquals(pageHost.Content, views[^1]));
        foreach (var panel in pages) Card(panel);
        disposeCompanionSettingsContent = () => { timer.Stop(); disposeMcpSettings?.Invoke(); disposeLog(); };
        refreshCompanionSettings = () => BuildCompanionSettingsContent(w, selectedPage, views.Select(v => v.VerticalOffset).ToArray());
        if (scrollOffsets != null) w.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
        {
            if (companionSettings != w || w.Content != shell) return;
            for (int i = 0; i < views.Length; i++) views[i].ScrollToVerticalOffset(scrollOffsets[i]);
        }));
    }

    private static void CloseDropDowns(DependencyObject root)
    {
        if (root is not Visual) return;
        if (root is ComboBox combo) combo.IsDropDownOpen = false;
        for (int i = VisualTreeHelper.GetChildrenCount(root) - 1; i >= 0; i--)
            CloseDropDowns(VisualTreeHelper.GetChild(root, i));
    }

    private void ActivateCompanionSettings(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Topmost = false;
        window.Show();
        window.Activate();
        // Pet/tray menus finish restoring their previous foreground window after
        // the click handler. Activate once that input/menu work has completed.
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, (Action)(() =>
        {
            if (companionSettings == window && window.IsVisible && !companionClosed && !exitAnimation)
            {
                // This is only reached by an explicit user request. Keep the
                // ordinary window band even if a native topmost flag was left over.
                window.Topmost = false;
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                CompanionNative.SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0013);
                window.Activate();
            }
        }));
    }

}
