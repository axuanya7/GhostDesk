using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace GhostDesk
{
    internal static class GlassTheme
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaSystemBackdropType = 38;
        private const int BackdropTransientWindow = 3;
        private static Style _contextMenuStyle;

        public static readonly Color TextPrimary = Color.FromRgb(238, 244, 255);
        public static readonly Color TextSecondary = Color.FromRgb(168, 178, 196);
        public static readonly Color Accent = Color.FromRgb(112, 208, 255);

        public static Brush TextPrimaryBrush
        {
            get { return new SolidColorBrush(TextPrimary); }
        }

        public static Brush TextSecondaryBrush
        {
            get { return new SolidColorBrush(TextSecondary); }
        }

        public static Brush HairlineBrush
        {
            get { return CreateBrush(30, 255, 255, 255); }
        }

        public static Brush CardBrush
        {
            get { return CreateBrush(25, 255, 255, 255); }
        }

        public static Brush HoverBrush
        {
            get { return CreateBrush(42, 255, 255, 255); }
        }

        public static Brush PrimaryBrush
        {
            get { return CreateBrush(84, 58, 178, 255); }
        }

        public static Brush PrimaryBorderBrush
        {
            get { return CreateBrush(130, 108, 216, 255); }
        }

        public static Brush DangerBrush
        {
            get { return CreateBrush(38, 255, 92, 122); }
        }

        public static Brush DangerBorderBrush
        {
            get { return CreateBrush(92, 255, 118, 151); }
        }

        public static Brush InputBrush
        {
            get { return CreateBrush(28, 16, 22, 34); }
        }

        public static Brush InputBorderBrush
        {
            get { return CreateBrush(64, 255, 255, 255); }
        }

        public static Brush CreateBrush(int alpha, byte red, byte green, byte blue)
        {
            return new SolidColorBrush(Color.FromArgb((byte)alpha, red, green, blue));
        }

        public static void ApplyContextMenuStyle(ContextMenu menu)
        {
            if (menu == null)
            {
                return;
            }

            if (_contextMenuStyle == null)
            {
                _contextMenuStyle = CreateContextMenuStyle();
            }

            menu.Style = _contextMenuStyle;
            menu.HasDropShadow = true;
            menu.SnapsToDevicePixels = true;
            menu.UseLayoutRounding = true;

            // 菜单打开期间通知桌面层取消覆盖层置顶，否则菜单会被分区挡住
            menu.Opened += delegate { NotifyContextMenuVisibility(true); };
            menu.Closed += delegate { NotifyContextMenuVisibility(false); };
        }

        /// <summary>右键菜单打开/关闭通知，true = 打开。</summary>
        internal static Action<bool> ContextMenuVisibilityChanged;

        private static void NotifyContextMenuVisibility(bool opened)
        {
            Action<bool> handler = ContextMenuVisibilityChanged;
            if (handler != null)
            {
                handler(opened);
            }
        }

        private static Style CreateContextMenuStyle()
        {
            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type ContextMenu}'>" +
                "<Style.Resources>" +
                "<Style TargetType='{x:Type MenuItem}'>" +
                "<Setter Property='Foreground' Value='#F2F6FFFF'/>" +
                "<Setter Property='FontFamily' Value='Microsoft YaHei UI'/>" +
                "<Setter Property='FontSize' Value='13'/>" +
                "<Setter Property='Margin' Value='0,1'/>" +
                "<Setter Property='SnapsToDevicePixels' Value='True'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type MenuItem}'>" +
                "<Grid>" +
                "<Border x:Name='ItemBackground' Background='Transparent' " +
                "BorderBrush='Transparent' BorderThickness='1' CornerRadius='7'/>" +
                "<Border x:Name='HoverAccent' Width='2' Height='18' Margin='5,0,0,0' " +
                "HorizontalAlignment='Left' VerticalAlignment='Center' " +
                "Background='#8BE2FF' CornerRadius='1' Visibility='Collapsed'/>" +
                "<Grid MinHeight='34' Margin='8,0,6,0'>" +
                "<Grid.ColumnDefinitions>" +
                "<ColumnDefinition Width='20'/>" +
                "<ColumnDefinition Width='*'/>" +
                "<ColumnDefinition Width='18'/>" +
                "</Grid.ColumnDefinitions>" +
                "<TextBlock x:Name='CheckGlyph' Text='&#x2713;' FontSize='13' " +
                "FontWeight='SemiBold' Foreground='#8BE2FF' " +
                "HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>" +
                "<ContentPresenter x:Name='HeaderHost' Grid.Column='1' ContentSource='Header' " +
                "RecognizesAccessKey='True' VerticalAlignment='Center' Margin='2,0,10,0'/>" +
                "<Path x:Name='SubmenuArrow' Grid.Column='2' Data='M 0 0 L 4 4 L 0 8' " +
                "Stroke='#CDD8E8' StrokeThickness='1.5' StrokeStartLineCap='Round' " +
                "StrokeEndLineCap='Round' StrokeLineJoin='Round' " +
                "HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>" +
                "</Grid>" +
                "<Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='2' VerticalOffset='-6' " +
                "AllowsTransparency='True' Focusable='False' PopupAnimation='Fade' " +
                "IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>" +
                "<Border Margin='8' Padding='6' Background='#F21B2433' BorderBrush='#42FFFFFF' " +
                "BorderThickness='1' CornerRadius='10'>" +
                "<Border.Effect>" +
                "<DropShadowEffect BlurRadius='18' ShadowDepth='3' Opacity='0.42' Color='#000000'/>" +
                "</Border.Effect>" +
                "<ScrollViewer MaxHeight='520' VerticalScrollBarVisibility='Hidden' " +
                "HorizontalScrollBarVisibility='Disabled'>" +
                "<ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>" +
                "</ScrollViewer>" +
                "</Border>" +
                "</Popup>" +
                "</Grid>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsHighlighted' Value='True'>" +
                "<Setter TargetName='ItemBackground' Property='Background' Value='#32FFFFFF'/>" +
                "<Setter TargetName='ItemBackground' Property='BorderBrush' Value='#20FFFFFF'/>" +
                "<Setter TargetName='HoverAccent' Property='Visibility' Value='Visible'/>" +
                "</Trigger>" +
                "<Trigger Property='IsChecked' Value='True'>" +
                "<Setter TargetName='CheckGlyph' Property='Visibility' Value='Visible'/>" +
                "</Trigger>" +
                "<Trigger Property='HasItems' Value='True'>" +
                "<Setter TargetName='SubmenuArrow' Property='Visibility' Value='Visible'/>" +
                "</Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'>" +
                "<Setter Property='Opacity' Value='0.42'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>" +
                "<Style TargetType='{x:Type Separator}'>" +
                "<Setter Property='Height' Value='9'/>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type Separator}'>" +
                "<Border Height='1' Margin='10,4' Background='#28FFFFFF'/>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>" +
                "</Style.Resources>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type ContextMenu}'>" +
                "<Border Margin='8' Padding='6' Background='#F21B2433' BorderBrush='#42FFFFFF' " +
                "BorderThickness='1' CornerRadius='10'>" +
                "<Border.Effect>" +
                "<DropShadowEffect BlurRadius='20' ShadowDepth='4' Opacity='0.46' Color='#000000'/>" +
                "</Border.Effect>" +
                "<ScrollViewer MaxHeight='620' VerticalScrollBarVisibility='Hidden' " +
                "HorizontalScrollBarVisibility='Disabled'>" +
                "<ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>" +
                "</ScrollViewer>" +
                "</Border>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>";

            return (Style)XamlReader.Parse(xaml);
        }

        public static void ConfigureWindow(Window window)
        {
            window.WindowStyle = WindowStyle.None;
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;
            window.FontFamily = new FontFamily("Microsoft YaHei UI");
            window.Foreground = TextPrimaryBrush;
        }

        public static bool EnableAcrylic(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }

                int darkMode = 1;
                DwmSetWindowAttribute(
                    hwnd,
                    DwmwaUseImmersiveDarkMode,
                    ref darkMode,
                    sizeof(int));

                int backdrop = BackdropTransientWindow;
                int result = DwmSetWindowAttribute(
                    hwnd,
                    DwmwaSystemBackdropType,
                    ref backdrop,
                    sizeof(int));
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public static Border CreateWindowRoot(out Border sidebar, out Grid mainGrid)
        {
            Grid root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(246) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            sidebar = CreatePanel(new Thickness(1), new CornerRadius(16, 0, 0, 16));
            Grid.SetColumn(sidebar, 0);
            root.Children.Add(sidebar);

            mainGrid = new Grid();
            Grid.SetColumn(mainGrid, 1);
            root.Children.Add(mainGrid);

            Border host = new Border();
            host.Child = root;
            host.Margin = new Thickness(0);
            host.BorderBrush = HairlineBrush;
            host.BorderThickness = new Thickness(1);
            host.CornerRadius = new CornerRadius(16);
            return host;
        }

        public static Border CreatePanel(Thickness margin, CornerRadius cornerRadius)
        {
            Border border = new Border();
            border.Margin = margin;
            border.Background = CreateBrush(54, 255, 255, 255);
            border.BorderBrush = HairlineBrush;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = cornerRadius;
            return border;
        }

        public static Border CreateCard(Thickness margin, Thickness padding)
        {
            Border border = new Border();
            border.Margin = margin;
            border.Padding = padding;
            border.Background = CreateBrush(20, 255, 255, 255);
            border.BorderBrush = HairlineBrush;
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(12);
            return border;
        }

        public static TextBlock CreateText(string text, double fontSize, bool secondary)
        {
            TextBlock textBlock = new TextBlock();
            textBlock.Text = text;
            textBlock.FontSize = fontSize;
            textBlock.Foreground = secondary ? TextSecondaryBrush : TextPrimaryBrush;
            return textBlock;
        }

        public static Button CreateButton(string text, ButtonKind kind)
        {
            Button button = new Button();
            button.Content = text;
            button.Height = 34;
            button.MinWidth = 88;
            button.Padding = new Thickness(16, 0, 16, 0);
            button.Cursor = Cursors.Hand;
            button.Foreground = TextPrimaryBrush;
            button.FontSize = 13;
            button.Background = kind == ButtonKind.Primary ? PrimaryBrush : CreateBrush(16, 255, 255, 255);
            button.BorderBrush = kind == ButtonKind.Primary
                ? PrimaryBorderBrush
                : (kind == ButtonKind.Danger ? DangerBorderBrush : HairlineBrush);
            button.BorderThickness = new Thickness(1);

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            SetTemplateBinding(border, Border.BackgroundProperty, Control.BackgroundProperty);
            SetTemplateBinding(border, Border.BorderBrushProperty, Control.BorderBrushProperty);
            border.SetValue(Border.BorderThicknessProperty, button.BorderThickness);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            SetTemplateBinding(content, ContentPresenter.HorizontalAlignmentProperty, Control.HorizontalContentAlignmentProperty);
            SetTemplateBinding(content, ContentPresenter.VerticalAlignmentProperty, Control.VerticalContentAlignmentProperty);
            SetTemplateBinding(content, ContentPresenter.MarginProperty, Control.PaddingProperty);
            border.AppendChild(content);
            template.VisualTree = border;

            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, HoverBrush, "ButtonBorder"));
            template.Triggers.Add(hover);

            Trigger pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, CreateBrush(58, 255, 255, 255), "ButtonBorder"));
            template.Triggers.Add(pressed);

            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.48, "ButtonBorder"));
            template.Triggers.Add(disabled);

            button.Template = template;
            return button;
        }

        public static Button CreateIconButton(string glyph, string tooltip, Action click)
        {
            Button button = new Button();
            button.Content = glyph;
            button.Width = 38;
            button.Height = 36;
            button.Margin = new Thickness(2, 0, 0, 0);
            button.ToolTip = tooltip;
            button.Cursor = Cursors.Hand;
            button.Focusable = false;
            button.Foreground = CreateBrush(214, 255, 255, 255);
            button.Background = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            SetTemplateBinding(border, Border.BackgroundProperty, Control.BackgroundProperty);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;

            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, HoverBrush, "ButtonBorder"));
            template.Triggers.Add(hover);
            template.Triggers.Add(pressedTrigger());
            button.Template = template;
            button.Click += delegate
            {
                if (click != null)
                {
                    click();
                }
            };
            return button;
        }

        public static CheckBox CreateToggleCheckBox(string text)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Content = text;
            checkBox.FontSize = 13;
            checkBox.Cursor = Cursors.Hand;
            checkBox.Foreground = TextPrimaryBrush;
            checkBox.FocusVisualStyle = null;

            ControlTemplate template = new ControlTemplate(typeof(CheckBox));
            FrameworkElementFactory panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            FrameworkElementFactory track = new FrameworkElementFactory(typeof(Border));
            track.Name = "ToggleTrack";
            track.SetValue(Border.WidthProperty, 44.0);
            track.SetValue(Border.HeightProperty, 24.0);
            track.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            track.SetValue(Border.BackgroundProperty, CreateBrush(30, 255, 255, 255));
            track.SetValue(Border.BorderBrushProperty, HairlineBrush);
            track.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            FrameworkElementFactory thumb = new FrameworkElementFactory(typeof(Border));
            thumb.Name = "ToggleThumb";
            thumb.SetValue(Border.WidthProperty, 18.0);
            thumb.SetValue(Border.HeightProperty, 18.0);
            thumb.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            thumb.SetValue(Border.BackgroundProperty, TextPrimaryBrush);
            thumb.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            thumb.SetValue(Border.MarginProperty, new Thickness(3));
            track.AppendChild(thumb);

            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.MarginProperty, new Thickness(10, 0, 0, 0));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            panel.AppendChild(track);
            panel.AppendChild(content);

            template.VisualTree = panel;
            Trigger checkedTrigger = new Trigger
            {
                Property = ToggleButton.IsCheckedProperty,
                Value = true
            };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, PrimaryBrush, "ToggleTrack"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, PrimaryBorderBrush, "ToggleTrack"));
            checkedTrigger.Setters.Add(new Setter(Border.HorizontalAlignmentProperty, HorizontalAlignment.Right, "ToggleThumb"));
            template.Triggers.Add(checkedTrigger);
            checkBox.Template = template;
            return checkBox;
        }

        private static Trigger pressedTrigger()
        {
            Trigger pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, CreateBrush(30, 255, 255, 255), "ButtonBorder"));
            return pressed;
        }

        private static void SetTemplateBinding(
            FrameworkElementFactory element,
            DependencyProperty targetProperty,
            DependencyProperty sourceProperty)
        {
            element.SetBinding(
                targetProperty,
                new Binding
                {
                    Path = new PropertyPath(sourceProperty.Name),
                    RelativeSource = RelativeSource.TemplatedParent
                });
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int size);
    }

    internal enum ButtonKind
    {
        Ghost,
        Primary,
        Danger
    }
}
