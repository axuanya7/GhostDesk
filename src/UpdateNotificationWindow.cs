using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GhostDesk
{
    internal static class UpdateNotification
    {
        public static void Show(
            string title,
            string message,
            string actionText,
            Action action)
        {
            ShowUpdate(
                title,
                title,
                message,
                "",
                actionText,
                action);
        }

        public static void ShowUpdate(
            string title,
            string heading,
            string message,
            string notes,
            string actionText,
            Action action)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                UpdateNotificationWindow window = new UpdateNotificationWindow(
                    title,
                    heading,
                    message,
                    notes,
                    actionText,
                    action);
                window.Show();
            }));
        }

        public static void ShowUpdateDownload(
            string title,
            string heading,
            string message,
            string notes,
            string actionText,
            Action<Action<int>, Action<string>> startAction)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                UpdateNotificationWindow window = new UpdateNotificationWindow(
                    title,
                    heading,
                    message,
                    notes,
                    actionText,
                    startAction);
                window.Show();
            }));
        }
    }

    internal sealed class UpdateNotificationWindow : Window
    {
        private readonly Action<Action<int>, Action<string>> _downloadAction;
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _actions;
        private readonly Button _laterButton;
        private readonly Button _updateButton;
        private ProgressBar _progressBar;
        private TextBlock _progressText;
        private bool _downloadStarted;

        public UpdateNotificationWindow(
            string title,
            string heading,
            string message,
            string notes,
            string actionText,
            Action action)
            : this(title, heading, message, notes, actionText, action, null)
        {
        }

        public UpdateNotificationWindow(
            string title,
            string heading,
            string message,
            string notes,
            string actionText,
            Action<Action<int>, Action<string>> downloadAction)
            : this(title, heading, message, notes, actionText, null, downloadAction)
        {
        }

        private UpdateNotificationWindow(
            string title,
            string heading,
            string message,
            string notes,
            string actionText,
            Action action,
            Action<Action<int>, Action<string>> downloadAction)
        {
            _downloadAction = downloadAction;

            Title = string.IsNullOrWhiteSpace(title) ? "GhostDesk 更新" : title;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            MinWidth = 380;
            MaxHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            ShowActivated = true;
            GlassTheme.ConfigureWindow(this);
            ResizeMode = ResizeMode.NoResize;

            Border surface = new Border();
            surface.Margin = new Thickness(1);
            surface.CornerRadius = new CornerRadius(14);
            surface.Background = GlassTheme.CreateBrush(244, 13, 17, 26);
            surface.BorderBrush = GlassTheme.HairlineBrush;
            surface.BorderThickness = new Thickness(1);
            surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            surface.Child = root;

            Border titleBar = BuildTitleBar();
            root.Children.Add(titleBar);

            _scroll = new ScrollViewer();
            _scroll.Margin = new Thickness(22, 10, 14, 0);
            _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _scroll.Resources.Add(
                typeof(System.Windows.Controls.Primitives.ScrollBar),
                RegionControl.CreateGlassScrollBarStyle());

            StackPanel body = new StackPanel();
            body.Margin = new Thickness(0, 0, 10, 14);
            _scroll.Content = body;
            Grid.SetRow(_scroll, 1);
            root.Children.Add(_scroll);

            TextBlock headingText = GlassTheme.CreateText(
                string.IsNullOrWhiteSpace(heading) ? "GhostDesk 更新" : heading,
                22,
                false);
            headingText.FontWeight = FontWeights.Bold;
            headingText.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(headingText);

            TextBlock messageText = GlassTheme.CreateText(
                string.IsNullOrWhiteSpace(message)
                    ? "GhostDesk 已有可用更新。"
                    : message,
                13,
                true);
            messageText.Margin = new Thickness(0, 10, 0, 14);
            messageText.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(messageText);

            if (!string.IsNullOrWhiteSpace(notes))
            {
                TextBlock notesText = GlassTheme.CreateText(notes, 13, false);
                notesText.FontFamily = new FontFamily("Consolas, Microsoft YaHei UI");
                notesText.LineHeight = 22;
                notesText.TextWrapping = TextWrapping.Wrap;
                body.Children.Add(notesText);
            }

            Border footer = new Border();
            footer.BorderBrush = GlassTheme.HairlineBrush;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(22, 12, 22, 14);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            _actions = new StackPanel();
            _actions.Orientation = Orientation.Horizontal;
            _actions.HorizontalAlignment = HorizontalAlignment.Right;
            footer.Child = _actions;

            if (downloadAction != null)
            {
                _laterButton = GlassTheme.CreateButton("稍后", ButtonKind.Ghost);
                _laterButton.MinWidth = 84;
                _laterButton.Margin = new Thickness(0, 0, 10, 0);
                _laterButton.Click += delegate { Close(); };
                _actions.Children.Add(_laterButton);

                _updateButton = GlassTheme.CreateButton(
                    string.IsNullOrWhiteSpace(actionText) ? "立即更新" : actionText,
                    ButtonKind.Primary);
                _updateButton.MinWidth = 96;
                _updateButton.Click += delegate { StartDownload(); };
                _actions.Children.Add(_updateButton);
            }
            else if (!string.IsNullOrWhiteSpace(actionText) && action != null)
            {
                Button later = GlassTheme.CreateButton("稍后", ButtonKind.Ghost);
                later.MinWidth = 84;
                later.Margin = new Thickness(0, 0, 10, 0);
                later.Click += delegate { Close(); };
                _actions.Children.Add(later);

                Button update = GlassTheme.CreateButton(actionText, ButtonKind.Primary);
                update.MinWidth = 96;
                update.Click += delegate
                {
                    try
                    {
                        action();
                    }
                    catch
                    {
                    }
                    Close();
                };
                _actions.Children.Add(update);
            }
            else
            {
                Button close = GlassTheme.CreateButton("关闭", ButtonKind.Ghost);
                close.MinWidth = 84;
                close.Click += delegate { Close(); };
                _actions.Children.Add(close);
            }

            Content = surface;
        }

        private Border BuildTitleBar()
        {
            Border titleBar = new Border();
            titleBar.Background = Brushes.Transparent;
            titleBar.Padding = new Thickness(20, 0, 10, 0);
            titleBar.MouseLeftButtonDown += Surface_MouseLeftButtonDown;

            Grid bar = new Grid();
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.Child = bar;

            TextBlock title = GlassTheme.CreateText(
                string.IsNullOrWhiteSpace(Title) ? "GhostDesk 更新" : Title,
                14,
                false);
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 0);
            bar.Children.Add(title);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.VerticalAlignment = VerticalAlignment.Center;
            Border closeButton = new Border();
            closeButton.Width = 30;
            closeButton.Height = 30;
            closeButton.CornerRadius = new CornerRadius(8);
            closeButton.Background = Brushes.Transparent;
            closeButton.Cursor = Cursors.Hand;

            TextBlock closeGlyph = GlassTheme.CreateText("X", 13, false);
            closeGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            closeGlyph.VerticalAlignment = VerticalAlignment.Center;
            closeButton.Child = closeGlyph;
            closeButton.MouseEnter += delegate
            {
                closeButton.Background = GlassTheme.HoverBrush;
            };
            closeButton.MouseLeave += delegate
            {
                closeButton.Background = Brushes.Transparent;
            };
            closeButton.MouseLeftButtonDown += delegate { Close(); };
            buttons.Children.Add(closeButton);

            Border minimizeButton = new Border();
            minimizeButton.Width = 30;
            minimizeButton.Height = 30;
            minimizeButton.Margin = new Thickness(0, 0, 6, 0);
            minimizeButton.CornerRadius = new CornerRadius(8);
            minimizeButton.Background = Brushes.Transparent;
            minimizeButton.Cursor = Cursors.Hand;

            TextBlock minimizeGlyph = GlassTheme.CreateText("-", 15, false);
            minimizeGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            minimizeGlyph.VerticalAlignment = VerticalAlignment.Center;
            minimizeButton.Child = minimizeGlyph;
            minimizeButton.MouseEnter += delegate
            {
                minimizeButton.Background = GlassTheme.HoverBrush;
            };
            minimizeButton.MouseLeave += delegate
            {
                minimizeButton.Background = Brushes.Transparent;
            };
            minimizeButton.MouseLeftButtonDown += delegate
            {
                WindowState = WindowState.Minimized;
            };
            buttons.Children.Insert(0, minimizeButton);
            Grid.SetColumn(buttons, 1);
            bar.Children.Add(buttons);
            return titleBar;
        }

        private void StartDownload()
        {
            if (_downloadStarted || _downloadAction == null)
            {
                return;
            }

            _downloadStarted = true;
            if (_laterButton != null)
            {
                _laterButton.IsEnabled = false;
            }

            if (_updateButton != null)
            {
                _updateButton.IsEnabled = false;
                _updateButton.Content = "正在下载...";
            }

            ShowProgress();

            try
            {
                _downloadAction(
                    delegate(int percent)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            SetProgress(percent);
                        }));
                    },
                    delegate(string error)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            CompleteDownload(error);
                        }));
                    });
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    CompleteDownload(ex.Message);
                }));
            }
        }

        private void ShowProgress()
        {
            Border panel = new Border();
            panel.Background = GlassTheme.CreateBrush(20, 255, 255, 255);
            panel.BorderBrush = GlassTheme.HairlineBrush;
            panel.BorderThickness = new Thickness(1);
            panel.CornerRadius = new CornerRadius(12);
            panel.Padding = new Thickness(16, 14, 16, 14);

            StackPanel content = new StackPanel();
            panel.Child = content;

            TextBlock heading = GlassTheme.CreateText(
                "正在下载更新",
                15,
                false);
            heading.FontWeight = FontWeights.Bold;
            content.Children.Add(heading);

            _progressBar = new ProgressBar();
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Height = 8;
            _progressBar.Margin = new Thickness(0, 16, 0, 8);
            _progressBar.Foreground = GlassTheme.PrimaryBrush;
            _progressBar.Background = GlassTheme.CreateBrush(24, 255, 255, 255);
            _progressBar.BorderThickness = new Thickness(0);
            content.Children.Add(_progressBar);

            _progressText = GlassTheme.CreateText("0%", 13, true);
            _progressText.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(_progressText);

            _scroll.Content = panel;
        }

        private void SetProgress(int percent)
        {
            if (_progressBar == null || _progressText == null)
            {
                return;
            }

            if (percent < 0)
            {
                percent = 0;
            }

            if (percent > 100)
            {
                percent = 100;
            }

            _progressBar.Value = percent;
            _progressText.Text = percent + "%";

            if (percent >= 100)
            {
                _progressText.Text = "下载完成，正在安装更新...";
            }
        }

        private void CompleteDownload(string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                SetProgress(100);
                return;
            }

            if (_progressText != null)
            {
                _progressText.Text = "下载失败：" + error;
                _progressText.Foreground = GlassTheme.DangerBrush;
            }

            _actions.Children.Clear();
            Button close = GlassTheme.CreateButton("关闭", ButtonKind.Ghost);
            close.MinWidth = 92;
            close.Click += delegate { Close(); };
            _actions.Children.Add(close);
        }

        private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch
            {
            }
        }
    }
}
