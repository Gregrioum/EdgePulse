using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace EdgePulse;
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool verify = args.Contains("--verify");
        string? verifyDirectory = verify ? args.SkipWhile(a => a != "--verify").Skip(1).FirstOrDefault() : null;
        if (verify && string.IsNullOrEmpty(verifyDirectory)) return 2;
        var store = new SettingsStore(verify ? Path.Combine(Path.GetFullPath(verifyDirectory!), "profile") : null);
        using var mutex = verify ? null : new Mutex(false, @"Local\EdgePulse.Widget");
        using var activation = verify ? null : new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\EdgePulse.Activate");
        bool ownsMutex = verify;
        if (!verify)
        {
            try { ownsMutex = mutex!.WaitOne(0); } catch (AbandonedMutexException) { ownsMutex = true; }
            if (!ownsMutex) { activation!.Set(); return 0; }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources = new ResourceDictionary { Source = new Uri("Themes.xaml", UriKind.Relative) };
        app.DispatcherUnhandledException += (_, e) =>
        {
            store.Log(e.Exception);
            if (verify) { File.WriteAllText(Path.Combine(verifyDirectory!, "fatal.txt"), e.Exception.ToString()); e.Handled = true; app.Shutdown(1); }
        };
        var window = new MainWindow(store, verify); app.MainWindow = window;
        RegisteredWaitHandle? listener = null;
        if (!verify) listener = ThreadPool.RegisterWaitForSingleObject(activation!, (_, _) => app.Dispatcher.BeginInvoke(() => { window.Reveal(); window.OpenSettings(); }), null, Timeout.Infinite, false);
        if (verify) window.Loaded += async (_, _) => await Verification.Run(window, Path.GetFullPath(verifyDirectory!));
        int exitCode = app.Run(window);
        listener?.Unregister(null);
        window.Telemetry.Dispose();
        try { window.Telemetry.Completion.Wait(TimeSpan.FromSeconds(3)); } catch { }
        if (!verify && ownsMutex) { try { mutex!.ReleaseMutex(); } catch (ApplicationException) { } }
        return exitCode;
    }
}
