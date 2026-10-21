using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GhostDesk
{
    internal sealed class DonateWindow : Window
    {
        private Image _codeImage;
        private Button _alipayButton;
        private Button _wechatButton;

        public static void ShowDonate()
        {
            DonateWindow window = new DonateWindow();
            window.Show();
        }

        public DonateWindow()
        {
            Title = "支持 GhostDesk";
            Width = 390;
            SizeToContent = SizeToContent.Height;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            GlassTheme.ConfigureWindow(this);

            SourceInitialized += delegate
            {
                GlassTheme.EnableAcrylic(this);
            };

            Border surface = new Border();
            surface.Margin = new Thickness(1);
            surface.CornerRadius = new CornerRadius(18);
            surface.Background = GlassTheme.CreateBrush(230, 12, 16, 25);
            surface.BorderBrush = GlassTheme.HairlineBrush;
            surface.BorderThickness = new Thickness(1);
            surface.Padding = new Thickness(22, 20, 22, 22);
            surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;

            StackPanel panel = new StackPanel();
            surface.Child = panel;

            TextBlock title = GlassTheme.CreateText("支持作者", 21, false);
            title.FontWeight = FontWeights.SemiBold;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(title);
            title.MouseLeftButtonDown += Surface_MouseLeftButtonDown;

            TextBlock subtitle = GlassTheme.CreateText(
                "如果 GhostDesk 让你的桌面更清爽，可以扫码请作者喝一杯。",
                12,
                true);
            subtitle.Margin = new Thickness(0, 8, 0, 18);
            subtitle.TextWrapping = TextWrapping.Wrap;
            subtitle.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(subtitle);
            subtitle.MouseLeftButtonDown += Surface_MouseLeftButtonDown;

            StackPanel tabs = new StackPanel();
            tabs.Orientation = Orientation.Horizontal;
            tabs.Margin = new Thickness(0, 0, 0, 14);
            tabs.HorizontalAlignment = HorizontalAlignment.Center;

            _wechatButton = GlassTheme.CreateButton("微信", ButtonKind.Ghost);
            _wechatButton.MinWidth = 0;
            _wechatButton.Width = 78;
            _wechatButton.Margin = new Thickness(0, 0, 8, 0);
            _wechatButton.Click += Wechat_Click;
            tabs.Children.Add(_wechatButton);

            _alipayButton = GlassTheme.CreateButton("支付宝", ButtonKind.Ghost);
            _alipayButton.MinWidth = 0;
            _alipayButton.Width = 78;
            _alipayButton.Click += Alipay_Click;
            tabs.Children.Add(_alipayButton);

            panel.Children.Add(tabs);

            Border imageFrame = new Border();
            imageFrame.Width = 300;
            imageFrame.Height = 300;
            imageFrame.HorizontalAlignment = HorizontalAlignment.Center;
            imageFrame.Background = Brushes.White;
            imageFrame.CornerRadius = new CornerRadius(12);
            imageFrame.Padding = new Thickness(10);

            _codeImage = new Image();
            _codeImage.Stretch = Stretch.Uniform;
            _codeImage.Tag = "wechat";
            _codeImage.Source = LoadImage("donate-wechat.png");
            imageFrame.Child = _codeImage;
            panel.Children.Add(imageFrame);

            TextBlock hint = GlassTheme.CreateText("感谢支持！", 12, true);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.Margin = new Thickness(0, 14, 0, 18);
            panel.Children.Add(hint);

            Button close = GlassTheme.CreateButton("关闭", ButtonKind.Primary);
            close.Width = 110;
            close.HorizontalAlignment = HorizontalAlignment.Center;
            close.Click += delegate { Close(); };
            panel.Children.Add(close);

            Content = surface;
            UpdateTabState();
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

        private void Alipay_Click(object sender, RoutedEventArgs e)
        {
            _codeImage.Tag = "alipay";
            _codeImage.Source = LoadImage("donate-alipay.jpg");
            UpdateTabState();
        }

        private void Wechat_Click(object sender, RoutedEventArgs e)
        {
            _codeImage.Tag = "wechat";
            _codeImage.Source = LoadImage("donate-wechat.png");
            UpdateTabState();
        }

        private void UpdateTabState()
        {
            if (_codeImage == null)
            {
                return;
            }

            bool isAlipay = string.Equals(
                _codeImage.Tag as string,
                "alipay",
                StringComparison.Ordinal);

            _alipayButton.Background = isAlipay
                ? GlassTheme.CreateBrush(46, 255, 255, 255)
                : GlassTheme.CreateBrush(16, 255, 255, 255);
            _wechatButton.Background = isAlipay
                ? GlassTheme.CreateBrush(16, 255, 255, 255)
                : GlassTheme.CreateBrush(46, 255, 255, 255);
            _alipayButton.Foreground = isAlipay
                ? GlassTheme.TextPrimaryBrush
                : GlassTheme.TextSecondaryBrush;
            _wechatButton.Foreground = isAlipay
                ? GlassTheme.TextSecondaryBrush
                : GlassTheme.TextPrimaryBrush;
        }

        private static BitmapSource LoadImage(string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("GhostDesk." + resourceName))
            {
                if (stream == null)
                {
                    return CreatePlaceholder();
                }

                BitmapDecoder decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                return decoder.Frames[0];
            }
        }

        private static BitmapSource CreatePlaceholder()
        {
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                280,
                280,
                96,
                96,
                PixelFormats.Pbgra32);

            Border frame = new Border();
            frame.Width = 280;
            frame.Height = 280;
            frame.Background = Brushes.White;

            TextBlock message = new TextBlock();
            message.Text = "二维码图片未找到";
            message.Foreground = Brushes.Gray;
            message.FontSize = 18;
            message.HorizontalAlignment = HorizontalAlignment.Center;
            message.VerticalAlignment = VerticalAlignment.Center;
            message.TextWrapping = TextWrapping.Wrap;
            frame.Child = message;

            frame.Measure(new Size(280, 280));
            frame.Arrange(new Rect(0, 0, 280, 280));
            bitmap.Render(frame);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
