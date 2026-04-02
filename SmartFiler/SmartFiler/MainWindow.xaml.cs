using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SmartFiler.Services;
using SmartFiler.ViewModels;
using WinForms = System.Windows.Forms;

namespace SmartFiler;

public static class MainWindowCommands
{
    public static readonly RoutedUICommand ApproveSelected = new("Approve", "ApproveSelected", typeof(MainWindow));
    public static readonly RoutedUICommand DeferSelected = new("Defer", "DeferSelected", typeof(MainWindow));
    public static readonly RoutedUICommand DeleteSelected = new("Delete", "DeleteSelected", typeof(MainWindow));
}

public partial class MainWindow : Window
{
    private WinForms.NotifyIcon _notifyIcon = null!;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<MainViewModel>();
        UpdateThemeIcon();
        InitializeTrayIcon();

        // Listen for ViewModel property changes to update tray badge
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new WinForms.NotifyIcon();

        // Load icon — try the Resources folder first, then extract from the exe
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app-icon.ico");
        if (File.Exists(iconPath))
        {
            _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
        }
        else
        {
            // Extract icon from the running executable
            var exePath = Environment.ProcessPath;
            if (exePath != null)
                _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }

        _notifyIcon.Text = "Smart Filer";
        _notifyIcon.Visible = true;

        // Right-click context menu
        var contextMenu = new WinForms.ContextMenuStrip();

        var scanItem = new WinForms.ToolStripMenuItem("Scan Now");
        scanItem.Click += (_, _) =>
        {
            if (DataContext is MainViewModel vm && vm.ScanCommand.CanExecute(null))
                vm.ScanCommand.Execute(null);
        };
        contextMenu.Items.Add(scanItem);

        var openItem = new WinForms.ToolStripMenuItem("Open Smart Filer");
        openItem.Click += (_, _) => RestoreFromTray();
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _isExiting = true;
            System.Windows.Application.Current.Shutdown();
        };
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;

        // Double-click to restore
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        this.Show();
        this.WindowState = WindowState.Normal;
        this.Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            this.Hide();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            // Minimize to tray instead of closing
            e.Cancel = true;
            this.WindowState = WindowState.Minimized;
            return;
        }

        // Actually closing — clean up tray icon
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();

        if (DataContext is MainViewModel vm)
            vm.PropertyChanged -= ViewModel_PropertyChanged;

        base.OnClosing(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PendingFileCount) && DataContext is MainViewModel vm)
        {
            var count = vm.PendingFileCount;
            _notifyIcon.Text = count > 0
                ? $"Smart Filer — {count} pending file{(count == 1 ? "" : "s")}"
                : "Smart Filer";
        }
    }

    // ─── Keyboard commands (multi-select aware) ───

    private void KeyCmd_Approve(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count > 0) vm.ApproveFiles(selected);
            else vm.ApproveSelectedCommand.Execute(null);
        }
    }

    private void KeyCmd_Defer(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count == 0 && vm.SelectedFile != null)
                selected = [vm.SelectedFile];
            if (selected.Count > 0) vm.DeferFiles(selected);
        }
    }

    private void KeyCmd_Delete(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count > 0) vm.MarkFilesForDeletion(selected);
            else vm.DeleteSelectedCommand.Execute(null);
        }
    }

    // ─── Column sort ───
    private GridViewColumnHeader? _lastSortHeader;
    private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

    private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header) return;
        if (header.Tag is not string sortBy || string.IsNullOrEmpty(sortBy)) return;

        // Toggle direction if clicking the same column
        ListSortDirection direction;
        if (header == _lastSortHeader)
            direction = _lastSortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        else
            direction = ListSortDirection.Ascending;

        // Apply sort to the ListView's CollectionView
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(TriageListView.ItemsSource);
        if (view == null) return;

        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(sortBy, direction));
        view.Refresh();

        // Update header visual — add arrow indicator
        if (_lastSortHeader != null)
            _lastSortHeader.Content = _lastSortHeader.Tag?.ToString();

        header.Content = $"{sortBy switch {
            "FileName" => "File",
            "Category" => "Type",
            "SourceDirectory" => "Location",
            "SuggestedDestination" => "Destination",
            _ => sortBy
        }} {(direction == ListSortDirection.Ascending ? "\u25B2" : "\u25BC")}";

        _lastSortHeader = header;
        _lastSortDirection = direction;
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out var index))
        {
            if (DataContext is MainViewModel vm)
                vm.SelectedTabIndex = index;
        }
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ToggleTheme();
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        ThemeIcon.Text = ThemeManager.CurrentTheme == ThemeManager.Theme.Dark ? "\u2600" : "\uD83C\uDF19";
    }

    // ─── Keyboard shortcuts (PreviewKeyDown intercepts before ListView type-ahead) ───

    private void TriageListView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        switch (e.Key)
        {
            case System.Windows.Input.Key.Return:
            {
                var selected = GetSelectedFiles();
                if (selected.Count > 0) vm.ApproveFiles(selected);
                else vm.ApproveSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            }
            case System.Windows.Input.Key.D:
            {
                var selected = GetSelectedFiles();
                if (selected.Count == 0 && vm.SelectedFile != null)
                    selected = [vm.SelectedFile];
                if (selected.Count > 0) vm.DeferFiles(selected);
                e.Handled = true;
                break;
            }
            case System.Windows.Input.Key.Delete:
            {
                var selected = GetSelectedFiles();
                if (selected.Count > 0) vm.MarkFilesForDeletion(selected);
                else vm.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            }
        }
    }

    // ─── Double-click to open file ───

    private void TriageListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedFile != null)
            OpenFileInDefaultApp(vm.SelectedFile.FullPath);
    }

    // ─── Context menu handlers ───

    private void ContextMenu_OpenFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedFile != null)
            OpenFileInDefaultApp(vm.SelectedFile.FullPath);
    }

    private void ContextMenu_OpenFolder(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedFile != null)
        {
            var dir = Path.GetDirectoryName(vm.SelectedFile.FullPath);
            if (dir != null && Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{vm.SelectedFile.FullPath}\"");
        }
    }

    private List<SmartFiler.Data.ScannedFile> GetSelectedFiles()
    {
        return TriageListView.SelectedItems
            .Cast<SmartFiler.Data.ScannedFile>()
            .ToList();
    }

    private void ContextMenu_Approve(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count > 1)
                vm.ApproveFiles(selected);
            else
                vm.ApproveSelectedCommand.Execute(null);
        }
    }

    private void ContextMenu_SetAlternative(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select destination folder for selected files",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        // Apply to all selected files
        var selected = GetSelectedFiles();
        if (selected.Count == 0 && vm.SelectedFile != null)
            selected = [vm.SelectedFile];

        foreach (var f in selected)
        {
            f.AlternativeDestination = dialog.SelectedPath;
            f.Action = SmartFiler.Data.FileAction.Approved;
        }

        vm.AlternativeDestination = dialog.SelectedPath;
        vm.UpdateCounts();
    }

    private void ContextMenu_Defer(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count > 0)
                vm.DeferFiles(selected);
            else if (vm.SelectedFile != null)
                vm.DeferFiles([vm.SelectedFile]);
        }
    }

    private void ContextMenu_Reset(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count > 0) vm.ResetFiles(selected);
            else if (vm.SelectedFile != null) vm.ResetFiles([vm.SelectedFile]);
        }
    }

    private void ContextMenu_Delete(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count > 1)
                vm.MarkFilesForDeletion(selected);
            else
                vm.DeleteSelectedCommand.Execute(null);
        }
    }

    // ─── Selection action buttons ───

    private void Btn_ApproveSelection(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count > 0) vm.ApproveFiles(selected);
            else if (vm.SelectedFile != null) vm.ApproveFiles([vm.SelectedFile]);
        }
    }

    private void Btn_DeferSelection(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count == 0 && vm.SelectedFile != null)
                selected = [vm.SelectedFile];
            if (selected.Count > 0) vm.DeferFiles(selected);
        }
    }

    private void Btn_DeleteSelection(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count > 0) vm.MarkFilesForDeletion(selected);
            else if (vm.SelectedFile != null) vm.MarkFilesForDeletion([vm.SelectedFile]);
        }
    }

    private void Btn_ResetSelection(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var selected = GetSelectedFiles();
            if (selected.Count > 0) vm.ResetFiles(selected);
            else if (vm.SelectedFile != null) vm.ResetFiles([vm.SelectedFile]);
        }
    }

    private void Btn_SelectAll(object sender, RoutedEventArgs e)
    {
        TriageListView.SelectAll();
    }

    private void Btn_DeselectAll(object sender, RoutedEventArgs e)
    {
        TriageListView.UnselectAll();
    }

    // ─── Preview panel buttons ───

    private void Preview_BrowseAlternative(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select destination folder for selected files",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        // Get all selected files from the ListView
        var selected = GetSelectedFiles();
        if (selected.Count == 0 && vm.SelectedFile != null)
            selected = [vm.SelectedFile];

        // Apply destination and auto-approve ALL selected files
        foreach (var f in selected)
        {
            f.AlternativeDestination = dialog.SelectedPath;
            f.Action = SmartFiler.Data.FileAction.Approved;
        }

        vm.AlternativeDestination = dialog.SelectedPath;
        vm.UpdateCounts();
    }

    private void Preview_OpenFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedFile != null)
            OpenFileInDefaultApp(vm.SelectedFile.FullPath);
    }

    private void Preview_DeleteFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedFile != null)
        {
            var result = System.Windows.MessageBox.Show(
                $"Mark \"{vm.SelectedFile.FileName}\" for deletion?\n\nThe file won't be deleted until you click 'Move Selected'.",
                "Mark for Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                vm.DeleteSelectedCommand.Execute(null);
        }
    }

    // ─── Destination column click handlers ───

    private void DestButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.DataContext is not SmartFiler.Data.ScannedFile file) return;
        if (!int.TryParse(btn.Tag?.ToString(), out var index)) return;
        if (DataContext is MainViewModel vm)
            vm.SetFileDestination(file, index);
    }

    // ─── Destination right-click alternatives menu ───

    private void DestContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        if (DataContext is not MainViewModel vm) return;

        menu.Items.Clear();

        var alts = vm.AlternativeSuggestions;

        if (alts.Count == 0)
        {
            var empty = new MenuItem { Header = "No alternative suggestions", IsEnabled = false };
            menu.Items.Add(empty);
        }
        else
        {
            foreach (var alt in alts)
            {
                var item = new MenuItem { Header = alt };
                item.Click += (_, _) => vm.SelectAlternativeSuggestionCommand.Execute(alt);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());

        var browseItem = new MenuItem { Header = "Browse for folder..." };
        browseItem.Click += (_, _) => Preview_BrowseAlternative(this, new RoutedEventArgs());
        menu.Items.Add(browseItem);
    }

    // ─── Helpers ───

    private static void OpenFileInDefaultApp(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not open file:\n{ex.Message}",
                "Open Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
