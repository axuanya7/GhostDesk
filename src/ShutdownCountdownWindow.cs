using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace GhostDesk
{
    internal sealed class ShutdownCountdownWindow : Window
    {
        private static ShutdownCountdownWindow _activeWindow;

        private readonly string _taskName;
        private readonly TextBlock _countdownText;
        private readonly DispatcherTimer _timer;
        private int _remainingSeconds;
        private bool _shutdownStarted;

        private ShutdownCountdownWindow(string taskName, int seconds)
        {
            _taskName = string.IsNullOrWhiteSpace(taskName)
                ? "定时关机"
                : taskName.Trim();
            _remainingSeconds = Math.Max(5, Math.Min(600, seconds));

            Title = "GhostDesk 定时关机";
            Width = 430;
            Height = 286;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            ShowActivated = true;
            Topmost = true;
            GlassTheme.ConfigureWindow(this);
            SourceInitialized += ShutdownCountdownWindow_SourceInitialized;
            Closed += ShutdownCountdownWindow_Closed;

            Border surface = new Border();
            surface.Margin = new Thickness(1);
            surface.CornerRadius = new CornerRadius(16);
            surface.Background = GlassTheme.CreateBrush(246, 12, 18, 29);
            surface.BorderBrush = GlassTheme.CreateBrush(76, 255, 255, 255);
            surface.BorderThickness = new Thickness(1);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(44)
            });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            surface.Child = root;

            root.Children.Add(BuildTitleBar());

            StackPanel body = new StackPanel();
            body.Margin = new Thickness(28, 12, 28, 8);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            TextBlock task = GlassTheme.CreateText(_taskName, 15, false);
            task.FontWeight = FontWeights.SemiBold;
            task.TextAlignment = TextAlignment.Center;
            task.TextTrimming = TextTrimming.CharacterEllipsis;
            body.Children.Add(task);

            _countdownText = GlassTheme.CreateText("", 42, false);
            _countdownText.FontWeight = FontWeights.Bold;
            _countdownText.TextAlignment = TextAlignment.Center;
            _countdownText.Margin = new Thickness(0, 12, 0, 8);
            body.Children.Add(_countdownText);

            TextBlock explanation = GlassTheme.CreateText(
                "关闭此窗口或点击取消，本次关机将不会执行。",
                12,
                true);
            explanation.TextAlignment = TextAlignment.Center;
            explanation.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(explanation);

            Border footer = new Border();
            footer.BorderBrush = GlassTheme.HairlineBrush;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(24, 12, 24, 16);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Button cancel = GlassTheme.CreateButton("取消关机", ButtonKind.Primary);
            cancel.MinWidth = 112;
            cancel.HorizontalAlignment = HorizontalAlignment.Center;
            cancel.Click += delegate
            {
                _timer.Stop();
                Close();
            };
            footer.Child = cancel;

            _timer = new DispatcherTimer(DispatcherPriority.Send);
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;

            Content = surface;
            UpdateCountdownText();
            Loaded += delegate
            {
                Activate();
                _timer.Start();
            };
        }

        public static bool TryShow(string taskName, int seconds)
        {
            if (Application.Current == null ||
                Application.Current.Dispatcher == null)
            {
                return false;
            }

            if (_activeWindow != null)
            {
                _activeWindow.Activate();
                _activeWindow.Topmost = true;
                return true;
            }

            ShutdownCountdownWindow window =
                new ShutdownCountdownWindow(taskName, seconds);
            _activeWindow = window;
            window.Show();
            return true;
        }

        private Border BuildTitleBar()
        {
            Border titleBar = new Border();
            titleBar.Padding = new Thickness(22, 0, 10, 0);
            titleBar.Background = Brushes.Transparent;
            titleBar.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
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
            };

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            titleBar.Child = grid;

            TextBlock title = GlassTheme.CreateText("定时关机", 14, false);
            title.FontWeight = FontWeights.SemiBold;
            title.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(title);

            Button close = GlassTheme.CreateIconButton(
                "X",
                "取消关机",
                delegate
                {
                    _timer.Stop();
                    Close();
                });
            Grid.SetColumn(close, 1);
            grid.Children.Add(close);
            return titleBar;
        }

        private void ShutdownCountdownWindow_SourceInitialized(
            object sender,
            EventArgs e)
        {
            GlassTheme.EnableAcrylic(this);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_remainingSeconds <= 1)
            {
                ExecuteShutdown();
                return;
            }

            _remainingSeconds--;
            UpdateCountdownText();
        }

        private void UpdateCountdownText()
        {
            _countdownText.Text =
                _remainingSeconds.ToString() + " 秒后关机";
        }

        private void ExecuteShutdown()
        {
            if (_shutdownStarted)
            {
                return;
            }

            _shutdownStarted = true;
            _timer.Stop();
            Close();

            try
            {
                ProcessStartInfo startInfo =
                    new ProcessStartInfo("shutdown.exe", "/s /t 0");
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "执行关机失败：\n" + ex.Message,
                    "GhostDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ShutdownCountdownWindow_Closed(object sender, EventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
            }

            if (object.ReferenceEquals(_activeWindow, this))
            {
                _activeWindow = null;
            }
        }
    }
}
