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
    /// <summary>
    /// 希沃风格直尺工具覆盖层：半透明矩形尺身 + 上沿刻度 + 两端旋转标记 + 两端伸缩手柄。
    /// 交互：拖尺身任意位置 = 整体平移；拖两端旋转标记 = 绕尺中心旋转；
    /// 拖两端伸缩手柄 = 拉伸/缩短尺长（另一端固定），刻度、旋转热区与移动热区同步更新。
    /// 关闭按钮位于尺身正中间，随尺移动/旋转一起跟随。
    /// 关闭策略与三角板一致：仅点击关闭按钮、退出批注模式、退出白板模式时关闭，其余情况一直保留。
    /// </summary>
    public class StraightRulerTool : WpfCanvas
    {
        public event EventHandler CloseRequested;

        private enum DragKind { None, Move, Rotate, ExtendLeft, ExtendRight, Pivot }

        // 刻度单位与三角板一致（1 单位 = 45px）；默认尺长与三角板刻度边等长（10√2 单位 ≈ 636px），
        // 这样三把尺的刻度段数与数字完全一致
        private const double UnitPx = 45;
        private const double RulerLength = 10 * UnitPx * 1.4142135623730951;
        private const double MinRulerLength = 320;   // 最短：至少容纳两端热区 + 中间移动区
        private const double MaxRulerLength = 2400;  // 最长：覆盖大屏
        private const double RulerWidth = 90;        // 尺宽（接近真实直尺的宽长比）
        private const double EndZoneRadius = 28;     // 两端旋转热区半径
        private const double EndMarkInset = 50;      // 旋转标记/热区距尺端的内缩量（避开伸缩手柄）
        private const double ExtendHandleWidth = 22; // 端部伸缩手柄宽度
        private const double ExtendHandleHeight = 56;// 端部伸缩手柄视觉高度
        private const double ScaleLong = 15;         // 长刻度线长
        private const double ScaleMid = 9;           // 中刻度线长
        private const double PivotZoneSize = 30;       // 旋转中心点拖动热区尺寸
        private const double PivotAvoidPadding = 6;    // 中心点与关闭按钮之间的避让间距

        private static readonly Color PivotColor = Color.FromArgb(0xFF, 0xE0, 0x7A, 0x1B); // 旋转中心点（橙色）

        private static readonly Color SurfaceColor = Color.FromArgb(0xC8, 0xCA, 0xD1, 0xE4); // 尺身底色 #CAD1E4（半透明）
        private static readonly Color EdgeColor = Color.FromArgb(0x59, 0x2B, 0x2B, 0x2B);    // 尺边描边
        private static readonly Color ScaleColor = Color.FromArgb(0xFF, 0x11, 0x11, 0x11);   // 刻度线与数字（纯黑）
        private static readonly Color MarkColor = Color.FromArgb(0xB4, 0x2B, 0x2B, 0x2B);    // 旋转标记

        private readonly WpfCanvas _root = CreateRoot();          // 承载尺身/刻度/热区，整体随尺旋转
        private readonly Button _closeBtn = CreateSmallButton();

        // 以下元素只创建一次，长度变化时仅更新尺寸/位置，
        // 避免拖拽过程中重建导致鼠标捕获丢失、伸缩手柄拖到一半中断。
        private readonly Rectangle _body;
        private readonly WpfCanvas _scaleHost;
        private readonly Rectangle _moveZone;
        private readonly WpfCanvas _leftRotateMark;
        private readonly WpfCanvas _rightRotateMark;
        private readonly Ellipse _leftRotateZone;
        private readonly Ellipse _rightRotateZone;
        private readonly WpfCanvas _leftExtendMark;
        private readonly WpfCanvas _rightExtendMark;
        private readonly Rectangle _leftExtendZone;
        private readonly Rectangle _rightExtendZone;
        private readonly WpfCanvas _pivotMark; // 旋转中心点视觉
        private readonly Ellipse _pivotZone;   // 旋转中心点拖动热区

        private Point _pivotLocal;      // 旋转中心（尺本地坐标，始终限制在尺身内）
        private Point _center;          // 尺中心（覆盖层坐标）
        private double _rotationDeg;    // 尺旋转角（度，屏幕顺时针为正）
        private double _rulerLength = RulerLength;
        private DragKind _drag = DragKind.None;
        private Point _dragLast;
        private double _lastRotateAngleDeg;

        public StraightRulerTool()
        {
            Background = null; // 未命中热区时触摸/鼠标穿透

            _closeBtn.Content = "✕";
            _closeBtn.Click += (s, e) => { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; };

            Children.Add(_root);

            // 尺身
            _body = new Rectangle
            {
                Width = _rulerLength,
                Height = RulerWidth,
                RadiusX = 6,
                RadiusY = 6,
                Fill = new SolidColorBrush(SurfaceColor),
                Stroke = new SolidColorBrush(EdgeColor),
                StrokeThickness = 2,
                IsHitTestVisible = false
            };
            _root.Children.Add(_body);

            // 上沿刻度（长度变化时重绘）
            _scaleHost = new WpfCanvas { IsHitTestVisible = false };
            _root.Children.Add(_scaleHost);

            // 移动热区：整块尺身（先加，位于旋转/伸缩热区下层）
            _moveZone = new Rectangle
            {
                Width = _rulerLength,
                Height = RulerWidth,
                RadiusX = 6,
                RadiusY = 6,
                Fill = Brushes.Transparent,
                Cursor = Cursors.SizeAll
            };
            AttachDrag(_moveZone, DragKind.Move);
            _root.Children.Add(_moveZone);

            // 两端旋转标记 + 旋转热区
            _leftRotateMark = CreateRotateMark();
            _rightRotateMark = CreateRotateMark();
            _root.Children.Add(_leftRotateMark);
            _root.Children.Add(_rightRotateMark);

            _leftRotateZone = CreateRotateZone();
            _rightRotateZone = CreateRotateZone();
            AttachDrag(_leftRotateZone, DragKind.Rotate);
            AttachDrag(_rightRotateZone, DragKind.Rotate);
            _root.Children.Add(_leftRotateZone);
            _root.Children.Add(_rightRotateZone);

            // 两端伸缩手柄 + 伸缩热区（最上层，避免被移动热区覆盖）
            _leftExtendMark = CreateExtendHandleMark();
            _rightExtendMark = CreateExtendHandleMark();
            _root.Children.Add(_leftExtendMark);
            _root.Children.Add(_rightExtendMark);

            _leftExtendZone = CreateExtendZone();
            _rightExtendZone = CreateExtendZone();
            AttachDrag(_leftExtendZone, DragKind.ExtendLeft);
            AttachDrag(_rightExtendZone, DragKind.ExtendRight);
            _root.Children.Add(_leftExtendZone);
            _root.Children.Add(_rightExtendZone);

            // 旋转中心点（最上层，拖它绕该点旋转尺子）
            _pivotMark = CreatePivotMark();
            _pivotZone = new Ellipse
            {
                Width = PivotZoneSize,
                Height = PivotZoneSize,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Cross
            };
            AttachDrag(_pivotZone, DragKind.Pivot);
            _root.Children.Add(_pivotMark);
            _root.Children.Add(_pivotZone);

            Children.Add(_closeBtn);

            _pivotLocal = new Point(_rulerLength / 2, RulerWidth - 16);
            UpdateRulerGeometry();
        }

        /// <summary>显示直尺并复位到主屏中央（水平摆放，刻度数字正立可读，长度恢复默认）</summary>
        public void ShowAtCenter()
        {
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
            _rotationDeg = 0;
            _rulerLength = RulerLength;
            // 中心点默认放在尺身中下部，避开关闭按钮（按钮占据尺中心）
            _pivotLocal = new Point(_rulerLength / 2, RulerWidth - 16);
            _drag = DragKind.None;
            UpdateRulerGeometry();
        }

        /// <summary>
        /// 按当前长度重建刻度，并同步尺身、移动热区、两端旋转标记/热区、两端伸缩手柄/热区的位置。
        /// </summary>
        private void UpdateRulerGeometry()
        {
            double length = _rulerLength;
            double midY = RulerWidth / 2;

            _root.Width = length;
            _root.Height = RulerWidth;
            _body.Width = length;
            _moveZone.Width = length;

            // 刻度：整尺长度对应刻度段数，长度变化后数字与刻度线一起重排
            _scaleHost.Children.Clear();
            BuildScale(_scaleHost, new Point(0, 0), new Point(length, 0), new Point(length / 2, RulerWidth));

            // 两端旋转标记与旋转热区
            SetCenter(_leftRotateMark, new Point(EndMarkInset, midY));
            SetCenter(_rightRotateMark, new Point(length - EndMarkInset, midY));
            SetCenter(_leftRotateZone, new Point(EndMarkInset, midY));
            SetCenter(_rightRotateZone, new Point(length - EndMarkInset, midY));

            // 两端伸缩手柄与伸缩热区
            SetCenter(_leftExtendMark, new Point(ExtendHandleWidth / 2, midY));
            SetCenter(_rightExtendMark, new Point(length - ExtendHandleWidth / 2, midY));
            SetCanvas(_leftExtendZone, 0, 0);
            SetCanvas(_rightExtendZone, length - ExtendHandleWidth, 0);

            // 旋转中心点：可到尺身边缘线，并自动避开关闭按钮
            _pivotLocal = ClampToRuler(_pivotLocal);
            UpdatePivotVisual();

            UpdateVisual();
        }

        private void UpdatePivotVisual()
        {
            SetCenter(_pivotMark, _pivotLocal);
            SetCenter(_pivotZone, _pivotLocal);
        }

        private void UpdateVisual()
        {
            _root.Width = _rulerLength;

            // 绕用户指定的旋转中心点旋转（本地坐标，不依赖布局尺寸）
            var rotate = (RotateTransform)_root.RenderTransform;
            rotate.CenterX = _pivotLocal.X;
            rotate.CenterY = _pivotLocal.Y;
            rotate.Angle = _rotationDeg;

            SetCanvas(_root, _center.X - _rulerLength / 2, _center.Y - RulerWidth / 2);

            // 关闭按钮固定在尺身几何中心，随尺绕旋转中心点一起转动
            SetCenter(_closeBtn, LocalToOverlay(new Point(_rulerLength / 2, RulerWidth / 2)));
        }

        /// <summary>尺本地坐标 → 覆盖层坐标（以旋转中心为原点按当前旋转角映射）</summary>
        private Point LocalToOverlay(Point local)
        {
            var origin = PivotOverlayPoint;
            double rad = _rotationDeg * Math.PI / 180;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double dx = local.X - _pivotLocal.X;
            double dy = local.Y - _pivotLocal.Y;
            return new Point(origin.X + dx * cos - dy * sin,
                             origin.Y + dx * sin + dy * cos);
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

                case DragKind.ExtendLeft:
                case DragKind.ExtendRight:
                {
                    HandleExtend(pos, _drag == DragKind.ExtendRight);
                    break;
                }

                case DragKind.Pivot:
                {
                    MovePivotTo(ClampToRuler(OverlayToLocal(pos)));
                    break;
                }
            }
        }

        /// <summary>旋转中心点的屏幕坐标（本身是旋转轴，不随旋转移动）</summary>
        private Point PivotOverlayPoint =>
            new Point(_center.X - _rulerLength / 2 + _pivotLocal.X,
                      _center.Y - RulerWidth / 2 + _pivotLocal.Y);

        /// <summary>覆盖层坐标 → 尺本地坐标（以旋转中心为原点撤销旋转）</summary>
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

        /// <summary>
        /// 把点限制在尺身范围内（中心点可以落在边缘线上），并自动避开关闭按钮区域。
        /// </summary>
        private Point ClampToRuler(Point local)
        {
            var p = new Point(
                Math.Max(0, Math.Min(_rulerLength, local.X)),
                Math.Max(0, Math.Min(RulerWidth, local.Y)));
            return AvoidCloseButton(p);
        }

        /// <summary>中心点位于关闭按钮矩形内时，沿最近方向推到按钮外</summary>
        private Point AvoidCloseButton(Point local)
        {
            double cx = _rulerLength / 2;
            double cy = RulerWidth / 2;
            double halfW = 30 + PivotAvoidPadding;
            double halfH = 15 + PivotAvoidPadding;

            double dx = local.X - cx;
            double dy = local.Y - cy;
            if (Math.Abs(dx) >= halfW || Math.Abs(dy) >= halfH) return local;

            double toLeft = dx + halfW;
            double toRight = halfW - dx;
            double toTop = dy + halfH;
            double toBottom = halfH - dy;
            double min = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));

            if (min == toLeft) return new Point(cx - halfW, local.Y);
            if (min == toRight) return new Point(cx + halfW, local.Y);
            if (min == toTop) return new Point(local.X, cy - halfH);
            return new Point(local.X, cy + halfH);
        }

        /// <summary>
        /// 移动旋转中心点：改变旋转中心后补偿尺子左上角位置，
        /// 保证尺子在屏幕上的位置不跳变。
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

        /// <summary>
        /// 拖某一端伸缩：该端跟随鼠标在尺轴上的投影，另一端固定不动。
        /// </summary>
        private void HandleExtend(Point pos, bool extendRight)
        {
            var u = UnitVector(_rotationDeg);
            double half = _rulerLength / 2;

            // 固定端（世界坐标）
            var fixedEnd = extendRight
                ? new Point(_center.X - u.X * half, _center.Y - u.Y * half)
                : new Point(_center.X + u.X * half, _center.Y + u.Y * half);

            // 鼠标位置在尺轴方向上的投影 = 新的整尺长度
            double projected = extendRight
                ? (pos.X - fixedEnd.X) * u.X + (pos.Y - fixedEnd.Y) * u.Y
                : (fixedEnd.X - pos.X) * u.X + (fixedEnd.Y - pos.Y) * u.Y;

            double newLength = Math.Max(MinRulerLength, Math.Min(MaxRulerLength, projected));
            if (Math.Abs(newLength - _rulerLength) < 0.5) return;

            _rulerLength = newLength;
            _center = extendRight
                ? new Point(fixedEnd.X + u.X * newLength / 2, fixedEnd.Y + u.Y * newLength / 2)
                : new Point(fixedEnd.X - u.X * newLength / 2, fixedEnd.Y - u.Y * newLength / 2);

            UpdateRulerGeometry();
        }

        /// <summary>
        /// 屏幕角度（度）：与 WPF RotateTransform 的顺时针方向一致，保证尺随鼠标同向旋转
        /// </summary>
        private static double ScreenAngle(Vector v)
        {
            return Math.Atan2(v.Y, v.X) * 180 / Math.PI;
        }

        /// <summary>与 RotateTransform 同向（顺时针为正）的单位向量</summary>
        private static Point UnitVector(double degrees)
        {
            double rad = degrees * Math.PI / 180;
            return new Point(Math.Cos(rad), Math.Sin(rad));
        }

        #endregion

        #region 视觉元素工厂

        private static WpfCanvas CreateRoot()
        {
            return new WpfCanvas
            {
                Background = null, // 空白处不命中，仅热区可命中
                // 旋转中心由 UpdateVisual 写入 RotateTransform.Center，避免依赖布局尺寸
                RenderTransform = new RotateTransform(0)
            };
        }

        /// <summary>
        /// 沿一条边铺刻度：每 1 单位长刻度（内侧标数字），每 0.5 单位中刻度
        /// </summary>
        private static void BuildScale(WpfCanvas host, Point from, Point to, Point interior)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;

            var ux = dx / len;
            var uy = dy / len;
            var inward = InwardNormal(from, to, interior);
            var scaleBrush = new SolidColorBrush(ScaleColor);

            // 数字基线沿 from→to（即数值递增方向）排布，与实物一致
            var textAngle = Math.Atan2(uy, ux) * 180 / Math.PI;

            var units = (int)Math.Floor(len / UnitPx);
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

        /// <summary>边的指向尺身内部的单位法线</summary>
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

        private static Ellipse CreateRotateZone()
        {
            return new Ellipse
            {
                Width = EndZoneRadius * 2,
                Height = EndZoneRadius * 2,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
        }

        private static Rectangle CreateExtendZone()
        {
            return new Rectangle
            {
                Width = ExtendHandleWidth,
                Height = RulerWidth,
                Fill = Brushes.Transparent,
                Cursor = Cursors.SizeWE
            };
        }

        /// <summary>端部伸缩手柄：圆角握把 + 两道竖向抓取纹，提示"此处可拉伸"</summary>
        private static WpfCanvas CreateExtendHandleMark()
        {
            var brush = new SolidColorBrush(MarkColor);
            var root = new WpfCanvas
            {
                Width = ExtendHandleWidth,
                Height = ExtendHandleHeight,
                IsHitTestVisible = false
            };

            root.Children.Add(new Rectangle
            {
                Width = ExtendHandleWidth,
                Height = ExtendHandleHeight,
                RadiusX = 8,
                RadiusY = 8,
                Fill = new SolidColorBrush(Color.FromArgb(0x46, 0xFF, 0xFF, 0xFF)),
                Stroke = new SolidColorBrush(EdgeColor),
                StrokeThickness = 1.4
            });

            for (var i = 0; i < 2; i++)
            {
                double x = ExtendHandleWidth / 2 + (i == 0 ? -3.5 : 3.5);
                root.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = ExtendHandleHeight / 2 - 10,
                    X2 = x,
                    Y2 = ExtendHandleHeight / 2 + 10,
                    Stroke = brush,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }

            return root;
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
            const double r = 13;
            const double pad = 6;
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
                StrokeThickness = 1.8,
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
                    new Point(end.X + t.X * 5.4, end.Y + t.Y * 5.4),
                    new Point(end.X + n.X * 3.4 + t.X * 1.2, end.Y + n.Y * 3.4 + t.Y * 1.2),
                    new Point(end.X - n.X * 3.4 + t.X * 1.2, end.Y - n.Y * 3.4 + t.Y * 1.2)
                },
                Fill = brush
            });

            return root;
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
