using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using FinderApp.Models;
using FinderApp.Services;

namespace FinderApp;

public partial class MainWindow : Window
{
    private readonly FileIndexService _indexService = new();
    private readonly HotKeyManager _hotKeyManager = new();
    private readonly DispatcherTimer _searchDebounceTimer;
    private string _currentQuery = "";
    private string _selectedCategory = "All";

    // Infinite scrolling / pagination with ultra-lightweight candidate structs
    private List<SearchCandidate> _allCandidates = new();
    private readonly ObservableCollection<SearchResultItem> _displayedItems = new();
    private int _loadedCount = 0;
    private const int PageSize = 50;
    private bool _isLoadingMore = false;
    private bool _isManualReindexing = false;

    // Category bar sliding & drag state
    private Point _categoryDragStartPoint;
    private double _categoryDragStartOffset;
    private bool _isCategoryDragging = false;
    private long _categoryDragSuppressUntil = 0;

    // Search sequence tracking to prevent results mixing / out-of-order responses
    private int _searchSequenceId = 0;

    // Native Win32 System Tray Icon Manager
    private readonly TrayIconManager _trayIconManager = new();

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    public MainWindow()
    {
        InitializeComponent();

        ResultsList.ItemsSource = _displayedItems;

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

        _indexService.IndexCountChanged += count =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_indexService.IsScanning)
                {
                    StatusText.Text = $"⚡ Indexing ({count:N0} files)...";
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    if (ReindexButton != null) ReindexButton.IsEnabled = false;
                }
            });
        };

        _indexService.IndexFinished += () =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                StatusText.Text = $"Ready • {_indexService.TotalFilesIndexed:N0} files indexed";
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                if (ReindexButton != null) ReindexButton.IsEnabled = true;

                if (_isManualReindexing)
                {
                    _isManualReindexing = false;
                    if (!string.IsNullOrWhiteSpace(_currentQuery))
                    {
                        PerformSearch(_currentQuery);
                    }
                }
            });
        };

        _indexService.LiveIndexUpdated += () =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!_indexService.IsScanning)
                {
                    StatusText.Text = $"Ready • {_indexService.TotalFilesIndexed:N0} files indexed";
                }
            });
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();

        // Register Global HotKey
        bool registered = _hotKeyManager.Register(this);
        ShortcutBadge.Text = _hotKeyManager.ActiveShortcutDescription;
        _hotKeyManager.HotKeyPressed += OnGlobalHotKeyPressed;

        if (!registered)
        {
            MessageBox.Show(
                "Could not register global hotkey (Alt+Space or Ctrl+Space might be in use by another app).",
                "Finder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // Register in Windows Startup so it starts on system boot
        StartupHelper.EnsureStartupRegistered();

        // Start indexing in background
        _indexService.StartBackgroundIndexing();

        // Auto-focus search input
        SearchBox.Focus();

        // Initialize Native System Tray Icon
        _trayIconManager.Initialize(this);
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProcTrayHook);
    }

    private void PositionWindow()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;

        Left = (screenWidth - Width) / 2;
        Top = (screenHeight - 500) * 0.22;
    }

    /// <summary>
    /// Allows freely dragging and moving the window anywhere across the screen.
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
        {
            DragMove();
        }
    }

    private void OnGlobalHotKeyPressed()
    {
        if (IsVisible && IsActive)
        {
            HideWindow();
        }
        else
        {
            ShowWindow();
        }
    }

    public void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void HideWindow()
    {
        Hide();
        try
        {
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch { }
    }

    /// <summary>
    /// Closes/hides automatically whenever the user clicks anywhere outside the window.
    /// </summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        HideWindow();
    }

    private void WindowCloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideWindow();
    }

    private void ReindexButton_Click(object sender, RoutedEventArgs e)
    {
        StartManualReindex();
    }

    private void StartManualReindex()
    {
        if (_indexService.IsScanning) return;

        _isManualReindexing = true;
        StatusText.Text = "⚡ Starting re-index...";
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        if (ReindexButton != null) ReindexButton.IsEnabled = false;

        _indexService.TriggerReindex();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = SearchBox.Text;
        PlaceholderText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

        if (string.IsNullOrWhiteSpace(text))
        {
            _selectedCategory = "All";
            _allCandidates.Clear();
            _displayedItems.Clear();
            _loadedCount = 0;
            CategoryBar.Visibility = Visibility.Collapsed;
        }

        _currentQuery = text;
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        PerformSearch(_currentQuery);
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        // Ignore click if user was dragging or sliding
        if (Environment.TickCount64 < _categoryDragSuppressUntil)
            return;

        if (sender is Button btn && btn.Tag is string catName)
        {
            _selectedCategory = catName;
            PerformSearch(_currentQuery);
        }
    }

    #region Category Bar Sliding & Drag Handlers

    private void CategoryScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _categoryDragStartPoint = e.GetPosition(CategoryScrollViewer);
            _categoryDragStartOffset = CategoryScrollViewer.HorizontalOffset;
            _isCategoryDragging = false;
        }
    }

    private void CategoryScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && CategoryScrollViewer != null)
        {
            Point currentPoint = e.GetPosition(CategoryScrollViewer);
            double deltaX = _categoryDragStartPoint.X - currentPoint.X;

            if (!_isCategoryDragging && Math.Abs(deltaX) > 4)
            {
                _isCategoryDragging = true;
                CategoryScrollViewer.CaptureMouse();
                CategoryScrollViewer.Cursor = Cursors.SizeWE;
            }

            if (_isCategoryDragging)
            {
                CategoryScrollViewer.ScrollToHorizontalOffset(_categoryDragStartOffset + deltaX);
            }
        }
    }

    private void CategoryScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isCategoryDragging)
        {
            _isCategoryDragging = false;
            CategoryScrollViewer.ReleaseMouseCapture();
            CategoryScrollViewer.Cursor = null;
            _categoryDragSuppressUntil = Environment.TickCount64 + 250;
            e.Handled = true;
        }
    }

    private void CategoryScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (CategoryScrollViewer == null) return;

        // Slide horizontally on mouse wheel (wheel down/backward -> slide right, wheel up/forward -> slide left)
        double delta = -(e.Delta * 0.75);
        CategoryScrollViewer.ScrollToHorizontalOffset(CategoryScrollViewer.HorizontalOffset + delta);
        e.Handled = true;
    }

    private void CategoryScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryScrollViewer != null)
        {
            CategoryScrollViewer.ScrollToHorizontalOffset(CategoryScrollViewer.HorizontalOffset - 120);
        }
    }

    private void CategoryScrollRight_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryScrollViewer != null)
        {
            CategoryScrollViewer.ScrollToHorizontalOffset(CategoryScrollViewer.HorizontalOffset + 120);
        }
    }

    #endregion

    private void PerformSearch(string query)
    {
        int seq = Interlocked.Increment(ref _searchSequenceId);

        if (string.IsNullOrWhiteSpace(query))
        {
            _allCandidates.Clear();
            _displayedItems.Clear();
            _loadedCount = 0;
            CategoryBar.Visibility = Visibility.Collapsed;
            return;
        }

        string activeCategory = _selectedCategory;

        Task.Run(() =>
        {
            return _indexService.Search(query, activeCategory);
        }).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && _searchSequenceId == seq)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (_searchSequenceId != seq) return;

                    var response = t.Result;

                    // Update category breakdown list (shows Folder 10, Code 4881 etc.)
                    CategoryItemsList.ItemsSource = response.Categories;
                    CategoryBar.Visibility = response.Categories.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

                    // Reset pagination
                    _allCandidates = response.Candidates;
                    _displayedItems.Clear();
                    _loadedCount = 0;

                    // Load initial page (40 items)
                    LoadNextBatch(seq);

                    if (_displayedItems.Count > 0)
                    {
                        ResultsList.SelectedIndex = 0;
                    }
                });
            }
        });
    }

    /// <summary>
    /// Loads the next batch of results seamlessly as the user scrolls down.
    /// Resolves full path, icons, and highlight indices on-demand ONLY for the 40 visible items!
    /// </summary>
    private void LoadNextBatch(int? expectedSeq = null)
    {
        int seq = expectedSeq ?? _searchSequenceId;
        if (_searchSequenceId != seq) return;

        if (_isLoadingMore || _loadedCount >= _allCandidates.Count)
            return;

        _isLoadingMore = true;

        int toTake = Math.Min(PageSize, _allCandidates.Count - _loadedCount);
        var batchCandidates = _allCandidates.GetRange(_loadedCount, toTake);
        _loadedCount += toTake;
        string query = _currentQuery;

        Task.Run(() =>
        {
            var items = new List<SearchResultItem>(toTake);

            foreach (var c in batchCandidates)
            {
                string dir = _indexService.GetDirectoryPath(c.DirIndex);
                string fullPath = Path.Combine(dir, c.Name);
                var highlights = FuzzySearchEngine.GetHighlightIndices(c.Name, query, c.MatchType);

                var item = new SearchResultItem
                {
                    Name = c.Name,
                    FullPath = fullPath,
                    DirectoryPath = dir,
                    IsDirectory = c.IsDirectory,
                    HighlightIndices = highlights,
                    Score = c.Score,
                    MatchType = c.MatchType,
                    Category = c.Category,
                    CategoryIcon = c.CategoryIcon
                };

                item.Icon = IconHelper.GetIcon(item.FullPath, item.IsDirectory);
                if (item.IsDirectory)
                {
                    item.SizeText = "Folder";
                }
                else
                {
                    try
                    {
                        if (File.Exists(item.FullPath))
                        {
                            var length = new FileInfo(item.FullPath).Length;
                            item.SizeText = FormatFileSize(length);
                        }
                    }
                    catch { }
                }

                items.Add(item);
            }

            return items;
        }).ContinueWith(t =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isLoadingMore = false;
                if (_searchSequenceId != seq) return;

                if (t.IsCompletedSuccessfully)
                {
                    foreach (var item in t.Result)
                    {
                        _displayedItems.Add(item);
                    }
                }
            });
        });
    }

    /// <summary>
    /// Triggered whenever the user scrolls. Proactively pre-fetches the next batch before reaching bottom.
    /// </summary>
    private void ResultsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 350)
        {
            if (_loadedCount < _allCandidates.Count && !_isLoadingMore)
            {
                LoadNextBatch();
            }
        }
    }

    private ScrollViewer? _resultsScrollViewer;

    private ScrollViewer? GetResultsScrollViewer()
    {
        if (_resultsScrollViewer != null)
            return _resultsScrollViewer;

        _resultsScrollViewer = FindVisualChild<ScrollViewer>(ResultsList);
        return _resultsScrollViewer;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    /// <summary>
    /// Smoothly controls the scroll speed of the search results list, reducing default jumpy wheel speed.
    /// </summary>
    private void ResultsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = GetResultsScrollViewer();
        if (scrollViewer == null) return;

        // Controlled scroll step (~28px per 120 notch) for comfortable, smooth list browsing
        double delta = -(e.Delta * 0.24);
        double targetOffset = Math.Clamp(scrollViewer.VerticalOffset + delta, 0, scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(targetOffset);

        if (targetOffset >= scrollViewer.ScrollableHeight - 350)
        {
            if (_loadedCount < _allCandidates.Count && !_isLoadingMore)
            {
                LoadNextBatch();
            }
        }

        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C) ||
            ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C))
        {
            if (SearchBox.IsFocused && SearchBox.SelectionLength > 0)
            {
                return;
            }

            if (ResultsList.SelectedItem is SearchResultItem selectedItem)
            {
                CopyPathToClipboard(selectedItem.FullPath);
                e.Handled = true;
                return;
            }
        }

        if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q) ||
            (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.F4))
        {
            ExitApplication();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R) || e.Key == Key.F5)
        {
            StartManualReindex();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = "";
            }
            else
            {
                HideWindow();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            if (_displayedItems.Count > 0)
            {
                // If navigating near the end of loaded items, load next batch automatically
                if (ResultsList.SelectedIndex >= _displayedItems.Count - 10)
                {
                    LoadNextBatch();
                }

                int nextIndex = Math.Min(ResultsList.SelectedIndex + 1, _displayedItems.Count - 1);
                ResultsList.SelectedIndex = nextIndex;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            if (_displayedItems.Count > 0)
            {
                int prevIndex = Math.Max(ResultsList.SelectedIndex - 1, 0);
                ResultsList.SelectedIndex = prevIndex;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (ResultsList.SelectedItem is SearchResultItem selected)
            {
                bool isCtrlOrAlt = Keyboard.IsKeyDown(Key.LeftCtrl) ||
                                  Keyboard.IsKeyDown(Key.RightCtrl) ||
                                  Keyboard.IsKeyDown(Key.LeftAlt) ||
                                  Keyboard.IsKeyDown(Key.RightAlt);

                if (isCtrlOrAlt)
                {
                    OpenContainingFolder(selected.FullPath);
                }
                else
                {
                    LaunchItem(selected.FullPath);
                }

                HideWindow();
                e.Handled = true;
            }
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultItem selected)
        {
            LaunchItem(selected.FullPath);
            HideWindow();
        }
    }

    private static void LaunchItem(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch:\n{ex.Message}", "Finder Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenContainingFolder(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open folder:\n{ex.Message}", "Finder Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private DispatcherTimer? _toastTimer;
    private string _savedStatusText = "";

    public void CopyPathToClipboard(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return;
            Clipboard.SetDataObject(path, true);
            ShowToastNotification($"📋 Path copied: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            ShowToastNotification($"⚠️ Copy failed: {ex.Message}");
        }
    }

    private void ShowToastNotification(string message)
    {
        if (_toastTimer == null)
        {
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                StatusText.Text = _savedStatusText;
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            };
        }

        _toastTimer.Stop();
        if (StatusText.Foreground is SolidColorBrush scb && scb.Color == Color.FromRgb(148, 163, 184))
        {
            _savedStatusText = StatusText.Text;
        }

        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        _toastTimer.Start();
    }

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SearchResultItem item)
        {
            CopyPathToClipboard(item.FullPath);
            e.Handled = true;
        }
    }

    private void ContextMenu_CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultItem selected)
        {
            CopyPathToClipboard(selected.FullPath);
        }
    }

    private void ContextMenu_OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultItem selected)
        {
            OpenContainingFolder(selected.FullPath);
        }
    }

    private void ContextMenu_Launch_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is SearchResultItem selected)
        {
            LaunchItem(selected.FullPath);
            HideWindow();
        }
    }

    private void ResultsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null)
        {
            item.IsSelected = true;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent)
                return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024)):F1} MB";
        return $"{(bytes / (1024.0 * 1024 * 1024)):F2} GB";
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    public void ExitApplication()
    {
        try
        {
            _trayIconManager.Dispose();
        }
        catch { }

        System.Windows.Application.Current.Shutdown();
    }

    private IntPtr WndProcTrayHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == TrayIconManager.WM_TRAYICON)
        {
            int lp = lParam.ToInt32() & 0xFFFF;
            if (lp == TrayIconManager.WM_LBUTTONUP || lp == TrayIconManager.WM_LBUTTONDBLCLK)
            {
                ShowWindow();
                handled = true;
            }
            else if (lp == TrayIconManager.WM_RBUTTONUP)
            {
                ShowTrayContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowTrayContextMenu()
    {
        var menu = new ContextMenu();
        var itemOpen = new MenuItem { Header = "🔍 Open Finder (Alt + Space)" };
        itemOpen.Click += (s, e) => ShowWindow();

        var itemReindex = new MenuItem { Header = "↻ Reindex All Files" };
        itemReindex.Click += (s, e) => StartManualReindex();

        var itemExit = new MenuItem { Header = "✕ Exit Finder" };
        itemExit.Click += (s, e) => ExitApplication();

        menu.Items.Add(itemOpen);
        menu.Items.Add(itemReindex);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemExit);

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _trayIconManager.Dispose();
        }
        catch { }

        _hotKeyManager.Dispose();
        base.OnClosed(e);
    }
}