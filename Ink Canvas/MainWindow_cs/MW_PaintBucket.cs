using Ink_Canvas.Controls;
using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        #region 油漆桶（填充闭合区域）

        /// <summary>油漆桶占用的绘制模式号（不占用 1~28）。</summary>
        private const int PaintBucketShapeMode = 29;

        /// <summary>笔迹像素的 alpha 阈值：大于该值视为边界，填充不会越过。</summary>
        private const byte PaintBucketBarrierAlpha = 16;

        /// <summary>采样位图的最大像素数，超出时按比例缩小，避免超大画布拖慢填充。</summary>
        private const int PaintBucketMaxPixels = 4_000_000;

        /// <summary>填充色的不透明度：半透明，保证之后画上去的笔迹仍然能透出。</summary>
        private const double PaintBucketFillOpacity = 0.45;

        /// <summary>轮廓简化容差（采样像素），把像素阶梯边简化成折线。</summary>
        private const double PaintBucketContourTolerance = 1.2;

        /// <summary>
        /// 填充轮廓的外扩像素数（采样像素）：抵消边界掩码 1 像素膨胀造成的缩进，
        /// 让填充色贴住笔迹内沿，不再出现白缝。调大可进一步压掉缝隙，过大会压到笔迹上。
        /// </summary>
        private const int PaintBucketContourOutset = 1;

        /// <summary>填充元素的名字前缀：用于缩放/漫游时识别并同步变换。</summary>
        private const string PaintBucketFillElementPrefix = "paintfill_";

        /// <summary>重复触发保护：触摸抬起后可能再补一个提升的鼠标抬起事件。</summary>
        private static readonly TimeSpan PaintBucketDuplicateWindow = TimeSpan.FromMilliseconds(250);

        /// <summary>触摸 / 手写笔抬笔位置：合成的鼠标事件里拿不到真实坐标。</summary>
        private Point _paintBucketTapPoint;
        private bool _paintBucketTapPointValid;

        private DateTime _paintBucketLastFillTime = DateTime.MinValue;
        private Point _paintBucketLastFillPoint;

        /// <summary>用户选定的填充色；为 null 时跟随当前画笔颜色。</summary>
        private Color? _paintBucketFillColor;

        /// <summary>填充色调色盘弹窗（与「选中对象工具栏」的颜色弹窗同一套控件与交互）。</summary>
        private Popup _paintBucketColorPopup;
        private ShapeDrawPopupContent _paintBucketColorPopupOwner;

        /// <summary>
        /// 油漆桶：把 <paramref name="point"/> 所在的连通空白区域填充为当前填充色。
        /// 边界由画布上现有笔迹构成（按笔宽光栅化后取 alpha），未闭合（漫到采样边界）时不填充；
        /// 结果生成为矢量 <see cref="System.Windows.Shapes.Path"/> 元素加入画布，并写入元素级撤销历史。
        /// </summary>
        private void TryFillRegionAt(Point point)
        {
            if (inkCanvas == null) return;
            if (TryBlockFrozenPageMutation("填充区域"))
            {
                PaintBucketTrace($"页面冻结，跳过填充 point={point}");
                return;
            }

            // 同一次单击可能同时走触摸路径与提升的鼠标路径，短时间内同点重复填充只取第一次
            var now = DateTime.UtcNow;
            if (now - _paintBucketLastFillTime < PaintBucketDuplicateWindow &&
                (point - _paintBucketLastFillPoint).Length < 12)
            {
                PaintBucketTrace($"重复触发，跳过填充 point={point}");
                return;
            }

            var strokes = inkCanvas.Strokes;
            if (strokes == null || strokes.Count == 0)
            {
                PaintBucketTrace("画布上没有笔迹，无法形成闭合区域");
                return;
            }

            // 采样范围：全部笔迹的包围盒（含笔宽外扩），并限制在画布附近，避免生成无意义的采样
            var sampleBounds = Rect.Empty;
            double maxPenSize = 1;
            foreach (var stroke in strokes)
            {
                if (stroke == null) continue;
                sampleBounds.Union(stroke.GetBounds());
                maxPenSize = Math.Max(maxPenSize,
                    Math.Max(stroke.DrawingAttributes.Width, stroke.DrawingAttributes.Height));
            }

            if (sampleBounds.IsEmpty) return;
            sampleBounds.Inflate(maxPenSize, maxPenSize);

            var canvasBounds = new Rect(0, 0,
                Math.Max(1, inkCanvas.ActualWidth), Math.Max(1, inkCanvas.ActualHeight));
            // 允许内容部分滚出视口，最额外扩一屏
            canvasBounds.Inflate(canvasBounds.Width, canvasBounds.Height);
            sampleBounds.Intersect(canvasBounds);

            if (sampleBounds.IsEmpty || !sampleBounds.Contains(point))
            {
                PaintBucketTrace($"落点不在笔迹采样范围内：point={point} sampleBounds={sampleBounds}");
                return;
            }

            double scale = 1.0;
            double area = sampleBounds.Width * sampleBounds.Height;
            if (area > PaintBucketMaxPixels) scale = Math.Sqrt(PaintBucketMaxPixels / area);

            int width = Math.Max(1, (int)Math.Ceiling(sampleBounds.Width * scale));
            int height = Math.Max(1, (int)Math.Ceiling(sampleBounds.Height * scale));

            byte[] barrier;
            try
            {
                barrier = RenderStrokeAlphaMask(sampleBounds, scale, width, height);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"油漆桶采样笔迹失败: {ex}", LogHelper.LogType.Error);
                return;
            }

            // 边界向内膨胀 1 像素：手绘闭合图形常有 1~2 像素的缝，膨胀后能吃掉这种毛缝
            var blocked = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (barrier[index] <= PaintBucketBarrierAlpha) continue;

                    blocked[index] = true;
                    if (x > 0) blocked[index - 1] = true;
                    if (x < width - 1) blocked[index + 1] = true;
                    if (y > 0) blocked[index - width] = true;
                    if (y < height - 1) blocked[index + width] = true;
                }
            }

            int startX = (int)Math.Floor((point.X - sampleBounds.X) * scale);
            int startY = (int)Math.Floor((point.Y - sampleBounds.Y) * scale);
            if (startX < 0 || startY < 0 || startX >= width || startY >= height)
            {
                PaintBucketTrace($"落点超出采样范围 start=({startX},{startY}) mask={width}x{height}");
                return;
            }
            // 点在笔迹本身上：不做填充
            if (blocked[startY * width + startX])
            {
                PaintBucketTrace($"落点落在笔迹上 alpha={barrier[startY * width + startX]}");
                return;
            }

            // 洪水填充（4 邻域，按膨胀后的笔迹掩码判定边界）
            var region = new bool[width * height];
            var stack = new Stack<int>();

            void TryPush(int index)
            {
                if (region[index]) return;
                if (blocked[index]) return;
                region[index] = true;
                stack.Push(index);
            }

            int startIndex = startY * width + startX;
            region[startIndex] = true;
            stack.Push(startIndex);

            int minX = startX, maxX = startX, minY = startY, maxY = startY;
            bool leakedToBounds = false;
            while (stack.Count > 0)
            {
                int index = stack.Pop();
                int y = index / width;
                int x = index - y * width;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                // 区域漫到采样边界 = 该处没有闭合，按需求不填充
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    leakedToBounds = true;

                if (x > 0) TryPush(index - 1);
                if (x < width - 1) TryPush(index + 1);
                if (y > 0) TryPush(index - width);
                if (y < height - 1) TryPush(index + width);
            }

            if (leakedToBounds)
            {
                // 点击处不在闭合笔迹内部：不生成任何填充
                PaintBucketTrace($"区域未闭合（漫到采样边界）bbox=({minX},{minY})-({maxX},{maxY})");
                return;
            }

            // 轮廓外扩：判定边界时把笔迹掩码向外膨胀了 1 像素（防手绘毛缝漏色），
            // 填充轮廓因此比笔迹内沿缩进约 1 像素，再叠加简化容差就是肉眼看到的白缝。
            // 这里把区域掩码向外扩同样的像素数把它抵消掉；外扩后压在笔迹内沿下方，不易察觉。
            DilateRegionMask(region, width, height, PaintBucketContourOutset);

            // 外扩会改变区域包围盒，重新统计（它是 Path 布局原点与坐标换算的基准）
            minX = width - 1;
            minY = height - 1;
            maxX = 0;
            maxY = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!region[y * width + x]) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            // 区域边界 → 矢量路径；以区域左上角为 Path 的布局原点
            var contours = TraceRegionContours(region, width, height);
            if (contours.Count == 0)
            {
                PaintBucketTrace("区域没有可用的边界环");
                return;
            }

            var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
            foreach (var contour in contours)
            {
                var simplified = SimplifyContour(contour, PaintBucketContourTolerance);
                if (simplified.Count < 3) continue;

                var figure = new PathFigure
                {
                    StartPoint = MaskPointToCanvas(simplified[0], minX, minY, scale),
                    IsClosed = true,
                    IsFilled = true
                };

                var segment = new PolyLineSegment();
                for (int i = 1; i < simplified.Count; i++)
                    segment.Points.Add(MaskPointToCanvas(simplified[i], minX, minY, scale));
                figure.Segments.Add(segment);

                geometry.Figures.Add(figure);
            }

            if (geometry.Figures.Count == 0)
            {
                PaintBucketTrace("轮廓简化后没有剩下可用的图形");
                return;
            }
            geometry.Freeze();

            var color = _paintBucketFillColor ?? inkCanvas.DefaultDrawingAttributes.Color;
            byte fillAlpha = (byte)Math.Round(255 * PaintBucketFillOpacity);

            var fillPath = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Fill = new SolidColorBrush(Color.FromArgb(fillAlpha, color.R, color.G, color.B)),
                Stretch = Stretch.None,
                // 填充层像背景色块：不参与命中测试，避免挡住笔迹的选择与擦除
                IsHitTestVisible = false,
                Focusable = false,
                Name = PaintBucketFillElementPrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
            };

            // 与插入图片一致的 TransformGroup，保证双指缩放 / 画布漫游时与笔迹同步
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(1, 1));
            transformGroup.Children.Add(new TranslateTransform(0, 0));
            transformGroup.Children.Add(new RotateTransform(0));
            fillPath.RenderTransform = transformGroup;

            InkCanvas.SetLeft(fillPath, sampleBounds.X + minX / scale);
            InkCanvas.SetTop(fillPath, sampleBounds.Y + minY / scale);

            inkCanvas.Children.Add(fillPath);
            // 选择模式下可选中填充块：绑定与图片一致的元素事件（拖动、滚轮缩放、触摸），
            // 随后按当前工具模式收敛命中测试，非选择模式下保持穿透以免挡住书写与擦除
            BindElementEvents(fillPath);
            UpdatePaintFillHitTest();
            timeMachine.CommitElementInsertHistory(fillPath);

            PaintBucketTrace($"填充完成：环数={contours.Count} 图形数={geometry.Figures.Count} " +
                             $"bounds={geometry.Bounds} 颜色={color} 落点={point}");

            _paintBucketLastFillTime = DateTime.UtcNow;
            _paintBucketLastFillPoint = point;
        }

        /// <summary>把采样范围内的笔迹渲染成 alpha 掩码（0 = 空白，>0 = 笔迹像素）。</summary>
        private byte[] RenderStrokeAlphaMask(Rect bounds, double scale, int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                foreach (var stroke in inkCanvas.Strokes)
                {
                    if (stroke == null) continue;
                    if (!stroke.GetBounds().IntersectsWith(bounds)) continue;
                    stroke.Draw(dc);
                }
                dc.Pop();
                dc.Pop();
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);

            var alpha = new byte[width * height];
            for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
            return alpha;
        }

        /// <summary>
        /// 找出填充区域的所有边界环（像素角点坐标）。每段区域边界都是有向边，首尾相接即可拼成闭合环；
        /// 外轮廓与「洞」用相同的方向输出，配合 EvenOdd 填充规则即可正确挖洞。
        /// </summary>
        private static List<List<Point>> TraceRegionContours(bool[] region, int width, int height)
        {
            var edges = new Dictionary<long, List<long>>();
            int stride = width + 1;

            void AddEdge(int startX, int startY, int endX, int endY)
            {
                long key = (long)startY * stride + startX;
                if (!edges.TryGetValue(key, out var list))
                {
                    list = new List<long>(2);
                    edges[key] = list;
                }
                list.Add((long)endY * stride + endX);
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!region[y * width + x]) continue;

                    if (y == 0 || !region[(y - 1) * width + x]) AddEdge(x, y, x + 1, y);
                    if (x == width - 1 || !region[y * width + x + 1]) AddEdge(x + 1, y, x + 1, y + 1);
                    if (y == height - 1 || !region[(y + 1) * width + x]) AddEdge(x + 1, y + 1, x, y + 1);
                    if (x == 0 || !region[y * width + x - 1]) AddEdge(x, y + 1, x, y);
                }
            }

            var contours = new List<List<Point>>();
            var startKeys = new List<long>(edges.Keys);

            foreach (var startKey in startKeys)
            {
                while (edges.TryGetValue(startKey, out var pending) && pending.Count > 0)
                {
                    var contour = new List<Point>();
                    long current = startKey;

                    while (true)
                    {
                        if (!edges.TryGetValue(current, out var outgoing) || outgoing.Count == 0) break;

                        int last = outgoing.Count - 1;
                        long next = outgoing[last];
                        outgoing.RemoveAt(last);
                        if (outgoing.Count == 0) edges.Remove(current);

                        contour.Add(new Point((int)(current % stride), (int)(current / stride)));
                        current = next;

                        if (current == startKey) break;
                    }

                    if (contour.Count >= 4) contours.Add(contour);
                }
            }

            return contours;
        }

        /// <summary>
        /// 把区域掩码向外扩指定像素（4 邻域逐轮膨胀）。用于抵消判定边界时对笔迹掩码的膨胀，
        /// 让填充轮廓贴住笔迹内沿。
        /// </summary>
        private static void DilateRegionMask(bool[] region, int width, int height, int pixels)
        {
            if (region == null || pixels <= 0) return;

            var previous = new bool[region.Length];
            for (int pass = 0; pass < pixels; pass++)
            {
                Array.Copy(region, previous, region.Length);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        if (previous[index]) continue;

                        if ((x > 0 && previous[index - 1])
                            || (x < width - 1 && previous[index + 1])
                            || (y > 0 && previous[index - width])
                            || (y < height - 1 && previous[index + width]))
                        {
                            region[index] = true;
                        }
                    }
                }
            }
        }

        /// <summary>把像素角点坐标换算成 Path 元素内的坐标（相对区域左上角）。</summary>
        private static Point MaskPointToCanvas(Point maskPoint, int minX, int minY, double scale)
        {
            return new Point((maskPoint.X - minX) / scale, (maskPoint.Y - minY) / scale);
        }

        /// <summary>轮廓简化：先去掉共线点，再用 Douglas-Peucker 把像素阶梯边压成折线。</summary>
        private static List<Point> SimplifyContour(List<Point> points, double tolerance)
        {
            int n = points.Count;
            if (n < 4) return points;

            var collinear = new List<Point>(n);
            for (int i = 0; i < n; i++)
            {
                var prev = points[(i - 1 + n) % n];
                var cur = points[i];
                var next = points[(i + 1) % n];
                double cross = (cur.X - prev.X) * (next.Y - cur.Y) - (cur.Y - prev.Y) * (next.X - cur.X);
                if (Math.Abs(cross) > 1e-9) collinear.Add(cur);
            }

            if (collinear.Count < 4) return collinear.Count >= 3 ? collinear : points;

            // 闭环：取距首点最远的点把环拆成两段，分别简化后再拼回去
            int farIndex = 1;
            double maxDistance = -1;
            for (int i = 1; i < collinear.Count; i++)
            {
                double distance = (collinear[i] - collinear[0]).LengthSquared;
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farIndex = i;
                }
            }

            var firstHalf = DouglasPeucker(collinear.GetRange(0, farIndex + 1), tolerance);
            var secondRange = collinear.GetRange(farIndex, collinear.Count - farIndex);
            secondRange.Add(collinear[0]);
            var secondHalf = DouglasPeucker(secondRange, tolerance);

            // 两段共享首尾点，拼接时各去掉一个端点
            firstHalf.RemoveAt(firstHalf.Count - 1);
            secondHalf.RemoveAt(secondHalf.Count - 1);

            var result = new List<Point>(firstHalf.Count + secondHalf.Count);
            result.AddRange(firstHalf);
            result.AddRange(secondHalf);

            return result.Count >= 3 ? result : collinear;
        }

        /// <summary>Douglas-Peucker 折线简化（迭代实现，避免深递归）。</summary>
        private static List<Point> DouglasPeucker(List<Point> points, double tolerance)
        {
            if (points.Count < 3) return new List<Point>(points);

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;

            var stack = new Stack<(int Start, int End)>();
            stack.Push((0, points.Count - 1));

            while (stack.Count > 0)
            {
                var (start, end) = stack.Pop();
                if (end <= start + 1) continue;

                double maxDistance = -1;
                int farthest = -1;
                for (int i = start + 1; i < end; i++)
                {
                    double distance = DistanceToSegment(points[i], points[start], points[end]);
                    if (distance > maxDistance)
                    {
                        maxDistance = distance;
                        farthest = i;
                    }
                }

                if (maxDistance > tolerance && farthest > 0)
                {
                    keep[farthest] = true;
                    stack.Push((start, farthest));
                    stack.Push((farthest, end));
                }
            }

            var result = new List<Point>();
            for (int i = 0; i < points.Count; i++)
            {
                if (keep[i]) result.Add(points[i]);
            }
            return result;
        }

        /// <summary>点到线段的距离（Douglas-Peucker 用）。</summary>
        private static double DistanceToSegment(Point point, Point start, Point end)
        {
            var vx = end.X - start.X;
            var vy = end.Y - start.Y;
            double lengthSquared = vx * vx + vy * vy;

            if (lengthSquared < 1e-12)
                return (point - start).Length;

            double t = ((point.X - start.X) * vx + (point.Y - start.Y) * vy) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));

            var projected = new Point(start.X + t * vx, start.Y + t * vy);
            return (point - projected).Length;
        }

        /// <summary>
        /// 接通油漆桶的填充色选择：右下角▲按钮弹出与「选中对象工具栏」一致的快捷调色盘。
        /// （浮动栏与白板各有一份几何面板，各自调用一次。）
        /// </summary>
        private void WireUpPaintBucketColorPicker(ShapeDrawPopupContent content)
        {
            if (content == null) return;

            var toggle = content.BucketColorToggleButton;
            if (toggle != null)
            {
                toggle.Checked += (s, e) => OpenPaintBucketColorPalette(content);
                toggle.Unchecked += (s, e) =>
                {
                    if (_paintBucketColorPopup != null && _paintBucketColorPopup.IsOpen)
                        _paintBucketColorPopup.IsOpen = false;
                };
            }

            UpdatePaintBucketFillState();
        }

        /// <summary>弹出填充色调色盘，并把当前填充色同步为勾选态。</summary>
        private void OpenPaintBucketColorPalette(ShapeDrawPopupContent content)
        {
            var toggle = content?.BucketColorToggleButton;
            if (toggle == null) return;

            if (_paintBucketColorPopup == null)
            {
                var palette = new QuickColorPaletteControl();
                palette.ColorClicked += PaintBucketColorPalette_ColorClicked;

                _paintBucketColorPopup = new Popup
                {
                    Child = new Border
                    {
                        Background = (Brush)FindResource("FloatingBarBackgroundBrush"),
                        BorderBrush = (Brush)FindResource("FloatingBarBorderBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(8, 6, 8, 6),
                        Child = palette
                    },
                    Placement = PlacementMode.Bottom,
                    // 与「选中对象工具栏」的颜色弹窗保持一致：由按钮自己控制开合。
                    // StaysOpen=false 会在按钮抬起（触发 Checked）的同一轮鼠标事件里把弹窗立即关掉。
                    StaysOpen = true,
                    AllowsTransparency = true,
                    PopupAnimation = PopupAnimation.Fade
                };

                // 点击外部关闭时，把▲按钮恢复到未选中态
                _paintBucketColorPopup.Closed += (s, e) => SetPaintBucketColorToggleState(false);

                // 交给宿主统一的 Popup 管理器接管 Z 序：批注/置顶模式下它会周期性把弹窗拉回 TOPMOST，
                // 否则弹窗会被其它应用窗口盖住（与「选中对象工具栏」的颜色弹窗行为一致）
                GetPopupManager()?.RegisterPopup(_paintBucketColorPopup);
            }

            _paintBucketColorPopupOwner = content;
            _paintBucketColorPopup.PlacementTarget = toggle;

            if (_paintBucketColorPopup.Child is Border popupBorder
                && popupBorder.Child is QuickColorPaletteControl paletteControl)
            {
                paletteControl.ClearAllChecked();
                paletteControl.SetCheckedByColor(GetPaintBucketDisplayColor());
            }

            _paintBucketColorPopup.IsOpen = true;
        }

        /// <summary>调色盘选色：作为油漆桶的填充色，并收起弹窗。</summary>
        private void PaintBucketColorPalette_ColorClicked(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not string colorName) return;

            var color = colorName switch
            {
                "Black" => Colors.Black,
                "White" => Colors.White,
                "Red" => Colors.Red,
                "Orange" => Color.FromRgb(251, 150, 80),
                "Yellow" => Colors.Yellow,
                "Green" => Color.FromRgb(22, 163, 74),
                "Blue" => Color.FromRgb(37, 99, 235),
                "Purple" => Color.FromRgb(147, 51, 234),
                _ => Colors.Black
            };

            SetPaintBucketFillColor(color);

            // 选完色块即视为开始使用填充：若填充工具当前没有被选中，顺手把它选中，
            // 省去「先选色再点一次填充按钮」的重复操作。
            if (drawingShapeMode != PaintBucketShapeMode)
                EnterShapeDrawingMode(PaintBucketShapeMode);

            if (_paintBucketColorPopup != null) _paintBucketColorPopup.IsOpen = false;
            SetPaintBucketColorToggleState(false);
        }

        /// <summary>同步两份面板的▲按钮勾选态（不触发弹窗开关循环）。</summary>
        private void SetPaintBucketColorToggleState(bool isChecked)
        {
            SetPaintBucketColorToggleState(ShapeDrawPopupContent, isChecked);
            SetPaintBucketColorToggleState(BoardShapeDrawPopupContent, isChecked);
        }

        private void SetPaintBucketColorToggleState(ShapeDrawPopupContent content, bool isChecked)
        {
            var toggle = content?.BucketColorToggleButton;
            if (toggle != null && toggle.IsChecked != isChecked) toggle.IsChecked = isChecked;
        }

        /// <summary>设定填充色（刷新油漆桶图标颜色）。</summary>
        private void SetPaintBucketFillColor(Color color)
        {
            _paintBucketFillColor = color;
            UpdatePaintBucketFillState();
        }

        /// <summary>刷新两份面板的油漆桶图标配色（桶内颜料 = 当前填充色）。</summary>
        private void UpdatePaintBucketFillState()
        {
            UpdatePaintBucketFillState(ShapeDrawPopupContent);
            UpdatePaintBucketFillState(BoardShapeDrawPopupContent);
        }

        private void UpdatePaintBucketFillState(ShapeDrawPopupContent content)
        {
            var bucketButton = content?.DrawBucketBtn;
            if (bucketButton == null) return;

            bucketButton.IconSource = BuildPaintBucketIcon(GetPaintBucketDisplayColor(), GetFloatingBarForegroundColor());
        }

        /// <summary>当前用于显示的填充色：未选色时跟随画笔颜色。</summary>
        private Color GetPaintBucketDisplayColor()
        {
            if (_paintBucketFillColor.HasValue) return _paintBucketFillColor.Value;
            return inkCanvas?.DefaultDrawingAttributes?.Color ?? Colors.Black;
        }

        /// <summary>取当前主题下浮动栏前景色，用于油漆桶图标的描边。</summary>
        private Color GetFloatingBarForegroundColor()
        {
            if (TryFindResource("FloatBarForeground") is SolidColorBrush foreground)
                return foreground.Color;
            if (TryFindResource("FloatingBarForegroundBrush") is SolidColorBrush fallback)
                return fallback.Color;
            return Colors.Black;
        }

        /// <summary>面板打开时刷新一次油漆桶图标配色（主题切换后前景色可能变化）。</summary>
        private void UpdatePaintBucketIconColors()
        {
            UpdatePaintBucketFillState();
        }

        /// <summary>在代码里构建油漆桶图标：描边跟随主题前景色，桶内颜料与液滴使用当前填充色。</summary>
        private static DrawingImage BuildPaintBucketIcon(Color paintColor, Color outlineColor)
        {
            var group = new DrawingGroup
            {
                ClipGeometry = Geometry.Parse("M0,0 V750 H750 V0 H0 Z")
            };

            var outlinePen = new Pen(new SolidColorBrush(outlineColor), 42)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            var handlePen = new Pen(new SolidColorBrush(outlineColor), 38)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            var paintBrush = new SolidColorBrush(paintColor);

            group.Children.Add(new GeometryDrawing
            {
                Pen = outlinePen,
                Geometry = Geometry.Parse("M165,275 L585,275 L515,655 Q508,695 468,695 L282,695 Q242,695 235,655 Z")
            });
            group.Children.Add(new GeometryDrawing
            {
                Brush = paintBrush,
                Geometry = Geometry.Parse("M196,430 L554,430 L515,655 Q508,695 468,695 L282,695 Q242,695 235,655 Z")
            });
            group.Children.Add(new GeometryDrawing
            {
                Pen = handlePen,
                Geometry = Geometry.Parse("M228,275 A147,147 0 0 1 522,275")
            });
            group.Children.Add(new GeometryDrawing
            {
                Brush = paintBrush,
                Geometry = Geometry.Parse("M640,120 C652,168 690,205 690,248 A50,50 0 0 1 590,248 C590,205 628,168 640,120 Z")
            });

            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        /// <summary>
        /// 橡皮擦命中填充路径时整块擦除（写入元素删除历史，可撤销）。
        /// 填充是 inkCanvas.Children 里的矢量路径，不在 inkCanvas.Strokes 中，原生橡皮擦不会处理它，
        /// 因此在橡皮擦的每个扫描点上单独做一次命中检测。
        /// </summary>
        private void ErasePaintFillElementsAt(Point point)
        {
            if (inkCanvas == null) return;
            if (TryBlockFrozenPageMutation("擦除填充")) return;

            var halfWidth = Math.Max(5, eraserWidth / 2);
            var halfHeight = isEraserCircleShape ? halfWidth : Math.Max(5, eraserWidth * 56 / 38 / 2);
            var eraserRect = new Rect(point.X - halfWidth, point.Y - halfHeight, halfWidth * 2, halfHeight * 2);

            for (int i = inkCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (inkCanvas.Children[i] is not System.Windows.Shapes.Path path) continue;
                if (!IsPaintBucketFillElement(path)) continue;
                if (!PaintFillHitByRect(path, eraserRect)) continue;

                inkCanvas.Children.Remove(path);
                timeMachine.CommitElementRemoveHistory(path);
                PaintBucketTrace($"橡皮擦移除了一块填充，落点={point}");
            }
        }

        /// <summary>判断画布坐标下的矩形（橡皮擦范围）是否命中填充路径。</summary>
        private static bool PaintFillHitByRect(System.Windows.Shapes.Path path, Rect canvasRect)
        {
            var geometry = path.Data;
            if (geometry == null) return false;

            var offsetX = InkCanvas.GetLeft(path);
            var offsetY = InkCanvas.GetTop(path);
            if (double.IsNaN(offsetX)) offsetX = 0;
            if (double.IsNaN(offsetY)) offsetY = 0;

            var matrix = Matrix.Identity;
            if (path.RenderTransform is Transform transform)
            {
                matrix = transform.Value;
                if (!matrix.HasInverse) return false;
                matrix.Invert();
            }

            // 画布坐标 → 路径自身坐标
            var localRect = new Rect(canvasRect.X - offsetX, canvasRect.Y - offsetY,
                canvasRect.Width, canvasRect.Height);
            localRect.Transform(matrix);

            if (!geometry.Bounds.IntersectsWith(localRect)) return false;

            try
            {
                return geometry.FillContainsWithDetail(new RectangleGeometry(localRect)) != IntersectionDetail.Empty;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>油漆桶诊断日志：每一步提前返回都记录原因，便于排查「点了没反应」。</summary>
        private static void PaintBucketTrace(string message)
        {
            LogHelper.WriteLogToFile("[油漆桶] " + message, LogHelper.LogType.Trace);
        }

        /// <summary>判断某个画布子元素是否是油漆桶生成的填充路径。</summary>
        private static bool IsPaintBucketFillElement(UIElement element)
        {
            return element is System.Windows.Shapes.Path path
                   && path.Name != null
                   && path.Name.StartsWith(PaintBucketFillElementPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// 在选择覆盖层上做填充块命中测试（按 z 序从上往下）。覆盖层铺满画布且位于
        /// inkCanvas 之上，填充块本身又是穿透的，所以必须在这里手动判定点击落在哪块填充上。
        /// </summary>
        private System.Windows.Shapes.Path FindPaintBucketFillAtCanvasPoint(Point canvasPoint)
        {
            if (inkCanvas == null) return null;

            for (int i = inkCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (inkCanvas.Children[i] is not System.Windows.Shapes.Path path) continue;
                if (!IsPaintBucketFillElement(path)) continue;
                if (PaintFillContainsCanvasPoint(path, canvasPoint)) return path;
            }

            return null;
        }

        /// <summary>画布坐标点是否落在填充路径内部（考虑元素偏移与渲染变换）。</summary>
        private static bool PaintFillContainsCanvasPoint(System.Windows.Shapes.Path path, Point canvasPoint)
        {
            var geometry = path.Data;
            if (geometry == null) return false;

            var offsetX = InkCanvas.GetLeft(path);
            var offsetY = InkCanvas.GetTop(path);
            if (double.IsNaN(offsetX)) offsetX = 0;
            if (double.IsNaN(offsetY)) offsetY = 0;

            var local = new Point(canvasPoint.X - offsetX, canvasPoint.Y - offsetY);

            if (path.RenderTransform is Transform transform)
            {
                var matrix = transform.Value;
                if (!matrix.HasInverse) return false;
                matrix.Invert();
                local = matrix.Transform(local);
            }

            try
            {
                return geometry.FillContains(local);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 按当前工具模式刷新填充块的命中测试：只有选择模式下允许命中（于是能被选中、
        /// 拖动与缩放），其余模式下保持穿透，否则填充块会挡住其覆盖范围内的书写与擦除。
        /// </summary>
        private void UpdatePaintFillHitTest()
        {
            if (inkCanvas == null) return;

            var hitTestable = inkCanvas.EditingMode == InkCanvasEditingMode.Select;
            foreach (UIElement child in inkCanvas.Children)
            {
                if (child is FrameworkElement element && IsPaintBucketFillElement(element))
                    element.IsHitTestVisible = hitTestable;
            }
        }

        #region 填充块多选（与笔迹套索联动）

        /// <summary>当前一起被选中的填充块（套索多选或单点选中都汇总到这里）。</summary>
        private readonly List<FrameworkElement> _selectedPaintFills = new List<FrameworkElement>();

        /// <summary>选中态描边（填充块本身没有边框，选中时临时加一圈细线作反馈）。</summary>
        private static readonly Brush PaintFillSelectionStroke = CreatePaintFillSelectionStroke();

        private static Brush CreatePaintFillSelectionStroke()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
            brush.Freeze();
            return brush;
        }

        /// <summary>当前选中的填充块集合（空表示没有填充块参与选中）。</summary>
        internal IReadOnlyList<FrameworkElement> GetSelectedPaintFills()
        {
            // 撤销 / 切页等操作可能已把元素从画布移除，这里顺手剔除失效引用
            if (_selectedPaintFills.Count > 0 && inkCanvas != null)
                _selectedPaintFills.RemoveAll(fill => fill == null || !inkCanvas.Children.Contains(fill));

            return _selectedPaintFills;
        }

        /// <summary>是否有填充块参与当前选中。</summary>
        private bool HasPaintFillSelection => _selectedPaintFills.Count > 0;

        /// <summary>切换填充块的选中描边。</summary>
        private static void SetPaintFillSelectionVisual(FrameworkElement fill, bool selected)
        {
            if (fill is not System.Windows.Shapes.Path path) return;
            if (selected)
            {
                path.Stroke = PaintFillSelectionStroke;
                path.StrokeThickness = 1.5;
            }
            else
            {
                path.Stroke = null;
                path.StrokeThickness = 0;
            }
        }

        /// <summary>把一块填充加入选中集合（重复加入会被忽略）。</summary>
        internal void AddPaintFillToSelection(FrameworkElement fill)
        {
            if (fill == null || !IsPaintBucketFillElement(fill)) return;
            if (_selectedPaintFills.Contains(fill)) return;

            _selectedPaintFills.Add(fill);
            SetPaintFillSelectionVisual(fill, true);
        }

        /// <summary>清空填充块选中集合（并撤掉选中描边）。</summary>
        internal void ClearPaintFillSelection()
        {
            foreach (var fill in _selectedPaintFills) SetPaintFillSelectionVisual(fill, false);
            _selectedPaintFills.Clear();
        }

        /// <summary>只选中这一块填充（点选路径）。</summary>
        internal void SelectSinglePaintFill(FrameworkElement fill)
        {
            ClearPaintFillSelection();
            AddPaintFillToSelection(fill);
        }

        /// <summary>填充块在画布坐标系下的包围盒（考虑布局偏移与渲染变换）。</summary>
        private Rect GetPaintFillCanvasBounds(FrameworkElement fill)
        {
            if (fill is not System.Windows.Shapes.Path path || path.Data == null) return Rect.Empty;

            try
            {
                var local = path.Data.Bounds;
                if (local.IsEmpty) return Rect.Empty;
                return path.TransformToAncestor(inkCanvas).TransformBounds(local);
            }
            catch
            {
                return GetElementActualBounds(fill);
            }
        }

        /// <summary>按位移平移一块填充（改布局偏移，保留已有渲染变换）。</summary>
        private void MovePaintFillByDelta(FrameworkElement fill, Vector delta)
        {
            if (fill == null) return;

            var left = InkCanvas.GetLeft(fill);
            var top = InkCanvas.GetTop(fill);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            InkCanvas.SetLeft(fill, left + delta.X);
            InkCanvas.SetTop(fill, top + delta.Y);
        }

        /// <summary>选中填充块的合并包围盒。</summary>
        private Rect GetPaintFillSelectionBounds()
        {
            var bounds = Rect.Empty;
            foreach (var fill in _selectedPaintFills)
            {
                var fillBounds = GetPaintFillCanvasBounds(fill);
                if (fillBounds.IsEmpty) continue;
                bounds.Union(fillBounds);
            }
            return bounds;
        }

        /// <summary>
        /// 套索/框选后同步填充块选中：把与当前墨迹选区相交的填充块也纳入选中集合。
        /// 纯点选填充块的路径不走这里（由 <see cref="SelectSinglePaintFill"/> 维护）。
        /// </summary>
        internal void SyncPaintFillSelectionFromStrokeSelection()
        {
            if (inkCanvas == null) return;

            var strokes = inkCanvas.GetSelectedStrokes();
            if (strokes == null || strokes.Count == 0)
            {
                ClearPaintFillSelection();
                return;
            }

            var selectionBounds = inkCanvas.GetSelectionBounds();
            if (selectionBounds.IsEmpty)
            {
                ClearPaintFillSelection();
                return;
            }

            ClearPaintFillSelection();
            foreach (var child in inkCanvas.Children.OfType<FrameworkElement>())
            {
                if (!IsPaintBucketFillElement(child)) continue;
                var fillBounds = GetPaintFillCanvasBounds(child);
                if (fillBounds.IsEmpty) continue;
                if (!fillBounds.IntersectsWith(selectionBounds)) continue;
                AddPaintFillToSelection(child);
            }

            if (_selectedPaintFills.Count > 0) RefreshPaintFillSelectionChrome();
        }

        /// <summary>多选时刷新选中框与工具栏位置：只保留一套工具栏，摆在合并包围盒下方。</summary>
        private void RefreshPaintFillSelectionChrome()
        {
            if (GridInkCanvasSelectionCover == null) return;

            SetStrokeSelectionBarPaintFillMode(true);
            GridInkCanvasSelectionCover.Visibility = Visibility.Visible;

            var strokeCount = inkCanvas.GetSelectedStrokes().Count;

            // 单块填充且没有笔迹：沿用元素那套「四角缩放 + 旋转」手柄
            if (_selectedPaintFills.Count == 1 && strokeCount == 0)
            {
                ShowImageResizeHandles(_selectedPaintFills[0]);
                UpdateStrokeSelectionBarLocationForPaintFill(_selectedPaintFills[0]);
                return;
            }

            // 多选（或与笔迹混合）：用笔迹那套「选区框 + 旋转手柄」表达整体选区，
            // 否则多选时既没有框也没有旋转手柄，用户无法旋转
            var bounds = GetPaintFillSelectionBounds();
            if (strokeCount > 0)
            {
                var strokeBounds = inkCanvas.GetSelectionBounds();
                if (!strokeBounds.IsEmpty)
                    bounds = bounds.IsEmpty ? strokeBounds : Rect.Union(bounds, strokeBounds);
            }

            if (bounds.IsEmpty) return;

            const double expandOffset = 8;
            SelectionRectangle.Visibility = Visibility.Visible;
            SelectionRectangle.Margin = new Thickness(bounds.Left - expandOffset, bounds.Top - expandOffset, 0, 0);
            SelectionRectangle.Width = bounds.Width + expandOffset * 2;
            SelectionRectangle.Height = bounds.Height + expandOffset * 2;

            ShowStrokeRotationHandle(bounds);
            UpdateStrokeSelectionBarLocationForBounds(bounds);
        }

        #endregion

        #region 填充块复用「选中对象工具栏」

        /// <summary>
        /// 选中对象工具栏上的按钮对填充块与笔迹一视同仁，全部保持可用：
        /// 每个按钮的 handler 内部通过 IsPaintBucketFillElement 分流到元素操作
        /// （克隆、克隆到新白板、插入白板、沿轴翻转、旋转、透明度/颜色、删除）。
        /// 这里负责在切换选中对象类型时把可能残留的置灰状态还原。
        /// </summary>
        private void SetStrokeSelectionBarPaintFillMode(bool paintFillMode)
        {
            var buttons = new FrameworkElement[]
            {
                BorderStrokeSelectionCloneToNewBoard,
                BorderStrokeSelectionInsertToWhiteboard,
                BorderStrokeSelectionFlip,
                BorderStrokeSelectionPenWidth,
                BorderStrokeSelectionColor
            };

            foreach (var element in buttons)
            {
                if (element == null) continue;
                element.IsEnabled = true;
                element.Opacity = 1.0;
            }
        }

        /// <summary>
        /// 选中填充块时显示「选中对象工具栏」（与笔迹同一套），只隐藏选择框与缩放手柄
        /// （填充块没有选中笔迹，无需墨迹选区框）。
        /// </summary>
        private void ShowStrokeSelectionBarForPaintFill(FrameworkElement fill)
        {
            if (fill == null || GridInkCanvasSelectionCover == null) return;

            SetStrokeSelectionBarPaintFillMode(true);

            // 填充块没有墨迹选区，隐藏墨迹选择框，但保留元素那套缩放/旋转控制手柄
            SelectionRectangle.Visibility = Visibility.Collapsed;
            SelectionHandlesCanvas.Visibility = Visibility.Collapsed;
            HideCoordinateAxisHandles();
            HideGeometryLineHandles();
            ShowImageResizeHandles(fill);

            GridInkCanvasSelectionCover.Visibility = Visibility.Visible;
            UpdateStrokeSelectionBarLocationForPaintFill(fill);
        }

        /// <summary>取消填充块选中时恢复按钮可用性并收起工具栏。</summary>
        private void HideStrokeSelectionBarForPaintFill()
        {
            SetStrokeSelectionBarPaintFillMode(false);
            if (GridInkCanvasSelectionCover != null)
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
        }

        /// <summary>把选中对象工具栏摆到填充块下方（下方放不下时摆到上方）。</summary>
        private void UpdateStrokeSelectionBarLocationForPaintFill(FrameworkElement fill)
        {
            if (fill == null) return;
            UpdateStrokeSelectionBarLocationForBounds(GetElementActualBounds(fill));
        }

        /// <summary>把选中对象工具栏摆到给定包围盒下方（下方放不下时摆到上方）。</summary>
        private void UpdateStrokeSelectionBarLocationForBounds(Rect bounds)
        {
            if (BorderStrokeSelectionControl == null || bounds.IsEmpty) return;

            var borderLeft = (bounds.Left + bounds.Right - BorderStrokeSelectionControlWidth) / 2;
            var borderTop = bounds.Bottom + 10;

            if (double.IsNaN(borderLeft)) borderLeft = 0;
            if (double.IsNaN(borderTop)) borderTop = 0;
            if (borderLeft < 0) borderLeft = 0;
            if (borderTop < 0) borderTop = 0;
            if (Width - borderLeft < BorderStrokeSelectionControlWidth)
                borderLeft = Math.Max(0, Width - BorderStrokeSelectionControlWidth);
            if (Height - borderTop < BorderStrokeSelectionControlHeight)
                borderTop = Math.Max(10, bounds.Top - BorderStrokeSelectionControlHeight - 10);

            BorderStrokeSelectionControl.Margin = new Thickness(borderLeft, borderTop, 0, 0);
        }

        /// <summary>删除当前选中的全部填充块（写入元素删除历史，可撤销）。</summary>
        private void DeleteSelectedPaintFill()
        {
            if (inkCanvas == null) return;
            var targets = GetSelectedPaintFills().ToList();
            if (targets.Count == 0) return;
            if (TryBlockFrozenPageMutation("删除填充")) return;

            currentSelectedElement = null;
            HideStrokeSelectionBarForPaintFill();
            HideImageResizeHandles();
            ClearPaintFillSelection();

            int removed = 0;
            foreach (var fill in targets)
            {
                try
                {
                    if (inkCanvas.Children.Contains(fill)) inkCanvas.Children.Remove(fill);
                    timeMachine.CommitElementRemoveHistory(fill);
                    removed++;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"[油漆桶] 删除填充失败: {ex.Message}", LogHelper.LogType.Error);
                }
            }

            PaintBucketTrace($"已删除 {removed} 块填充");
        }

        /// <summary>
        /// 按给定角度旋转当前选中的全部填充块（与笔迹旋转按钮同一入口）。
        /// pivotCanvas 为旋转中心，必须与笔迹用同一个点，否则填充块只会在原地自转、不会绕着选区转。
        /// </summary>
        private void RotateSelectedPaintFill(double angleDegrees, Point? pivotCanvas = null)
        {
            var targets = GetSelectedPaintFills().ToList();
            if (targets.Count == 0) return;
            if (TryBlockFrozenPageMutation("旋转填充")) return;

            foreach (var fill in targets) ApplyRotateTransform(fill, angleDegrees, pivotCanvas);

            if (targets.Count == 1) UpdateStrokeSelectionBarLocationForPaintFill(targets[0]);
            else UpdateStrokeSelectionBarLocationForBounds(GetPaintFillSelectionBounds());
        }

        /// <summary>克隆当前选中的填充块（偏移 16px，写入元素插入历史，可撤销）。</summary>
        private void CloneSelectedPaintFill()
        {
            if (inkCanvas == null) return;
            var targets = GetSelectedPaintFills().ToList();
            if (targets.Count == 0) return;
            if (TryBlockFrozenPageMutation("克隆填充")) return;

            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    // 多选时逐块错开，避免副本彼此完全重叠
                    var offset = 16 * (i + 1);
                    PlacePaintFillClone(CreatePaintFillClone((System.Windows.Shapes.Path)targets[i], offset, offset));
                }
                PaintBucketTrace($"已克隆 {targets.Count} 块填充");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[油漆桶] 克隆填充失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>复制一块填充（不加入画布），offset 为画布坐标偏移。</summary>
        private System.Windows.Shapes.Path CreatePaintFillClone(System.Windows.Shapes.Path source, double offsetX, double offsetY)
        {
            var clone = new System.Windows.Shapes.Path
            {
                Data = source.Data,
                Fill = source.Fill == null ? null : (Brush)source.Fill.Clone(),
                Stretch = Stretch.None,
                IsHitTestVisible = false,
                Focusable = false,
                Name = PaintBucketFillElementPrefix + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")
            };

            // 复制源元素的渲染变换，副本的位置、缩放与旋转才能和源保持一致
            if (source.RenderTransform is TransformGroup sourceTransformGroup)
            {
                clone.RenderTransform = (TransformGroup)sourceTransformGroup.Clone();
            }
            else
            {
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(1, 1));
                transformGroup.Children.Add(new TranslateTransform(0, 0));
                transformGroup.Children.Add(new RotateTransform(0));
                clone.RenderTransform = transformGroup;
            }

            var left = InkCanvas.GetLeft(source);
            var top = InkCanvas.GetTop(source);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            InkCanvas.SetLeft(clone, left + offsetX);
            InkCanvas.SetTop(clone, top + offsetY);

            return clone;
        }

        /// <summary>把填充副本加入当前画布：绑定事件、按模式收敛命中测试、写入撤销历史。</summary>
        private void PlacePaintFillClone(System.Windows.Shapes.Path clone)
        {
            if (clone == null || inkCanvas == null) return;

            inkCanvas.Children.Add(clone);
            BindElementEvents(clone);
            UpdatePaintFillHitTest();
            timeMachine.CommitElementInsertHistory(clone);
        }

        /// <summary>克隆到新白板页（先新建页，再把副本放到新页画布上）。</summary>
        private void CloneSelectedPaintFillToNewBoard(bool includeStrokes = false)
        {
            var targets = GetSelectedPaintFills().ToList();
            var strokes = includeStrokes && inkCanvas != null ? inkCanvas.GetSelectedStrokes().Clone() : null;
            if (targets.Count == 0 && (strokes == null || strokes.Count == 0)) return;
            if (TryBlockFrozenPageMutation("克隆填充到新页面")) return;

            try
            {
                BtnWhiteBoardAdd_Click(null, null);
                foreach (var fill in targets)
                    PlacePaintFillClone(CreatePaintFillClone((System.Windows.Shapes.Path)fill, 0, 0));

                // 混合选中：同时选中的笔迹也复制到同一个新页面
                if (strokes != null && strokes.Count > 0)
                {
                    inkCanvas.Strokes.Add(strokes);
                    timeMachine.CommitStrokeUserInputHistory(strokes);
                }

                PaintBucketTrace($"已克隆 {targets.Count} 块填充（+{strokes?.Count ?? 0} 笔迹）到新页面");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[油漆桶] 克隆填充到新页面失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>把填充块插入常规白板（必要时先切到白板模式）。</summary>
        private void InsertSelectedPaintFillToWhiteboard(bool includeStrokes = false)
        {
            var targets = GetSelectedPaintFills().ToList();
            var strokes = includeStrokes && inkCanvas != null ? inkCanvas.GetSelectedStrokes().Clone() : null;
            if (targets.Count == 0 && (strokes == null || strokes.Count == 0)) return;
            if (TryBlockFrozenPageMutation("插入填充到白板")) return;

            try
            {
                var clones = targets.Select(fill => CreatePaintFillClone((System.Windows.Shapes.Path)fill, 0, 0)).ToList();

                if (currentMode != 1)
                {
                    ImageBlackboard_MouseUp(null, null);
                }

                foreach (var clone in clones) PlacePaintFillClone(clone);

                // 混合选中：同时选中的笔迹也插入白板
                if (strokes != null && strokes.Count > 0)
                {
                    inkCanvas.Select(new System.Windows.Ink.StrokeCollection());
                    inkCanvas.Strokes.Add(strokes);
                    timeMachine.CommitStrokeUserInputHistory(strokes);
                }

                PaintBucketTrace($"已把 {targets.Count} 块填充（+{strokes?.Count ?? 0} 笔迹）插入白板");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[油漆桶] 插入填充到白板失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>把快捷调色盘的颜色应用到全部选中的填充块（保留各自原有不透明度）。</summary>
        private void ApplyColorToSelectedPaintFill(Color color)
        {
            var targets = GetSelectedPaintFills().ToList();
            if (targets.Count == 0) return;
            if (TryBlockFrozenPageMutation("修改填充颜色")) return;

            foreach (var fill in targets.OfType<System.Windows.Shapes.Path>())
            {
                var alpha = (fill.Fill as SolidColorBrush)?.Color.A ?? PaintBucketFillAlpha;
                fill.Fill = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            }
        }

        /// <summary>当前选中填充块的颜色（多选时取第一块，供快捷调色盘回显勾选状态）。</summary>
        private Color? GetSelectedPaintFillColor()
        {
            var fill = GetSelectedPaintFills().FirstOrDefault() as System.Windows.Shapes.Path;
            if (fill == null) return null;
            return (fill.Fill as SolidColorBrush)?.Color;
        }

        /// <summary>
        /// 选中栏的「粗细」按钮对填充块的映射：纯色块没有笔宽概念，改为调整填充不透明度。
        /// reset 为 true 时恢复默认半透明。
        /// </summary>
        private void ChangeSelectedPaintFillOpacity(double multiplier, bool reset)
        {
            var targets = GetSelectedPaintFills().OfType<System.Windows.Shapes.Path>().ToList();
            if (targets.Count == 0) return;
            if (TryBlockFrozenPageMutation("修改填充透明度")) return;

            foreach (var fill in targets)
            {
                if (fill.Fill is not SolidColorBrush brush) continue;

                byte alpha = reset
                    ? PaintBucketFillAlpha
                    : (byte)Math.Max(20, Math.Min(255, Math.Round(brush.Color.A * multiplier)));

                fill.Fill = new SolidColorBrush(Color.FromArgb(alpha, brush.Color.R, brush.Color.G, brush.Color.B));
            }
        }

        /// <summary>沿给定轴线镜像一块填充，生成副本（原图保留，写入撤销历史）。</summary>
        private void MirrorPaintFillElement(System.Windows.Shapes.Path source, Point axisPoint, Vector axisDirection)
        {
            if (source == null || inkCanvas == null) return;

            try
            {
                var matrix = CreateAxisReflectionMatrix(axisPoint, axisDirection);
                var clone = CreatePaintFillClone(source, 0, 0);

                var left = InkCanvas.GetLeft(clone);
                var top = InkCanvas.GetTop(clone);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                // 镜像矩阵按画布坐标计算，而元素先走自身 RenderTransform、再由布局偏移搬到画布上，
                // 所以整条链必须是：源变换 → 平移(left,top) → 镜像 → 平移(-left,-top)，
                // 这样“局部坐标 → 画布坐标 → 镜像 → 回到局部坐标”。
                // 用 TransformGroup 的子项顺序表达，避免再手写矩阵乘法：
                // 早先的两端平移方向写反（先 T(-l,-t) 再 T(l,t)），等价于以 (-l,-t) 为原点镜像，
                // 副本被整体推到画布外约 1460px（看起来像「没生成」）。
                var baseTransform = clone.RenderTransform;
                var mirrorChain = new TransformGroup();
                if (baseTransform != null) mirrorChain.Children.Add(baseTransform);
                mirrorChain.Children.Add(new TranslateTransform(left, top));
                mirrorChain.Children.Add(new MatrixTransform(matrix));
                mirrorChain.Children.Add(new TranslateTransform(-left, -top));
                clone.RenderTransform = mirrorChain;

                PlacePaintFillClone(clone);
                PaintBucketTrace("已沿轴镜像一块填充");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[油漆桶] 沿轴镜像填充失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>填充块默认不透明度对应的 alpha（与创建填充时一致）。</summary>
        private static byte PaintBucketFillAlpha => (byte)Math.Round(255 * PaintBucketFillOpacity);

        #endregion

        /// <summary>对填充路径应用与笔迹一致的矩阵变换（双指缩放 / 画布漫游）。</summary>
        private static void ApplyMatrixTransformToPaintFill(System.Windows.Shapes.Path path, Matrix matrix)
        {
            if (!(path.RenderTransform is TransformGroup transformGroup))
            {
                transformGroup = new TransformGroup();
                path.RenderTransform = transformGroup;
            }

            // 双指缩放/漫游会逐帧调用这里。原来每帧追加一个 MatrixTransform，变换子项会无限增长
            // （一次缩放几百帧就是几百个矩阵），渲染越来越慢；这里直接把增量合并进末尾那个矩阵。
            if (transformGroup.Children.Count > 0
                && transformGroup.Children[transformGroup.Children.Count - 1] is MatrixTransform last)
            {
                var merged = last.Matrix;
                merged *= matrix;
                last.Matrix = merged;
                return;
            }

            transformGroup.Children.Add(new MatrixTransform(matrix));
        }

        #endregion
    }
}
