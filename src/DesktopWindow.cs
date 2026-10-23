using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Shapes;

namespace GhostDesk
{
    internal class DesktopWindow : Window
    {
        private readonly AppConfig _config;
        private readonly StorageManifest _manifest;
        private readonly List<RegionControl> _regions = new List<RegionControl>();
        private Canvas _root;
        private Canvas _dragLayer;
        private Canvas _guideLayer;
        private readonly List<Line> _guideLines = new List<Line>();
        private Border _sizeBubble;
        private TextBlock _sizeBubbleText;        private DispatcherTimer _timer;
        private HwndSource _source;
        private IntPtr _hwnd = IntPtr.Zero;
        private readonly HashSet<RegionControl> _interactionRegions =
            new HashSet<RegionControl>();
        private bool _leftButtonWasDown;
        private bool _keyboardEditing;
        private DispatcherTimer _layerRestoreTimer;
        private DateTime _raiseHoldUntilUtc = DateTime.MinValue;
        private DateTime _raiseSinceUtc = DateTime.MinValue;
        private int _openContextMenuCount;
        private bool _paused;
        private bool _hoverEnabled = true;
        private uint _activateMsgId;
        private readonly Dictionary<RegionControl, DateTime> _pendingHideAtUtc =
            new Dictionary<RegionControl, DateTime>();
        private const double SnapThreshold = 20.0;
        private const double DockThreshold = 2.0;
        private const double ConnectedTolerance = 8.0;
        private const double ConnectedGap = 4.0;

        /// <summary>同一侧分区之间的统一间距（成组吸附与贴边排布都用它）。</summary>
        private const double RegionGap = 8.0;
        private const double FastTimerMilliseconds = 16.0;
        private const double IdleTimerMilliseconds = 200.0;
        private const double ProcessNameCacheTtlSeconds = 0.5;
        private readonly List<RegionControl> _movingGroup = new List<RegionControl>();
        private RegionControl _primaryMovingRegion;
        private Point _lastGroupPrimaryPosition;
        private Point _groupMoveOriginPrimaryPosition;
        private DateTime _lastDebugLogUtc = DateTime.MinValue;
        private readonly Dictionary<uint, ProcessNameCacheEntry> _processNameCache =
            new Dictionary<uint, ProcessNameCacheEntry>();
        public event Action ConfigChanged;
        public event Action<string> LaunchRequested;

        private sealed class ProcessNameCacheEntry
        {
            public string Name;
            public DateTime CheckedAtUtc;
        }

        public bool HoverEnabled
        {
            get { return _hoverEnabled; }
            set { _hoverEnabled = value; }
        }

        public bool Paused
        {
            get { return _paused; }
            set
            {
                _paused = value;
                _pendingHideAtUtc.Clear();
                SetAllRegionsHidden(_paused ? false : true, _config.FadeSeconds);
            }
        }

        public DesktopWindow(AppConfig config)
            : this(config, null)
        {
        }

        public DesktopWindow(
            AppConfig config,
            StorageManifest manifest)
        {
            _config = config;
            _manifest = manifest;
            InitializeWindow();
            BuildRegions();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(FastTimerMilliseconds);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            SetAllRegionsHidden(true, 0.0);

            // 只有在分区上按下左键时才把覆盖层抬到其它窗口之上
            PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left)
                {
                    return;
                }

                RaiseOverlayAboveWindows();
                _raiseHoldUntilUtc = DateTime.UtcNow.AddSeconds(1.2);
                ScheduleOverlayBottom();
            };

            GlassTheme.ContextMenuVisibilityChanged = HandleContextMenuVisibility;
        }

        public void AddRegion(RegionConfig model)
        {
            NormalizeRegionDock(model);
            if (!IsDocked(model))
            {
                ClampModel(model);
            }
            RegionControl control = new RegionControl(
                model,
                RemoveRegion,
                PersistRegionConfigChange,
                OpenItem,
                _dragLayer,
                _config.CollapseHoverTrigger,
                delegate { return _config.Regions; },
                AddItemToLaunchRegion,
                SynchronizeItemVisibility,
                _manifest);
            control.SetMoveCallbacks(BeginRegionMove, UpdateRegionMove, EndRegionMove);
            control.SetLayoutChangedCallback(RegionLayoutChanged);
            control.SetDockAnchorChangedCallback(DockAnchorChanged);
            control.SetResizeSnapping(ApplyResizeGuides, delegate { ClearGuides(); });
            control.SetMaterialEditor(OpenMaterialEditor);
            control.ApplyMaterial(_config);
            control.InteractionChanged += RegionInteractionChanged;
            _regions.Add(control);
            _root.Children.Add(control);
        }

        private bool SynchronizeItemVisibility(DesktopItem item)
        {
            return ItemStorage.SynchronizeItemVisibility(
                _config,
                item,
                _config.ShowHiddenFiles);
        }

        public void AddItemToLaunchRegion(DesktopItem item, string regionId)
        {
            if (item == null || string.IsNullOrEmpty(regionId))
            {
                return;
            }

            foreach (RegionControl region in _regions)
            {
                if (region != null &&
                    string.Equals(region.Model.Id, regionId, StringComparison.Ordinal))
                {
                    region.AddItemToLaunch(item);
                    return;
                }
            }
        }

        public void RefreshRegionItems()
        {
            foreach (RegionControl region in _regions)
            {
                region.RefreshItems();
            }
        }

        public void RefreshRegionLaunchButtons()
        {
            foreach (RegionControl region in _regions)
            {
                region.RefreshLaunchButton();
            }
        }

        public void RequestLaunch(string regionId)
        {
            Action<string> handler = LaunchRequested;
            if (handler != null)
            {
                handler(regionId);
            }
        }

        public void ApplyCollapseHoverTrigger()
        {
            foreach (RegionControl region in _regions)
            {
                region.SetCollapseHoverTrigger(_config.CollapseHoverTrigger);
            }
        }

        /// <summary>贴边灵敏度或安全边距变化后，按新设置重新摆放所有分区。</summary>
        public void ApplyDockSettings()
        {
            ApplyDockLayout(RegionConfig.DockTop);
            ApplyDockLayout(RegionConfig.DockBottom);
            ApplyDockLayout(RegionConfig.DockLeft);
            ApplyDockLayout(RegionConfig.DockRight);

            foreach (RegionControl region in _regions)
            {
                if (region != null && !IsDocked(region))
                {
                    ClampRegionIntoWorkspace(region);
                }
            }
        }

        private void BeginRegionMove(RegionControl region)
        {
            _movingGroup.Clear();
            _primaryMovingRegion = region;
            if (IsSnapEnabled())
            {
                BuildConnectedMoveGroup(region, _movingGroup);
            }
            else
            {
                _movingGroup.Add(region);
            }

            _lastGroupPrimaryPosition = GetRegionPosition(region);
            _groupMoveOriginPrimaryPosition = _lastGroupPrimaryPosition;
        }

        private void UpdateRegionMove(RegionControl region, double desiredX, double desiredY)
        {
            if (region == null || !ReferenceEquals(region, _primaryMovingRegion))
            {
                return;
            }

            if (!IsSnapEnabled())
            {
                double guidedX = desiredX;
                double guidedY = desiredY;
                ApplyMoveGuides(region, ref guidedX, ref guidedY);
                region.ApplyPosition(guidedX, guidedY);
                ClampMovingGroupToWorkArea();
                return;
            }

            if (_movingGroup.Count == 0 || !_movingGroup.Contains(region))
            {
                _movingGroup.Clear();
                _movingGroup.Add(region);
            }

            Point desired = new Point(desiredX, desiredY);
            Point snapped;
            RegionControl attach;
            SnapPrimaryToGroup(region, desired, out snapped, out attach);

            double snapGuideX = snapped.X;
            double snapGuideY = snapped.Y;
            ApplyMoveGuides(region, ref snapGuideX, ref snapGuideY);
            snapped = new Point(snapGuideX, snapGuideY);

            double deltaX = snapped.X - _lastGroupPrimaryPosition.X;
            double deltaY = snapped.Y - _lastGroupPrimaryPosition.Y;
            MoveGroupBy(deltaX, deltaY);

            if (attach != null && !_movingGroup.Contains(attach))
            {
                _movingGroup.Add(attach);
            }

            _lastGroupPrimaryPosition = GetRegionPosition(region);
        }

        private void EndRegionMove(RegionControl region)
        {
            if (_primaryMovingRegion != null && ReferenceEquals(_primaryMovingRegion, region))
            {
                ApplyMovingGroupPlacement(region);
                _primaryMovingRegion = null;
                _movingGroup.Clear();
                PersistConfigChange();
                ClearGuides();
                ScheduleOverlayBottom();
            }
        }

        private void ApplyMovingGroupPlacement(RegionControl region)
        {
            if (region == null || _movingGroup.Count <= 1)
            {
                ApplyDockPlacement(region);
                return;
            }

            string targetEdge = ResolvePlacementEdge(region, true);
            HashSet<string> previousEdges = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegionControl movingRegion in _movingGroup)
            {
                string previousEdge = NormalizeDockEdge(movingRegion.Model.DockEdge);
                if (!string.IsNullOrEmpty(previousEdge))
                {
                    previousEdges.Add(previousEdge);
                }
            }

            bool keepRowAtSide = false;
            if (string.IsNullOrEmpty(targetEdge) ||
                !TryPrepareMovingGroupForDock(targetEdge, out keepRowAtSide))
            {
                ClearMovingGroupDockState();
                ClampMovingGroupToWorkArea();
                return;
            }

            if (keepRowAtSide)
            {
                ClearMovingGroupDockState();
                foreach (string previousEdge in previousEdges)
                {
                    ApplyDockLayout(previousEdge);
                }
                AlignMovingGroupRowToDockEdge(targetEdge);
                return;
            }

            foreach (RegionControl movingRegion in _movingGroup)
            {
                movingRegion.Model.DockEdge = targetEdge;
            }

            AssignDockOrdersByCurrentPosition(targetEdge);

            foreach (RegionControl movingRegion in _movingGroup)
            {
                movingRegion.RefreshDockLayout();
            }

            foreach (string previousEdge in previousEdges)
            {
                if (!string.Equals(previousEdge, targetEdge, StringComparison.Ordinal))
                {
                    ApplyDockLayout(previousEdge);
                }
            }

            ApplyDockLayout(targetEdge);
        }

        private bool TryPrepareMovingGroupForDock(
            string edge,
            out bool keepRowAtSide)
        {
            keepRowAtSide = false;
            if (_movingGroup.Count <= 1)
            {
                return true;
            }

            bool horizontalEdge =
                string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal) ||
                string.Equals(edge, RegionConfig.DockBottom, StringComparison.Ordinal);
            bool verticalEdge =
                string.Equals(edge, RegionConfig.DockLeft, StringComparison.Ordinal) ||
                string.Equals(edge, RegionConfig.DockRight, StringComparison.Ordinal);
            if (!horizontalEdge && !verticalEdge)
            {
                return false;
            }

            bool row = IsMovingGroupAligned(true);
            bool column = IsMovingGroupAligned(false);
            if (horizontalEdge)
            {
                if (row)
                {
                    return true;
                }

                if (!column)
                {
                    return false;
                }

                ConvertMovingGroupColumnToRow();
                return true;
            }

            if (column)
            {
                return true;
            }

            if (!row)
            {
                return false;
            }

            List<RegionControl> ordered = GetOrderedMovingGroup(false);
            double gap = GetMovingGroupGap(ordered, false);
            double totalHeight = 0.0;
            foreach (RegionControl region in ordered)
            {
                double width;
                double height;
                GetRegionSize(region, out width, out height);
                totalHeight += height;
            }
            totalHeight += gap * Math.Max(0, ordered.Count - 1);
            if (totalHeight > GetWorkspaceHeight())
            {
                keepRowAtSide = true;
                return true;
            }

            ConvertMovingGroupRowToColumn();
            return true;
        }

        private void AlignMovingGroupRowToDockEdge(string edge)
        {
            Rect bounds = GetMovingGroupBounds();
            double deltaX = 0.0;
            if (string.Equals(edge, RegionConfig.DockLeft, StringComparison.Ordinal))
            {
                deltaX = -bounds.Left;
            }
            else if (string.Equals(edge, RegionConfig.DockRight, StringComparison.Ordinal))
            {
                deltaX = GetWorkspaceWidth() - bounds.Right;
            }

            MoveGroupBy(deltaX, 0.0);
        }

        private bool IsMovingGroupAligned(bool horizontal)
        {
            bool hasReference = false;
            double reference = 0.0;
            foreach (RegionControl region in _movingGroup)
            {
                Rect rect = GetRegionRect(region);
                double value = horizontal ? rect.Top : rect.Left;
                if (!hasReference)
                {
                    reference = value;
                    hasReference = true;
                    continue;
                }

                if (Math.Abs(value - reference) > ConnectedTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private void ConvertMovingGroupColumnToRow()
        {
            List<RegionControl> ordered = GetOrderedMovingGroup(true);
            double gap = GetMovingGroupGap(ordered, true);
            Rect bounds = GetMovingGroupBounds();
            double totalWidth = 0.0;
            for (int index = 0; index < ordered.Count; index++)
            {
                double width;
                double height;
                GetRegionSize(ordered[index], out width, out height);
                totalWidth += width;
            }
            totalWidth += gap * Math.Max(0, ordered.Count - 1);

            double availableWidth = GetWorkspaceWidth();
            double startX = bounds.Left + ((bounds.Width - totalWidth) * 0.5);
            startX = Math.Max(0.0, Math.Min(
                Math.Max(0.0, availableWidth - totalWidth),
                startX));
            double startY = bounds.Top;

            double x = startX;
            foreach (RegionControl region in ordered)
            {
                double width;
                double height;
                GetRegionSize(region, out width, out height);
                region.ApplyPosition(x, startY);
                x += width + gap;
            }
        }

        private void ConvertMovingGroupRowToColumn()
        {
            List<RegionControl> ordered = GetOrderedMovingGroup(false);
            double gap = GetMovingGroupGap(ordered, false);
            Rect bounds = GetMovingGroupBounds();
            double totalHeight = 0.0;
            for (int index = 0; index < ordered.Count; index++)
            {
                double width;
                double height;
                GetRegionSize(ordered[index], out width, out height);
                totalHeight += height;
            }
            totalHeight += gap * Math.Max(0, ordered.Count - 1);

            double availableHeight = GetWorkspaceHeight();
            double startY = bounds.Top + ((bounds.Height - totalHeight) * 0.5);
            startY = Math.Max(0.0, Math.Min(
                Math.Max(0.0, availableHeight - totalHeight),
                startY));
            double startX = bounds.Left;

            double y = startY;
            foreach (RegionControl region in ordered)
            {
                double width;
                double height;
                GetRegionSize(region, out width, out height);
                region.ApplyPosition(startX, y);
                y += height + gap;
            }
        }

        private List<RegionControl> GetOrderedMovingGroup(bool byTop)
        {
            List<RegionControl> ordered = new List<RegionControl>(_movingGroup);
            ordered.Sort(delegate(RegionControl first, RegionControl second)
            {
                Rect firstRect = GetRegionRect(first);
                Rect secondRect = GetRegionRect(second);
                double firstValue = byTop ? firstRect.Top : firstRect.Left;
                double secondValue = byTop ? secondRect.Top : secondRect.Left;
                int position = firstValue.CompareTo(secondValue);
                if (position != 0)
                {
                    return position;
                }

                return _regions.IndexOf(first).CompareTo(_regions.IndexOf(second));
            });
            return ordered;
        }

        private double GetMovingGroupGap(
            List<RegionControl> ordered,
            bool vertical)
        {
            if (ordered.Count <= 1)
            {
                return ConnectedGap;
            }

            double total = 0.0;
            int count = 0;
            for (int index = 1; index < ordered.Count; index++)
            {
                Rect previous = GetRegionRect(ordered[index - 1]);
                Rect current = GetRegionRect(ordered[index]);
                double gap = vertical
                    ? current.Top - previous.Bottom
                    : current.Left - previous.Right;
                if (gap >= 0.0)
                {
                    total += gap;
                    count++;
                }
            }

            if (count == 0)
            {
                return ConnectedGap;
            }

            return Math.Max(0.0, Math.Min(ConnectedTolerance, total / count));
        }

        private Rect GetMovingGroupBounds()
        {
            if (_movingGroup.Count == 0)
            {
                return Rect.Empty;
            }

            Rect bounds = GetRegionRect(_movingGroup[0]);
            for (int index = 1; index < _movingGroup.Count; index++)
            {
                bounds.Union(GetRegionRect(_movingGroup[index]));
            }

            return bounds;
        }

        private void ClearMovingGroupDockState()
        {
            foreach (RegionControl movingRegion in _movingGroup)
            {
                movingRegion.Model.DockEdge = RegionConfig.DockNone;
                movingRegion.Model.DockOrder = 0;
            }

            foreach (RegionControl movingRegion in _movingGroup)
            {
                movingRegion.RefreshDockLayout();
            }
        }

        private void AssignDockOrdersByCurrentPosition(string edge)
        {
            List<RegionControl> ordered = GetDockedRegions(edge);
            if (ordered.Count == 0)
            {
                return;
            }

            bool horizontal =
                string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal) ||
                string.Equals(edge, RegionConfig.DockBottom, StringComparison.Ordinal);
            ordered.Sort(delegate(RegionControl first, RegionControl second)
            {
                Rect firstRect = GetRegionRect(first);
                Rect secondRect = GetRegionRect(second);
                double firstCenter = horizontal
                    ? firstRect.Left + (firstRect.Width * 0.5)
                    : firstRect.Top + (firstRect.Height * 0.5);
                double secondCenter = horizontal
                    ? secondRect.Left + (secondRect.Width * 0.5)
                    : secondRect.Top + (secondRect.Height * 0.5);
                int position = firstCenter.CompareTo(secondCenter);
                if (position != 0)
                {
                    return position;
                }

                return _regions.IndexOf(first).CompareTo(_regions.IndexOf(second));
            });

            for (int index = 0; index < ordered.Count; index++)
            {
                ordered[index].Model.DockOrder = index + 1;
            }
        }

        private void RegionLayoutChanged(RegionControl region)
        {
            if (region == null)
            {
                return;
            }

            NormalizeRegionDock(region.Model);
            if (IsDocked(region))
            {
                ApplyDockLayout(region.Model.DockEdge);
            }
        }

        /// <summary>打开单个分区的材质微调窗口，改动实时生效。</summary>
        private void OpenMaterialEditor(RegionConfig model)
        {
            if (model == null)
            {
                return;
            }

            RegionControl target = null;
            foreach (RegionControl region in _regions)
            {
                if (region != null && ReferenceEquals(region.Model, model))
                {
                    target = region;
                    break;
                }
            }

            MaterialWindow window = new MaterialWindow(
                model,
                _config,
                delegate
                {
                    if (target != null)
                    {
                        target.ApplyMaterial(_config);
                    }

                    PersistConfigChange();
                });
            window.Topmost = true;
            window.ShowDialog();
        }

        /// <summary>全局材质设置变化后，把所有分区重新刷一遍。</summary>
        public void ApplyMaterialSettings()
        {
            foreach (RegionControl region in _regions)
            {
                if (region != null)
                {
                    region.ApplyMaterial(_config);
                }
            }
        }
        /// <summary>分区贴边朝向被手动修改后，立即按新朝向贴边并落盘。</summary>
        private void DockAnchorChanged(RegionControl region)
        {
            if (region == null)
            {
                return;
            }

            ApplyDockPlacement(region);
            PersistConfigChange();
        }

        private void ApplyDockPlacement(RegionControl region)
        {
            if (region == null)
            {
                return;
            }

            NormalizeRegionDock(region.Model);
            string previousEdge = region.Model.DockEdge;
            string targetEdge = ResolvePlacementEdge(region, false);
            Point releasePosition = GetRegionPosition(region);

            if (!string.IsNullOrEmpty(targetEdge))
            {
                region.Model.DockEdge = targetEdge;
                AssignDockOrderAtPosition(region, targetEdge);

                region.RefreshDockLayout();
                if (!string.Equals(previousEdge, targetEdge, StringComparison.Ordinal))
                {
                    ApplyDockLayout(previousEdge);
                }

                double anchorPosition =
                    string.Equals(targetEdge, RegionConfig.DockTop, StringComparison.Ordinal) ||
                    string.Equals(targetEdge, RegionConfig.DockBottom, StringComparison.Ordinal)
                        ? releasePosition.X
                        : releasePosition.Y;
                ApplyDockLayout(targetEdge, region, anchorPosition);
                return;
            }

            if (!string.IsNullOrEmpty(previousEdge))
            {
                region.Model.DockEdge = RegionConfig.DockNone;
                region.Model.DockOrder = 0;
                region.RefreshDockLayout();
                ApplyDockLayout(previousEdge);
                ClampRegionIntoWorkspace(region);
            }
        }

        private string FindDockEdge(RegionControl region)
        {
            Rect rect = GetRegionRect(region);
            double band = GetDockBand();
            Rect usable = GetUsableRect();

            double leftDistance = Math.Max(0.0, rect.Left - usable.Left);
            double topDistance = Math.Max(0.0, rect.Top - usable.Top);
            double rightDistance = Math.Max(0.0, usable.Right - rect.Right);
            double bottomDistance = Math.Max(0.0, usable.Bottom - rect.Bottom);

            Point current = GetRegionPosition(region);
            double deltaX = current.X - _groupMoveOriginPrimaryPosition.X;
            double deltaY = current.Y - _groupMoveOriginPrimaryPosition.Y;

            return PickDockEdge(
                leftDistance,
                topDistance,
                rightDistance,
                bottomDistance,
                band,
                deltaX,
                deltaY);
        }

        /// <summary>
        /// 从四个边的距离里挑一个：「进入边缘带」才贴边；距离并列时按拖动方向裁决，
        /// 避免在左下/右下角被固定判定成竖排。
        /// </summary>
        private static string PickDockEdge(
            double leftDistance,
            double topDistance,
            double rightDistance,
            double bottomDistance,
            double band,
            double deltaX,
            double deltaY)
        {
            double[] distances =
            {
                leftDistance,
                topDistance,
                rightDistance,
                bottomDistance
            };
            string[] edges =
            {
                RegionConfig.DockLeft,
                RegionConfig.DockTop,
                RegionConfig.DockRight,
                RegionConfig.DockBottom
            };

            double nearest = double.MaxValue;
            for (int i = 0; i < distances.Length; i++)
            {
                if (distances[i] <= band && distances[i] < nearest)
                {
                    nearest = distances[i];
                }
            }

            if (nearest == double.MaxValue)
            {
                return RegionConfig.DockNone;
            }

            bool preferHorizontal =
                Math.Abs(deltaY) > Math.Abs(deltaX) + 4.0;
            bool preferVertical =
                Math.Abs(deltaX) > Math.Abs(deltaY) + 4.0;

            string fallback = RegionConfig.DockNone;
            for (int i = 0; i < distances.Length; i++)
            {
                if (distances[i] > nearest + 1.0)
                {
                    continue;
                }

                bool horizontalEdge = i == 1 || i == 3;
                if (preferHorizontal && horizontalEdge)
                {
                    return edges[i];
                }
                if (preferVertical && !horizontalEdge)
                {
                    return edges[i];
                }
                if (string.IsNullOrEmpty(fallback))
                {
                    fallback = edges[i];
                }
            }

            return fallback;
        }

        /// <summary>按分区朝向锁定、总开关和 Alt 修饰键决定本次贴边的目标边。</summary>
        private string ResolvePlacementEdge(RegionControl region, bool movingGroup)
        {
            // 标题朝向不影响位置；按住 Alt 时才临时不贴边
            if (IsAltPressed())
            {
                return RegionConfig.DockNone;
            }

            return movingGroup
                ? FindDockEdgeForMovingGroup(region)
                : FindDockEdge(region);
        }

        private string FindDockEdgeForMovingGroup(RegionControl region)
        {
            if (region == null || _movingGroup.Count <= 1)
            {
                return FindDockEdge(region);
            }

            Rect rect = GetMovingGroupBounds();
            Rect usable = GetUsableRect();
            double band = GetDockBand();
            double leftDistance = Math.Max(0.0, rect.Left - usable.Left);
            double topDistance = Math.Max(0.0, rect.Top - usable.Top);
            double rightDistance = Math.Max(0.0, usable.Right - rect.Right);
            double bottomDistance = Math.Max(0.0, usable.Bottom - rect.Bottom);

            Point current = GetRegionPosition(region);
            double deltaX = current.X - _groupMoveOriginPrimaryPosition.X;
            double deltaY = current.Y - _groupMoveOriginPrimaryPosition.Y;
            bool row = IsMovingGroupAligned(true);
            if (!row && Math.Abs(deltaY) > Math.Abs(deltaX) + 4.0)
            {
                if (topDistance <= band &&
                    topDistance <= bottomDistance)
                {
                    return RegionConfig.DockTop;
                }
                if (bottomDistance <= band)
                {
                    return RegionConfig.DockBottom;
                }
            }
            else if (Math.Abs(deltaX) > Math.Abs(deltaY) + 4.0)
            {
                if (leftDistance <= band &&
                    leftDistance <= rightDistance)
                {
                    return RegionConfig.DockLeft;
                }
                if (rightDistance <= band)
                {
                    return RegionConfig.DockRight;
                }
            }

            return PickDockEdge(
                leftDistance,
                topDistance,
                rightDistance,
                bottomDistance,
                band,
                deltaX,
                deltaY);
        }

        private void AssignDockOrderAtPosition(RegionControl target, string edge)
        {
            if (target == null)
            {
                return;
            }

            List<RegionControl> ordered = new List<RegionControl>();
            foreach (RegionControl region in _regions)
            {
                if (region == null ||
                    ReferenceEquals(region, target) ||
                    !string.Equals(region.Model.DockEdge, edge, StringComparison.Ordinal))
                {
                    continue;
                }

                ordered.Add(region);
            }

            Rect targetRect = GetRegionRect(target);
            bool horizontal =
                string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal) ||
                string.Equals(edge, RegionConfig.DockBottom, StringComparison.Ordinal);
            double targetCenter = horizontal
                ? targetRect.Left + (targetRect.Width * 0.5)
                : targetRect.Top + (targetRect.Height * 0.5);

            ordered.Sort(delegate(RegionControl first, RegionControl second)
            {
                Rect firstRect = GetRegionRect(first);
                Rect secondRect = GetRegionRect(second);
                double firstCenter = horizontal
                    ? firstRect.Left + (firstRect.Width * 0.5)
                    : firstRect.Top + (firstRect.Height * 0.5);
                double secondCenter = horizontal
                    ? secondRect.Left + (secondRect.Width * 0.5)
                    : secondRect.Top + (secondRect.Height * 0.5);
                int position = firstCenter.CompareTo(secondCenter);
                if (position != 0)
                {
                    return position;
                }

                return first.Model.DockOrder.CompareTo(second.Model.DockOrder);
            });

            int insertAt = ordered.Count;
            for (int index = 0; index < ordered.Count; index++)
            {
                Rect rect = GetRegionRect(ordered[index]);
                double center = horizontal
                    ? rect.Left + (rect.Width * 0.5)
                    : rect.Top + (rect.Height * 0.5);
                if (targetCenter < center)
                {
                    insertAt = index;
                    break;
                }
            }

            ordered.Insert(insertAt, target);
            for (int index = 0; index < ordered.Count; index++)
            {
                ordered[index].Model.DockOrder = index + 1;
            }
        }

        private void ApplyAllDockLayouts()
        {
            ApplyDockLayout(RegionConfig.DockTop);
            ApplyDockLayout(RegionConfig.DockBottom);
            ApplyDockLayout(RegionConfig.DockLeft);
            ApplyDockLayout(RegionConfig.DockRight);
        }

        private void ApplyDockLayout(string edge)
        {
            ApplyDockLayout(edge, null, 0.0);
        }

        private void ApplyDockLayout(
            string edge,
            RegionControl anchor,
            double anchorPosition)
        {
            edge = NormalizeDockEdge(edge);
            if (string.IsNullOrEmpty(edge))
            {
                return;
            }

            List<RegionControl> docked = GetDockedRegions(edge);

            if (docked.Count == 0)
            {
                return;
            }

            Rect usable = GetUsableRect();
            double availableWidth = usable.Width;
            double availableHeight = usable.Height;
            bool horizontal =
                string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal) ||
                string.Equals(edge, RegionConfig.DockBottom, StringComparison.Ordinal);

            double availableAlong = horizontal ? availableWidth : availableHeight;

            List<RegionControl> ordered = new List<RegionControl>(docked);
            Dictionary<RegionControl, double> starts =
                new Dictionary<RegionControl, double>();

            // 只做「贴边 + 夹进工作区」：同一个边上放多个分区时允许重叠，
            // 不会再自动把别的分区推开（旧实现的避让逻辑会让其它分区莫名移位）。
            foreach (RegionControl region in ordered)
            {
                double width;
                double height;
                GetRegionSize(region, out width, out height);
                double size = horizontal ? width : height;

                double along = anchor != null && ReferenceEquals(region, anchor)
                    ? anchorPosition
                    : (horizontal
                        ? GetRegionPosition(region).X - usable.Left
                        : GetRegionPosition(region).Y - usable.Top);

                starts[region] = Math.Max(
                    0.0,
                    Math.Min(Math.Max(0.0, availableAlong - size), along));
            }
            foreach (RegionControl region in ordered)
            {
                double width;
                double height;
                GetRegionSize(region, out width, out height);
                double along = starts[region];

                double x;
                double y;
                if (horizontal)
                {
                    x = usable.Left + along;
                    y = string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal)
                        ? usable.Top
                        : usable.Top + availableHeight - height;
                }
                else
                {
                    y = usable.Top + along;
                    x = string.Equals(edge, RegionConfig.DockLeft, StringComparison.Ordinal)
                        ? usable.Left
                        : usable.Left + availableWidth - width;
                }

                region.ApplyPosition(Math.Max(0.0, x), Math.Max(0.0, y));
            }
        }

        private List<RegionControl> GetDockedRegions(string edge)
        {
            List<RegionControl> docked = new List<RegionControl>();
            foreach (RegionControl region in _regions)
            {
                if (region != null &&
                    string.Equals(region.Model.DockEdge, edge, StringComparison.Ordinal))
                {
                    docked.Add(region);
                }
            }

            return docked;
        }

        private static void GetRegionSize(
            RegionControl region,
            out double width,
            out double height)
        {
            width = region.Width;
            height = region.Height;
            if (double.IsNaN(width) || width <= 0.0)
            {
                width = region.ActualWidth;
            }
            if (double.IsNaN(height) || height <= 0.0)
            {
                height = region.ActualHeight;
            }
            if (double.IsNaN(width) || width <= 0.0)
            {
                width = region.Model.Width;
            }
            if (double.IsNaN(height) || height <= 0.0)
            {
                height = region.Model.Height;
            }
        }

        private void ClampRegionIntoWorkspace(RegionControl region)
        {
            if (region == null)
            {
                return;
            }

            Point position = GetRegionPosition(region);
            double width;
            double height;
            GetRegionSize(region, out width, out height);
            Rect usable = GetUsableRect();
            double maximumX = Math.Max(usable.Left, usable.Right - width);
            double maximumY = Math.Max(usable.Top, usable.Bottom - height);
            double x = Math.Min(maximumX, Math.Max(usable.Left, position.X));
            double y = Math.Min(maximumY, Math.Max(usable.Top, position.Y));
            region.ApplyPosition(x, y);
        }

        private double GetWorkspaceWidth()
        {
            if (!double.IsNaN(Width) && Width > 0.0)
            {
                return Width;
            }
            if (_root != null && _root.ActualWidth > 0.0)
            {
                return _root.ActualWidth;
            }
            return SystemParameters.WorkArea.Width;
        }

        private double GetWorkspaceHeight()
        {
            if (!double.IsNaN(Height) && Height > 0.0)
            {
                return Height;
            }
            if (_root != null && _root.ActualHeight > 0.0)
            {
                return _root.ActualHeight;
            }
            return SystemParameters.WorkArea.Height;
        }

        /// <summary>
        /// 可放置分区的区域：整屏减去任务栏等系统占位，再减去用户设置的安全边距。
        /// 这样贴边后的分区不会盖住任务栏或顶部的桌面挂件（如 Rainmeter 时钟条）。
        /// </summary>
        private Rect GetUsableRect()
        {
            double screenWidth = GetWorkspaceWidth();
            double screenHeight = GetWorkspaceHeight();
            double left = 0.0;
            double top = 0.0;
            double right = 0.0;
            double bottom = 0.0;

            try
            {
                Rect work = SystemParameters.WorkArea;
                double originX = Math.Max(0.0, SystemParameters.VirtualScreenLeft);
                double originY = Math.Max(0.0, SystemParameters.VirtualScreenTop);
                left = Math.Max(0.0, work.Left - originX);
                top = Math.Max(0.0, work.Top - originY);
                right = Math.Max(0.0, screenWidth - (work.Right - originX));
                bottom = Math.Max(0.0, screenHeight - (work.Bottom - originY));
            }
            catch
            {
                left = 0.0;
                top = 0.0;
                right = 0.0;
                bottom = 0.0;
            }

            if (_config != null)
            {
                left += _config.GetSafeInset(RegionConfig.DockLeft);
                top += _config.GetSafeInset(RegionConfig.DockTop);
                right += _config.GetSafeInset(RegionConfig.DockRight);
                bottom += _config.GetSafeInset(RegionConfig.DockBottom);
            }

            double width = Math.Max(80.0, screenWidth - left - right);
            double height = Math.Max(80.0, screenHeight - top - bottom);
            return new Rect(left, top, width, height);
        }

        /// <summary>贴边判定使用的边缘带宽度（像素）。</summary>
        private double GetDockBand()
        {
            return _config != null ? _config.GetEdgeDockBand() : DockThreshold;
        }


        /// <summary>拖动时按住 Alt 临时禁用贴边，方便精确摆放。</summary>
        private static bool IsAltPressed()
        {
            return (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        }


        private static void NormalizeRegionDock(RegionConfig model)
        {
            if (model == null)
            {
                return;
            }

            model.DockEdge = NormalizeDockEdge(model.DockEdge);
            if (string.IsNullOrEmpty(model.DockEdge))
            {
                model.DockOrder = 0;
            }
            else if (model.DockOrder < 1)
            {
                model.DockOrder = 1;
            }
        }

        private static string NormalizeDockEdge(string edge)
        {
            if (string.IsNullOrEmpty(edge))
            {
                return RegionConfig.DockNone;
            }

            string value = edge.Trim().ToLowerInvariant();
            if (value == RegionConfig.DockTop ||
                value == RegionConfig.DockBottom ||
                value == RegionConfig.DockLeft ||
                value == RegionConfig.DockRight)
            {
                return value;
            }

            return RegionConfig.DockNone;
        }

        private static bool IsDocked(RegionControl region)
        {
            return region != null && !string.IsNullOrEmpty(region.Model.DockEdge);
        }

        private static bool IsDocked(RegionConfig model)
        {
            return model != null && !string.IsNullOrEmpty(model.DockEdge);
        }

        private bool IsSnapEnabled()
        {
            return _config.EnableSnap == true;
        }

        private static Point GetRegionPosition(RegionControl region)
        {
            return new Point(Canvas.GetLeft(region), Canvas.GetTop(region));
        }

        private static Rect GetRegionRect(RegionControl region)
        {
            Point position = GetRegionPosition(region);
            double width;
            double height;
            GetRegionSize(region, out width, out height);
            return new Rect(
                position.X,
                position.Y,
                width,
                height);
        }

        private void BuildConnectedMoveGroup(RegionControl start, List<RegionControl> group)
        {
            group.Clear();
            if (start == null)
            {
                return;
            }

            Queue<RegionControl> queue = new Queue<RegionControl>();
            HashSet<RegionControl> visited = new HashSet<RegionControl>();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                RegionControl current = queue.Dequeue();
                group.Add(current);

                foreach (RegionControl other in _regions)
                {
                    if (other == null || ReferenceEquals(other, current) || visited.Contains(other))
                    {
                        continue;
                    }

                    if (AreRegionsConnected(current, other))
                    {
                        visited.Add(other);
                        queue.Enqueue(other);
                    }
                }
            }
        }

        private static bool AreRegionsConnected(RegionControl first, RegionControl second)
        {
            Rect a = GetRegionRect(first);
            Rect b = GetRegionRect(second);

            bool horizontal = (Math.Abs(a.Right - b.Left) <= ConnectedTolerance ||
                Math.Abs(b.Right - a.Left) <= ConnectedTolerance) &&
                Math.Abs(a.Top - b.Top) <= ConnectedTolerance;

            bool vertical = (Math.Abs(a.Bottom - b.Top) <= ConnectedTolerance ||
                Math.Abs(b.Bottom - a.Top) <= ConnectedTolerance) &&
                Math.Abs(a.Left - b.Left) <= ConnectedTolerance;

            return horizontal || vertical;
        }

        private void SnapPrimaryToGroup(
            RegionControl primary,
            Point desired,
            out Point snapped,
            out RegionControl attach)
        {
            snapped = desired;
            attach = null;

            double primaryLeft = desired.X;
            double primaryTop = desired.Y;
            double primaryRight = desired.X + primary.Model.Width;
            double primaryBottom = desired.Y + primary.Model.Height;
            double bestDistance = double.MaxValue;
            double bestX = desired.X;
            double bestY = desired.Y;
            RegionControl bestAttach = null;

            foreach (RegionControl other in _regions)
            {
                if (other == null ||
                    ReferenceEquals(other, primary) ||
                    _movingGroup.Contains(other))
                {
                    continue;
                }

                Rect rect = GetRegionRect(other);
                bool sameRow = Math.Abs(primaryTop - rect.Top) <= SnapThreshold;
                bool sameColumn = Math.Abs(primaryLeft - rect.Left) <= SnapThreshold;

                // 只吸附到「并排」的位置，且始终留出统一间距 RegionGap：
                // 贴近相邻分区时不会再压到对方身上（旧实现会把两块吸成重叠）。
                if (sameRow &&
                    Math.Abs(primaryRight + RegionGap - rect.Left) <= SnapThreshold)
                {
                    TrySnapCandidate(
                        ref bestDistance,
                        ref bestX,
                        ref bestY,
                        ref bestAttach,
                        rect.Left - primary.Model.Width - RegionGap,
                        rect.Top,
                        desired,
                        other);
                }
                if (sameRow &&
                    Math.Abs(primaryLeft - RegionGap - rect.Right) <= SnapThreshold)
                {
                    TrySnapCandidate(
                        ref bestDistance,
                        ref bestX,
                        ref bestY,
                        ref bestAttach,
                        rect.Right + RegionGap,
                        rect.Top,
                        desired,
                        other);
                }
                if (sameColumn &&
                    Math.Abs(primaryBottom + RegionGap - rect.Top) <= SnapThreshold)
                {
                    TrySnapCandidate(
                        ref bestDistance,
                        ref bestX,
                        ref bestY,
                        ref bestAttach,
                        rect.Left,
                        rect.Top - primary.Model.Height - RegionGap,
                        desired,
                        other);
                }
                if (sameColumn &&
                    Math.Abs(primaryTop - RegionGap - rect.Bottom) <= SnapThreshold)
                {
                    TrySnapCandidate(
                        ref bestDistance,
                        ref bestX,
                        ref bestY,
                        ref bestAttach,
                        rect.Left,
                        rect.Bottom + RegionGap,
                        desired,
                        other);
                }
            }

            snapped = new Point(bestX, bestY);
            attach = bestAttach;
        }

        private static void TrySnapCandidate(
            ref double bestDistance,
            ref double bestX,
            ref double bestY,
            ref RegionControl bestAttach,
            double x,
            double y,
            Point desired,
            RegionControl other)
        {
            double dx = x - desired.X;
            double dy = y - desired.Y;
            double distance = (dx * dx) + (dy * dy);
            if (distance >= bestDistance)
            {
                return;
            }

            bestDistance = distance;
            bestX = x;
            bestY = y;
            bestAttach = other;
        }
        private void MoveGroupBy(double deltaX, double deltaY)
        {
            foreach (RegionControl region in _movingGroup)
            {
                Point position = GetRegionPosition(region);
                region.ApplyPosition(position.X + deltaX, position.Y + deltaY);
            }

            ClampMovingGroupToWorkArea();
        }

        private void ClampMovingGroupToWorkArea()
        {
            if (_movingGroup.Count == 0)
            {
                return;
            }

            Rect usable = GetUsableRect();
            double left = usable.Left;
            double top = usable.Top;
            double right = usable.Right;
            double bottom = usable.Bottom;

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            foreach (RegionControl region in _movingGroup)
            {
                Rect rect = GetRegionRect(region);
                if (rect.Left < minX)
                {
                    minX = rect.Left;
                }
                if (rect.Top < minY)
                {
                    minY = rect.Top;
                }
                if (rect.Right > maxX)
                {
                    maxX = rect.Right;
                }
                if (rect.Bottom > maxY)
                {
                    maxY = rect.Bottom;
                }
            }

            double adjustX = 0.0;
            double adjustY = 0.0;
            if (minX < left)
            {
                adjustX = left - minX;
            }
            else if (maxX > right)
            {
                adjustX = right - maxX;
            }
            if (minY < top)
            {
                adjustY = top - minY;
            }
            else if (maxY > bottom)
            {
                adjustY = bottom - maxY;
            }

            if (adjustX == 0.0 && adjustY == 0.0)
            {
                return;
            }

            foreach (RegionControl region in _movingGroup)
            {
                Point position = GetRegionPosition(region);
                region.ApplyPosition(position.X + adjustX, position.Y + adjustY);
            }
        }

        public void RemoveRegion(RegionControl control)
        {
            if (control != null &&
                !RestoreStorageItemsBeforeRegionRemoval(control.Model))
            {
                return;
            }

            string dockEdge = control == null
                ? RegionConfig.DockNone
                : NormalizeDockEdge(control.Model.DockEdge);

            if (_primaryMovingRegion != null && ReferenceEquals(_primaryMovingRegion, control))
            {
                _primaryMovingRegion = null;
                _movingGroup.Clear();
            }
            else
            {
                _movingGroup.Remove(control);
            }

            if (control != null)
            {
                control.ReleaseInteractionState();
                _interactionRegions.Remove(control);
            }

            if (_regions.Remove(control))
            {
                _pendingHideAtUtc.Remove(control);
                _config.Regions.Remove(control.Model);
                _root.Children.Remove(control);
                ApplyDockLayout(dockEdge);
                PersistConfigChange();
            }
        }

        public bool RemoveRegionById(string id)
        {
            foreach (RegionControl region in _regions)
            {
                if (string.Equals(region.Model.Id, id, StringComparison.Ordinal))
                {
                    RemoveRegion(region);
                    return true;
                }
            }

            return false;
        }

        public bool ResetRegionPosition(string id)
        {
            for (int index = 0; index < _regions.Count; index++)
            {
                RegionControl region = _regions[index];
                if (!string.Equals(region.Model.Id, id, StringComparison.Ordinal))
                {
                    continue;
                }

                RegionConfig model = region.Model;
                model.DockEdge = RegionConfig.DockNone;
                model.DockOrder = 0;
                region.RefreshDockLayout();
                model.X = 48 + ((index % 4) * 420);
                model.Y = 48 + ((index / 4) * 260);
                ClampModel(model);
                Canvas.SetLeft(region, model.X);
                Canvas.SetTop(region, model.Y);
                ApplyAllDockLayouts();
                PersistConfigChange();
                return true;
            }

            return false;
        }

        private bool RestoreStorageItemsBeforeRegionRemoval(RegionConfig region)
        {
            if (region == null)
            {
                return true;
            }

            HashSet<string> processed = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            bool failed = false;
            failed |= RestoreStorageItemsBeforeRegionRemoval(
                region,
                region.Items,
                processed);
            failed |= RestoreStorageItemsBeforeRegionRemoval(
                region,
                region.LaunchItems,
                processed);
            return !failed;
        }

        private bool RestoreStorageItemsBeforeRegionRemoval(
            RegionConfig region,
            List<DesktopItem> items,
            HashSet<string> processed)
        {
            if (items == null)
            {
                return false;
            }

            bool failed = false;
            foreach (DesktopItem item in items)
            {
                if (item == null ||
                    !ItemStorage.IsManagedPath(item.Path))
                {
                    continue;
                }

                string sourcePath = ItemStorage.NormalizePath(item.Path);
                if (string.IsNullOrEmpty(sourcePath) ||
                    !processed.Add(sourcePath) ||
                    IsStoragePathReferencedOutside(region, sourcePath))
                {
                    continue;
                }

                try
                {
                    string destination = ItemStorage.MoveToDesktop(item);
                    UpdateRestoredRegionItemPaths(region, sourcePath, destination);
                }
                catch (Exception ex)
                {
                    failed = true;
                    MessageBox.Show(
                        "无法将这个项目放回桌面，分区已保留：\n" +
                        item.Name +
                        "\n\n" +
                        ex.Message,
                        "GhostDesk",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            return failed;
        }

        private bool IsStoragePathReferencedOutside(
            RegionConfig sourceRegion,
            string path)
        {
            foreach (RegionConfig region in _config.Regions)
            {
                if (region == null || ReferenceEquals(region, sourceRegion))
                {
                    continue;
                }

                if (ContainsStoragePath(region.Items, path) ||
                    ContainsStoragePath(region.LaunchItems, path))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsStoragePath(
            List<DesktopItem> items,
            string path)
        {
            if (items == null)
            {
                return false;
            }

            foreach (DesktopItem item in items)
            {
                if (item != null &&
                    string.Equals(
                        ItemStorage.NormalizePath(item.Path),
                        path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void UpdateRestoredRegionItemPaths(
            RegionConfig region,
            string sourcePath,
            string destination)
        {
            UpdateRestoredRegionItemPaths(region.Items, sourcePath, destination);
            UpdateRestoredRegionItemPaths(region.LaunchItems, sourcePath, destination);
        }

        private static void UpdateRestoredRegionItemPaths(
            List<DesktopItem> items,
            string sourcePath,
            string destination)
        {
            if (items == null)
            {
                return;
            }

            foreach (DesktopItem item in items)
            {
                if (item != null &&
                    string.Equals(
                        ItemStorage.NormalizePath(item.Path),
                        sourcePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    item.Path = destination;
                    item.Name = ItemStorage.GetDisplayNameForPath(destination);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }
            base.OnClosed(e);
        }

        private void InitializeWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = false;
            ShowActivated = false;
            Focusable = true;
            ApplyDesktopAreaBounds();

            SourceInitialized += DesktopWindow_SourceInitialized;
            Loaded += DesktopWindow_Loaded;
        }

        private void ApplyDesktopAreaBounds()
        {
            Rect workArea = SystemParameters.WorkArea;
            double height = workArea.Height;

            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = height;

            if (_dragLayer != null)
            {
                _dragLayer.Width = Width;
                _dragLayer.Height = Height;
            }

            if (_root != null)
            {
                _root.UpdateLayout();
            }
        }

        private void RefreshDesktopLayout()
        {
            ApplyDesktopAreaBounds();
            ApplyAllDockLayouts();

            foreach (RegionControl region in _regions)
            {
                if (region != null && !IsDocked(region))
                {
                    ClampRegionIntoWorkspace(region);
                }
            }
        }

        private void BuildRegions()
        {
            _root = new Canvas();
            _root.Background = null;
            _root.HorizontalAlignment = HorizontalAlignment.Stretch;
            _root.VerticalAlignment = VerticalAlignment.Stretch;
            _root.ClipToBounds = false;
            Content = _root;

            _dragLayer = new Canvas();
            _dragLayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            _dragLayer.VerticalAlignment = VerticalAlignment.Stretch;
            _dragLayer.Width = Width;
            _dragLayer.Height = Height;
            _dragLayer.IsHitTestVisible = false;
            _dragLayer.Background = null;
            _dragLayer.ClipToBounds = false;
            Panel.SetZIndex(_dragLayer, 1000000);

            _guideLayer = new Canvas();
            _guideLayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            _guideLayer.VerticalAlignment = VerticalAlignment.Stretch;
            _guideLayer.Width = Width;
            _guideLayer.Height = Height;
            _guideLayer.IsHitTestVisible = false;
            _guideLayer.Background = null;
            _guideLayer.ClipToBounds = false;
            Panel.SetZIndex(_guideLayer, 1000001);

            foreach (RegionConfig model in _config.Regions)
            {
                AddRegion(model);
            }

            _root.Children.Add(_dragLayer);
            _root.Children.Add(_guideLayer);
            ApplyAllDockLayouts();
        }

        private void DesktopWindow_SourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _source = PresentationSource.FromVisual(this) as HwndSource;
            if (_source != null)
            {
                _source.AddHook(WndProc);
            }

            long exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
            UpdateInputTransparency();

            _activateMsgId = NativeMethods.RegisterWindowMessage(
                "GhostDesk_Activate_20261016");
        }

        private void UpdateInputTransparency()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            long exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            long layeredStyle = (exStyle | NativeMethods.WS_EX_LAYERED) & ~NativeMethods.WS_EX_TRANSPARENT;

            if (layeredStyle != exStyle)
            {
                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(layeredStyle));
            }
        }

        public void SetKeyboardEditing(bool editing)
        {
            if (_hwnd == IntPtr.Zero || _keyboardEditing == editing)
            {
                return;
            }

            _keyboardEditing = editing;
            long exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            if (editing)
            {
                exStyle &= ~NativeMethods.WS_EX_NOACTIVATE;
            }
            else
            {
                exStyle |= NativeMethods.WS_EX_NOACTIVATE;
            }

            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

            if (editing)
            {
                NativeMethods.SetWindowPos(
                    _hwnd,
                    NativeMethods.HWND_TOP,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER);
                NativeMethods.SetActiveWindow(_hwnd);
                NativeMethods.SetForegroundWindow(_hwnd);
                NativeMethods.SetFocus(_hwnd);
            }
            else
            {
                PositionBottom();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_activateMsgId != 0 &&
                msg == unchecked((int)_activateMsgId))
            {
                HandleActivateFromSecondInstance();
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == NativeMethods.WM_SETTINGCHANGE || msg == NativeMethods.WM_DISPLAYCHANGE)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(RefreshDesktopLayout));
            }

            if (msg != NativeMethods.WM_NCHITTEST)
            {
                return IntPtr.Zero;
            }

            handled = true;

            int screenX = unchecked((short)((long)lParam & 0xFFFF));
            int screenY = unchecked((short)(((long)lParam >> 16) & 0xFFFF));
            Point point = PointFromScreen(new Point(screenX, screenY));
            DependencyObject hit = InputHitTest(point) as DependencyObject;

            return IsInsideRegion(hit)
                ? new IntPtr(NativeMethods.HTCLIENT)
                : new IntPtr(NativeMethods.HTTRANSPARENT);
        }

        private static bool IsInsideRegion(DependencyObject target)
        {
            while (target != null)
            {
                if (target is RegionControl)
                {
                    return true;
                }

                target = VisualTreeHelper.GetParent(target);
            }

            return false;
        }

        private void ShowRegionsForHotkey()
        {
            if (!IsVisible)
            {
                Show();
            }

            _paused = false;
            _pendingHideAtUtc.Clear();
            foreach (RegionControl region in _regions)
            {
                region.SetHidden(false, _config.FadeSeconds);
            }
        }

        private string GetCachedProcessName(uint processId)
        {
            if (processId == 0)
            {
                return "";
            }

            DateTime nowUtc = DateTime.UtcNow;
            ProcessNameCacheEntry cached;
            if (_processNameCache.TryGetValue(processId, out cached) &&
                cached != null &&
                (nowUtc - cached.CheckedAtUtc).TotalSeconds < ProcessNameCacheTtlSeconds)
            {
                return cached.Name;
            }

            string processName = "";
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
                // 进程可能已退出或无权读取，缓存空值即可。
            }

            _processNameCache[processId] = new ProcessNameCacheEntry
            {
                Name = processName,
                CheckedAtUtc = nowUtc
            };
            return processName;
        }

        private bool IsScreenshotCaptureWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            StringBuilder classNameBuilder = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, classNameBuilder, classNameBuilder.Capacity);
            string windowClass = classNameBuilder.ToString();
            if (!string.IsNullOrWhiteSpace(windowClass))
            {
                string normalizedClass = windowClass.ToLowerInvariant();
                if (normalizedClass.Contains("screenclip") ||
                    normalizedClass.Contains("snipping") ||
                    normalizedClass.Contains("screensketch") ||
                    normalizedClass.Contains("screen sketch") ||
                    normalizedClass.Contains("screen capture"))
                {
                    return true;
                }
            }

            string processName = "";
            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            processName = GetCachedProcessName(processId);

            if (!string.IsNullOrWhiteSpace(processName))
            {
                string normalizedProcess = processName.ToLowerInvariant();
                if (normalizedProcess.StartsWith("snippingtool", StringComparison.Ordinal) ||
                    normalizedProcess.StartsWith("screensketch", StringComparison.Ordinal) ||
                    normalizedProcess.StartsWith("screenclip", StringComparison.Ordinal) ||
                    normalizedProcess.StartsWith("snipaste", StringComparison.Ordinal) ||
                    normalizedProcess.StartsWith("greenshot", StringComparison.Ordinal) ||
                    normalizedProcess.StartsWith("sharex", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            StringBuilder titleBuilder = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString();
            if (!string.IsNullOrWhiteSpace(title))
            {
                string normalizedTitle = title.ToLowerInvariant();
                if (normalizedTitle.Contains("snipping tool") ||
                    normalizedTitle.Contains("screen sketch") ||
                    normalizedTitle.Contains("截图工具") ||
                    normalizedTitle.Contains("屏幕截图"))
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleActivateFromSecondInstance()
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(delegate
                {
                    ShowRegionsForHotkey();
                }));
        }

        private void DesktopWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                RefreshDesktopLayout();
                PositionBottom();

                UpdateInputTransparency();
            }));
        }

        /// <summary>把覆盖层抬到普通窗口之上，让正在操作的分区不被遮住。</summary>
        /// <summary>当前前台窗口是不是 GhostDesk 自己的其它窗口（设置页等）。</summary>
        private bool IsOwnDialogForeground()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == _hwnd)
            {
                return false;
            }

            uint owner;
            NativeMethods.GetWindowThreadProcessId(foreground, out owner);
            return owner == (uint)Process.GetCurrentProcess().Id;
        }

        /// <summary>鼠标是不是停在某个分区上（决定要不要把覆盖层留在最上层）。</summary>
        private bool IsCursorOverAnyRegion()
        {
            try
            {
                System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
                Point local = PointFromScreen(new Point(cursor.X, cursor.Y));
                return FindRegionAt(local) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>右键菜单打开时取消覆盖层置顶，否则菜单会被分区挡住。</summary>
        private void SuspendOverlayTopmost()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }

        private void HandleContextMenuVisibility(bool opened)
        {
            if (opened)
            {
                _openContextMenuCount++;
                SuspendOverlayTopmost();
                return;
            }

            if (_openContextMenuCount > 0)
            {
                _openContextMenuCount--;
            }

            if (_openContextMenuCount == 0)
            {
                ScheduleOverlayBottom();
            }
        }

        private void RaiseOverlayAboveWindows()
        {
            if (_hwnd == IntPtr.Zero ||
                IsOwnDialogForeground() ||
                _openContextMenuCount > 0)
            {
                return;
            }

            if (_layerRestoreTimer != null)
            {
                _layerRestoreTimer.Stop();
            }

            _raiseSinceUtc = DateTime.UtcNow;

            // 前台窗口属于别的进程时，HWND_TOP 会被前台锁定挡住，必须临时置顶才能压上去
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }

        /// <summary>
        /// 交互（拖动、右键菜单、重命名）结束后把覆盖层放回普通窗口之下，
        /// 避免分区长期浮在其他程序上面。
        /// </summary>
        private void ScheduleOverlayBottom()
        {
            if (_layerRestoreTimer == null)
            {
                _layerRestoreTimer = new DispatcherTimer();
                _layerRestoreTimer.Interval = TimeSpan.FromMilliseconds(400);
                _layerRestoreTimer.Tick += delegate
                {
                    _layerRestoreTimer.Stop();
                    bool busy = _interactionRegions.Count > 0 ||
                        _primaryMovingRegion != null ||
                        _keyboardEditing;
                    if (busy)
                    {
                        return;
                    }

                    bool raisedTooLong =
                        (DateTime.UtcNow - _raiseSinceUtc).TotalSeconds > 6.0;
                    if (!raisedTooLong &&
                        (DateTime.UtcNow < _raiseHoldUntilUtc || IsCursorOverAnyRegion()))
                    {
                        // 刚点过、或鼠标还停在分区上：稍后再看
                        _layerRestoreTimer.Start();
                        return;
                    }

                    PositionBottom();
                };
            }

            _layerRestoreTimer.Stop();
            _layerRestoreTimer.Start();
        }

        /// <summary>是否显示对齐参考线（默认开启）。</summary>
        private bool GuidesEnabled()
        {
            return _config == null || _config.EnableGuides != false;
        }

        private void ClearGuides()
        {
            if (_guideLayer == null)
            {
                return;
            }

            foreach (Line line in _guideLines)
            {
                _guideLayer.Children.Remove(line);
            }

            _guideLines.Clear();

            if (_sizeBubble != null)
            {
                _guideLayer.Children.Remove(_sizeBubble);
                _sizeBubble = null;
                _sizeBubbleText = null;
            }
        }

        private void DrawGuide(double? verticalX, double? horizontalY)
        {
            if (_guideLayer == null)
            {
                return;
            }

            foreach (Line line in _guideLines)
            {
                _guideLayer.Children.Remove(line);
            }

            _guideLines.Clear();

            Brush brush = GlassTheme.CreateBrush(220, 139, 226, 255);
            double width = GetWorkspaceWidth();
            double height = GetWorkspaceHeight();

            if (verticalX.HasValue)
            {
                Line v = new Line();
                v.X1 = verticalX.Value;
                v.X2 = verticalX.Value;
                v.Y1 = 0;
                v.Y2 = height;
                v.Stroke = brush;
                v.StrokeThickness = 1;
                v.SnapsToDevicePixels = true;
                _guideLayer.Children.Add(v);
                _guideLines.Add(v);
            }

            if (horizontalY.HasValue)
            {
                Line h = new Line();
                h.X1 = 0;
                h.X2 = width;
                h.Y1 = horizontalY.Value;
                h.Y2 = horizontalY.Value;
                h.Stroke = brush;
                h.StrokeThickness = 1;
                h.SnapsToDevicePixels = true;
                _guideLayer.Children.Add(h);
                _guideLines.Add(h);
            }
        }

        private List<Rect> GetGuideTargetRects(RegionControl primary)
        {
            List<Rect> targets = new List<Rect>();
            targets.Add(GetUsableRect());

            foreach (RegionControl region in _regions)
            {
                if (region == null ||
                    ReferenceEquals(region, primary) ||
                    _movingGroup.Contains(region))
                {
                    continue;
                }

                Rect rect = GetRegionRect(region);
                if (rect.Width > 1.0 && rect.Height > 1.0)
                {
                    targets.Add(rect);
                }
            }

            return targets;
        }

        /// <summary>拖动时按其它分区/工作区的边与中线吸附，并画出参考线。</summary>
        private void ApplyMoveGuides(RegionControl primary, ref double x, ref double y)
        {
            if (!GuidesEnabled() || primary == null)
            {
                ClearGuides();
                return;
            }

            double width = primary.Model.Width;
            double height = primary.Model.Height;
            const double tolerance = 7.0;

            List<Rect> targets = GetGuideTargetRects(primary);

            double bestDx = 0.0;
            double bestDxDistance = double.MaxValue;
            double guideX = 0.0;
            bool hasGuideX = false;

            double[] myXs = { x, x + (width / 2.0), x + width };
            foreach (Rect target in targets)
            {
                double[] targetXs =
                {
                    target.Left,
                    target.Left + (target.Width / 2.0),
                    target.Right
                };

                foreach (double myX in myXs)
                {
                    foreach (double targetX in targetXs)
                    {
                        double distance = Math.Abs(targetX - myX);
                        if (distance <= tolerance && distance < bestDxDistance)
                        {
                            bestDxDistance = distance;
                            bestDx = targetX - myX;
                            guideX = targetX;
                            hasGuideX = true;
                        }
                    }
                }
            }

            double bestDy = 0.0;
            double bestDyDistance = double.MaxValue;
            double guideY = 0.0;
            bool hasGuideY = false;

            double[] myYs = { y, y + (height / 2.0), y + height };
            foreach (Rect target in targets)
            {
                double[] targetYs =
                {
                    target.Top,
                    target.Top + (target.Height / 2.0),
                    target.Bottom
                };

                foreach (double myY in myYs)
                {
                    foreach (double targetY in targetYs)
                    {
                        double distance = Math.Abs(targetY - myY);
                        if (distance <= tolerance && distance < bestDyDistance)
                        {
                            bestDyDistance = distance;
                            bestDy = targetY - myY;
                            guideY = targetY;
                            hasGuideY = true;
                        }
                    }
                }
            }

            if (hasGuideX)
            {
                x += bestDx;
            }

            if (hasGuideY)
            {
                y += bestDy;
            }

            DrawGuide(hasGuideX ? (double?)guideX : null, hasGuideY ? (double?)guideY : null);
        }

        /// <summary>缩放时吸附到其它分区的宽/高，并显示尺寸气泡。</summary>
        private Size ApplyResizeGuides(RegionControl region, double width, double height)
        {
            if (!GuidesEnabled() || region == null)
            {
                ClearGuides();
                return new Size(width, height);
            }

            const double tolerance = 7.0;
            double bestWidth = double.NaN;
            double bestWidthDistance = double.MaxValue;
            double bestHeight = double.NaN;
            double bestHeightDistance = double.MaxValue;

            foreach (RegionControl other in _regions)
            {
                if (other == null || ReferenceEquals(other, region))
                {
                    continue;
                }

                Rect rect = GetRegionRect(other);
                if (rect.Width <= 1.0 || rect.Height <= 1.0)
                {
                    continue;
                }

                double widthDistance = Math.Abs(rect.Width - width);
                if (widthDistance <= tolerance && widthDistance < bestWidthDistance)
                {
                    bestWidthDistance = widthDistance;
                    bestWidth = rect.Width;
                }

                double heightDistance = Math.Abs(rect.Height - height);
                if (heightDistance <= tolerance && heightDistance < bestHeightDistance)
                {
                    bestHeightDistance = heightDistance;
                    bestHeight = rect.Height;
                }
            }

            if (!double.IsNaN(bestWidth))
            {
                width = bestWidth;
            }

            if (!double.IsNaN(bestHeight))
            {
                height = bestHeight;
            }

            ShowSizeBubble(region, width, height);
            return new Size(width, height);
        }

        private void ShowSizeBubble(RegionControl region, double width, double height)
        {
            if (_guideLayer == null || region == null)
            {
                return;
            }

            if (_sizeBubble == null)
            {
                _sizeBubbleText = new TextBlock();
                _sizeBubbleText.FontSize = 12;
                _sizeBubbleText.Foreground = GlassTheme.TextPrimaryBrush;

                _sizeBubble = new Border();
                _sizeBubble.CornerRadius = new CornerRadius(6);
                _sizeBubble.Padding = new Thickness(8, 3, 8, 3);
                _sizeBubble.Background = GlassTheme.CreateBrush(220, 16, 20, 30);
                _sizeBubble.BorderBrush = GlassTheme.CreateBrush(120, 139, 226, 255);
                _sizeBubble.BorderThickness = new Thickness(1);
                _sizeBubble.IsHitTestVisible = false;
                _sizeBubble.Child = _sizeBubbleText;
                _guideLayer.Children.Add(_sizeBubble);
            }

            _sizeBubbleText.Text = ((int)Math.Round(width)).ToString() +
                " × " + ((int)Math.Round(height)).ToString();

            Rect rect = GetRegionRect(region);
            Canvas.SetLeft(_sizeBubble, rect.Left);
            Canvas.SetTop(_sizeBubble, Math.Max(0.0, rect.Bottom + 6.0));
        }

        private void PositionBottom()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            // 先取消置顶，再沉到最底层
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);

            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_BOTTOM,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSENDCHANGING);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            MonitorRegionContextMenus();

            if (_config.PauseInFullscreen && IsForegroundFullscreen())
            {
                _pendingHideAtUtc.Clear();
                SetAllRegionsHidden(true, 0.04);
                UpdateTimerCadence(false);
                return;
            }

            if (_paused)
            {
                _pendingHideAtUtc.Clear();
                SetAllRegionsHidden(false, _config.FadeSeconds);
                UpdateTimerCadence(false);
                return;
            }

            if (_interactionRegions.Count > 0)
            {
                UpdateTimerCadence(false);
                return;
            }

            if (_hwnd == IntPtr.Zero)
            {
                _pendingHideAtUtc.Clear();
                SetAllRegionsHidden(true, 0.0);
                UpdateTimerCadence(false);
                return;
            }

            bool cursorOverDesktop = false;
            RegionControl hoverRegion = null;
            if (HoverEnabled)
            {
                System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
                if (IsCursorOverDesktop())
                {
                    cursorOverDesktop = true;
                    Point point = PointFromScreen(new Point(cursor.X, cursor.Y));
                    hoverRegion = FindRegionAt(point);
                }
            }

            DateTime nowUtc = DateTime.UtcNow;

            foreach (RegionControl region in _regions)
            {
                if (region == hoverRegion)
                {
                    _pendingHideAtUtc.Remove(region);
                    region.SetHidden(false, _config.FadeSeconds);
                    continue;
                }

                if (region.IsHidden)
                {
                    _pendingHideAtUtc.Remove(region);
                    continue;
                }

                DateTime leaveAtUtc;
                if (!_pendingHideAtUtc.TryGetValue(region, out leaveAtUtc))
                {
                    _pendingHideAtUtc[region] = nowUtc;
                    continue;
                }

                if ((nowUtc - leaveAtUtc).TotalSeconds >= _config.IdleSeconds)
                {
                    _pendingHideAtUtc.Remove(region);
                    region.SetHidden(true, _config.FadeSeconds);
                }
            }

            UpdateTimerCadence(cursorOverDesktop);
        }

        private bool AnyRegionNeedsFastTimer()
        {
            foreach (RegionControl region in _regions)
            {
                if (region != null && region.NeedsFastTimer)
                {
                    return true;
                }
            }

            return false;
        }

        private bool AnyRegionHasOpenContextMenu()
        {
            foreach (RegionControl region in _regions)
            {
                if (region != null && region.HasOpenContextMenu)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateTimerCadence(bool cursorOverDesktop)
        {
            if (_timer == null)
            {
                return;
            }

            bool fast = AnyRegionHasOpenContextMenu() ||
                (!_paused &&
                    _interactionRegions.Count == 0 &&
                    (cursorOverDesktop || AnyRegionNeedsFastTimer()));

            double targetMilliseconds = fast
                ? FastTimerMilliseconds
                : IdleTimerMilliseconds;

            if (Math.Abs(_timer.Interval.TotalMilliseconds - targetMilliseconds) > 0.5)
            {
                _timer.Interval = TimeSpan.FromMilliseconds(targetMilliseconds);
            }
        }

        private bool IsCursorOverDesktop()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return false;
            }

            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            NativeMethods.POINT nativePoint = new NativeMethods.POINT
            {
                X = cursor.X,
                Y = cursor.Y
            };
            IntPtr windowAtPoint = NativeMethods.WindowFromPoint(nativePoint);
            IntPtr rootAtPoint = NativeMethods.GetAncestor(windowAtPoint, NativeMethods.GA_ROOT);

            return rootAtPoint == _hwnd ||
                   windowAtPoint == _hwnd ||
                   IsDesktopShellWindow(rootAtPoint) ||
                   IsDesktopShellWindow(windowAtPoint);
        }

        private void SetAllRegionsHidden(bool hidden, double fadeSeconds)
        {
            foreach (RegionControl region in _regions)
            {
                region.SetHidden(hidden, fadeSeconds);
            }

            // 全部收起来后把覆盖层落回桌面层，避免透明窗口压在别的程序上
            if (hidden && _interactionRegions.Count == 0 && _primaryMovingRegion == null)
            {
                ScheduleOverlayBottom();
            }
        }

        private void SetRegionHidden(RegionControl region, bool hidden, double fadeSeconds)
        {
            if (region == null)
            {
                return;
            }

            region.SetHidden(hidden, fadeSeconds);
        }

        private RegionControl FindRegionAt(Point point)
        {
            for (int index = _regions.Count - 1; index >= 0; index--)
            {
                RegionControl region = _regions[index];
                double left = Canvas.GetLeft(region);
                double top = Canvas.GetTop(region);

                if (point.X >= left && point.X <= left + region.ActualWidth &&
                    point.Y >= top && point.Y <= top + region.ActualHeight)
                {
                    return region;
                }
            }

            return null;
        }

        private void MonitorRegionContextMenus()
        {
            bool leftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;
            if (!leftDown)
            {
                _leftButtonWasDown = false;
                return;
            }

            if (_leftButtonWasDown)
            {
                return;
            }

            _leftButtonWasDown = true;

            System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
            NativeMethods.POINT nativePoint = new NativeMethods.POINT
            {
                X = cursor.X,
                Y = cursor.Y
            };
            IntPtr target = NativeMethods.WindowFromPoint(nativePoint);
            Point localPoint = PointFromScreen(new Point(cursor.X, cursor.Y));
            RegionControl clickedRegion = FindRegionAt(localPoint);
            if (target == IntPtr.Zero)
            {
                return;
            }

            CommitRenamesOutsideTarget(target, clickedRegion);

            RegionControl openRegion = null;
            foreach (RegionControl region in _regions)
            {
                if (region.HasOpenContextMenu)
                {
                    openRegion = region;
                    break;
                }
            }

            if (openRegion == null)
            {
                return;
            }

            if (target != _hwnd && IsWindowOwnedByGhostDesk(target))
            {
                return;
            }

            foreach (RegionControl region in _regions)
            {
                if (region.HasOpenContextMenu)
                {
                    region.CloseOpenContextMenus();
                }
            }
        }

        private void CommitRenamesOutsideTarget(IntPtr target, RegionControl clickedRegion)
        {
            if (target == IntPtr.Zero)
            {
                return;
            }

            foreach (RegionControl region in _regions)
            {
                if (!region.IsRenaming)
                {
                    continue;
                }

                if (target == _hwnd && ReferenceEquals(clickedRegion, region))
                {
                    continue;
                }

                if (target != _hwnd && IsWindowOwnedByGhostDesk(target))
                {
                    continue;
                }

                region.CommitActiveRename();
            }
        }

        private bool IsWindowOwnedByGhostDesk(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (hwnd == _hwnd)
            {
                return true;
            }

            uint currentProcessId = (uint)Process.GetCurrentProcess().Id;
            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            return processId == currentProcessId;
        }

        private void RegionInteractionChanged(RegionControl region, bool active)
        {
            if (active)
            {
                _interactionRegions.Add(region);
                SetRegionHidden(region, false, _config.FadeSeconds);
            }
            else
            {
                _interactionRegions.Remove(region);
                if (_interactionRegions.Count == 0)
                {
                    ScheduleOverlayBottom();
                }
            }
        }

        private void OpenItem(DesktopItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path))
            {
                return;
            }

            try
            {
                if (item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start("explorer.exe", item.Path);
                    return;
                }

                AppController.StartProcess(AppController.CreateLaunchStartInfo(item.Path));
            }
            catch (Exception ex)
            {
                if (AppController.IsElevationCancelled(ex))
                {
                    return;
                }

                MessageBox.Show("无法打开：\n" + item.Path + "\n\n" + ex.Message, "GhostDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool IsForegroundFullscreen()
        {
            return IsWindowFullscreen(NativeMethods.GetForegroundWindow());
        }

        private bool IsWindowFullscreen(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero || foreground == _hwnd)
            {
                return false;
            }

            if (!NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
            {
                return false;
            }

            uint processId;
            NativeMethods.GetWindowThreadProcessId(foreground, out processId);
            if (processId == (uint)Process.GetCurrentProcess().Id)
            {
                return false;
            }

            string processName = GetCachedProcessName(processId);
            if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetClassName(foreground, className, className.Capacity);
            string windowClass = className.ToString();
            if (windowClass == "Progman" || windowClass == "WorkerW" ||
                windowClass == "Shell_TrayWnd" || windowClass == "Shell_SecondaryTrayWnd" ||
                windowClass == "XamlExplorerHostIslandWindow")
            {
                return false;
            }

            if (IsScreenshotCaptureWindow(foreground))
            {
                return false;
            }

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(foreground, out rect))
            {
                return false;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            int screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            int screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            if (width < screenWidth || height < screenHeight)
            {
                return false;
            }

            long style = NativeMethods.GetWindowLong(foreground, NativeMethods.GWL_STYLE).ToInt64();
            return (style & NativeMethods.WS_CAPTION) == 0;
        }

        private static bool IsDesktopShellWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            string value = className.ToString();
            return value == "Progman" || value == "WorkerW";
        }

        private static void ClampModel(RegionConfig model)
        {
            double left = 0.0;
            double top = 0.0;
            double right = Math.Max(left, SystemParameters.WorkArea.Width - model.Width);
            double bottom = Math.Max(top, SystemParameters.WorkArea.Height - model.Height);

            if (model.X < left)
            {
                model.X = left;
            }
            if (model.X > right)
            {
                model.X = right;
            }
            if (model.Y < top)
            {
                model.Y = top;
            }
            if (model.Y > bottom)
            {
                model.Y = bottom;
            }
        }

        private void NotifyConfigChanged()
        {
            Action handler = ConfigChanged;
            if (handler != null)
            {
                handler();
            }
        }

        private bool PersistRegionConfigChange()
        {
            if (!ConfigStore.Save(_config))
            {
                return false;
            }

            NotifyConfigChanged();
            return true;
        }

        private void PersistConfigChange()
        {
            if (ConfigStore.Save(_config))
            {
                NotifyConfigChanged();
                return;
            }

            if (string.Equals(
                    Environment.GetEnvironmentVariable(
                        "GHOSTDESK_TEST_SUPPRESS_DIALOGS"),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }

            Exception error = ConfigStore.LastSaveError;
            MessageBox.Show(
                "配置保存失败：\n" +
                (error == null ? "未知错误。" : error.Message),
                "GhostDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
