using System.Diagnostics;

namespace Everything2;

public sealed class MainForm : Form
{
    private readonly TextBox search = new() { PlaceholderText = "Search files and folders…", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 17), BorderStyle = BorderStyle.FixedSingle };
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox drive = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ListView results = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, VirtualMode = true, HideSelection = false, BorderStyle = BorderStyle.None };
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
        Size = new Size(1140, 760); MinimumSize = new Size(760, 480); StartPosition = FormStartPosition.CenterScreen;
        var roots = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable).Select(d => d.RootDirectory.FullName).ToArray();
        index = new(roots, Path.Combine(AppContext.BaseDirectory, "data", "index.bin.gz"));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new(SizeType.Absolute, 54)); layout.RowStyles.Add(new(SizeType.Absolute, 51)); layout.RowStyles.Add(new(SizeType.Absolute, 40)); layout.RowStyles.Add(new(SizeType.Absolute, 36)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 35));
        var header = new Label { Text = "Everything 2   /   local file search", Font = new Font("Segoe UI Semibold", 20), AutoSize = true };
        layout.Controls.Add(header, 0, 0); layout.Controls.Add(search, 0, 1);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0), WrapContents = false };
        kind.Items.AddRange(["Everything", "Files", "Folders"]); kind.SelectedIndex = 0;
        drive.Items.Add("All local drives"); drive.Items.AddRange(roots); drive.SelectedIndex = 0;
        var rebuild = new Button { Text = "Refresh index", AutoSize = false, Size = new Size(120, 28), FlatStyle = FlatStyle.Flat };
        rebuild.Click += (_, _) => RefreshIndex();
        var help = new Label { Text = "ext:pdf   path:Downloads   \"exact phrase\"   !exclude   *.cs", AutoSize = true, Padding = new Padding(12, 6, 0, 0), ForeColor = Color.DimGray };
        filters.Controls.AddRange([kind, drive, rebuild, help]); layout.Controls.Add(filters, 0, 2); layout.Controls.Add(count, 0, 3);
        results.Columns.Add("Name", 320); results.Columns.Add("Folder", 590); results.Columns.Add("Type", 110);
        results.Resize += (_, _) => SizeColumns();
        results.RetrieveVirtualItem += (_, e) =>
        {
            if (e.ItemIndex >= displayed.Length) { e.Item = new ListViewItem(""); return; }
            var item = displayed[e.ItemIndex];
            e.Item = new ListViewItem([item.Name, item.Folder, item.IsDirectory ? "Folder" : Path.GetExtension(item.Name).TrimStart('.').ToUpperInvariant()]);
        };
        results.ColumnClick += (_, e) => { var next = e.Column == 1 ? "Path" : e.Column == 2 ? "Type" : "Name"; descending = sortColumn == next && !descending; sortColumn = next; SortResults(); };
        results.DoubleClick += (_, _) => OpenSelected(false);
        results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { OpenSelected(e.Control); e.SuppressKeyPress = true; } };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => OpenSelected(false)); menu.Items.Add("Show in Explorer", null, (_, _) => OpenSelected(true)); menu.Items.Add("Copy full path", null, (_, _) => { if (Selected() is { } entry) Clipboard.SetText(entry.Path); });
        results.ContextMenuStrip = menu;
        layout.Controls.Add(results, 0, 4); layout.Controls.Add(status, 0, 5); Controls.Add(layout);
        search.TextChanged += (_, _) => Schedule(); kind.SelectedIndexChanged += (_, _) => Schedule(); drive.SelectedIndexChanged += (_, _) => Schedule();
        search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Down && displayed.Length > 0) { results.Focus(); results.SelectedIndices.Clear(); results.SelectedIndices.Add(0); e.SuppressKeyPress = true; } };
        debounce.Tick += async (_, _) => { debounce.Stop(); await RunSearch(backgroundSearch); };
        index.Updated += () => Interlocked.Exchange(ref needsRefresh, 1);
        refresh.Tick += (_, _) =>
        {
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
        Shown += (_, _) => { SizeColumns(); search.Focus(); };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) HideToTray(); };
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); } };
        FormClosed += (_, _) => { refresh.Stop(); debounce.Stop(); queryStop?.Cancel(); index.Dispose(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Q) { exiting = true; Close(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.L) { search.Focus(); search.SelectAll(); e.SuppressKeyPress = true; }
            if (e.Control && e.KeyCode == Keys.R) { Schedule(); e.SuppressKeyPress = true; }
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
    private void RefreshIndex() { index.Rebuild(); Schedule(); status.Text = "Refresh requested…"; }
    private bool BrowsingResults() => Visible && displayed.Length > 0 && (results.ContainsFocus || results.ClientRectangle.Contains(results.PointToClient(Cursor.Position)) || results.ContextMenuStrip?.Visible == true);
    private void SizeColumns()
    {
        if (results.Columns.Count < 3) return;
        var scale = DeviceDpi / 96f;
        results.Columns[0].Width = (int)(300 * scale);
        results.Columns[2].Width = (int)(80 * scale);
        results.Columns[1].Width = Math.Max((int)(150 * scale), results.ClientSize.Width - results.Columns[0].Width - results.Columns[2].Width - SystemInformation.VerticalScrollBarWidth);
    }
    private void Schedule(bool background = false) { queryStop?.Cancel(); request++; backgroundSearch = background; debounce.Stop(); debounce.Start(); }
    private async Task RunSearch(bool background)
    {
        var id = request;
        queryStop = new(); var source = queryStop; var token = source.Token;
        queryRunning = true;
        try
        {
            var query = new Query(search.Text, kind.SelectedIndex, drive.SelectedIndex > 0 ? drive.SelectedItem?.ToString() : null);
            var answer = await Task.Run(() => query.Search(index.Snapshot, token), token);
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
            count.Text = $"{answer.Total:N0} matches · {answer.Milliseconds:N0} ms" + (answer.Total > displayed.Length ? $" · showing first {displayed.Length:N0}; narrow your search for more" : "") + (displayed.Length == 0 ? " · try a different name or wait for the scan" : "");
        }
        catch (OperationCanceledException) { }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { count.Text = "Pattern too broad. Try a simpler search."; }
        catch (Exception e) { if (!IsDisposed) count.Text = "Search error: " + e.Message; }
        finally { if (queryStop == source) { queryRunning = false; queryStop = null; } source.Dispose(); }
    }
    private void SortEntries()
    {
        Array.Sort(displayed, (a, b) => { var comparison = sortColumn == "Path" ? StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path) : sortColumn == "Type" ? StringComparer.OrdinalIgnoreCase.Compare(a.IsDirectory ? "Folder" : Path.GetExtension(a.Name), b.IsDirectory ? "Folder" : Path.GetExtension(b.Name)) : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); return descending ? -comparison : comparison; });
    }
    private void SortResults() { SortEntries(); results.VirtualListSize = displayed.Length; results.Invalidate(); }
    private Entry? Selected() => results.SelectedIndices.Count > 0 && results.SelectedIndices[0] < displayed.Length ? displayed[results.SelectedIndices[0]] : null;
    private void OpenSelected(bool reveal)
    {
        if (Selected() is not { } entry) return;
        try
        {
            if (reveal) Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{entry.Path}\"", UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true, WorkingDirectory = entry.Folder });
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Couldn't open item", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
}
