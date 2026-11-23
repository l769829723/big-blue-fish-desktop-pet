using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    DateTime nextTestStatus;
    async Task TestCommands()
    {
        if (!App.Args.Contains("--test-mode")) return;
        if (DateTime.UtcNow >= nextTestStatus) {
            nextTestStatus = DateTime.UtcNow.AddSeconds(1);
            File.WriteAllText("test-status.json", JsonSerializer.Serialize(new { version = typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3), currentAction, phase, frames = player.Frames, animationSeconds=player.PositionSeconds, animationBackend=player.Backend, player.LastReadMs, player.LastUiMs, player.LastCopyMs, player.LastShapeMs, text = bubbleText.Text, bubbleVisible = bubbleFrame.IsVisible, bubbleInPetWindow = ReferenceEquals(bubbleFrame.Parent, petCanvas), loadingInPetWindow = ReferenceEquals(loading.Parent, petCanvas), topmost = Topmost, bubbleReservedHeight, Puppet, McpAvailable, visible = IsVisible, hidden, fullscreenHidden, modelPid = brain.RunnerPid, modelBusy = brain.Busy, modelStatus = brain.Status, generating, typing, loadingVisible = loading.IsVisible,
                nextSpeechSeconds = nextSpeech == DateTime.MinValue ? -1 : Math.Round((nextSpeech - DateTime.UtcNow).TotalSeconds, 1), settingsOpen = settings != null, contextMenuOpen = ContextMenu?.IsOpen == true, Position.X, Position.Y, mcpPipe = TestPipe,
                roamingEnabled = Preferences.RoamingEnabled, roamState = roam?.State.ToString(), roamTrip = roam?.Trip.ToString(),
                roamTarget = roam?.Target?.Title, roamTargetTop = roam?.Target?.Bounds.Top, roamBodyBottom = roam?.Body.Bottom,
                roamBodyCenterX = roam?.Body.CenterX, roamFeetX = roam?.Feet.X, roamFeetY = roam?.Feet.Y,
                roamWork = WorkArea(), roamScale = DesktopScaling }));
        }
        if (!File.Exists("test-command.txt")) return;
        string command = File.ReadAllText("test-command.txt").Trim(); File.Delete("test-command.txt");
        try {
            switch (command)
            {
                case "settings": OpenSettings(); break;
                case "settings-regression": await VerifySettingsRegression(); break;
                case "close-settings": settings?.Close(); break;
                case "speech-test": await Speak(false); break;
                case "speech-pacing": await VerifySpeechPacing(); break;
                case "weather": await Speak(true); break;
                case "hide": hidden = true; HideBubble(); Hide(); break;
                case "show": RestorePet(); break;
                case "close": Close(); break;
                case "exit": CloseImmediately(); break;
                case "ocr":
                    var ocr = await CompanionOcr.ReadAsync(CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground), CancellationToken.None);
                    File.WriteAllText("ocr-status.json", JsonSerializer.Serialize(ocr)); break;
                case "render": RenderControl(this, "pet.png"); break;
                case "render-settings": if (settings != null) RenderControl(settings, "settings.png"); break;
#if !COMPANION_SAFE
                case "render-input": if (mcpInput != null) RenderControl(mcpInput, "input.png"); break;
#endif
                case "roam-perch": ForceRoam(0, false); break;
                case var perch when perch.StartsWith("roam-perch:"): ForceRoam(ParseRoamHandle(perch[11..]), false); break;
                case "roam-push": ForceRoam(0, true); break;
                case var push when push.StartsWith("roam-push:"): ForceRoam(ParseRoamHandle(push[10..]), true); break;
                case "roam-cancel": CancelRoam(); break;
                default: if (command.StartsWith("bubble ")) ShowText(command[7..], true); break;
            }
        } catch (Exception error) { File.WriteAllText("test-error.txt", error.ToString()); }
    }
    async Task VerifySpeechPacing()
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        const string text = "主人，鱼鱼正在把这句很长的话一个字一个字地显示出来，确认上一句没有说完之前，不会再去请求新的文本，也不会重新转圈加载。";
        DateTime shownAt = DateTime.UtcNow;
        ShowText(text, true);
        // The line itself must reserve the next automatic request before its first character is typed.
        Check(nextSpeech > shownAt, "an external line schedules the next request when it appears");
        Check(typing, "the long external line types progressively");
        // Reproduce a slow model: the interval was already due before the reply became visible.
        nextSpeech = DateTime.UtcNow.AddMinutes(-1);
        bool requestedWhileTyping = false;
        while (typing && DateTime.UtcNow - shownAt < TimeSpan.FromSeconds(20))
        {
            if (generating || brain.Busy || loading.IsVisible) requestedWhileTyping = true;
            await Task.Delay(50);
        }
        Check(!typing, "the progressive line finishes inside the test window");
        Check(bubbleText.Text == text, "the whole line is visible after typing");
        Check(!requestedWhileTyping, "no generation starts while the line is still typing");
        Check(nextSpeech > DateTime.UtcNow.AddSeconds(60), "the next request waits until after the line is fully shown");
        File.WriteAllText("speech-pacing.json", JsonSerializer.Serialize(new { checks, text, seconds = Math.Round((DateTime.UtcNow - shownAt).TotalSeconds, 2) }));
    }
    async Task VerifySettingsRegression()
    {
        Preferences.SpeakerRole = "assistant";
        Preferences.CustomRoles.RemoveAll(role => role.Name.StartsWith("回归测试角色", StringComparison.Ordinal));
        Preferences.AddCustomRole("回归测试角色甲");
        Preferences.AddCustomRole("回归测试角色乙");
        Preferences.SpeakerRole = "assistant";
        OpenSettings();
        await Task.Delay(150);
        var settingsWindow = settings ?? throw new InvalidOperationException("Settings window did not open.");
        var navigation = settingsWindow.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "模型与信息");
        navigation.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(100);
        var controls = settingsWindow.GetVisualDescendants().OfType<Avalonia.Controls.Control>().ToArray();
        var roleSelect = controls.OfType<ComboBox>().Single(combo =>
            combo.ItemsSource is string[] items && items.Length == Preferences.CustomRoles.Count + 2 && items[0] == "助手（默认）");
        var editors = controls.OfType<TextBox>().Where(editor => editor.MaxLength == 16000).ToArray();
        if (editors.Length != 3) throw new InvalidOperationException($"Expected three role prompt editors, found {editors.Length}.");
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        void CheckEditorRole(string roleId)
        {
            for (int i = 0; i < editors.Length; i++)
                Check(editors[i].Text == (Preferences.CustomRoles.FirstOrDefault(role => role.Id == roleId) is { } profile
                    ? i switch { 0 => profile.Prompt, 1 => profile.WeatherPrompt, _ => profile.NewsPrompt }
                    : roleId == "assistant"
                        ? i switch {
                            0 => CompanionBrain.FixedSpeechInstructions(CompanionBrain.InputMode == "OCR" ? CompanionBrain.OcrPersona : CompanionBrain.Persona),
                            1 => CompanionBrain.WeatherPersona,
                            _ => CompanionBrain.NewsPersona
                        }
                        : i switch { 0 => Preferences.OwnerPrompt, 1 => Preferences.OwnerWeatherPrompt, _ => Preferences.OwnerNewsPrompt }),
                    $"prompt editor {i} follows selected role {roleId}");
        }
        roleSelect.SelectedIndex = 0;
        CheckEditorRole("assistant");
        Check(editors.All(editor => !editor.IsEnabled), "built-in assistant prompts are shown read-only");
        roleSelect.SelectedIndex = 1;
        CheckEditorRole("owner");
        Check(editors.All(editor => editor.IsEnabled), "owner prompts remain editable");
        var firstRole = Preferences.CustomRoles[^2];
        var secondRole = Preferences.CustomRoles[^1];
        int firstRoleIndex = Preferences.CustomRoles.Count;
        int secondRoleIndex = firstRoleIndex + 1;
        editors[0].Text = "回归测试：主人普通提示词";
        roleSelect.SelectedIndex = firstRoleIndex;
        Check(Preferences.OwnerPrompt == "回归测试：主人普通提示词", "switch commits edited prompt to the owner identity");
        Check(firstRole.Prompt == editors[0].Text, "switch refreshes prompt editor to the selected custom role");
        editors[1].Focus();
        editors[1].Text = "回归测试：角色甲天气提示词";
        roleSelect.SelectedIndex = secondRoleIndex;
        Check(firstRole.WeatherPrompt == "回归测试：角色甲天气提示词", "rapid selection preserves weather prompt on its source role");
        Check(editors[1].Text == secondRole.WeatherPrompt, "weather prompt editor refreshes to the newly selected role");
        editors[2].Text = "回归测试：角色乙新闻提示词";
        roleSelect.SelectedIndex = firstRoleIndex;
        Check(secondRole.NewsPrompt == "回归测试：角色乙新闻提示词", "selection commits prompts before changing the displayed role");
        foreach (int index in new[] { 0, 1, firstRoleIndex, secondRoleIndex, 0, secondRoleIndex, firstRoleIndex, 1, 0 })
        {
            roleSelect.SelectedIndex = index;
            string selectedId = index switch { 0 => "assistant", 1 => "owner", _ => Preferences.CustomRoles[index - 2].Id };
            CheckEditorRole(selectedId);
        }
        foreach (var toggle in controls.OfType<CheckBox>().Where(box => box.IsEnabled).Take(8))
        {
            bool original = toggle.IsChecked == true;
            toggle.IsChecked = !original;
            toggle.IsChecked = original;
            Check(true, "speech-setting checkbox remains interactive");
        }
        foreach (var combo in controls.OfType<ComboBox>().Where(combo => combo != roleSelect && combo.IsEnabled && combo.Items.Count > 1).Take(8))
        {
            int original = combo.SelectedIndex;
            combo.SelectedIndex = (original + 1 + combo.Items.Count) % combo.Items.Count;
            combo.SelectedIndex = original;
            Check(true, "speech-setting selector remains interactive");
        }
        foreach (var slider in controls.OfType<Slider>().Where(slider => slider.IsEnabled).Take(4))
        {
            double original = slider.Value;
            slider.Value = Math.Clamp(original + slider.TickFrequency, slider.Minimum, slider.Maximum);
            slider.Value = original;
            Check(true, "speech-setting slider remains interactive");
        }
        File.WriteAllText("test-regression.json", JsonSerializer.Serialize(new { ok = true, checks, firstRole = firstRole.Name, secondRole = secondRole.Name }));
    }
    static long ParseRoamHandle(string value)
    {
        // xdotool prints decimal window ids; accept hex too so manual runs are easy.
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(value[2..], System.Globalization.NumberStyles.HexNumber)
            : long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }
    void ForceRoam(long handle, bool push)
    {
        if (roam == null) throw new InvalidOperationException("Roaming engine missing.");
        roamSurfaces = CompanionNative.Surfaces(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        roam.Sync(Body());
        nint? only = handle == 0 ? null : (nint)handle;
        bool started = push
            ? roam.TryStartPush(DateTime.UtcNow, WorkArea(), roamSurfaces, only)
            : roam.TryStartPerch(DateTime.UtcNow, WorkArea(), roamSurfaces, only);
        if (!started) File.WriteAllText("test-error.txt", "Roam target rejected: " + handle);
    }
    static void RenderControl(Avalonia.Controls.Control control, string path)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)control.Bounds.Width, (int)control.Bounds.Height)); image.Render(control); image.Save(path, PngBitmapEncoderOptions.Default);
    }
}
