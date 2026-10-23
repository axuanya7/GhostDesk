using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace GhostDesk
{
    /// <summary>说明类弹窗：标题 + 若干段落文字 + 关闭按钮，使用系统标准窗体。</summary>
    internal class InfoWindow : Window
    {
        public InfoWindow(string title, string subtitle, IList<string> sections)
        {
            Title = title;
            Width = 560;
            Height = 560;
            MinWidth = 420;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = GlassTheme.CreateBrush(255, 18, 22, 32);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            Foreground = GlassTheme.TextPrimaryBrush;
            ShowInTaskbar = false;

            Grid root = new Grid();
            root.Margin = new Thickness(20, 16, 20, 18);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock heading = GlassTheme.CreateText(title, 18, false);
            heading.FontWeight = FontWeights.SemiBold;
            Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            if (!string.IsNullOrEmpty(subtitle))
            {
                TextBlock sub = GlassTheme.CreateText(subtitle, 12, true);
                sub.Margin = new Thickness(0, 6, 0, 0);
                sub.TextWrapping = TextWrapping.Wrap;
                Grid.SetRow(sub, 1);
                root.Children.Add(sub);
            }

            ScrollViewer scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Margin = new Thickness(0, 14, 0, 12);

            StackPanel body = new StackPanel();
            if (sections != null)
            {
                foreach (string section in sections)
                {
                    if (string.IsNullOrEmpty(section))
                    {
                        continue;
                    }

                    TextBlock line = GlassTheme.CreateText(section, 13, false);
                    line.TextWrapping = TextWrapping.Wrap;
                    line.Margin = new Thickness(0, 0, 0, 10);
                    body.Children.Add(line);
                }
            }

            scroll.Content = body;
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            Button close = GlassTheme.CreateButton("知道了", ButtonKind.Primary);
            close.Click += delegate { Close(); };
            actions.Children.Add(close);
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);

            Content = root;

            SourceInitialized += delegate
            {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd == IntPtr.Zero)
                    {
                        return;
                    }

                    int enabled = 1;
                    if (NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int)) != 0)
                    {
                        NativeMethods.DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
                    }
                }
                catch
                {
                }
            };

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter)
                {
                    Close();
                }
            };
        }
    }
}
