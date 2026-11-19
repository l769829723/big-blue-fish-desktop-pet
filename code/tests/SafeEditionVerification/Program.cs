using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BigBlueFish.Control;
using BigBlueFish.Windows;

static class Program
{
    const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i); if(child is T value) yield return value; foreach(var nested in Descendants<T>(child)) yield return nested; } }
    static void Pump(int ms) { var frame=new DispatcherFrame(); var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(ms)}; timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame); }
    [STAThread] static int Main(string[] args)
    {
        MainWindow? pet=null;
        try {
            string path=Path.GetFullPath(args[0]);Directory.CreateDirectory(path);
            File.Copy(Path.Combine(AppContext.BaseDirectory,"BigBlueFish.ico"),Path.Combine(path,"BigBlueFish.ico"),true);
            File.WriteAllText(Path.Combine(path,"preferences.json"),"{\"ModelEnabled\":false,\"PublicInfo\":false,\"CheckUpdatesOnStartup\":false,\"McpEnabled\":true,\"ExternalMcpEnabled\":true,\"PuppetMode\":true}");
            App.Args=["--test-mode"];var app=new System.Windows.Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Environment.CurrentDirectory=path;
            pet=new MainWindow();pet.Show();Pump(500);
            object Read(string name)=>pet.GetType().GetField(name,Access)!.GetValue(pet)!;
            object? Invoke(string name,params object[] values)=>pet.GetType().GetMethod(name,Access)!.Invoke(pet,values);
            var assembly=typeof(MainWindow).Assembly;
            Check(assembly.GetReferencedAssemblies().Any(a=>a.Name=="BigBlueFish.Control"),"safe executable references the shared control layer");
            Check(!assembly.GetReferencedAssemblies().Any(a=>a.Name=="BigBlueFish.Agent"),"safe executable has no Agent assembly reference");
            Check(pet.GetType().GetMethod("ExecuteMcp",Access)==null,"safe executable contains no Agent MCP executor");
            Check(pet.GetType().GetMethod("ExecuteExternalMcp",Access)!=null,"safe executable serves the restricted external MCP surface");
            var prefs=Read("preferences");
            Check(!(bool)prefs.GetType().GetProperty("McpEnabled")!.GetValue(prefs)! && !(bool)prefs.GetType().GetProperty("PuppetMode")!.GetValue(prefs)!,"old enabled flags cannot start MCP or puppet in Safe");
            Check((bool)prefs.GetType().GetProperty("ExternalMcpEnabled")!.GetValue(prefs)!,"safe edition keeps the stored external MCP switch");
            var external=Read("externalMcpServer");
            Check(external!=null,"safe edition starts the external control server when enabled");
            Check((bool)external.GetType().GetProperty("Restricted")!.GetValue(external)!,"external server runs in restricted mode");
            Check(ControlTools.ExternalTools.SequenceEqual(new[]{"list_actions","play_action","interrupt_action","say","say_next"}),"external surface exposes only the speech and action tools");
            var json=JsonDocument.Parse("{}").RootElement.Clone();
            string Text(object result)
            {
                var content=(Array)result.GetType().GetProperty("content")!.GetValue(result)!;
                return (string)content.GetValue(0)!.GetType().GetProperty("text")!.GetValue(content.GetValue(0))!;
            }
            var actions=JsonDocument.Parse(Text(Invoke("ExecuteSpeechOrAction","list_actions",json)!)).RootElement;
            Check(actions.GetArrayLength()>0,"external list_actions returns playable actions");
            Invoke("ExecuteSpeechOrAction","say_next",JsonDocument.Parse("{\"text\":\"外部排队消息\"}").RootElement.Clone());
            Check((bool)pet.GetType().GetProperty("HasScheduledMcpSpeech",Access)!.GetValue(pet)!,"external say_next queues speech");
            Check((bool)Invoke("ConsumeMcpSpeech")!,"queued external speech reaches the bubble");
            try { Invoke("ExecuteSpeechOrAction","get_settings",json); Check(false,"external surface rejects tools outside the speech and action subset"); }
            catch(TargetInvocationException e) when(e.InnerException is ArgumentException) { Check(true,"external surface rejects tools outside the speech and action subset"); }
            Check((string)Read("animationPhase")=="Startup","safe keeps ordinary welcome animation");
            Invoke("OpenCompanionSettings");Pump(150);var settings=(Window)Read("companionSettings");
            Check(!Descendants<Button>(settings).Any(b=>b.Content?.ToString() is "Agent 与 MCP" or "连接 Agent"),"safe settings have no Agent page");
            Check(Descendants<Button>(settings).Any(b=>b.Content?.ToString()=="MCP 接入"),"safe settings expose the MCP access page");
            Descendants<Button>(settings).Single(b=>b.Content?.ToString()=="日志控制台").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(350);
            Check(Descendants<TextBox>(settings).Any(t=>t.Name=="LogConsoleText"&&t.IsReadOnly&&t.Text.Contains("SettingsOpened")),"safe edition has a working shareable log console without loading Agent");
            settings.Close();Invoke("AnimationBoundary");
            Check((string)Read("animationPhase")=="Idle","safe keeps ordinary idle logic");
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="BigBlueFish.Agent"),"no Agent assembly loaded in actual process");
            pet.GetType().GetField("exitReady",Access)!.SetValue(pet,true);pet.Close();
            Console.WriteLine($"Safe actual WPF integration: {checks} checks passed.");return 0;
        } catch(Exception error) { Console.Error.WriteLine(error);if(pet!=null){pet.GetType().GetField("exitReady",Access)!.SetValue(pet,true);pet.Close();}return 1; }
    }
}
