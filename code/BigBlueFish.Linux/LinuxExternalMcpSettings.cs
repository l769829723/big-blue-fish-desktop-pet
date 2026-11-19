using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BigBlueFish.Control;

namespace BigBlueFish.Windows;

internal sealed partial class LinuxPetWindow
{
    // The restricted external interface exists in both editions, so its card is shared by the
    // standard "Agent 与 MCP" page and the Safe edition's MCP page.
    Action BuildExternalMcpSettings(StackPanel body, bool heading)
    {
        if (heading) body.Children.Add(new TextBlock { Text = "外部 MCP 接入", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 20, 0, 6) });
        var external = new CheckBox { Content = ControlTools.ExternalMcpLabel, IsChecked = Preferences.ExternalMcpEnabled };
        body.Children.Add(external); body.Children.Add(Paragraph(ControlTools.ExternalMcpDescription));
        var externalDetails = new StackPanel { Spacing = 6, IsVisible = Preferences.ExternalMcpEnabled };
        body.Children.Add(externalDetails);
        externalDetails.Children.Add(Paragraph("把下面这段配置加入支持 MCP 的客户端，然后重启该客户端（选中文字即可复制）："));
        var configuration = new TextBox { Text = ExternalMcpConfiguration(), IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 84 };
        Avalonia.Automation.AutomationProperties.SetName(configuration, "外部 MCP 客户端配置");
        externalDetails.Children.Add(configuration);
        var externalStatus = Paragraph("");
        externalDetails.Children.Add(externalStatus);
        body.Children.Add(Paragraph("接口只在本机通过命名管道提供，不开放网络端口；关闭开关或退出桌宠后连接立即失效。"));
        external.Click += async (_, _) => {
            external.IsEnabled = false;
            try
            {
                bool on = external.IsChecked == true;
                if (on && !await Confirm(ControlTools.ExternalMcpTitle, ControlTools.ExternalMcpWarning, "了解并开启")) { external.IsChecked = false; return; }
                Preferences.ExternalMcpEnabled = on;
                if (on) InitializeMcp(); else StopExternalMcp();
                externalDetails.IsVisible = on; Save(); RefreshExternal();
            }
            finally { external.IsEnabled = true; }
        };
        void RefreshExternal() => externalStatus.Text = Preferences.ExternalMcpEnabled ? $"已连接 {externalMcpServer?.Connections ?? 0} 个外部客户端" : "";
        RefreshExternal();
        return RefreshExternal;
    }

    static string ExternalMcpConfiguration() => JsonSerializer.Serialize(new
    {
        mcpServers = new Dictionary<string, object>
        {
            ["big-blue-fish"] = new Dictionary<string, object> { ["command"] = Path.Combine(AppContext.BaseDirectory, "BigBlueFish.Mcp") }
        }
    }, new JsonSerializerOptions { WriteIndented = true });
}
