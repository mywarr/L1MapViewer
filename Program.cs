using System.Text;
using L1FlyMapViewer;
using L1MapViewer;
using L1MapViewer.CLI;
using System.Text;

using System.Diagnostics;

namespace L1MapViewerCore;

static class Program
{
    // 效能 Log 開關（供 MapForm 讀取）
    public static bool PerfLogEnabled { get; private set; } = false;

    // 全域啟動計時器
    public static Stopwatch StartupStopwatch { get; } = Stopwatch.StartNew();

    [STAThread]
    static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 檢查是否啟用效能 Log
        var argsList = args.ToList();
        if (argsList.Contains("--perf-log"))
        {
            PerfLogEnabled = true;
            argsList.Remove("--perf-log");
            args = argsList.ToArray();
            LogPerf("[PROGRAM] PerfLog enabled");
        }

        // 檢查是否為 CLI 模式
        if (args.Length > 0 && args[0].ToLower() == "-cli")
        {
            return CliHandler.Execute(args);
        }

        // GUI 模式
        LogPerf("[PROGRAM] Starting GUI mode");
        ApplicationConfiguration.Initialize();
        LogPerf("[PROGRAM] ApplicationConfiguration.Initialize() done");

        LogPerf("[PROGRAM] Creating MapForm...");
        var form = new MapForm();
        LogPerf("[PROGRAM] MapForm created");

        LogPerf("[PROGRAM] Application.Run() starting...");
        Application.Run(form);
        return 0;
    }

    public static void LogPerf(string message)
    {
        if (!PerfLogEnabled) return;
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string elapsed = $"+{StartupStopwatch.ElapsedMilliseconds}ms";
        Console.WriteLine($"{timestamp} {elapsed,-10} {message}");
    }
}
