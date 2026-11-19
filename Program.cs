namespace Everything2;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest.Run();
        if (args.Contains("--enable-startup")) { StartupRegistration.SetEnabled(true); return StartupRegistration.Enabled ? 0 : 1; }
        if (args.Contains("--disable-startup")) { StartupRegistration.SetEnabled(false); return StartupRegistration.Enabled ? 1 : 0; }
        using var instance = new Mutex(true, @"Local\Everything2.Desktop", out var firstInstance);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Everything2.ShowSearch");
        if (!firstInstance) { if (!args.Contains("--tray")) show.Set(); return 0; }
        ApplicationConfiguration.Initialize();
        using var form = new MainForm(args.Contains("--tray"));
        _ = form.Handle;
        var registration = ThreadPool.RegisterWaitForSingleObject(show, (_, _) =>
        {
            try { form.BeginInvoke(form.ShowSearch); }
            catch (InvalidOperationException) { }
        }, null, Timeout.Infinite, false);
        try { Application.Run(form); }
        finally { registration.Unregister(null); instance.ReleaseMutex(); }
        return 0;
    }
}
