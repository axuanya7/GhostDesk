using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace GhostDesk
{
    internal sealed class TimePickerControl : Grid
    {
        private readonly TextBox _hourInput;
        private readonly TextBox _minuteInput;
        private readonly Border _surface;
        private readonly Popup _popup;
        private readonly ListBox _hourList;
        private readonly ListBox _minuteList;
        private int _hour;
        private int _minute;
        private bool _updatingSelection;

        public TimePickerControl()
        {
            Height = 36;
            MinWidth = 132;

            _surface = new Border();
            _surface.CornerRadius = new CornerRadius(9);
            _surface.Background = GlassTheme.InputBrush;
            _surface.BorderBrush = GlassTheme.InputBorderBrush;
            _surface.BorderThickness = new Thickness(1);
            _surface.MouseEnter += delegate
            {
                _surface.BorderBrush = GlassTheme.CreateBrush(112, 255, 255, 255);
            };
            _surface.MouseLeave += delegate
            {
                if (!_popup.IsOpen)
                {
                    _surface.BorderBrush = GlassTheme.InputBorderBrush;
                }
            };

            Grid content = new Grid();
            content.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(38) });
            _surface.Child = content;

            Grid timeInput = new Grid();
            timeInput.Margin = new Thickness(9, 0, 2, 0);
            timeInput.VerticalAlignment = VerticalAlignment.Stretch;
            timeInput.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(28) });
            timeInput.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(12) });
            timeInput.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(28) });
            timeInput.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _hourInput = CreateTimePartInput("小时");
            _minuteInput = CreateTimePartInput("分钟");
            TextBlock colon = new TextBlock();
            colon.Text = ":";
            colon.FontFamily = new FontFamily("Consolas");
            colon.FontSize = 13;
            colon.Foreground = GlassTheme.TextPrimaryBrush;
            colon.HorizontalAlignment = HorizontalAlignment.Center;
            colon.VerticalAlignment = VerticalAlignment.Center;
            colon.IsHitTestVisible = false;
            Grid.SetColumn(colon, 1);

            Grid.SetColumn(_hourInput, 0);
            Grid.SetColumn(_minuteInput, 2);
            timeInput.Children.Add(_hourInput);
            timeInput.Children.Add(colon);
            timeInput.Children.Add(_minuteInput);
            content.Children.Add(timeInput);

            TextBlock clock = new TextBlock();
            clock.Text = "\u25F7";
            clock.FontFamily = new FontFamily("Segoe UI Symbol");
            clock.FontSize = 16;
            clock.Foreground = GlassTheme.CreateBrush(210, 238, 244, 255);
            clock.HorizontalAlignment = HorizontalAlignment.Center;
            clock.VerticalAlignment = VerticalAlignment.Center;
            clock.Cursor = Cursors.Hand;
            clock.ToolTip = "选择时间";
            clock.MouseLeftButtonUp += delegate
            {
                TogglePopup();
            };
            Grid.SetColumn(clock, 1);
            content.Children.Add(clock);

            Children.Add(_surface);

            Grid picker = new Grid();
            picker.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9) });
            picker.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            picker.RowDefinitions.Add(new RowDefinition { Height = new GridLength(176) });
            picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock hourLabel = GlassTheme.CreateText("小时", 10, true);
            hourLabel.Margin = new Thickness(8, 0, 0, 6);
            picker.Children.Add(hourLabel);

            TextBlock minuteLabel = GlassTheme.CreateText("分钟", 10, true);
            minuteLabel.Margin = new Thickness(8, 0, 0, 6);
            Grid.SetColumn(minuteLabel, 2);
            picker.Children.Add(minuteLabel);

            _hourList = CreateValueList();
            for (int value = 0; value < 24; value++)
            {
                _hourList.Items.Add(CreateValueItem(value));
            }
            _hourList.SelectionChanged += HourList_SelectionChanged;
            Grid.SetRow(_hourList, 1);
            picker.Children.Add(_hourList);

            _minuteList = CreateValueList();
            for (int value = 0; value < 60; value++)
            {
                _minuteList.Items.Add(CreateValueItem(value));
            }
            _minuteList.SelectionChanged += MinuteList_SelectionChanged;
            Grid.SetColumn(_minuteList, 2);
            Grid.SetRow(_minuteList, 1);
            picker.Children.Add(_minuteList);

            Button done = GlassTheme.CreateButton("完成", ButtonKind.Primary);
            done.Height = 30;
            done.MinWidth = 72;
            done.HorizontalAlignment = HorizontalAlignment.Right;
            done.Margin = new Thickness(0, 8, 0, 0);
            done.Click += delegate
            {
                _popup.IsOpen = false;
            };
            Grid.SetColumnSpan(done, 3);
            Grid.SetRow(done, 2);
            picker.Children.Add(done);

            Border popupSurface = new Border();
            popupSurface.Width = 224;
            popupSurface.Padding = new Thickness(10, 9, 10, 10);
            popupSurface.CornerRadius = new CornerRadius(12);
            popupSurface.Background = GlassTheme.CreateBrush(248, 13, 20, 31);
            popupSurface.BorderBrush = GlassTheme.CreateBrush(72, 255, 255, 255);
            popupSurface.BorderThickness = new Thickness(1);
            popupSurface.Child = picker;

            _popup = new Popup();
            _popup.PlacementTarget = _surface;
            _popup.Placement = PlacementMode.Bottom;
            _popup.VerticalOffset = 5;
            _popup.StaysOpen = false;
            _popup.AllowsTransparency = true;
            _popup.PopupAnimation = PopupAnimation.Fade;
            _popup.Child = popupSurface;
            _popup.Closed += delegate
            {
                _surface.BorderBrush = GlassTheme.InputBorderBrush;
            };

            DateTime now = DateTime.Now;
            SetValue(now.Hour, now.Minute);
        }

        public string Value
        {
            get
            {
                return _hour.ToString("00", CultureInfo.InvariantCulture) +
                    ":" +
                    _minute.ToString("00", CultureInfo.InvariantCulture);
            }
            set
            {
                DateTime parsed;
                if (DateTime.TryParseExact(
                        (value ?? "").Trim(),
                        "HH:mm",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out parsed))
                {
                    SetValue(parsed.Hour, parsed.Minute);
                }
            }
        }

        public bool TryGetValue(out string value)
        {
            int hour;
            int minute;
            if (TryParsePart(_hourInput.Text, 23, out hour) &&
                TryParsePart(_minuteInput.Text, 59, out minute))
            {
                SetValue(hour, minute);
                value = FormatValue(_hour, _minute);
                return true;
            }

            value = "";
            return false;
        }

        private static TextBox CreateTimePartInput(string toolTip)
        {
            TextBox input = new TextBox();
            input.Height = 28;
            input.MinWidth = 28;
            input.MaxLength = 2;
            input.Padding = new Thickness(0);
            input.BorderThickness = new Thickness(0);
            input.Background = Brushes.Transparent;
            input.Foreground = GlassTheme.TextPrimaryBrush;
            input.CaretBrush = GlassTheme.TextPrimaryBrush;
            input.FontFamily = new FontFamily("Consolas");
            input.FontSize = 13;
            input.TextAlignment = TextAlignment.Center;
            input.VerticalContentAlignment = VerticalAlignment.Center;
            input.FocusVisualStyle = null;
            input.ToolTip = toolTip;
            input.PreviewTextInput += TimePart_PreviewTextInput;
            input.GotKeyboardFocus += TimePart_GotKeyboardFocus;
            input.LostKeyboardFocus += delegate(object sender, KeyboardFocusChangedEventArgs e)
            {
                TimePickerControl owner = FindTimePicker(input);
                if (owner != null)
                {
                    owner.CommitInput();
                }
            };
            input.PreviewKeyDown += TimePart_PreviewKeyDown;
            DataObject.AddPastingHandler(input, TimePart_Pasting);
            return input;
        }

        private static TimePickerControl FindTimePicker(DependencyObject input)
        {
            DependencyObject current = input;
            while (current != null)
            {
                TimePickerControl picker = current as TimePickerControl;
                if (picker != null)
                {
                    return picker;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static void TimePart_PreviewTextInput(
            object sender,
            TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            foreach (char character in e.Text)
            {
                if (character < '0' || character > '9')
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private static void TimePart_Pasting(
            object sender,
            DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            string text = e.DataObject.GetData(DataFormats.Text) as string;
            if (string.IsNullOrEmpty(text))
            {
                e.CancelCommand();
                return;
            }

            foreach (char character in text)
            {
                if (character < '0' || character > '9')
                {
                    e.CancelCommand();
                    return;
                }
            }

            TextBox input = sender as TextBox;
            if (input != null &&
                text.Length + input.Text.Length - input.SelectionLength > 2)
            {
                e.CancelCommand();
            }
        }

        private static void TimePart_GotKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
        {
            TextBox input = sender as TextBox;
            if (input == null)
            {
                return;
            }

            input.Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    if (input.IsKeyboardFocused)
                    {
                        input.SelectAll();
                    }
                }),
                DispatcherPriority.Input);
        }

        private static void TimePart_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            TextBox input = sender as TextBox;
            if (input == null)
            {
                return;
            }

            TimePickerControl owner = FindTimePicker(input);
            if (owner == null)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (input == owner._hourInput)
                {
                    owner.CommitInput();
                    owner._minuteInput.Focus();
                    owner._minuteInput.SelectAll();
                }
                else
                {
                    owner.CommitInput();
                    input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                owner.UpdateDisplay();
                input.SelectAll();
                e.Handled = true;
            }
        }

        private void CommitInput()
        {
            int hour;
            int minute;
            if (TryParsePart(_hourInput.Text, 23, out hour) &&
                TryParsePart(_minuteInput.Text, 59, out minute))
            {
                SetValue(hour, minute);
                return;
            }

            UpdateDisplay();
        }

        private void TogglePopup()
        {
            CommitInput();
            _popup.IsOpen = !_popup.IsOpen;
            if (!_popup.IsOpen)
            {
                return;
            }

            _surface.BorderBrush = GlassTheme.CreateBrush(168, 139, 226, 255);
            Dispatcher.BeginInvoke(
                new Action(delegate
                {
                    ScrollSelectionIntoView(_hourList);
                    ScrollSelectionIntoView(_minuteList);
                }),
                DispatcherPriority.Background);
        }

        private void SetValue(int hour, int minute)
        {
            _hour = Math.Max(0, Math.Min(23, hour));
            _minute = Math.Max(0, Math.Min(59, minute));
            UpdateDisplay();

            _updatingSelection = true;
            try
            {
                _hourList.SelectedIndex = _hour;
                _minuteList.SelectedIndex = _minute;
            }
            finally
            {
                _updatingSelection = false;
            }
        }

        private void HourList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_updatingSelection || _hourList.SelectedIndex < 0)
            {
                return;
            }

            _hour = _hourList.SelectedIndex;
            UpdateDisplay();
        }

        private void MinuteList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_updatingSelection || _minuteList.SelectedIndex < 0)
            {
                return;
            }

            _minute = _minuteList.SelectedIndex;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            _hourInput.Text = _hour.ToString("00", CultureInfo.InvariantCulture);
            _minuteInput.Text = _minute.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string FormatValue(int hour, int minute)
        {
            return hour.ToString("00", CultureInfo.InvariantCulture) +
                ":" +
                minute.ToString("00", CultureInfo.InvariantCulture);
        }

        private static bool TryParsePart(
            string value,
            int maximum,
            out int result)
        {
            return int.TryParse(
                    (value ?? "").Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out result) &&
                result >= 0 &&
                result <= maximum;
        }

        private static ListBox CreateValueList()
        {
            ListBox list = new ListBox();
            list.BorderThickness = new Thickness(0);
            list.Background = Brushes.Transparent;
            list.Foreground = GlassTheme.TextPrimaryBrush;
            list.Padding = new Thickness(0);
            list.Margin = new Thickness(0);
            list.ItemContainerStyle = CreateValueItemStyle();
            ScrollViewer.SetHorizontalScrollBarVisibility(
                list,
                ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(
                list,
                ScrollBarVisibility.Hidden);
            return list;
        }

        private static ListBoxItem CreateValueItem(int value)
        {
            ListBoxItem item = new ListBoxItem();
            item.Content = value.ToString("00", CultureInfo.InvariantCulture);
            item.HorizontalContentAlignment = HorizontalAlignment.Center;
            return item;
        }

        private static Style CreateValueItemStyle()
        {
            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type ListBoxItem}'>" +
                "<Setter Property='Foreground' Value='#DCE6F5'/>" +
                "<Setter Property='FontFamily' Value='Consolas'/>" +
                "<Setter Property='FontSize' Value='13'/>" +
                "<Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Padding' Value='8,7'/>" +
                "<Setter Property='Margin' Value='0,1'/>" +
                "<Setter Property='HorizontalContentAlignment' Value='Center'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type ListBoxItem}'>" +
                "<Border x:Name='ItemBackground' CornerRadius='7' " +
                "Background='Transparent' BorderBrush='Transparent' " +
                "BorderThickness='1'>" +
                "<ContentPresenter Margin='{TemplateBinding Padding}' " +
                "HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
                "</Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='ItemBackground' Property='Background' Value='#32FFFFFF'/>" +
                "</Trigger>" +
                "<Trigger Property='IsSelected' Value='True'>" +
                "<Setter Property='Foreground' Value='#FFFFFF'/>" +
                "<Setter TargetName='ItemBackground' Property='Background' Value='#704AA6D8'/>" +
                "<Setter TargetName='ItemBackground' Property='BorderBrush' Value='#A88BE2FF'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        private static void ScrollSelectionIntoView(ListBox list)
        {
            if (list.SelectedItem != null)
            {
                list.ScrollIntoView(list.SelectedItem);
            }
        }
    }
}
