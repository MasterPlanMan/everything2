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

## Limits

Searches names and paths, not document contents. Protected folders are skipped; directory junctions and symlinks are listed but not followed to prevent loops. Cloud placeholders are indexed by name without opening their content. Network drives are excluded. Newly attached drives are included on the next app launch. Results are capped at 2,000 displayed rows; total match counts include all matches. Sorting applies to those displayed rows. Initial enumeration is slower than Everything's privileged NTFS index. This version uses memory proportional to the number of indexed paths.

## Validation

The custom application and tray icon is embedded in the executable at nine sizes from 16 to 256 pixels. Its editable source is `Assets/Everything2.svg`; `tools/Build-Icon.ps1` regenerates the matching PNG and Windows ICO assets.

`dotnet build -c Release`, then `bin/Release/net10.0-windows/Everything2.exe --self-test`. The process writes `self-test.log` beside the executable and exits with 0 on success. Tests exercise query syntax, cancellation, a 250,000-entry search, real filesystem create/rename/delete updates, cache reconciliation, and corrupt-cache recovery. Test fixtures are kept in the executable's `test-work` folder.
