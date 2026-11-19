using System;
using System.Diagnostics;
using Icon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EdgePulse.Native;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace EdgePulse;
public sealed class MainWindow : Window
{
    public AppSettings Settings { get; }
    public SettingsStore Store { get; }
    public TelemetryService Telemetry { get; }
    public Snapshot Latest { get; private set; } = Snapshot.Empty;
    public WidgetSurface Surface { get; }
    public event Action<Snapshot>? SnapshotChanged;
    public event Action? SettingsChanged;
    private readonly WindowPlacement placement;
    private readonly bool verification;
    private Forms.NotifyIcon? tray;
    private SettingsWindow? settingsWindow;
    private DetailWindow? detailWindow;
    private bool closing, dragging, userHidden, autoHiddenByFullscreen;

    public MainWindow(SettingsStore store, bool verification = false)
    {
        this.verification = verification; Store = store; Settings = store.Load();
        Title = "EdgePulse"; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; ShowInTaskbar = false; ShowActivated = false; Topmost = Settings.AlwaysOnTop;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        Surface = new(Settings); Content = Surface; placement = new(this, Settings);
        Telemetry = new(store) { IntervalSeconds = Settings.RefreshSeconds };
        Telemetry.Updated += UpdateSnapshot;
        Surface.DragRequested += () =>
        {
            if (Settings.LockPosition) return;
            dragging = true;
            try { DragMove(); } catch (InvalidOperationException) { }
            finally { dragging = false; placement.Remember(); Save(); placement.Restore(); }
        };
        Surface.ItemClicked += OpenItem;
        Surface.MouseRightButtonUp += (_, e) => { OpenMenu(); e.Handled = true; };
        SourceInitialized += (_, _) => placement.Attach();
        Loaded += (_, _) =>
        {
            Surface.Update(Latest, verification ? 1100 : placement.AvailableWidth);
            if (!verification) { placement.Restore(); CreateTray(); SystemEvents.DisplaySettingsChanged += DisplayChanged; SystemEvents.PowerModeChanged += PowerChanged; }
            Telemetry.Start();
        };
        SizeChanged += (_, _) => { if (!verification && !dragging) Dispatcher.BeginInvoke(placement.Restore, DispatcherPriority.Loaded); };
        Closed += (_, _) =>
        {
            closing = true; if (!verification) { SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.PowerModeChanged -= PowerChanged; }
            tray?.Dispose(); Telemetry.Dispose(); if (!verification) Save();
        };
        KeyDown += (_, e) => { if (e.Key == Key.F2) { OpenSettings(); e.Handled = true; } };
        if (verification) { Left = -10000; Top = -10000; Topmost = false; }
        Surface.Update(Latest, 1000);
    }

    private void UpdateSnapshot(Snapshot snapshot)
    {
        if (closing || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() => SetSnapshot(snapshot));
    }

    public void SetSnapshot(Snapshot snapshot)
    {
        if (closing) return;
        Latest = snapshot;
        if (!verification && !userHidden)
        {
            if (Settings.HideInFullscreen && placement.IsForegroundFullscreen())
            {
                if (IsVisible) { autoHiddenByFullscreen = true; Hide(); }
            }
            else if (autoHiddenByFullscreen)
            {
                autoHiddenByFullscreen = false; Show(); placement.Restore();
            }
        }
        if (verification || IsVisible)
            Surface.Update(snapshot, verification ? 1100 : placement.AvailableWidth);
        SnapshotChanged?.Invoke(snapshot);
        if (tray != null)
            tray.Text = "EdgePulse · CPU " + Display.Percent(snapshot.Cpu) + " · RAM " + Display.Percent(snapshot.MemoryPercent);
    }

    public void ApplySettings()
    {
        Settings.Normalize(); Topmost = !verification && Settings.AlwaysOnTop;
        Telemetry.IntervalSeconds = Settings.RefreshSeconds;
        placement.ApplyExtendedStyles();
        if (!Settings.HideInFullscreen && autoHiddenByFullscreen && !userHidden)
        {
            autoHiddenByFullscreen = false; Show();
        }
        Surface.Update(Latest, verification ? 1100 : placement.AvailableWidth);
        Save();
        SettingsChanged?.Invoke();
        if (!verification) Dispatcher.BeginInvoke(placement.Restore, DispatcherPriority.Loaded);
    }

    private void Save() { try { Store.Save(Settings); } catch (Exception e) { Store.Log(e); } }
    public void ResetPosition() { placement.Reset(); Save(); }
    public void OpenSettings()
    {
        if (settingsWindow == null)
        {
            settingsWindow = new SettingsWindow(this, verification); settingsWindow.Closed += (_, _) => settingsWindow = null; settingsWindow.Show();
        }
        settingsWindow.Activate();
    }

    public void Reveal()
    {
        userHidden = false;
        autoHiddenByFullscreen = false;
        if (!IsVisible) Show();
        Surface.Update(Latest, verification ? 1100 : placement.AvailableWidth);
        if (!verification) placement.Restore();
    }

    public void HideWidget()
    {
        userHidden = true;
        Hide();
    }

    private void OpenItem(string target)
    {
        if (target == "settings") { OpenSettings(); return; }
        if (target == "drives")
        {
            OpenPopup(CreateDriveMenu()); return;
        }
        if (detailWindow != null) detailWindow.Close();
        detailWindow = new DetailWindow(this, target); detailWindow.Closed += (_, _) => detailWindow = null; detailWindow.Show();
    }

    internal ContextMenu CreateDriveMenu()
    {
        var menu = new ContextMenu();
        foreach (var drive in Latest.Drives) { string id = drive.Id; var item = new MenuItem { Header = $"{id} {drive.Label} · {Display.Percent(drive.UsedPercent)} used" }; item.Click += (_, _) => OpenItem("drive:" + id); menu.Items.Add(item); }
        return menu;
    }

    private void OpenMenu() => OpenPopup(CreateWidgetMenu());
    private void OpenPopup(ContextMenu menu)
    {
        menu.PlacementTarget = Surface; menu.IsOpen = true;
    }

    internal ContextMenu CreateWidgetMenu()
    {
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "Settings…", InputGestureText = "F2" }; settings.Click += (_, _) => OpenSettings(); menu.Items.Add(settings);
        foreach (var layout in Enum.GetValues<WidgetLayout>())
        {
            var item = new MenuItem { Header = LayoutName(layout), IsCheckable = true, IsChecked = Settings.Layout == layout };
            item.Click += (_, _) => { Settings.Layout = layout; ApplySettings(); }; menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var locked = new MenuItem { Header = "Lock position", IsCheckable = true, IsChecked = Settings.LockPosition }; locked.Click += (_, _) => { Settings.LockPosition = locked.IsChecked; ApplySettings(); }; menu.Items.Add(locked);
        var clickThru = new MenuItem { Header = "Click-through mode", IsCheckable = true, IsChecked = Settings.ClickThrough }; clickThru.Click += (_, _) => { Settings.ClickThrough = clickThru.IsChecked; ApplySettings(); }; menu.Items.Add(clickThru);
        var autoHide = new MenuItem { Header = "Hide in fullscreen apps", IsCheckable = true, IsChecked = Settings.HideInFullscreen }; autoHide.Click += (_, _) => { Settings.HideInFullscreen = autoHide.IsChecked; ApplySettings(); }; menu.Items.Add(autoHide);
        var reset = new MenuItem { Header = "Reset position" }; reset.Click += (_, _) => ResetPosition(); menu.Items.Add(reset);
        var manager = new MenuItem { Header = "Open Task Manager" }; manager.Click += (_, _) => Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); menu.Items.Add(manager);
        var hide = new MenuItem { Header = "Hide widget" }; hide.Click += (_, _) => HideWidget(); menu.Items.Add(hide);
        menu.Items.Add(new Separator()); var quit = new MenuItem { Header = "Quit EdgePulse" }; quit.Click += (_, _) => Application.Current.Shutdown(); menu.Items.Add(quit);
        return menu;
    }

    private void CreateTray()
    {
        tray = new Forms.NotifyIcon { Text = "EdgePulse", Visible = true, Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Reveal(); OpenSettings(); });
        var menu = new Forms.ContextMenuStrip();
        var showItem = new Forms.ToolStripMenuItem("Show widget", null, (_, _) => Dispatcher.Invoke(Reveal));
        var hideItem = new Forms.ToolStripMenuItem("Hide widget", null, (_, _) => Dispatcher.Invoke(HideWidget));
        var settingsItem = new Forms.ToolStripMenuItem("Settings…", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        var layouts = new Forms.ToolStripMenuItem("Layout");
        foreach (var layout in Enum.GetValues<WidgetLayout>())
        {
            var lItem = new Forms.ToolStripMenuItem(LayoutName(layout), null, (_, _) => Dispatcher.Invoke(() => { Settings.Layout = layout; ApplySettings(); })) { Tag = layout };
            layouts.DropDownItems.Add(lItem);
        }
        var lockItem = new Forms.ToolStripMenuItem("Lock position", null, (_, _) => Dispatcher.Invoke(() => { Settings.LockPosition = !Settings.LockPosition; ApplySettings(); }));
        var clickThruItem = new Forms.ToolStripMenuItem("Click-through mode", null, (_, _) => Dispatcher.Invoke(() => { Settings.ClickThrough = !Settings.ClickThrough; ApplySettings(); }));
        var autoHideItem = new Forms.ToolStripMenuItem("Hide in fullscreen apps", null, (_, _) => Dispatcher.Invoke(() => { Settings.HideInFullscreen = !Settings.HideInFullscreen; ApplySettings(); }));
        var resetItem = new Forms.ToolStripMenuItem("Reset position", null, (_, _) => Dispatcher.Invoke(ResetPosition));
        var taskMgrItem = new Forms.ToolStripMenuItem("Open Task Manager", null, (_, _) => Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }));

        menu.Opening += (_, _) => Dispatcher.Invoke(() =>
        {
            showItem.Enabled = !IsVisible;
            hideItem.Enabled = IsVisible;
            lockItem.Checked = Settings.LockPosition;
            clickThruItem.Checked = Settings.ClickThrough;
            autoHideItem.Checked = Settings.HideInFullscreen;
            foreach (Forms.ToolStripMenuItem lItem in layouts.DropDownItems)
                lItem.Checked = lItem.Tag is WidgetLayout l && Settings.Layout == l;
        });

        menu.Items.Add(showItem);
        menu.Items.Add(hideItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(layouts);
        menu.Items.Add(lockItem);
        menu.Items.Add(clickThruItem);
        menu.Items.Add(autoHideItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(taskMgrItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit EdgePulse", null, (_, _) => Dispatcher.Invoke(() => Application.Current.Shutdown()));
        tray.ContextMenuStrip = menu;
    }

    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { Surface.Update(Latest, placement.AvailableWidth); placement.Restore(); });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Telemetry.RefreshDevices(); }
    public static string LayoutName(WidgetLayout layout) => layout switch { WidgetLayout.MicroStrip => "Micro Strip", WidgetLayout.CompactRail => "Compact Rail", _ => "Dual Row" };
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (Surface != null && placement != null) Dispatcher.BeginInvoke(() => { Surface.Update(Latest, verification ? 1100 : placement.AvailableWidth); if (!verification && !dragging) placement.Restore(); });
    }
}
