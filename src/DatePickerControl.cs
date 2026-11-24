using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GhostDesk
{
    /// <summary>
    /// 一次性任务的执行日期：默认「自动」，保存时按当前时间判定今天还是明天；
    /// 点右侧日历图标可以自选某一天（自选后按所选日期执行，早于当前时间则顺延到次日）。
    /// </summary>
    internal sealed class DatePickerControl : Grid
    {
        private const string DateFormat = "yyyy-MM-dd";

        private static readonly string[] WeekdayNames = new string[]
        {
            "日", "一", "二", "三", "四", "五", "六"
        };

        private readonly Border _surface;
        private readonly TextBlock _valueText;
        private readonly Popup _popup;
        private readonly TextBlock _monthText;
        private readonly Border[] _cells;
        private readonly TextBlock[] _cellTexts;
        private readonly Brush _selectedBrush;
        private string _value;
        private DateTime _viewMonth;

        public DatePickerControl()
        {
            Height = 36;
            HorizontalAlignment = HorizontalAlignment.Left;
            _value = "";
            _cells = new Border[42];
            _cellTexts = new TextBlock[42];
            _selectedBrush = GlassTheme.CreateBrush(96, 75, 167, 207);
            _viewMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);

            _surface = new Border();
            _surface.CornerRadius = new CornerRadius(9);
            _surface.Background = GlassTheme.InputBrush;
            _surface.BorderBrush = GlassTheme.InputBorderBrush;
            _surface.BorderThickness = new Thickness(1);
            _surface.Cursor = Cursors.Hand;
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
            _surface.MouseLeftButtonUp += delegate
            {
                TogglePopup();
            };

            Grid content = new Grid();
            content.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(34) });

            _valueText = new TextBlock();
            _valueText.FontFamily = new FontFamily("Microsoft YaHei UI");
            _valueText.FontSize = 13;
            _valueText.Foreground = GlassTheme.TextPrimaryBrush;
            _valueText.VerticalAlignment = VerticalAlignment.Center;
            _valueText.Margin = new Thickness(10, 0, 2, 0);
            _valueText.IsHitTestVisible = false;
            content.Children.Add(_valueText);

            TextBlock glyph = new TextBlock();
            glyph.Text = "\uE787";
            glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
            glyph.FontSize = 14;
            glyph.Foreground = GlassTheme.CreateBrush(210, 238, 244, 255);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.IsHitTestVisible = false;
            Grid.SetColumn(glyph, 1);
            content.Children.Add(glyph);

            _surface.Child = content;
            Children.Add(_surface);

            Grid picker = new Grid();
            picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            header.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

            Button previous = CreateNavButton("\u2039", "上个月");
            previous.Click += delegate { ShiftMonth(-1); };
            header.Children.Add(previous);

            _monthText = new TextBlock();
            _monthText.FontFamily = new FontFamily("Microsoft YaHei UI");
            _monthText.FontSize = 12;
            _monthText.Foreground = GlassTheme.TextPrimaryBrush;
            _monthText.HorizontalAlignment = HorizontalAlignment.Center;
            _monthText.VerticalAlignment = VerticalAlignment.Center;
            _monthText.IsHitTestVisible = false;
            Grid.SetColumn(_monthText, 1);
            header.Children.Add(_monthText);

            Button next = CreateNavButton("\u203A", "下个月");
            next.Click += delegate { ShiftMonth(1); };
            Grid.SetColumn(next, 2);
            header.Children.Add(next);
            picker.Children.Add(header);

            Grid weekdayRow = new Grid();
            weekdayRow.Margin = new Thickness(0, 6, 0, 2);
            for (int column = 0; column < 7; column++)
            {
                weekdayRow.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TextBlock label = GlassTheme.CreateText(WeekdayNames[column], 10, true);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(label, column);
                weekdayRow.Children.Add(label);
            }
            Grid.SetRow(weekdayRow, 1);
            picker.Children.Add(weekdayRow);

            Grid days = new Grid();
            for (int column = 0; column < 7; column++)
            {
                days.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            for (int row = 0; row < 6; row++)
            {
                days.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            }
            for (int index = 0; index < 42; index++)
            {
                Border cell = new Border();
                cell.CornerRadius = new CornerRadius(8);
                cell.Margin = new Thickness(1);
                cell.Background = Brushes.Transparent;
                cell.Cursor = Cursors.Hand;

                TextBlock day = new TextBlock();
                day.FontFamily = new FontFamily("Microsoft YaHei UI");
                day.FontSize = 12;
                day.Foreground = GlassTheme.TextPrimaryBrush;
                day.HorizontalAlignment = HorizontalAlignment.Center;
                day.VerticalAlignment = VerticalAlignment.Center;
                day.IsHitTestVisible = false;
                cell.Child = day;

                Border captured = cell;
                cell.MouseEnter += delegate
                {
                    captured.Background = GlassTheme.CreateBrush(40, 255, 255, 255);
                };
                cell.MouseLeave += delegate
                {
                    RefreshCellBackground(captured);
                };
                cell.MouseLeftButtonUp += delegate
                {
                    SelectCell(captured);
                };

                _cells[index] = cell;
                _cellTexts[index] = day;
                Grid.SetColumn(cell, index % 7);
                Grid.SetRow(cell, index / 7);
                days.Children.Add(cell);
            }
            Grid.SetRow(days, 2);
            picker.Children.Add(days);

            StackPanel footer = new StackPanel();
            footer.Orientation = Orientation.Horizontal;
            footer.HorizontalAlignment = HorizontalAlignment.Right;
            footer.Margin = new Thickness(0, 8, 0, 0);

            Button automatic = CreateFooterButton("自动", "按当前时间自动选今天或明天");
            automatic.Click += delegate { CommitValue(""); };
            footer.Children.Add(automatic);

            Button today = CreateFooterButton("今天", "选择今天");
            today.Click += delegate
            {
                CommitValue(DateTime.Today.ToString(DateFormat, CultureInfo.InvariantCulture));
            };
            footer.Children.Add(today);

            Button tomorrow = CreateFooterButton("明天", "选择明天");
            tomorrow.Click += delegate
            {
                CommitValue(DateTime.Today.AddDays(1).ToString(
                    DateFormat,
                    CultureInfo.InvariantCulture));
            };
            footer.Children.Add(tomorrow);
            Grid.SetRow(footer, 3);
            picker.Children.Add(footer);

            Border popupSurface = new Border();
            popupSurface.Width = 236;
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

            UpdateValueText();
            RebuildDays();
        }

        /// <summary>"" 表示自动（今天或明天）；否则为 yyyy-MM-dd。</summary>
        public string Value
        {
            get
            {
                return _value;
            }
            set
            {
                _value = NormalizeDate(value);
                DateTime parsed;
                if (ScheduledTaskRunner.TryParseOnceDate(_value, out parsed))
                {
                    _viewMonth = new DateTime(parsed.Year, parsed.Month, 1);
                }
                else
                {
                    _viewMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                }

                UpdateValueText();
                RebuildDays();
            }
        }

        /// <summary>用户是否自选了具体日期（false = 自动）。</summary>
        public bool HasCustomDate
        {
            get { return _value.Length > 0; }
        }

        private static string NormalizeDate(string value)
        {
            DateTime parsed;
            if (ScheduledTaskRunner.TryParseOnceDate(value, out parsed))
            {
                return parsed.ToString(DateFormat, CultureInfo.InvariantCulture);
            }

            return "";
        }

        private void TogglePopup()
        {
            if (_popup.IsOpen)
            {
                _popup.IsOpen = false;
                return;
            }

            RebuildDays();
            _popup.IsOpen = true;
            _surface.BorderBrush = GlassTheme.CreateBrush(168, 139, 226, 255);
        }

        private void ShiftMonth(int delta)
        {
            _viewMonth = _viewMonth.AddMonths(delta);
            RebuildDays();
        }

        private void SelectCell(Border cell)
        {
            if (cell == null || !(cell.Tag is DateTime))
            {
                return;
            }

            DateTime day = (DateTime)cell.Tag;
            CommitValue(day.ToString(DateFormat, CultureInfo.InvariantCulture));
        }

        private void CommitValue(string value)
        {
            Value = value;
            _popup.IsOpen = false;
        }

        private void UpdateValueText()
        {
            if (_value.Length == 0)
            {
                _valueText.Text = "自动（今天 / 明天）";
                _valueText.Foreground = GlassTheme.TextSecondaryBrush;
            }
            else
            {
                _valueText.Text = _value;
                _valueText.Foreground = GlassTheme.TextPrimaryBrush;
            }
        }

        private void RebuildDays()
        {
            _monthText.Text = _viewMonth.ToString("yyyy 年 M 月", CultureInfo.InvariantCulture);
            int offset = (int)_viewMonth.DayOfWeek;
            DateTime today = DateTime.Today;

            for (int index = 0; index < 42; index++)
            {
                DateTime day = _viewMonth.AddDays(index - offset);
                Border cell = _cells[index];
                TextBlock text = _cellTexts[index];

                if (day.Month != _viewMonth.Month || day.Year != _viewMonth.Year)
                {
                    cell.Tag = null;
                    cell.Visibility = Visibility.Collapsed;
                    continue;
                }

                cell.Tag = day.Date;
                cell.Visibility = Visibility.Visible;
                text.Text = day.Day.ToString(CultureInfo.InvariantCulture);

                bool selected = string.Equals(
                    day.Date.ToString(DateFormat, CultureInfo.InvariantCulture),
                    _value,
                    StringComparison.Ordinal);
                text.Foreground = selected || day.Date == today
                    ? GlassTheme.CreateBrush(255, 139, 226, 255)
                    : GlassTheme.TextPrimaryBrush;
                cell.Background = selected ? _selectedBrush : Brushes.Transparent;
            }
        }

        private void RefreshCellBackground(Border cell)
        {
            if (cell == null || !(cell.Tag is DateTime))
            {
                cell.Background = Brushes.Transparent;
                return;
            }

            DateTime day = (DateTime)cell.Tag;
            bool selected = string.Equals(
                day.ToString(DateFormat, CultureInfo.InvariantCulture),
                _value,
                StringComparison.Ordinal);
            cell.Background = selected ? _selectedBrush : Brushes.Transparent;
        }

        private static Button CreateNavButton(string glyph, string toolTip)
        {
            Button button = GlassTheme.CreateButton(glyph, ButtonKind.Ghost);
            button.Width = 24;
            button.Height = 24;
            button.MinWidth = 24;
            button.Padding = new Thickness(0);
            button.FontSize = 13;
            button.ToolTip = toolTip;
            return button;
        }

        private static Button CreateFooterButton(string text, string toolTip)
        {
            Button button = GlassTheme.CreateButton(text, ButtonKind.Ghost);
            button.Height = 26;
            button.MinWidth = 0;
            button.Padding = new Thickness(8, 0, 8, 0);
            button.FontSize = 11;
            button.Margin = new Thickness(4, 0, 0, 0);
            button.ToolTip = toolTip;
            return button;
        }
    }
}
