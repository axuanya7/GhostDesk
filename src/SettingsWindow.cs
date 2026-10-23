using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace GhostDesk
{
    internal class SettingsWindow : Window
    {
        private readonly AppConfig _config;
        private readonly DesktopWindow _desktop;
        private readonly Action<bool> _setAutoStart;
        private readonly Action _reloadApplication;
        private readonly Action _checkForUpdates;
        private readonly ScheduledTaskRunner _scheduledTasks;
        private readonly List<Button> _navButtons = new List<Button>();
        private readonly Dictionary<string, Border> _navAccentBars =
            new Dictionary<string, Border>(StringComparer.Ordinal);
        private readonly Dictionary<string, TextBlock> _navLabels =
            new Dictionary<string, TextBlock>(StringComparer.Ordinal);

        private static readonly Brush WindowSurfaceBrush =
            GlassTheme.CreateBrush(242, 10, 16, 27);
        private static readonly Brush SidebarSurfaceBrush =
            GlassTheme.CreateBrush(174, 5, 10, 18);
        private static readonly Brush ContentSurfaceBrush =
            GlassTheme.CreateBrush(126, 12, 20, 33);
        private static readonly Brush CardSurfaceBrush =
            GlassTheme.CreateBrush(148, 15, 24, 39);
        private static readonly Brush CardRaisedBrush =
            GlassTheme.CreateBrush(172, 17, 27, 44);
        private static readonly Brush SettingsBorderBrush =
            GlassTheme.CreateBrush(38, 255, 255, 255);
        private static readonly Brush AccentSoftBrush =
            GlassTheme.CreateBrush(42, 139, 226, 255);

        private static Style _settingsCheckBoxStyle;
        private static Style _settingsRadioButtonStyle;
        private static Style _settingsSliderStyle;
        private static Style _settingsTextBoxStyle;

        private Border _windowRoot;
        private TextBlock _pageTitle;
        private TextBlock _pageSubtitle;
        private ScrollViewer _pageHost;
        private StackPanel _regionList;
        private Slider _hideDelaySlider;
        private TextBlock _hideDelayValue;
        private Slider _fadeSlider;
        private TextBlock _fadeValue;
        private CheckBox _autoStartCheck;
        private CheckBox _showHiddenFilesCheck;
        private bool _suppressVisibilityChange;
        private CheckBox _enableSnapCheck;
        private CheckBox _enableGuidesCheck;

        /// <summary>「!」提示图标（圆周感叹号）的矢量路径数据。</summary>
        private const string NotesIconPath =
            "M526.797519 107.535155c-214.532051 0-388.451126 173.917028-388.451126 388.488988 0 214.535121 173.919075 388.453173 388.451126 388.453173 214.57503 0 388.493082-173.918052 388.493082-388.453173C915.289578 281.452183 741.371526 107.535155 526.797519 107.535155zM491.338936 200.38075l70.958098 0c31.743982 0 56.938803 26.716478 55.075363 58.402131L602.973456 503.515768c-1.714038 29.15911-25.864064 51.9113-55.075363 51.9113l-42.166355 0c-29.212322 0-53.356208-22.751167-55.067176-51.9113l-14.398942-244.732887C434.401156 227.098251 459.595978 200.38075 491.338936 200.38075zM526.819009 791.629675c-50.069349 0-90.654696-40.573067-90.654696-90.641393 0-50.070372 40.585347-90.645486 90.654696-90.645486 50.067302 0 90.654696 40.575114 90.654696 90.645486C617.473705 751.054561 576.886311 791.629675 526.819009 791.629675z";
        private TextBox _edgeDockBandBox;
        private TextBox _safeTopBox;
        private TextBox _safeBottomBox;
        private TextBox _safeSideBox;
        private Slider _edgeDockBandSlider;
        private Slider _safeTopSlider;
        private Slider _safeBottomSlider;
        private Slider _safeSideSlider;
        private bool _dockSyncBusy;
        private TextBox _matOpacityBox;
        private Slider _matOpacitySlider;
        private TextBox _matTintBox;
        private Slider _matTintSlider;
        private TextBox _matBorderBox;
        private Slider _matBorderSlider;
        private TextBox _matShadowBox;
        private Slider _matShadowSlider;
        private bool _matSyncBusy;

        /// <summary>是否放出「分区材质」区块（材质功能验收后再置为 true）。</summary>
        private static readonly bool MaterialUiEnabled = false;
        private CheckBox _showExtensionsCheck;
        private Slider _backupIntervalSlider;
        private TextBlock _backupIntervalValue;
        private Slider _backupCountSlider;
        private TextBlock _backupCountValue;
        private RadioButton _collapseHoverButtonRadio;
        private RadioButton _collapseHoverRowRadio;
        private StackPanel _launchOverviewHost;
        private Slider _launchIntervalSlider;
        private TextBlock _launchIntervalValue;
        private StackPanel _scheduleList;
        private TextBlock _scheduleCountText;
        private string _scheduleSearchText = "";
        private string _currentPage = "regions";
        private TextBlock _backupStatusText;
        private StackPanel _autoBackupHost;
        private StackPanel _manualBackupHost;
        private bool _restoreInProgress;
        private Border _titlebarDragArea;
        private HwndSource _settingsSource;

        public SettingsWindow(
            AppConfig config,
            DesktopWindow desktop,
            Action<bool> setAutoStart,
            Action reloadApplication,
            Action checkForUpdates,
            ScheduledTaskRunner scheduledTasks)
        {
            _config = config;
            _desktop = desktop;
            _setAutoStart = setAutoStart;
            _reloadApplication = reloadApplication;
            _checkForUpdates = checkForUpdates;
            _scheduledTasks = scheduledTasks;

            Title = "GhostDesk 设置";
            Width = 900;
            Height = 640;
            MinWidth = 820;
            MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // 用系统标准窗体：原生标题栏、最小化/最大化/关闭、可缩放、可贴边、有投影，行为与资源管理器一致
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            AllowsTransparency = false;
            Background = WindowSurfaceBrush;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            Foreground = GlassTheme.TextPrimaryBrush;
            SourceInitialized += SettingsWindow_SourceInitialized;
            Closed += delegate
            {
                if (_settingsSource != null)
                {
                    _settingsSource.RemoveHook(SettingsWindow_WndProc);
                    _settingsSource = null;
                }

                if (!_restoreInProgress)
                {
                    ConfigStore.Save(_config);
                }
            };

            Content = BuildShell();
            Navigate("regions");
        }

        private void SettingsWindow_SourceInitialized(object sender, EventArgs e)
        {
            _settingsSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_settingsSource != null)
            {
                _settingsSource.AddHook(SettingsWindow_WndProc);
            }

            if (GlassTheme.EnableAcrylic(this))
            {
                _windowRoot.Background = GlassTheme.CreateBrush(198, 8, 14, 24);
            }
            else
            {
                _windowRoot.Background = WindowSurfaceBrush;
            }

            ApplyDarkTitleBar();
        }

        /// <summary>把系统标题栏切成深色，和窗口内容统一。</summary>
        private void ApplyDarkTitleBar()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return;
                }

                int enabled = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE：Win10 20H1+ 是 20，早期版本是 19
                if (NativeMethods.DwmSetWindowAttribute(
                        hwnd,
                        20,
                        ref enabled,
                        sizeof(int)) != 0)
                {
                    NativeMethods.DwmSetWindowAttribute(
                        hwnd,
                        19,
                        ref enabled,
                        sizeof(int));
                }
            }
            catch
            {
            }
        }

        private UIElement BuildShell()
        {
            Border sidebar;
            Grid mainGrid;
            _windowRoot = GlassTheme.CreateWindowRoot(out sidebar, out mainGrid);
            _windowRoot.Background = WindowSurfaceBrush;
            _windowRoot.BorderBrush = GlassTheme.CreateBrush(56, 255, 255, 255);
            _windowRoot.BorderThickness = new Thickness(1);
            _windowRoot.CornerRadius = new CornerRadius(0);

            sidebar.Background = SidebarSurfaceBrush;
            sidebar.BorderBrush = GlassTheme.CreateBrush(28, 255, 255, 255);
            sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
            sidebar.CornerRadius = new CornerRadius(16, 0, 0, 16);
            sidebar.Child = BuildSidebar();

            Grid main = new Grid();
            main.Margin = new Thickness(26, 22, 26, 24);
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });


            Grid header = BuildHeader();
            Grid.SetRow(header, 1);
            main.Children.Add(header);

            Border content = new Border();
            content.Margin = new Thickness(0, 18, 0, 0);
            content.Background = ContentSurfaceBrush;
            content.BorderBrush = SettingsBorderBrush;
            content.BorderThickness = new Thickness(1);
            content.CornerRadius = new CornerRadius(12);
            content.Padding = new Thickness(16);

            _pageHost = new ScrollViewer();
            _pageHost.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            _pageHost.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _pageHost.Background = Brushes.Transparent;
            _pageHost.Resources.Add(
                typeof(System.Windows.Controls.Primitives.ScrollBar),
                RegionControl.CreateGlassScrollBarStyle());
            content.Child = _pageHost;
            Grid.SetRow(content, 2);
            main.Children.Add(content);

            mainGrid.Children.Add(main);
            return _windowRoot;
        }

        private UIElement BuildTitlebar()
        {
            Grid bar = new Grid();
            bar.Margin = new Thickness(0, 0, 0, 12);
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border dragArea = new Border();
            dragArea.HorizontalAlignment = HorizontalAlignment.Stretch;
            dragArea.Height = 42;
            dragArea.VerticalAlignment = VerticalAlignment.Center;
            dragArea.Padding = new Thickness(0, 8, 12, 8);
            dragArea.Background = Brushes.Transparent;
            _titlebarDragArea = dragArea;

            TextBlock title = GlassTheme.CreateText("GhostDesk 设置", 13, true);
            title.VerticalAlignment = VerticalAlignment.Center;
            dragArea.Child = title;
            Grid.SetColumn(dragArea, 0);
            bar.Children.Add(dragArea);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.VerticalAlignment = VerticalAlignment.Center;

            Button minimize = GlassTheme.CreateIconButton(
                "-",
                "最小化",
                delegate
                {
                    WindowState = WindowState.Minimized;
                });
            actions.Children.Add(minimize);

            Button close = GlassTheme.CreateIconButton(
                "X",
                "关闭",
                delegate
                {
                    Close();
                });
            actions.Children.Add(close);

            Grid.SetColumn(actions, 1);
            bar.Children.Add(actions);
            return bar;
        }

        private IntPtr SettingsWindow_WndProc(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (msg != NativeMethods.WM_NCHITTEST ||
                _titlebarDragArea == null ||
                !_titlebarDragArea.IsVisible)
            {
                return IntPtr.Zero;
            }

            int screenX = unchecked((short)((long)lParam & 0xFFFF));
            int screenY = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
            Point local;
            try
            {
                local = _titlebarDragArea.PointFromScreen(new Point(screenX, screenY));
            }
            catch
            {
                return IntPtr.Zero;
            }

            if (local.X >= 0.0 &&
                local.Y >= 0.0 &&
                local.X <= _titlebarDragArea.ActualWidth &&
                local.Y <= _titlebarDragArea.ActualHeight)
            {
                handled = true;
                return new IntPtr(HtCaption);
            }

            return IntPtr.Zero;
        }

        private const int HtCaption = 2;

        private Grid BuildHeader()
        {
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel heading = new StackPanel();
            _pageTitle = GlassTheme.CreateText("分区管理", 24, false);
            _pageTitle.FontWeight = FontWeights.SemiBold;
            _pageTitle.Margin = new Thickness(0, 0, 0, 4);
            heading.Children.Add(_pageTitle);

            _pageSubtitle = GlassTheme.CreateText("管理桌面上的收纳分区", 13, true);
            heading.Children.Add(_pageSubtitle);
            Grid.SetColumn(heading, 0);
            header.Children.Add(heading);

            StackPanel headerActions = new StackPanel();
            headerActions.Orientation = Orientation.Horizontal;
            headerActions.VerticalAlignment = VerticalAlignment.Center;

            Button notes = GlassTheme.CreateIconButton(CreateNotesIcon(), "使用说明与注意事项", ShowNotes);
            notes.Margin = new Thickness(0, 0, 10, 0);
            headerActions.Children.Add(notes);

            Button done = GlassTheme.CreateButton("完成", ButtonKind.Primary);
            done.Click += Done_Click;
            headerActions.Children.Add(done);

            Grid.SetColumn(headerActions, 1);
            header.Children.Add(headerActions);
            return header;
        }

        private UIElement BuildSidebar()
        {
            Grid grid = new Grid();
            grid.Margin = new Thickness(16, 22, 16, 20);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel brand = new StackPanel();
            TextBlock name = GlassTheme.CreateText("GhostDesk", 24, false);
            name.FontWeight = FontWeights.Bold;
            brand.Children.Add(name);

            TextBlock subtitle = GlassTheme.CreateText("桌面玻璃分区", 12, true);
            subtitle.Margin = new Thickness(1, 4, 0, 0);
            brand.Children.Add(subtitle);
            Grid.SetRow(brand, 0);
            grid.Children.Add(brand);

            StackPanel nav = new StackPanel();
            nav.Margin = new Thickness(0, 28, 0, 0);
            nav.Children.Add(CreateNavButton("分区管理", "regions"));
            nav.Children.Add(CreateNavButton("隐身设置", "settings"));
            nav.Children.Add(CreateNavButton("一键启动", "launch"));
            nav.Children.Add(CreateNavButton("定时启动", "schedule"));
            nav.Children.Add(CreateNavButton("高级设置", "advanced"));
            nav.Children.Add(CreateNavButton("关于软件", "about"));
            Grid.SetRow(nav, 1);
            grid.Children.Add(nav);

            StackPanel footer = new StackPanel();

            _showHiddenFilesCheck = GlassTheme.CreateToggleCheckBox("显示隐藏文件");
            _showHiddenFilesCheck.IsChecked = _config.ShowHiddenFiles;
            _showHiddenFilesCheck.Margin = new Thickness(0, 0, 0, 2);
            _showHiddenFilesCheck.Checked += ShowHiddenFiles_Changed;
            _showHiddenFilesCheck.Unchecked += ShowHiddenFiles_Changed;
            footer.Children.Add(_showHiddenFilesCheck);

            TextBlock hiddenHint = GlassTheme.CreateText("仅用于临时查看桌面隐藏文件", 11, true);
            hiddenHint.Margin = new Thickness(1, 0, 0, 14);
            hiddenHint.Opacity = 0.62;
            footer.Children.Add(hiddenHint);

            _autoStartCheck = GlassTheme.CreateToggleCheckBox("开机自启");
            _autoStartCheck.IsChecked = _config.AutoStart;
            _autoStartCheck.Margin = new Thickness(0, 0, 0, 14);
            _autoStartCheck.Checked += AutoStart_Changed;
            _autoStartCheck.Unchecked += AutoStart_Changed;
            footer.Children.Add(_autoStartCheck);

            TextBlock version = GlassTheme.CreateText("GhostDesk " + AppInfo.Version, 11, true);
            version.Opacity = 0.62;
            footer.Children.Add(version);
            Grid.SetRow(footer, 2);
            grid.Children.Add(footer);

            return grid;
        }

        private Button CreateNavButton(string text, string tag)
        {
            Button button = GlassTheme.CreateButton(string.Empty, ButtonKind.Ghost);
            button.Tag = tag;
            button.Height = 42;
            button.MinWidth = 0;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(0, 0, 0, 6);
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(1);
            button.Foreground = GlassTheme.TextSecondaryBrush;

            Grid content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border accent = new Border();
            accent.Width = 3;
            accent.Height = 18;
            accent.HorizontalAlignment = HorizontalAlignment.Left;
            accent.VerticalAlignment = VerticalAlignment.Center;
            accent.CornerRadius = new CornerRadius(1.5);
            accent.Background = Brushes.Transparent;
            content.Children.Add(accent);

            TextBlock label = GlassTheme.CreateText(text, 13, true);
            label.Margin = new Thickness(13, 0, 10, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            content.Children.Add(label);

            button.Content = content;
            _navAccentBars[tag] = accent;
            _navLabels[tag] = label;
            button.Click += NavButton_Click;
            _navButtons.Add(button);
            return button;
        }

        private CheckBox CreateCheckBox(string text)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Content = text;
            checkBox.FontSize = 13;
            checkBox.Cursor = Cursors.Hand;
            checkBox.Style = GetSettingsCheckBoxStyle();
            return checkBox;
        }

        private RadioButton CreateRadioButton(string text)
        {
            RadioButton radioButton = new RadioButton();
            radioButton.Content = text;
            radioButton.FontSize = 13;
            radioButton.Cursor = Cursors.Hand;
            radioButton.Style = GetSettingsRadioButtonStyle();
            return radioButton;
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button != null)
            {
                Navigate((string)button.Tag);
            }
        }

        private void Navigate(string page)
        {
            _currentPage = page;
            foreach (Button button in _navButtons)
            {
                string tag = (string)button.Tag;
                bool active = string.Equals(tag, page, StringComparison.Ordinal);
                button.Background = active
                    ? AccentSoftBrush
                    : Brushes.Transparent;
                button.BorderBrush = active
                    ? SettingsBorderBrush
                    : Brushes.Transparent;
                button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;

                Border accent;
                if (_navAccentBars.TryGetValue(tag, out accent))
                {
                    accent.Background = active
                        ? GlassTheme.CreateBrush(255, 139, 226, 255)
                        : Brushes.Transparent;
                }

                TextBlock label;
                if (_navLabels.TryGetValue(tag, out label))
                {
                    label.Foreground = active
                        ? GlassTheme.TextPrimaryBrush
                        : GlassTheme.TextSecondaryBrush;
                    label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }

            if (string.Equals(page, "regions", StringComparison.Ordinal))
            {
                _pageTitle.Text = "分区管理";
                _pageSubtitle.Text = "新建、布局和管理桌面收纳分区";
                _pageHost.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                _pageHost.Content = BuildRegionsPage();
            }
            else if (string.Equals(page, "settings", StringComparison.Ordinal))
            {
                _pageTitle.Text = "隐身设置";
                _pageSubtitle.Text = "控制分区自动隐身与显示行为";
                _pageHost.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                _pageHost.Content = BuildSettingsPage();
            }
            else if (string.Equals(page, "launch", StringComparison.Ordinal))
            {
                _pageTitle.Text = "一键启动";
                _pageSubtitle.Text = "把常用软件绑定到现有分区，一次点击集中启动";
                _pageHost.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                _pageHost.Content = BuildLaunchPage();
            }
            else if (string.Equals(page, "schedule", StringComparison.Ordinal))
            {
                _pageTitle.Text = "定时启动";
                _pageSubtitle.Text = "按计划启动应用、文件或网址，也可定时关机";
                _pageHost.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                _pageHost.Content = BuildSchedulePage();
            }
            else if (string.Equals(page, "advanced", StringComparison.Ordinal))
            {
                _pageTitle.Text = "高级设置";
                _pageSubtitle.Text = "配置文件、自动备份和程序维护";
                _pageHost.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                _pageHost.Content = BuildAdvancedPage();
            }
            else
            {
                _pageTitle.Text = "关于软件";
                _pageSubtitle.Text = "GhostDesk 信息与支持";
                _pageHost.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                _pageHost.Content = BuildAboutPage();
            }
        }

        private UIElement BuildRegionsPage()
        {
            StackPanel page = new StackPanel();

            WrapPanel tools = new WrapPanel();
            tools.Margin = new Thickness(0, 0, 0, 8);

            Button add = GlassTheme.CreateButton("新建空白分区", ButtonKind.Primary);
            add.Margin = new Thickness(0, 0, 12, 8);
            add.Click += AddRegion_Click;
            tools.Children.Add(add);

            _enableSnapCheck = CreateCheckBox("分区成组吸附");
            _enableSnapCheck.IsChecked = _config.EnableSnap == true;
            _enableSnapCheck.Margin = new Thickness(0, 0, 18, 8);
            _enableSnapCheck.Checked += EnableSnap_Changed;
            _enableSnapCheck.Unchecked += EnableSnap_Changed;
            tools.Children.Add(_enableSnapCheck);

            _enableGuidesCheck = CreateCheckBox("对齐参考线");
            _enableGuidesCheck.IsChecked = _config.EnableGuides != false;
            _enableGuidesCheck.Margin = new Thickness(0, 0, 18, 8);
            _enableGuidesCheck.Checked += EnableGuides_Changed;
            _enableGuidesCheck.Unchecked += EnableGuides_Changed;
            tools.Children.Add(_enableGuidesCheck);

            _showExtensionsCheck = CreateCheckBox("显示文件扩展名");
            _showExtensionsCheck.IsChecked = AreAllRegionsShowingExtensions();
            _showExtensionsCheck.Margin = new Thickness(0, 0, 0, 8);
            _showExtensionsCheck.Checked += ShowExtensions_Changed;
            _showExtensionsCheck.Unchecked += ShowExtensions_Changed;
            _showExtensionsCheck.IsEnabled = _config.Regions != null && _config.Regions.Count > 0;
            tools.Children.Add(_showExtensionsCheck);

            page.Children.Add(tools);
            StackPanel dockPanel = new StackPanel();
            dockPanel.Margin = new Thickness(0, 0, 0, 8);

            Grid dockGrid = new Grid();
            dockGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dockGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            dockGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dockGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dockGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _edgeDockBandBox = CreateNumberBox(
                _config.GetEdgeDockBand(),
                0,
                240,
                1,
                EdgeDockBand_Changed);
            _edgeDockBandSlider = CreateDockSlider(
                _config.GetEdgeDockBand(),
                0,
                240,
                1,
                _edgeDockBandBox);
            AddDockField(dockGrid, 0, 0, "贴边灵敏度", _edgeDockBandSlider, _edgeDockBandBox);

            _safeTopBox = CreateNumberBox(
                _config.GetSafeInset(RegionConfig.DockTop),
                0,
                400,
                4,
                SafeInset_Changed);
            _safeTopSlider = CreateDockSlider(
                _config.GetSafeInset(RegionConfig.DockTop),
                0,
                400,
                4,
                _safeTopBox);
            AddDockField(dockGrid, 0, 2, "顶部安全边距", _safeTopSlider, _safeTopBox);

            _safeBottomBox = CreateNumberBox(
                _config.GetSafeInset(RegionConfig.DockBottom),
                0,
                400,
                4,
                SafeInset_Changed);
            _safeBottomSlider = CreateDockSlider(
                _config.GetSafeInset(RegionConfig.DockBottom),
                0,
                400,
                4,
                _safeBottomBox);
            AddDockField(dockGrid, 1, 0, "底部安全边距", _safeBottomSlider, _safeBottomBox);

            double sideInset = Math.Max(
                _config.GetSafeInset(RegionConfig.DockLeft),
                _config.GetSafeInset(RegionConfig.DockRight));
            _safeSideBox = CreateNumberBox(sideInset, 0, 400, 4, SafeInset_Changed);
            _safeSideSlider = CreateDockSlider(sideInset, 0, 400, 4, _safeSideBox);
            AddDockField(dockGrid, 1, 2, "左右安全边距", _safeSideSlider, _safeSideBox);

            dockPanel.Children.Add(dockGrid);

            TextBlock dockHint = GlassTheme.CreateText(
                "每个分区的朝向在分区右键「贴边方向」里单独设置（自动判断 / 固定四边 / 不贴边），"
                    + "灵敏度只影响「自动判断」。滑块、输入框、鼠标滚轮三者联动。",
                12,
                true);
            dockHint.TextWrapping = TextWrapping.Wrap;
            dockHint.Margin = new Thickness(0, 4, 0, 0);
            dockPanel.Children.Add(dockHint);

            page.Children.Add(dockPanel);

            StackPanel materialPanel = new StackPanel();
            materialPanel.Margin = new Thickness(0, 0, 0, 8);

            TextBlock materialTitle = GlassTheme.CreateText("分区材质", 13, false);
            materialTitle.FontWeight = FontWeights.SemiBold;
            materialTitle.Margin = new Thickness(0, 0, 0, 4);
            materialPanel.Children.Add(materialTitle);

            WrapPanel presets = new WrapPanel();
            presets.Margin = new Thickness(0, 0, 0, 6);
            presets.Children.Add(CreateMaterialPresetButton("高透玻璃", AppConfig.MaterialGlass));
            presets.Children.Add(CreateMaterialPresetButton("磨砂玻璃", AppConfig.MaterialFrosted));
            presets.Children.Add(CreateMaterialPresetButton("纯色块", AppConfig.MaterialSolid));
            materialPanel.Children.Add(presets);

            Grid materialGrid = new Grid();
            materialGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            materialGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            materialGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            materialGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            materialGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int matOpacity = _config.MaterialOpacity ?? AppConfig.DefaultMaterialOpacity;
            _matOpacityBox = CreateNumberBox(matOpacity, 0, 100, 4, Material_Changed);
            _matOpacitySlider = CreateMaterialSlider(matOpacity, _matOpacityBox);
            AddDockField(materialGrid, 0, 0, "背景不透明度", _matOpacitySlider, _matOpacityBox);

            int matTint = _config.MaterialTint ?? AppConfig.DefaultMaterialTint;
            _matTintBox = CreateNumberBox(matTint, 0, 100, 4, Material_Changed);
            _matTintSlider = CreateMaterialSlider(matTint, _matTintBox);
            AddDockField(materialGrid, 0, 2, "底色深浅", _matTintSlider, _matTintBox);

            int matBorder = _config.MaterialBorder ?? AppConfig.DefaultMaterialBorder;
            _matBorderBox = CreateNumberBox(matBorder, 0, 100, 4, Material_Changed);
            _matBorderSlider = CreateMaterialSlider(matBorder, _matBorderBox);
            AddDockField(materialGrid, 1, 0, "描边强度", _matBorderSlider, _matBorderBox);

            int matShadow = _config.MaterialShadow ?? AppConfig.DefaultMaterialShadow;
            _matShadowBox = CreateNumberBox(matShadow, 0, 100, 4, Material_Changed);
            _matShadowSlider = CreateMaterialSlider(matShadow, _matShadowBox);
            AddDockField(materialGrid, 1, 2, "阴影强度", _matShadowSlider, _matShadowBox);

            materialPanel.Children.Add(materialGrid);

            TextBlock materialHint = GlassTheme.CreateText(
                "想单独调某个分区：右键该分区 →「外观材质...」。这里的数值是所有分区的默认值。",
                12,
                true);
            materialHint.TextWrapping = TextWrapping.Wrap;
            materialHint.Margin = new Thickness(0, 6, 0, 0);
            materialPanel.Children.Add(materialHint);

            if (MaterialUiEnabled)
            {
                page.Children.Add(materialPanel);
            }

            TextBlock hint = GlassTheme.CreateText(
                "拖到屏幕边缘始终会贴边。开启分区成组吸附后，相邻分区会按松开位置排序，并可成组拖动。扩展名设置会统一应用到所有分区。",
                12,
                true);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 12);
            page.Children.Add(hint);

            _regionList = new StackPanel();
            page.Children.Add(_regionList);

            RefreshRegionList();
            return page;
        }

        private void RefreshRegionList()
        {
            _regionList.Children.Clear();
            if (_config.Regions == null || _config.Regions.Count == 0)
            {
                TextBlock empty = GlassTheme.CreateText(
                    "还没有分区。点击上方按钮新建一个空白分区。",
                    13,
                    true);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.VerticalAlignment = VerticalAlignment.Center;
                empty.Margin = new Thickness(0, 28, 0, 0);
                _regionList.Children.Add(empty);
                return;
            }

            for (int i = 0; i < _config.Regions.Count; i++)
            {
                RegionConfig region = _config.Regions[i];
                _regionList.Children.Add(BuildRegionCard(region, i + 1));
            }
        }

        private UIElement BuildRegionCard(RegionConfig region, int index)
        {
            Border card = new Border();
            card.Margin = new Thickness(0, 0, 0, 10);
            card.Padding = new Thickness(14);
            card.Background = CardSurfaceBrush;
            card.BorderBrush = SettingsBorderBrush;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(12);

            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(CreateRegionGlyph());

            StackPanel info = new StackPanel();
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(13, 0, 12, 0);

            TextBlock name = GlassTheme.CreateText(region.Name ?? "新分区", 15, false);
            name.FontWeight = FontWeights.SemiBold;
            info.Children.Add(name);

            int count = region.Items == null ? 0 : region.Items.Count;
            TextBlock meta = GlassTheme.CreateText(
                "第 " + index + " 个分区 · " + count + " 个图标 · " +
                (int)region.Width + " × " + (int)region.Height,
                12,
                true);
            meta.Margin = new Thickness(0, 5, 0, 0);
            info.Children.Add(meta);
            Grid.SetColumn(info, 1);
            row.Children.Add(info);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.VerticalAlignment = VerticalAlignment.Center;

            Button reset = GlassTheme.CreateButton("重置位置", ButtonKind.Ghost);
            reset.Margin = new Thickness(0, 0, 8, 0);
            reset.Tag = region.Id;
            reset.Click += ResetRegion_Click;
            actions.Children.Add(reset);

            Button remove = GlassTheme.CreateButton("解除区域", ButtonKind.Danger);
            remove.Tag = region.Id;
            remove.Click += RemoveRegion_Click;
            actions.Children.Add(remove);

            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            card.Child = row;
            return card;
        }

        private UIElement CreateRegionGlyph()
        {
            Border glyph = new Border();
            glyph.Width = 46;
            glyph.Height = 46;
            glyph.CornerRadius = new CornerRadius(12);
            glyph.Background = GlassTheme.CreateBrush(42, 112, 208, 255);
            glyph.VerticalAlignment = VerticalAlignment.Center;

            Grid inner = new Grid();
            inner.Width = 26;
            inner.Height = 22;
            inner.HorizontalAlignment = HorizontalAlignment.Center;
            inner.VerticalAlignment = VerticalAlignment.Center;

            Border top = new Border();
            top.Width = 26;
            top.Height = 7;
            top.HorizontalAlignment = HorizontalAlignment.Left;
            top.VerticalAlignment = VerticalAlignment.Top;
            top.Background = GlassTheme.CreateBrush(245, 255, 255, 255);
            top.CornerRadius = new CornerRadius(2);
            inner.Children.Add(top);

            Border left = new Border();
            left.Width = 12;
            left.Height = 11;
            left.HorizontalAlignment = HorizontalAlignment.Left;
            left.VerticalAlignment = VerticalAlignment.Bottom;
            left.Background = GlassTheme.CreateBrush(180, 224, 241, 255);
            left.CornerRadius = new CornerRadius(2);
            inner.Children.Add(left);

            Border right = new Border();
            right.Width = 12;
            right.Height = 11;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Bottom;
            right.Background = GlassTheme.CreateBrush(180, 224, 241, 255);
            right.CornerRadius = new CornerRadius(2);
            inner.Children.Add(right);

            glyph.Child = inner;
            return glyph;
        }

        private UIElement BuildSettingsPage()
        {
            StackPanel panel = new StackPanel();

            _hideDelaySlider = CreateSlider(0, 20, Math.Max(0, _config.IdleSeconds), 0.5, 1, 0.5);
            _hideDelayValue = new TextBlock();
            _hideDelayValue.Text = _hideDelaySlider.Value.ToString("0.#") + " 秒";
            _hideDelayValue.HorizontalAlignment = HorizontalAlignment.Right;
            _hideDelayValue.Foreground = GlassTheme.TextPrimaryBrush;
            _hideDelaySlider.ValueChanged += HideDelay_Changed;
            panel.Children.Add(CreateSettingLabel("鼠标移出后隐藏延迟"));
            panel.Children.Add(_hideDelaySlider);
            panel.Children.Add(_hideDelayValue);

            _fadeSlider = CreateSlider(0.1, 3, Math.Max(0.1, _config.FadeSeconds), 0.1, 0.2, 0.1);
            _fadeValue = new TextBlock();
            _fadeValue.Text = _fadeSlider.Value.ToString("0.#") + " 秒";
            _fadeValue.HorizontalAlignment = HorizontalAlignment.Right;
            _fadeValue.Foreground = GlassTheme.TextPrimaryBrush;
            _fadeSlider.ValueChanged += Fade_Changed;
            panel.Children.Add(CreateSettingLabel("隐身动画时长"));
            panel.Children.Add(_fadeSlider);
            panel.Children.Add(_fadeValue);

            panel.Children.Add(CreateSettingLabel("收起后临时展开触发范围"));

            WrapPanel collapseHoverPanel = new WrapPanel();
            collapseHoverPanel.Margin = new Thickness(0, 8, 0, 6);

            bool collapseHoverRow = string.Equals(
                _config.CollapseHoverTrigger,
                AppConfig.CollapseHoverRow,
                StringComparison.OrdinalIgnoreCase);

            _collapseHoverButtonRadio = CreateRadioButton("仅折叠按钮");
            _collapseHoverButtonRadio.GroupName = "GhostDeskCollapseHover";
            _collapseHoverButtonRadio.IsChecked = !collapseHoverRow;
            _collapseHoverButtonRadio.Margin = new Thickness(0, 0, 24, 6);
            collapseHoverPanel.Children.Add(_collapseHoverButtonRadio);

            _collapseHoverRowRadio = CreateRadioButton("整个标题栏");
            _collapseHoverRowRadio.GroupName = "GhostDeskCollapseHover";
            _collapseHoverRowRadio.IsChecked = collapseHoverRow;
            _collapseHoverRowRadio.Margin = new Thickness(0, 0, 0, 6);
            collapseHoverPanel.Children.Add(_collapseHoverRowRadio);

            _collapseHoverButtonRadio.Checked += CollapseHover_Changed;
            _collapseHoverRowRadio.Checked += CollapseHover_Changed;
            panel.Children.Add(collapseHoverPanel);

            return panel;
        }

        private Slider CreateSlider(
            double minimum,
            double maximum,
            double value,
            double smallChange,
            double largeChange,
            double tickFrequency)
        {
            Slider slider = new Slider();
            slider.Minimum = minimum;
            slider.Maximum = maximum;
            slider.Value = value;
            slider.SmallChange = smallChange;
            slider.LargeChange = largeChange;
            slider.TickFrequency = tickFrequency;
            slider.IsSnapToTickEnabled = true;
            slider.IsMoveToPointEnabled = true;
            slider.Cursor = Cursors.Hand;
            slider.Style = GetSettingsSliderStyle();
            slider.Margin = new Thickness(0, 6, 0, 8);
            return slider;
        }

        private UIElement BuildLaunchPage()
        {
            StackPanel page = new StackPanel();

            TextBlock hint = GlassTheme.CreateText(
                "在分区图标上右键选择“加入一键启动”，即可把常用软件绑定到对应分区。",
                12,
                true);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 14);
            page.Children.Add(hint);

            page.Children.Add(CreateSettingLabel("启动配置总览"));
            _launchOverviewHost = new StackPanel();
            _launchOverviewHost.Margin = new Thickness(0, 6, 0, 18);
            page.Children.Add(_launchOverviewHost);

            page.Children.Add(CreateSettingLabel("启动间隔"));
            _launchIntervalSlider = CreateSlider(
                0,
                3000,
                Math.Max(0, _config.LaunchIntervalMs),
                50,
                200,
                50);
            _launchIntervalValue = new TextBlock();
            _launchIntervalValue.Text = ((int)_launchIntervalSlider.Value).ToString() + " 毫秒";
            _launchIntervalValue.HorizontalAlignment = HorizontalAlignment.Right;
            _launchIntervalValue.Foreground = GlassTheme.TextPrimaryBrush;
            _launchIntervalSlider.ValueChanged += LaunchInterval_Changed;
            page.Children.Add(_launchIntervalSlider);
            page.Children.Add(_launchIntervalValue);

            RefreshLaunchOverview();
            return page;
        }

        public void RefreshLaunchPage()
        {
            if (string.Equals(_currentPage, "launch", StringComparison.Ordinal) &&
                _pageHost != null)
            {
                _pageHost.Content = BuildLaunchPage();
            }
        }


        private void RefreshLaunchOverview()
        {
            if (_launchOverviewHost == null)
            {
                return;
            }

            _launchOverviewHost.Children.Clear();
            bool hasItems = false;

            if (_config.Regions != null)
            {
                foreach (RegionConfig region in _config.Regions)
                {
                    if (region == null || region.LaunchItems == null ||
                        region.LaunchItems.Count == 0)
                    {
                        continue;
                    }

                    hasItems = true;

                    Border card = new Border();
                    card.Margin = new Thickness(0, 0, 0, 10);
                    card.Padding = new Thickness(12);
                    card.Background = CardSurfaceBrush;
                    card.BorderBrush = SettingsBorderBrush;
                    card.BorderThickness = new Thickness(1);
                    card.CornerRadius = new CornerRadius(12);
                    StackPanel cardContent = new StackPanel();

                    TextBlock regionName = GlassTheme.CreateText(
                        region.Name ?? "未命名分区",
                        13,
                        false);
                    regionName.FontWeight = FontWeights.SemiBold;
                    regionName.TextTrimming = TextTrimming.CharacterEllipsis;
                    cardContent.Children.Add(regionName);

                    for (int i = 0; i < region.LaunchItems.Count; i++)
                    {
                        DesktopItem item = region.LaunchItems[i];
                        if (item == null)
                        {
                            continue;
                        }

                        string displayName = item.Name;
                        if (string.IsNullOrEmpty(displayName))
                        {
                            displayName = string.IsNullOrEmpty(item.Path)
                                ? "未命名项目"
                                : Path.GetFileName(item.Path);
                        }

                        Border row = new Border();
                        row.Margin = new Thickness(0, 7, 0, 0);
                        row.Padding = new Thickness(10);
                        row.Background = GlassTheme.CreateBrush(28, 14, 19, 32);
                        row.BorderBrush = SettingsBorderBrush;
                        row.BorderThickness = new Thickness(1);
                        row.CornerRadius = new CornerRadius(10);

                        Grid rowGrid = new Grid();
                        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        StackPanel info = new StackPanel();
                        info.VerticalAlignment = VerticalAlignment.Center;
                        info.Margin = new Thickness(0, 0, 8, 0);

                        TextBlock itemName = GlassTheme.CreateText(displayName, 12, false);
                        itemName.TextWrapping = TextWrapping.Wrap;
                        itemName.TextTrimming = TextTrimming.CharacterEllipsis;
                        info.Children.Add(itemName);

                        if (!string.IsNullOrEmpty(item.Path))
                        {
                            TextBlock itemPath = GlassTheme.CreateText(item.Path, 11, true);
                            itemPath.Margin = new Thickness(0, 2, 0, 0);
                            itemPath.TextWrapping = TextWrapping.Wrap;
                            itemPath.TextTrimming = TextTrimming.CharacterEllipsis;
                            itemPath.Opacity = 0.76;
                            info.Children.Add(itemPath);
                        }

                        Button remove = GlassTheme.CreateButton("删除", ButtonKind.Danger);
                        remove.MinWidth = 54;
                        remove.Tag = item;
                        remove.Click += RemoveLaunchOverviewItem_Click;
                        Grid.SetColumn(info, 0);
                        Grid.SetColumn(remove, 1);
                        rowGrid.Children.Add(info);
                        rowGrid.Children.Add(remove);
                        row.Child = rowGrid;
                        cardContent.Children.Add(row);
                    }

                    card.Child = cardContent;
                    _launchOverviewHost.Children.Add(card);
                }
            }

            if (!hasItems)
            {
                TextBlock empty = GlassTheme.CreateText(
                    "还没有配置一键启动。可在分区图标上右键，或在上方选择分区后添加。",
                    12,
                    true);
                empty.TextWrapping = TextWrapping.Wrap;
                _launchOverviewHost.Children.Add(empty);
            }
        }

        private void RemoveLaunchOverviewItem_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            DesktopItem item = button == null ? null : button.Tag as DesktopItem;
            if (item == null || _config.Regions == null)
            {
                return;
            }

            foreach (RegionConfig region in _config.Regions)
            {
                if (region != null && region.LaunchItems != null &&
                    region.LaunchItems.Remove(item))
                {
                    break;
                }
            }

            ConfigStore.Save(_config);
            _desktop.RefreshRegionLaunchButtons();
            RefreshLaunchOverview();
        }

        private void LaunchInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_launchIntervalValue == null)
            {
                return;
            }

            _config.LaunchIntervalMs = (int)Math.Round(e.NewValue, 0);
            _launchIntervalValue.Text = _config.LaunchIntervalMs.ToString() + " 毫秒";
        }

        private UIElement BuildSchedulePage()
        {
            StackPanel page = new StackPanel();

            Grid summary = new Grid();
            summary.Margin = new Thickness(0, 0, 0, 10);
            summary.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            summary.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            StackPanel summaryText = new StackPanel();
            TextBlock heading = GlassTheme.CreateText("任务清单", 14, false);
            heading.FontWeight = FontWeights.SemiBold;
            summaryText.Children.Add(heading);

            _scheduleCountText = GlassTheme.CreateText(
                GetScheduledTaskCountText(),
                11,
                true);
            _scheduleCountText.Margin = new Thickness(0, 3, 0, 0);
            summaryText.Children.Add(_scheduleCountText);
            summary.Children.Add(summaryText);

            Button add = GlassTheme.CreateButton("添加任务", ButtonKind.Primary);
            add.MinWidth = 92;
            add.Click += AddScheduledTask_Click;
            Grid.SetColumn(add, 1);
            summary.Children.Add(add);
            page.Children.Add(summary);

            Grid search = CreateScheduleSearchBox();
            search.Margin = new Thickness(0, 0, 0, 8);
            page.Children.Add(search);

            TextBlock hint = GlassTheme.CreateText(
                "任务按设定的绝对时间执行；开启提前提醒后，可在执行前取消或立即运行。",
                11,
                true);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 12);
            hint.Opacity = 0.8;
            page.Children.Add(hint);

            _scheduleList = new StackPanel();
            page.Children.Add(_scheduleList);
            RefreshScheduleList();
            return page;
        }

        public void RefreshSchedulePage()
        {
            if (string.Equals(_currentPage, "schedule", StringComparison.Ordinal) &&
                _pageHost != null)
            {
                _pageHost.Content = BuildSchedulePage();
            }
        }

        public void ShowSchedulePage()
        {
            Navigate("schedule");

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private string GetScheduledTaskCountText()
        {
            int count = _config.ScheduledTasks == null
                ? 0
                : _config.ScheduledTasks.Count;
            return "共 " + count.ToString() + " 个任务";
        }

        private void RefreshScheduleList()
        {
            if (_scheduleList == null)
            {
                return;
            }

            _scheduleList.Children.Clear();
            int totalCount = _config.ScheduledTasks == null
                ? 0
                : _config.ScheduledTasks.Count;
            int visibleCount = 0;

            if (totalCount == 0)
            {
                TextBlock empty = GlassTheme.CreateText(
                    "还没有定时任务。点击右上角“添加任务”开始配置。",
                    12,
                    true);
                empty.TextWrapping = TextWrapping.Wrap;
                empty.Margin = new Thickness(2, 10, 0, 0);
                _scheduleList.Children.Add(empty);
                UpdateScheduleCountText(_scheduleCountText, totalCount, visibleCount);
                return;
            }

            foreach (ScheduledTaskConfig task in _config.ScheduledTasks)
            {
                if (task != null && MatchesScheduleSearch(task))
                {
                    _scheduleList.Children.Add(BuildScheduledTaskCard(task));
                    visibleCount++;
                }
            }

            if (visibleCount == 0)
            {
                TextBlock empty = GlassTheme.CreateText(
                    "没有找到匹配的定时任务。",
                    12,
                    true);
                empty.Margin = new Thickness(2, 10, 0, 0);
                _scheduleList.Children.Add(empty);
            }

            UpdateScheduleCountText(_scheduleCountText, totalCount, visibleCount);
        }

        private UIElement BuildScheduledTaskCard(ScheduledTaskConfig task)
        {
            Border card = new Border();
            card.Margin = new Thickness(0, 0, 0, 8);
            card.Padding = new Thickness(11);
            card.Background = CardSurfaceBrush;
            card.BorderBrush = SettingsBorderBrush;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(11);

            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            row.Children.Add(CreateScheduleGlyph());

            StackPanel info = new StackPanel();
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(11, 0, 12, 0);

            Grid titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            TextBlock name = GlassTheme.CreateText(
                string.IsNullOrWhiteSpace(task.Name) ? "定时任务" : task.Name,
                13,
                false);
            name.FontWeight = FontWeights.SemiBold;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(name);

            Border typeBadge = CreateScheduleBadge(
                GetTaskTypeLabel(task.TaskType),
                GlassTheme.CreateBrush(38, 112, 208, 255),
                GlassTheme.CreateBrush(64, 139, 226, 255));
            typeBadge.Margin = new Thickness(7, 0, 0, 0);
            Grid.SetColumn(typeBadge, 1);
            titleRow.Children.Add(typeBadge);

            Border statusBadge = CreateScheduleBadge(
                task.Enabled ? "已启用" : "已暂停",
                task.Enabled
                    ? GlassTheme.CreateBrush(28, 83, 214, 142)
                    : GlassTheme.CreateBrush(28, 255, 112, 132),
                task.Enabled
                    ? GlassTheme.CreateBrush(52, 104, 226, 164)
                    : GlassTheme.CreateBrush(52, 255, 128, 146));
            statusBadge.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(statusBadge, 2);
            titleRow.Children.Add(statusBadge);
            info.Children.Add(titleRow);

            TextBlock schedule = GlassTheme.CreateText(
                DescribeSchedule(task),
                11,
                false);
            schedule.Margin = new Thickness(0, 5, 0, 0);
            schedule.TextTrimming = TextTrimming.CharacterEllipsis;
            info.Children.Add(schedule);

            string targetText = GetTaskTargetText(task);
            if (targetText.Length > 0)
            {
                TextBlock target = GlassTheme.CreateText(targetText, 10, true);
                target.Margin = new Thickness(0, 3, 0, 0);
                target.TextTrimming = TextTrimming.CharacterEllipsis;
                target.Opacity = 0.72;
                info.Children.Add(target);
            }

            Grid.SetColumn(info, 1);
            row.Children.Add(info);

            StackPanel actions = new StackPanel();
            actions.VerticalAlignment = VerticalAlignment.Center;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Width = 104;

            CheckBox enabled = GlassTheme.CreateToggleCheckBox("启用");
            enabled.Tag = task;
            enabled.IsChecked = task.Enabled;
            enabled.HorizontalAlignment = HorizontalAlignment.Right;
            enabled.Checked += ScheduleEnabled_Changed;
            enabled.Unchecked += ScheduleEnabled_Changed;
            actions.Children.Add(enabled);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 5, 0, 0);

            Button edit = GlassTheme.CreateButton("编辑", ButtonKind.Ghost);
            edit.Tag = task;
            edit.Height = 30;
            edit.MinWidth = 46;
            edit.Padding = new Thickness(9, 0, 9, 0);
            edit.Margin = new Thickness(0, 0, 6, 0);
            edit.Click += EditScheduledTask_Click;
            buttons.Children.Add(edit);

            Button remove = GlassTheme.CreateButton("删除", ButtonKind.Danger);
            remove.Tag = task;
            remove.Height = 30;
            remove.MinWidth = 46;
            remove.Padding = new Thickness(9, 0, 9, 0);
            remove.Click += RemoveScheduledTask_Click;
            buttons.Children.Add(remove);
            actions.Children.Add(buttons);

            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            card.Child = row;
            return card;
        }

        private static Border CreateScheduleBadge(
            string text,
            Brush background,
            Brush borderBrush)
        {
            Border badge = new Border();
            badge.Padding = new Thickness(6, 2, 6, 2);
            badge.CornerRadius = new CornerRadius(6);
            badge.Background = background;
            badge.BorderBrush = borderBrush;
            badge.BorderThickness = new Thickness(1);
            badge.VerticalAlignment = VerticalAlignment.Center;

            TextBlock label = GlassTheme.CreateText(text, 9, false);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            badge.Child = label;
            return badge;
        }

        private UIElement CreateScheduleGlyph()
        {
            Border glyph = new Border();
            glyph.Width = 44;
            glyph.Height = 44;
            glyph.CornerRadius = new CornerRadius(12);
            glyph.Background = GlassTheme.CreateBrush(42, 112, 208, 255);
            glyph.BorderBrush = GlassTheme.CreateBrush(54, 139, 226, 255);
            glyph.BorderThickness = new Thickness(1);
            glyph.VerticalAlignment = VerticalAlignment.Center;

            Grid clock = new Grid();
            clock.Width = 25;
            clock.Height = 25;
            clock.HorizontalAlignment = HorizontalAlignment.Center;
            clock.VerticalAlignment = VerticalAlignment.Center;

            Border ring = new Border();
            ring.Width = 25;
            ring.Height = 25;
            ring.CornerRadius = new CornerRadius(12.5);
            ring.BorderBrush = GlassTheme.TextPrimaryBrush;
            ring.BorderThickness = new Thickness(1.6);
            clock.Children.Add(ring);

            Border hour = new Border();
            hour.Width = 1.6;
            hour.Height = 7;
            hour.Background = GlassTheme.TextPrimaryBrush;
            hour.HorizontalAlignment = HorizontalAlignment.Center;
            hour.VerticalAlignment = VerticalAlignment.Top;
            hour.Margin = new Thickness(0, 4, 0, 0);
            hour.CornerRadius = new CornerRadius(0.8);
            clock.Children.Add(hour);

            Border minute = new Border();
            minute.Width = 7;
            minute.Height = 1.6;
            minute.Background = GlassTheme.TextPrimaryBrush;
            minute.HorizontalAlignment = HorizontalAlignment.Right;
            minute.VerticalAlignment = VerticalAlignment.Center;
            minute.Margin = new Thickness(0, 0, 4, 0);
            minute.CornerRadius = new CornerRadius(0.8);
            clock.Children.Add(minute);

            glyph.Child = clock;
            return glyph;
        }

        private Grid CreateScheduleSearchBox()
        {
            Grid wrapper = new Grid();
            wrapper.Height = 36;

            TextBox input = new TextBox();
            input.Height = 36;
            input.Padding = new Thickness(12, 6, 12, 6);
            input.VerticalContentAlignment = VerticalAlignment.Center;
            input.Background = GlassTheme.InputBrush;
            input.BorderBrush = GlassTheme.InputBorderBrush;
            input.Foreground = GlassTheme.TextPrimaryBrush;
            input.CaretBrush = GlassTheme.TextPrimaryBrush;
            input.ToolTip = "搜索任务名称、目标、类型或执行计划";
            input.Text = _scheduleSearchText ?? "";
            input.Style = GetSettingsTextBoxStyle();
            wrapper.Children.Add(input);

            TextBlock placeholder = GlassTheme.CreateText(
                "搜索任务名称、路径或执行计划",
                12,
                true);
            placeholder.Margin = new Thickness(13, 0, 13, 0);
            placeholder.VerticalAlignment = VerticalAlignment.Center;
            placeholder.IsHitTestVisible = false;
            placeholder.Visibility = string.IsNullOrEmpty(input.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            wrapper.Children.Add(placeholder);

            input.TextChanged += delegate(object sender, TextChangedEventArgs e)
            {
                placeholder.Visibility = string.IsNullOrEmpty(input.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                ScheduleSearch_TextChanged(sender, e);
            };
            return wrapper;
        }

        private void ScheduleSearch_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            TextBox input = sender as TextBox;
            _scheduleSearchText = input == null ? "" : (input.Text ?? "");
            RefreshScheduleList();
        }

        private bool MatchesScheduleSearch(ScheduledTaskConfig task)
        {
            string query = (_scheduleSearchText ?? "").Trim();
            if (task == null)
            {
                return false;
            }

            if (query.Length == 0)
            {
                return true;
            }

            return ContainsScheduleText(task.Name, query) ||
                ContainsScheduleText(task.Target, query) ||
                ContainsScheduleText(GetTaskTypeLabel(task.TaskType), query) ||
                ContainsScheduleText(DescribeSchedule(task), query);
        }

        private static void UpdateScheduleCountText(
            TextBlock target,
            int totalCount,
            int visibleCount)
        {
            if (target == null)
            {
                return;
            }

            target.Text = visibleCount < totalCount
                ? "显示 " + visibleCount.ToString() + " / 共 " +
                    totalCount.ToString() + " 个任务"
                : "共 " + totalCount.ToString() + " 个任务";
        }

        private static bool ContainsScheduleText(string source, string query)
        {
            return !string.IsNullOrEmpty(source) &&
                source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Style GetSettingsTextBoxStyle()
        {
            if (_settingsTextBoxStyle != null)
            {
                return _settingsTextBoxStyle;
            }

            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type TextBox}'>" +
                "<Setter Property='FontFamily' Value='Microsoft YaHei UI'/>" +
                "<Setter Property='FontSize' Value='12'/>" +
                "<Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type TextBox}'>" +
                "<Border x:Name='Border' CornerRadius='9' " +
                "Background='{TemplateBinding Background}' " +
                "BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'>" +
                "<ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' " +
                "VerticalAlignment='Center'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='Border' Property='BorderBrush' Value='#70FFFFFF'/>" +
                "</Trigger>" +
                "<Trigger Property='IsKeyboardFocusWithin' Value='True'>" +
                "<Setter TargetName='Border' Property='BorderBrush' Value='#A88BE2FF'/>" +
                "<Setter TargetName='Border' Property='Background' Value='#3A152235'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>";
            _settingsTextBoxStyle = (Style)XamlReader.Parse(xaml);
            return _settingsTextBoxStyle;
        }

        private void AddScheduledTask_Click(object sender, RoutedEventArgs e)
        {
            ScheduledTaskWindow dialog = new ScheduledTaskWindow();
            dialog.Owner = this;
            // WPF 的 ShowDialog 会禁用整个应用的窗口（包括桌面分区），改用非模态
            dialog.NonModal = true;
            dialog.Closed += delegate
            {
                if (!dialog.ResultAccepted || dialog.ResultTask == null)
                {
                    return;
                }

                if (_config.ScheduledTasks == null)
                {
                    _config.ScheduledTasks = new List<ScheduledTaskConfig>();
                }

                _config.ScheduledTasks.Add(dialog.ResultTask);
                ConfigStore.Save(_config);

                if (_scheduledTasks != null)
                {
                    _scheduledTasks.CheckNow();
                }

                RefreshSchedulePage();
            };
            dialog.Show();
        }

        private void EditScheduledTask_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            ScheduledTaskConfig task = button == null
                ? null
                : button.Tag as ScheduledTaskConfig;
            if (task == null || _config.ScheduledTasks == null)
            {
                return;
            }

            int index = _config.ScheduledTasks.IndexOf(task);
            if (index < 0)
            {
                return;
            }

            ScheduledTaskWindow dialog = new ScheduledTaskWindow(task);
            dialog.Owner = this;
            dialog.NonModal = true;
            dialog.Closed += delegate
            {
                if (!dialog.ResultAccepted || dialog.ResultTask == null)
                {
                    return;
                }

                if (_config.ScheduledTasks == null ||
                    index >= _config.ScheduledTasks.Count)
                {
                    return;
                }

                _config.ScheduledTasks[index] = dialog.ResultTask;
                ConfigStore.Save(_config);

                if (_scheduledTasks != null)
                {
                    _scheduledTasks.CheckNow();
                }

                RefreshSchedulePage();
            };
            dialog.Show();
        }

        private void ScheduleEnabled_Changed(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            ScheduledTaskConfig task = checkBox == null
                ? null
                : checkBox.Tag as ScheduledTaskConfig;
            if (task == null)
            {
                return;
            }

            task.Enabled = checkBox.IsChecked == true;
            ConfigStore.Save(_config);

            if (_scheduledTasks != null)
            {
                _scheduledTasks.CheckNow();
            }

            RefreshSchedulePage();
        }

        private void RemoveScheduledTask_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            ScheduledTaskConfig task = button == null
                ? null
                : button.Tag as ScheduledTaskConfig;
            if (task == null || _config.ScheduledTasks == null)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                "确定删除定时任务“" +
                (string.IsNullOrWhiteSpace(task.Name) ? "定时任务" : task.Name) +
                "”吗？",
                "GhostDesk",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _config.ScheduledTasks.Remove(task);
            ConfigStore.Save(_config);
            RefreshSchedulePage();
        }

        private static string GetTaskTypeLabel(string taskType)
        {
            if (string.Equals(
                    taskType,
                    ScheduledTaskConfig.TypeApplication,
                    StringComparison.Ordinal))
            {
                return "应用";
            }

            if (string.Equals(
                    taskType,
                    ScheduledTaskConfig.TypeUrl,
                    StringComparison.Ordinal))
            {
                return "网址";
            }

            if (string.Equals(
                    taskType,
                    ScheduledTaskConfig.TypeShutdown,
                    StringComparison.Ordinal))
            {
                return "关机";
            }

            return "文件";
        }

        private static string DescribeSchedule(ScheduledTaskConfig task)
        {
            DateTime firstRun;
            string firstRunText = "";
            if (DateTime.TryParse(task.FirstRunUtc, out firstRun))
            {
                firstRunText = firstRun.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }

            string description;
            if (string.Equals(
                    task.ScheduleMode,
                    ScheduledTaskConfig.ModeDaily,
                    StringComparison.Ordinal))
            {
                description = "每天 " + (task.Time ?? "") +
                    (firstRunText.Length == 0 ? "" : " · 首次 " + firstRunText);
            }
            else if (string.Equals(
                    task.ScheduleMode,
                    ScheduledTaskConfig.ModeWeekly,
                    StringComparison.Ordinal))
            {
                description = "每周" + GetWeekdayLabel(task.DayOfWeek) +
                    " " + (task.Time ?? "") +
                    (firstRunText.Length == 0 ? "" : " · 首次 " + firstRunText);
            }
            else if (string.Equals(
                    task.ScheduleMode,
                    ScheduledTaskConfig.ModeInterval,
                    StringComparison.Ordinal))
            {
                description = "每隔 " + task.IntervalMinutes.ToString() +
                    " 分钟" +
                    (firstRunText.Length == 0 ? "" : " · 首次 " + firstRunText);
            }
            else
            {
                description = "一次性 · " +
                    (firstRunText.Length == 0 ? (task.Time ?? "") : firstRunText);
            }

            int countdown = ScheduledTaskRunner.GetEffectiveCountdownSeconds(task);
            if (countdown > 0)
            {
                description += " · 提前 " +
                    countdown.ToString() + " 秒提醒";
            }
            else
            {
                description += " · 准点执行";
            }

            return description;
        }

        private static string GetWeekdayLabel(int dayOfWeek)
        {
            string[] names = new string[]
            {
                "日", "一", "二", "三", "四", "五", "六"
            };
            int index = Math.Max(0, Math.Min(names.Length - 1, dayOfWeek));
            return names[index];
        }

        private static string GetTaskTargetText(ScheduledTaskConfig task)
        {
            if (string.Equals(
                    task.TaskType,
                    ScheduledTaskConfig.TypeShutdown,
                    StringComparison.Ordinal))
            {
                return "";
            }

            string target = (task.Target ?? "").Trim();
            if (target.Length == 0)
            {
                return "";
            }

            return "目标：" + target;
        }


        private UIElement BuildAdvancedPage()
        {
            StackPanel panel = new StackPanel();

            panel.Children.Add(CreateSettingLabel("配置文件"));

            TextBlock configPath = GlassTheme.CreateText(ConfigStore.ConfigPath, 12, true);
            configPath.TextWrapping = TextWrapping.Wrap;
            configPath.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(configPath);

            panel.Children.Add(CreateSettingLabel("备份与恢复"));

            Grid backupHeader = new Grid();
            backupHeader.Margin = new Thickness(0, 12, 0, 8);
            backupHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            backupHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock backupHint = GlassTheme.CreateText(
                "创建配置备份，在误删分区或配置错乱后可一键恢复。恢复后软件会自动重启以应用结果。",
                12,
                true);
            backupHint.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(backupHint, 0);
            backupHeader.Children.Add(backupHint);

            Button createBackup = GlassTheme.CreateButton("新建备份", ButtonKind.Primary);
            createBackup.Click += CreateManualBackup_Click;
            Grid.SetColumn(createBackup, 1);
            backupHeader.Children.Add(createBackup);
            panel.Children.Add(backupHeader);

            _backupStatusText = GlassTheme.CreateText("已创建备份。", 12, false);
            _backupStatusText.Opacity = 0.9;
            _backupStatusText.Visibility = Visibility.Collapsed;
            panel.Children.Add(_backupStatusText);

            panel.Children.Add(CreateSettingLabel("自动备份"));

            Grid autoBackupGrid = new Grid();
            autoBackupGrid.Margin = new Thickness(0, 6, 0, 12);
            autoBackupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            autoBackupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel intervalPanel = new StackPanel();
            intervalPanel.Margin = new Thickness(0, 0, 10, 0);
            intervalPanel.Children.Add(GlassTheme.CreateText("备份间隔", 12, true));

            _backupIntervalSlider = CreateSlider(
                1,
                168,
                Math.Max(1, _config.BackupIntervalHours),
                1,
                6,
                1);
            _backupIntervalSlider.Margin = new Thickness(0, 4, 0, 2);
            _backupIntervalSlider.ValueChanged += BackupInterval_Changed;
            intervalPanel.Children.Add(_backupIntervalSlider);

            _backupIntervalValue = new TextBlock();
            _backupIntervalValue.Text = ((int)_backupIntervalSlider.Value).ToString() + " 小时";
            _backupIntervalValue.Foreground = GlassTheme.TextPrimaryBrush;
            intervalPanel.Children.Add(_backupIntervalValue);

            Grid.SetColumn(intervalPanel, 0);
            autoBackupGrid.Children.Add(intervalPanel);

            StackPanel countPanel = new StackPanel();
            countPanel.Margin = new Thickness(10, 0, 0, 0);
            countPanel.Children.Add(GlassTheme.CreateText("最多保留", 12, true));

            _backupCountSlider = CreateSlider(
                1,
                30,
                Math.Max(1, _config.BackupMaxCount),
                1,
                5,
                1);
            _backupCountSlider.Margin = new Thickness(0, 4, 0, 2);
            _backupCountSlider.ValueChanged += BackupCount_Changed;
            countPanel.Children.Add(_backupCountSlider);

            _backupCountValue = new TextBlock();
            _backupCountValue.Text = ((int)_backupCountSlider.Value).ToString() + " 份";
            _backupCountValue.Foreground = GlassTheme.TextPrimaryBrush;
            countPanel.Children.Add(_backupCountValue);

            Grid.SetColumn(countPanel, 1);
            autoBackupGrid.Children.Add(countPanel);
            panel.Children.Add(autoBackupGrid);

            _autoBackupHost = new StackPanel();
            _autoBackupHost.Margin = new Thickness(0, 10, 0, 18);
            panel.Children.Add(_autoBackupHost);

            panel.Children.Add(CreateSettingLabel("手动备份"));

            TextBlock manualHint = GlassTheme.CreateText(
                "手动创建的备份不会被自动清理，可长期保留或自行删除。",
                12,
                true);
            manualHint.Margin = new Thickness(0, 8, 0, 10);
            manualHint.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(manualHint);

            _manualBackupHost = new StackPanel();
            panel.Children.Add(_manualBackupHost);

            TextBlock backupPath = GlassTheme.CreateText(
                "备份文件保存在：" + ConfigStore.BackupDirectory + "\n可在上方列表中直接恢复或删除。",
                12,
                true);
            backupPath.Margin = new Thickness(0, 8, 0, 0);
            backupPath.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(backupPath);

            RefreshBackupLists();

            return panel;
        }

        private sealed class BackupFile
        {
            public string Path;
            public DateTime Time;
        }

        private void RefreshBackupLists()
        {
            if (_autoBackupHost != null)
            {
                _autoBackupHost.Children.Clear();
                FillBackupList(_autoBackupHost, GetBackupFiles("config_*.json"), true);
            }

            if (_manualBackupHost != null)
            {
                _manualBackupHost.Children.Clear();
                FillBackupList(_manualBackupHost, GetBackupFiles("manual_*.json"), false);
            }
        }

        private static List<BackupFile> GetBackupFiles(string pattern)
        {
            List<BackupFile> result = new List<BackupFile>();
            if (!Directory.Exists(ConfigStore.BackupDirectory))
            {
                return result;
            }

            string[] paths = Directory.GetFiles(ConfigStore.BackupDirectory, pattern);
            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    FileInfo info = new FileInfo(paths[i]);
                    result.Add(new BackupFile
                    {
                        Path = info.FullName,
                        Time = info.LastWriteTime
                    });
                }
                catch
                {
                }
            }

            result.Sort(delegate(BackupFile left, BackupFile right)
            {
                return right.Time.CompareTo(left.Time);
            });
            return result;
        }

        private void FillBackupList(StackPanel host, List<BackupFile> backups, bool isAuto)
        {
            if (backups == null || backups.Count == 0)
            {
                TextBlock empty = GlassTheme.CreateText("暂无备份。", 12, true);
                host.Children.Add(empty);
                return;
            }

            for (int i = 0; i < backups.Count; i++)
            {
                host.Children.Add(BuildBackupCard(backups[i], isAuto));
            }
        }

        private UIElement BuildBackupCard(BackupFile backup, bool isAuto)
        {
            Border card = new Border();
            card.Margin = new Thickness(0, 0, 0, 10);
            card.Padding = new Thickness(14);
            card.Background = CardSurfaceBrush;
            card.BorderBrush = SettingsBorderBrush;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(10);

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel info = new StackPanel();
            info.VerticalAlignment = VerticalAlignment.Center;
            info.Margin = new Thickness(0, 0, 10, 0);

            TextBlock time = new TextBlock();
            time.Text = backup.Time.ToString("yyyy-MM-dd HH:mm:ss");
            time.FontSize = 13;
            time.Foreground = GlassTheme.TextPrimaryBrush;
            info.Children.Add(time);

            TextBlock kind = new TextBlock();
            kind.Text = isAuto ? "自动备份" : "手动备份";
            kind.FontSize = 11;
            kind.Margin = new Thickness(0, 4, 0, 0);
            kind.Foreground = GlassTheme.TextSecondaryBrush;
            info.Children.Add(kind);
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            Button restore = GlassTheme.CreateButton("恢复", ButtonKind.Primary);
            restore.MinWidth = 74;
            restore.Margin = new Thickness(0, 0, 8, 0);
            restore.Tag = backup.Path;
            restore.Click += RestoreBackup_Click;
            Grid.SetColumn(restore, 1);
            grid.Children.Add(restore);

            Button delete = GlassTheme.CreateButton("删除", ButtonKind.Danger);
            delete.MinWidth = 74;
            delete.Tag = backup.Path;
            delete.Click += DeleteBackup_Click;
            Grid.SetColumn(delete, 2);
            grid.Children.Add(delete);

            card.Child = grid;
            return card;
        }

        private void CreateManualBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(ConfigStore.BackupDirectory))
                {
                    Directory.CreateDirectory(ConfigStore.BackupDirectory);
                }

                ConfigStore.Save(_config);
                string content = File.ReadAllText(ConfigStore.ConfigPath);
                string name = "manual_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json";
                string path = Path.Combine(ConfigStore.BackupDirectory, name);
                File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));

                if (_backupStatusText != null)
                {
                    _backupStatusText.Text = "已创建备份。";
                    _backupStatusText.Visibility = Visibility.Visible;
                }

                RefreshBackupLists();
            }
            catch
            {
                if (_backupStatusText != null)
                {
                    _backupStatusText.Text = "创建备份失败，请检查备份目录权限。";
                    _backupStatusText.Visibility = Visibility.Visible;
                }
            }
        }

        private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string path = button == null ? null : button.Tag as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Copy(path, ConfigStore.ConfigPath, true);
            }
            catch
            {
                return;
            }

            if (_reloadApplication != null)
            {
                _restoreInProgress = true;
                _reloadApplication();
            }
        }

        private void DeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            string path = button == null ? null : button.Tag as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
            }

            RefreshBackupLists();
        }

        private UIElement BuildAboutPage()
        {
            StackPanel panel = new StackPanel();
            panel.HorizontalAlignment = HorizontalAlignment.Center;
            panel.VerticalAlignment = VerticalAlignment.Top;
            panel.MaxWidth = 540;
            Border badge = new Border();
            badge.Width = 72;
            badge.Height = 72;
            badge.CornerRadius = new CornerRadius(20);
            badge.Background = GlassTheme.CreateBrush(58, 112, 208, 255);
            badge.HorizontalAlignment = HorizontalAlignment.Center;
            badge.Margin = new Thickness(0, 8, 0, 18);

            TextBlock glyph = GlassTheme.CreateText("G", 36, false);
            glyph.FontWeight = FontWeights.Bold;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = glyph;
            panel.Children.Add(badge);

            TextBlock name = GlassTheme.CreateText("GhostDesk", 28, false);
            name.FontWeight = FontWeights.SemiBold;
            name.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(name);

            TextBlock version = GlassTheme.CreateText("版本 " + AppInfo.Version, 13, true);
            version.HorizontalAlignment = HorizontalAlignment.Center;
            version.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(version);

            TextBlock path = GlassTheme.CreateText(
                "配置目录：" + Path.GetDirectoryName(ConfigStore.ConfigPath),
                12,
                true);
            path.HorizontalAlignment = HorizontalAlignment.Center;
            path.Margin = new Thickness(0, 12, 0, 0);
            path.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(path);

            WrapPanel links = new WrapPanel();
            links.HorizontalAlignment = HorizontalAlignment.Center;
            links.Margin = new Thickness(0, 18, 0, 0);

            Button github = GlassTheme.CreateButton("GitHub 项目", ButtonKind.Ghost);
            github.Margin = new Thickness(0, 0, 10, 0);
            github.Click += OpenGitHub_Click;
            links.Children.Add(github);

            Button releases = GlassTheme.CreateButton("检查更新", ButtonKind.Ghost);
            releases.Click += CheckForUpdates_Click;
            links.Children.Add(releases);

            panel.Children.Add(links);

            Border supportCard = CreateSettingsCard(
                new Thickness(0, 24, 0, 0),
                new Thickness(18));
            StackPanel support = new StackPanel();

            TextBlock supportTitle = GlassTheme.CreateText("支持作者", 15, false);
            supportTitle.FontWeight = FontWeights.SemiBold;
            supportTitle.HorizontalAlignment = HorizontalAlignment.Center;
            support.Children.Add(supportTitle);

            TextBlock supportText = GlassTheme.CreateText(
                "如果 GhostDesk 让你的桌面更清爽，可以请作者喝一杯。",
                12,
                true);
            supportText.Margin = new Thickness(0, 7, 0, 14);
            supportText.TextWrapping = TextWrapping.Wrap;
            supportText.TextAlignment = TextAlignment.Center;
            support.Children.Add(supportText);

            Button donate = GlassTheme.CreateButton("请作者喝一杯", ButtonKind.Primary);
            donate.HorizontalAlignment = HorizontalAlignment.Center;
            donate.Click += Donate_Click;
            support.Children.Add(donate);

            supportCard.Child = support;
            panel.Children.Add(supportCard);

            return panel;
        }

        private ScrollViewer CreateScrollViewer(UIElement content)
        {
            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Background = Brushes.Transparent;
            scroll.Resources.Add(
                typeof(System.Windows.Controls.Primitives.ScrollBar),
                RegionControl.CreateGlassScrollBarStyle());
            if (content != null)
            {
                scroll.Content = content;
            }
            return scroll;
        }

        private TextBlock CreateSettingLabel(string text)
        {
            TextBlock label = GlassTheme.CreateText(text, 13, false);
            label.FontWeight = FontWeights.SemiBold;
            label.Margin = new Thickness(0, 4, 0, 0);
            return label;
        }

        private static Border CreateSettingsCard(Thickness margin, Thickness padding)
        {
            Border card = new Border();
            card.Margin = margin;
            card.Padding = padding;
            card.Background = CardRaisedBrush;
            card.BorderBrush = SettingsBorderBrush;
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(12);
            return card;
        }

        private static Style GetSettingsCheckBoxStyle()
        {
            if (_settingsCheckBoxStyle != null)
            {
                return _settingsCheckBoxStyle;
            }

            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type CheckBox}'>" +
                "<Setter Property='Foreground' Value='#A8B2C4'/>" +
                "<Setter Property='FontFamily' Value='Microsoft YaHei UI'/>" +
                "<Setter Property='FontSize' Value='13'/>" +
                "<Setter Property='Cursor' Value='Hand'/>" +
                "<Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type CheckBox}'>" +
                "<Grid Background='Transparent'>" +
                "<Grid.ColumnDefinitions>" +
                "<ColumnDefinition Width='17'/>" +
                "<ColumnDefinition Width='*'/>" +
                "</Grid.ColumnDefinitions>" +
                "<Border x:Name='Box' Width='17' Height='17' CornerRadius='4' " +
                "Background='#26060C15' BorderBrush='#52FFFFFF' BorderThickness='1'/>" +
                "<Path x:Name='CheckGlyph' Width='10' Height='8' Stretch='Fill' " +
                "Data='M 0,4 L 3.5,7 L 10,1' Stroke='#EAF8FF' StrokeThickness='1.7' " +
                "StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' " +
                "HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>" +
                "<ContentPresenter Grid.Column='1' Margin='9,0,0,0' " +
                "VerticalAlignment='Center' RecognizesAccessKey='True'/>" +
                "</Grid>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='Box' Property='Background' Value='#3A223448'/>" +
                "<Setter TargetName='Box' Property='BorderBrush' Value='#78FFFFFF'/>" +
                "</Trigger>" +
                "<Trigger Property='IsChecked' Value='True'>" +
                "<Setter TargetName='Box' Property='Background' Value='#7A4BA7CF'/>" +
                "<Setter TargetName='Box' Property='BorderBrush' Value='#B08BE2FF'/>" +
                "<Setter TargetName='CheckGlyph' Property='Visibility' Value='Visible'/>" +
                "</Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'>" +
                "<Setter Property='Opacity' Value='0.42'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>";

            _settingsCheckBoxStyle = (Style)XamlReader.Parse(xaml);
            return _settingsCheckBoxStyle;
        }

        private static Style GetSettingsRadioButtonStyle()
        {
            if (_settingsRadioButtonStyle != null)
            {
                return _settingsRadioButtonStyle;
            }

            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type RadioButton}'>" +
                "<Setter Property='Foreground' Value='#EEF4FF'/>" +
                "<Setter Property='FontFamily' Value='Microsoft YaHei UI'/>" +
                "<Setter Property='FontSize' Value='13'/>" +
                "<Setter Property='Cursor' Value='Hand'/>" +
                "<Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type RadioButton}'>" +
                "<Grid Background='Transparent'>" +
                "<Grid.ColumnDefinitions>" +
                "<ColumnDefinition Width='17'/>" +
                "<ColumnDefinition Width='*'/>" +
                "</Grid.ColumnDefinitions>" +
                "<Border x:Name='Ring' Width='17' Height='17' CornerRadius='8.5' " +
                "Background='#26060C15' BorderBrush='#52FFFFFF' BorderThickness='1'>" +
                "<Border x:Name='Dot' Width='7' Height='7' CornerRadius='3.5' " +
                "Background='#8BE2FF' HorizontalAlignment='Center' " +
                "VerticalAlignment='Center' Visibility='Collapsed'/>" +
                "</Border>" +
                "<ContentPresenter Grid.Column='1' Margin='9,0,0,0' " +
                "VerticalAlignment='Center' RecognizesAccessKey='True'/>" +
                "</Grid>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='Ring' Property='Background' Value='#3A223448'/>" +
                "<Setter TargetName='Ring' Property='BorderBrush' Value='#78FFFFFF'/>" +
                "</Trigger>" +
                "<Trigger Property='IsChecked' Value='True'>" +
                "<Setter TargetName='Ring' Property='BorderBrush' Value='#B08BE2FF'/>" +
                "<Setter TargetName='Dot' Property='Visibility' Value='Visible'/>" +
                "</Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'>" +
                "<Setter Property='Opacity' Value='0.42'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>";

            _settingsRadioButtonStyle = (Style)XamlReader.Parse(xaml);
            return _settingsRadioButtonStyle;
        }

        private static Style GetSettingsSliderStyle()
        {
            if (_settingsSliderStyle != null)
            {
                return _settingsSliderStyle;
            }

            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type Slider}'>" +
                "<Setter Property='Height' Value='28'/>" +
                "<Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type Slider}'>" +
                "<Grid Margin='0,2'>" +
                "<Border Height='4' CornerRadius='2' Background='#3AFFFFFF' " +
                "VerticalAlignment='Center'/>" +
                "<Track x:Name='PART_Track' VerticalAlignment='Center' " +
                "Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' " +
                "Value='{TemplateBinding Value}' Orientation='Horizontal'>" +
                "<Track.DecreaseRepeatButton>" +
                "<RepeatButton Command='Slider.DecreaseLarge' Focusable='False'>" +
                "<RepeatButton.Template>" +
                "<ControlTemplate TargetType='{x:Type RepeatButton}'>" +
                "<Border Height='4' CornerRadius='2' Background='#8BE2FF'/>" +
                "</ControlTemplate>" +
                "</RepeatButton.Template>" +
                "</RepeatButton>" +
                "</Track.DecreaseRepeatButton>" +
                "<Track.IncreaseRepeatButton>" +
                "<RepeatButton Command='Slider.IncreaseLarge' Focusable='False'>" +
                "<RepeatButton.Template>" +
                "<ControlTemplate TargetType='{x:Type RepeatButton}'>" +
                "<Border Height='4' Background='Transparent'/>" +
                "</ControlTemplate>" +
                "</RepeatButton.Template>" +
                "</RepeatButton>" +
                "</Track.IncreaseRepeatButton>" +
                "<Track.Thumb>" +
                "<Thumb Width='18' Height='18' Cursor='Hand'>" +
                "<Thumb.Template>" +
                "<ControlTemplate TargetType='{x:Type Thumb}'>" +
                "<Grid>" +
                "<Ellipse Fill='#182334' Stroke='#8BE2FF' StrokeThickness='2'/>" +
                "<Ellipse Width='7' Height='7' Fill='#EAF8FF'/>" +
                "</Grid>" +
                "</ControlTemplate>" +
                "</Thumb.Template>" +
                "</Thumb>" +
                "</Track.Thumb>" +
                "</Track>" +
                "</Grid>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>";

            _settingsSliderStyle = (Style)XamlReader.Parse(xaml);
            return _settingsSliderStyle;
        }

        private void AddRegion_Click(object sender, RoutedEventArgs e)
        {
            double width = 360;
            double height = 180;
            double left = SystemParameters.WorkArea.Left;
            double top = SystemParameters.WorkArea.Top;
            double x = left + ((SystemParameters.WorkArea.Width - width) / 2.0);
            double y = top + ((SystemParameters.WorkArea.Height - height) / 2.0);

            RegionConfig region = new RegionConfig();
            region.Id = Guid.NewGuid().ToString("N");
            region.Name = "新分区";
            region.X = x;
            region.Y = y;
            region.Width = width;
            region.Height = height;
            region.ShowExtensions =
                _showExtensionsCheck != null && _showExtensionsCheck.IsChecked == true
                    ? "true"
                    : "false";
            region.Items = new List<DesktopItem>();
            region.LaunchItems = new List<DesktopItem>();

            _config.Regions.Add(region);
            _desktop.AddRegion(region);
            RefreshRegionList();
            if (_showExtensionsCheck != null)
            {
                _showExtensionsCheck.IsEnabled = true;
            }
            ConfigStore.Save(_config);
        }

        private void ResetRegion_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            _desktop.ResetRegionPosition((string)button.Tag);
            ConfigStore.Save(_config);
            RefreshRegionList();
        }

        private void RemoveRegion_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            _desktop.RemoveRegionById((string)button.Tag);
            ConfigStore.Save(_config);
            RefreshRegionList();
            if (_showExtensionsCheck != null)
            {
                _showExtensionsCheck.IsEnabled =
                    _config.Regions != null && _config.Regions.Count > 0;
            }
        }

        private void HideDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_hideDelayValue == null)
            {
                return;
            }

            _config.IdleSeconds = Math.Round(e.NewValue, 1);
            _hideDelayValue.Text = _config.IdleSeconds.ToString("0.#") + " 秒";
        }

        private void Fade_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_fadeValue == null)
            {
                return;
            }

            _config.FadeSeconds = Math.Round(e.NewValue, 1);
            _fadeValue.Text = _config.FadeSeconds.ToString("0.#") + " 秒";
        }

        private void AutoStart_Changed(object sender, RoutedEventArgs e)
        {
            if (_autoStartCheck == null)
            {
                return;
            }

            _config.AutoStart = _autoStartCheck.IsChecked == true;
            if (_setAutoStart != null)
            {
                _setAutoStart(_config.AutoStart);
            }
        }

        private void ShowHiddenFiles_Changed(object sender, RoutedEventArgs e)
        {
            if (_showHiddenFilesCheck == null || _suppressVisibilityChange)
            {
                return;
            }

            bool previous = _config.ShowHiddenFiles;
            bool desired = _showHiddenFilesCheck.IsChecked == true;
            if (desired == previous)
            {
                return;
            }

            if (desired)
            {
                // 只改资源管理器的「显示隐藏文件」设置：文件保持隐藏属性，桌面以半透明样式呈现
                if (!_config.ExplorerHiddenOriginal.HasValue)
                {
                    _config.ExplorerHiddenOriginal = StorageSafety.ReadExplorerHidden();
                }

                if (StorageSafety.SetExplorerHidden(1))
                {
                    _config.ShowHiddenFiles = true;
                    ConfigStore.Save(_config);
                    return;
                }
            }
            else
            {
                int original = 2;
                if (_config.ExplorerHiddenOriginal.HasValue &&
                    _config.ExplorerHiddenOriginal.Value != 0)
                {
                    original = _config.ExplorerHiddenOriginal.Value;
                }

                if (StorageSafety.SetExplorerHidden(original))
                {
                    // 关掉「临时查看」时把隐藏属性重新压回去，否则图标会一直露在桌面上
                    ItemStorage.SynchronizeManagedVisibility(_config, false);
                    _config.ShowHiddenFiles = false;
                    _config.ExplorerHiddenOriginal = null;
                    ConfigStore.Save(_config);
                    return;
                }
            }

            _suppressVisibilityChange = true;
            try
            {
                _showHiddenFilesCheck.IsChecked = previous;
            }
            finally
            {
                _suppressVisibilityChange = false;
            }

            MessageBox.Show(
                "无法修改资源管理器「显示隐藏文件」设置，请稍后重试。",
                "GhostDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void BackupInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_backupIntervalValue == null)
            {
                return;
            }

            _config.BackupIntervalHours = Math.Round(e.NewValue, 0);
            _backupIntervalValue.Text = ((int)e.NewValue).ToString() + " 小时";
        }

        private void BackupCount_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_backupCountValue == null)
            {
                return;
            }

            _config.BackupMaxCount = (int)Math.Round(e.NewValue, 0);
            _backupCountValue.Text = ((int)e.NewValue).ToString() + " 份";
        }


        private void EdgeDockBand_Changed(object sender, RoutedEventArgs e)
        {
            SyncSliderFromBox(_edgeDockBandSlider, _edgeDockBandBox, 0, 240);
            ApplyDockValuesFromControls();
        }

        private void SafeInset_Changed(object sender, RoutedEventArgs e)
        {
            SyncSliderFromBox(_safeTopSlider, _safeTopBox, 0, 400);
            SyncSliderFromBox(_safeBottomSlider, _safeBottomBox, 0, 400);
            SyncSliderFromBox(_safeSideSlider, _safeSideBox, 0, 400);
            ApplyDockValuesFromControls();
        }

        /// <summary>滑块拖动：把数值同步回输入框并立刻生效。</summary>
        private void DockSlider_Changed(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_dockSyncBusy)
            {
                return;
            }

            Slider slider = sender as Slider;
            if (slider == null)
            {
                return;
            }

            _dockSyncBusy = true;
            try
            {
                int value = (int)Math.Round(e.NewValue);
                TextBox box = slider.Tag as TextBox;
                if (box != null)
                {
                    string text = value.ToString();
                    if (!string.Equals(box.Text, text, StringComparison.Ordinal))
                    {
                        box.Text = text;
                    }
                }

                ApplyDockValuesFromControls();
            }
            finally
            {
                _dockSyncBusy = false;
            }
        }

        private void SyncSliderFromBox(
            Slider slider,
            TextBox box,
            int min,
            int max)
        {
            if (slider == null || box == null)
            {
                return;
            }

            int value = ParseDockValue(box, min, max);
            if (Math.Abs(slider.Value - value) <= 0.01)
            {
                return;
            }

            _dockSyncBusy = true;
            try
            {
                slider.Value = value;
            }
            finally
            {
                _dockSyncBusy = false;
            }
        }

        private static int ParseDockValue(TextBox box, int min, int max)
        {
            int value;
            if (!int.TryParse((box.Text ?? "").Trim(), out value))
            {
                value = min;
            }

            return Math.Max(min, Math.Min(max, value));
        }

        /// <summary>把四条参数当前控件的值写回配置并即时生效。</summary>
        private void ApplyDockValuesFromControls()
        {
            if (_edgeDockBandBox == null ||
                _safeTopBox == null ||
                _safeBottomBox == null ||
                _safeSideBox == null)
            {
                return;
            }

            int band = ParseDockValue(_edgeDockBandBox, 0, 240);
            int top = ParseDockValue(_safeTopBox, 0, 400);
            int bottom = ParseDockValue(_safeBottomBox, 0, 400);
            int side = ParseDockValue(_safeSideBox, 0, 400);
            _config.EdgeDockBand = band;
            _config.SafeInsetTop = top;
            _config.SafeInsetBottom = bottom;
            _config.SafeInsetLeft = side;
            _config.SafeInsetRight = side;
            ApplyDockSettings();
        }

        private void ApplyDockSettings()
        {
            ConfigStore.Save(_config);
            if (_desktop != null)
            {
                _desktop.ApplyDockSettings();
            }
        }

        /// <summary>参数用的短滑块（宽度受限，配合右侧输入框使用）。</summary>
        private Slider CreateDockSlider(
            double value,
            double min,
            double max,
            double step,
            TextBox box)
        {
            Slider slider = new Slider();
            slider.Minimum = min;
            slider.Maximum = max;
            slider.SmallChange = step;
            slider.LargeChange = step * 5.0;
            slider.TickFrequency = step;
            slider.Width = 116;
            slider.VerticalAlignment = VerticalAlignment.Center;
            slider.Margin = new Thickness(0, 0, 10, 0);
            slider.Tag = box;
            slider.Value = Math.Max(min, Math.Min(max, value));
            slider.ValueChanged += DockSlider_Changed;
            return slider;
        }

        /// <summary>数字输入框：可直接输入，也能用鼠标滚轮微调。</summary>
        private TextBox CreateNumberBox(
            double value,
            int min,
            int max,
            int step,
            RoutedEventHandler commit)
        {
            TextBox box = new TextBox();
            box.Width = 70;
            box.FontSize = 13;
            box.Text = ((int)Math.Round(value)).ToString();
            box.TextAlignment = TextAlignment.Right;
            box.Padding = new Thickness(6, 4, 6, 4);
            box.VerticalAlignment = VerticalAlignment.Center;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.Background = GlassTheme.CreateBrush(90, 255, 255, 255);
            box.Foreground = GlassTheme.CreateBrush(240, 255, 255, 255);
            box.CaretBrush = GlassTheme.CreateBrush(240, 255, 255, 255);
            box.BorderBrush = GlassTheme.CreateBrush(70, 255, 255, 255);
            box.BorderThickness = new Thickness(1);
            box.Tag = new int[] { min, max, step };

            box.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                int[] range = box.Tag as int[];
                if (range == null)
                {
                    return;
                }

                int current;
                if (!int.TryParse((box.Text ?? "").Trim(), out current))
                {
                    current = range[0];
                }

                int delta = e.Delta > 0 ? range[2] : -range[2];
                int next = Math.Max(range[0], Math.Min(range[1], current + delta));
                box.Text = next.ToString();
                if (commit != null)
                {
                    commit(box, new RoutedEventArgs());
                }

                e.Handled = true;
            };

            box.LostFocus += delegate
            {
                if (commit != null)
                {
                    commit(box, new RoutedEventArgs());
                }
            };

            box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }

                if (commit != null)
                {
                    commit(box, new RoutedEventArgs());
                }

                e.Handled = true;
            };

            return box;
        }

        /// <summary>「标签 + 短滑块 + 输入框 + 单位」放成紧凑的一格。</summary>
        private void AddDockField(
            Grid grid,
            int row,
            int column,
            string label,
            Slider slider,
            TextBox box)
        {
            StackPanel cell = new StackPanel();
            cell.Margin = new Thickness(0, 2, 0, 6);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);

            TextBlock caption = GlassTheme.CreateText(label, 13, false);
            caption.FontWeight = FontWeights.SemiBold;
            caption.Margin = new Thickness(0, 0, 0, 4);
            cell.Children.Add(caption);

            StackPanel line = new StackPanel();
            line.Orientation = Orientation.Horizontal;
            if (slider != null)
            {
                line.Children.Add(slider);
            }

            line.Children.Add(box);

            TextBlock unitText = GlassTheme.CreateText("px", 12, true);
            unitText.VerticalAlignment = VerticalAlignment.Center;
            unitText.Margin = new Thickness(6, 0, 0, 0);
            line.Children.Add(unitText);

            cell.Children.Add(line);
            grid.Children.Add(cell);
        }

        private Button CreateMaterialPresetButton(string text, string preset)
        {
            Button button = GlassTheme.CreateButton(text, ButtonKind.Ghost);
            button.Tag = preset;
            button.Margin = new Thickness(0, 0, 8, 6);
            button.Click += MaterialPreset_Click;
            return button;
        }

        private Slider CreateMaterialSlider(double value, TextBox box)
        {
            Slider slider = new Slider();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.SmallChange = 1;
            slider.LargeChange = 10;
            slider.TickFrequency = 10;
            slider.Width = 116;
            slider.VerticalAlignment = VerticalAlignment.Center;
            slider.Margin = new Thickness(0, 0, 10, 0);
            slider.Tag = box;
            slider.Value = Math.Max(0, Math.Min(100, value));
            slider.ValueChanged += MaterialSlider_Changed;
            return slider;
        }

        private void MaterialPreset_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null)
            {
                return;
            }

            _config.MaterialPreset = ConfigStore.NormalizeMaterialPreset(button.Tag as string);
            if (string.IsNullOrEmpty(_config.MaterialPreset))
            {
                _config.MaterialPreset = AppConfig.MaterialGlass;
            }

            ConfigStore.Save(_config);
            if (_desktop != null)
            {
                _desktop.ApplyMaterialSettings();
            }
        }

        private void MaterialSlider_Changed(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_matSyncBusy)
            {
                return;
            }

            Slider slider = sender as Slider;
            if (slider == null)
            {
                return;
            }

            _matSyncBusy = true;
            try
            {
                int value = (int)Math.Round(e.NewValue);
                TextBox box = slider.Tag as TextBox;
                if (box != null)
                {
                    string text = value.ToString();
                    if (!string.Equals(box.Text, text, StringComparison.Ordinal))
                    {
                        box.Text = text;
                    }
                }

                ApplyMaterialValues();
            }
            finally
            {
                _matSyncBusy = false;
            }
        }

        private void Material_Changed(object sender, RoutedEventArgs e)
        {
            SyncMaterialSlider(_matTintSlider, _matTintBox);
            SyncMaterialSlider(_matBorderSlider, _matBorderBox);
            SyncMaterialSlider(_matShadowSlider, _matShadowBox);
            ApplyMaterialValues();
        }

        private void SyncMaterialSlider(Slider slider, TextBox box)
        {
            if (slider == null || box == null)
            {
                return;
            }

            int value = ParseDockValue(box, 0, 100);
            if (Math.Abs(slider.Value - value) <= 0.01)
            {
                return;
            }

            _matSyncBusy = true;
            try
            {
                slider.Value = value;
            }
            finally
            {
                _matSyncBusy = false;
            }
        }

        private void ApplyMaterialValues()
        {
            if (_matOpacityBox == null ||
                _matTintBox == null ||
                _matBorderBox == null ||
                _matShadowBox == null)
            {
                return;
            }

            _config.MaterialOpacity = ParseDockValue(_matOpacityBox, 0, 100);
            _config.MaterialTint = ParseDockValue(_matTintBox, 0, 100);
            _config.MaterialBorder = ParseDockValue(_matBorderBox, 0, 100);
            _config.MaterialShadow = ParseDockValue(_matShadowBox, 0, 100);
            ConfigStore.Save(_config);
            if (_desktop != null)
            {
                _desktop.ApplyMaterialSettings();
            }
        }
        private void EnableGuides_Changed(object sender, RoutedEventArgs e)
        {
            if (_enableGuidesCheck == null)
            {
                return;
            }

            _config.EnableGuides = _enableGuidesCheck.IsChecked == true;
            ConfigStore.Save(_config);
        }

        private void EnableSnap_Changed(object sender, RoutedEventArgs e)
        {
            if (_enableSnapCheck == null)
            {
                return;
            }

            _config.EnableSnap = _enableSnapCheck.IsChecked == true;
        }

        private void CollapseHover_Changed(object sender, RoutedEventArgs e)
        {
            if (_collapseHoverButtonRadio == null || _collapseHoverRowRadio == null)
            {
                return;
            }

            _config.CollapseHoverTrigger =
                _collapseHoverRowRadio.IsChecked == true
                    ? AppConfig.CollapseHoverRow
                    : AppConfig.CollapseHoverButton;
            _desktop.ApplyCollapseHoverTrigger();
            ConfigStore.Save(_config);
        }

        private bool AreAllRegionsShowingExtensions()
        {
            if (_config.Regions == null || _config.Regions.Count == 0)
            {
                return true;
            }

            foreach (RegionConfig region in _config.Regions)
            {
                if (region != null &&
                    string.Equals(region.ShowExtensions, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void ShowExtensions_Changed(object sender, RoutedEventArgs e)
        {
            if (_showExtensionsCheck == null || _config.Regions == null)
            {
                return;
            }

            bool show = _showExtensionsCheck.IsChecked == true;
            string value = show ? "true" : "false";
            foreach (RegionConfig region in _config.Regions)
            {
                if (region != null)
                {
                    region.ShowExtensions = value;
                }
            }

            _desktop.RefreshRegionItems();
            ConfigStore.Save(_config);
        }

        private void ReloadApplication_Click(object sender, RoutedEventArgs e)
        {
            if (_reloadApplication != null)
            {
                _reloadApplication();
            }
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://github.com/" + AppInfo.GitHubOwner + "/" + AppInfo.GitHubRepo);
        }

        private void OpenRelease_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(AppInfo.ReleasesUrl);
        }

        private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_checkForUpdates != null)
            {
                _checkForUpdates();
            }
        }

        private void Donate_Click(object sender, RoutedEventArgs e)
        {
            DonateWindow.ShowDonate();
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(url);
            }
            catch
            {
            }
        }

        /// <summary>把 SVG 路径数据变成可缩放的 WPF 图形。</summary>
        private static Geometry CreateNotesIcon()
        {
            try
            {
                return Geometry.Parse(NotesIconPath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>「!」说明弹窗：讲清楚收纳/删除/放回到底动了什么。</summary>
        private void ShowNotes()
        {
            List<string> sections = new List<string>();
            sections.Add("• 桌面上的文件 / 文件夹 / 快捷方式：拖进分区后只给原文件加「隐藏」属性，桌面图标消失，文件本身留在原处，不会移动。");
            sections.Add("• 桌面以外的路径：GhostDesk 会先在这个文件所在的目录里建一个同名 .lnk 快捷方式，分区引用的是这份快捷方式，所以源文件的位置、名字都不会被改动。");
            sections.Add("• 右键「删除（回收站）」：GhostDesk 代建的 .lnk 只会删掉这份快捷方式；直接引用的项目只移除分区记录，绝不会删除原始文件。");
            sections.Add("• 「放回桌面 / 放回源路径」：只移除分区记录并恢复显示，文件位置和内容都不变。");
            sections.Add("• 重命名：只改分区里显示的入口名（以及代建快捷方式的名字），不会修改源文件。");
            sections.Add("• 贴边与朝向：右键分区 →「贴边方向」。固定在上/下/左/右只改变标题栏朝向，不会挪动分区；灵敏度与四周安全边距在「分区管理」页调整。");
            sections.Add("• 配置与备份：配置保存在 %APPDATA%\\GhostDesk，可在「高级设置」里手动备份或恢复。");

            InfoWindow window = new InfoWindow(
                "分区收纳说明",
                "下面几条说明可以帮你确认哪些操作不会动到原始文件。",
                sections);
            window.Owner = this;
            window.ShowDialog();
        }

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            ConfigStore.Save(_config);
            Close();
        }
    }
}
