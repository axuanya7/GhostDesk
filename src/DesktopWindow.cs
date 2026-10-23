using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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
        private DispatcherTimer _timer;
        private HwndSource _source;
        private IntPtr _hwnd = IntPtr.Zero;
        private readonly HashSet<RegionControl> _interactionRegions =
            new HashSet<RegionControl>();
        private bool _leftButtonWasDown;
        private bool _keyboardEditing;
        private bool _paused;
        private bool _hoverEnabled = true;
        private uint _activateMsgId;
        private readonly Dictionary<RegionControl, DateTime> _pendingHideAtUtc =
            new Dictionary<RegionControl, DateTime>();
        private const double SnapThreshold = 20.0;
        private const double DockThreshold = 2.0;
        private const double ConnectedTolerance = 8.0;
        private const double ConnectedGap = 4.0;
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
                region.ApplyPosition(desiredX, desiredY);
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
            }
        }

        private void ApplyMovingGroupPlacement(RegionControl region)
        {
            if (region == null || _movingGroup.Count <= 1)
            {
                ApplyDockPlacement(region);
                return;
            }

            string targetEdge = FindDockEdgeForMovingGroup(region);
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

        private void ApplyDockPlacement(RegionControl region)
        {
            if (region == null)
            {
                return;
            }

            NormalizeRegionDock(region.Model);
            string previousEdge = region.Model.DockEdge;
            string targetEdge = FindDockEdge(region);
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
            double availableWidth = GetWorkspaceWidth();
            double availableHeight = GetWorkspaceHeight();

            double leftDistance = Math.Max(0.0, rect.Left);
            double topDistance = Math.Max(0.0, rect.Top);
            double rightDistance = Math.Max(0.0, availableWidth - rect.Right);
            double bottomDistance = Math.Max(0.0, availableHeight - rect.Bottom);

            string edge = RegionConfig.DockNone;
            double nearest = DockThreshold + 1.0;

            if (leftDistance <= DockThreshold && leftDistance < nearest)
            {
                edge = RegionConfig.DockLeft;
                nearest = leftDistance;
            }
            if (topDistance <= DockThreshold && topDistance < nearest)
            {
                edge = RegionConfig.DockTop;
                nearest = topDistance;
            }
            if (rightDistance <= DockThreshold && rightDistance < nearest)
            {
                edge = RegionConfig.DockRight;
                nearest = rightDistance;
            }
            if (bottomDistance <= DockThreshold && bottomDistance < nearest)
            {
                edge = RegionConfig.DockBottom;
                nearest = bottomDistance;
            }

            return edge;
        }

        private string FindDockEdgeForMovingGroup(RegionControl region)
        {
            if (region == null || _movingGroup.Count <= 1)
            {
                return FindDockEdge(region);
            }

            Rect rect = GetMovingGroupBounds();
            double availableWidth = GetWorkspaceWidth();
            double availableHeight = GetWorkspaceHeight();
            double leftDistance = Math.Max(0.0, rect.Left);
            double topDistance = Math.Max(0.0, rect.Top);
            double rightDistance = Math.Max(0.0, availableWidth - rect.Right);
            double bottomDistance = Math.Max(0.0, availableHeight - rect.Bottom);

            Point current = GetRegionPosition(region);
            double deltaX = current.X - _groupMoveOriginPrimaryPosition.X;
            double deltaY = current.Y - _groupMoveOriginPrimaryPosition.Y;
            bool row = IsMovingGroupAligned(true);
            if (!row && Math.Abs(deltaY) > Math.Abs(deltaX) + 4.0)
            {
                if (topDistance <= DockThreshold &&
                    topDistance <= bottomDistance)
                {
                    return RegionConfig.DockTop;
                }
                if (bottomDistance <= DockThreshold)
                {
                    return RegionConfig.DockBottom;
                }
            }
            else if (Math.Abs(deltaX) > Math.Abs(deltaY) + 4.0)
            {
                if (leftDistance <= DockThreshold &&
                    leftDistance <= rightDistance)
                {
                    return RegionConfig.DockLeft;
                }
                if (rightDistance <= DockThreshold)
                {
                    return RegionConfig.DockRight;
                }
            }

            string edge = RegionConfig.DockNone;
            double nearest = DockThreshold + 1.0;
            if (leftDistance <= DockThreshold && leftDistance < nearest)
            {
                edge = RegionConfig.DockLeft;
                nearest = leftDistance;
            }
            if (!row &&
                topDistance <= DockThreshold &&
                topDistance < nearest)
            {
                edge = RegionConfig.DockTop;
                nearest = topDistance;
            }
            if (rightDistance <= DockThreshold && rightDistance < nearest)
            {
                edge = RegionConfig.DockRight;
                nearest = rightDistance;
            }
            if (!row &&
                bottomDistance <= DockThreshold &&
                bottomDistance < nearest)
            {
                edge = RegionConfig.DockBottom;
            }

            return edge;
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

            double availableWidth = GetWorkspaceWidth();
            double availableHeight = GetWorkspaceHeight();
            bool horizontal =
                string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal) ||
                string.Equals(edge, RegionConfig.DockBottom, StringComparison.Ordinal);

            double availableAlong = horizontal ? availableWidth : availableHeight;
            foreach (RegionControl region in docked)
            {
                double width;
                double height;
                GetRegionSize(region, out width, out height);

                Point current = GetRegionPosition(region);
                double along = horizontal ? current.X : current.Y;
                if (anchor != null && ReferenceEquals(region, anchor))
                {
                    along = anchorPosition;
                }

                double size = horizontal ? width : height;
                along = Math.Min(
                    Math.Max(0.0, availableAlong - size),
                    Math.Max(0.0, along));

                double x;
                double y;

                if (horizontal)
                {
                    x = along;
                    if (string.Equals(edge, RegionConfig.DockTop, StringComparison.Ordinal))
                    {
                        y = 0.0;
                    }
                    else
                    {
                        y = availableHeight - height;
                    }
                }
                else
                {
                    y = along;
                    if (string.Equals(edge, RegionConfig.DockLeft, StringComparison.Ordinal))
                    {
                        x = 0.0;
                    }
                    else
                    {
                        x = availableWidth - width;
                    }
                }

                region.ApplyPosition(
                    Math.Max(0.0, x),
                    Math.Max(0.0, y));
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
            double maximumX = Math.Max(0.0, GetWorkspaceWidth() - width);
            double maximumY = Math.Max(0.0, GetWorkspaceHeight() - height);
            double x = Math.Min(maximumX, Math.Max(0.0, position.X));
            double y = Math.Min(maximumY, Math.Max(0.0, position.Y));
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
                if (other == null || ReferenceEquals(other, primary) || _movingGroup.Contains(other))
                {
                    continue;
                }

                Rect rect = GetRegionRect(other);
                bool horizontallyNear = Math.Abs(primaryTop - rect.Top) <= SnapThreshold;
                bool verticallyNear = Math.Abs(primaryLeft - rect.Left) <= SnapThreshold;

                if (horizontallyNear && Math.Abs(primaryRight - rect.Left) <= SnapThreshold)
                {
                    double x = rect.Left - primary.Model.Width;
                    double y = rect.Top;
                    double dx = x - desired.X;
                    double dy = y - desired.Y;
                    double distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                        bestAttach = other;
                    }
                }
                if (horizontallyNear && Math.Abs(primaryLeft - rect.Right) <= SnapThreshold)
                {
                    double x = rect.Right;
                    double y = rect.Top;
                    double dx = x - desired.X;
                    double dy = y - desired.Y;
                    double distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                        bestAttach = other;
                    }
                }
                if (horizontallyNear && Math.Abs(primaryLeft - rect.Left) <= SnapThreshold)
                {
                    double x = rect.Left;
                    double y = rect.Top;
                    double dx = x - desired.X;
                    double dy = y - desired.Y;
                    double distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                        bestAttach = other;
                    }
                }
                if (horizontallyNear && Math.Abs(primaryRight - rect.Right) <= SnapThreshold)
                {
                    double x = rect.Right - primary.Model.Width;
                    double y = rect.Top;
                    double dx = x - desired.X;
                    double dy = y - desired.Y;
                    double distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                        bestAttach = other;
                    }
                }

                if (verticallyNear && Math.Abs(primaryBottom - rect.Top) <= SnapThreshold)
                {
                    double x = rect.Left;
                    double y = rect.Top - primary.Model.Height;
                    double dx = x - desired.X;
                    double dy = y - desired.Y;
                    double distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                        bestAttach = other;
                    }
                }
                if (verticallyNear && Math.Abs(primaryTop - rect.Bottom) <= SnapThreshold)
                {
                    double x = rect.Left;
                    double y = rect.Bottom;
                    double dx = x - desired.X;
                    double dy = y - desired.Y;
                    double distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                        bestAttach = other;
                    }
                }
                if (verticallyNear && Math.Abs(primaryTop - rect.Top) <= SnapThreshold)
                {
                    double x = rect.Left;
                    double y = rect.Top;
                    double dx = x - desired.X;
                    double dy = y - desired.Y;
                    double distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                        bestAttach = other;
                    }
                }
                if (verticallyNear && Math.Abs(primaryBottom - rect.Bottom) <= SnapThreshold)
                {
                    double x = rect.Left;
                    double y = rect.Bottom - primary.Model.Height;
                    double dx = x - desired.X;
                    double dy = y - desired.Y;
                    double distance = (dx * dx) + (dy * dy);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                        bestAttach = other;
                    }
                }
            }

            snapped = new Point(bestX, bestY);
            attach = bestAttach;
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

            double left = 0.0;
            double top = 0.0;
            double right = GetWorkspaceWidth();
            double bottom = GetWorkspaceHeight();

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

            foreach (RegionConfig model in _config.Regions)
            {
                AddRegion(model);
            }

            _root.Children.Add(_dragLayer);
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

        private void PositionBottom()
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

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
