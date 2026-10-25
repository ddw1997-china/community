using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

// 本工程与 InkCanvas.Controls 库存在同名命名空间，为避免 Canvas 类型解析歧义，显式取 WPF 的 Canvas
using WpfCanvas = System.Windows.Controls.Canvas;

namespace Ink_Canvas.Controls
{
    /// <summary>三角板类型</summary>
    public enum TriangleRulerKind
    {
        /// <summary>45°-45°-90° 等腰直角三角形</summary>
        IsoscelesRight = 0,

        /// <summary>30°-60°-90° 直角三角形</summary>
        ThirtySixty = 1
    }

    /// <summary>
    /// 希沃风格三角板工具覆盖层：半透明镂空板身（中间为空）+ 斜边刻度 + 三个角上的旋转标记。
    /// 交互：拖板框（三条边的整个区域）= 整体平移；拖三个角（旋转标记处）= 绕板中心旋转；
    /// 关闭按钮位于板中央的空白（镂空）区域。
    /// 板身的镂空部分不参与命中，可以直接在板上书写；两种形状各自由图形面板上的独立按钮开关，
    /// 可与另一块三角板同时显示。
    /// </summary>
    public class TriangleRulerTool : WpfCanvas
    {
        public event EventHandler CloseRequested;

        private enum DragKind { None, Move, Rotate, Pivot }

        // 虚拟刻度单位：1 单位 = 45px（约合真实三角板 8mm 一格），斜边共 10 单位
        private const double UnitPx = 45;
        // 带刻度的一边（斜边）两种三角板等长：45° 板直角边 450 → 斜边 450√2 ≈ 636
        private const double Hypotenuse = 10 * UnitPx * 1.4142135623730951;
        private static readonly double Leg45 = Hypotenuse / Math.Sqrt(2);            // 45° 板直角边 ≈450
        private static readonly double LegLong30 = Hypotenuse * Math.Sqrt(3) / 2;     // 30-60 板长直角边 ≈551
        private static readonly double LegShort30 = Hypotenuse / 2;                   // 30-60 板短直角边 ≈318
        private const double FrameWidth = 45;       // 板框宽度（中间镂空，与真实三角板一致）
        private const double CornerZoneRadius = 30; // 角旋转热区半径
        private const double CornerMarkInset = 22;  // 角标记/热区向板内缩进
        private const double ScaleLong = 15;        // 长刻度线长
        private const double ScaleMid = 9;          // 中刻度线长
        private const double PivotZoneSize = 30;      // 旋转中心点拖动热区尺寸
        private const double PivotAvoidPadding = 6;   // 中心点与关闭按钮之间的避让间距

        private static readonly Color SurfaceColor = Color.FromArgb(0xC8, 0xCA, 0xD1, 0xE4); // 板身底色 #CAD1E4（半透明）
        private static readonly Color EdgeColor = Color.FromArgb(0x59, 0x2B, 0x2B, 0x2B);    // 板边描边
        private static readonly Color ScaleColor = Color.FromArgb(0xFF, 0x11, 0x11, 0x11);   // 刻度线与数字（纯黑，与真实三角板一致）
        private static readonly Color MarkColor = Color.FromArgb(0xB4, 0x2B, 0x2B, 0x2B);    // 旋转标记
        private static readonly Color PivotColor = Color.FromArgb(0xFF, 0xE0, 0x7A, 0x1B);   // 旋转中心点（橙色）

        private readonly WpfCanvas _root = CreateRoot();          // 承载板身/刻度/热区，整体随板旋转
        private readonly Button _closeBtn = CreateSmallButton();

        private TriangleRulerKind _kind = TriangleRulerKind.IsoscelesRight;
        private Point _center;          // 板中心（覆盖层坐标）
        private Point _incenter;        // 内心（板本地坐标）：镂空区的中心，关闭按钮锚点
        private double _rotationDeg;    // 板旋转角（度，屏幕顺时针为正）
        private DragKind _drag = DragKind.None;
        private Point _dragLast;
        private double _lastRotateAngleDeg;

        // 三角形本地顶点（直角顶点 O / 右上 A / 左下 B），用于把旋转中心限制在板内
        private Point _vO, _vA, _vB;
        private Point _pivotLocal;      // 旋转中心（板本地坐标）
        private WpfCanvas _pivotMark;   // 旋转中心点视觉
        private Ellipse _pivotZone;     // 旋转中心点拖动热区

        public TriangleRulerTool()
        {
            Background = null; // 未命中热区时触摸/鼠标穿透

            _closeBtn.Content = "✕";
            _closeBtn.Click += (s, e) => { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; };

            Children.Add(_root);
            Children.Add(_closeBtn);
        }

        /// <summary>板宽（水平直角边）</summary>
        private double RulerWidth => _kind == TriangleRulerKind.IsoscelesRight ? Leg45 : LegShort30;

        /// <summary>板高（竖直直角边）</summary>
        private double RulerHeight => _kind == TriangleRulerKind.IsoscelesRight ? Leg45 : LegLong30;

        /// <summary>显示指定类型的三角板，并复位到主屏中央</summary>
        public void ShowAtCenter(TriangleRulerKind kind)
        {
            _kind = kind;

            // 折叠状态下 ActualWidth/Height 为 0，回退到宿主窗口尺寸
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0)
            {
                var win = Window.GetWindow(this);
                if (win != null) { w = win.ActualWidth; h = win.ActualHeight; }
            }
            if (w <= 0) w = 1920;
            if (h <= 0) h = 1080;

            _center = new Point(w / 2, h / 2);
            // 初始摆放：斜边转到上方呈水平、板身落在下方，此时斜边刻度数字正好正立可读
            // （与真实三角板一致），旋转角即斜边方向的反向角。
            _rotationDeg = -Math.Atan2(RulerHeight, -RulerWidth) * 180 / Math.PI;
            _drag = DragKind.None;
            _pivotLocal = DefaultPivotLocal();
            RebuildRuler();
            UpdateVisual();
        }

        /// <summary>
        /// 按当前类型重建板身、斜边刻度、旋转标记与热区（仅在显示时执行）
        /// </summary>
        private void RebuildRuler()
        {
            _root.Children.Clear();
            _root.Width = RulerWidth;
            _root.Height = RulerHeight;

            // 本地坐标：直角顶点 O 在左上，A 在右上，B 在左下
            var o = new Point(0, 0);
            var a = new Point(RulerWidth, 0);
            var b = new Point(0, RulerHeight);

            // 内心 = 板框等宽内缩的相似中心，同时是镂空空白区的中心（关闭按钮锚点）
            _incenter = ComputeInnerTriangle(o, a, b, out var io, out var ia, out var ib);

            // 板身：外三角 + 内缩三角（EvenOdd）构成镂空板框，中间为空，可穿透书写
            _root.Children.Add(new Path
            {
                Data = BuildHollowFrame(o, a, b, io, ia, ib),
                Fill = new SolidColorBrush(SurfaceColor),
                Stroke = new SolidColorBrush(EdgeColor),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            });

            // 刻度：仅斜边，刻度线与数字一律朝板内部方向排布，数字标记到 13 为止
            var scaleHost = new WpfCanvas { IsHitTestVisible = false };
            BuildScale(scaleHost, a, b, _incenter, 13);
            _root.Children.Add(scaleHost);

            // 三个角上的旋转标记
            var interior = new Point((o.X + a.X + b.X) / 3, (o.Y + a.Y + b.Y) / 3);
            foreach (var corner in new[] { o, a, b })
            {
                var mark = CreateRotateMark();
                SetCenter(mark, PointTowards(corner, interior, CornerMarkInset));
                _root.Children.Add(mark);
            }

            // 板框热区（先加，位于下层：拖板框任意位置 = 整体移动）
            AddFrameZone(o, a, b, io, ia, ib);

            // 角旋转热区（后加，位于上层：拖角 = 旋转）
            AddCornerZone(o, interior);
            AddCornerZone(a, interior);
            AddCornerZone(b, interior);

            // 旋转中心点（最后加，最上层：拖它改变绕哪个点旋转）
            _vO = o;
            _vA = a;
            _vB = b;
            _pivotLocal = ClampPivot(_pivotLocal);
            _pivotMark = CreatePivotMark();
            _pivotZone = new Ellipse
            {
                Width = PivotZoneSize,
                Height = PivotZoneSize,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Cross
            };
            SetCenter(_pivotMark, _pivotLocal);
            SetCenter(_pivotZone, _pivotLocal);
            AttachDrag(_pivotZone, DragKind.Pivot);
            _root.Children.Add(_pivotMark);
            _root.Children.Add(_pivotZone);
        }

        private void UpdateVisual()
        {
            SetCenter(_root, _center);

            // 绕用户指定的旋转中心点旋转
            var rotate = (RotateTransform)_root.RenderTransform;
            rotate.CenterX = _pivotLocal.X;
            rotate.CenterY = _pivotLocal.Y;
            rotate.Angle = _rotationDeg;

            // 关闭按钮落在板中央的镂空空白区，随板的移动/旋转一起跟随
            SetCenter(_closeBtn, LocalPointToOverlay(_incenter));
        }

        /// <summary>板本地坐标 → 覆盖层坐标（以旋转中心点为原点按当前旋转角映射）</summary>
        private Point LocalPointToOverlay(Point local)
        {
            double dx = local.X - _pivotLocal.X;
            double dy = local.Y - _pivotLocal.Y;
            var rad = _rotationDeg * Math.PI / 180;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            return new Point(_center.X - RulerWidth / 2 + _pivotLocal.X + dx * cos - dy * sin,
                             _center.Y - RulerHeight / 2 + _pivotLocal.Y + dx * sin + dy * cos);
        }

        #region 拖拽交互

        private void AttachDrag(FrameworkElement el, DragKind kind)
        {
            el.MouseLeftButtonDown += (s, e) =>
            {
                if (_drag != DragKind.None) return;
                BeginDrag(kind, e.GetPosition(this));
                el.CaptureMouse();
                e.Handled = true;
            };
            el.MouseMove += (s, e) =>
            {
                if (_drag != kind || !el.IsMouseCaptured) return;
                HandleMove(e.GetPosition(this));
                e.Handled = true;
            };
            el.MouseLeftButtonUp += (s, e) =>
            {
                if (_drag != kind) return;
                _drag = DragKind.None;
                el.ReleaseMouseCapture();
                e.Handled = true;
            };

            el.TouchDown += (s, e) =>
            {
                if (_drag != DragKind.None) return;
                BeginDrag(kind, e.GetTouchPoint(this).Position);
                el.CaptureTouch(e.TouchDevice);
                e.Handled = true;
            };
            el.TouchMove += (s, e) =>
            {
                if (_drag != kind || !el.AreAnyTouchesCaptured) return;
                HandleMove(e.GetTouchPoint(this).Position);
                e.Handled = true;
            };
            el.TouchUp += (s, e) =>
            {
                if (_drag != kind) return;
                _drag = DragKind.None;
                el.ReleaseTouchCapture(e.TouchDevice);
                e.Handled = true;
            };
        }

        private void BeginDrag(DragKind kind, Point pos)
        {
            _drag = kind;
            _dragLast = pos;
            _lastRotateAngleDeg = ScreenAngle(pos - PivotOverlayPoint);
        }

        private void HandleMove(Point pos)
        {
            switch (_drag)
            {
                case DragKind.Move:
                {
                    var delta = pos - _dragLast;
                    _center = new Point(_center.X + delta.X, _center.Y + delta.Y);
                    _dragLast = pos;
                    UpdateVisual();
                    break;
                }

                case DragKind.Rotate:
                {
                    // 以旋转中心点为轴，光标相对该点的角度差即旋转增量
                    var d = pos - PivotOverlayPoint;
                    if (d.Length < 24) break; // 离中心太近角度不稳定
                    var angle = ScreenAngle(d);
                    var delta = ((angle - _lastRotateAngleDeg + 540) % 360) - 180;
                    _lastRotateAngleDeg = angle;
                    if (Math.Abs(delta) < 0.01) break;
                    _rotationDeg += delta;
                    UpdateVisual();
                    break;
                }

                case DragKind.Pivot:
                {
                    MovePivotTo(ClampPivot(OverlayToLocal(pos)));
                    break;
                }
            }
        }

        /// <summary>旋转中心点的屏幕坐标（随板旋转）</summary>
        private Point PivotOverlayPoint => LocalPointToOverlay(_pivotLocal);

        /// <summary>覆盖层坐标 → 板本地坐标（以旋转中心为原点撤销旋转）</summary>
        private Point OverlayToLocal(Point overlay)
        {
            var origin = PivotOverlayPoint;
            double rad = -_rotationDeg * Math.PI / 180;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double dx = overlay.X - origin.X;
            double dy = overlay.Y - origin.Y;
            return new Point(_pivotLocal.X + dx * cos - dy * sin,
                             _pivotLocal.Y + dx * sin + dy * cos);
        }

        /// <summary>默认旋转中心：重心向远离关闭按钮（内心）的方向偏移，保证初始即可拖动</summary>
        private Point DefaultPivotLocal()
        {
            var o = new Point(0, 0);
            var a = new Point(RulerWidth, 0);
            var b = new Point(0, RulerHeight);
            var centroid = new Point((o.X + a.X + b.X) / 3, (o.Y + a.Y + b.Y) / 3);
            var incenter = ComputeInnerTriangle(o, a, b, out _, out _, out _);
            var pivot = PointTowards(centroid, incenter, -70);
            return ClampToTriangle(pivot, o, a, b, 0);
        }

        /// <summary>
        /// 把旋转中心点限制在三角形内（可落在边缘线上），并自动避开关闭按钮区域。
        /// </summary>
        private Point ClampPivot(Point local)
        {
            var p = ClampToTriangle(local, _vO, _vA, _vB, 0);

            double cx = _incenter.X;
            double cy = _incenter.Y;
            double halfW = 30 + PivotAvoidPadding;
            double halfH = 15 + PivotAvoidPadding;
            double dx = p.X - cx;
            double dy = p.Y - cy;

            if (Math.Abs(dx) < halfW && Math.Abs(dy) < halfH)
            {
                double toLeft = dx + halfW;
                double toRight = halfW - dx;
                double toTop = dy + halfH;
                double toBottom = halfH - dy;
                double min = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));

                if (min == toLeft) p = new Point(cx - halfW, p.Y);
                else if (min == toRight) p = new Point(cx + halfW, p.Y);
                else if (min == toTop) p = new Point(p.X, cy - halfH);
                else p = new Point(p.X, cy + halfH);
            }

            return ClampToTriangle(p, _vO, _vA, _vB, 0);
        }

        /// <summary>
        /// 把点限制在三角形内（重心坐标 clamp：点到第 i 条边的距离 = λi × 该边上的高，
        /// 因此 λi 的下限为 margin / hi）。
        /// </summary>
        private static Point ClampToTriangle(Point p, Point o, Point a, Point b, double margin)
        {
            double den = (a.Y - b.Y) * (o.X - b.X) + (b.X - a.X) * (o.Y - b.Y);
            if (Math.Abs(den) < 0.001) return p;

            double lO = ((a.Y - b.Y) * (p.X - b.X) + (b.X - a.X) * (p.Y - b.Y)) / den;
            double lA = ((b.Y - o.Y) * (p.X - b.X) + (o.X - b.X) * (p.Y - b.Y)) / den;
            double lB = 1 - lO - lA;

            double area2 = Math.Abs(den);
            double minO = margin * Distance(a, b) / area2;
            double minA = margin * Distance(b, o) / area2;
            double minB = margin * Distance(o, a) / area2;

            lO = Math.Max(lO, minO);
            lA = Math.Max(lA, minA);
            lB = Math.Max(lB, minB);

            double sum = lO + lA + lB;
            if (sum <= 0.0001) return p;

            lO /= sum;
            lA /= sum;
            lB /= sum;
            return new Point(lO * o.X + lA * a.X + lB * b.X,
                             lO * o.Y + lA * a.Y + lB * b.Y);
        }

        private static double Distance(Point p1, Point p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 移动旋转中心点：补偿板的左上角位置，保证板在屏幕上的位置不跳变。
        /// </summary>
        private void MovePivotTo(Point newLocal)
        {
            if (Math.Abs(newLocal.X - _pivotLocal.X) < 0.01 &&
                Math.Abs(newLocal.Y - _pivotLocal.Y) < 0.01)
            {
                return;
            }

            double ddx = newLocal.X - _pivotLocal.X;
            double ddy = newLocal.Y - _pivotLocal.Y;
            double rad = _rotationDeg * Math.PI / 180;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double rx = ddx * cos - ddy * sin;
            double ry = ddx * sin + ddy * cos;

            _center = new Point(_center.X - ddx + rx, _center.Y - ddy + ry);
            _pivotLocal = newLocal;
            UpdatePivotVisual();
            UpdateVisual();
        }

        private void UpdatePivotVisual()
        {
            SetCenter(_pivotMark, _pivotLocal);
            SetCenter(_pivotZone, _pivotLocal);
        }

        /// <summary>
        /// 屏幕角度（度）：与 WPF RotateTransform 的顺时针方向一致，保证板随鼠标同向旋转
        /// </summary>
        private static double ScreenAngle(Vector v)
        {
            return Math.Atan2(v.Y, v.X) * 180 / Math.PI;
        }

        #endregion

        #region 视觉元素工厂

        private static WpfCanvas CreateRoot()
        {
            return new WpfCanvas
            {
                Background = null, // 空白处不命中，仅热区可命中
                // 旋转中心由 UpdateVisual 写入 RotateTransform.Center（绕用户指定的中心点）
                RenderTransform = new RotateTransform(0)
            };
        }

        /// <summary>
        /// 镂空板框：外三角向内偏移 FrameWidth 得到内三角，EvenOdd 只填充板框，中间为空
        /// </summary>
        private static StreamGeometry BuildHollowFrame(Point o, Point a, Point b, Point io, Point ia, Point ib)
        {
            var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(o, true, true);
                ctx.LineTo(a, true, false);
                ctx.LineTo(b, true, false);

                ctx.BeginFigure(io, true, true);
                ctx.LineTo(ia, true, false);
                ctx.LineTo(ib, true, false);
            }
            return geo;
        }

        /// <summary>
        /// 求内心（板框等宽内缩的相似中心，也是镂空空白区的中心）与内缩后的内三角顶点
        /// </summary>
        private static Point ComputeInnerTriangle(Point o, Point a, Point b,
            out Point io, out Point ia, out Point ib)
        {
            // 内心为相似中心时，各边内缩量相等（等于板框宽度）
            var la = (b - a).Length;
            var lb = (o - b).Length;
            var lc = (a - o).Length;
            var sum = la + lb + lc;
            var incenter = new Point(
                (la * o.X + lb * a.X + lc * b.X) / sum,
                (la * o.Y + lb * a.Y + lc * b.Y) / sum);

            var area = Math.Abs((a.X - o.X) * (b.Y - o.Y) - (b.X - o.X) * (a.Y - o.Y)) / 2;
            var inradius = 2 * area / sum;
            var scale = Math.Max(0.2, 1 - FrameWidth / inradius);

            Point Inner(Point p) => new Point(
                incenter.X + (p.X - incenter.X) * scale,
                incenter.Y + (p.Y - incenter.Y) * scale);

            io = Inner(o);
            ia = Inner(a);
            ib = Inner(b);
            return incenter;
        }

        /// <summary>
        /// 沿一条边铺刻度：每 1 单位长刻度（内侧标数字），每 0.5 单位中刻度。
        /// maxUnits 限制数字与刻度标记的最大单位数（三角板只需标到 13，末端留白）。
        /// </summary>
        private static void BuildScale(WpfCanvas host, Point from, Point to, Point interior,
                                       int maxUnits = int.MaxValue)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;

            var ux = dx / len;
            var uy = dy / len;
            var inward = InwardNormal(from, to, interior);
            var scaleBrush = new SolidColorBrush(ScaleColor);

            // 数字基线沿 from→to（即数值递增方向）排布：把斜边转到上方水平、板身朝下时，
            // 数字恰好正立可读，与真实三角板一致（不可再按 ±90° 归一化，否则数字会倒置）
            var textAngle = Math.Atan2(uy, ux) * 180 / Math.PI;

            var units = (int)Math.Floor(len / UnitPx);
            if (units > maxUnits) units = maxUnits;
            for (var i = 1; i <= units * 2; i++)
            {
                var dist = i * UnitPx / 2;
                if (dist > len - 1) break;

                var isLong = i % 2 == 0;
                var tickLen = isLong ? ScaleLong : ScaleMid;
                var p0 = new Point(from.X + ux * dist, from.Y + uy * dist);

                host.Children.Add(new Line
                {
                    X1 = p0.X,
                    Y1 = p0.Y,
                    X2 = p0.X + inward.X * tickLen,
                    Y2 = p0.Y + inward.Y * tickLen,
                    Stroke = scaleBrush,
                    StrokeThickness = isLong ? 1.3 : 1.0
                });

                if (!isLong) continue;

                var label = new TextBlock
                {
                    Text = (i / 2).ToString(CultureInfo.InvariantCulture),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = scaleBrush,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(textAngle)
                };
                // 此处尚未参与布局，ActualWidth 为 0，先测量拿到尺寸再居中
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = label.DesiredSize;
                SetCanvas(label,
                    p0.X + inward.X * (ScaleLong + 7) - size.Width / 2,
                    p0.Y + inward.Y * (ScaleLong + 7) - size.Height / 2);
                host.Children.Add(label);
            }
        }

        /// <summary>边的指向三角形内部的单位法线</summary>
        private static Point InwardNormal(Point from, Point to, Point interior)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) return new Point(0, 1);

            var nx = -dy / len;
            var ny = dx / len;
            var midX = (from.X + to.X) / 2;
            var midY = (from.Y + to.Y) / 2;
            // 与"中点指向内部"同向者即为向内法线
            return (nx * (interior.X - midX) + ny * (interior.Y - midY)) >= 0
                ? new Point(nx, ny)
                : new Point(-nx, -ny);
        }

        /// <summary>旋转中心点：橙色圆环 + 中心实点 + 四向短十字，提示"拖动它改变旋转中心"</summary>
        private static WpfCanvas CreatePivotMark()
        {
            const double size = 20;
            const double c = size / 2;
            var brush = new SolidColorBrush(PivotColor);
            var root = new WpfCanvas
            {
                Width = size,
                Height = size,
                IsHitTestVisible = false
            };

            var ring = new Ellipse
            {
                Width = 14,
                Height = 14,
                Stroke = brush,
                StrokeThickness = 1.4,
                Fill = new SolidColorBrush(Color.FromArgb(0x2E, 0xE0, 0x7A, 0x1B))
            };
            SetCanvas(ring, c - 7, c - 7);
            root.Children.Add(ring);

            for (var i = 0; i < 4; i++)
            {
                var rad = i * Math.PI / 2;
                root.Children.Add(new Line
                {
                    X1 = c + Math.Cos(rad) * 4,
                    Y1 = c + Math.Sin(rad) * 4,
                    X2 = c + Math.Cos(rad) * 9,
                    Y2 = c + Math.Sin(rad) * 9,
                    Stroke = brush,
                    StrokeThickness = 1.2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }

            var dot = new Ellipse { Width = 4, Height = 4, Fill = brush };
            SetCanvas(dot, c - 2, c - 2);
            root.Children.Add(dot);

            return root;
        }

        /// <summary>旋转标记：约 280° 的圆弧 + 箭头，提示"此处可旋转"</summary>
        private static WpfCanvas CreateRotateMark()
        {
            // 尺寸较原版减半（13/6 → 6.5/3），避免在三角板锐角处超出板身区域
            const double r = 6.5;
            const double pad = 3;
            var brush = new SolidColorBrush(MarkColor);
            var root = new WpfCanvas
            {
                Width = (r + pad) * 2,
                Height = (r + pad) * 2,
                IsHitTestVisible = false
            };
            var cx = r + pad;
            var cy = r + pad;

            Point OnArc(double deg)
            {
                var rad = deg * Math.PI / 180;
                return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
            }

            // 圆弧：自 110° 顺时针绕到 30°（约 280°）
            var arc = new StreamGeometry();
            using (var ctx = arc.Open())
            {
                ctx.BeginFigure(OnArc(110), false, false);
                ctx.ArcTo(OnArc(30), new Size(r, r), 0, true, SweepDirection.Clockwise, true, false);
            }
            root.Children.Add(new Path
            {
                Data = arc,
                Stroke = brush,
                StrokeThickness = 1.0,
                Fill = null,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });

            // 箭头：沿顺时针切线方向，落在圆弧终点外侧
            var end = OnArc(30);
            var t = new Point(-Math.Sin(30 * Math.PI / 180), Math.Cos(30 * Math.PI / 180));
            var n = new Point(-t.Y, t.X);
            root.Children.Add(new Polygon
            {
                Points = new PointCollection
                {
                    new Point(end.X + t.X * 2.7, end.Y + t.Y * 2.7),
                    new Point(end.X + n.X * 1.7 + t.X * 0.6, end.Y + n.Y * 1.7 + t.Y * 0.6),
                    new Point(end.X - n.X * 1.7 + t.X * 0.6, end.Y - n.Y * 1.7 + t.Y * 0.6)
                },
                Fill = brush
            });

            return root;
        }

        /// <summary>
        /// 板框移动热区：透明镂空三角环，覆盖三条边的整个板框区域（而非仅边线），拖动即整体移动。
        /// 中间镂空部分不命中，保证仍可在板上空白处书写。
        /// </summary>
        private void AddFrameZone(Point o, Point a, Point b, Point io, Point ia, Point ib)
        {
            var zone = new Path
            {
                Data = BuildHollowFrame(o, a, b, io, ia, ib),
                Fill = Brushes.Transparent,
                Stroke = null,
                Cursor = Cursors.SizeAll
            };
            AttachDrag(zone, DragKind.Move);
            _root.Children.Add(zone);
        }

        /// <summary>角旋转热区：透明圆，覆盖角点与旋转标记，拖动绕板中心旋转</summary>
        private void AddCornerZone(Point corner, Point centroid)
        {
            var zone = new Ellipse
            {
                Width = CornerZoneRadius * 2,
                Height = CornerZoneRadius * 2,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            SetCenter(zone, PointTowards(corner, centroid, CornerMarkInset));
            AttachDrag(zone, DragKind.Rotate);
            _root.Children.Add(zone);
        }

        private static Button CreateSmallButton()
        {
            return new Button
            {
                Width = 60,
                Height = 30,
                FontSize = 14,
                Padding = new Thickness(4, 0, 4, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
                Content = ""
            };
        }

        #endregion

        #region 布局辅助

        /// <summary>从 from 沿 from→to 方向前进 dist 后的点</summary>
        private static Point PointTowards(Point from, Point to, double dist)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) return from;
            return new Point(from.X + dx / len * dist, from.Y + dy / len * dist);
        }

        private static void SetCenter(FrameworkElement el, Point center)
        {
            // 首次显示时尚未完成布局，ActualWidth/Height 可能为 0，回退到显式尺寸
            double w = el.ActualWidth > 0 ? el.ActualWidth : el.Width;
            double hgt = el.ActualHeight > 0 ? el.ActualHeight : el.Height;
            if (double.IsNaN(w)) w = 0;
            if (double.IsNaN(hgt)) hgt = 0;
            SetCanvas(el, center.X - w / 2, center.Y - hgt / 2);
        }

        private static void SetCanvas(FrameworkElement el, double left, double top)
        {
            WpfCanvas.SetLeft(el, left);
            WpfCanvas.SetTop(el, top);
        }

        #endregion
    }
}