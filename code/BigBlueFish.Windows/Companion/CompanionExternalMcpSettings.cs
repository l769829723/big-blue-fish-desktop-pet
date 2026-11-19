using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BigBlueFish.Control;
using BigBlueFish.Deployment;

namespace BigBlueFish.Windows;

public partial class MainWindow
{
    // The restricted external interface exists in both editions, so its card is shared by the
    // standard "Agent 与 MCP" page and the Safe edition's MCP page.
    private Action AddExternalMcpSettings(StackPanel panel, Window owner, bool heading)
    {
        if (heading) panel.Children.Add(new TextBlock { Text = "外部 MCP 接入", FontWeight = FontWeights.SemiBold, Margin = new(0, 20, 0, 6) });
        var external = new CheckBox { Content = ControlTools.ExternalMcpLabel, IsChecked = preferences.ExternalMcpEnabled, Margin = new(0, 0, 0, 5) };
        panel.Children.Add(external);
        panel.Children.Add(ModernUI.Text(ControlTools.ExternalMcpDescription));
        var externalDetails = new StackPanel { Visibility = preferences.ExternalMcpEnabled ? Visibility.Visible : Visibility.Collapsed, Margin = new(0, 8, 0, 0) };
        panel.Children.Add(externalDetails);
        externalDetails.Children.Add(new TextBlock { Text = "把下面这段配置加入支持 MCP 的客户端，然后重启该客户端：", Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap });
        var configuration = new TextBox { Text = ExternalMcpConfiguration(), IsReadOnly = true, IsReadOnlyCaretVisible = false,
            TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 12, MinHeight = 84,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderBrush = new SolidColorBrush(Color.FromRgb(226, 233, 243)),
            Padding = new Thickness(8), Margin = new(0, 6, 0, 6) };
        System.Windows.Automation.AutomationProperties.SetName(configuration, "外部 MCP 客户端配置");
        externalDetails.Children.Add(configuration);
        var externalRow = new WrapPanel();
        var copyStatus = new TextBlock { Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) };
        var externalStatus = new TextBlock { Foreground = Brushes.SlateGray, Margin = new(0, 4, 0, 0) };
        externalRow.Children.Add(ModernUI.Button("复制配置", () => {
            try { Clipboard.SetText(configuration.Text); copyStatus.Text = "已复制"; }
            catch (Exception e) { copyStatus.Text = "复制失败，请手动选择文本"; DiagnosticLog.Write(DiagnosticEvent.SettingsChanged, error: e); }
        }));
        externalRow.Children.Add(copyStatus);
        externalDetails.Children.Add(externalRow);
        externalDetails.Children.Add(externalStatus);
        panel.Children.Add(ModernUI.Text("接口只在本机通过命名管道提供，不开放网络端口；关闭开关或退出桌宠后连接立即失效。"));
        external.Click += async (_, _) => {
            external.IsEnabled = false;
            try
            {
                bool on = external.IsChecked == true;
                if (on && !await ConfirmMcp(owner, ControlTools.ExternalMcpTitle, ControlTools.ExternalMcpWarning)) { external.IsChecked = false; return; }
                preferences.ExternalMcpEnabled = on;
                if (on) InitializeMcp(); else StopExternalMcp();
                externalDetails.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                SaveCompanion(); RefreshExternal();
            }
            catch (Exception e) { ModernUI.Error(owner, "外部 MCP 设置失败", e); }
            finally { external.IsEnabled = true; }
        };
        void RefreshExternal() => externalStatus.Text = preferences.ExternalMcpEnabled ? $"已连接 {externalMcpServer?.Connections ?? 0} 个外部客户端" : "";
        RefreshExternal();
        return RefreshExternal;
    }

    // Safe edition: the MCP page contains only the external interface.
    private Action AddExternalMcpPage(StackPanel panel, Window owner)
    {
        var refresh = AddExternalMcpSettings(panel, owner, false);
        var clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        clock.Tick += (_, _) => refresh();
        clock.Start();
        return clock.Stop;
    }

    private static string ExternalMcpConfiguration() => JsonSerializer.Serialize(new
    {
        mcpServers = new Dictionary<string, object>
        {
            ["big-blue-fish"] = new Dictionary<string, object>
            {
                ["command"] = Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp.exe")
            }
        }
    }, new JsonSerializerOptions { WriteIndented = true });
}
