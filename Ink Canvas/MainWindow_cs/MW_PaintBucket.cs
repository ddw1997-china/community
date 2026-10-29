using Ink_Canvas.Controls;
using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
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

        /// <summary>对填充路径应用与笔迹一致的矩阵变换（双指缩放 / 画布漫游）。</summary>
        private static void ApplyMatrixTransformToPaintFill(System.Windows.Shapes.Path path, Matrix matrix)
        {
            if (!(path.RenderTransform is TransformGroup transformGroup))
            {
                transformGroup = new TransformGroup();
                path.RenderTransform = transformGroup;
            }

            transformGroup.Children.Add(new MatrixTransform(matrix));
        }

        #endregion
    }
}
