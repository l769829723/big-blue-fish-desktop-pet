using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace BigBlueFish.Windows;

public partial class App : Application
{
    public static string[] Args { get; set; } = [];

    public App()
    {
#if !DEBUG
        DispatcherUnhandledException += (_, e) => { e.Handled = true; RecordException(e.Exception, false); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => RecordException(e.ExceptionObject as Exception ?? new Exception("非 Exception 类型的未处理异常"), true);
        TaskScheduler.UnobservedTaskException += (_, e) => { RecordException(e.Exception, false); e.SetObserved(); };
#endif
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            Environment.CurrentDirectory = BigBlueFish.Deployment.ApplicationData.Prepare(AppContext.BaseDirectory);
            DiagnosticLog.ConfigureDirectories(AppContext.BaseDirectory, Environment.CurrentDirectory);
        }
        catch (Exception error)
        {
            MessageBox.Show("无法准备大肥鱼的数据目录：" + error.Message, "蓝色大肥鱼", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1); return;
        }
        Args = e.Args;
        base.OnStartup(e);
    }

    private void RecordException(Exception exception, bool fatal)
    {
        try { DiagnosticLog.Write(DiagnosticEvent.AppFailed, state: fatal ? "exit" : "failed", error: exception); }
        catch { }
        DiagnosticLog.SaveCrashReport(exception, fatal);
        if (fatal) Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLog.CloseSession();
        base.OnExit(e);
    }
}
