using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace GhostDesk
{
    /// <summary>
    /// 批量添加快捷方式：列出扫描到的开始菜单/桌面程序，可搜索、可多选。
    /// </summary>
    internal class AppPickerWindow : Window
    {
        private readonly List<ScannedShortcut> _all;
        private readonly ListBox _list;
        private readonly TextBox _search;
        private readonly TextBlock _countText;

        public AppPickerWindow(List<ScannedShortcut> apps)
        {
            _all = apps ?? new List<ScannedShortcut>();
            SelectedPaths = new List<string>();

            Title = "批量添加快捷方式";
            Width = 620;
            Height = 580;
            MinWidth = 480;
            MinHeight = 420;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;

            Border surface = new Border();
            surface.CornerRadius = new CornerRadius(18);
            surface.Background = GlassTheme.CreateBrush(205, 16, 20, 30);
            surface.BorderBrush = GlassTheme.CreateBrush(100, 255, 255, 255);
            surface.BorderThickness = new Thickness(1);
            surface.Padding = new Thickness(20);
            surface.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 24,
                ShadowDepth = 6,
                Opacity = 0.24
            };
            surface.MouseLeftButtonDown += delegate
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }
            };

            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            surface.Child = layout;

            StackPanel header = new StackPanel();
            header.Orientation = Orientation.Horizontal;
            header.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            TextBlock title = GlassTheme.CreateText(
                "批量添加快捷方式（共 " + _all.Count.ToString() + " 项）",
                15,
                false);
            title.FontWeight = FontWeights.SemiBold;
            title.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(title);

            _search = new TextBox();
            _search.FontSize = 13;
            _search.Padding = new Thickness(10, 6, 10, 6);
            _search.Margin = new Thickness(0, 0, 0, 10);
            _search.Background = GlassTheme.CreateBrush(90, 255, 255, 255);
            _search.Foreground = GlassTheme.CreateBrush(240, 255, 255, 255);
            _search.CaretBrush = GlassTheme.CreateBrush(240, 255, 255, 255);
            _search.BorderBrush = GlassTheme.CreateBrush(70, 255, 255, 255);
            _search.BorderThickness = new Thickness(1);
            _search.TextChanged += delegate { RefreshList(); };
            Grid.SetRow(_search, 1);
            layout.Children.Add(_search);

            _list = new ListBox();
            _list.SelectionMode = SelectionMode.Extended;
            _list.Background = GlassTheme.CreateBrush(70, 255, 255, 255);
            _list.BorderBrush = GlassTheme.CreateBrush(70, 255, 255, 255);
            _list.BorderThickness = new Thickness(1);
            _list.Foreground = GlassTheme.CreateBrush(240, 255, 255, 255);
            _list.Margin = new Thickness(0, 0, 0, 12);
            _list.MouseDoubleClick += delegate
            {
                AcceptSelection();
            };
            Grid.SetRow(_list, 2);
            layout.Children.Add(_list);

            Grid footer = new Grid();
            footer.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(footer, 3);
            layout.Children.Add(footer);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            Grid.SetColumn(actions, 1);
            footer.Children.Add(actions);

            Button selectAll = GlassTheme.CreateButton("全选", ButtonKind.Ghost);
            selectAll.Margin = new Thickness(0, 0, 8, 0);
            selectAll.Click += delegate { _list.SelectAll(); };
            actions.Children.Add(selectAll);

            Button clear = GlassTheme.CreateButton("清空选择", ButtonKind.Ghost);
            clear.Margin = new Thickness(0, 0, 8, 0);
            clear.Click += delegate { _list.UnselectAll(); };
            actions.Children.Add(clear);

            Button cancel = GlassTheme.CreateButton("取消", ButtonKind.Ghost);
            cancel.Margin = new Thickness(0, 0, 8, 0);
            cancel.Click += delegate
            {
                DialogResult = false;
                Close();
            };
            actions.Children.Add(cancel);

            Button accept = GlassTheme.CreateButton("添加选中", ButtonKind.Primary);
            accept.Click += delegate { AcceptSelection(); };
            actions.Children.Add(accept);

            _countText = GlassTheme.CreateText("", 12, true);
            _countText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_countText, 0);
            footer.Children.Add(_countText);

            Content = surface;

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
                else if (e.Key == Key.Enter)
                {
                    AcceptSelection();
                }
            };

            RefreshList();
            Loaded += delegate { _search.Focus(); };
        }

        public List<string> SelectedPaths { get; private set; }

        private void AcceptSelection()
        {
            List<string> paths = new List<string>();
            foreach (object item in _list.SelectedItems)
            {
                ListBoxItem row = item as ListBoxItem;
                ScannedShortcut shortcut = row == null
                    ? null
                    : row.Tag as ScannedShortcut;
                if (shortcut != null && !string.IsNullOrEmpty(shortcut.Path))
                {
                    paths.Add(shortcut.Path);
                }
            }

            if (paths.Count == 0)
            {
                _countText.Text = "请先选择要添加的项目";
                return;
            }

            SelectedPaths = paths;
            DialogResult = true;
            Close();
        }

        private void RefreshList()
        {
            string keyword = _search.Text == null ? "" : _search.Text.Trim();
            _list.Items.Clear();

            int shown = 0;
            foreach (ScannedShortcut shortcut in _all)
            {
                if (keyword.Length > 0 && !Matches(shortcut, keyword))
                {
                    continue;
                }

                _list.Items.Add(CreateRow(shortcut));
                shown++;
            }

            _countText.Text = keyword.Length == 0
                ? "共 " + _all.Count.ToString() + " 项，按住 Ctrl/Shift 多选"
                : "匹配 " + shown.ToString() + " 项";
        }

        private static bool Matches(ScannedShortcut shortcut, string keyword)
        {
            return Contains(shortcut.Name, keyword) ||
                Contains(shortcut.Target, keyword) ||
                Contains(shortcut.Source, keyword);
        }

        private static bool Contains(string value, string keyword)
        {
            return !string.IsNullOrEmpty(value) &&
                value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ListBoxItem CreateRow(ScannedShortcut shortcut)
        {
            ListBoxItem row = new ListBoxItem();
            row.Tag = shortcut;
            row.Padding = new Thickness(8, 4, 8, 4);
            row.HorizontalContentAlignment = HorizontalAlignment.Stretch;

            StackPanel content = new StackPanel();
            content.Orientation = Orientation.Vertical;

            TextBlock name = GlassTheme.CreateText(shortcut.Name, 13, false);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            content.Children.Add(name);

            TextBlock detail = GlassTheme.CreateText(
                shortcut.Source + " · " + shortcut.Target,
                11,
                true);
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            content.Children.Add(detail);

            row.Content = content;
            return row;
        }
    }
}
