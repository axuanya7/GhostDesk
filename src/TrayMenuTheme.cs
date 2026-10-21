using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GhostDesk
{
    internal static class TrayMenuTheme
    {
        private const string ThemeMarker = "GhostDesk.TrayMenuTheme";

        internal static readonly Color SurfaceTop = Color.FromArgb(248, 24, 32, 45);
        internal static readonly Color SurfaceBottom = Color.FromArgb(246, 37, 48, 68);
        internal static readonly Color BorderColor = Color.FromArgb(70, 255, 255, 255);
        internal static readonly Color TextColor = Color.FromArgb(242, 247, 255);
        internal static readonly Color DisabledTextColor = Color.FromArgb(112, 128, 148);
        internal static readonly Color HoverColor = Color.FromArgb(46, 255, 255, 255);
        internal static readonly Color AccentColor = Color.FromArgb(139, 226, 255);
        internal static readonly Color SeparatorColor = Color.FromArgb(32, 255, 255, 255);

        public static void Apply(ContextMenuStrip menu)
        {
            if (menu == null)
            {
                return;
            }

            StyleDropDown(menu);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Opacity = 0.97;
            menu.Opening += delegate { StyleChildDropDowns(menu.Items); };
        }

        private static void StyleChildDropDowns(ToolStripItemCollection items)
        {
            if (items == null)
            {
                return;
            }

            foreach (ToolStripItem item in items)
            {
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem == null || menuItem.DropDown == null)
                {
                    continue;
                }

                StyleDropDown(menuItem.DropDown);
                StyleChildDropDowns(menuItem.DropDown.Items);
            }
        }

        private static void StyleDropDown(ToolStripDropDown dropDown)
        {
            if (dropDown == null || string.Equals(dropDown.Tag as string, ThemeMarker))
            {
                return;
            }

            dropDown.Tag = ThemeMarker;
            dropDown.Renderer = new GlassToolStripRenderer();
            dropDown.BackColor = SurfaceBottom;
            dropDown.ForeColor = TextColor;
            dropDown.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            dropDown.Padding = new Padding(6, 7, 6, 7);
            dropDown.DropShadowEnabled = true;
            dropDown.Opacity = 0.97;

            ToolStripDropDownMenu dropDownMenu = dropDown as ToolStripDropDownMenu;
            if (dropDownMenu != null)
            {
                dropDownMenu.ShowImageMargin = false;
                dropDownMenu.ShowCheckMargin = true;
            }

            dropDown.HandleCreated += delegate { ApplyRoundedRegion(dropDown); };
            dropDown.Resize += delegate { ApplyRoundedRegion(dropDown); };
        }

        private static void ApplyRoundedRegion(Control control)
        {
            if (control == null || control.Width <= 2 || control.Height <= 2)
            {
                return;
            }

            int radius = 10;
            using (GraphicsPath path = CreateRoundedRectangle(
                new Rectangle(0, 0, control.Width - 1, control.Height - 1),
                radius))
            {
                Region previous = control.Region;
                control.Region = new Region(path);
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        internal static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                path.CloseFigure();
                return path;
            }

            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);

            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);

            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class GlassToolStripColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground
        {
            get { return TrayMenuTheme.SurfaceTop; }
        }

        public override Color ImageMarginGradientBegin
        {
            get { return TrayMenuTheme.SurfaceTop; }
        }

        public override Color ImageMarginGradientMiddle
        {
            get { return TrayMenuTheme.SurfaceTop; }
        }

        public override Color ImageMarginGradientEnd
        {
            get { return TrayMenuTheme.SurfaceBottom; }
        }

        public override Color MenuBorder
        {
            get { return TrayMenuTheme.BorderColor; }
        }

        public override Color MenuItemBorder
        {
            get { return Color.Transparent; }
        }

        public override Color MenuItemSelected
        {
            get { return TrayMenuTheme.HoverColor; }
        }

        public override Color MenuItemSelectedGradientBegin
        {
            get { return TrayMenuTheme.HoverColor; }
        }

        public override Color MenuItemSelectedGradientEnd
        {
            get { return TrayMenuTheme.HoverColor; }
        }

        public override Color MenuItemPressedGradientBegin
        {
            get { return Color.FromArgb(36, 255, 255, 255); }
        }

        public override Color MenuItemPressedGradientEnd
        {
            get { return Color.FromArgb(24, 255, 255, 255); }
        }

        public override Color SeparatorDark
        {
            get { return TrayMenuTheme.SeparatorColor; }
        }

        public override Color SeparatorLight
        {
            get { return Color.Transparent; }
        }

        public override Color CheckBackground
        {
            get { return Color.Transparent; }
        }

        public override Color CheckSelectedBackground
        {
            get { return Color.Transparent; }
        }

        public override Color CheckPressedBackground
        {
            get { return Color.Transparent; }
        }

        public override Color ToolStripGradientBegin
        {
            get { return TrayMenuTheme.SurfaceTop; }
        }

        public override Color ToolStripGradientMiddle
        {
            get { return TrayMenuTheme.SurfaceTop; }
        }

        public override Color ToolStripGradientEnd
        {
            get { return TrayMenuTheme.SurfaceBottom; }
        }
    }

    internal sealed class GlassToolStripRenderer : ToolStripProfessionalRenderer
    {
        public GlassToolStripRenderer()
            : base(new GlassToolStripColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Rectangle bounds = e.AffectedBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (LinearGradientBrush brush = new LinearGradientBrush(
                bounds,
                TrayMenuTheme.SurfaceTop,
                TrayMenuTheme.SurfaceBottom,
                LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle bounds = new Rectangle(
                0,
                0,
                Math.Max(1, e.ToolStrip.Width - 1),
                Math.Max(1, e.ToolStrip.Height - 1));

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = TrayMenuTheme.CreateRoundedRectangle(bounds, 10))
            using (Pen pen = new Pen(TrayMenuTheme.BorderColor, 1.0f))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            Rectangle bounds = e.AffectedBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (LinearGradientBrush brush = new LinearGradientBrush(
                bounds,
                TrayMenuTheme.SurfaceTop,
                TrayMenuTheme.SurfaceBottom,
                LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item == null || !e.Item.Selected || !e.Item.Enabled)
            {
                return;
            }

            Rectangle bounds = new Rectangle(
                e.Item.ContentRectangle.Left + 1,
                e.Item.ContentRectangle.Top + 1,
                Math.Max(1, e.Item.ContentRectangle.Width - 2),
                Math.Max(1, e.Item.ContentRectangle.Height - 2));

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = TrayMenuTheme.CreateRoundedRectangle(bounds, 6))
            using (SolidBrush background = new SolidBrush(TrayMenuTheme.HoverColor))
            using (SolidBrush accent = new SolidBrush(TrayMenuTheme.AccentColor))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.FillRectangle(accent, bounds.Left + 5, bounds.Top + 8, 2, Math.Max(1, bounds.Height - 16));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item != null)
            {
                e.TextColor = e.Item.Enabled
                    ? TrayMenuTheme.TextColor
                    : TrayMenuTheme.DisabledTextColor;
            }

            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle bounds = e.ImageRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(TrayMenuTheme.AccentColor, 1.8f))
            {
                Point start = new Point(bounds.Left + 3, bounds.Top + (bounds.Height / 2));
                Point middle = new Point(bounds.Left + 7, bounds.Bottom - 4);
                Point end = new Point(bounds.Right - 2, bounds.Top + 4);
                e.Graphics.DrawLines(pen, new Point[] { start, middle, end });
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle bounds = e.Item.ContentRectangle;
            int y = bounds.Top + (bounds.Height / 2);
            using (Pen pen = new Pen(TrayMenuTheme.SeparatorColor, 1.0f))
            {
                e.Graphics.DrawLine(pen, bounds.Left + 7, y, bounds.Right - 7, y);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && e.Item.Selected
                ? TrayMenuTheme.AccentColor
                : TrayMenuTheme.TextColor;
            base.OnRenderArrow(e);
        }
    }
}
