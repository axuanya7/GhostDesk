using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Markup;
using System.Windows.Threading;

namespace GhostDesk
{
    internal class RegionControl : Border
    {
        private const double HiddenOpacity = 0.0;
        internal const string ItemDragFormat = "GhostDesk-ItemDrag";

        private readonly RegionConfig _model;
        private readonly Action<RegionControl> _removeCallback;
        private readonly Action _changeCallback;
        private readonly Action<DesktopItem> _openCallback;
        private readonly Canvas _dragLayer;
        private readonly Func<List<RegionConfig>> _launchTargetsProvider;
        private readonly Action<DesktopItem, string> _addToLaunchCallback;

        private WrapPanel _itemsHost;
        private ScrollViewer _itemsScroll;
        private Grid _rootGrid;
        private RowDefinition _firstRow;
        private RowDefinition _secondRow;
        private ColumnDefinition _firstColumn;
        private ColumnDefinition _secondColumn;
        private RowDefinition _headerRow;
        private RowDefinition _contentRow;
        private ColumnDefinition _headerColumn;
        private ColumnDefinition _contentColumn;
        private TextBlock _titleText;
        private Border _header;
        private Grid _headerGrid;
        private Border _headerDragArea;
        private TextBlock _collapseGlyph;
        private Button _collapseButton;
        private Button _launchButton;
        private Thumb _resizeGrip;
        private bool _moving;
        private bool _interactionActive;
        private bool _hoverActive;
        private Point _moveStart;
        private Point _regionStart;
        private Action<RegionControl> _moveStartedCallback;
        private Action<RegionControl, double, double> _moveCallback;
        private Action<RegionControl> _moveEndedCallback;
        private bool _hidden;
        private bool _opacityAnimationRunning;
        private int _opacityAnimationGeneration;
        private bool _collapsed;
        private double _expandedWidth;
        private double _expandedHeight;
        private bool _autoExpanded;
        private bool _pinnedExpanded;
        private Action<RegionControl> _layoutChangedCallback;
        private string _collapseHoverTrigger;
        private TextBox _renameBox;
        private DesktopItemControl _internalDragItem;
        private Border _internalDragGhost;
        private TranslateTransform _internalDragGhostMotion;
        private Point _internalDragGrabOffset;
        private bool _internalDragActive;
        private bool _oleFallbackPending;
        private bool _internalDragMouseCaptured;
        private bool _oleTransferQueued;
        private bool _oleTransferRunning;
        private bool _resumeCaptureFromOle;
        private int _internalDragSession;
        private long _lastOlePointerSampleTicks;
        private Point? _pendingReorder;
        private bool _reorderFrameQueued;
        private TimeSpan? _lastReorderRenderingTime;
        private List<DesktopItemControl> _dragPreviewOrder;
        private readonly List<string> _dragHiddenDesktopPaths = new List<string>();
        private readonly Dictionary<DesktopItemControl, Point> _itemTargetOffsets =
            new Dictionary<DesktopItemControl, Point>();
        private readonly Dictionary<DesktopItemControl, IconOffsetMotion> _itemOffsetMotions =
            new Dictionary<DesktopItemControl, IconOffsetMotion>();

        private sealed class IconOffsetMotion
        {
            public readonly TranslateTransform Transform;
            public Point Start;
            public Point Target;
            public long Started;
            public bool Active;

            public IconOffsetMotion(TranslateTransform transform)
            {
                Transform = transform;
            }
        }

        public event Action<RegionControl, bool> HoverChanged;
        public event Action<RegionControl, bool> InteractionChanged;

        internal class ItemDragPayload
        {
            public DesktopItem Item;
            public RegionControl Source;
            public DesktopItemControl Control;
            public bool Dropped;
        }

        public RegionConfig Model
        {
            get { return _model; }
        }

        public void SetMoveCallbacks(
            Action<RegionControl> started,
            Action<RegionControl, double, double> moved,
            Action<RegionControl> ended)
        {
            _moveStartedCallback = started;
            _moveCallback = moved;
            _moveEndedCallback = ended;
        }

        public void SetLayoutChangedCallback(Action<RegionControl> callback)
        {
            _layoutChangedCallback = callback;
        }

        public void RefreshDockLayout()
        {
            if (_header == null)
            {
                return;
            }

            UpdateDockChrome();
            if (_collapsed)
            {
                ApplyCollapsedDimensions();
            }
            else
            {
                Width = _expandedWidth;
                Height = _expandedHeight;
            }

            NotifyLayoutChanged();
        }

        public void ApplyPosition(double x, double y)
        {
            Canvas.SetLeft(this, x);
            Canvas.SetTop(this, y);
            _model.X = x;
            _model.Y = y;
        }

        public bool IsHidden
        {
            get { return _hidden; }
        }

        public bool IsOpacityAnimating
        {
            get { return _opacityAnimationRunning; }
        }

        public bool NeedsFastTimer
        {
            get
            {
                return _opacityAnimationRunning ||
                    !_hidden ||
                    Opacity > 0.001;
            }
        }

        public bool IsCollapsed
        {
            get { return _collapsed; }
        }

        public bool HasOpenContextMenu
        {
            get
            {
                ContextMenu menu = ContextMenu;
                if (menu != null && menu.IsOpen)
                {
                    return true;
                }

                foreach (UIElement child in _itemsHost.Children)
                {
                    DesktopItemControl item = child as DesktopItemControl;
                    if (item != null && item.ContextMenu != null && item.ContextMenu.IsOpen)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool IsRenaming
        {
            get { return _renameBox != null; }
        }

        public bool IsInternalDragActive
        {
            get { return _internalDragActive; }
        }

        public RegionControl(
            RegionConfig model,
            Action<RegionControl> removeCallback,
            Action changeCallback,
            Action<DesktopItem> openCallback,
            Canvas dragLayer,
            string collapseHoverTrigger,
            Func<List<RegionConfig>> launchTargetsProvider,
            Action<DesktopItem, string> addToLaunchCallback)
        {
            _model = model;
            _removeCallback = removeCallback;
            _changeCallback = changeCallback;
            _openCallback = openCallback;
            _dragLayer = dragLayer;
            _launchTargetsProvider = launchTargetsProvider;
            _addToLaunchCallback = addToLaunchCallback;
            SetCollapseHoverTrigger(collapseHoverTrigger);

            Width = model.Width;
            Height = model.Height;
            _expandedWidth = model.Width;
            _expandedHeight = model.Height;
            Canvas.SetLeft(this, model.X);
            Canvas.SetTop(this, model.Y);

            AllowDrop = true;
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            Focusable = false;
            FocusVisualStyle = null;
            Opacity = 1.0;
            Background = CreateGlassBrush();
            BorderBrush = CreateGlassBorderBrush();
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(16);
            Padding = new Thickness(7);
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 0,
                Opacity = 0.32
            };

            BuildUi();
            UpdateDockChrome();
        }

        public void SetCollapseHoverTrigger(string value)
        {
            if (!string.Equals(
                    value,
                    AppConfig.CollapseHoverRow,
                    StringComparison.OrdinalIgnoreCase))
            {
                _collapseHoverTrigger = AppConfig.CollapseHoverButton;
            }
            else
            {
                _collapseHoverTrigger = AppConfig.CollapseHoverRow;
            }
        }

        public void RefreshItems()
        {
            _itemsHost.Children.Clear();
            bool showExtensions = ShouldShowExtensions();
            foreach (DesktopItem item in _model.Items)
            {
                DesktopItemControl control = new DesktopItemControl(
                    item,
                    _openCallback,
                    RemoveItem,
                    ShowItemLocation,
                    ItemChanged,
                    _addToLaunchCallback,
                    _launchTargetsProvider,
                    showExtensions);
                control.InteractionStarted += delegate { SetInteraction(true); };
                control.InteractionEnded += delegate { SetInteraction(false); };
                _itemsHost.Children.Add(control);
            }
        }

        private void ItemChanged()
        {
            RefreshItems();
            NotifyChanged();
        }

        public void AddPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string normalized = Path.GetFullPath(path);
            bool desktopPath = IsDesktopPath(normalized);

            foreach (DesktopItem existing in _model.Items)
            {
                if (string.Equals(existing.Path, normalized, StringComparison.OrdinalIgnoreCase) ||
                    (!desktopPath && DesktopItemControl.IsShortcutPointingToPath(existing.Path, normalized)))
                {
                    return;
                }
            }

            if (!desktopPath &&
                !DesktopItemControl.IsShortcutPath(normalized) &&
                (File.Exists(normalized) || Directory.Exists(normalized)))
            {
                try
                {
                    normalized = DesktopItemControl.CreateManagedShortcutForExternalPath(normalized);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "创建快捷方式失败：\n" + ex.Message,
                        "GhostDesk",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            string type = "file";
            string name = Path.GetFileName(normalized);
            if (Directory.Exists(normalized))
            {
                type = "folder";
                name = Path.GetFileName(normalized.TrimEnd('\\'));
            }
            else if (DesktopItemControl.IsShortcutPath(normalized))
            {
                type = "shortcut";
                name = Path.GetFileNameWithoutExtension(normalized);
            }

            if (desktopPath)
            {
                try
                {
                    SetDesktopItemVisibility(normalized, false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "桌面图标隐藏失败：\n" + ex.Message,
                        "GhostDesk",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            _model.Items.Add(new DesktopItem
            {
                Type = type,
                Path = normalized,
                Name = string.IsNullOrEmpty(name) ? "未命名" : name,
                ExternalReference = !desktopPath && !DesktopItemControl.IsShortcutPath(normalized)
            });

            RefreshItems();
            NotifyChanged();
        }

        public void SetHidden(bool hidden, double fadeSeconds)
        {
            double target = hidden ? HiddenOpacity : 1.0;
            if (_hidden == hidden)
            {
                if (_opacityAnimationRunning ||
                    Math.Abs(Opacity - target) < 0.001)
                {
                    return;
                }
            }

            int generation = ++_opacityAnimationGeneration;
            BeginAnimation(OpacityProperty, null);

            _hidden = hidden;
            IsHitTestVisible = !hidden;
            _opacityAnimationRunning = true;

            DoubleAnimation animation = new DoubleAnimation(
                target,
                TimeSpan.FromSeconds(fadeSeconds));
            animation.FillBehavior = FillBehavior.HoldEnd;
            animation.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            animation.Completed += delegate
            {
                if (generation != _opacityAnimationGeneration)
                {
                    return;
                }

                BeginAnimation(OpacityProperty, null);
                Opacity = target;
                _opacityAnimationRunning = false;
            };
            BeginAnimation(OpacityProperty, animation);
        }

        private void BuildUi()
        {
            _rootGrid = new Grid();
            _firstRow = new RowDefinition { Height = GridLength.Auto };
            _secondRow = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
            _firstColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            _secondColumn = new ColumnDefinition { Width = new GridLength(0) };
            _headerRow = _firstRow;
            _contentRow = new RowDefinition { Height = new GridLength(1, GridUnitType.Star) };
            _headerColumn = _firstColumn;
            _contentColumn = _secondColumn;
            _rootGrid.RowDefinitions.Add(_firstRow);
            _rootGrid.RowDefinitions.Add(_secondRow);
            _rootGrid.ColumnDefinitions.Add(_firstColumn);
            _rootGrid.ColumnDefinitions.Add(_secondColumn);

            _header = new Border();
            _header.Height = 34;
            _header.Margin = new Thickness(0, 0, 0, 6);
            _header.Padding = new Thickness(9, 0, 9, 0);
            _header.Background = CreateBrush(Color.FromArgb(24, 255, 255, 255));
            _header.CornerRadius = new CornerRadius(7);
            _header.Cursor = Cursors.SizeAll;
            _header.MouseLeftButtonDown += Header_MouseLeftButtonDown;
            _header.MouseLeftButtonUp += Header_MouseLeftButtonUp;
            _header.MouseMove += Header_MouseMove;
            _header.MouseEnter += Header_MouseEnter;
            _header.MouseLeave += Child_MouseLeave;

            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _titleText = new TextBlock();
            _titleText.Text = _model.Name;
            _titleText.FontSize = 12;
            _titleText.FontWeight = FontWeights.SemiBold;
            _titleText.Foreground = CreateBrush(Color.FromArgb(245, 255, 255, 255));
            _titleText.VerticalAlignment = VerticalAlignment.Center;
            _titleText.HorizontalAlignment = HorizontalAlignment.Left;
            _titleText.MinWidth = 72;
            _titleText.MaxWidth = 260;
            _titleText.Padding = new Thickness(0, 0, 10, 0);
            _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
            _titleText.Cursor = Cursors.SizeAll;
            _titleText.MouseLeftButtonDown += Title_MouseLeftButtonDown;
            Grid.SetColumn(_titleText, 0);
            _headerGrid = headerGrid;
            headerGrid.Children.Add(_titleText);

            _headerDragArea = new Border();
            _headerDragArea.Background = Brushes.Transparent;
            _headerDragArea.HorizontalAlignment = HorizontalAlignment.Stretch;
            _headerDragArea.VerticalAlignment = VerticalAlignment.Stretch;
            _headerDragArea.Cursor = Cursors.SizeAll;
            _headerDragArea.MouseLeftButtonDown += Header_MouseLeftButtonDown;
            Grid.SetColumn(_headerDragArea, 1);
            headerGrid.Children.Add(_headerDragArea);

            _launchButton = new Button();
            _launchButton.Width = 26;
            _launchButton.Height = 26;
            _launchButton.Margin = new Thickness(5, 0, 2, 0);
            _launchButton.Padding = new Thickness(0);
            _launchButton.Focusable = false;
            _launchButton.IsTabStop = false;
            _launchButton.FocusVisualStyle = null;
            _launchButton.Cursor = Cursors.Hand;
            _launchButton.Background = Brushes.Transparent;
            _launchButton.BorderThickness = new Thickness(0);
            _launchButton.ToolTip = "一键启动";

            TextBlock launchGlyph = new TextBlock();
            launchGlyph.Text = "▶";
            launchGlyph.FontSize = 11;
            launchGlyph.Foreground = CreateBrush(Color.FromArgb(230, 255, 255, 255));
            launchGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            launchGlyph.VerticalAlignment = VerticalAlignment.Center;
            _launchButton.Content = launchGlyph;
            _launchButton.MouseEnter += LaunchButton_MouseEnter;
            _launchButton.MouseLeave += LaunchButton_MouseLeave;
            _launchButton.Click += LaunchButton_Click;
            Grid.SetColumn(_launchButton, 2);
            headerGrid.Children.Add(_launchButton);

            _collapseButton = new Button();
            _collapseButton.Width = 26;
            _collapseButton.Height = 26;
            _collapseButton.Margin = new Thickness(2, 0, 2, 0);
            _collapseButton.Padding = new Thickness(0);
            _collapseButton.Focusable = false;
            _collapseButton.IsTabStop = false;
            _collapseButton.FocusVisualStyle = null;
            _collapseButton.Cursor = Cursors.Hand;
            _collapseButton.Background = Brushes.Transparent;
            _collapseButton.BorderThickness = new Thickness(0);
            _collapseButton.ToolTip = null;

            _collapseGlyph = new TextBlock();
            _collapseGlyph.Text = "▾";
            _collapseGlyph.FontSize = 13;
            _collapseGlyph.Foreground = CreateBrush(Color.FromArgb(220, 255, 255, 255));
            _collapseGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            _collapseGlyph.VerticalAlignment = VerticalAlignment.Center;
            _collapseButton.Content = _collapseGlyph;
            _collapseButton.MouseEnter += CollapseButton_MouseEnter;
            _collapseButton.MouseLeave += CollapseButton_MouseLeave;
            _collapseButton.Click += CollapseButton_Click;
            Grid.SetColumn(_collapseButton, 3);
            headerGrid.Children.Add(_collapseButton);

            _header.Child = headerGrid;
            Grid.SetRow(_header, 0);
            Grid.SetColumn(_header, 0);
            Grid.SetColumnSpan(_header, 2);
            _rootGrid.Children.Add(_header);

            ScrollViewer scroll = new ScrollViewer();
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.CanContentScroll = false;
            scroll.Padding = new Thickness(1);
            scroll.Focusable = false;
            scroll.IsTabStop = false;
            scroll.FocusVisualStyle = null;
            Grid.SetRow(scroll, 1);
            Grid.SetColumn(scroll, 0);
            Grid.SetColumnSpan(scroll, 2);
            _itemsScroll = scroll;

            _itemsHost = new WrapPanel();
            _itemsHost.Orientation = Orientation.Horizontal;
            _itemsHost.Margin = new Thickness(2);
            _itemsHost.Focusable = false;
            _itemsHost.FocusVisualStyle = null;
            scroll.Content = _itemsHost;
            _rootGrid.Children.Add(scroll);

            _resizeGrip = new Thumb();
            _resizeGrip.Width = 14;
            _resizeGrip.Height = 14;
            _resizeGrip.HorizontalAlignment = HorizontalAlignment.Right;
            _resizeGrip.VerticalAlignment = VerticalAlignment.Bottom;
            _resizeGrip.Margin = new Thickness(0, 0, 1, 1);
            _resizeGrip.Cursor = Cursors.SizeNWSE;
            _resizeGrip.Background = Brushes.Transparent;
            _resizeGrip.Opacity = 0.55;
            _resizeGrip.Focusable = false;
            _resizeGrip.IsTabStop = false;
            _resizeGrip.FocusVisualStyle = null;
            _resizeGrip.DragStarted += Resize_DragStarted;
            _resizeGrip.DragDelta += Resize_DragDelta;
            _resizeGrip.DragCompleted += Resize_DragCompleted;
            Grid.SetRow(_resizeGrip, 1);
            Grid.SetColumn(_resizeGrip, 0);
            Grid.SetColumnSpan(_resizeGrip, 2);
            _rootGrid.Children.Add(_resizeGrip);

            Child = _rootGrid;
            ContextMenu = BuildRegionMenu();
            RefreshLaunchButton();
            RefreshItems();

            DragOver += Region_DragOver;
            DragLeave += Region_DragLeave;
            Drop += Region_Drop;
            MouseEnter += Region_MouseEnter;
            MouseLeave += Region_MouseLeave;
            ContextMenuOpening += Region_ContextMenuOpening;
            ContextMenuClosing += Region_ContextMenuClosing;
        }

        private void Region_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            SetInteraction(true);
        }

        private void Region_ContextMenuClosing(object sender, ContextMenuEventArgs e)
        {
            SetInteraction(false);
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(delegate
                {
                    if (_autoExpanded &&
                        !_pinnedExpanded &&
                        !IsMouseOver &&
                        !HasOpenContextMenu)
                    {
                        CollapseTemporary();
                    }
                }));
        }

        private ContextMenu BuildRegionMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.StaysOpen = false;
            GlassTheme.ApplyContextMenuStyle(menu);

            MenuItem addFile = new MenuItem { Header = "添加文件或快捷方式..." };
            addFile.Click += AddFile_Click;
            menu.Items.Add(addFile);

            MenuItem create = new MenuItem { Header = "新建" };
            MenuItem createFolder = new MenuItem { Header = "文件夹" };
            createFolder.Click += NewFolder_Click;
            create.Items.Add(createFolder);

            MenuItem createTextDocument = new MenuItem { Header = "文本文档" };
            createTextDocument.Click += NewTextDocument_Click;
            create.Items.Add(createTextDocument);
            menu.Items.Add(create);

            menu.Items.Add(new Separator());

            MenuItem rename = new MenuItem { Header = "重命名" };
            rename.Click += Rename_Click;
            menu.Items.Add(rename);

            MenuItem remove = new MenuItem { Header = "删除分区" };
            remove.Click += Remove_Click;
            menu.Items.Add(remove);

            return menu;
        }

        private void ShowExtensions_Click(object sender, RoutedEventArgs e)
        {
            MenuItem item = sender as MenuItem;
            if (item == null)
            {
                return;
            }

            _model.ShowExtensions = item.IsChecked ? "true" : "false";
            RefreshItems();
            NotifyChanged();
        }

        private void Region_DragLeave(object sender, DragEventArgs e)
        {
            RestoreDragHiddenDesktopFiles();
        }

        private void HideDraggedDesktopFiles(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null)
            {
                return;
            }

            foreach (string file in files)
            {
                if (string.IsNullOrEmpty(file) || !IsDesktopPath(file) ||
                    (!File.Exists(file) && !Directory.Exists(file)) ||
                    _dragHiddenDesktopPaths.Contains(file))
                {
                    continue;
                }

                try
                {
                    SetDesktopItemVisibility(file, false);
                    _dragHiddenDesktopPaths.Add(file);
                }
                catch
                {
                }
            }
        }

        private void RestoreDragHiddenDesktopFiles()
        {
            for (int i = _dragHiddenDesktopPaths.Count - 1; i >= 0; i--)
            {
                string path = _dragHiddenDesktopPaths[i];
                try
                {
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        SetDesktopItemVisibility(path, true);
                    }
                }
                catch
                {
                }
            }

            _dragHiddenDesktopPaths.Clear();
        }

        private void AddFile_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Title = "选择文件或快捷方式";
            dialog.Filter = "所有文件 (*.*)|*.*";
            dialog.Multiselect = true;
            Window owner = Window.GetWindow(this);
            bool? result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (result == true)
            {
                foreach (string file in dialog.FileNames)
                {
                    AddPath(file);
                }
            }
        }

        private void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            CreateDesktopItem("新建文件夹", "", true);
        }

        private void NewTextDocument_Click(object sender, RoutedEventArgs e)
        {
            CreateDesktopItem("新建文本文档", ".txt", false);
        }

        private void CreateDesktopItem(string baseName, string extension, bool isFolder)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
            {
                MessageBox.Show(
                    "无法找到当前用户的桌面文件夹。",
                    "GhostDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            for (int index = 1; index <= 9999; index++)
            {
                string fileName = index == 1
                    ? baseName + extension
                    : string.Format("{0} ({1}){2}", baseName, index, extension);
                string path = Path.Combine(desktop, fileName);

                if (File.Exists(path) || Directory.Exists(path))
                {
                    continue;
                }

                try
                {
                    if (isFolder)
                    {
                        Directory.CreateDirectory(path);
                    }
                    else
                    {
                        using (FileStream stream = new FileStream(
                            path,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None))
                        {
                        }
                    }

                    AddPath(path);
                    return;
                }
                catch (Exception ex)
                {
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        continue;
                    }

                    MessageBox.Show(
                        "新建项目失败：\n" + ex.Message,
                        "GhostDesk",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            MessageBox.Show(
                "无法为新建项目生成可用名称。",
                "GhostDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            BeginRename();
        }

        private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                BeginRename();
                e.Handled = true;
            }
            else
            {
                BeginHeaderDrag(e);
            }
        }

        private void BeginRename()
        {
            if (_renameBox != null)
            {
                return;
            }

            DesktopWindow owner = Window.GetWindow(this) as DesktopWindow;
            if (owner != null)
            {
                owner.SetKeyboardEditing(true);
            }
            SetInteraction(true);
            _titleText.Visibility = Visibility.Collapsed;

            _renameBox = new TextBox();
            _renameBox.Text = _model.Name;
            _renameBox.FontSize = 12;
            _renameBox.VerticalContentAlignment = VerticalAlignment.Center;
            _renameBox.Background = CreateBrush(Color.FromArgb(46, 255, 255, 255));
            _renameBox.Foreground = Brushes.White;
            _renameBox.BorderBrush = CreateBrush(Color.FromArgb(180, 210, 232, 255));
            _renameBox.CaretBrush = Brushes.White;
            _renameBox.Margin = new Thickness(0, 2, 8, 2);
            _renameBox.KeyDown += RenameBox_KeyDown;
            _renameBox.LostFocus += RenameBox_LostFocus;
            _renameBox.PreviewMouseLeftButtonDown += RenameBox_PreviewMouseLeftButtonDown;
            _renameBox.Loaded += delegate
            {
                FocusRenameBox();
                _renameBox.SelectAll();
            };

            Grid.SetColumn(_renameBox, 0);
            _headerGrid.Children.Add(_renameBox);
        }

        private void FocusRenameBox()
        {
            if (_renameBox == null)
            {
                return;
            }

            _renameBox.Focusable = true;
            _renameBox.IsTabStop = true;
            Keyboard.Focus(_renameBox);
            Window owner = Window.GetWindow(this);
            if (owner != null)
            {
                FocusManager.SetFocusedElement(owner, _renameBox);
            }

            HwndSource source = PresentationSource.FromVisual(_renameBox) as HwndSource;
            if (source != null && source.Handle != IntPtr.Zero)
            {
                NativeMethods.SetFocus(source.Handle);
            }

            _renameBox.Focus();
            _renameBox.CaretIndex = _renameBox.Text.Length;
        }

        private void RenameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitRename();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelRename();
                e.Handled = true;
            }
        }

        private void CommitRename()
        {
            if (_renameBox == null)
            {
                return;
            }

            string value = _renameBox.Text;
            EndRename();
            if (!string.IsNullOrWhiteSpace(value))
            {
                value = value.Trim();
                if (value != _model.Name)
                {
                    _model.Name = value;
                    _titleText.Text = value;
                    NotifyChanged();
                }
            }
        }

        private void CancelRename()
        {
            EndRename();
        }

        private void EndRename()
        {
            if (_renameBox == null)
            {
                return;
            }

            TextBox box = _renameBox;
            _renameBox = null;
            box.LostFocus -= RenameBox_LostFocus;
            box.KeyDown -= RenameBox_KeyDown;
            box.PreviewMouseLeftButtonDown -= RenameBox_PreviewMouseLeftButtonDown;
            _headerGrid.Children.Remove(box);
            _titleText.Visibility = Visibility.Visible;
            SetInteraction(false);
            DesktopWindow owner = Window.GetWindow(this) as DesktopWindow;
            if (owner != null)
            {
                owner.SetKeyboardEditing(false);
            }
            if (!IsMouseOver)
            {
                SetHover(false);
            }
        }

        private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitRename();
        }

        private void RenameBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_renameBox != null && !_renameBox.IsKeyboardFocusWithin)
            {
                _renameBox.Focus();
                _renameBox.CaretIndex = _renameBox.Text.Length;
            }
        }

        public void ReleaseInteractionState()
        {
            if (_renameBox != null)
            {
                CancelRename();
            }

            if (_internalDragActive)
            {
                EndInternalDrag();
            }

            if (_moving)
            {
                _moving = false;
                _header.ReleaseMouseCapture();
                Mouse.OverrideCursor = null;
            }

            CloseOpenContextMenus();

            SetInteraction(false);
            SetHover(false);
        }

        public void CloseOpenContextMenus()
        {
            ContextMenu menu = ContextMenu;
            if (menu != null && menu.IsOpen)
            {
                menu.IsOpen = false;
            }

            foreach (UIElement child in _itemsHost.Children)
            {
                DesktopItemControl item = child as DesktopItemControl;
                if (item != null && item.ContextMenu != null && item.ContextMenu.IsOpen)
                {
                    item.ContextMenu.IsOpen = false;
                }
            }
        }

        public void CommitActiveRename()
        {
            if (_renameBox != null)
            {
                CommitRename();
            }
        }

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_collapsed)
            {
                _autoExpanded = false;
                _pinnedExpanded = true;
                SetCollapsed(false);
            }
            else if (_autoExpanded)
            {
                _autoExpanded = false;
                _pinnedExpanded = true;
            }
            else
            {
                _autoExpanded = false;
                _pinnedExpanded = false;
                SetCollapsed(true);
            }
        }

        private void CollapseButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_collapsed)
            {
                _autoExpanded = true;
                SetCollapsed(false);
            }
        }

        private void CollapseButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_autoExpanded && !IsMouseOver)
            {
                CollapseTemporary();
            }
        }

        private void Header_MouseEnter(object sender, MouseEventArgs e)
        {
            SetHover(true);
            if (_collapsed &&
                string.Equals(
                    _collapseHoverTrigger,
                    AppConfig.CollapseHoverRow,
                    StringComparison.Ordinal))
            {
                _autoExpanded = true;
                SetCollapsed(false);
            }
        }

        private void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed)
            {
                return;
            }

            if (!_collapsed)
            {
                _expandedWidth = Math.Max(96.0, Width);
                _expandedHeight = Math.Max(72.0, Height);
            }

            _collapsed = collapsed;
            if (collapsed)
            {
                _autoExpanded = false;
                UpdateDockChrome();
                ApplyCollapsedDimensions();
            }
            else
            {
                UpdateDockChrome();
                Width = _expandedWidth;
                Height = _expandedHeight;
            }

            RefreshItems();
            NotifyLayoutChanged();
        }

        private void ApplyCollapsedDimensions()
        {
            if (IsVerticalDock())
            {
                Width = 44.0;
                Height = Math.Max(72.0, _expandedHeight);
                return;
            }

            Width = Math.Max(96.0, _expandedWidth);
            double headerHeight = _header.ActualHeight > 1.0
                ? _header.ActualHeight
                : 34.0;
            Height = Math.Ceiling(
                headerHeight +
                Padding.Top +
                Padding.Bottom +
                BorderThickness.Top +
                BorderThickness.Bottom +
                6.0);
        }

        private bool IsVerticalDock()
        {
            return string.Equals(
                       _model.DockEdge,
                       RegionConfig.DockLeft,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       _model.DockEdge,
                       RegionConfig.DockRight,
                       StringComparison.Ordinal);
        }

        private void UpdateDockChrome()
        {
            if (_header == null ||
                _collapseGlyph == null ||
                _rootGrid == null ||
                _headerGrid == null)
            {
                return;
            }

            string edge = _model.DockEdge ?? RegionConfig.DockNone;
            bool vertical = IsVerticalDock();
            bool bottom =
                string.Equals(edge, RegionConfig.DockBottom, StringComparison.Ordinal);
            bool right =
                string.Equals(edge, RegionConfig.DockRight, StringComparison.Ordinal);
            bool verticalCollapsed = _collapsed && vertical;
            Visibility expandedContentVisibility = verticalCollapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            ConfigureDockStructure(vertical, bottom, right, verticalCollapsed);
            _titleText.Visibility = expandedContentVisibility;
            _launchButton.Visibility = !verticalCollapsed && _model.HasValidLaunchItems()
                ? Visibility.Visible
                : Visibility.Collapsed;
            _resizeGrip.Visibility = _collapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (verticalCollapsed)
            {
                _header.Padding = new Thickness(0);
                Grid.SetColumn(_headerDragArea, 0);
                Grid.SetColumnSpan(_headerDragArea, 4);
            }
            else
            {
                _header.Padding = vertical
                    ? new Thickness(0)
                    : new Thickness(9, 0, 9, 0);
                Grid.SetColumn(_headerDragArea, 1);
                Grid.SetColumnSpan(_headerDragArea, 1);
            }

            double headerRotation = vertical ? (right ? 90.0 : -90.0) : 0.0;
            _headerGrid.LayoutTransform = vertical
                ? new RotateTransform(headerRotation)
                : Transform.Identity;
            SetCounterRotation(_launchButton, vertical ? -headerRotation : 0.0);
            SetCounterRotation(_collapseButton, vertical ? -headerRotation : 0.0);

            if (!_collapsed)
            {
                if (string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal))
                {
                    _collapseGlyph.Text = "▴";
                }
                else if (string.Equals(edge, RegionConfig.DockBottom, StringComparison.Ordinal))
                {
                    _collapseGlyph.Text = "▾";
                }
                else if (string.Equals(edge, RegionConfig.DockLeft, StringComparison.Ordinal))
                {
                    _collapseGlyph.Text = "◂";
                }
                else if (string.Equals(edge, RegionConfig.DockRight, StringComparison.Ordinal))
                {
                    _collapseGlyph.Text = "▸";
                }
                else
                {
                    _collapseGlyph.Text = "▾";
                }
            }
            else
            {
                if (string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal))
                {
                    _collapseGlyph.Text = "▾";
                }
                else if (string.Equals(edge, RegionConfig.DockBottom, StringComparison.Ordinal))
                {
                    _collapseGlyph.Text = "▴";
                }
                else if (string.Equals(edge, RegionConfig.DockLeft, StringComparison.Ordinal))
                {
                    _collapseGlyph.Text = "▸";
                }
                else if (string.Equals(edge, RegionConfig.DockRight, StringComparison.Ordinal))
                {
                    _collapseGlyph.Text = "◂";
                }
                else
                {
                    _collapseGlyph.Text = "▸";
                }
            }
        }

        private void ConfigureDockStructure(
            bool vertical,
            bool bottom,
            bool right,
            bool verticalCollapsed)
        {
            Padding = vertical
                ? new Thickness(4)
                : new Thickness(7);
            _itemsScroll.Visibility = _collapsed
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (vertical)
            {
                _headerRow = _firstRow;
                _contentRow = _firstRow;
                _firstRow.Height = new GridLength(1, GridUnitType.Star);
                _secondRow.Height = new GridLength(0);

                _headerColumn = right ? _secondColumn : _firstColumn;
                _contentColumn = right ? _firstColumn : _secondColumn;
                _headerColumn.Width = new GridLength(32);
                _contentColumn.Width = verticalCollapsed
                    ? new GridLength(0)
                    : new GridLength(1, GridUnitType.Star);

                Grid.SetRow(_header, 0);
                Grid.SetRowSpan(_header, 2);
                Grid.SetColumn(_header, right ? 1 : 0);
                Grid.SetColumnSpan(_header, 1);
                _header.Width = double.NaN;
                _header.Height = double.NaN;
                _header.Margin = right
                    ? new Thickness(6, 0, 0, 0)
                    : new Thickness(0, 0, 6, 0);

                PlaceGridChild(_itemsScroll, 0, right ? 0 : 1, 2, 1);
                PlaceGridChild(_resizeGrip, 0, right ? 0 : 1, 2, 1);
                ConfigureResizeGrip(vertical, bottom, right);
                return;
            }

            _headerColumn = _firstColumn;
            _contentColumn = _secondColumn;
            _firstColumn.Width = new GridLength(1, GridUnitType.Star);
            _secondColumn.Width = new GridLength(0);
            _headerRow = bottom ? _secondRow : _firstRow;
            _contentRow = bottom ? _firstRow : _secondRow;
            _headerRow.Height = GridLength.Auto;
            _contentRow.Height = _collapsed
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);

            Grid.SetRow(_header, bottom ? 1 : 0);
            Grid.SetRowSpan(_header, 1);
            Grid.SetColumn(_header, 0);
            Grid.SetColumnSpan(_header, 2);
            _header.Width = double.NaN;
            _header.Height = 34.0;
            _header.Margin = bottom
                ? new Thickness(0, 6, 0, 0)
                : new Thickness(0, 0, 0, 6);

            PlaceGridChild(_itemsScroll, bottom ? 0 : 1, 0, 1, 2);
            PlaceGridChild(_resizeGrip, bottom ? 0 : 1, 0, 1, 2);
            ConfigureResizeGrip(vertical, bottom, right);
        }

        private void ConfigureResizeGrip(bool vertical, bool bottom, bool right)
        {
            bool alignLeft = vertical && right;
            bool alignTop = !vertical && bottom;

            _resizeGrip.HorizontalAlignment = alignLeft
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right;
            _resizeGrip.VerticalAlignment = alignTop
                ? VerticalAlignment.Top
                : VerticalAlignment.Bottom;
            _resizeGrip.Margin = new Thickness(
                alignLeft ? 1 : 0,
                alignTop ? 1 : 0,
                alignLeft ? 0 : 1,
                alignTop ? 0 : 1);
            _resizeGrip.Cursor = alignLeft || alignTop
                ? Cursors.SizeNESW
                : Cursors.SizeNWSE;
        }

        private static void PlaceGridChild(
            UIElement child,
            int row,
            int column,
            int rowSpan,
            int columnSpan)
        {
            if (child == null)
            {
                return;
            }

            Grid.SetRow(child, row);
            Grid.SetRowSpan(child, rowSpan);
            Grid.SetColumn(child, column);
            Grid.SetColumnSpan(child, columnSpan);
        }

        private static void SetCounterRotation(FrameworkElement element, double angle)
        {
            if (element == null)
            {
                return;
            }

            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = Math.Abs(angle) < 0.01
                ? Transform.Identity
                : new RotateTransform(angle);
        }

        private void NotifyLayoutChanged()
        {
            if (_layoutChangedCallback != null)
            {
                _layoutChangedCallback(this);
            }
        }

        private void CollapseTemporary()
        {
            _autoExpanded = false;
            _pinnedExpanded = false;
            SetCollapsed(true);
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            DesktopWindow owner = Window.GetWindow(this) as DesktopWindow;
            if (owner != null)
            {
                owner.RequestLaunch(_model.Id);
            }
        }

        private void LaunchButton_MouseEnter(object sender, MouseEventArgs e)
        {
            SetInteraction(true);
        }

        private void LaunchButton_MouseLeave(object sender, MouseEventArgs e)
        {
            SetInteraction(false);
        }

        public void AddItemToLaunch(DesktopItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path))
            {
                return;
            }

            if (_model.LaunchItems == null)
            {
                _model.LaunchItems = new List<DesktopItem>();
            }

            foreach (DesktopItem existing in _model.LaunchItems)
            {
                if (existing != null &&
                    string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            _model.LaunchItems.Add(CloneItem(item));
            RefreshLaunchButton();
            NotifyChanged();
        }

        public void RefreshLaunchButton()
        {
            if (_launchButton == null)
            {
                return;
            }

            int count = _model.GetValidLaunchItemCount();
            _launchButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _launchButton.ToolTip = count > 0 ? "一键启动（" + count + " 项）" : "一键启动";
        }

        private static DesktopItem CloneItem(DesktopItem item)
        {
            if (item == null)
            {
                return null;
            }

            return new DesktopItem
            {
                Type = item.Type,
                Path = item.Path,
                Name = item.Name,
                Aliases = item.Aliases,
                IconPath = item.IconPath,
                ExternalReference = item.ExternalReference
            };
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (_removeCallback != null)
            {
                _removeCallback(this);
            }
        }

        private void Region_DragOver(object sender, DragEventArgs e)
        {
            ItemDragPayload payload = e.Data.GetData(ItemDragFormat) as ItemDragPayload;
            if (payload != null && payload.Item != null)
            {
                e.Effects = DragDropEffects.Move;
                if (payload.Source == this)
                {
                    PreviewReorder(e.GetPosition(_itemsHost), payload.Control);
                }

                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                HideDraggedDesktopFiles(e);
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Region_Drop(object sender, DragEventArgs e)
        {
            ItemDragPayload payload = e.Data.GetData(ItemDragFormat) as ItemDragPayload;
            if (payload != null && payload.Item != null)
            {
                payload.Dropped = true;
                if (payload.Source == this)
                {
                    PreviewReorder(e.GetPosition(_itemsHost), payload.Control);
                    CommitVisualOrder();
                }
                else if (payload.Source != null)
                {
                    if (AddTransferredItem(payload.Item))
                    {
                        payload.Source.RemoveItemForRegionMove(payload.Item);
                    }
                }

                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null)
            {
                return;
            }

            foreach (string file in files)
            {
                AddPath(file);
            }
            _dragHiddenDesktopPaths.Clear();
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void QueueReorderPreview(Point point)
        {
            if (!_internalDragActive || _internalDragItem == null)
            {
                return;
            }

            _pendingReorder = point;
            EnsureReorderFrame();
        }

        private void EnsureReorderFrame()
        {
            if (_reorderFrameQueued)
            {
                return;
            }

            _reorderFrameQueued = true;
            CompositionTarget.Rendering += ReorderRendering;
        }

        private void ReorderRendering(object sender, EventArgs e)
        {
            RenderingEventArgs frame = e as RenderingEventArgs;
            if (frame != null)
            {
                if (_lastReorderRenderingTime == frame.RenderingTime)
                {
                    return;
                }

                _lastReorderRenderingTime = frame.RenderingTime;
            }

            AdvanceIconMotions();

            if (_internalDragActive && _pendingReorder.HasValue)
            {
                Point point = _pendingReorder.Value;
                _pendingReorder = null;
                PreviewReorder(point, _internalDragItem);
            }

            if (!_internalDragActive && !HasActiveIconMotions())
            {
                CancelReorderFrame();
            }
        }

        private void CancelReorderFrame()
        {
            if (_reorderFrameQueued)
            {
                CompositionTarget.Rendering -= ReorderRendering;
            }

            _reorderFrameQueued = false;
            _pendingReorder = null;
            _lastReorderRenderingTime = null;
        }

        private void PreviewReorder(Point point, DesktopItemControl dragged)
        {
            if (dragged == null || !_itemsHost.Children.Contains(dragged))
            {
                return;
            }

            if (_dragPreviewOrder == null)
            {
                _dragPreviewOrder = new List<DesktopItemControl>(_itemsHost.Children.Count);
                foreach (DesktopItemControl item in _itemsHost.Children)
                {
                    _dragPreviewOrder.Add(item);
                }
            }

            int targetIndex = FindPreviewIndex(point);
            int currentIndex = _dragPreviewOrder.IndexOf(dragged);
            if (targetIndex == currentIndex)
            {
                return;
            }

            if (targetIndex < 0)
            {
                targetIndex = 0;
            }
            if (targetIndex > _dragPreviewOrder.Count)
            {
                targetIndex = _dragPreviewOrder.Count;
            }

            List<DesktopItemControl> nextOrder = new List<DesktopItemControl>(_dragPreviewOrder);
            nextOrder.Remove(dragged);
            if (targetIndex > nextOrder.Count)
            {
                targetIndex = nextOrder.Count;
            }
            nextOrder.Insert(targetIndex, dragged);
            _dragPreviewOrder = nextOrder;

            Dictionary<DesktopItemControl, Point> anchors = CaptureBaseItemPositions();
            foreach (DesktopItemControl item in nextOrder)
            {
                if (ReferenceEquals(item, dragged) || !anchors.ContainsKey(item))
                {
                    continue;
                }

                int anchorIndex = _itemsHost.Children.IndexOf(item);
                int previewIndex = nextOrder.IndexOf(item);
                if (anchorIndex < 0 || previewIndex < 0 || anchorIndex == previewIndex)
                {
                    Point zero = new Point(0.0, 0.0);
                    MoveIconToOffset(item, zero.X, zero.Y);
                    continue;
                }

                double cellWidth = GetItemCellWidth(item);
                double cellHeight = GetItemCellHeight(item);
                int columns = Math.Max(1, (int)Math.Floor(_itemsHost.ActualWidth / cellWidth));
                int anchorColumn = anchorIndex % columns;
                int previewColumn = previewIndex % columns;
                int anchorRow = anchorIndex / columns;
                int previewRow = previewIndex / columns;
                MoveIconToOffset(
                    item,
                    (previewColumn - anchorColumn) * cellWidth,
                    (previewRow - anchorRow) * cellHeight);
            }

            ClearItemMotion(dragged);
        }

        private double GetItemCellWidth(DesktopItemControl item)
        {
            return item.ActualWidth + item.Margin.Left + item.Margin.Right;
        }

        private double GetItemCellHeight(DesktopItemControl item)
        {
            return item.ActualHeight + item.Margin.Top + item.Margin.Bottom;
        }

        private Dictionary<DesktopItemControl, Point> CaptureBaseItemPositions()
        {
            Dictionary<DesktopItemControl, Point> positions =
                new Dictionary<DesktopItemControl, Point>(_itemsHost.Children.Count);
            foreach (UIElement child in _itemsHost.Children)
            {
                DesktopItemControl item = child as DesktopItemControl;
                if (item == null)
                {
                    continue;
                }

                Point point = item.TranslatePoint(new Point(0, 0), _itemsHost);
                TranslateTransform transform = item.RenderTransform as TranslateTransform;
                if (transform != null)
                {
                    point.X -= transform.X;
                    point.Y -= transform.Y;
                }

                positions[item] = point;
            }

            return positions;
        }

        private void MoveIconToOffset(DesktopItemControl item, double x, double y)
        {
            TranslateTransform transform = item.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                if (Math.Abs(x) < 0.1 && Math.Abs(y) < 0.1)
                {
                    return;
                }

                transform = new TranslateTransform();
                item.RenderTransform = transform;
            }

            Point target = new Point(x, y);
            Point existing;
            if (_itemTargetOffsets.TryGetValue(item, out existing) &&
                Math.Abs(existing.X - target.X) < 0.1 &&
                Math.Abs(existing.Y - target.Y) < 0.1)
            {
                return;
            }

            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);

            IconOffsetMotion state;
            if (!_itemOffsetMotions.TryGetValue(item, out state) ||
                !ReferenceEquals(state.Transform, transform))
            {
                state = new IconOffsetMotion(transform);
                _itemOffsetMotions[item] = state;
            }

            state.Start = new Point(transform.X, transform.Y);
            state.Target = target;
            state.Started = Stopwatch.GetTimestamp();
            state.Active = true;
            _itemTargetOffsets[item] = target;
            EnsureReorderFrame();
        }

        private void AdvanceIconMotions()
        {
            long now = Stopwatch.GetTimestamp();
            foreach (IconOffsetMotion state in _itemOffsetMotions.Values)
            {
                if (!state.Active)
                {
                    continue;
                }

                long elapsedTicks = (now - state.Started) * TimeSpan.TicksPerSecond /
                    Stopwatch.Frequency;
                double milliseconds = TimeSpan.FromTicks(elapsedTicks).TotalMilliseconds;
                double progress = Math.Min(1.0, milliseconds / 210.0);
                double remaining = 1.0 - progress;
                double eased = 1.0 - remaining * remaining * remaining;
                state.Transform.X = state.Start.X + (state.Target.X - state.Start.X) * eased;
                state.Transform.Y = state.Start.Y + (state.Target.Y - state.Start.Y) * eased;
                if (progress == 1.0)
                {
                    state.Active = false;
                    state.Transform.X = state.Target.X;
                    state.Transform.Y = state.Target.Y;
                }
            }
        }

        private bool HasActiveIconMotions()
        {
            foreach (IconOffsetMotion state in _itemOffsetMotions.Values)
            {
                if (state.Active)
                {
                    return true;
                }
            }

            return false;
        }

        private int FindPreviewIndex(Point point)
        {
            int nearestIndex = 0;
            double nearestDistance = double.MaxValue;
            double nearestCenterX = 0;
            for (int index = 0; index < _itemsHost.Children.Count; index++)
            {
                UIElement child = _itemsHost.Children[index];
                Point topLeft = child.TranslatePoint(new Point(0, 0), _itemsHost);
                double centerX = topLeft.X + child.RenderSize.Width * 0.5;
                double centerY = topLeft.Y + child.RenderSize.Height * 0.5;
                double deltaX = point.X - centerX;
                double deltaY = point.Y - centerY;
                double distance = deltaX * deltaX + deltaY * deltaY;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = index;
                    nearestCenterX = centerX;
                }
            }

            if (_itemsHost.Children.Count == 0 || point.X < nearestCenterX)
            {
                return nearestIndex;
            }

            return nearestIndex + 1;
        }

        private void CommitVisualOrder()
        {
            List<DesktopItem> orderedItems = new List<DesktopItem>(_itemsHost.Children.Count);
            foreach (DesktopItemControl control in _itemsHost.Children)
            {
                orderedItems.Add(control.Model);
            }

            if (orderedItems.Count != _model.Items.Count)
            {
                RefreshItems();
                return;
            }

            bool changed = false;
            for (int index = 0; index < orderedItems.Count; index++)
            {
                if (!ReferenceEquals(orderedItems[index], _model.Items[index]))
                {
                    changed = true;
                    break;
                }
            }

            if (!changed)
            {
                return;
            }

            _model.Items.Clear();
            _model.Items.AddRange(orderedItems);
            NotifyChanged();
        }

        private void CommitVisualOrder(List<DesktopItemControl> previewOrder)
        {
            if (previewOrder == null || previewOrder.Count != _itemsHost.Children.Count)
            {
                CommitVisualOrder();
                return;
            }

            List<DesktopItem> orderedItems = new List<DesktopItem>(previewOrder.Count);
            for (int index = 0; index < previewOrder.Count; index++)
            {
                DesktopItemControl control = previewOrder[index];
                if (control == null || !_itemsHost.Children.Contains(control))
                {
                    CommitVisualOrder();
                    return;
                }

                orderedItems.Add(control.Model);
            }

            _itemsHost.Children.Clear();
            foreach (DesktopItemControl control in previewOrder)
            {
                ClearItemMotion(control);
                _itemsHost.Children.Add(control);
            }

            _itemTargetOffsets.Clear();
            _itemOffsetMotions.Clear();

            bool changed = false;
            if (orderedItems.Count == _model.Items.Count)
            {
                for (int index = 0; index < orderedItems.Count; index++)
                {
                    if (!ReferenceEquals(orderedItems[index], _model.Items[index]))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            else
            {
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            _model.Items.Clear();
            _model.Items.AddRange(orderedItems);
            NotifyChanged();
        }

        private void RemoveItemForRegionMove(DesktopItem item)
        {
            if (item == null || !_model.Items.Remove(item))
            {
                return;
            }

            RefreshItems();
            NotifyChanged();
        }

        private bool AddTransferredItem(DesktopItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path))
            {
                return false;
            }

            foreach (DesktopItem existing in _model.Items)
            {
                if (existing != null &&
                    string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            _model.Items.Add(item);
            RefreshItems();
            NotifyChanged();
            return true;
        }

        internal Point PointToDragLayer(Point screenPoint)
        {
            if (_dragLayer == null)
            {
                return new Point(0, 0);
            }

            try
            {
                return _dragLayer.PointFromScreen(screenPoint);
            }
            catch
            {
                return new Point(screenPoint.X, screenPoint.Y);
            }
        }

        private Point PointToItemsHost(Point screenPoint)
        {
            try
            {
                return _itemsHost.PointFromScreen(screenPoint);
            }
            catch
            {
                return new Point(screenPoint.X, screenPoint.Y);
            }
        }

        public void BeginInternalDrag(DesktopItemControl source, Point layerPoint)
        {
            if (_internalDragActive || source == null)
            {
                return;
            }

            Point sourceTopLeft = source.TranslatePoint(new Point(0, 0), _dragLayer);
            _internalDragGrabOffset = new Point(
                layerPoint.X - sourceTopLeft.X,
                layerPoint.Y - sourceTopLeft.Y);
            _internalDragActive = true;
            _oleFallbackPending = false;
            _oleTransferQueued = false;
            _oleTransferRunning = false;
            _resumeCaptureFromOle = false;
            _lastOlePointerSampleTicks = 0;
            _internalDragSession++;
            _internalDragItem = source;
            _internalDragMouseCaptured = false;
            _dragPreviewOrder = new List<DesktopItemControl>(_itemsHost.Children.Count);
            foreach (DesktopItemControl item in _itemsHost.Children)
            {
                _dragPreviewOrder.Add(item);
            }

            source.ReleaseMouseCaptureForDrag();
            AttachInternalDragInputHandlers();
            try
            {
                _internalDragMouseCaptured = Mouse.Capture(this, CaptureMode.SubTree);
            }
            catch
            {
                _internalDragMouseCaptured = false;
            }

            if (!_internalDragMouseCaptured)
            {
                source.CaptureMouse();
            }

            ClearItemMotion(source);
            source.Opacity = 0.0;
            source.IsHitTestVisible = false;

            DesktopItemControl ghost = source.CreateDragClone();
            ghost.Width = source.ActualWidth > 0 ? source.ActualWidth : source.Width;
            ghost.Height = source.ActualHeight > 0 ? source.ActualHeight : source.Height;
            ghost.Margin = new Thickness(0);
            ghost.Opacity = 0.96;
            ghost.IsHitTestVisible = false;
            ghost.ToolTip = null;
            ghost.ContextMenu = null;
            _internalDragGhostMotion = new TranslateTransform();
            TransformGroup ghostTransforms = new TransformGroup();
            ghostTransforms.Children.Add(new ScaleTransform(1.06, 1.06));
            ghostTransforms.Children.Add(_internalDragGhostMotion);
            ghost.RenderTransformOrigin = new Point(0.5, 0.5);
            ghost.RenderTransform = ghostTransforms;
            ghost.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 5,
                Opacity = 0.28
            };

            _internalDragGhost = ghost;
            _internalDragGhostMotion = ghost.RenderTransform as TranslateTransform ??
                _internalDragGhostMotion;
            _dragLayer.Children.Add(ghost);
            Panel.SetZIndex(ghost, 1000000);
            Canvas.SetLeft(ghost, sourceTopLeft.X);
            Canvas.SetTop(ghost, sourceTopLeft.Y);

            SetInteraction(true);
        }

        private void AttachInternalDragInputHandlers()
        {
            PreviewMouseMove += InternalDrag_PreviewMouseMove;
            PreviewMouseLeftButtonUp += InternalDrag_PreviewMouseLeftButtonUp;
        }

        private void DetachInternalDragInputHandlers()
        {
            PreviewMouseMove -= InternalDrag_PreviewMouseMove;
            PreviewMouseLeftButtonUp -= InternalDrag_PreviewMouseLeftButtonUp;
        }

        private void InternalDrag_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_internalDragActive || _internalDragItem == null)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndInternalDrag();
                return;
            }

            UpdateInternalDrag(GetCursorScreenPoint());
            e.Handled = true;
        }

        private void InternalDrag_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_internalDragActive || e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            CompleteInternalDrag(GetCursorScreenPoint());
            e.Handled = true;
        }

        public void UpdateInternalDrag(Point screenPoint)
        {
            if (!_internalDragActive || _internalDragItem == null)
            {
                return;
            }

            UpdateDragPointer(screenPoint);

            if (ContainsScreenPoint(screenPoint))
            {
                QueueReorderPreview(PointToItemsHost(screenPoint));
            }
            else
            {
                QueueOleFallback();
            }
        }

        public void CompleteInternalDrag(Point screenPoint)
        {
            if (!_internalDragActive)
            {
                return;
            }

            PreviewReorder(PointToItemsHost(screenPoint), _internalDragItem);
            List<DesktopItemControl> previewOrder = _dragPreviewOrder;
            EndInternalDrag();
            CommitVisualOrder(previewOrder);
        }

        private void QueueOleFallback()
        {
            if (!_internalDragActive || _oleFallbackPending ||
                _oleTransferQueued || _oleTransferRunning)
            {
                return;
            }

            _oleFallbackPending = true;
            _oleTransferQueued = true;
            DesktopItemControl source = _internalDragItem;
            if (source != null)
            {
                source.ArmOleFallback();
            }

            int session = _internalDragSession;
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate
            {
                _oleTransferQueued = false;
                if (session != _internalDragSession ||
                    !_internalDragActive ||
                    source == null)
                {
                    return;
                }

                if (Mouse.LeftButton != MouseButtonState.Pressed)
                {
                    EndInternalDrag();
                    return;
                }

                if (ContainsScreenPoint(GetCursorScreenPoint()))
                {
                    _oleFallbackPending = false;
                    return;
                }

                RunOleTransfer(source, session);
            }));
        }

        private void RunOleTransfer(DesktopItemControl source, int session)
        {
            if (source == null || _oleTransferRunning)
            {
                return;
            }

            _oleTransferRunning = true;
            _oleFallbackPending = false;
            _resumeCaptureFromOle = false;

            ItemDragPayload payload = new ItemDragPayload
            {
                Item = source.Model,
                Source = this,
                Control = source,
                Dropped = false
            };
            DataObject data = new DataObject();
            data.SetData(ItemDragFormat, payload);
            if (!string.IsNullOrWhiteSpace(source.Model.Path) &&
                (File.Exists(source.Model.Path) || Directory.Exists(source.Model.Path)))
            {
                if (!source.Model.ExternalReference &&
                    !IsDesktopPath(source.Model.Path))
                {
                    StringCollection files = new StringCollection();
                    files.Add(source.Model.Path);
                    data.SetFileDropList(files);
                }
            }

            ReleaseInternalDragCapture();
            DetachInternalDragInputHandlers();

            QueryContinueDragEventHandler continueDrag = delegate(object sender, QueryContinueDragEventArgs e)
            {
                if (!_internalDragActive || source == null)
                {
                    e.Action = DragAction.Cancel;
                    e.Handled = true;
                    return;
                }

                if (!e.EscapePressed &&
                    (e.KeyStates & DragDropKeyStates.LeftMouseButton) != 0 &&
                    ContainsScreenPoint(GetCursorScreenPoint()))
                {
                    _resumeCaptureFromOle = true;
                    e.Action = DragAction.Cancel;
                    e.Handled = true;
                }
            };
            GiveFeedbackEventHandler feedback = delegate(object sender, GiveFeedbackEventArgs e)
            {
                if (_internalDragActive)
                {
                    long now = Stopwatch.GetTimestamp();
                    long elapsedTicks = (now - _lastOlePointerSampleTicks) * TimeSpan.TicksPerSecond /
                        Stopwatch.Frequency;
                    if (_lastOlePointerSampleTicks == 0 ||
                        TimeSpan.FromTicks(elapsedTicks).TotalMilliseconds >= 8.0)
                    {
                        _lastOlePointerSampleTicks = now;
                        UpdateDragPointer(GetCursorScreenPoint());
                    }

                    e.UseDefaultCursors = false;
                    Mouse.SetCursor(Cursors.Hand);
                    e.Handled = true;
                }
            };

            QueryContinueDrag += continueDrag;
            GiveFeedback += feedback;
            DragDropEffects effect = DragDropEffects.None;
            try
            {
                effect = DragDrop.DoDragDrop(
                    this,
                    data,
                    DragDropEffects.Move | DragDropEffects.Copy);
            }
            finally
            {
                QueryContinueDrag -= continueDrag;
                GiveFeedback -= feedback;
                _oleTransferRunning = false;
                _oleFallbackPending = false;
                _lastOlePointerSampleTicks = 0;

                bool resumed = _resumeCaptureFromOle &&
                    _internalDragActive &&
                    session == _internalDragSession &&
                    CaptureInternalDragMouse();
                _resumeCaptureFromOle = false;
                source.ClearOleFallback();
                if (resumed)
                {
                    AttachInternalDragInputHandlers();
                }
                else
                {
                    DesktopItem item = source.Model;
                    Point cursor = GetCursorScreenPoint();
                    bool outside = !ContainsScreenPoint(cursor);
                    bool overDesktop = outside && IsCursorOverDesktop();
                    bool externalRestored = false;
                    bool shouldRemove = false;

                    if (!payload.Dropped &&
                        outside &&
                        item != null &&
                        item.ExternalReference &&
                        overDesktop)
                    {
                        externalRestored = source.CreateDesktopShortcutForExternalReference();
                    }

                    shouldRemove = _internalDragActive &&
                        session == _internalDragSession &&
                        effect != DragDropEffects.None &&
                        !payload.Dropped &&
                        outside;
                    if (!shouldRemove &&
                        _internalDragActive &&
                        session == _internalDragSession &&
                        !payload.Dropped &&
                        outside &&
                        IsDesktopPath(source.Model.Path) &&
                        overDesktop)
                    {
                        shouldRemove = true;
                    }
                    EndInternalDrag();
                    if ((externalRestored || shouldRemove) && item != null)
                    {
                        RemoveItem(item);
                    }
                }
            }
        }

        private void UpdateDragPointer(Point screenPoint)
        {
            if (!_internalDragActive || _internalDragGhost == null ||
                _internalDragGhostMotion == null)
            {
                return;
            }

            Point layerPoint = PointToDragLayer(screenPoint);
            double left = Canvas.GetLeft(_internalDragGhost);
            double top = Canvas.GetTop(_internalDragGhost);
            if (double.IsNaN(left))
            {
                left = 0.0;
            }
            if (double.IsNaN(top))
            {
                top = 0.0;
            }

            _internalDragGhostMotion.X = layerPoint.X - _internalDragGrabOffset.X - left;
            _internalDragGhostMotion.Y = layerPoint.Y - _internalDragGrabOffset.Y - top;
        }

        private bool CaptureInternalDragMouse()
        {
            try
            {
                _internalDragMouseCaptured = Mouse.Capture(this, CaptureMode.SubTree);
            }
            catch
            {
                _internalDragMouseCaptured = false;
            }

            if (!_internalDragMouseCaptured && _internalDragItem != null)
            {
                _internalDragItem.CaptureMouse();
            }

            return _internalDragMouseCaptured || (_internalDragItem != null && _internalDragItem.IsMouseCaptured);
        }

        private void ReleaseInternalDragCapture()
        {
            _internalDragMouseCaptured = false;
            if (ReferenceEquals(Mouse.Captured, this))
            {
                Mouse.Capture(null);
            }

            if (_internalDragItem != null)
            {
                _internalDragItem.ReleaseMouseCaptureForDrag();
            }
        }

        private void EndInternalDrag()
        {
            if (!_internalDragActive && _internalDragGhost == null)
            {
                return;
            }

            DesktopItemControl source = _internalDragItem;
            _internalDragActive = false;
            _internalDragItem = null;
            _oleFallbackPending = false;
            _oleTransferQueued = false;
            _oleTransferRunning = false;
            _resumeCaptureFromOle = false;
            _dragPreviewOrder = null;

            if (_internalDragGhost != null)
            {
                _dragLayer.Children.Remove(_internalDragGhost);
                _internalDragGhost = null;
            }
            _internalDragGhostMotion = null;

            DetachInternalDragInputHandlers();
            if (_internalDragMouseCaptured)
            {
                _internalDragMouseCaptured = false;
                ReleaseMouseCapture();
            }

            if (source != null)
            {
                ClearItemMotion(source);
                source.Opacity = 1.0;
                source.IsHitTestVisible = true;
                source.ReleaseMouseCaptureForDrag();
            }

            ClearAllItemMotions();
            CancelReorderFrame();
            SetInteraction(false);
        }

        private void ClearItemMotion(DesktopItemControl item)
        {
            if (item == null)
            {
                return;
            }

            TranslateTransform transform = item.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                return;
            }

            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.X = 0.0;
            transform.Y = 0.0;
            _itemTargetOffsets.Remove(item);
            _itemOffsetMotions.Remove(item);
        }

        private void ClearAllItemMotions()
        {
            foreach (DesktopItemControl item in _itemsHost.Children)
            {
                ClearItemMotion(item);
            }

            _itemTargetOffsets.Clear();
            _itemOffsetMotions.Clear();
        }

        private Point GetCursorScreenPoint()
        {
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            return new Point(cursor.X, cursor.Y);
        }

        internal static bool IsCursorOverDesktop()
        {
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            NativeMethods.POINT point = new NativeMethods.POINT
            {
                X = cursor.X,
                Y = cursor.Y
            };

            IntPtr window = NativeMethods.WindowFromPoint(point);
            IntPtr root = NativeMethods.GetAncestor(window, NativeMethods.GA_ROOT);
            if (root == IntPtr.Zero)
            {
                root = window;
            }

            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetClassName(root, className, className.Capacity);
            string value = className.ToString();
            return value == "Progman" || value == "WorkerW" || value == "SHELLDLL_DefView";
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BeginHeaderDrag(e);
        }

        private void BeginHeaderDrag(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left ||
                e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            Canvas parent = Parent as Canvas;
            if (parent == null)
            {
                return;
            }

            if (_renameBox != null)
            {
                if (IsRenameBoxSource(e.OriginalSource))
                {
                    return;
                }

                e.Handled = true;
                return;
            }

            _moving = true;
            _moveStart = e.GetPosition(parent);
            _regionStart = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
            _header.CaptureMouse();
            Mouse.OverrideCursor = Cursors.SizeAll;
            SetInteraction(true);
            if (_moveStartedCallback != null)
            {
                _moveStartedCallback(this);
            }
            e.Handled = true;
        }

        private bool IsRenameBoxSource(object source)
        {
            DependencyObject node = source as DependencyObject;
            return node != null && _renameBox != null &&
                (ReferenceEquals(node, _renameBox) || IsVisualDescendant(node, _renameBox));
        }

        private static bool IsVisualDescendant(DependencyObject node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, ancestor))
                {
                    return true;
                }

                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }

            return false;
        }

        private void Header_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_moving)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishRegionMove();
                return;
            }

            Canvas parent = Parent as Canvas;
            if (parent == null)
            {
                return;
            }

            Point position = e.GetPosition(parent);
            double x = _regionStart.X + (position.X - _moveStart.X);
            double y = _regionStart.Y + (position.Y - _moveStart.Y);
            ClampPosition(ref x, ref y);

            if (_moveCallback != null)
            {
                _moveCallback(this, x, y);
            }
            else
            {
                ApplyPosition(x, y);
            }
        }

        private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            FinishRegionMove();
            e.Handled = true;
        }

        private void FinishRegionMove()
        {
            if (!_moving)
            {
                return;
            }

            _moving = false;
            _header.ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
            if (_moveEndedCallback != null)
            {
                _moveEndedCallback(this);
            }
            SetInteraction(false);
            if (!IsMouseOver)
            {
                SetHover(false);
            }
            NotifyChanged();
        }

        private void Resize_DragStarted(object sender, DragStartedEventArgs e)
        {
            SetInteraction(true);
        }

        private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double horizontalChange = e.HorizontalChange;
            double verticalChange = e.VerticalChange;
            bool bottom =
                string.Equals(_model.DockEdge, RegionConfig.DockBottom, StringComparison.Ordinal);
            bool right =
                string.Equals(_model.DockEdge, RegionConfig.DockRight, StringComparison.Ordinal);

            if (bottom)
            {
                verticalChange = -verticalChange;
            }
            if (right)
            {
                horizontalChange = -horizontalChange;
            }

            double width = Width + horizontalChange;
            double height = Height + verticalChange;
            if (width < 96)
            {
                width = 96;
            }
            if (height < 72)
            {
                height = 72;
            }

            Width = width;
            Height = height;
            _model.Width = width;
            _model.Height = height;
            _expandedWidth = width;
            _expandedHeight = height;
            NotifyLayoutChanged();
        }

        private void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            SetInteraction(false);
            if (!IsMouseOver)
            {
                SetHover(false);
            }
            NotifyLayoutChanged();
            NotifyChanged();
        }

        private void Region_MouseEnter(object sender, MouseEventArgs e)
        {
            SetHover(true);
            if (_collapsed &&
                string.Equals(
                    _collapseHoverTrigger,
                    AppConfig.CollapseHoverRow,
                    StringComparison.Ordinal))
            {
                _autoExpanded = true;
                SetCollapsed(false);
            }
        }

        private void Region_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_moving && !_resizeGrip.IsDragging)
            {
                SetHover(false);
            }

            if (_autoExpanded &&
                !_pinnedExpanded &&
                !_interactionActive &&
                !HasOpenContextMenu)
            {
                CollapseTemporary();
            }
        }

        private void Child_MouseEnter(object sender, MouseEventArgs e)
        {
            SetHover(true);
        }

        private void Child_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_moving)
            {
                SetHover(false);
            }
        }

        private void SetHover(bool active)
        {
            if (_hoverActive == active)
            {
                return;
            }

            _hoverActive = active;
            Action<RegionControl, bool> handler = HoverChanged;
            if (handler != null)
            {
                handler(this, active);
            }
        }

        private void SetInteraction(bool active)
        {
            if (_interactionActive == active)
            {
                return;
            }

            _interactionActive = active;
            Action<RegionControl, bool> handler = InteractionChanged;
            if (handler != null)
            {
                handler(this, active);
            }
        }

        private void RemoveItem(DesktopItem item)
        {
            if (item == null)
            {
                return;
            }

            if (IsDesktopPath(item.Path))
            {
                try
                {
                    SetDesktopItemVisibility(item.Path, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "从分区移除失败：\n" + ex.Message,
                        "GhostDesk",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            if (!_model.Items.Remove(item))
            {
                return;
            }

            RefreshItems();
            NotifyChanged();
        }

        private void ShowItemLocation(DesktopItem item)
        {
            if (string.IsNullOrEmpty(item.Path) || item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                string locationPath = DesktopItemControl.ResolveShortcutTargetPath(item.Path);
                if (Directory.Exists(locationPath))
                {
                    Process.Start("explorer.exe", "\"" + locationPath + "\"");
                }
                else
                {
                    Process.Start("explorer.exe", "/select,\"" + locationPath + "\"");
                }
            }
            catch
            {
            }
        }

        private void ClampPosition(ref double x, ref double y)
        {
            Canvas parent = Parent as Canvas;
            double availableWidth = parent != null && parent.ActualWidth > 0.0
                ? parent.ActualWidth
                : SystemParameters.WorkArea.Width;
            double availableHeight = parent != null && parent.ActualHeight > 0.0
                ? parent.ActualHeight
                : SystemParameters.WorkArea.Height;
            double left = 0.0;
            double top = 0.0;
            double right = Math.Max(left, availableWidth - Width);
            double bottom = Math.Max(top, availableHeight - Height);

            if (x < left)
            {
                x = left;
            }
            if (x > right)
            {
                x = right;
            }
            if (y < top)
            {
                y = top;
            }
            if (y > bottom)
            {
                y = bottom;
            }
        }

        internal static bool IsDesktopPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (string.IsNullOrEmpty(desktop))
            {
                return false;
            }

            string normalized = path.TrimEnd('\\');
            string normalizedDesktop = desktop.TrimEnd('\\');
            return normalized.StartsWith(normalizedDesktop + "\\", StringComparison.OrdinalIgnoreCase);
        }

        internal static void SetDesktopItemVisibility(string path, bool visible)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if (visible)
            {
                attributes &= ~(FileAttributes.Hidden | FileAttributes.System);
            }
            else
            {
                attributes |= FileAttributes.Hidden | FileAttributes.System;
            }

            File.SetAttributes(path, attributes);
            NativeMethods.SHChangeNotify(
                NativeMethods.SHCNE_UPDATEITEM,
                NativeMethods.SHCNF_PATHW,
                path,
                IntPtr.Zero);

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(desktop))
            {
                NativeMethods.SHChangeNotify(
                    NativeMethods.SHCNE_UPDATEDIR,
                    NativeMethods.SHCNF_PATHW,
                    desktop,
                    IntPtr.Zero);
            }
        }

        internal bool ContainsScreenPoint(Point screenPoint)
        {
            try
            {
                Point local = PointFromScreen(screenPoint);
                return local.X >= 0 && local.X <= ActualWidth &&
                       local.Y >= 0 && local.Y <= ActualHeight;
            }
            catch
            {
                return IsMouseOver;
            }
        }

        private void NotifyChanged()
        {
            if (_changeCallback != null)
            {
                _changeCallback();
            }
        }

        private bool ShouldShowExtensions()
        {
            return !string.Equals(
                _model.ShowExtensions,
                "false",
                StringComparison.OrdinalIgnoreCase);
        }

        private static SolidColorBrush CreateBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush CreateGlassBrush()
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(72, 255, 255, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(28, 24, 32, 48), 0.48));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(58, 112, 160, 222), 1.0));
            brush.Freeze();
            return brush;
        }

        private static LinearGradientBrush CreateGlassBorderBrush()
        {
            LinearGradientBrush brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(190, 255, 255, 255), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(72, 255, 255, 255), 0.5));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 190, 226, 255), 1.0));
            brush.Freeze();
            return brush;
        }

        internal static Style CreateGlassScrollBarStyle()
        {
            string xaml = "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type ScrollBar}'>" +
                "<Setter Property='Width' Value='9'/>" +
                "<Setter Property='MinWidth' Value='9'/>" +
                "<Setter Property='Background' Value='Transparent'/>" +
                "<Setter Property='Focusable' Value='False'/>" +
                "<Setter Property='IsTabStop' Value='False'/>" +
                "<Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type ScrollBar}'>" +
                "<Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' " +
                "Value='{TemplateBinding Value}' Minimum='{TemplateBinding Minimum}' " +
                "Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}' " +
                "IsDirectionReversed='True'>" +
                "<Track.DecreaseRepeatButton>" +
                "<RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False' IsTabStop='False' FocusVisualStyle='{x:Null}'>" +
                "<RepeatButton.Template>" +
                "<ControlTemplate TargetType='{x:Type RepeatButton}'>" +
                "<Border Background='Transparent'/>" +
                "</ControlTemplate>" +
                "</RepeatButton.Template>" +
                "</RepeatButton>" +
                "</Track.DecreaseRepeatButton>" +
                "<Track.Thumb>" +
                "<Thumb Focusable='False' IsTabStop='False' FocusVisualStyle='{x:Null}'>" +
                "<Thumb.Template>" +
                "<ControlTemplate TargetType='{x:Type Thumb}'>" +
                "<Border x:Name='ThumbBorder' Background='#24FFFFFF' BorderBrush='#5CFFFFFF' " +
                "BorderThickness='1' CornerRadius='4' Margin='1,0' Opacity='0.78'/>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='ThumbBorder' Property='Background' Value='#3CFFFFFF'/>" +
                "<Setter TargetName='ThumbBorder' Property='Opacity' Value='0.95'/>" +
                "</Trigger>" +
                "<Trigger Property='IsDragging' Value='True'>" +
                "<Setter TargetName='ThumbBorder' Property='Background' Value='#52FFFFFF'/>" +
                "<Setter TargetName='ThumbBorder' Property='Opacity' Value='1'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Thumb.Template>" +
                "</Thumb>" +
                "</Track.Thumb>" +
                "<Track.IncreaseRepeatButton>" +
                "<RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False' IsTabStop='False' FocusVisualStyle='{x:Null}'>" +
                "<RepeatButton.Template>" +
                "<ControlTemplate TargetType='{x:Type RepeatButton}'>" +
                "<Border Background='Transparent'/>" +
                "</ControlTemplate>" +
                "</RepeatButton.Template>" +
                "</RepeatButton>" +
                "</Track.IncreaseRepeatButton>" +
                "</Track>" +
                "</Grid>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='Orientation' Value='Horizontal'>" +
                "<Setter Property='Width' Value='Auto'/>" +
                "<Setter Property='Height' Value='9'/>" +
                "<Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>";

            return (Style)XamlReader.Parse(xaml);
        }
    }

    internal class DesktopItemControl : Border
    {
        private readonly DesktopItem _model;
        private readonly Action<DesktopItem> _openCallback;
        private readonly Action<DesktopItem> _removeCallback;
        private readonly Action<DesktopItem> _locateCallback;
        private readonly Action _itemsChangedCallback;
        private readonly Action<DesktopItem, string> _addToLaunchCallback;
        private readonly Func<List<RegionConfig>> _launchTargetsProvider;
        private readonly bool _showExtensions;
        private Border _inner;
        private Point _dragStartPoint;
        private bool _leftButtonPressed;
        private bool _oleDragRunning;
        private bool _oleFallbackArmed;

        public event Action InteractionStarted;
        public event Action InteractionEnded;

        public DesktopItem Model
        {
            get { return _model; }
        }

        public DesktopItemControl(
            DesktopItem model,
            Action<DesktopItem> openCallback,
            Action<DesktopItem> removeCallback,
            Action<DesktopItem> locateCallback,
            Action itemsChangedCallback,
            Action<DesktopItem, string> addToLaunchCallback,
            Func<List<RegionConfig>> launchTargetsProvider,
            bool showExtensions)
        {
            _model = model;
            _openCallback = openCallback;
            _removeCallback = removeCallback;
            _locateCallback = locateCallback;
            _itemsChangedCallback = itemsChangedCallback;
            _addToLaunchCallback = addToLaunchCallback;
            _launchTargetsProvider = launchTargetsProvider;
            _showExtensions = showExtensions;

            Width = 82;
            Height = 94;
            Margin = new Thickness(3);
            Padding = new Thickness(4);
            Background = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            CornerRadius = new CornerRadius(8);
            Cursor = Cursors.Hand;
            ToolTip = GetDisplayName();
            ContextMenu = BuildMenu();

            BuildUi();
            MouseEnter += Control_MouseEnter;
            MouseLeave += Control_MouseLeave;
            MouseLeftButtonDown += Control_MouseLeftButtonDown;
            MouseMove += Control_MouseMove;
            MouseLeftButtonUp += Control_MouseLeftButtonUp;
        }

        private void BuildUi()
        {
            _inner = new Border();
            _inner.CornerRadius = new CornerRadius(8);
            _inner.Background = Brushes.Transparent;
            _inner.Padding = new Thickness(4);

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Image icon = new Image();
            icon.Width = 42;
            icon.Height = 42;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Stretch = Stretch.Uniform;
            icon.Source = IconHelper.GetIcon(_model);
            icon.SnapsToDevicePixels = true;
            Grid.SetRow(icon, 0);
            grid.Children.Add(icon);

            TextBlock name = new TextBlock();
            name.Text = GetDisplayName();
            name.FontSize = 11;
            name.Foreground = Brushes.White;
            name.TextAlignment = TextAlignment.Center;
            name.TextWrapping = TextWrapping.Wrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxHeight = 34;
            name.HorizontalAlignment = HorizontalAlignment.Stretch;
            name.VerticalAlignment = VerticalAlignment.Top;
            name.Margin = new Thickness(0, 1, 0, 0);
            Grid.SetRow(name, 1);
            grid.Children.Add(name);

            _inner.Child = grid;
            Child = _inner;
        }

        private ContextMenu BuildMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.StaysOpen = false;
            GlassTheme.ApplyContextMenuStyle(menu);
            MenuItem restore = null;

            MenuItem open = new MenuItem { Header = "打开" };
            open.Click += Open_Click;
            menu.Items.Add(open);

            if (CanEditInNotepad())
            {
                MenuItem editInNotepad = new MenuItem { Header = "在记事本中编辑" };
                editInNotepad.Click += EditInNotepad_Click;
                menu.Items.Add(editInNotepad);
            }

            if (CanRunAsAdministrator())
            {
                MenuItem runAsAdmin = new MenuItem { Header = "以管理员身份运行" };
                runAsAdmin.Click += RunAsAdmin_Click;
                menu.Items.Add(runAsAdmin);
            }

            if (!IsShellItem())
            {
                MenuItem openLocation = new MenuItem { Header = "打开文件所在位置" };
                openLocation.Click += delegate
                {
                    if (_locateCallback != null)
                    {
                        _locateCallback(_model);
                    }
                };
                menu.Items.Add(openLocation);
            }

            MenuItem copyPath = new MenuItem { Header = "复制路径" };
            copyPath.Click += CopyPath_Click;
            menu.Items.Add(copyPath);

            menu.Items.Add(new Separator());

            if (!IsShellItem())
            {
                MenuItem rename = new MenuItem { Header = "重命名" };
                rename.Click += RenameItem_Click;
                menu.Items.Add(rename);

                MenuItem customizeIcon = new MenuItem { Header = "自定义图标..." };
                customizeIcon.Click += CustomizeIcon_Click;
                menu.Items.Add(customizeIcon);

                MenuItem properties = new MenuItem { Header = "属性" };
                properties.Click += Properties_Click;
                menu.Items.Add(properties);

                MenuItem recycle = new MenuItem { Header = "删除（回收站）" };
                recycle.Click += DeleteToRecycleBin_Click;
                menu.Items.Add(recycle);

                restore = new MenuItem { Header = "放回桌面" };
                restore.Click += RestoreToDesktop_Click;
            }

            if (_addToLaunchCallback != null)
            {
                menu.Items.Add(new Separator());
                MenuItem addToLaunch = new MenuItem { Header = "加入一键启动" };
                addToLaunch.SubmenuOpened += delegate { RefreshLaunchTargets(addToLaunch); };
                menu.Opened += delegate { RefreshLaunchTargets(addToLaunch); };
                menu.Items.Add(addToLaunch);
            }

            if (restore != null)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(restore);
            }

            menu.Opened += delegate { if (InteractionStarted != null) { InteractionStarted(); } };
            menu.Closed += delegate { if (InteractionEnded != null) { InteractionEnded(); } };

            return menu;
        }

        private void RefreshLaunchTargets(MenuItem parent)
        {
            parent.Items.Clear();
            if (_launchTargetsProvider == null)
            {
                parent.IsEnabled = false;
                return;
            }

            List<RegionConfig> regions = _launchTargetsProvider();
            if (regions == null || regions.Count == 0)
            {
                MenuItem empty = new MenuItem { Header = "暂无可用分区" };
                empty.IsEnabled = false;
                parent.Items.Add(empty);
                parent.IsEnabled = true;
                return;
            }

            parent.IsEnabled = true;
            for (int i = 0; i < regions.Count; i++)
            {
                RegionConfig region = regions[i];
                MenuItem option = new MenuItem
                {
                    Header = region == null || string.IsNullOrEmpty(region.Name)
                        ? "新分区"
                        : region.Name,
                    Tag = region == null ? null : region.Id
                };
                option.Click += AddToLaunchTarget_Click;
                parent.Items.Add(option);
            }
        }

        private void AddToLaunchTarget_Click(object sender, RoutedEventArgs e)
        {
            if (_addToLaunchCallback == null)
            {
                return;
            }

            MenuItem option = sender as MenuItem;
            string regionId = option == null ? null : option.Tag as string;
            _addToLaunchCallback(_model, regionId);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (_openCallback != null)
            {
                _openCallback(_model);
            }
        }

        private void RunAsAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (IsShellItem())
            {
                return;
            }

            try
            {
                ProcessStartInfo startInfo = AppController.CreateLaunchStartInfo(_model.Path);
                startInfo.Verb = "runas";
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowError("无法以管理员身份运行", ex);
            }
        }

        private void Locate_Click(object sender, RoutedEventArgs e)
        {
            if (_locateCallback != null)
            {
                _locateCallback(_model);
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            TrySetClipboardText(_model.Path ?? "");
        }

        private void EditInNotepad_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditInNotepad())
            {
                return;
            }

            try
            {
                string notepad = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "notepad.exe");
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = notepad;
                startInfo.Arguments = "\"" + _model.Path + "\"";
                startInfo.UseShellExecute = false;
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowError("无法在记事本中打开", ex);
            }
        }

        private static bool TrySetClipboardText(string value)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(value, true, 10, 100);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void RenameItem_Click(object sender, RoutedEventArgs e)
        {
            if (IsShellItem() || !File.Exists(_model.Path) && !Directory.Exists(_model.Path))
            {
                return;
            }

            string oldName = GetDisplayName();

            GlassPromptWindow dialog = new GlassPromptWindow(
                "重命名",
                new GlassPromptField { Label = "名称", Value = oldName },
                new GlassPromptField
                {
                    Label = "自定义图标路径（留空保持现状）",
                    Value = "",
                    IsIconPath = true
                });
            Window owner = Window.GetWindow(this);
            if (owner != null)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            bool? dialogResult = dialog.ShowDialog();
            if (dialogResult != true)
            {
                return;
            }

            string[] values = dialog.GetValues();
            string value = values.Length > 0 ? values[0] : oldName;
            string iconPath = values.Length > 1 ? values[1] : "";
            bool changed = false;

            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                if (!File.Exists(iconPath))
                {
                    MessageBox.Show(
                        "自定义图标文件不存在，已保持原图标不变。",
                        "GhostDesk",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    _model.IconPath = iconPath;
                    changed = true;
                }
            }

            string newName = string.IsNullOrWhiteSpace(value) ? oldName : value.Trim();
            if (_model.ExternalReference)
            {
                try
                {
                    string managedShortcut = CreateManagedShortcutForExternalPath(_model.Path);
                    _model.Path = managedShortcut;
                    _model.Type = "shortcut";
                    _model.Name = Path.GetFileNameWithoutExtension(managedShortcut);
                    _model.ExternalReference = false;
                    changed = true;
                }
                catch (Exception ex)
                {
                    ShowError("创建受管快捷方式失败", ex);
                    return;
                }
            }

            string oldFileName = Path.GetFileName(_model.Path);
            string newFileName = ResolveRenameFileName(newName, oldFileName);
            string directory = Path.GetDirectoryName(_model.Path);
            if (string.IsNullOrEmpty(directory))
            {
                MessageBox.Show(
                    "无法确定文件所在目录。",
                    "GhostDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string newPath = Path.Combine(directory, newFileName);
            bool pathChanged = !string.Equals(
                _model.Path,
                newPath,
                StringComparison.OrdinalIgnoreCase);

            if (pathChanged)
            {
                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    MessageBox.Show(
                        "目标名称已存在，请换一个名称。",
                        "GhostDesk",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    if (Directory.Exists(_model.Path))
                    {
                        Directory.Move(_model.Path, newPath);
                    }
                    else
                    {
                        File.Move(_model.Path, newPath);
                    }

                    _model.Path = newPath;
                    _model.Name = GetModelNameForPath(newPath);
                    if (RegionControl.IsDesktopPath(newPath))
                    {
                        RegionControl.SetDesktopItemVisibility(newPath, false);
                    }

                    changed = true;
                }
                catch (Exception ex)
                {
                    ShowError("重命名失败", ex);
                    return;
                }
            }

            if (changed && _itemsChangedCallback != null)
            {
                _itemsChangedCallback();
            }
        }

        private void Properties_Click(object sender, RoutedEventArgs e)
        {
            if (IsShellItem())
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(_model.Path)
                {
                    UseShellExecute = true,
                    Verb = "properties"
                });
            }
            catch
            {
                try
                {
                    if (_locateCallback != null)
                    {
                        _locateCallback(_model);
                    }
                }
                catch
                {
                }
            }
        }

        private void CustomizeIcon_Click(object sender, RoutedEventArgs e)
        {
            if (IsShellItem())
            {
                return;
            }

            GlassPromptWindow dialog = new GlassPromptWindow(
                "自定义图标",
                new GlassPromptField
                {
                    Label = "自定义图标路径（留空保持现状）",
                    Value = "",
                    IsIconPath = true
                });
            Window owner = Window.GetWindow(this);
            if (owner != null)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string[] values = dialog.GetValues();
            string iconPath = values.Length > 0 ? values[0] : "";
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                return;
            }

            _model.IconPath = iconPath;
            if (_itemsChangedCallback != null)
            {
                _itemsChangedCallback();
            }
        }

        private void CopyItem_Click(object sender, RoutedEventArgs e)
        {
            if (IsShellItem())
            {
                return;
            }

            try
            {
                StringCollection files = new StringCollection();
                files.Add(_model.Path);
                System.Windows.Clipboard.SetFileDropList(files);
            }
            catch (Exception ex)
            {
                ShowError("复制失败", ex);
            }
        }

        private void RestoreToDesktop_Click(object sender, RoutedEventArgs e)
        {
            if (IsShellItem() || !File.Exists(_model.Path) && !Directory.Exists(_model.Path))
            {
                return;
            }

            ContextMenu menu = ContextMenu;
            if (menu != null && menu.IsOpen)
            {
                menu.IsOpen = false;
            }

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                if (_model.ExternalReference)
                {
                    if (CreateDesktopShortcutForExternalReference())
                    {
                        if (_removeCallback != null)
                        {
                            _removeCallback(_model);
                        }
                    }
                    return;
                }

                if (RegionControl.IsDesktopPath(_model.Path))
                {
                    if (_removeCallback != null)
                    {
                        _removeCallback(_model);
                    }
                    return;
                }

                string fileName = Path.GetFileName(_model.Path);
                string destination = GetUniqueDestination(desktop, fileName);

                if (string.Equals(_model.Path, destination, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (Directory.Exists(_model.Path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(
                        _model.Path,
                        destination,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(
                        _model.Path,
                        destination,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                }

                _model.Path = destination;
                _model.Name = DisplayNameForPath(destination);
                RegionControl.SetDesktopItemVisibility(destination, true);
                if (_removeCallback != null)
                {
                    _removeCallback(_model);
                }
            }
            catch (Exception ex)
            {
                ShowError("放回桌面失败", ex);
            }
        }

        private void DeleteToRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            if (IsShellItem() || !File.Exists(_model.Path) && !Directory.Exists(_model.Path))
            {
                return;
            }

            try
            {
                if (Directory.Exists(_model.Path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        _model.Path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        _model.Path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }

                if (_removeCallback != null)
                {
                    _removeCallback(_model);
                }
            }
            catch (Exception ex)
            {
                ShowError("删除失败", ex);
            }
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (_removeCallback != null)
            {
                _removeCallback(_model);
            }
        }

        private void Control_MouseEnter(object sender, MouseEventArgs e)
        {
            _inner.Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
        }

        private void Control_MouseLeave(object sender, MouseEventArgs e)
        {
            _inner.Background = Brushes.Transparent;
        }

        private void Control_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = GetCursorScreenPoint();
            _leftButtonPressed = true;
            CaptureMouse();

            if (e.ClickCount == 2 && _openCallback != null)
            {
                _openCallback(_model);
                e.Handled = true;
            }
        }

        private void Control_MouseMove(object sender, MouseEventArgs e)
        {
            RegionControl owner = FindAncestor<RegionControl>();
            if (_oleFallbackArmed)
            {
                return;
            }

            if (!_leftButtonPressed || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point position = GetCursorScreenPoint();
            double deltaX = Math.Abs(position.X - _dragStartPoint.X);
            double deltaY = Math.Abs(position.Y - _dragStartPoint.Y);
            if (deltaX <= SystemParameters.MinimumHorizontalDragDistance &&
                deltaY <= SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (owner == null)
            {
                return;
            }

            if (IsShellItem())
            {
                return;
            }

            if (!IsMouseCaptured)
            {
                CaptureMouse();
            }

            e.Handled = true;
            owner.BeginInternalDrag(this, owner.PointToDragLayer(position));
        }

        public DesktopItemControl CreateDragClone()
        {
            DesktopItemControl clone = new DesktopItemControl(
                _model,
                _openCallback,
                _removeCallback,
                _locateCallback,
                _itemsChangedCallback,
                _addToLaunchCallback,
                _launchTargetsProvider,
                _showExtensions);
            clone.ToolTip = null;
            clone.ContextMenu = null;
            clone.Margin = new Thickness(0);
            clone.Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            return clone;
        }

        public void ReleaseMouseCaptureForDrag()
        {
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        public void ArmOleFallback()
        {
            _oleFallbackArmed = true;
        }

        public void ClearOleFallback()
        {
            _oleFallbackArmed = false;
        }

        public void StartOleDrag()
        {
            _oleFallbackArmed = false;
            if (IsShellItem() || _oleDragRunning)
            {
                return;
            }

            ReleaseMouseCapture();
            _oleDragRunning = true;
            DataObject data = new DataObject();
            RegionControl.ItemDragPayload payload = new RegionControl.ItemDragPayload
            {
                Item = _model,
                Source = FindAncestor<RegionControl>(),
                Control = this
            };
            data.SetData(RegionControl.ItemDragFormat, payload);
            if (!string.IsNullOrWhiteSpace(_model.Path) &&
                (File.Exists(_model.Path) || Directory.Exists(_model.Path)))
            {
                if (!_model.ExternalReference &&
                    !RegionControl.IsDesktopPath(_model.Path))
                {
                    StringCollection files = new StringCollection();
                    files.Add(_model.Path);
                    data.SetFileDropList(files);
                }
            }

            try
            {
                if (InteractionStarted != null)
                {
                    InteractionStarted();
                }

                DragDropEffects effect = DragDrop.DoDragDrop(
                    this,
                    data,
                    DragDropEffects.Move | DragDropEffects.Copy);

                if (payload.Dropped)
                {
                    return;
                }

                RegionControl dropOwner = payload.Source ?? FindAncestor<RegionControl>();
                if (_model.ExternalReference &&
                    (dropOwner == null || !dropOwner.ContainsScreenPoint(GetCursorScreenPoint())) &&
                    RegionControl.IsCursorOverDesktop() &&
                    CreateDesktopShortcutForExternalReference())
                {
                    if (_removeCallback != null)
                    {
                        _removeCallback(_model);
                    }
                    return;
                }

                if (effect != DragDropEffects.None &&
                    (dropOwner == null || !dropOwner.ContainsScreenPoint(GetCursorScreenPoint())))
                {
                    if (_removeCallback != null)
                    {
                        _removeCallback(_model);
                    }
                }
            }
            finally
            {
                _oleDragRunning = false;
                if (InteractionEnded != null)
                {
                    InteractionEnded();
                }
            }
        }

        private Point GetCursorScreenPoint()
        {
            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            return new Point(cursor.X, cursor.Y);
        }

        private void Control_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _leftButtonPressed = false;
            _oleFallbackArmed = false;

            RegionControl owner = FindAncestor<RegionControl>();
            if (owner != null && owner.IsInternalDragActive)
            {
                owner.CompleteInternalDrag(GetCursorScreenPoint());
                e.Handled = true;
            }

            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        private bool IsShellItem()
        {
            return string.IsNullOrEmpty(_model.Path) ||
                   _model.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
        }

        private T FindAncestor<T>() where T : DependencyObject
        {
            DependencyObject current = this;
            while (current != null)
            {
                T target = current as T;
                if (target != null)
                {
                    return target;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private bool CanEditInNotepad()
        {
            if (IsShellItem() || !File.Exists(_model.Path))
            {
                return false;
            }

            return HasShellEditVerb(_model.Path) || LooksLikeTextFile(_model.Path);
        }

        private static bool HasShellEditVerb(string path)
        {
            uint commandLength = 0;
            AssocQueryString(
                0,
                1,
                path,
                "edit",
                null,
                ref commandLength);

            if (commandLength == 0)
            {
                return false;
            }

            StringBuilder command = new StringBuilder((int)commandLength);
            return AssocQueryString(
                0,
                1,
                path,
                "edit",
                command,
                ref commandLength) == 0;
        }

        private static bool LooksLikeTextFile(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    byte[] buffer = new byte[4096];
                    int count = stream.Read(buffer, 0, buffer.Length);
                    for (int i = 0; i < count; i++)
                    {
                        byte value = buffer[i];
                        if (value == 0 ||
                            value < 0x20 && value != 0x09 && value != 0x0A && value != 0x0D)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int AssocQueryString(
            uint flags,
            int str,
            string assoc,
            string extra,
            StringBuilder output,
            ref uint outputSize);

        private bool CanRunAsAdministrator()
        {
            if (IsShellItem())
            {
                return false;
            }

            string extension = Path.GetExtension(_model.Path);
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            extension = extension.ToLowerInvariant();
            return extension == ".exe" ||
                   extension == ".lnk" ||
                   extension == ".bat" ||
                   extension == ".cmd" ||
                   extension == ".msi" ||
                   extension == ".com";
        }

        private string GetDisplayName()
        {
            if (IsShellItem())
            {
                return _model.Name ?? "";
            }

            string type = _model.Type ?? "";
            string path = _model.Path ?? "";
            if (_model.ExternalReference)
            {
                return GetExternalReferenceDisplayName(type, path);
            }

            if (string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(path.TrimEnd('\\'));
            }

            if (string.Equals(type, "shortcut", StringComparison.OrdinalIgnoreCase))
            {
                string name = string.IsNullOrWhiteSpace(_model.Name)
                    ? Path.GetFileNameWithoutExtension(path)
                    : _model.Name.Trim();
                return name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(0, name.Length - 4)
                    : name;
            }

            if (_showExtensions)
            {
                return Path.GetFileName(path);
            }

            return Path.GetFileNameWithoutExtension(path);
        }

        private string GetExternalReferenceDisplayName(string type, string path)
        {
            string name = string.IsNullOrWhiteSpace(_model.Name)
                ? Path.GetFileName(path)
                : _model.Name.Trim();
            if (string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase))
            {
                return name.TrimEnd('\\');
            }

            if (string.Equals(type, "shortcut", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Path.GetFileNameWithoutExtension(path);
                }

                return _showExtensions ||
                       !name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                    ? name
                    : name.Substring(0, name.Length - 4);
            }

            string sourceExtension = Path.GetExtension(path);
            if (!_showExtensions)
            {
                if (!string.IsNullOrEmpty(sourceExtension) &&
                    name.EndsWith(sourceExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return name.Substring(0, name.Length - sourceExtension.Length);
                }

                return name;
            }

            if (!string.IsNullOrEmpty(sourceExtension) &&
                string.IsNullOrEmpty(Path.GetExtension(name)))
            {
                return name + sourceExtension;
            }

            return name;
        }

        private string ResolveRenameFileName(string newName, string oldFileName)
        {
            string type = _model.Type ?? "";
            string oldExtension = Path.GetExtension(oldFileName);

            if (string.Equals(type, "shortcut", StringComparison.OrdinalIgnoreCase))
            {
                if (newName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    return newName;
                }

                return newName + ".lnk";
            }

            if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                return newName;
            }

            if (!_showExtensions &&
                !string.IsNullOrEmpty(oldExtension) &&
                string.IsNullOrEmpty(Path.GetExtension(newName)))
            {
                return newName + oldExtension;
            }

            return newName;
        }

        private string GetModelNameForPath(string path)
        {
            if (Directory.Exists(path))
            {
                return Path.GetFileName(path.TrimEnd('\\'));
            }

            if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            return Path.GetFileName(path);
        }

        private string DisplayNameForPath(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            return Path.GetFileName(path);
        }

        internal bool CreateDesktopShortcutForExternalReference()
        {
            if (IsShellItem() ||
                (!File.Exists(_model.Path) && !Directory.Exists(_model.Path)))
            {
                return false;
            }

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutName = GetExternalShortcutName();
                string destination = GetUniqueDestination(
                    desktop,
                    shortcutName + ".lnk");

                if (string.Equals(
                        Path.GetExtension(_model.Path),
                        ".lnk",
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(_model.Path, destination, false);
                }
                else
                {
                    string sourceDirectory = Path.GetDirectoryName(_model.Path);
                    string existingShortcut =
                        FindExistingShortcutForTarget(_model.Path, sourceDirectory);
                    if (!string.IsNullOrEmpty(existingShortcut))
                    {
                        File.Copy(existingShortcut, destination, false);
                    }
                    else
                    {
                        CreateWindowsShortcut(destination, _model.Path);
                    }
                }

                RegionControl.SetDesktopItemVisibility(destination, true);
                return true;
            }
            catch (Exception ex)
            {
                ShowError("创建桌面快捷方式失败", ex);
                return false;
            }
        }

        internal static string CreateManagedShortcutForExternalPath(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("源路径为空。", "sourcePath");
            }

            string normalized = Path.GetFullPath(sourcePath);
            if (IsShortcutPath(normalized))
            {
                return normalized;
            }

            if (!File.Exists(normalized) && !Directory.Exists(normalized))
            {
                throw new FileNotFoundException("源文件或文件夹不存在。", normalized);
            }

            string sourceDirectory = Path.GetDirectoryName(normalized.TrimEnd('\\'));
            if (string.IsNullOrEmpty(sourceDirectory))
            {
                sourceDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GhostDesk",
                    "Shortcuts");
            }

            Directory.CreateDirectory(sourceDirectory);

            string baseName = Directory.Exists(normalized)
                ? new DirectoryInfo(normalized.TrimEnd('\\')).Name
                : Path.GetFileNameWithoutExtension(normalized);
            baseName = SanitizeShortcutFileName(baseName);

            string candidate = Path.Combine(sourceDirectory, baseName + ".lnk");
            string existingShortcut =
                FindExistingShortcutForTarget(normalized, sourceDirectory, candidate);
            if (File.Exists(candidate))
            {
                if (IsShortcutPointingToPath(candidate, normalized))
                {
                    if (!string.IsNullOrEmpty(existingShortcut) &&
                        !PathsEqual(existingShortcut, candidate) &&
                        ShouldRepairManagedShortcut(candidate, existingShortcut, sourceDirectory))
                    {
                        File.Copy(existingShortcut, candidate, true);
                    }

                    return candidate;
                }

                candidate = GetUniqueDestination(sourceDirectory, baseName + ".lnk");
            }

            if (!string.IsNullOrEmpty(existingShortcut))
            {
                File.Copy(existingShortcut, candidate, false);
            }
            else
            {
                CreateWindowsShortcut(candidate, normalized);
            }

            if (!IsShortcutPointingToPath(candidate, normalized))
            {
                throw new InvalidOperationException("快捷方式创建后校验失败。");
            }

            return candidate;
        }

        internal static string FindExistingShortcutForTarget(
            string targetPath,
            string sourceDirectory)
        {
            return FindExistingShortcutForTarget(targetPath, sourceDirectory, null);
        }

        private static string FindExistingShortcutForTarget(
            string targetPath,
            string sourceDirectory,
            string excludedShortcutPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath) ||
                string.IsNullOrWhiteSpace(sourceDirectory) ||
                !Directory.Exists(sourceDirectory))
            {
                return null;
            }

            string normalizedTarget;
            try
            {
                normalizedTarget = Path.GetFullPath(targetPath);
            }
            catch
            {
                return null;
            }

            List<string> searchDirectories = new List<string>();
            AddShortcutSearchDirectory(searchDirectories, sourceDirectory);
            DirectoryInfo current = Directory.GetParent(sourceDirectory);
            int depth = 0;
            while (current != null && depth < 2)
            {
                AddShortcutSearchDirectory(searchDirectories, current.FullName);
                current = current.Parent;
                depth++;
            }

            foreach (string directory in searchDirectories)
            {
                string[] shortcuts;
                try
                {
                    shortcuts = Directory.GetFiles(
                        directory,
                        "*.lnk",
                        SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                Array.Sort(shortcuts, StringComparer.OrdinalIgnoreCase);
                foreach (string shortcut in shortcuts)
                {
                    if (!string.IsNullOrEmpty(excludedShortcutPath) &&
                        PathsEqual(shortcut, excludedShortcutPath))
                    {
                        continue;
                    }

                    if (IsShortcutPointingToPath(shortcut, normalizedTarget))
                    {
                        return shortcut;
                    }
                }
            }

            return null;
        }

        private static void AddShortcutSearchDirectory(
            List<string> directories,
            string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            string normalized;
            try
            {
                normalized = Path.GetFullPath(directory).TrimEnd('\\');
            }
            catch
            {
                return;
            }

            foreach (string existing in directories)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            directories.Add(normalized);
        }

        private static bool ShouldRepairManagedShortcut(
            string candidate,
            string preferredShortcut,
            string sourceDirectory)
        {
            string candidateWorkingDirectory =
                ReadWindowsShortcutProperty(candidate, "WorkingDirectory") as string;
            string preferredWorkingDirectory =
                ReadWindowsShortcutProperty(preferredShortcut, "WorkingDirectory") as string;

            if (string.IsNullOrWhiteSpace(preferredWorkingDirectory) ||
                PathsEqual(candidateWorkingDirectory, preferredWorkingDirectory))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(candidateWorkingDirectory) ||
                PathsEqual(candidateWorkingDirectory, sourceDirectory);
        }

        private static bool PathsEqual(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(first).TrimEnd('\\'),
                    Path.GetFullPath(second).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string SanitizeShortcutFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sanitized = new StringBuilder(value == null ? 0 : value.Length);
            if (!string.IsNullOrEmpty(value))
            {
                foreach (char character in value)
                {
                    sanitized.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
                }
            }

            string result = sanitized.ToString().Trim().TrimEnd('.');
            return string.IsNullOrEmpty(result) ? "快捷方式" : result;
        }

        internal static bool IsShortcutPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                string.Equals(
                    Path.GetExtension(path),
                    ".lnk",
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsShortcutPointingToPath(string shortcutPath, string targetPath)
        {
            if (!IsShortcutPath(shortcutPath) ||
                !File.Exists(shortcutPath) ||
                string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            string shortcutTarget = ReadWindowsShortcutTarget(shortcutPath);
            if (string.IsNullOrEmpty(shortcutTarget))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(shortcutTarget),
                    Path.GetFullPath(targetPath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static string ResolveShortcutTargetPath(string path)
        {
            if (!IsShortcutPath(path))
            {
                return path;
            }

            string target = ReadWindowsShortcutTarget(path);
            if (string.IsNullOrWhiteSpace(target))
            {
                return path;
            }

            target = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
            try
            {
                string fullPath = Path.GetFullPath(target);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
            }

            return target;
        }

        private string GetExternalShortcutName()
        {
            string name = string.IsNullOrWhiteSpace(_model.Name)
                ? Path.GetFileName(_model.Path)
                : _model.Name.Trim();
            string sourceExtension = Path.GetExtension(_model.Path);
            if (!string.IsNullOrEmpty(sourceExtension) &&
                name.EndsWith(sourceExtension, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - sourceExtension.Length);
            }

            if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sanitized = new StringBuilder(name.Length);
            foreach (char value in name)
            {
                sanitized.Append(Array.IndexOf(invalid, value) >= 0 ? '_' : value);
            }

            name = sanitized.ToString().Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "快捷方式" : name;
        }

        private static void CreateWindowsShortcut(
            string shortcutPath,
            string targetPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                throw new InvalidOperationException("当前系统不支持创建快捷方式。");
            }

            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember(
                    "TargetPath",
                    BindingFlags.SetProperty,
                    null,
                    shortcut,
                    new object[] { targetPath });

                string workingDirectory = Directory.Exists(targetPath)
                    ? targetPath
                    : Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    shortcutType.InvokeMember(
                        "WorkingDirectory",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        new object[] { workingDirectory });
                }

                shortcutType.InvokeMember(
                    "Save",
                    BindingFlags.InvokeMethod,
                    null,
                    shortcut,
                    null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    Marshal.ReleaseComObject(shortcut);
                }
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
        }

        private static string ReadWindowsShortcutTarget(string shortcutPath)
        {
            return ReadWindowsShortcutProperty(shortcutPath, "TargetPath") as string;
        }

        private static object ReadWindowsShortcutProperty(
            string shortcutPath,
            string propertyName)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return null;
            }

            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });
                object target = shortcut.GetType().InvokeMember(
                    propertyName,
                    BindingFlags.GetProperty,
                    null,
                    shortcut,
                    null);
                return target;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    Marshal.ReleaseComObject(shortcut);
                }
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
        }

        private static string GetUniqueDestination(string directory, string fileName)
        {
            string candidate = Path.Combine(directory, fileName);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int index = 2; index < 1000; index++)
            {
                candidate = Path.Combine(directory, baseName + " (" + index + ")" + extension);
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
        }

        private void ShowError(string title, Exception ex)
        {
            System.Windows.MessageBox.Show(
                title + "：\n" + ex.Message,
                "GhostDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
