using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GhostDesk
{
    internal sealed class ScheduledTaskCountdownWindow : Window
    {
        private readonly ScheduledTaskConfig _task;
        private readonly DateTime _occurrenceLocal;
        private readonly Action _executeAction;
        private readonly Action _cancelAction;
        private readonly TextBlock _countdownText;
        private readonly DispatcherTimer _timer;
        private bool _finished;
        private bool _silentClose;

        public ScheduledTaskCountdownWindow(
            ScheduledTaskConfig task,
            DateTime occurrenceLocal,
            Action executeAction,
            Action cancelAction)
        {
            _task = task;
            _occurrenceLocal = occurrenceLocal;
            _executeAction = executeAction;
            _cancelAction = cancelAction;

            Title = "GhostDesk 定时任务";
            Width = 342;
            Height = 176;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            GlassTheme.ConfigureWindow(this);

            Border surface = new Border();
            surface.Margin = new Thickness(1);
            surface.CornerRadius = new CornerRadius(15);
            surface.Background = GlassTheme.CreateBrush(246, 12, 18, 29);
            surface.BorderBrush = GlassTheme.CreateBrush(78, 255, 255, 255);
            surface.BorderThickness = new Thickness(1);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            surface.Child = root;

            Grid header = new Grid();
            header.Margin = new Thickness(18, 14, 16, 5);
            header.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            header.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            root.Children.Add(header);

            TextBlock title = GlassTheme.CreateText("即将执行定时任务", 13, false);
            title.FontWeight = FontWeights.SemiBold;
            title.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(title);

            Button close = GlassTheme.CreateIconButton(
                "X",
                "取消本次",
                delegate
                {
                    Finish(_cancelAction);
                });
            Grid.SetColumn(close, 1);
            header.Children.Add(close);

            StackPanel body = new StackPanel();
            body.Margin = new Thickness(20, 2, 20, 10);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            TextBlock taskName = GlassTheme.CreateText(
                task == null || string.IsNullOrWhiteSpace(task.Name)
                    ? "定时任务"
                    : task.Name,
                13,
                false);
            taskName.FontWeight = FontWeights.SemiBold;
            taskName.TextTrimming = TextTrimming.CharacterEllipsis;
            body.Children.Add(taskName);

            _countdownText = GlassTheme.CreateText("", 16, false);
            _countdownText.Margin = new Thickness(0, 5, 0, 8);
            _countdownText.Foreground = GlassTheme.CreateBrush(235, 139, 226, 255);
            body.Children.Add(_countdownText);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            body.Children.Add(actions);

            Button cancel = GlassTheme.CreateButton("取消本次", ButtonKind.Ghost);
            cancel.Height = 31;
            cancel.MinWidth = 82;
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.Click += delegate
            {
                Finish(_cancelAction);
            };
            actions.Children.Add(cancel);

            Button execute = GlassTheme.CreateButton("立即执行", ButtonKind.Primary);
            execute.Height = 31;
            execute.MinWidth = 82;
            execute.Click += delegate
            {
                Finish(_executeAction);
            };
            actions.Children.Add(execute);

            _timer = new DispatcherTimer(DispatcherPriority.Send);
            _timer.Interval = TimeSpan.FromMilliseconds(250);
            _timer.Tick += Timer_Tick;

            Content = surface;
            Closed += ScheduledTaskCountdownWindow_Closed;
            Loaded += ScheduledTaskCountdownWindow_Loaded;
            UpdateCountdownText();
        }

        public void CloseSilently()
        {
            if (_finished)
            {
                return;
            }

            _silentClose = true;
            _finished = true;
            _timer.Stop();
            Close();
        }

        private void ScheduledTaskCountdownWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 24;
            Top = workArea.Bottom - Height - 24;
            UpdateCountdownText();
            _timer.Start();
            Timer_Tick(null, EventArgs.Empty);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            TimeSpan remaining = _occurrenceLocal - DateTime.Now;
            if (remaining.TotalMilliseconds <= 0)
            {
                Finish(_executeAction);
                return;
            }

            UpdateCountdownText();
        }

        private void UpdateCountdownText()
        {
            TimeSpan remaining = _occurrenceLocal - DateTime.Now;
            int seconds = Math.Max(
                0,
                (int)Math.Ceiling(remaining.TotalSeconds));
            _countdownText.Text = "将在 " + seconds.ToString() + " 秒后执行";
        }

        private void Finish(Action action)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _timer.Stop();
            Close();
            if (action != null)
            {
                action();
            }
        }

        private void ScheduledTaskCountdownWindow_Closed(
            object sender,
            EventArgs e)
        {
            _timer.Stop();
            if (_finished || _silentClose)
            {
                return;
            }

            _finished = true;
            if (_cancelAction != null)
            {
                _cancelAction();
            }
        }
    }
}
