using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using Ink_Canvas.Helpers;

namespace Ink_Canvas
{
    /// <summary>
    /// 线擦（EraseByStroke）的「剪枝」实现。
    ///
    /// 原生线擦命中一条笔迹就整条删除。这里改成：以该笔迹与其它笔迹的交点为边界
    /// （两侧没有交点时以笔迹端点为界），只删除落点所在的那一段，其余部分保留。
    /// 因此一条枝杈擦一次只剪掉交点之外的那截，主干仍然在；独立的一笔没有交点时
    /// 仍是整条删除，与原来的线擦行为一致。
    ///
    /// 输入接管：线擦期间在 PreviewStylus*/PreviewMouse*/PreviewTouch* 层把事件标记为
    /// Handled，阻止 InkCanvas 内部的整条擦除，然后自己按采样点做剪枝。
    /// </summary>
    public partial class MainWindow
    {
        private bool _lineEraseSessionActive;
        private bool _lineErasePruning;
        private Point _lineErasePointer;
        private bool _lineErasePointerValid;
        private int _lineEraseTouchId;
        private DateTime _lineEraseLastPruneUtc = DateTime.MinValue;

        // 本次按下期间被剪枝的原始笔迹，以及「当前画布上的段 → 原始笔迹」映射，用于一次按下只记一条撤销历史
        private readonly Dictionary<Stroke, Stroke> _lineEraseOriginalBySegment = new Dictionary<Stroke, Stroke>();
        private readonly List<Stroke> _lineEraseOriginals = new List<Stroke>();

        /// <summary>开始一次线擦会话（一次按下 → 抬起）。</summary>
        internal void BeginLineEraseSession(Point point)
        {
            // 上一次抬起没收到（触摸被取消、输入设备切换等）：先把旧会话收尾，
            // 否则这次的剪枝会累积到上一次的历史里
            if (_lineEraseSessionActive) EndLineEraseSession();

            _lineEraseSessionActive = true;
            _lineErasePointer = point;
            _lineErasePointerValid = true;
            _lineEraseLastPruneUtc = DateTime.MinValue;
            _lineEraseOriginalBySegment.Clear();
            _lineEraseOriginals.Clear();
        }

        /// <summary>结束一次线擦会话，并把这段期间的所有剪枝合成一条撤销历史。</summary>
        internal void EndLineEraseSession()
        {
            if (!_lineEraseSessionActive) return;

            _lineEraseSessionActive = false;
            _lineErasePointerValid = false;
            _lineEraseTouchId = 0;
            _lineEraseLastPruneUtc = DateTime.MinValue;

            if (_lineEraseOriginals.Count > 0)
            {
                try
                {
                    // Undo：移除当前保留的段、恢复被剪枝前的原始笔迹
                    var addedFinal = new StrokeCollection(_lineEraseOriginalBySegment.Keys.ToArray());
                    var originals = new StrokeCollection(_lineEraseOriginals.ToArray());
                    timeMachine.CommitStrokeEraseHistory(addedFinal, originals);
                    MarkCurrentPageInkChanged();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"线擦历史提交失败: {ex.Message}", LogHelper.LogType.Warning);
                }
            }

            _lineEraseOriginalBySegment.Clear();
            _lineEraseOriginals.Clear();
        }

        /// <summary>线擦剪枝入口：命中笔迹时只删除落点所在的两个边界之间那一段。</summary>
        /// <returns>是否真的改变了画布</returns>
        internal bool PruneLineEraseAt(Point point)
        {
            if (!_lineEraseSessionActive || _lineErasePruning) return false;
            if (inkCanvas == null || inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke) return false;
            if (TryBlockFrozenPageMutation("线擦")) return false;

            var now = DateTime.UtcNow;
            if (_lineErasePointerValid)
            {
                var dx = point.X - _lineErasePointer.X;
                var dy = point.Y - _lineErasePointer.Y;
                // stylus / touch / mouse 三条事件链会重复上报同一个点，去抖避免同一处反复剪枝
                if (dx * dx + dy * dy < 4 && (now - _lineEraseLastPruneUtc).TotalMilliseconds < 12) return false;
            }

            _lineErasePointer = point;
            _lineErasePointerValid = true;
            _lineEraseLastPruneUtc = now;

            StrokeCollection hits;
            try
            {
                hits = inkCanvas.Strokes.HitTest(point);
            }
            catch
            {
                return false;
            }

            if (hits == null || hits.Count == 0) return false;

            var changes = new List<(Stroke Stroke, StrokeCollection Kept, Stroke Original)>();
            foreach (var stroke in hits.OfType<Stroke>().ToArray())
            {
                if (stroke == null || stroke.StylusPoints.Count < 2) continue;
                if (stroke.ContainsPropertyData(FrozenStrokePropertyGuid)) continue;
                if (!TryComputeLineEraseRange(stroke, point, out var cutStart, out var cutEnd)) continue;

                var original = _lineEraseOriginalBySegment.TryGetValue(stroke, out var root) ? root : stroke;
                var kept = BuildLineEraseKeptSegments(stroke, cutStart, cutEnd);
                changes.Add((stroke, kept, original));
            }

            if (changes.Count == 0) return false;

            var previousCommitType = _currentCommitType;
            _lineErasePruning = true;
            _currentCommitType = CommitReason.CodeInput; // 抑制 StrokesChanged 的自动历史，本次按下结束时统一提交
            try
            {
                foreach (var change in changes)
                {
                    if (!inkCanvas.Strokes.Contains(change.Stroke)) continue;

                    _lineEraseOriginalBySegment.Remove(change.Stroke);
                    if (!_lineEraseOriginals.Contains(change.Original)) _lineEraseOriginals.Add(change.Original);

                    if (change.Kept.Count > 0)
                        inkCanvas.Strokes.Replace(new StrokeCollection { change.Stroke }, change.Kept);
                    else
                        inkCanvas.Strokes.Remove(change.Stroke);

                    foreach (var segment in change.Kept)
                        _lineEraseOriginalBySegment[segment] = change.Original;
                }
            }
            finally
            {
                _currentCommitType = previousCommitType;
                _lineErasePruning = false;
            }

            return true;
        }

        /// <summary>
        /// 计算落点所在的可删除区间（浮点索引，如 12.3 表示第 12 段 30% 处）。
        /// 边界 = 该笔迹与其它笔迹的交点 ∪ 自身端点。
        /// </summary>
        private bool TryComputeLineEraseRange(Stroke stroke, Point point, out double cutStart, out double cutEnd)
        {
            cutStart = 0;
            cutEnd = 0;

            var points = stroke.StylusPoints;
            if (points.Count < 2) return false;
            if (!TryFindClosestParamOnStroke(points, point, out var hitParam, out var distance)) return false;

            // 原生橡皮是按形状命中的，落点可能离笔迹有点距离；超出容差就交给原生整条删除
            var maxDistance = Math.Max(8, eraserWidth / 2 + 6);
            if (distance > maxDistance) return false;

            var last = points.Count - 1;
            var cuts = new List<double>(8) { 0, last };
            CollectLineEraseIntersectionParams(stroke, cuts);

            cuts.Sort();
            var merged = new List<double>(cuts.Count);
            foreach (var cut in cuts)
            {
                var value = Math.Max(0, Math.Min(last, cut));
                if (merged.Count == 0 || value - merged[merged.Count - 1] > 1e-3)
                    merged.Add(value);
            }

            for (var i = 0; i < merged.Count - 1; i++)
            {
                if (hitParam < merged[i] - 1e-6 || hitParam > merged[i + 1] + 1e-6) continue;

                cutStart = merged[i];
                cutEnd = merged[i + 1];
                return cutEnd - cutStart > 1e-3;
            }

            return false;
        }

        /// <summary>收集目标笔迹与画布上其它笔迹的交点参数（浮点索引）。</summary>
        private void CollectLineEraseIntersectionParams(Stroke target, List<double> cuts)
        {
            if (inkCanvas == null) return;

            var targetPoints = target.StylusPoints;
            var targetBounds = target.GetBounds();
            targetBounds.Inflate(2, 2);

            foreach (var other in inkCanvas.Strokes)
            {
                if (other == null || ReferenceEquals(other, target)) continue;
                if (other.StylusPoints.Count < 2) continue;
                if (!targetBounds.IntersectsWith(other.GetBounds())) continue;

                var otherPoints = other.StylusPoints;
                var otherSegmentBounds = new Rect[otherPoints.Count - 1];
                for (var j = 0; j < otherSegmentBounds.Length; j++)
                {
                    otherSegmentBounds[j] = SegmentBounds(otherPoints[j].ToPoint(), otherPoints[j + 1].ToPoint());
                }

                for (var i = 0; i < targetPoints.Count - 1; i++)
                {
                    var p1 = targetPoints[i].ToPoint();
                    var p2 = targetPoints[i + 1].ToPoint();
                    var segmentBounds = SegmentBounds(p1, p2);

                    for (var j = 0; j < otherSegmentBounds.Length; j++)
                    {
                        if (!segmentBounds.IntersectsWith(otherSegmentBounds[j])) continue;
                        if (!TrySegmentIntersection(p1, p2, otherPoints[j].ToPoint(), otherPoints[j + 1].ToPoint(), out var t)) continue;
                        cuts.Add(i + t);
                    }
                }
            }
        }

        private static Rect SegmentBounds(Point a, Point b)
        {
            return new Rect(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X),
                Math.Abs(a.Y - b.Y));
        }

        /// <summary>线段 p1p2 与 q1q2 是否相交，t 为交点在线段 p1p2 上的比例。</summary>
        private static bool TrySegmentIntersection(Point p1, Point p2, Point q1, Point q2, out double t)
        {
            t = 0;

            var rx = p2.X - p1.X;
            var ry = p2.Y - p1.Y;
            var sx = q2.X - q1.X;
            var sy = q2.Y - q1.Y;
            var denominator = rx * sy - ry * sx;
            if (Math.Abs(denominator) < 1e-9) return false; // 平行（含共线）

            var qpx = q1.X - p1.X;
            var qpy = q1.Y - p1.Y;
            var tValue = (qpx * sy - qpy * sx) / denominator;
            var uValue = (qpx * ry - qpy * rx) / denominator;

            // 端点不算交点：端点本身就是边界，重复计入会产生零长度区间
            if (tValue <= 1e-6 || tValue >= 1 - 1e-6) return false;
            if (uValue < -1e-6 || uValue > 1 + 1e-6) return false;

            t = tValue;
            return true;
        }

        /// <summary>找落点在笔迹折线上的最近参数（浮点索引）与距离。</summary>
        private static bool TryFindClosestParamOnStroke(StylusPointCollection points, Point point,
            out double param, out double distance)
        {
            param = 0;
            distance = double.MaxValue;

            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = points[i].ToPoint();
                var b = points[i + 1].ToPoint();
                var ab = b - a;
                var lengthSquared = ab.LengthSquared;
                var t = lengthSquared <= 1e-12
                    ? 0
                    : Math.Max(0, Math.Min(1, ((point - a) * ab) / lengthSquared));
                var projected = a + ab * t;
                var d = (point - projected).Length;
                if (d >= distance) continue;

                distance = d;
                param = i + t;
            }

            return distance < double.MaxValue;
        }

        /// <summary>按删除区间切出保留的两段（区间外的部分）。</summary>
        private StrokeCollection BuildLineEraseKeptSegments(Stroke stroke, double cutStart, double cutEnd)
        {
            var points = stroke.StylusPoints;
            var last = points.Count - 1;
            var kept = new StrokeCollection();

            if (cutStart > 1e-6)
            {
                var left = new StylusPointCollection();
                var endIndex = Math.Min((int)Math.Floor(cutStart), last);
                for (var i = 0; i <= endIndex; i++) left.Add(points[i]);

                var cutPoint = InterpolateLineErasePoint(points, cutStart);
                if (left.Count == 0 || !PointsEqual(left[left.Count - 1].ToPoint(), cutPoint.ToPoint()))
                    left.Add(cutPoint);

                if (left.Count >= 2) kept.Add(CreateLineEraseSegment(stroke, left));
            }

            if (cutEnd < last - 1e-6)
            {
                var right = new StylusPointCollection();
                right.Add(InterpolateLineErasePoint(points, cutEnd));

                var startIndex = Math.Min((int)Math.Ceiling(cutEnd), last);
                for (var i = startIndex; i <= last; i++)
                {
                    if (PointsEqual(right[right.Count - 1].ToPoint(), points[i].ToPoint())) continue;
                    right.Add(points[i]);
                }

                if (right.Count >= 2) kept.Add(CreateLineEraseSegment(stroke, right));
            }

            return kept;
        }

        private static bool PointsEqual(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
        }

        /// <summary>按浮点索引插值出一个点（保留压感）。</summary>
        private static StylusPoint InterpolateLineErasePoint(StylusPointCollection points, double param)
        {
            var last = points.Count - 1;
            if (param <= 0) return points[0];
            if (param >= last) return points[last];

            var index = (int)Math.Floor(param);
            var t = param - index;
            var a = points[index];
            var b = points[index + 1];

            return new StylusPoint(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.PressureFactor + (b.PressureFactor - a.PressureFactor) * (float)t);
        }

        /// <summary>用截取的点集复制出一条新笔迹（沿用画笔属性与自定义属性数据）。</summary>
        private static Stroke CreateLineEraseSegment(Stroke source, StylusPointCollection points)
        {
            var segment = new Stroke(points, source.DrawingAttributes.Clone());

            try
            {
                foreach (var id in source.GetPropertyDataIds())
                {
                    if (segment.ContainsPropertyData(id) || !source.ContainsPropertyData(id)) continue;
                    var value = source.GetPropertyData(id);
                    if (value == null) continue;
                    segment.AddPropertyData(id, value);
                }
            }
            catch
            {
                // 个别自定义属性不允许复制，忽略即可，不影响剪枝结果
            }

            return segment;
        }

        /// <summary>
        /// 兜底：个别设备/驱动上 InkCanvas 会在路由事件之前就把命中的笔迹整条删掉。
        /// 一旦发现线擦会话期间发生了「非本类逻辑」的整条删除，就把笔迹还原并按落点剪枝，
        /// 保证剪枝语义不因输入路径不同而退化。
        /// </summary>
        private bool TryRestoreLineEraseDeletedStrokes(StrokeCollectionChangedEventArgs e)
        {
            if (!_lineEraseSessionActive || _lineErasePruning) return false;
            if (inkCanvas?.EditingMode != InkCanvasEditingMode.EraseByStroke) return false;
            if (e?.Removed == null || e.Removed.Count == 0) return false;
            if (!_lineErasePointerValid) return false;
            if (_currentCommitType == CommitReason.CodeInput) return false;

            var point = _lineErasePointer;
            var previousCommitType = _currentCommitType;
            _currentCommitType = CommitReason.CodeInput;
            _lineErasePruning = true;
            try
            {
                foreach (var original in e.Removed.OfType<Stroke>().ToArray())
                {
                    if (original == null || original.StylusPoints.Count < 2) continue;
                    if (original.ContainsPropertyData(FrozenStrokePropertyGuid)) continue;

                    if (!inkCanvas.Strokes.Contains(original)) inkCanvas.Strokes.Add(original);

                    if (!TryComputeLineEraseRange(original, point, out var cutStart, out var cutEnd))
                    {
                        // 没有可用边界或落点太远：保持原生行为（整条删除）
                        if (inkCanvas.Strokes.Contains(original)) inkCanvas.Strokes.Remove(original);
                        continue;
                    }

                    var root = _lineEraseOriginalBySegment.TryGetValue(original, out var known) ? known : original;
                    var kept = BuildLineEraseKeptSegments(original, cutStart, cutEnd);

                    _lineEraseOriginalBySegment.Remove(original);
                    if (!_lineEraseOriginals.Contains(root)) _lineEraseOriginals.Add(root);

                    if (kept.Count > 0)
                        inkCanvas.Strokes.Replace(new StrokeCollection { original }, kept);
                    else
                        inkCanvas.Strokes.Remove(original);

                    foreach (var segment in kept)
                        _lineEraseOriginalBySegment[segment] = root;
                }

                MarkCurrentPageInkChanged();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"线擦兜底剪枝失败: {ex.Message}", LogHelper.LogType.Warning);
                return false;
            }
            finally
            {
                _lineErasePruning = false;
                _currentCommitType = previousCommitType;
            }

            return true;
        }

        // ---------------- 输入接管 ----------------

        private void InkCanvas_PreviewStylusDown_LineErase(object sender, StylusDownEventArgs e)
        {
            if (inkCanvas?.EditingMode != InkCanvasEditingMode.EraseByStroke) return;

            if (IsCurrentPageFrozen)
            {
                TryBlockFrozenPageMutation("线擦");
                e.Handled = true;
                return;
            }

            var point = e.GetPosition(inkCanvas);
            BeginLineEraseSession(point);
            // SVG 元素（可编辑场景）仍按原逻辑整行/整元素擦除
            BeginSecAgentStrokeErase(point);
            PruneLineEraseAt(point);

            if (!inkCanvas.IsStylusCaptured) inkCanvas.CaptureStylus();
            e.Handled = true;
        }

        private void InkCanvas_PreviewStylusMove_LineErase(object sender, StylusEventArgs e)
        {
            if (!_lineEraseSessionActive) return;
            if (inkCanvas?.EditingMode != InkCanvasEditingMode.EraseByStroke) return;

            var point = e.GetPosition(inkCanvas);
            MoveSecAgentStrokeErase(point);
            PruneLineEraseAt(point);
            e.Handled = true;
        }

        private void InkCanvas_PreviewStylusUp_LineErase(object sender, StylusEventArgs e)
        {
            if (!_lineEraseSessionActive) return;

            EndSecAgentStrokeErase();
            EndLineEraseSession();

            if (inkCanvas?.IsStylusCaptured == true) inkCanvas.ReleaseStylusCapture();

            // 冒泡的 StylusUp 被 Handled 挡住了，橡皮擦自动切回要在这里补一次
            HandleEraserOperationEnded();
            e.Handled = true;
        }
    }
}
