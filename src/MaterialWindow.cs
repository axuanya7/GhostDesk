using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace GhostDesk
{
    /// <summary>单个分区的材质微调：预设 + 不透明度 / 底色深浅 / 描边 / 阴影，实时生效。</summary>
    internal class MaterialWindow : Window
    {
        private readonly RegionConfig _model;
        private readonly AppConfig _config;
        private readonly Action _changed;

        public MaterialWindow(RegionConfig model, AppConfig config, Action changed)
        {
            _model = model;
            _config = config;
            _changed = changed;

            Title = "分区材质";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.SingleBorderWindow;
            Background = GlassTheme.CreateBrush(255, 18, 22, 32);
            FontFamily = new FontFamily("Microsoft YaHei UI");
            Foreground = GlassTheme.TextPrimaryBrush;
            ShowInTaskbar = false;

            StackPanel root = new StackPanel();
            root.Margin = new Thickness(20, 16, 20, 18);

            TextBlock heading = GlassTheme.CreateText("分区材质", 18, false);
            heading.FontWeight = FontWeights.SemiBold;
            root.Children.Add(heading);

            TextBlock subtitle = GlassTheme.CreateText(
                "调整这个分区的玻璃质感；对高对比或大色块壁纸，把不透明度调高会更清楚。",
                12,
                true);
            subtitle.TextWrapping = TextWrapping.Wrap;
            subtitle.Margin = new Thickness(0, 6, 0, 12);
            root.Children.Add(subtitle);

            root.Children.Add(CreateSettingLabel("预设"));
            WrapPanel presets = new WrapPanel();
            presets.Margin = new Thickness(0, 0, 0, 10);
            presets.Children.Add(CreatePresetButton("跟随全局", ""));
            presets.Children.Add(CreatePresetButton("高透玻璃", AppConfig.MaterialGlass));
            presets.Children.Add(CreatePresetButton("磨砂玻璃", AppConfig.MaterialFrosted));
            presets.Children.Add(CreatePresetButton("纯色块", AppConfig.MaterialSolid));
            root.Children.Add(presets);

            root.Children.Add(CreateSliderRow(
                "背景不透明度",
                () => Resolve(_model.MaterialOpacity, _config == null ? null : _config.MaterialOpacity, AppConfig.DefaultMaterialOpacity),
                delegate(int value) { _model.MaterialOpacity = value; }));
            root.Children.Add(CreateSliderRow(
                "底色深浅（越小越亮）",
                () => Resolve(_model.MaterialTint, _config == null ? null : _config.MaterialTint, AppConfig.DefaultMaterialTint),
                delegate(int value) { _model.MaterialTint = value; }));
            root.Children.Add(CreateSliderRow(
                "描边强度",
                () => Resolve(_model.MaterialBorder, _config == null ? null : _config.MaterialBorder, AppConfig.DefaultMaterialBorder),
                delegate(int value) { _model.MaterialBorder = value; }));
            root.Children.Add(CreateSliderRow(
                "阴影强度",
                () => Resolve(_model.MaterialShadow, _config == null ? null : _config.MaterialShadow, AppConfig.DefaultMaterialShadow),
                delegate(int value) { _model.MaterialShadow = value; }));

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 8, 0, 0);

            Button reset = GlassTheme.CreateButton("全部跟随全局", ButtonKind.Ghost);
            reset.Margin = new Thickness(0, 0, 8, 0);
            reset.Click += delegate
            {
                _model.MaterialPreset = "";
                _model.MaterialOpacity = null;
                _model.MaterialTint = null;
                _model.MaterialBorder = null;
                _model.MaterialShadow = null;
                Notify();
                Close();
            };
            actions.Children.Add(reset);

            Button close = GlassTheme.CreateButton("完成", ButtonKind.Primary);
            close.Click += delegate { Close(); };
            actions.Children.Add(close);
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
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
        }

        private static int Resolve(int? regionValue, int? globalValue, int fallback)
        {
            int value = regionValue.HasValue
                ? regionValue.Value
                : (globalValue.HasValue ? globalValue.Value : fallback);
            return Math.Max(0, Math.Min(100, value));
        }

        private static TextBlock CreateSettingLabel(string text)
        {
            TextBlock label = GlassTheme.CreateText(text, 13, false);
            label.FontWeight = FontWeights.SemiBold;
            label.Margin = new Thickness(0, 6, 0, 4);
            return label;
        }

        private Button CreatePresetButton(string text, string preset)
        {
            Button button = GlassTheme.CreateButton(text, ButtonKind.Ghost);
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Click += delegate
            {
                _model.MaterialPreset = preset;
                Notify();
            };
            return button;
        }

        private UIElement CreateSliderRow(string label, Func<int> getter, Action<int> setter)
        {
            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(0, 0, 0, 6);
            panel.Children.Add(CreateSettingLabel(label));

            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });

            Slider slider = new Slider();
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.SmallChange = 1;
            slider.LargeChange = 10;
            slider.TickFrequency = 10;
            slider.Value = getter();
            slider.VerticalAlignment = VerticalAlignment.Center;
            slider.Margin = new Thickness(0, 0, 10, 0);

            TextBlock valueText = GlassTheme.CreateText(
                ((int)Math.Round(slider.Value)).ToString(),
                12,
                true);
            valueText.VerticalAlignment = VerticalAlignment.Center;
            valueText.HorizontalAlignment = HorizontalAlignment.Right;

            slider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e)
            {
                int value = (int)Math.Round(e.NewValue);
                valueText.Text = value.ToString();
                setter(value);
                Notify();
            };

            Grid.SetColumn(slider, 0);
            Grid.SetColumn(valueText, 1);
            grid.Children.Add(slider);
            grid.Children.Add(valueText);
            panel.Children.Add(grid);
            return panel;
        }

        private void Notify()
        {
            if (_changed != null)
            {
                _changed();
            }
        }
    }
}
