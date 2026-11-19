using System.Diagnostics;

namespace Everything2;

internal static class SelfTest
{
    public static int Run()
    {
        var log = new List<string>();
        var root = Path.Combine(AppContext.BaseDirectory, "test-work", Guid.NewGuid().ToString("N"));
        try
        {
            void Check(bool condition, string name) { if (!condition) throw new Exception(name); log.Add("PASS " + name); }
            using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("Everything2.Icon"))
            {
                Check(iconStream != null, "Application icon embedded");
                using var icon = new Icon(iconStream!, 32, 32);
                Check(icon.Width == 32 && icon.Height == 32, "Multi-resolution icon loads");
            }
            Check(StartupRegistration.Command == $"\"{Path.Combine(AppContext.BaseDirectory, "Everything2.exe")}\" --tray", "Windows startup command quotes executable and starts hidden");
            var entries = new[] { new Entry(@"C:\Docs\Annual Report.pdf", false), new Entry(@"C:\Docs\report draft.docx", false), new Entry(@"D:\Code\main.cs", false), new Entry(@"C:\Docs\Reports", true) };
            int Count(string q) => new Query(q).Search(entries, default).Total;
            Check(Count("report") == 3, "Case-insensitive name matching");
            Check(Count("report !draft") == 2, "Exclusion");
            Check(Count("\"Annual Report\" ext:pdf") == 1, "Quoted phrase and extension");
            Check(Count("ext:pdf;cs") == 2, "Multiple extensions");
            Check(Count("folder:") == 1 && Count("file:") == 3, "File and folder filters");
            Check(Count("path:Docs *.pdf") == 1 && Count("D:/Code") == 1, "Path and wildcard matching");
            Check(new Query("", 1, @"D:\").Search(entries, default).Total == 1, "Drive scope");
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel(); var threw = false;
                try { new Query("").Search(entries, canceled.Token); } catch (OperationCanceledException) { threw = true; }
                Check(threw, "Canceled searches stop");
            }
            var large = Enumerable.Range(0, 250000).Select(i => new Entry($@"C:\Data\document-{i}.txt", false)).ToArray();
            var result = new Query("document-249999").Search(large, default);
            Check(result.Total == 1, "250,000-entry search"); log.Add($"BENCH {result.Milliseconds:F1} ms for 250,000 entries");
            Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "original.txt"), "test");
            var cache = Path.Combine(root, "cache", "index.gz");
            void Wait(Func<bool> condition) { var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("Index update timed out"); Thread.Sleep(100); } }
            using (var index = new FileIndex([root + Path.DirectorySeparatorChar], cache))
            {
                index.Start(); Wait(() => index.Snapshot.Any(e => e.Name == "original.txt"));
                Check(true, "Initial scan");
                Directory.CreateDirectory(Path.Combine(root, "before")); File.WriteAllText(Path.Combine(root, "before", "child.txt"), "test");
                Wait(() => index.Snapshot.Any(e => e.Name == "child.txt")); Check(true, "Live creation");
                Directory.Move(Path.Combine(root, "before"), Path.Combine(root, "after"));
                Wait(() => index.Snapshot.Any(e => e.Path.EndsWith(@"after\child.txt")) && !index.Snapshot.Any(e => e.Path.Contains(@"\before"))); Check(true, "Folder rename updates descendants");
                Directory.Delete(Path.Combine(root, "after"), true);
                Wait(() => !index.Snapshot.Any(e => e.Name == "child.txt")); Check(true, "Folder deletion removes descendants");
                File.WriteAllText(Path.Combine(root, "offline.txt"), "test");
                Wait(() => index.Snapshot.Any(e => e.Name == "offline.txt"));
                index.Rebuild(); Wait(() => File.Exists(cache));
            }
            File.Delete(Path.Combine(root, "offline.txt"));
            using (var index = new FileIndex([root + Path.DirectorySeparatorChar], cache))
            {
                index.Start(); Wait(() => index.Status.StartsWith("Watching") && !index.Snapshot.Any(e => e.Name == "offline.txt"));
                Check(index.Snapshot.Any(e => e.Name == "original.txt"), "Cache reload reconciles offline changes");
            }
            File.WriteAllText(cache, "broken");
            using (var index = new FileIndex([root + Path.DirectorySeparatorChar], cache))
            {
                index.Start(); Wait(() => index.Snapshot.Any(e => e.Name == "original.txt")); Check(true, "Corrupt cache recovers");
            }
            log.Add("ALL TESTS PASSED"); File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test.log"), log); return 0;
        }
        catch (Exception e) { log.Add("FAIL " + e); File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test.log"), log); return 1; }
    }
}
