using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Markup;

namespace GhostDesk
{
    /// <summary>
    /// 退出前询问：是否把收纳区的图标恢复到桌面。
    /// </summary>
    internal class ExitConfirmWindow : Window
    {
        public const int ChoiceRestore = 0;
        public const int ChoiceKeepHidden = 1;
        public const int ChoiceCancel = 2;

        public int Choice { get; private set; }

        public ExitConfirmWindow(int hiddenCount) : this(hiddenCount, null)
        {
        }

        public ExitConfirmWindow(int hiddenCount, Window owner)
        {
            Choice = ChoiceCancel;

            Title = "退出应用";
            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowStartupLocation = owner == null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner;
            if (owner != null)
            {
                Owner = owner;
            }

            Border surface = new Border();
            surface.CornerRadius = new CornerRadius(18);
            surface.Background = CreateBrush(198, 16, 20, 30);
            surface.BorderBrush = CreateBrush(100, 255, 255, 255);
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

            StackPanel panel = new StackPanel();
            surface.Child = panel;

            TextBlock titleText = new TextBlock();
            titleText.Text = "退出应用";
            titleText.FontSize = 15;
            titleText.FontWeight = FontWeights.SemiBold;
            titleText.Foreground = CreateBrush(246, 255, 255, 255);
            titleText.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(titleText);

            TextBlock question = new TextBlock();
            question.Text = "退出前，是否把收纳区的图标恢复到桌面？";
            question.FontSize = 13;
            question.Foreground = CreateBrush(240, 255, 255, 255);
            question.TextWrapping = TextWrapping.Wrap;
            question.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(question);

            TextBlock restoreHint = new TextBlock();
            restoreHint.Text = "· 恢复图标到桌面：图标回到桌面，收纳区配置仍会保留，下次启动会按原配置重新收纳";
            restoreHint.FontSize = 12;
            restoreHint.Foreground = CreateBrush(190, 255, 255, 255);
            restoreHint.TextWrapping = TextWrapping.Wrap;
            restoreHint.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(restoreHint);

            TextBlock keepHint = new TextBlock();
            keepHint.Text = "· 不恢复图标：图标保持隐藏，桌面保持整洁";
            keepHint.FontSize = 12;
            keepHint.Foreground = CreateBrush(190, 255, 255, 255);
            keepHint.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(keepHint);

            if (hiddenCount > 0)
            {
                TextBlock countHint = new TextBlock();
                countHint.Text = "当前收纳中的项目：" +
                    hiddenCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个";
                countHint.FontSize = 12;
                countHint.Foreground = CreateBrush(150, 255, 255, 255);
                countHint.Margin = new Thickness(0, 10, 0, 0);
                panel.Children.Add(countHint);
            }

            Button cancelButton = new Button();
            cancelButton.Content = "取消";
            cancelButton.MinWidth = 80;
            cancelButton.MinHeight = 32;
            cancelButton.Margin = new Thickness(0, 18, 10, 0);
            cancelButton.Style = CreateButtonStyle("#28000000");
            cancelButton.Click += delegate
            {
                Choice = ChoiceCancel;
                DialogResult = false;
            };

            Button keepButton = new Button();
            keepButton.Content = "不恢复图标";
            keepButton.MinWidth = 100;
            keepButton.MinHeight = 32;
            keepButton.Margin = new Thickness(0, 18, 10, 0);
            keepButton.Style = CreateButtonStyle("#28000000");
            keepButton.Click += delegate
            {
                Choice = ChoiceKeepHidden;
                DialogResult = true;
            };

            Button restoreButton = new Button();
            restoreButton.Content = "恢复图标到桌面";
            restoreButton.MinWidth = 128;
            restoreButton.MinHeight = 32;
            restoreButton.Margin = new Thickness(0, 18, 0, 0);
            restoreButton.IsDefault = true;
            restoreButton.Style = CreateButtonStyle("#3B6FD4");
            restoreButton.Click += delegate
            {
                Choice = ChoiceRestore;
                DialogResult = true;
            };

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Children.Add(cancelButton);
            actions.Children.Add(keepButton);
            actions.Children.Add(restoreButton);
            panel.Children.Add(actions);

            Content = surface;

            KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Choice = ChoiceCancel;
                    DialogResult = false;
                }
            };

            Loaded += delegate
            {
                Activate();
                restoreButton.Focus();
            };
        }

        private static SolidColorBrush CreateBrush(byte alpha, byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static Style CreateButtonStyle(string background)
        {
            string xaml = "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type Button}'>" +
                "<Setter Property='Foreground' Value='#F2FFFFFF'/>" +
                "<Setter Property='Background' Value='" + background + "'/>" +
                "<Setter Property='BorderBrush' Value='#8AFFFFFF'/>" +
                "<Setter Property='BorderThickness' Value='1'/>" +
                "<Setter Property='Padding' Value='12,5'/>" +
                "<Setter Property='FocusVisualStyle' Value='{x:Null}'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type Button}'>" +
                "<Border x:Name='Border' Background='{TemplateBinding Background}' " +
                "BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='12'>" +
                "<ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' " +
                "VerticalAlignment='Center' RecognizesAccessKey='True'/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='Border' Property='Background' Value='#40FFFFFF'/>" +
                "</Trigger>" +
                "<Trigger Property='IsPressed' Value='True'>" +
                "<Setter TargetName='Border' Property='Background' Value='#58FFFFFF'/>" +
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
