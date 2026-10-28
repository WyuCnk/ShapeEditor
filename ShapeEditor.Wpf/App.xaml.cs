using System.Windows;
using System.Windows.Threading;

namespace ShapeEditor.Wpf;

public partial class App : Application
{
    protected override void OnStartup(
        StartupEventArgs e)
    {
        StartupLog.Write("OnStartup args=" + string.Join(" | ", e.Args));

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            StartupLog.WriteException("AppDomain", args.ExceptionObject as Exception ?? new Exception("unknown"));

        base.OnStartup(e);

        /*
         * 自检模式：不打开窗口，把编辑器与批量视图离屏渲染成 PNG。
         *
         * 开发机没有可视化桌面时，用它确认渲染管线是通的：
         *   ShapeEditor.Wpf.exe --render-test <输出目录>
         */
        if (e.Args.Length >= 2 &&
            string.Equals(
                e.Args[0],
                "--render-test",
                StringComparison.OrdinalIgnoreCase))
        {
            StartupLog.Write("render-test mode, output=" + e.Args[1]);

            int exitCode =
                SelfTest.RenderRunner.Run(e.Args[1]);

            StartupLog.Write("render-test finished, exit=" + exitCode);

            Shutdown(exitCode);

            return;
        }

        StartupLog.Write("normal mode, creating MainWindow");

        var window = new MainWindow();

        MainWindow = window;

        window.Show();

        StartupLog.Write("MainWindow shown");
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        StartupLog.WriteException("Dispatcher", e.Exception);
    }
}
