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
            var now = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Local);
            var metadata = new[]
            {
                new Entry(@"C:\Data\big.pdf", false, 20 * 1024 * 1024, now.ToUniversalTime()),
                new Entry(@"C:\Data\small.pdf", false, 1024, now.AddDays(-2).ToUniversalTime()),
                new Entry(@"C:\Data\empty.txt", false, 0, now.AddDays(-10).ToUniversalTime()),
                new Entry(@"C:\Data\folder", true, null, now.ToUniversalTime()),
                new Entry(@"C:\Data\unknown.txt", false)
            };
            int MetadataCount(string q) => new Query(q, now: now).Search(metadata, default).Total;
            Check(MetadataCount("size:>10MB") == 1 && MetadataCount("size:>=1KB size:<=20MB") == 2, "Size comparisons and binary units");
            Check(MetadataCount("size:1KB..20MB ext:pdf") == 2 && MetadataCount("size:0") == 1, "Inclusive size range and empty files");
            Check(MetadataCount("size:>0.5KB size:<1.5KB") == 1 && MetadataCount("!size:>10MB") == 4, "Decimal sizes and negation");
            Check(MetadataCount("modified:today") == 2 && MetadataCount("modified:7d") == 3, "Local dates and relative modified filter");
            Check(MetadataCount("modified:2026-11-17..2026-11-19") == 3 && MetadataCount("modified:>2026-11-17") == 2, "Inclusive date range and day comparisons");
            Check(MetadataCount("modified:<2026-11-17") == 1 && MetadataCount("size:>10MB modified:today file:") == 1, "Combined metadata filters exclude unknown metadata");
            foreach (var bad in new[] { "size:banana", "size:2MB..1MB", "size:79228162514264337593543950335TB", "modified:2026-02-30", "modified:tomorrow", "modified:0d", "modified:9999-12-31", "modified:2026-11-19..2026-11-17" })
            {
                var invalid = false;
                try { _ = new Query(bad); } catch (FormatException) { invalid = true; }
                Check(invalid, "Invalid filter rejected: " + bad);
            }
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
            Check(ActionCatalog.Search("", default).Total == ActionCatalog.All.Count && ActionCatalog.All.Select(a => a.Entry.Path).Distinct().Count() == ActionCatalog.All.Count, "Actions catalog lists unique runnable items");
            Check(ActionCatalog.Search("wifi", default).Entries.Any(e => ActionCatalog.Find(e)?.Target == "ms-settings:network"), "Action aliases find network settings");
            Check(ActionCatalog.Search("teams mute", default).Entries.Any(e => ActionCatalog.Find(e)?.Name == "Volume mixer"), "Teams audio action is discoverable by intent");
            Check(ActionCatalog.Search("cleanup", default).Total >= 3 && ActionCatalog.Search("browsers", default).Total == 2, "Cleanup and browser action categories are searchable");
            Check(ActionCatalog.Search("\"Task Manager\" !Settings", default).Total == 1, "Actions support phrases and exclusions");
            foreach (var action in ActionCatalog.All.Where(a => a.RequiresConfirmation))
            {
                var executed = 0; var prompted = 0;
                Check(!ActionCatalog.Run(action, _ => { prompted++; return false; }, _ => executed++) && executed == 0 && prompted == 1, "No prevents execution: " + action.Name);
                ActionCatalog.Run(action, _ => { prompted++; return true; }, _ => executed++);
                ActionCatalog.Run(action, _ => { prompted++; return true; }, _ => executed++);
                Check(executed == 2 && prompted == 3, "Confirmation required on every invocation: " + action.Name);
            }
            foreach (var action in ActionCatalog.All.Where(a => a.Kind is DesktopActionKind.Restart or DesktopActionKind.ShutDown))
            {
                var info = action.StartInfo();
                Check(Path.GetFileName(info.FileName) == "shutdown.exe" && info.ArgumentList.SequenceEqual(new[] { action.Kind == DesktopActionKind.Restart ? "/r" : "/s", "/t", "0" }) && !info.ArgumentList.Contains("/f"), "Power command does not force-close applications: " + action.Name);
            }
            var browserGroup = new BrowserGroup(Path.Combine(root, "browser-session.json"));
            browserGroup.SaveTargets([new("chrome", @"C:\Apps\Chrome\chrome.exe"), new("waterfox", @"D:\Apps\waterfox.exe"), new("msedge", @"C:\Apps\Edge\msedge.exe"), new("brave", @"C:\Apps\Brave\brave.exe"), new("other", @"C:\Apps\other.exe")]);
            Check(browserGroup.LoadTargets().Length == 4, "Browser group persists only configured browser executables");
            browserGroup.SaveTargets([]);
            Check(browserGroup.LoadTargets().Length == 4, "Empty browser snapshot preserves last reopen group");
            Check(BrowserGroup.StartInfo(browserGroup.LoadTargets().First(t => t.Name == "chrome")).ArgumentList.Contains("--restore-last-session") && BrowserGroup.StartInfo(browserGroup.LoadTargets().First(t => t.Name == "waterfox")).ArgumentList.Count == 0, "Browser reopening requests Chromium session restore without unsupported Waterfox flags");
            var libraryPath = Path.Combine(root, "library", "library.json");
            var library = new UserLibrary(libraryPath);
            var saved = new SavedSearch("Recent PDFs", "ext:pdf !draft", 1, @"C:\", 2, 2, true);
            library.Save(saved); library.TogglePin(root); library.TogglePin(Path.Combine(root, "original.txt"));
            library = new UserLibrary(libraryPath);
            Check(library.Searches.Single() == saved && library.Pins.Count == 2, "Saved search filters and launcher pins survive restart");
            library.Save(saved with { Name = "recent pdfs", Text = "ext:docx" });
            Check(library.Searches.Count == 1 && library.Searches[0].Text == "ext:docx", "Saved search replacement ignores name case");
            Check(LauncherCatalog.Load(library.Pins).Any(e => e.Path == root && e.IsDirectory), "Launcher includes pinned project folders");
            Check(LauncherCatalog.Load(library.Pins).Any(e => e.Path == Path.Combine(root, "original.txt") && e.Size == 4), "Launcher includes pinned files and metadata");
            var launchables = new[]
            {
                new Entry(@"C:\Tools\Editor.EXE", false, 100),
                new Entry(@"D:\Projects\my script.py", false, 200),
                new Entry(@"D:\Projects\window.pyw", false, 300),
                new Entry(@"D:\Projects\notes.txt", false),
                new Entry(@"D:\Projects\folder.exe", true)
            };
            var catalog = LauncherCatalog.Load([launchables[0].Path, .. library.Pins], launchables);
            Check(launchables.Take(3).All(e => catalog.Contains(e)), "Launcher discovers indexed executables and Python scripts without pins");
            Check(!catalog.Any(e => e.Path == launchables[3].Path || e.Path == launchables[4].Path), "Launcher excludes unpinned documents and executable-named folders");
            Check(catalog.Count(e => e.Path.Equals(launchables[0].Path, StringComparison.OrdinalIgnoreCase)) == 1, "Pinned executables are deduplicated against index");
            Check(new Query("\"my script\" ext:py").Search(catalog, default).Total == 1, "Launcher searches scripts by name and extension");
            var ranked = new[] { new Entry(@"C:\Apps\a.exe", false, 100), new Entry(@"C:\Apps\b.exe", false, 200), new Entry(@"D:\Scripts\z.py", false, 300) };
            var preferredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"d:\scripts\z.py" };
            var rankedAnswer = new Query("").Search(ranked, default, limit: 2, preferredPaths: preferredPaths);
            Check(rankedAnswer.Total == 3 && rankedAnswer.Entries.Length == 2 && rankedAnswer.Entries[0] == ranked[2], "Pinned launcher item beyond display cap is included first");
            var sorted = ranked.ToArray();
            ResultOrder.Sort(sorted, "Size", descending: true, preferredPaths: preferredPaths);
            Check(sorted[0] == ranked[2] && sorted[1] == ranked[1], "Pinned group stays first when sorting descending");
            Check(new Query("ext:exe").Search(ranked, default, preferredPaths: preferredPaths).Entries.All(e => e != ranked[2]), "Pinned items still obey the current query");
            ResultOrder.Sort(sorted);
            Check(sorted[0] == ranked[0], "File search ordering stays alphabetical without pin preference");
            var appIcon = ResultIcons.ReadIcon(new Entry(Path.Combine(AppContext.BaseDirectory, "Everything2.exe"), false));
            Check(appIcon is { Length: > 0 }, "Shell extracts the executable's actual icon");
            using (var iconData = new MemoryStream(appIcon!))
            using (var bitmap = Image.FromStream(iconData)) Check(bitmap.Width > 0 && bitmap.Height > 0, "Shell icon decodes as an image");
            Check(ResultIcons.ReadIcon(new Entry(@"C:\not-a-real-folder\script.py", false)) is { Length: > 0 }, "Python file-type icon loads without reading or running the script");
            var scriptStart = LauncherCatalog.StartInfo(launchables[1], @"C:\Python\python.exe");
            Check(scriptStart.FileName == @"C:\Python\python.exe" && scriptStart.ArgumentList.Single() == launchables[1].Path && scriptStart.WorkingDirectory == @"D:\Projects", "Python launch uses interpreter with a separate script argument and script working directory");
            Check(LauncherCatalog.StartInfo(launchables[0]).FileName == launchables[0].Path, "Executable launch opens indexed executable directly");
            Check(library.NeedsLaunchConfirmation(launchables[0]) && library.NeedsLaunchConfirmation(launchables[1]), "New executables and Python scripts require first-launch approval");
            var shortcut = new Entry(@"C:\Start Menu\Editor.lnk", false);
            Check(library.NeedsLaunchConfirmation(shortcut), "App shortcuts require first-launch approval");
            Check(!library.NeedsLaunchConfirmation(launchables[3]) && !library.NeedsLaunchConfirmation(launchables[4]), "Documents and folders do not require app approval");
            library.ApproveLaunch(launchables[0].Path);
            library = new UserLibrary(libraryPath);
            Check(!library.NeedsLaunchConfirmation(launchables[0]) && library.IsLaunchApproved(@"c:\tools\editor.exe"), "Launch approval survives restart and ignores path case");
            Check(library.NeedsLaunchConfirmation(new Entry(@"D:\Other\Editor.EXE", false)), "Same app filename at another path requires separate approval");
            Check(library.NeedsLaunchConfirmation(launchables[1]) && !library.IsLaunchApproved(shortcut.Path), "Approving one item leaves other items unapproved");
            library.ForgetLaunchApproval(launchables[0].Path);
            library = new UserLibrary(libraryPath);
            Check(library.NeedsLaunchConfirmation(launchables[0]), "Ask-before-next-launch resets persisted approval");
            var legacyLibraryPath = Path.Combine(root, "legacy-library.json");
            File.WriteAllText(legacyLibraryPath, "{\"Searches\":[],\"Pins\":[\"C:\\\\Tools\\\\Editor.EXE\"]}");
            var legacyLibrary = new UserLibrary(legacyLibraryPath);
            Check(legacyLibrary.LoadWarning == null && legacyLibrary.Pins.Count == 1 && legacyLibrary.NeedsLaunchConfirmation(launchables[0]), "Older saved libraries keep pins and start with no launch approvals");
            using (var cancelLauncher = new CancellationTokenSource())
            {
                cancelLauncher.Cancel(); var stopped = false;
                try { LauncherCatalog.Load([], launchables, cancelLauncher.Token); } catch (OperationCanceledException) { stopped = true; }
                Check(stopped, "Launcher catalog supports cancellation");
            }
            library.TogglePin(root); library.Delete("RECENT PDFS");
            library = new UserLibrary(libraryPath);
            Check(library.Searches.Count == 0 && !library.IsPinned(root), "Unpin and delete persist");
            File.WriteAllText(libraryPath, "broken"); library = new UserLibrary(libraryPath);
            Check(library.LoadWarning != null && library.Searches.Count == 0, "Corrupt library reports recovery warning");
            var missingLibrary = new UserLibrary(Path.Combine(root, "blocked", "library.json"));
            File.WriteAllText(Path.Combine(root, "blocked"), "blocks directory");
            var writeFailed = false;
            try { missingLibrary.Save(saved); } catch (IOException) { writeFailed = true; }
            Check(writeFailed && missingLibrary.Searches.Count == 0, "Failed library writes leave state unchanged");
            writeFailed = false;
            try { missingLibrary.ApproveLaunch(launchables[0].Path); } catch (IOException) { writeFailed = true; }
            Check(writeFailed && missingLibrary.NeedsLaunchConfirmation(launchables[0]), "Failed approval writes keep first-launch confirmation enabled");
            var cache = Path.Combine(root, "cache", "index.gz");
            void Wait(Func<bool> condition) { var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("Index update timed out"); Thread.Sleep(100); } }
            using (var index = new FileIndex([root + Path.DirectorySeparatorChar], cache))
            {
                index.Start(); Wait(() => index.Snapshot.Any(e => e.Name == "original.txt"));
                Check(true, "Initial scan");
                Wait(() => index.Snapshot.Any(e => e.Name == "original.txt" && e.Size == 4 && e.ModifiedUtc.HasValue));
                Check(true, "Scan captures size and modification time");
                File.WriteAllText(Path.Combine(root, "original.txt"), "updated content");
                var changedDate = DateTime.UtcNow.AddDays(-3); File.SetLastWriteTimeUtc(Path.Combine(root, "original.txt"), changedDate);
                Wait(() => index.Snapshot.Any(e => e.Name == "original.txt" && e.Size == 15 && e.ModifiedUtc == changedDate));
                Check(true, "Live content and timestamp changes refresh metadata");
                Directory.CreateDirectory(Path.Combine(root, "before")); File.WriteAllText(Path.Combine(root, "before", "child.txt"), "test");
                Wait(() => index.Snapshot.Any(e => e.Name == "child.txt")); Check(true, "Live creation");
                Directory.Move(Path.Combine(root, "before"), Path.Combine(root, "after"));
                Wait(() => index.Snapshot.Any(e => e.Path.EndsWith(@"after\child.txt")) && !index.Snapshot.Any(e => e.Path.Contains(@"\before"))); Check(true, "Folder rename updates descendants");
                Directory.Delete(Path.Combine(root, "after"), true);
                Wait(() => !index.Snapshot.Any(e => e.Name == "child.txt")); Check(true, "Folder deletion removes descendants");
                File.WriteAllText(Path.Combine(root, "offline.txt"), "test");
                Wait(() => index.Snapshot.Any(e => e.Name == "offline.txt"));
                var beforeSave = File.Exists(cache) ? File.GetLastWriteTimeUtc(cache) : DateTime.MinValue;
                index.Rebuild(); Wait(() => File.Exists(cache) && File.GetLastWriteTimeUtc(cache) > beforeSave);
            }
            File.Delete(Path.Combine(root, "offline.txt"));
            using (var index = new FileIndex([root + Path.DirectorySeparatorChar], cache))
            {
                Entry? cachedEntry = null;
                index.Updated += () => { if (index.Status.StartsWith("Cached index loaded")) cachedEntry = index.Snapshot.FirstOrDefault(e => e.Name == "original.txt"); };
                index.Start(); Wait(() => index.Status.StartsWith("Watching") && !index.Snapshot.Any(e => e.Name == "offline.txt"));
                Check(index.Snapshot.Any(e => e.Name == "original.txt"), "Cache reload reconciles offline changes");
                Check(cachedEntry is { Size: 15, ModifiedUtc: not null }, "Metadata is restored directly from cache");
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
