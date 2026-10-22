using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace GhostDesk
{
    internal sealed class ScheduledTaskWindow : Window
    {
        private readonly TextBox _nameInput;
        private readonly TextBox _targetInput;
        private readonly TimePickerControl _timeInput;
        private readonly TextBox _intervalInput;
        private readonly TextBox _countdownInput;
        private readonly TextBlock _targetLabel;
        private readonly TextBlock _timeLabel;
        private readonly TextBlock _weekdayLabel;
        private readonly TextBlock _intervalLabel;
        private readonly TextBlock _errorText;
        private readonly Grid _targetRow;
        private readonly StackPanel _timeSection;
        private readonly StackPanel _weekdaySection;
        private readonly StackPanel _intervalSection;
        private readonly WrapPanel _weekdayChoices;
        private readonly Button _browseButton;
        private readonly RadioButton _applicationRadio;
        private readonly RadioButton _fileRadio;
        private readonly RadioButton _urlRadio;
        private readonly RadioButton _shutdownRadio;
        private readonly RadioButton _onceRadio;
        private readonly RadioButton _dailyRadio;
        private readonly RadioButton _weeklyRadio;
        private readonly RadioButton _intervalRadio;
        private readonly ScheduledTaskConfig _editingTask;
        private readonly string _dialogTitle;
        private readonly string _introText;
        private readonly string _confirmText;
        private readonly string _originalScheduleSignature;
        private int _selectedDay;

        public ScheduledTaskConfig ResultTask { get; private set; }

        /// <summary>
        /// 以 Show() 非模态方式打开时置 true：此时不能设置 DialogResult（会抛异常），
        /// 改用 ResultAccepted + Close()。非模态是为了不冻结桌面分区交互。
        /// </summary>
        internal bool NonModal { get; set; }

        /// <summary>用户在非模态模式下是否点击了确认。</summary>
        internal bool ResultAccepted { get; private set; }

        private void CloseWithResult(bool accepted)
        {
            if (NonModal)
            {
                ResultAccepted = accepted;
                Close();
                return;
            }

            DialogResult = accepted;
        }

        public ScheduledTaskWindow() : this(null)
        {
        }

        public ScheduledTaskWindow(ScheduledTaskConfig taskToEdit)
        {
            _editingTask = taskToEdit;
            _dialogTitle = taskToEdit == null
                ? "添加定时任务"
                : "编辑定时任务";
            _introText = taskToEdit == null
                ? "创建一个按计划自动执行的任务，并在执行前显示可取消提醒。"
                : "调整任务名称、目标或执行计划，保存后会保留原来的运行记录。";
            _confirmText = taskToEdit == null
                ? "确认添加"
                : "保存修改";
            _originalScheduleSignature = taskToEdit == null
                ? ""
                : BuildScheduleSignature(
                    taskToEdit.ScheduleMode,
                    taskToEdit.Time,
                    taskToEdit.DayOfWeek,
                    taskToEdit.IntervalMinutes);

            Title = _dialogTitle;
            Width = 520;
            Height = 580;
            MinWidth = 480;
            MinHeight = 530;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            GlassTheme.ConfigureWindow(this);
            SourceInitialized += ScheduledTaskWindow_SourceInitialized;

            Border surface = new Border();
            surface.Margin = new Thickness(1);
            surface.CornerRadius = new CornerRadius(16);
            surface.Background = GlassTheme.CreateBrush(244, 12, 18, 29);
            surface.BorderBrush = GlassTheme.CreateBrush(68, 255, 255, 255);
            surface.BorderThickness = new Thickness(1);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            surface.Child = root;

            root.Children.Add(BuildTitleBar());

            ScrollViewer scroll = new ScrollViewer();
            scroll.Margin = new Thickness(24, 0, 16, 0);
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Background = Brushes.Transparent;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            StackPanel content = new StackPanel();
            content.Margin = new Thickness(0, 2, 8, 10);
            scroll.Content = content;

            TextBlock intro = GlassTheme.CreateText(
                _introText,
                12,
                true);
            intro.TextWrapping = TextWrapping.Wrap;
            intro.Margin = new Thickness(0, 0, 0, 10);
            content.Children.Add(intro);

            content.Children.Add(CreateSectionLabel("任务类型"));
            WrapPanel typeChoices = new WrapPanel();
            typeChoices.Margin = new Thickness(0, 6, 0, 10);
            _applicationRadio = CreateChoiceRadio("应用", "TaskType", true);
            _fileRadio = CreateChoiceRadio("文件", "TaskType", false);
            _urlRadio = CreateChoiceRadio("网址", "TaskType", false);
            _shutdownRadio = CreateChoiceRadio("关机", "TaskType", false);
            typeChoices.Children.Add(_applicationRadio);
            typeChoices.Children.Add(_fileRadio);
            typeChoices.Children.Add(_urlRadio);
            typeChoices.Children.Add(_shutdownRadio);
            content.Children.Add(typeChoices);

            content.Children.Add(CreateSectionLabel("任务名称"));
            _nameInput = CreateTextBox("例如：每天打开工作区");
            _nameInput.Margin = new Thickness(0, 6, 0, 10);
            content.Children.Add(_nameInput);

            _targetLabel = CreateSectionLabel("程序路径");
            content.Children.Add(_targetLabel);
            _targetRow = new Grid();
            _targetRow.Margin = new Thickness(0, 6, 0, 10);
            _targetRow.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _targetRow.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            _targetInput = CreateTextBox("选择或输入完整路径");
            Grid.SetColumn(_targetInput, 0);
            _targetRow.Children.Add(_targetInput);
            _browseButton = GlassTheme.CreateButton("浏览", ButtonKind.Ghost);
            _browseButton.MinWidth = 76;
            _browseButton.Margin = new Thickness(8, 0, 0, 0);
            _browseButton.Click += Browse_Click;
            Grid.SetColumn(_browseButton, 1);
            _targetRow.Children.Add(_browseButton);
            content.Children.Add(_targetRow);

            content.Children.Add(CreateSectionLabel("执行模式"));
            WrapPanel modeChoices = new WrapPanel();
            modeChoices.Margin = new Thickness(0, 6, 0, 10);
            _onceRadio = CreateChoiceRadio("一次性", "ScheduleMode", true);
            _dailyRadio = CreateChoiceRadio("每天", "ScheduleMode", false);
            _weeklyRadio = CreateChoiceRadio("每周", "ScheduleMode", false);
            _intervalRadio = CreateChoiceRadio("间隔", "ScheduleMode", false);
            modeChoices.Children.Add(_onceRadio);
            modeChoices.Children.Add(_dailyRadio);
            modeChoices.Children.Add(_weeklyRadio);
            modeChoices.Children.Add(_intervalRadio);
            content.Children.Add(modeChoices);

            _weekdaySection = new StackPanel();
            _weekdayLabel = CreateSectionLabel("执行星期");
            _weekdaySection.Children.Add(_weekdayLabel);
            _weekdayChoices = new WrapPanel();
            _weekdayChoices.Margin = new Thickness(0, 6, 0, 8);
            string[] dayNames = new string[] { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            _selectedDay = (int)DateTime.Now.DayOfWeek;
            for (int index = 0; index < dayNames.Length; index++)
            {
                RadioButton day = CreateChoiceRadio(
                    dayNames[index],
                    "Weekday",
                    index == _selectedDay);
                day.Tag = index;
                day.Checked += Weekday_Checked;
                _weekdayChoices.Children.Add(day);
            }
            _weekdaySection.Children.Add(_weekdayChoices);
            content.Children.Add(_weekdaySection);

            Grid timingGrid = new Grid();
            timingGrid.Margin = new Thickness(0, 0, 0, 4);
            timingGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            timingGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(16)
            });
            timingGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            timingGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            timingGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });

            _timeSection = new StackPanel();
            _timeLabel = CreateSectionLabel("执行时间");
            _timeSection.Children.Add(_timeLabel);
            DateTime defaultTime = DateTime.Now.AddMinutes(1);
            _timeInput = new TimePickerControl();
            _timeInput.Value = defaultTime.ToString(
                "HH:mm",
                CultureInfo.InvariantCulture);
            _timeInput.Margin = new Thickness(0, 6, 0, 4);
            _timeSection.Children.Add(_timeInput);
            Grid.SetColumn(_timeSection, 0);
            Grid.SetRow(_timeSection, 0);
            timingGrid.Children.Add(_timeSection);

            _intervalSection = new StackPanel();
            _intervalLabel = CreateSectionLabel("执行间隔");
            _intervalSection.Children.Add(_intervalLabel);
            _intervalInput = CreateTextBox("间隔分钟，例如：60");
            _intervalInput.Text = "60";
            _intervalInput.Margin = new Thickness(0, 6, 0, 4);
            _intervalSection.Children.Add(_intervalInput);
            Grid.SetColumn(_intervalSection, 2);
            Grid.SetRow(_intervalSection, 0);
            timingGrid.Children.Add(_intervalSection);

            StackPanel countdownSection = new StackPanel();
            countdownSection.Children.Add(CreateSectionLabel("提前提醒（秒）"));
            _countdownInput = CreateTextBox("秒，0 表示立即执行");
            _countdownInput.Text = "0";
            _countdownInput.Margin = new Thickness(0, 6, 0, 4);
            countdownSection.Children.Add(_countdownInput);
            countdownSection.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetColumn(countdownSection, 0);
            Grid.SetRow(countdownSection, 1);
            timingGrid.Children.Add(countdownSection);
            content.Children.Add(timingGrid);

            _errorText = GlassTheme.CreateText("", 12, false);
            _errorText.Foreground = GlassTheme.DangerBrush;
            _errorText.TextWrapping = TextWrapping.Wrap;
            _errorText.Margin = new Thickness(0, 8, 0, 0);
            _errorText.Visibility = Visibility.Collapsed;
            content.Children.Add(_errorText);

            Border footer = new Border();
            footer.BorderBrush = GlassTheme.HairlineBrush;
            footer.BorderThickness = new Thickness(0, 1, 0, 0);
            footer.Padding = new Thickness(20, 10, 20, 14);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            footer.Child = actions;

            Button cancel = GlassTheme.CreateButton("取消", ButtonKind.Ghost);
            cancel.MinWidth = 88;
            cancel.Margin = new Thickness(0, 0, 10, 0);
            cancel.Click += delegate { CloseWithResult(false); };
            actions.Children.Add(cancel);

            Button confirm = GlassTheme.CreateButton(_confirmText, ButtonKind.Primary);
            confirm.MinWidth = 104;
            confirm.Click += Confirm_Click;
            actions.Children.Add(confirm);

            _applicationRadio.Checked += TaskType_Changed;
            _fileRadio.Checked += TaskType_Changed;
            _urlRadio.Checked += TaskType_Changed;
            _shutdownRadio.Checked += TaskType_Changed;
            _onceRadio.Checked += ScheduleMode_Changed;
            _dailyRadio.Checked += ScheduleMode_Changed;
            _weeklyRadio.Checked += ScheduleMode_Changed;
            _intervalRadio.Checked += ScheduleMode_Changed;

            ApplyInitialValues();
            UpdateTypeFields();
            UpdateScheduleFields();

            Content = surface;
            Loaded += delegate
            {
                Activate();
                _nameInput.Focus();
            };
        }

        private void ScheduledTaskWindow_SourceInitialized(object sender, EventArgs e)
        {
            GlassTheme.EnableAcrylic(this);
        }

        private Border BuildTitleBar()
        {
            Border titleBar = new Border();
            titleBar.Background = Brushes.Transparent;
            titleBar.Padding = new Thickness(24, 0, 14, 0);
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
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            titleBar.Child = grid;

            TextBlock title = GlassTheme.CreateText(_dialogTitle, 15, false);
            title.FontWeight = FontWeights.SemiBold;
            title.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(title);

            Button close = GlassTheme.CreateIconButton(
                "X",
                "关闭",
                delegate { CloseWithResult(false); });
            Grid.SetColumn(close, 1);
            grid.Children.Add(close);
            return titleBar;
        }

        private void TaskType_Changed(object sender, RoutedEventArgs e)
        {
            UpdateTypeFields();
        }

        private void ScheduleMode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateScheduleFields();
        }

        private void Weekday_Checked(object sender, RoutedEventArgs e)
        {
            RadioButton radio = sender as RadioButton;
            if (radio != null && radio.Tag != null &&
                radio.Tag.GetType() == typeof(int))
            {
                _selectedDay = (int)radio.Tag;
            }
        }

        private void UpdateTypeFields()
        {
            if (_targetLabel == null || _targetRow == null)
            {
                return;
            }

            bool shutdown = _shutdownRadio.IsChecked == true;
            bool url = _urlRadio.IsChecked == true;
            _targetLabel.Visibility = shutdown ? Visibility.Collapsed : Visibility.Visible;
            _targetRow.Visibility = shutdown ? Visibility.Collapsed : Visibility.Visible;
            _browseButton.Visibility = url ? Visibility.Collapsed : Visibility.Visible;

            if (shutdown)
            {
                return;
            }

            if (url)
            {
                _targetLabel.Text = "网址";
                _targetInput.ToolTip = "例如：https://example.com";
            }
            else if (_fileRadio.IsChecked == true)
            {
                _targetLabel.Text = "文件路径";
                _targetInput.ToolTip = "选择或输入要打开的文件路径";
            }
            else
            {
                _targetLabel.Text = "程序路径";
                _targetInput.ToolTip = "选择或输入要启动的程序路径";
            }
        }

        private void UpdateScheduleFields()
        {
            if (_timeSection == null)
            {
                return;
            }

            bool interval = _intervalRadio.IsChecked == true;
            bool weekly = _weeklyRadio.IsChecked == true;
            _timeSection.Visibility = Visibility.Visible;
            _timeLabel.Text = interval ? "首次执行时间" : "执行时间";
            _weekdaySection.Visibility = weekly ? Visibility.Visible : Visibility.Collapsed;
            _intervalSection.Visibility = interval ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Title = _fileRadio.IsChecked == true ? "选择定时任务文件" : "选择定时任务程序";
            dialog.CheckFileExists = true;
            if (_fileRadio.IsChecked == true)
            {
                dialog.Filter = "所有文件 (*.*)|*.*";
            }
            else
            {
                dialog.Filter =
                    "程序与快捷方式 (*.exe;*.lnk;*.bat;*.cmd;*.com)|*.exe;*.lnk;*.bat;*.cmd;*.com|所有文件 (*.*)|*.*";
            }

            bool? result = Owner == null ? dialog.ShowDialog() : dialog.ShowDialog(Owner);
            if (result == true)
            {
                _targetInput.Text = dialog.FileName;
                if (string.IsNullOrWhiteSpace(_nameInput.Text))
                {
                    _nameInput.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                }
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            _errorText.Visibility = Visibility.Collapsed;

            string name = (_nameInput.Text ?? "").Trim();
            if (name.Length == 0)
            {
                ShowError("请输入任务名称。");
                return;
            }

            string taskType = GetTaskType();
            string target = (_targetInput.Text ?? "").Trim();
            if (!string.Equals(taskType, ScheduledTaskConfig.TypeShutdown, StringComparison.Ordinal))
            {
                if (target.Length == 0)
                {
                    ShowError("请输入或选择任务目标。");
                    return;
                }

                if (string.Equals(taskType, ScheduledTaskConfig.TypeUrl, StringComparison.Ordinal))
                {
                    target = NormalizeUrl(target);
                    if (!IsHttpUrl(target))
                    {
                        ShowError("请输入有效的 http 或 https 网址。");
                        return;
                    }
                }
                else if (!File.Exists(target) && !Directory.Exists(target))
                {
                    ShowError("目标路径不存在，请重新选择。");
                    return;
                }
            }

            string mode = GetScheduleMode();
            string normalizedTime;
            if (!_timeInput.TryGetValue(out normalizedTime))
            {
                ShowError("执行时间格式应为 HH:mm，例如 09:30。");
                return;
            }

            int interval = 60;
            if (string.Equals(mode, ScheduledTaskConfig.ModeInterval, StringComparison.Ordinal) &&
                (!int.TryParse(_intervalInput.Text, out interval) ||
                 interval < 1 ||
                 interval > 10080))
            {
                ShowError("间隔分钟应为 1 到 10080 之间的整数。");
                return;
            }

            int countdown;
            if (!int.TryParse(_countdownInput.Text, out countdown) ||
                countdown < 0 ||
                countdown > 600)
            {
                ShowError("倒计时应为 0 到 600 秒之间的整数。");
                return;
            }

            if (string.Equals(taskType, ScheduledTaskConfig.TypeShutdown, StringComparison.Ordinal) &&
                countdown < 5)
            {
                ShowError("关机任务至少需要 5 秒提醒时间，避免无法取消。");
                return;
            }

            string scheduleSignature = BuildScheduleSignature(
                mode,
                normalizedTime,
                _selectedDay,
                interval);
            bool scheduleChanged = _editingTask == null ||
                !string.Equals(
                    scheduleSignature,
                    _originalScheduleSignature,
                    StringComparison.Ordinal);
            DateTime firstRunUtc;
            if (!scheduleChanged &&
                DateTime.TryParse(
                    _editingTask.FirstRunUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out firstRunUtc))
            {
                firstRunUtc = firstRunUtc.ToUniversalTime();
            }
            else
            {
                firstRunUtc = ScheduledTaskRunner.CalculateFirstRunUtc(
                    mode,
                    normalizedTime,
                    _selectedDay,
                    DateTime.Now);
            }

            ScheduledTaskConfig task = new ScheduledTaskConfig();
            task.Id = _editingTask == null || string.IsNullOrWhiteSpace(_editingTask.Id)
                ? Guid.NewGuid().ToString("N")
                : _editingTask.Id;
            task.Name = name;
            task.TaskType = taskType;
            task.Target = target;
            task.ScheduleMode = mode;
            task.Time = normalizedTime;
            task.DayOfWeek = _selectedDay;
            task.IntervalMinutes = interval;
            task.CountdownSeconds = countdown;
            task.Enabled = _editingTask == null ? true : _editingTask.Enabled;
            task.CreatedUtc = _editingTask == null ||
                string.IsNullOrWhiteSpace(_editingTask.CreatedUtc)
                ? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                : _editingTask.CreatedUtc;
            task.FirstRunUtc = firstRunUtc.ToString(
                "o",
                CultureInfo.InvariantCulture);
            task.LastRunUtc = _editingTask == null || scheduleChanged
                ? ""
                : (_editingTask.LastRunUtc ?? "");
            ResultTask = task;
            CloseWithResult(true);
        }

        private void ApplyInitialValues()
        {
            if (_editingTask == null)
            {
                return;
            }

            _nameInput.Text = _editingTask.Name ?? "";
            _targetInput.Text = _editingTask.Target ?? "";
            if (!string.IsNullOrWhiteSpace(_editingTask.Time))
            {
                _timeInput.Value = _editingTask.Time;
            }

            if (_editingTask.IntervalMinutes >= 1)
            {
                _intervalInput.Text = _editingTask.IntervalMinutes.ToString(
                    CultureInfo.InvariantCulture);
            }

            if (_editingTask.CountdownSeconds >= 0)
            {
                _countdownInput.Text = _editingTask.CountdownSeconds.ToString(
                    CultureInfo.InvariantCulture);
            }

            _selectedDay = Math.Max(0, Math.Min(6, _editingTask.DayOfWeek));
            _applicationRadio.IsChecked = string.Equals(
                _editingTask.TaskType,
                ScheduledTaskConfig.TypeApplication,
                StringComparison.Ordinal);
            _fileRadio.IsChecked = string.Equals(
                _editingTask.TaskType,
                ScheduledTaskConfig.TypeFile,
                StringComparison.Ordinal);
            _urlRadio.IsChecked = string.Equals(
                _editingTask.TaskType,
                ScheduledTaskConfig.TypeUrl,
                StringComparison.Ordinal);
            _shutdownRadio.IsChecked = string.Equals(
                _editingTask.TaskType,
                ScheduledTaskConfig.TypeShutdown,
                StringComparison.Ordinal);

            _onceRadio.IsChecked = string.Equals(
                _editingTask.ScheduleMode,
                ScheduledTaskConfig.ModeOnce,
                StringComparison.Ordinal);
            _dailyRadio.IsChecked = string.Equals(
                _editingTask.ScheduleMode,
                ScheduledTaskConfig.ModeDaily,
                StringComparison.Ordinal);
            _weeklyRadio.IsChecked = string.Equals(
                _editingTask.ScheduleMode,
                ScheduledTaskConfig.ModeWeekly,
                StringComparison.Ordinal);
            _intervalRadio.IsChecked = string.Equals(
                _editingTask.ScheduleMode,
                ScheduledTaskConfig.ModeInterval,
                StringComparison.Ordinal);

            foreach (UIElement element in _weekdayChoices.Children)
            {
                RadioButton radio = element as RadioButton;
                if (radio != null && radio.Tag != null &&
                    radio.Tag.GetType() == typeof(int))
                {
                    radio.IsChecked = (int)radio.Tag == _selectedDay;
                }
            }
        }

        private string GetTaskType()
        {
            if (_fileRadio.IsChecked == true)
            {
                return ScheduledTaskConfig.TypeFile;
            }

            if (_urlRadio.IsChecked == true)
            {
                return ScheduledTaskConfig.TypeUrl;
            }

            if (_shutdownRadio.IsChecked == true)
            {
                return ScheduledTaskConfig.TypeShutdown;
            }

            return ScheduledTaskConfig.TypeApplication;
        }

        private string GetScheduleMode()
        {
            if (_dailyRadio.IsChecked == true)
            {
                return ScheduledTaskConfig.ModeDaily;
            }

            if (_weeklyRadio.IsChecked == true)
            {
                return ScheduledTaskConfig.ModeWeekly;
            }

            if (_intervalRadio.IsChecked == true)
            {
                return ScheduledTaskConfig.ModeInterval;
            }

            return ScheduledTaskConfig.ModeOnce;
        }

        private static string BuildScheduleSignature(
            string mode,
            string time,
            int dayOfWeek,
            int intervalMinutes)
        {
            return (mode ?? "") + "|" +
                (time ?? "") + "|" +
                dayOfWeek.ToString(CultureInfo.InvariantCulture) + "|" +
                intervalMinutes.ToString(CultureInfo.InvariantCulture);
        }

        private void ShowError(string message)
        {
            _errorText.Text = message;
            _errorText.Visibility = Visibility.Visible;
        }

        private static string NormalizeUrl(string value)
        {
            string url = (value ?? "").Trim();
            if (url.Length > 0 &&
                url.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                url = "https://" + url;
            }

            return url;
        }

        private static bool IsHttpUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static TextBlock CreateSectionLabel(string text)
        {
            TextBlock label = GlassTheme.CreateText(text, 12, false);
            label.FontWeight = FontWeights.SemiBold;
            return label;
        }

        private static TextBox CreateTextBox(string toolTip)
        {
            TextBox input = new TextBox();
            input.MinHeight = 34;
            input.Padding = new Thickness(10, 6, 10, 6);
            input.VerticalContentAlignment = VerticalAlignment.Center;
            input.Background = GlassTheme.InputBrush;
            input.BorderBrush = GlassTheme.InputBorderBrush;
            input.Foreground = GlassTheme.TextPrimaryBrush;
            input.CaretBrush = GlassTheme.TextPrimaryBrush;
            input.ToolTip = toolTip;
            input.Style = CreateTextBoxStyle();
            return input;
        }

        private static RadioButton CreateChoiceRadio(
            string text,
            string groupName,
            bool isChecked)
        {
            RadioButton radio = new RadioButton();
            radio.Content = text;
            radio.GroupName = groupName;
            radio.IsChecked = isChecked;
            radio.Cursor = Cursors.Hand;
            radio.Margin = new Thickness(0, 0, 8, 8);
            radio.Style = CreateChoiceStyle();
            return radio;
        }

        private static Style CreateTextBoxStyle()
        {
            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type TextBox}'>" +
                "<Setter Property='FontFamily' Value='Microsoft YaHei UI'/>" +
                "<Setter Property='FontSize' Value='13'/>" +
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
            return (Style)XamlReader.Parse(xaml);
        }

        private static Style CreateChoiceStyle()
        {
            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type RadioButton}'>" +
                "<Setter Property='Foreground' Value='#C6D2E4'/>" +
                "<Setter Property='FontFamily' Value='Microsoft YaHei UI'/>" +
                "<Setter Property='FontSize' Value='12'/>" +
                "<Setter Property='Padding' Value='14,8'/>" +
                "<Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type RadioButton}'>" +
                "<Border x:Name='Chip' CornerRadius='9' Background='#1CFFFFFF' " +
                "BorderBrush='#38FFFFFF' BorderThickness='1'>" +
                "<ContentPresenter Margin='{TemplateBinding Padding}' " +
                "HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='Chip' Property='Background' Value='#30FFFFFF'/>" +
                "<Setter TargetName='Chip' Property='BorderBrush' Value='#62FFFFFF'/>" +
                "</Trigger>" +
                "<Trigger Property='IsChecked' Value='True'>" +
                "<Setter Property='Foreground' Value='#F4FBFF'/>" +
                "<Setter TargetName='Chip' Property='Background' Value='#544BA7CF'/>" +
                "<Setter TargetName='Chip' Property='BorderBrush' Value='#A88BE2FF'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>";
            return (Style)XamlReader.Parse(xaml);
        }
    }
}
