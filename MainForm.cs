using System.Diagnostics;

namespace Everything2;

public sealed class MainForm : Form
{
    private readonly TextBox search = new() { PlaceholderText = "Search files and folders…", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 17), BorderStyle = BorderStyle.FixedSingle };
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox drive = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly ComboBox sizeFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 195 };
    private readonly ComboBox dateFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 195 };
    private readonly UserLibrary library;
    private static readonly string[] SizeQueries = ["", "size:>10MB", "size:>100MB", "size:>1GB", "size:<1MB", "size:0B"];
    private static readonly string[] DateQueries = ["", "modified:today", "modified:7d", "modified:30d", "modified:365d"];
    private readonly ListView results = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, HideSelection = false, ShowItemToolTips = true, BorderStyle = BorderStyle.None };
    private readonly ResultIcons resultIcons;
    private readonly ResultIcons pinnedIcons;
    private readonly FlowLayoutPanel pinnedStrip = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, Padding = new Padding(0, 2, 0, 0), BackColor = Color.FromArgb(239, 243, 249) };
    private readonly ToolTip pinnedTooltips = new() { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };
    private readonly RowStyle pinnedRow = new(SizeType.Absolute, 0);
    private readonly TableLayoutPanel pinnedPanel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Visible = false };
    private readonly Label status = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label count = new() { AutoSize = true, Text = "Preparing search…", Padding = new Padding(0, 8, 0, 8) };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 120 };
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 1200 };
    private readonly FileIndex index;
    private readonly NotifyIcon tray;
    private bool exiting;
    private bool hotKeyRegistered;
    private bool backgroundSearch;
    private bool queryRunning;
    private bool searchReady;
    private bool allowVisible;
    private bool started;
    private Entry[] displayed = [];
    private CancellationTokenSource? queryStop;
    private int needsRefresh = 1;
    private int request;
    private string sortColumn = "Name";
    private bool descending;
    public MainForm(bool startInTray = false)
    {
        SuspendLayout();
        allowVisible = !startInTray;
        Text = "Everything 2";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("Everything2.Icon")!)
            Icon = new Icon(iconStream);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(246, 248, 251);
        Size = new Size(1200, 800); MinimumSize = new Size(1000, 700); StartPosition = FormStartPosition.CenterScreen;
        var roots = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable).Select(d => d.RootDirectory.FullName).ToArray();
        index = new(roots, Path.Combine(AppContext.BaseDirectory, "data", "index.bin.gz"));
        library = new(Path.Combine(AppContext.BaseDirectory, "data", "library.json"));
        resultIcons = new(results);
        pinnedIcons = new(pinnedStrip, large: true);
        pinnedIcons.Updated += UpdatePinnedIcons;
        pinnedStrip.HandleCreated += (_, _) => UpdatePinnedIcons();
        results.SmallImageList = resultIcons.Images;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 8 };
        var metadataStyle = new RowStyle(SizeType.Absolute, 40);
        layout.RowStyles.Add(new(SizeType.Absolute, 54)); layout.RowStyles.Add(new(SizeType.Absolute, 51)); layout.RowStyles.Add(new(SizeType.Absolute, 40)); layout.RowStyles.Add(metadataStyle); layout.RowStyles.Add(pinnedRow); layout.RowStyles.Add(new(SizeType.Absolute, 36)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 35));
        pinnedPanel.RowStyles.Add(new(SizeType.Absolute, 25)); pinnedPanel.RowStyles.Add(new(SizeType.Percent, 100));
        pinnedPanel.Controls.Add(new Label { Text = "Pinned   ·   Click to launch, right-click for options", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        pinnedPanel.Controls.Add(pinnedStrip, 0, 1);
        layout.Controls.Add(pinnedPanel, 0, 4);
        RebuildPinnedStrip();
        var header = new Label { Text = "Everything 2   /   Search", Font = new Font("Segoe UI Semibold", 20), AutoSize = true };
        layout.Controls.Add(header, 0, 0); layout.Controls.Add(search, 0, 1);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), WrapContents = false };
        kind.Items.AddRange(["Everything", "Files", "Folders"]); kind.SelectedIndex = 0;
        drive.Items.Add("All local drives"); drive.Items.AddRange(roots); drive.SelectedIndex = 0;
        var rebuild = ToolbarButton("Refresh index", 108);
        rebuild.Click += (_, _) => RefreshIndex();
        mode.Items.AddRange(["File search", "Launcher", "Actions"]); mode.SelectedIndex = 0;
        var addLauncher = ToolbarButton("Add launcher item…", 145);
        addLauncher.Click += (_, _) => AddLauncherItem();
        var help = new Label { Text = "Right-click any result to pin it to Launcher", AutoSize = true, Padding = new Padding(8, 6, 0, 0), ForeColor = Color.DimGray };
        filters.Controls.AddRange([mode, kind, drive, rebuild, addLauncher, help]); layout.Controls.Add(filters, 0, 2); layout.Controls.Add(count, 0, 5);
        var clear = ToolbarButton("Clear filters", 96);
        clear.Click += (_, _) => ClearSearchFilters();
        sizeFilter.Items.AddRange(["Any file size", "Larger than 10 MB", "Larger than 100 MB", "Larger than 1 GB", "Smaller than 1 MB", "Empty files (0 bytes)"]); sizeFilter.SelectedIndex = 0;
        dateFilter.Items.AddRange(["Any modified date", "Modified today", "Modified in past 7 days", "Modified in past 30 days", "Modified in past 365 days"]); dateFilter.SelectedIndex = 0;
        var metadataRow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0), WrapContents = false };
        var syntax = new Label { Text = "size:1MB..100MB   modified:>=2026-01-01   ext:pdf   !draft", AutoSize = true, Padding = new Padding(8, 6, 0, 0), ForeColor = Color.DimGray };
        metadataRow.Controls.AddRange([sizeFilter, dateFilter, clear, syntax]); layout.Controls.Add(metadataRow, 0, 3);
        results.Columns.Add("Name", 260); results.Columns.Add("Folder", 440); results.Columns.Add("Type", 100); results.Columns.Add("Size", 100); results.Columns.Add("Modified", 155);
        results.Resize += (_, _) => SizeColumns();
        results.RetrieveVirtualItem += (_, e) =>
        {
            if (e.ItemIndex >= displayed.Length) { e.Item = new ListViewItem(""); return; }
            var item = displayed[e.ItemIndex];
            var action = mode.SelectedIndex == 2 ? ActionCatalog.Find(item) : null;
            e.Item = new ListViewItem([item.Name, action?.Description ?? item.Folder, action?.Category ?? ((library.IsPinned(item.Path) ? "★ " : "") + (item.IsDirectory ? "Folder" : LauncherCatalog.IsAppShortcut(item.Path) ? "App shortcut" : Path.GetExtension(item.Name).TrimStart('.').ToUpperInvariant())), FormatSize(item.Size), item.ModifiedUtc?.ToLocalTime().ToString("g") ?? ""])
            {
                ImageIndex = resultIcons.GetIndex(action?.IconEntry ?? item),
                ToolTipText = action == null ? $"{item.Name}\n{item.Path}" : $"{action.Name}\n{action.Description}\n{action.Target}"
            };
        };
        results.ColumnClick += (_, e) => { var next = e.Column switch { 1 => "Path", 2 => "Type", 3 => "Size", 4 => "Modified", _ => "Name" }; descending = sortColumn == next && !descending; sortColumn = next; SortResults(); };
        results.DoubleClick += (_, _) => OpenSelected(false);
        results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { OpenSelected(e.Control); e.SuppressKeyPress = true; } };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenSelected(false)); menu.Items.Add("Show in Explorer", null, (_, _) => OpenSelected(true)); menu.Items.Add("Copy full path", null, (_, _) => { if (Selected() is { } entry) Clipboard.SetText(entry.Path); });
        var pin = menu.Items.Add("Pin to Launcher", null, (_, _) => { if (Selected() is { } entry) ChangeLibrary(() => library.TogglePin(entry.Path)); });
        var askAgain = menu.Items.Add("Ask before next launch", null, (_, _) => { if (Selected() is { } entry) ChangeLibrary(() => library.ForgetLaunchApproval(entry.Path)); });
        menu.Opening += (_, e) =>
        {
            if (Selected() is not { } entry) { e.Cancel = true; return; }
            var actions = mode.SelectedIndex == 2;
            menu.Items[0].Text = actions ? "Run action" : "Open";
            for (var i = 1; i < menu.Items.Count; i++) menu.Items[i].Visible = !actions;
            pin.Text = library.IsPinned(entry.Path) ? "Unpin from Launcher" : "Pin to Launcher";
            askAgain.Enabled = library.IsLaunchApproved(entry.Path);
        };
        results.ContextMenuStrip = menu;
        layout.Controls.Add(results, 0, 6); layout.Controls.Add(status, 0, 7); Controls.Add(layout);
        search.TextChanged += (_, _) => Schedule(); kind.SelectedIndexChanged += (_, _) => Schedule(); drive.SelectedIndexChanged += (_, _) => Schedule();
        sizeFilter.SelectedIndexChanged += (_, _) => Schedule(); dateFilter.SelectedIndexChanged += (_, _) => Schedule();
        mode.SelectedIndexChanged += (_, _) =>
        {
            var launcher = mode.SelectedIndex == 1;
            var actions = mode.SelectedIndex == 2;
            layout.SuspendLayout();
            header.Text = actions ? "Everything 2   /   Actions" : launcher ? "Everything 2   /   Launcher" : "Everything 2   /   Search";
            search.PlaceholderText = actions ? "Find an action… sound, cleanup, task manager, downloads" : launcher ? "Launch an EXE, Python script, app, or pinned item…" : "Search files and folders…";
            kind.Visible = drive.Visible = rebuild.Visible = addLauncher.Visible = !actions;
            help.Text = actions ? "Enter runs an action · Ctrl+1 Search · Ctrl+2 Launcher · Ctrl+3 Actions" : "Right-click any result to pin it to Launcher";
            metadataRow.Visible = !launcher && !actions;
            metadataStyle.Height = launcher || actions ? 0 : 40 * DeviceDpi / 96f;
            pinnedRow.Height = launcher ? 140 * DeviceDpi / 96f : 0;
            pinnedPanel.Visible = launcher;
            results.Columns[1].Text = actions ? "Description" : "Folder";
            results.Columns[2].Text = actions ? "Category" : "Type";
            SizeColumns();
            layout.ResumeLayout(true);
            Schedule(); search.Focus();
        };
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Down && displayed.Length > 0) { results.Focus(); results.SelectedIndices.Clear(); results.SelectedIndices.Add(0); e.SuppressKeyPress = true; } };
        search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            debounce.Stop();
            var activationRequest = ++request;
            searchReady = false;
            await RunSearch(false);
            if (activationRequest != request || IsDisposed || debounce.Enabled || queryRunning || !searchReady || displayed.Length == 0) return;
            results.SelectedIndices.Clear(); results.SelectedIndices.Add(0); OpenSelected(e.Control);
        };
        debounce.Tick += async (_, _) => { debounce.Stop(); await RunSearch(backgroundSearch); };
        index.Updated += () => Interlocked.Exchange(ref needsRefresh, 1);
        refresh.Tick += (_, _) =>
        {
            if (mode.SelectedIndex == 2) { status.Text = $"{ActionCatalog.All.Count} actions · Type a name or category · Enter to run"; return; }
            status.Text = $"{index.Snapshot.Length:N0} indexed · {index.Status}";
            if (Volatile.Read(ref needsRefresh) == 0) return;
            if (BrowsingResults()) status.Text += " · Results paused while browsing · Ctrl+R to update";
            else if (Visible && !debounce.Enabled && !queryRunning && Interlocked.Exchange(ref needsRefresh, 0) == 1) Schedule(true);
        };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Search — Ctrl+Space", null, (_, _) => ShowSearch());
        trayMenu.Items.Add("Refresh index", null, (_, _) => RefreshIndex());
        var startup = new ToolStripMenuItem("Start with Windows");
        trayMenu.Opening += (_, _) =>
        {
            try { startup.Checked = StartupRegistration.Enabled; }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { startup.Checked = false; }
        };
        startup.Click += (_, _) =>
        {
            try { StartupRegistration.SetEnabled(!StartupRegistration.Enabled); startup.Checked = StartupRegistration.Enabled; }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            { MessageBox.Show(this, e.Message, "Couldn't change Windows startup", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        };
        trayMenu.Items.Add(startup);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit Everything 2", null, (_, _) => { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = Icon, Text = "Everything 2 · Ctrl+Space", ContextMenuStrip = trayMenu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSearch(); };
        Shown += (_, _) => { SizeColumns(); search.Focus(); if (library.LoadWarning is { } warning) MessageBox.Show(this, warning, "Saved library", MessageBoxButtons.OK, MessageBoxIcon.Information); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) HideToTray(); };
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); } };
        FormClosed += (_, _) => { refresh.Stop(); debounce.Stop(); queryStop?.Cancel(); index.Dispose(); resultIcons.Dispose(); pinnedIcons.Dispose(); pinnedTooltips.Dispose(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Q) { exiting = true; Close(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.L) { search.Focus(); search.SelectAll(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.R) { Schedule(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.D1) { mode.SelectedIndex = 0; e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.D2) { mode.SelectedIndex = 1; ClearSearchFilters(); search.Focus(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.D3) { mode.SelectedIndex = 2; ClearSearchFilters(); search.Focus(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { HideToTray(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.F5) { RefreshIndex(); e.SuppressKeyPress = true; }
        };
        AutoScaleDimensions = new SizeF(96, 96);
        ResumeLayout(true);
    }
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(value && allowVisible);
    private void StartBackground()
    {
        if (started) return;
        started = true;
        index.Start(); refresh.Start();
        if (!hotKeyRegistered)
        {
            tray.Text = "Everything 2 · shortcut unavailable";
            tray.ShowBalloonTip(5000, "Search shortcut unavailable", "Ctrl+Space is already in use. Click the Everything 2 tray icon to search.", ToolTipIcon.Warning);
        }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        hotKeyRegistered = GlobalHotKey.Register(Handle);
        BeginInvoke(StartBackground);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (hotKeyRegistered) GlobalHotKey.Unregister(Handle);
        hotKeyRegistered = false;
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == GlobalHotKey.Message && message.WParam == GlobalHotKey.Id) { ShowSearch(); return; }
        base.WndProc(ref message);
    }
    public void ShowSearch()
    {
        allowVisible = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        search.Focus(); search.SelectAll();
        Schedule();
    }
    private void HideToTray() { Hide(); }
    private static Button ToolbarButton(string text, int width) => new()
    {
        Text = text, AutoSize = false, Size = new Size(width, 26),
        Font = new Font("Segoe UI", 9), FlatStyle = FlatStyle.System,
        Margin = new Padding(3, 0, 3, 0), Padding = Padding.Empty
    };
    private void ClearSearchFilters()
    {
        search.Clear(); kind.SelectedIndex = drive.SelectedIndex = sizeFilter.SelectedIndex = dateFilter.SelectedIndex = 0;
        Schedule();
    }
    private void ChangeLibrary(Action change)
    {
        try { change(); RebuildPinnedStrip(); Schedule(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        { MessageBox.Show(this, e.Message, "Couldn't save library", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    private void RebuildPinnedStrip()
    {
        pinnedStrip.SuspendLayout();
        try
        {
            pinnedTooltips.RemoveAll();
            foreach (var old in pinnedStrip.Controls.Cast<Control>().ToArray()) old.Dispose();
            foreach (var path in library.Pins)
            {
                var entry = FileIndex.ReadEntry(Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path)) ?? new Entry(path, false);
                var button = new Button
                {
                    Text = entry.Name, AccessibleName = entry.Name, AccessibleDescription = entry.Path,
                    Tag = entry, Size = new Size(116, 84), Margin = new Padding(4), Padding = new Padding(5),
                    ImageList = pinnedIcons.Images, ImageIndex = 0, TextImageRelation = TextImageRelation.ImageAboveText,
                    TextAlign = ContentAlignment.MiddleCenter, ImageAlign = ContentAlignment.MiddleCenter,
                    AutoEllipsis = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White
                };
                button.FlatAppearance.BorderColor = Color.FromArgb(212, 220, 232);
                pinnedTooltips.SetToolTip(button, entry.Path);
                button.Click += (_, _) => OpenEntry(entry, false, true);
                button.KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.Enter) { OpenEntry(entry, true, true); e.SuppressKeyPress = true; } };
                var menu = new ContextMenuStrip();
                menu.Items.Add("Launch", null, (_, _) => OpenEntry(entry, false, true));
                menu.Items.Add("Show in Explorer", null, (_, _) => OpenEntry(entry, true, true));
                menu.Items.Add("Copy full path", null, (_, _) => Clipboard.SetText(entry.Path));
                menu.Items.Add("Unpin from Launcher", null, (_, _) => ChangeLibrary(() => library.TogglePin(entry.Path)));
                var askAgain = menu.Items.Add("Ask before next launch", null, (_, _) => ChangeLibrary(() => library.ForgetLaunchApproval(entry.Path)));
                menu.Opening += (_, _) => askAgain.Enabled = library.IsLaunchApproved(entry.Path);
                button.ContextMenuStrip = menu;
                button.Disposed += (_, _) => menu.Dispose();
                pinnedStrip.Controls.Add(button);
            }
            if (library.Pins.Count == 0)
                pinnedStrip.Controls.Add(new Label { Text = "Pin an app or Python script from the results to keep it here.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 20, 0, 0) });
            if (pinnedStrip.IsHandleCreated) UpdatePinnedIcons();
        }
        finally { pinnedStrip.ResumeLayout(true); }
    }
    private void UpdatePinnedIcons()
    {
        foreach (var button in pinnedStrip.Controls.OfType<Button>())
            if (button.Tag is Entry entry) button.ImageIndex = pinnedIcons.GetIndex(entry);
    }
    private void AddLauncherItem()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Choose app or file…", null, (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "Pin an app, shortcut, or file to Launcher", Filter = "Apps, Python scripts and shortcuts|*.exe;*.py;*.pyw;*.lnk;*.appref-ms|All files|*.*" };
            if (picker.ShowDialog(this) == DialogResult.OK) PinLauncherItem(picker.FileName);
        });
        menu.Items.Add("Choose project folder…", null, (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = "Pin a project folder to Launcher", UseDescriptionForTitle = true };
            if (picker.ShowDialog(this) == DialogResult.OK) PinLauncherItem(picker.SelectedPath);
        });
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(Cursor.Position);
    }
    private void PinLauncherItem(string path)
    {
        ChangeLibrary(() => { if (!library.IsPinned(path)) library.TogglePin(path); });
        search.Clear(); kind.SelectedIndex = drive.SelectedIndex = sizeFilter.SelectedIndex = dateFilter.SelectedIndex = 0;
        mode.SelectedIndex = 1; Schedule();
    }
    private static string FormatSize(long? size)
    {
        if (size == null) return "";
        double number = size.Value; var units = new[] { "B", "KB", "MB", "GB", "TB" }; var unit = 0;
        while (number >= 1024 && unit < units.Length - 1) { number /= 1024; unit++; }
        return $"{number:0.##} {units[unit]}";
    }
    private void RefreshIndex() { index.Rebuild(); Schedule(); status.Text = "Refresh requested…"; }
    private bool BrowsingResults() => Visible && displayed.Length > 0 && (results.ContainsFocus || results.ClientRectangle.Contains(results.PointToClient(Cursor.Position)) || results.ContextMenuStrip?.Visible == true);
    private void SizeColumns()
    {
        if (results.Columns.Count < 5) return;
        var scale = DeviceDpi / 96f;
        results.Columns[0].Width = (int)(250 * scale);
        results.Columns[2].Width = (int)(110 * scale);
        results.Columns[3].Width = mode.SelectedIndex == 2 ? 0 : (int)(95 * scale);
        results.Columns[4].Width = mode.SelectedIndex == 2 ? 0 : (int)(155 * scale);
        results.Columns[1].Width = Math.Max((int)(150 * scale), results.ClientSize.Width - results.Columns.Cast<ColumnHeader>().Where((_, i) => i != 1).Sum(c => c.Width) - SystemInformation.VerticalScrollBarWidth);
    }
    private void Schedule(bool background = false) { queryStop?.Cancel(); request++; if (!background) searchReady = false; backgroundSearch = background; debounce.Stop(); debounce.Start(); }
    private async Task RunSearch(bool background)
    {
        var id = request;
        queryStop?.Cancel();
        queryStop = new(); var source = queryStop; var token = source.Token;
        queryRunning = true;
        try
        {
            var launcher = mode.SelectedIndex == 1;
            var actions = mode.SelectedIndex == 2; var text = search.Text;
            var query = actions ? null : new Query(string.Join(" ", text, launcher ? "" : SizeQueries[sizeFilter.SelectedIndex], launcher ? "" : DateQueries[dateFilter.SelectedIndex]), kind.SelectedIndex, drive.SelectedIndex > 0 ? drive.SelectedItem?.ToString() : null);
            var pins = library.Pins.ToArray();
            var preferredPaths = launcher ? pins.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
            var answer = await Task.Run(() => actions ? ActionCatalog.Search(text, token) : query!.Search(launcher ? LauncherCatalog.Load(pins, index.Snapshot, token) : index.Snapshot, token, preferredPaths: preferredPaths), token);
            if (id != request || IsDisposed || token.IsCancellationRequested) return;
            if (background && (!Visible || BrowsingResults())) { Interlocked.Exchange(ref needsRefresh, 1); return; }
            var selectedPaths = background ? results.SelectedIndices.Cast<int>().Where(i => i < displayed.Length).Select(i => displayed[i].Path).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
            var topPath = background && results.TopItem is { } top && top.Index < displayed.Length ? displayed[top.Index].Path : null;
            var previous = displayed;
            displayed = answer.Entries;
            SortEntries();
            if (!background || !previous.SequenceEqual(displayed))
            {
                results.BeginUpdate();
                try
                {
                    results.SelectedIndices.Clear();
                    results.VirtualListSize = displayed.Length;
                    for (var i = 0; i < displayed.Length; i++) if (selectedPaths.Contains(displayed[i].Path)) results.SelectedIndices.Add(i);
                    var topIndex = topPath == null ? 0 : Array.FindIndex(displayed, e => e.Path.Equals(topPath, StringComparison.OrdinalIgnoreCase));
                    if (displayed.Length > 0) results.TopItem = results.Items[Math.Max(0, topIndex)];
                    results.Invalidate();
                }
                finally { results.EndUpdate(); }
            }
            count.Text = $"{answer.Total:N0} {(actions ? "actions" : "matches")} · {answer.Milliseconds:N0} ms" + (answer.Total > displayed.Length ? $" · showing first {displayed.Length:N0}; narrow your search for more" : "") + (displayed.Length == 0 ? actions ? " · try a different action or category" : " · try a different name or wait for the scan" : "");
            searchReady = true;
        }
        catch (OperationCanceledException) { }
        catch (FormatException e) { if (id == request) SearchError(e.Message); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { if (id == request) SearchError("Pattern too broad. Try a simpler search."); }
        catch (Exception e) { if (id == request) SearchError("Search error: " + e.Message); }
        finally { if (queryStop == source) { queryRunning = false; queryStop = null; } source.Dispose(); }
    }
    private void SearchError(string message)
    {
        if (IsDisposed) return;
        searchReady = false; displayed = []; results.VirtualListSize = 0; count.Text = message;
    }
    private void SortEntries()
    {
        ResultOrder.Sort(displayed, sortColumn, descending, mode.SelectedIndex == 1 ? library.Pins.ToHashSet(StringComparer.OrdinalIgnoreCase) : null);
    }
    private void SortResults()
    {
        var selectedPaths = results.SelectedIndices.Cast<int>().Where(i => i < displayed.Length).Select(i => displayed[i].Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var topPath = results.TopItem is { } top && top.Index < displayed.Length ? displayed[top.Index].Path : null;
        results.BeginUpdate();
        try
        {
            SortEntries();
            results.SelectedIndices.Clear();
            results.VirtualListSize = displayed.Length;
            for (var i = 0; i < displayed.Length; i++) if (selectedPaths.Contains(displayed[i].Path)) results.SelectedIndices.Add(i);
            if (topPath != null && Array.FindIndex(displayed, e => e.Path.Equals(topPath, StringComparison.OrdinalIgnoreCase)) is var topIndex && topIndex >= 0) results.TopItem = results.Items[topIndex];
            results.Invalidate();
        }
        finally { results.EndUpdate(); }
    }
    private Entry? Selected() => results.SelectedIndices.Count > 0 && results.SelectedIndices[0] < displayed.Length ? displayed[results.SelectedIndices[0]] : null;
    private void OpenSelected(bool reveal)
    {
        if (!searchReady || Selected() is not { } entry) return;
        if (mode.SelectedIndex == 2)
        {
            if (!reveal && ActionCatalog.Find(entry) is { } action) RunAction(action);
            return;
        }
        OpenEntry(entry, reveal, mode.SelectedIndex == 1);
    }
    private void RunAction(DesktopAction action)
    {
        try
        {
            if (ActionCatalog.Run(action, a => MessageBox.Show(this,
                $"Are you sure you want to {a.Name.ToLowerInvariant()}?\n\nSave your work before continuing. This confirmation is required every time.",
                a.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)) HideToTray();
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Couldn't run action", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    private void OpenEntry(Entry entry, bool reveal, bool launcher)
    {
        try
        {
            if (reveal) Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{entry.Path}\"", UseShellExecute = true });
            else
            {
                var needsApproval = library.NeedsLaunchConfirmation(entry);
                if (needsApproval && MessageBox.Show(this,
                    $"Are you sure you want to launch {entry.Name}?\n\n{entry.Path}\n\nYes remembers approval for this path. No leaves it unapproved.",
                    "Confirm first launch", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                Process.Start(launcher ? LauncherCatalog.StartInfo(entry) : new ProcessStartInfo(entry.Path) { UseShellExecute = true, WorkingDirectory = entry.Folder });
                if (needsApproval)
                {
                    try { library.ApproveLaunch(entry.Path); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    { MessageBox.Show(this, "The item was launched, but its approval couldn't be saved. You'll be asked again next time.\n\n" + e.Message, "Couldn't remember approval", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                }
                if (launcher) HideToTray();
            }
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Couldn't open item", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
}
