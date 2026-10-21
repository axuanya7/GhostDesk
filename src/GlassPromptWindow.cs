using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Markup;

namespace GhostDesk
{
    internal class GlassPromptField
    {
        public string Label;
        public string Value;
        public bool IsIconPath;
    }

    internal class GlassPromptWindow : Window
    {
        private readonly List<TextBox> _inputs = new List<TextBox>();

        public GlassPromptWindow(string title, params GlassPromptField[] fields)
        {
            Title = title;
            Width = 430;
            SizeToContent = SizeToContent.Height;
            MinHeight = 160;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;

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
            titleText.Text = title;
            titleText.FontSize = 15;
            titleText.FontWeight = FontWeights.SemiBold;
            titleText.Foreground = CreateBrush(246, 255, 255, 255);
            titleText.Margin = new Thickness(0, 0, 0, 14);
            panel.Children.Add(titleText);

            for (int index = 0; index < fields.Length; index++)
            {
                GlassPromptField field = fields[index];
                TextBlock label = new TextBlock();
                label.Text = field.Label;
                label.Foreground = CreateBrush(235, 255, 255, 255);
                label.FontSize = 12;
                label.Margin = new Thickness(0, index == 0 ? 0 : 10, 0, 4);
                panel.Children.Add(label);

                TextBox input = new TextBox();
                input.Text = field.Value ?? "";
                input.MinHeight = 30;
                input.Padding = new Thickness(8, 5, 8, 5);
                input.VerticalContentAlignment = VerticalAlignment.Center;
                input.Background = CreateBrush(180, 8, 12, 18);
                input.BorderBrush = CreateBrush(100, 255, 255, 255);
                input.Foreground = CreateBrush(245, 255, 255, 255);
                input.CaretBrush = CreateBrush(245, 255, 255, 255);
                input.Style = CreateTextBoxStyle();

                if (field.IsIconPath)
                {
                    Button browse = new Button();
                    browse.Content = "选择...";
                    browse.MinWidth = 74;
                    browse.MinHeight = 30;
                    browse.Margin = new Thickness(8, 0, 0, 0);
                    browse.Style = CreateButtonStyle();
                    browse.Click += delegate
                    {
                        Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
                        dialog.Title = "选择自定义图标";
                        dialog.Filter = "图片或图标 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll|所有文件 (*.*)|*.*";
                        bool? result = Owner == null ? dialog.ShowDialog() : dialog.ShowDialog(Owner);
                        if (result == true)
                        {
                            input.Text = dialog.FileName;
                        }
                    };

                    DockPanel row = new DockPanel();
                    row.LastChildFill = true;
                    DockPanel.SetDock(browse, Dock.Right);
                    row.Children.Add(browse);
                    row.Children.Add(input);
                    panel.Children.Add(row);
                }
                else
                {
                    panel.Children.Add(input);
                }

                _inputs.Add(input);
            }

            Button cancelButton = new Button();
            cancelButton.Content = "取消";
            cancelButton.MinWidth = 76;
            cancelButton.MinHeight = 30;
            cancelButton.Margin = new Thickness(0, 16, 10, 0);
            cancelButton.IsCancel = true;
            cancelButton.Style = CreateButtonStyle();

            Button saveButton = new Button();
            saveButton.Content = "保存";
            saveButton.MinWidth = 76;
            saveButton.MinHeight = 30;
            saveButton.Margin = new Thickness(0, 16, 0, 0);
            saveButton.IsDefault = true;
            saveButton.Style = CreateButtonStyle();
            saveButton.Click += delegate { DialogResult = true; };

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Children.Add(cancelButton);
            actions.Children.Add(saveButton);
            panel.Children.Add(actions);

            Content = surface;
            Loaded += delegate
            {
                Activate();
                if (_inputs.Count > 0)
                {
                    _inputs[0].Focus();
                    _inputs[0].SelectAll();
                }
            };
        }

        public string[] GetValues()
        {
            string[] values = new string[_inputs.Count];
            for (int index = 0; index < _inputs.Count; index++)
            {
                values[index] = _inputs[index].Text;
            }

            return values;
        }

        private static SolidColorBrush CreateBrush(byte alpha, byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static Style CreateTextBoxStyle()
        {
            string xaml = "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type TextBox}'>" +
                "<Setter Property='Template'>" +
                "<Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type TextBox}'>" +
                "<Border x:Name='Border' Background='{TemplateBinding Background}' " +
                "BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='10'>" +
                "<ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' " +
                "VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'>" +
                "<Setter TargetName='Border' Property='Background' Value='#3CFFFFFF'/>" +
                "</Trigger>" +
                "<Trigger Property='IsKeyboardFocusWithin' Value='True'>" +
                "<Setter TargetName='Border' Property='BorderBrush' Value='#C8FFFFFF'/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Setter.Value>" +
                "</Setter>" +
                "</Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        private static Style CreateButtonStyle()
        {
            string xaml = "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
                "TargetType='{x:Type Button}'>" +
                "<Setter Property='Foreground' Value='#F2FFFFFF'/>" +
                "<Setter Property='Background' Value='#28000000'/>" +
                "<Setter Property='BorderBrush' Value='#8AFFFFFF'/>" +
                "<Setter Property='BorderThickness' Value='1'/>" +
                "<Setter Property='Padding' Value='10,4'/>" +
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
