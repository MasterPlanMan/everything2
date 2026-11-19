# Everything 2

A native Windows filename search app. Searches all ready fixed and removable drives without administrator access, third-party packages, a service, or a browser.

## Run

Requires the .NET 10 SDK to build and the .NET 10 Windows Desktop runtime to run (both installed on this machine).

Run `./Start-Everything2.ps1`, or build with `dotnet build -c Release` and open `bin/Release/net10.0-windows/Everything2.exe`.

The first scan walks your drives in the background. Results become available during the scan. Later launches load a compressed index from the executable's `data` folder, then reconcile it with disk. The app watches file and folder creation, rename, and deletion while running, rescans every 15 minutes, and requests a rescan if Windows reports dropped watcher events. F5 or **Refresh index** requests a fresh scan.

## System tray and global shortcut

- **Ctrl+Space** brings search to the foreground from any app and selects the search text.
- Closing, minimizing, or pressing Escape hides the window to the system tray. Indexing and the shortcut remain active.
- Click the tray icon to reopen search. Right-click it for Search, Refresh index, and **Exit Everything 2**. Exit or **Ctrl+Q** stops the app and releases its shortcut.
- **Start with Windows** in the tray menu toggles automatic startup for your Windows account. Startup launches this executable with `--tray`, keeping the search window hidden while indexing and the shortcut initialize. Windows may delay startup apps after sign-in.
- Launch with `Everything2.exe --tray` to start hidden manually. `--enable-startup` and `--disable-startup` set the same startup preference from the command line and exit. Re-enable startup if you move the executable to another directory.
- Launching the app again brings up the existing instance instead of creating a duplicate.
- If another program owns Ctrl+Space, a tray notification reports the conflict; the tray icon still opens search.

Background index updates pause for the visible results while the pointer is over the list, the list has keyboard focus, or its context menu is open. This keeps scrolling and selections stable. Updates resume when you leave the list and move focus elsewhere. **Ctrl+R** updates results immediately without rescanning the drives. Typing a new query always updates the results. Background updates preserve selection and the top visible item when those files still match, and leave unchanged rows alone.

## Search

- Words match names, ignoring case. Multiple words must all match.
- `"annual report"` matches a phrase; `!draft` excludes a term.
- `ext:pdf` or `ext:jpg;png` filters extensions.
- `path:Downloads` searches a path; terms with slashes also search paths.
- `*.cs` and `report-??.pdf` support wildcards (substring matching).
- `file:` and `folder:` filter item types. The dropdowns also filter types and drives.
- Double-click or Enter opens a result. Ctrl+Enter reveals it in Explorer. Right-click to copy its full path.
- Ctrl+L focuses and selects search text, Escape hides to the tray, and Down selects the first result. Click a column to sort the displayed results.

## Saved searches

Saved searches are disabled. The saved-search dropdown, Save/Delete buttons and Ctrl+D shortcut are removed. Existing saved-search records remain in `data/library.json` for compatibility. **Clear filters** is available beside the size/date filters and clears the query and filters in the current view.

Launcher pins and first-launch approvals persist in `data/library.json` beside the executable, separately from the rebuildable index.

## Size and date filters

Use the dropdown presets or combine precise filters in the search box:

- `size:>100MB` — files larger than 100 MB.
- `size:1MB..100MB` — files between those sizes, including both bounds.
- `size:0` — empty files. Units are bytes by default; B, KB, MB, GB and TB are supported, with 1 KB = 1,024 bytes. Decimal quantities such as `size:>=1.5GB` work.
- `modified:today` or `modified:yesterday` — modified on that local calendar day.
- `modified:7d` — modified in the past seven days, measured from the current time.
- `modified:>=2026-01-01` — modified on or after that date.
- `modified:2026-01-01..2026-01-31` — modified within an inclusive calendar-date range.

Both filters accept `>`, `>=`, `<`, `<=` and `=`. Date comparisons use whole local calendar days; `modified:>2026-01-01` starts on January 2. Terms, presets and filters are combined with AND; `!` negates an individual filter. Invalid filters show an explanation and clear the results.

The results include sortable **Size** and **Modified** columns. Folder sizes are blank and folders do not match positive size filters. Modified dates apply to files and folders. Metadata that cannot be read stays unavailable. The first launch after this update rebuilds the older filename-only index; subsequent launches cache sizes and dates too. While running, file size and timestamp changes update the index automatically.

## Launcher

Choose **Launcher** to search every indexed `.exe`, `.py`, and `.pyw` file, plus Start menu app shortcuts and your pinned items. **Ctrl+2** opens Launcher and clears the search text, type, drive, size and date filters every time, even when Launcher is already selected. Launchable files appear automatically without pinning as the drive scan progresses. **Ctrl+1** returns to File search. The existing **Ctrl+Space** shortcut opens whichever view you last used in the current session.

- Right-click a search result and choose **Pin to Launcher** to keep an app, file or project folder handy. Pinned results have a star in the Type column; the same menu can unpin them.
- Matching pinned items appear first in Launcher, including when the result cap is reached or a column is sorted descending. The chosen sort applies within the pinned and unpinned groups. File search keeps its normal ordering.
- Launcher also has a separate **Pinned** row above the results, with larger app/script icons and labels in pin order. This row stays available while you search or change filters; it scrolls horizontally when needed. Hover an icon for its full file path. Click once to launch with the usual first-launch confirmation, or right-click to reveal, copy the path, unpin, or reset launch approval. The row is hidden in File search.
- Results show Windows icons for executables, app shortcuts, Python file types and folders. Icons load in the background as rows become visible. Hover a result for its filename and full path, including the final `.exe` or script filename.
- **Add launcher item…** lets you choose an app/file or a project folder directly, including items outside the indexed drives.
- Type a name, then press **Enter** to open the first displayed result, or use **Down** to choose another result. Enter opens the selected item when the results list has focus. Launcher hides to the tray after opening an item.
- **Ctrl+Enter** reveals the item in Explorer. Project folders open in Explorer; documents use their default app, and apps use their usual Windows shortcut or executable.
- Python scripts run through `py.exe` or `python.exe` on PATH, with the script's folder as their working directory. `.pyw` prefers `pyw.exe` or `pythonw.exe` when available. Python must be installed. In File search, Python files still use their Windows file association.
- The first time you launch each executable, Python script or app shortcut, a **Yes/No** confirmation shows its name and full path, with **No** selected by default. No cancels without remembering approval. Yes allows the launch and saves approval after Windows accepts it; failed launches remain unapproved. This applies in both Launcher and File search. Approval is per path and survives restarts; a moved copy asks again. Right-click an approved item and choose **Ask before next launch** to reset it. Opening folders/documents and revealing items in Explorer do not need this app-launch confirmation.

Launcher hides and collapses the size/date filter row to leave more space for pins and results. The hidden dropdown presets do not affect Launcher; switching back to Search restores them. Explicit query filters such as `size:>1MB` still work, and `ext:exe` or `ext:py;pyw` narrows the launchable types. Press **Ctrl+2** to clear the launcher query and filters. Start menu `.lnk` and `.appref-ms` shortcuts are discovered each time the launcher searches. Executables and scripts share the file index's access and drive limits and the 2,000 displayed-result cap; type a name or path to narrow a broad list. Launcher does not execute typed shell commands.

## Actions

Press **Ctrl+3** or choose **Actions** for searchable PC tasks. Ctrl+3 clears the current search and filters. Type an action, category or keyword, such as `wifi`, `cleanup`, `teams mute`, `browsers`, or `downloads`. Quoted phrases and `!` exclusions also work. Enter runs the first displayed action from the search box, or the selected action when the results list has focus. Successful actions hide Everything2 to its tray. Ctrl+1 and Ctrl+2 return to Search and Launcher.

- **Settings:** Windows Settings, sound, displays, Bluetooth, network, installed apps, Control Panel, Volume mixer, notifications/Do Not Disturb, startup apps and power settings. These open the relevant Windows page; Volume mixer lets you mute other applications while keeping Teams audible.
- **Cleanup:** Storage cleanup settings, Disk Cleanup and your temporary files folder. These open Windows tools or the folder for review; they do not automatically delete files.
- **Utilities:** Task Manager, Calculator and Snipping Tool.
- **Folders:** Downloads, Documents and Recycle Bin.
- **Browsers:** Close browsers and Reopen browsers for Edge, Waterfox, Chrome and Brave. Closing asks for confirmation, saves executable paths in `data/browser-session.json`, and sends normal close requests to their visible windows. It never force-kills browser processes. Save/download prompts may keep a browser open; browser background processes may remain running. Reopen launches remembered executable paths and requests Chromium's last-session restoration. Waterfox uses its own session-restore settings. Custom profile arguments, private windows and unsaved form contents are not recorded or guaranteed to be restored.
- **Computer:** Lock, sleep, restart and shut down. Sleep, restart and shut down require a fresh Yes/No confirmation every time, with No selected by default. Restart/shut down do not force applications to close. These confirmations are separate from remembered app-launch approvals.

Actions ignore file/drive/size/date filters and hide those controls and launcher pins. Power and browser-closing tests use an injected executor so automated validation never performs those actions. Settings links follow Microsoft's [Windows Settings URI documentation](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings).

## Limits

Searches names and paths, not document contents. Protected folders are skipped; directory junctions and symlinks are listed but not followed to prevent loops. Cloud placeholders are indexed by name without opening their content. Network drives are excluded. Newly attached drives are included on the next app launch. Results are capped at 2,000 displayed rows; total match counts include all matches. Sorting applies to those displayed rows. Initial enumeration is slower than Everything's privileged NTFS index. This version uses memory proportional to the number of indexed paths.

## Validation

The custom application and tray icon is embedded in the executable at nine sizes from 16 to 256 pixels. Its editable source is `Assets/Everything2.svg`; `tools/Build-Icon.ps1` regenerates the matching PNG and Windows ICO assets.

Exit the running tray app with **Ctrl+Q** before rebuilding its executable. Run `dotnet build -c Release`, then `bin/Release/net10.0-windows/Everything2.exe --self-test`. The process writes `self-test.log` beside the executable and exits with 0 on success. Tests exercise query syntax, size/date boundaries and invalid input, saved-search and pin persistence, launcher entries, cancellation, a 250,000-entry search, real filesystem create/rename/delete and metadata updates, cache reconciliation, and corrupt-cache recovery. Test fixtures are kept in the executable's `test-work` folder.
