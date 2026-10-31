using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

// 本工程与 InkCanvas.Controls 库存在同名命名空间，为避免 Canvas 类型解析歧义，显式取 WPF 的 Canvas
using WpfCanvas = System.Windows.Controls.Canvas;

namespace Ink_Canvas.Controls
{
    /// <summary>
    /// 圆规弧线绘制事件参数
    /// </summary>
    public class CompassArcEventArgs : EventArgs
    {
        /// <summary>圆心（针脚，画布坐标）</summary>
        public Point Pivot { get; set; }

        /// <summary>当前半径</summary>
        public double Radius { get; set; }

        /// <summary>起始角度（度，数学角：0=右，逆时针为正）</summary>
        public double FromAngleDeg { get; set; }

        /// <summary>结束角度（度）</summary>
        public double ToAngleDeg { get; set; }

        /// <summary>true=本次抓取的第一段（新建笔画），false=延续当前笔画</summary>
        public bool StartNewStroke { get; set; }
    }

    /// <summary>
    /// 希沃风格圆规工具覆盖层。
    /// 物理模型：两脚等长 L，铰接在顶部；针脚 N 为圆心，铅笔脚张开角 δ 决定半径 r=2L·sin(δ/2)。
    /// 交互：拖两条腿（针脚腿/铅笔腿）=整体移动（落笔关闭时）；落笔开启后按住铰接绕针脚整体旋转画弧；
    /// 拖笔尖=调整张角（半径），不出墨。画布背景为 null，未落在部件上的触摸/鼠标穿透到 inkCanvas。
    /// </summary>
    public class CompassTool : WpfCanvas
    {
        public event EventHandler<CompassArcEventArgs> ArcDrawing;
        public event EventHandler CloseRequested;

        /// <summary>开始拖动圆规（按下任一可拖部件）</summary>
        public event EventHandler DragStarted;

        /// <summary>结束拖动圆规（抬起或丢失捕获）</summary>
        public event EventHandler DragEnded;

        private enum DragKind { None, Move, Hinge, PencilTip }

        // 腿长（两脚等长）
        private const double LegLength = 260;
        private const double FootOverhang = 8;   // 腿移动热区在脚端的外扩量
        private const double MinSpreadDeg = 8;
        private const double MaxSpreadDeg = 150;

        // 整体缩放（+ / - 按钮）：只缩放圆规本体，按钮与半径文字保持原尺寸
        private const double MinScale = 0.5;
        private const double MaxScale = 2.0;
        private const double ScaleStep = 0.1;

        // 悬浮工具条：与圆规本体解耦，固定在屏幕上（不随圆规缩放/旋转移动）
        private const double BarMargin = 8;        // 距画布边缘的最小留白
        private const double BarEdgeGap = 16;      // 自动摆放时与圆规本体的间距

        // 线稿造型参数
        private const double HeadRadius = 35;       // 圆规头（大圆）半径
        private const double HeadHoleRadius = 8;    // 圆规头中心圆孔半径
        private const double LegStub = 26;          // 腿伸入圆规头内的长度（被头部遮住）
        private const double LegHalfWidth = 13.5;   // 腿杆根部半宽
        private const double LegTipHalfWidth = 5.5; // 腿杆末端（接针尖/被铅笔遮住处）半宽
        private const double GripNeckHeight = 14;   // 头与手柄之间的细颈
        private const double GripWidth = 27;        // 顶部滚花手柄宽
        private const double GripHeight = 36;       // 顶部滚花手柄高
        private const double PencilTiltDeg = 16;    // 铅笔相对腿方向外倾角（角度越大笔尾越容易与腿杆分离）
        private const double NeedleCollarEnd = 40;  // 腿杆末端（距脚尖，往下为脚尖/铅笔笔尖）

        // 铅笔分段（距笔尖的距离，总长约为原来的一半）：0 笔尖 → 6 笔芯尾 → 21 木锥尾 → 61 笔杆尾 → 75 笔尾
        private const double PencilLeadEnd = 6;
        private const double PencilConeEnd = 21;
        private const double PencilBandFrom = 40;   // 笔杆上的白色箍线（近笔尖侧）
        private const double PencilBandTo = 45;
        private const double PencilBarrelEnd = 61;
        private const double PencilEnd = 75;
        private const double PencilBarrelHalf = 13.5; // 笔杆半宽（与腿杆半宽相贴，保证贴合成一体）
        private const double PencilNubHalf = 15;    // 笔尾半宽（略宽于笔杆）
        private const double PencilLeadHalf = 3.5;  // 笔芯半宽

        private static readonly Color InkColor = Color.FromRgb(0x1A, 0x1A, 0x1A);
        private static readonly Color LegRed = Color.FromRgb(0xE1, 0x1E, 0x22); // 脚尖红（取自参考图）
        private static readonly Color PenBlue = Color.FromRgb(0x31, 0x8C, 0xDD); // 铅笔笔尖蓝（取自参考图）
        private static readonly Color PencilFill = Color.FromRgb(0x4A, 0x4A, 0x4A); // 铅笔主体深灰

        private Point _needle;          // 针脚（圆心）
        private double _phiDeg = 292.5; // 铰接→针脚方向（数学角）；270 + 张角/2 使手柄朝正上、铅笔脚在左侧
        private double _spreadDeg = 45; // 两脚张角
        private bool _drawMode;
        private DragKind _drag = DragKind.None;
        private Point _dragLast;
        private bool _strokeNeedsNew = true;

        private readonly Ellipse _guideCircle = CreateGuideCircle();
        private readonly Polygon _pivotLegBar = CreateNeedleLegBar();   // 针脚腿杆
        private readonly Polygon _pivotNeedleTip = CreateLegTip();      // 针脚腿脚尖（红）
        private readonly Polygon _pencilLegBar = CreateNeedleLegBar();  // 铅笔腿杆（与针脚腿同形，无脚尖，脚尖位置留给铅笔笔尖）
        private readonly Ellipse _headOuter = CreateHeadCircle(HeadRadius);
        private readonly Ellipse _headHole = CreateHeadCircle(HeadHoleRadius);
        private readonly Rectangle _gripNeck = CreateGripNeck();
        private readonly WpfCanvas _grip = CreateGrip();
        private readonly WpfCanvas _pencilBody;     // 线稿铅笔（笔身+木锥+笔芯），笔尖即出墨点
        private readonly Ellipse _pencilTipZone = CreateTipZone(); // 笔尖热区：调整半径
        private readonly Rectangle _pivotLegHitZone = CreateLegHitZone();  // 针脚腿热区：整体移动
        private readonly Rectangle _pencilLegHitZone = CreateLegHitZone(); // 铅笔腿热区：整体移动
        private readonly Rectangle _gripHitZone = CreateGripHitZone();     // 顶部手柄放大热区：旋转
        private readonly TextBlock _angleText = CreateAngleText();
        private readonly Button _drawBtn = CreateSmallButton();
        private readonly Button _closeBtn = CreateSmallButton();
        private readonly Button _zoomInBtn = CreateSmallButton();
        private readonly Button _zoomOutBtn = CreateSmallButton();

        /// <summary>圆规本体所在图层：整体缩放只作用于它，按钮/文字保持原尺寸</summary>
        private readonly WpfCanvas _visualHost = new WpfCanvas { Background = null };

        /// <summary>圆规本体缩放变换，锚点固定为针脚（圆心），缩放时圆心不动、半径按比例变化</summary>
        private readonly ScaleTransform _visualScale = new ScaleTransform(1, 1);

        /// <summary>当前整体缩放系数</summary>
        private double _scale = 1.0;

        // 悬浮工具条（拖动移动 / + / − / ✕）：独立于圆规本体，不随缩放与旋转变化
        private readonly Border _buttonBar;
        private Border _buttonBarGrip;
        private bool _buttonBarPositioned;
        private bool _buttonBarDragging;
        private Point _buttonBarDragStartPoint;
        private double _buttonBarDragStartLeft;
        private double _buttonBarDragStartTop;

        public CompassTool()
        {
            Background = null; // 未命中部件时触摸/鼠标穿透

            // 圆规头（大圆+中心孔）与顶部手柄均可拖动旋转，使用旋转光标提示
            _headOuter.Cursor = RotateCursor;
            _headHole.Cursor = RotateCursor;
            _gripNeck.Cursor = RotateCursor;
            _grip.Cursor = RotateCursor;
            _pencilBody = CreatePencil();

            AttachDrag(_headOuter, DragKind.Hinge);
            AttachDrag(_headHole, DragKind.Hinge);
            AttachDrag(_gripNeck, DragKind.Hinge);
            AttachDrag(_grip, DragKind.Hinge);
            AttachDrag(_gripHitZone, DragKind.Hinge);
            AttachDrag(_pivotLegHitZone, DragKind.Move);
            AttachDrag(_pencilLegHitZone, DragKind.Move);
            AttachDrag(_pencilTipZone, DragKind.PencilTip);

            _drawBtn.Content = Ink_Canvas.Properties.CanvasStrings.Canvas_CompassDraw;
            _drawBtn.Click += (s, e) => { SetDrawMode(!_drawMode); e.Handled = true; };
            _closeBtn.Content = "✕";
            _closeBtn.Click += (s, e) => { CloseRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; };

            _zoomInBtn.Content = "+";
            _zoomOutBtn.Content = "−";
            _zoomInBtn.ToolTip = "整体放大";
            _zoomOutBtn.ToolTip = "整体缩小";
            _zoomInBtn.Click += (s, e) => { ChangeScale(ScaleStep); e.Handled = true; };
            _zoomOutBtn.Click += (s, e) => { ChangeScale(-ScaleStep); e.Handled = true; };

            // 四个按钮统一收进悬浮工具条：尺寸一致、位置固定，不随圆规缩放与旋转移动
            ConfigureBarButton(_zoomOutBtn, 40);
            ConfigureBarButton(_zoomInBtn, 40);
            ConfigureBarButton(_drawBtn, 76);
            ConfigureBarButton(_closeBtn, 40);
            _buttonBar = CreateButtonBar(out _buttonBarGrip);
            AttachButtonBarDrag(_buttonBarGrip);

            // 圆规本体放进独立图层，缩放只影响它（工具条与半径文字不跟着放大缩小）
            _visualHost.RenderTransform = _visualScale;
            _visualHost.Children.Add(_guideCircle);
            _visualHost.Children.Add(_pivotLegBar);
            _visualHost.Children.Add(_pivotNeedleTip);
            _visualHost.Children.Add(_pencilLegBar);
            _visualHost.Children.Add(_pivotLegHitZone);
            _visualHost.Children.Add(_pencilLegHitZone);
            _visualHost.Children.Add(_headOuter);
            _visualHost.Children.Add(_headHole);
            _visualHost.Children.Add(_gripNeck);
            _visualHost.Children.Add(_grip);
            _visualHost.Children.Add(_gripHitZone);
            _visualHost.Children.Add(_pencilBody);
            _visualHost.Children.Add(_pencilTipZone);

            Children.Add(_visualHost);
            Children.Add(_angleText);
            Children.Add(_buttonBar);
            Panel.SetZIndex(_buttonBar, 10);
        }

        #region 悬浮工具条

        /// <summary>统一样式：工具条内的按钮固定尺寸，不随圆规整体缩放变化</summary>
        private static void ConfigureBarButton(Button button, double width)
        {
            button.Width = width;
            button.Height = 30;
            button.FontSize = 15;
            button.Padding = new Thickness(2, 0, 2, 0);
            button.Margin = new Thickness(2, 0, 2, 0);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Focusable = false;
        }

        /// <summary>创建悬浮工具条：拖动手柄 + 缩小 / 放大 / 移动画线 / 关闭</summary>
        private Border CreateButtonBar(out Border grip)
        {
            grip = CreateButtonBarGrip();

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(grip);
            panel.Children.Add(_zoomOutBtn);
            panel.Children.Add(_zoomInBtn);
            panel.Children.Add(_drawBtn);
            panel.Children.Add(_closeBtn);

            return new Border
            {
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                Background = ResolveBarBrush("FloatingBarBackgroundBrush", Brushes.White),
                BorderBrush = ResolveBarBrush("FloatingBarBorderBrush", new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))),
                Padding = new Thickness(4, 4, 4, 4),
                Child = panel,
                Visibility = Visibility.Visible
            };
        }

        /// <summary>取主题画刷，缺失时回退到默认色（控件可能在没有资源的宿主里被创建）</summary>
        private Brush ResolveBarBrush(string resourceKey, Brush fallback)
        {
            try
            {
                if (TryFindResource(resourceKey) is Brush brush) return brush;
            }
            catch
            {
                // 忽略资源查找失败
            }
            return fallback;
        }

        /// <summary>拖动手柄：两列三点，与选中工具栏一致的手感</summary>
        private Border CreateButtonBarGrip()
        {
            var dots = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var dotBrush = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78));
            for (int col = 0; col < 2; col++)
            {
                var column = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(col == 0 ? 0 : 3, 0, 0, 0)
                };
                for (int row = 0; row < 3; row++)
                {
                    column.Children.Add(new Ellipse
                    {
                        Width = 3,
                        Height = 3,
                        Margin = new Thickness(0, 1, 0, 1),
                        Fill = dotBrush
                    });
                }
                dots.Children.Add(column);
            }

            return new Border
            {
                Width = 26,
                Height = 30,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                ToolTip = "拖动工具条",
                Child = dots
            };
        }

        /// <summary>工具条拖动（鼠标 + 触摸），并限制在画布范围内</summary>
        private void AttachButtonBarDrag(FrameworkElement grip)
        {
            if (grip == null) return;

            grip.MouseLeftButtonDown += (s, e) =>
            {
                if (_buttonBarDragging) return;
                BeginButtonBarDrag(e.GetPosition(this));
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += (s, e) =>
            {
                if (!_buttonBarDragging || !grip.IsMouseCaptured) return;
                DragButtonBarTo(e.GetPosition(this));
                e.Handled = true;
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (!_buttonBarDragging) return;
                _buttonBarDragging = false;
                grip.ReleaseMouseCapture();
                e.Handled = true;
            };
            grip.LostMouseCapture += (s, e) => _buttonBarDragging = false;

            grip.TouchDown += (s, e) =>
            {
                if (_buttonBarDragging) return;
                BeginButtonBarDrag(e.GetTouchPoint(this).Position);
                grip.CaptureTouch(e.TouchDevice);
                e.Handled = true;
            };
            grip.TouchMove += (s, e) =>
            {
                if (!_buttonBarDragging || !grip.AreAnyTouchesCaptured) return;
                DragButtonBarTo(e.GetTouchPoint(this).Position);
                e.Handled = true;
            };
            grip.TouchUp += (s, e) =>
            {
                if (!_buttonBarDragging) return;
                _buttonBarDragging = false;
                grip.ReleaseTouchCapture(e.TouchDevice);
                e.Handled = true;
            };
            grip.LostTouchCapture += (s, e) => _buttonBarDragging = false;
        }

        private void BeginButtonBarDrag(Point position)
        {
            _buttonBarDragging = true;
            _buttonBarDragStartPoint = position;
            _buttonBarDragStartLeft = WpfCanvas.GetLeft(_buttonBar);
            _buttonBarDragStartTop = WpfCanvas.GetTop(_buttonBar);
            if (double.IsNaN(_buttonBarDragStartLeft)) _buttonBarDragStartLeft = 0;
            if (double.IsNaN(_buttonBarDragStartTop)) _buttonBarDragStartTop = 0;
        }

        private void DragButtonBarTo(Point position)
        {
            var delta = position - _buttonBarDragStartPoint;
            var size = GetButtonBarSize();
            SetCanvas(_buttonBar,
                ClampToCanvas(_buttonBarDragStartLeft + delta.X, size.Width, ActualWidth),
                ClampToCanvas(_buttonBarDragStartTop + delta.Y, size.Height, ActualHeight));
        }

        private Size GetButtonBarSize()
        {
            if (_buttonBar == null) return new Size(260, 40);

            double width = _buttonBar.ActualWidth > 0 ? _buttonBar.ActualWidth : _buttonBar.DesiredSize.Width;
            double height = _buttonBar.ActualHeight > 0 ? _buttonBar.ActualHeight : _buttonBar.DesiredSize.Height;
            if (width <= 0 || double.IsNaN(width)) width = 260;
            if (height <= 0 || double.IsNaN(height)) height = 40;
            return new Size(width, height);
        }

        private static double ClampToCanvas(double value, double elementSize, double canvasSize)
        {
            if (canvasSize <= 0) canvasSize = 1920;
            var max = Math.Max(BarMargin, canvasSize - elementSize - BarMargin);
            if (double.IsNaN(value) || value < BarMargin) return BarMargin;
            return Math.Min(value, max);
        }

        /// <summary>首次显示时把工具条摆到圆规上方；之后保留用户拖动的位置</summary>
        private void PositionButtonBarIfNeeded()
        {
            if (_buttonBarPositioned || _buttonBar == null) return;

            _buttonBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = GetButtonBarSize();

            var hingeRoot = ToRoot(Hinge);
            var left = hingeRoot.X - size.Width / 2;
            var top = hingeRoot.Y - (HeadRadius + GripNeckHeight + GripHeight) * _scale - size.Height - BarEdgeGap;

            SetCanvas(_buttonBar,
                ClampToCanvas(left, size.Width, ActualWidth),
                ClampToCanvas(top, size.Height, ActualHeight));
            _buttonBarPositioned = true;
        }

        #endregion

        /// <summary>当前整体缩放系数（1.0 = 原始尺寸）</summary>
        public double Scale => _scale;

        /// <summary>整体放大/缩小一档：锚点为针脚，圆心不动、半径按比例变化</summary>
        private void ChangeScale(double delta)
        {
            var next = Math.Max(MinScale, Math.Min(MaxScale, _scale + delta));
            if (Math.Abs(next - _scale) < 0.0001) return;

            _scale = next;
            UpdateVisual();
        }

        /// <summary>把圆规本体（未缩放的本地坐标）换算到本控件的根坐标</summary>
        private Point ToRoot(Point local)
        {
            return new Point(
                _needle.X + (local.X - _needle.X) * _scale,
                _needle.Y + (local.Y - _needle.Y) * _scale);
        }

        #region 几何

        private static Point Unit(double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(Math.Cos(rad), -Math.Sin(rad)); // 屏幕 y 向下
        }

        /// <summary>铰接点（针脚沿针脚方向反推）</summary>
        private Point Hinge
        {
            get
            {
                var u = Unit(_phiDeg);
                return new Point(_needle.X - LegLength * u.X, _needle.Y - LegLength * u.Y);
            }
        }

        /// <summary>铅笔脚尖端 = 铰接 + 腿向量（方向 = 针脚方向 - 张角，即铅笔脚位于针脚左侧）</summary>
        private Point PencilTip
        {
            get
            {
                var u = Unit(_phiDeg - _spreadDeg);
                var h = Hinge;
                return new Point(h.X + LegLength * u.X, h.Y + LegLength * u.Y);
            }
        }

        /// <summary>圆规半径（针脚到铅笔脚尖端的距离）</summary>
        private double CompassRadius
        {
            get
            {
                var t = PencilTip;
                return (_needle - t).Length;
            }
        }

        #endregion

        /// <summary>显示并复位到主屏中央</summary>
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

            _needle = new Point(w / 2, h / 2 + 120); // 圆心略低于中心，铰接整体可见
            _spreadDeg = 45;
            // 角平分线指向正下方(270°)，手柄即朝向正上方，两脚关于竖直轴对称（铅笔脚在左）
            _phiDeg = 270 + _spreadDeg / 2;
            _strokeNeedsNew = true;
            // 每次打开都重新把工具条摆到圆规上方（避免上次拖到屏幕边缘后这次看不见）
            _buttonBarPositioned = false;
            SetDrawMode(false);
            UpdateVisual();
        }

        private void SetDrawMode(bool on)
        {
            _drawMode = on;
            _drawBtn.Content = on
                ? Ink_Canvas.Properties.CanvasStrings.Canvas_CompassStop
                : Ink_Canvas.Properties.CanvasStrings.Canvas_CompassDraw;
            // 线稿风格：未落笔白底黑字，落笔后反色提示当前处于画线状态
            _drawBtn.Background = new SolidColorBrush(on ? InkColor : Colors.White);
            _drawBtn.Foreground = new SolidColorBrush(on ? Colors.White : InkColor);

            // 落笔状态下锁定半径与移动：HandleMove 已对 Move / PencilTip 直接返回。
            // 热区必须保持可命中（不能 Collapsed），否则这些区域会穿透到 inkCanvas，
            // 主程序处于图形绘制模式（drawingShapeMode != 0）时会误画出当前选定的图形。
            _pencilTipZone.Visibility = System.Windows.Visibility.Visible;
            _pivotLegHitZone.Visibility = System.Windows.Visibility.Visible;
            _pencilLegHitZone.Visibility = System.Windows.Visibility.Visible;
            _pivotLegHitZone.Cursor = on ? Cursors.Arrow : Cursors.SizeAll;
            _pencilLegHitZone.Cursor = on ? Cursors.Arrow : Cursors.SizeAll;
            _pencilTipZone.Cursor = on ? Cursors.Arrow : Cursors.Cross;

            _guideCircle.Visibility = on ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void UpdateVisual()
        {
            var h = Hinge;
            var t = PencilTip;
            var r = CompassRadius;

            // 整体缩放：锚点跟随针脚，针脚在屏幕上始终不动
            _visualScale.ScaleX = _scale;
            _visualScale.ScaleY = _scale;
            _visualScale.CenterX = _needle.X;
            _visualScale.CenterY = _needle.Y;

            // 本体图层需要有实际尺寸，否则超出范围的部件（缩放后外扩）可能收不到鼠标/触摸
            double hostWidth = ActualWidth > 0 ? ActualWidth : 4000;
            double hostHeight = ActualHeight > 0 ? ActualHeight : 4000;
            if (!hostWidth.Equals(_visualHost.Width)) _visualHost.Width = hostWidth;
            if (!hostHeight.Equals(_visualHost.Height)) _visualHost.Height = hostHeight;

            // 移动热区：沿两条腿铺设，覆盖铰接→脚端（脚端略外扩）
            SetLegHitZone(_pivotLegHitZone, h, _needle);
            SetLegHitZone(_pencilLegHitZone, h, t);

            // 腿造型：以铰接点为原点，本地 +X 对齐腿方向
            PositionAlongLeg(_pivotLegBar, h, _needle);
            PositionAlongLeg(_pivotNeedleTip, h, _needle);
            PositionAlongLeg(_pencilLegBar, h, t);

            // 圆规头：大圆 + 中心圆孔
            SetCenter(_headOuter, h);
            SetCenter(_headHole, h);

            // 手柄方向 = 两脚张角的角平分线反向（背离两脚，始终实时跟随）
            double bisectorDeg = _phiDeg - _spreadDeg / 2;
            var bisector = Unit(bisectorDeg);                       // 指向两脚之间
            var handleDir = new Point(-bisector.X, -bisector.Y);    // 手柄朝外
            double gripAngle = Math.Atan2(-handleDir.X, handleDir.Y) * 180 / Math.PI;

            // 细颈：从圆规头边缘向外接手柄
            double neckDist = HeadRadius + GripNeckHeight / 2;
            SetCenter(_gripNeck, new Point(h.X + handleDir.X * neckDist, h.Y + handleDir.Y * neckDist));
            ((RotateTransform)_gripNeck.RenderTransform).Angle = gripAngle;

            // 顶部滚花手柄
            double gripDist = HeadRadius + GripNeckHeight + GripHeight / 2;
            var gripCenter = new Point(h.X + handleDir.X * gripDist, h.Y + handleDir.Y * gripDist);
            SetCenter(_grip, gripCenter);
            ((RotateTransform)_grip.RenderTransform).Angle = gripAngle;

            // 手柄热区：比手柄本体大一圈，触摸屏上手指落点偏一点也能命中，
            // 避免触摸穿透到 inkCanvas 后一边旋转一边被画笔画出多余线条
            SetCenter(_gripHitZone, gripCenter);

            // 铅笔：笔尖对准出墨点，笔身沿腿向后并向圆规外侧偏 PencilTiltDeg（笔尾伸向圆规外侧）
            SetCanvas(_pencilBody, t.X, t.Y);
            ((RotateTransform)_pencilBody.RenderTransform).Angle =
                Math.Atan2(t.Y - h.Y, t.X - h.X) * 180 / Math.PI - PencilTiltDeg;
            SetCenter(_pencilTipZone, t);

            // 半径文本显示实际（画布）半径，并放在两脚正中间（张角平分线上）
            _angleText.Text = string.Format(CultureInfo.InvariantCulture, "r={0:F0}", r * _scale);
            SetCenter(_angleText, ToRoot(new Point((_needle.X + t.X) / 2, (_needle.Y + t.Y) / 2)));

            // 工具条独立于圆规本体：只在首次显示时摆到圆规上方，之后保留用户拖动的位置
            PositionButtonBarIfNeeded();

            _guideCircle.Width = _guideCircle.Height = r * 2;
            SetCanvas(_guideCircle, _needle.X - r, _needle.Y - r);
        }

        #region 拖拽交互

        private void AttachDrag(FrameworkElement el, DragKind kind)
        {
            el.MouseLeftButtonDown += (s, e) =>
            {
                if (_drag != DragKind.None) return;
                // 取圆规本体图层坐标（未缩放），整体缩放后点位计算仍然一致
                BeginDrag(kind, e.GetPosition(_visualHost));
                el.CaptureMouse();
                e.Handled = true;
            };
            el.MouseMove += (s, e) =>
            {
                if (_drag != kind || !el.IsMouseCaptured) return;
                HandleMove(e.GetPosition(_visualHost));
                e.Handled = true;
            };
            el.MouseLeftButtonUp += (s, e) =>
            {
                if (_drag != kind) return;
                el.ReleaseMouseCapture();
                EndDrag();
                e.Handled = true;
            };
            // 捕获丢失（切换窗口、触摸被系统接管等）也要收尾，否则 InkCanvas 会一直处于抑制出墨状态
            el.LostMouseCapture += (s, e) => EndDrag();

            el.TouchDown += (s, e) =>
            {
                if (_drag != DragKind.None) return;
                BeginDrag(kind, e.GetTouchPoint(_visualHost).Position);
                el.CaptureTouch(e.TouchDevice);
                e.Handled = true;
            };
            el.TouchMove += (s, e) =>
            {
                if (_drag != kind || !el.AreAnyTouchesCaptured) return;
                HandleMove(e.GetTouchPoint(_visualHost).Position);
                e.Handled = true;
            };
            el.TouchUp += (s, e) =>
            {
                if (_drag != kind) return;
                el.ReleaseTouchCapture(e.TouchDevice);
                EndDrag();
                e.Handled = true;
            };
            el.LostTouchCapture += (s, e) => EndDrag();
        }

        private void BeginDrag(DragKind kind, Point pos)
        {
            _drag = kind;
            _dragLast = pos;
            _strokeNeedsNew = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
        }

        private void EndDrag()
        {
            if (_drag == DragKind.None) return;
            _drag = DragKind.None;
            _strokeNeedsNew = true;
            DragEnded?.Invoke(this, EventArgs.Empty);
        }

        private void HandleMove(Point pos)
        {
            switch (_drag)
            {
                case DragKind.Move:
                {
                    // 未落笔：拖两条腿（或针脚）=整体移动；落笔状态下针脚钉死，不可移动
                    if (_drawMode) break;
                    var delta = pos - _dragLast;
                    _needle += delta;
                    _dragLast = pos;
                    UpdateVisual();
                    break;
                }

                case DragKind.Hinge:
                {
                    // 按住圆规头：无论是否落笔都绕针脚整体旋转，落笔时才出墨
                    var r = CompassRadius;
                    if (r < 5) break;
                    var hinge = Hinge;
                    var pencil = PencilTip;

                    // 光标相对针脚的角度
                    var d = pos - _needle;
                    if (d.Length < 20) break; // 离圆心太近角度不稳定
                    var targetAngle = Math.Atan2(-d.Y, d.X) * 180 / Math.PI;
                    var hingeAngle = Math.Atan2(-(hinge.Y - _needle.Y), hinge.X - _needle.X) * 180 / Math.PI;
                    var deltaAngle = ((targetAngle - hingeAngle + 540) % 360) - 180;

                    if (Math.Abs(deltaAngle) < 0.01) break;

                    // 铅笔脚旧角度（相对圆心）
                    var fromAngle = Math.Atan2(-(pencil.Y - _needle.Y), pencil.X - _needle.X) * 180 / Math.PI;

                    // 整体绕针脚旋转 deltaAngle：铰接和铅笔脚都转，腿长与张角不变（半径不变）
                    _phiDeg += deltaAngle;

                    if (_drawMode)
                    {
                        // 落笔才出墨
                        var toAngle = fromAngle + deltaAngle;
                        ArcDrawing?.Invoke(this, new CompassArcEventArgs
                        {
                            // 出墨坐标用的是画布坐标：整体缩放后半径按系数换算，圆心随锚点换算
                            Pivot = ToRoot(_needle),
                            Radius = r * _scale,
                            FromAngleDeg = fromAngle,
                            ToAngleDeg = toAngle,
                            StartNewStroke = _strokeNeedsNew
                        });
                        _strokeNeedsNew = false;
                    }
                    UpdateVisual();
                    break;
                }

                case DragKind.PencilTip:
                {
                    // 笔尖热区：调整张角=调整半径（未落笔时可用），不出墨
                    if (_drawMode) break;
                    var rr = (pos - _needle).Length;
                    var half = Math.Asin(Math.Max(0.02, Math.Min(0.98, rr / (2 * LegLength)))) * 180 / Math.PI;
                    var spread = Math.Max(MinSpreadDeg, Math.Min(MaxSpreadDeg, half * 2));
                    if (Math.Abs(spread - _spreadDeg) < 0.01) break;
                    _spreadDeg = spread;
                    UpdateVisual();
                    break;
                }
            }
        }

        #endregion

        #region 旋转光标

        private static Cursor _rotateCursor;

        /// <summary>环形箭头光标（WPF 没有内置旋转光标，这里动态生成一个 .cur 流）</summary>
        private static Cursor RotateCursor => _rotateCursor ?? (_rotateCursor = CreateRotateCursor());

        private static Cursor CreateRotateCursor()
        {
            try
            {
                // WPF 的 Cursor(Stream) 内部走 CreateIconFromResourceEx，对完整的 .cur 文件
                // （含 ICONDIR/ICONDIRENTRY）不生效，必须用文件路径走 LoadCursorFromFile。
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "inkcanvas-rotate-cursor.cur");
                File.WriteAllBytes(path, BuildRotateCursorData());
                return new Cursor(path);
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "inkcanvas-cursor-debug.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {ex.GetType().Name}: {ex.Message}\r\n");
                }
                catch
                {
                    // 忽略日志失败
                }
                return Cursors.Hand;
            }
        }

        private static byte[] BuildRotateCursorData()
        {
            const int size = 32;
            var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    // 圆弧：从 110° 顺时针扫到 -70°
                    var arc = new StreamGeometry();
                    using (var ctx = arc.Open())
                    {
                        ctx.BeginFigure(ArcPoint(110, size), false, false);
                        ctx.ArcTo(ArcPoint(-70, size), new Size(9, 9), 0, true,
                                  SweepDirection.Clockwise, true, false);
                    }
                    arc.Freeze();

                    var outline = new Pen(Brushes.Black, 5.4)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    var inner = new Pen(Brushes.White, 2.8)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    dc.DrawGeometry(null, outline, arc);
                    dc.DrawGeometry(null, inner, arc);

                    // 箭头：位于弧的终点，指向顺时针切线方向
                    var tip = ArcPoint(-70, size);
                    double dirRad = (-70 - 90) * Math.PI / 180;
                    var forward = new Vector(Math.Cos(dirRad), -Math.Sin(dirRad));
                    var perp = new Vector(-forward.Y, forward.X);
                    var arrow = new StreamGeometry();
                    using (var ctx = arrow.Open())
                    {
                        ctx.BeginFigure(tip + forward * 6.5, true, true);
                        ctx.LineTo(tip - forward * 2.5 + perp * 5, true, false);
                        ctx.LineTo(tip - forward * 2.5 - perp * 5, true, false);
                    }
                    arrow.Freeze();
                    dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 2), arrow);
                    dc.DrawGeometry(Brushes.White, null, arrow);
                }

                // RenderTargetBitmap 只支持 Pbgra32（不支持 Bgra32），
                // 而 .cur 的 32bpp 位图要求非预乘 BGRA，这里做一次去预乘。
                var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                var pixels = new byte[size * size * 4];
                rtb.CopyPixels(pixels, size * 4, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte alpha = pixels[i + 3];
                    if (alpha == 0)
                    {
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
                    }
                    else if (alpha < 255)
                    {
                        pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / alpha);
                        pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / alpha);
                        pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / alpha);
                    }
                }

                int xorSize = size * size * 4;
                int andSize = size * (size / 8);
                int imageSize = 40 + xorSize + andSize;

                var stream = new MemoryStream();
                var writer = new BinaryWriter(stream);
                writer.Write((short)0);          // reserved
                writer.Write((short)2);          // type: cursor
                writer.Write((short)1);          // count
                writer.Write((byte)size);        // width
                writer.Write((byte)size);        // height
                writer.Write((byte)0);           // color count
                writer.Write((byte)0);           // reserved
                writer.Write((short)(size / 2)); // hotspot X
                writer.Write((short)(size / 2)); // hotspot Y
                writer.Write(imageSize);
                writer.Write(22);                // image offset

                // BITMAPINFOHEADER
                writer.Write(40);
                writer.Write(size);
                writer.Write(size * 2);          // XOR + AND 高度
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);

                // XOR 位图（自下而上）
                for (int y = size - 1; y >= 0; y--)
                    writer.Write(pixels, y * size * 4, size * 4);

                // AND 掩码（全 0，完全由 alpha 决定透明度）
                writer.Write(new byte[andSize]);
                writer.Flush();

                return stream.ToArray();
        }

        private static Point ArcPoint(double degrees, int size)
        {
            double rad = degrees * Math.PI / 180;
            double r = size / 2.0 - 6;
            return new Point(size / 2.0 + r * Math.Cos(rad), size / 2.0 - r * Math.Sin(rad));
        }

        #endregion

        #region 视觉元素工厂

        /// <summary>腿杆在距铰接 x 处的一半宽度（根部粗、向脚尖线性收细）</summary>
        private static double LegHalfAt(double x)
        {
            var t = (x - LegStub) / (LegLength - NeedleCollarEnd - LegStub);
            t = Math.Max(0, Math.Min(1, t));
            return LegHalfWidth + (LegTipHalfWidth - LegHalfWidth) * t;
        }

        /// <summary>针脚腿杆：根部粗、向脚尖线性收细</summary>
        private static Polygon CreateNeedleLegBar()
        {
            return new Polygon
            {
                Points = new PointCollection
                {
                    new Point(LegStub, -LegHalfAt(LegStub)),
                    new Point(LegStub, LegHalfAt(LegStub)),
                    new Point(LegLength - NeedleCollarEnd, LegHalfAt(LegLength - NeedleCollarEnd)),
                    new Point(LegLength - NeedleCollarEnd, -LegHalfAt(LegLength - NeedleCollarEnd))
                },
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(InkColor),
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(0)
            };
        }

        /// <summary>脚尖：实心红色圆锥，端点即脚端（针脚端即圆心，铅笔脚端即出墨点）</summary>
        private static Polygon CreateLegTip()
        {
            return new Polygon
            {
                Points = new PointCollection
                {
                    new Point(LegLength - NeedleCollarEnd, LegTipHalfWidth - 0.5),
                    new Point(LegLength - NeedleCollarEnd, -(LegTipHalfWidth - 0.5)),
                    new Point(LegLength, 0)
                },
                Fill = new SolidColorBrush(LegRed),
                Stroke = new SolidColorBrush(LegRed),
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(0)
            };
        }

        /// <summary>圆规头：白底黑边的圆（大圆与中心孔共用）</summary>
        private static Ellipse CreateHeadCircle(double radius)
        {
            return new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(InkColor),
                StrokeThickness = 2.5
            };
        }

        /// <summary>圆规头与手柄之间的细颈</summary>
        private static Rectangle CreateGripNeck()
        {
            return new Rectangle
            {
                Width = 11,
                Height = GripNeckHeight,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(InkColor),
                StrokeThickness = 2.5,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(0)
            };
        }

        /// <summary>顶部滚花握持手柄：外框 + 竖纹</summary>
        private static WpfCanvas CreateGrip()
        {
            var root = new WpfCanvas
            {
                Width = GripWidth,
                Height = GripHeight,
                Background = Brushes.Transparent, // Canvas 需背景才可命中（用于拖动旋转）
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(0)
            };
            root.Children.Add(new Rectangle
            {
                Width = GripWidth,
                Height = GripHeight,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(InkColor),
                StrokeThickness = 2.5,
                IsHitTestVisible = false
            });
            for (var x = 4.0; x < GripWidth - 3; x += 4.5)
            {
                root.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = 3,
                    X2 = x,
                    Y2 = GripHeight - 3,
                    Stroke = new SolidColorBrush(InkColor),
                    StrokeThickness = 1.4,
                    IsHitTestVisible = false
                });
            }
            return root;
        }

        /// <summary>
        /// 线稿铅笔：笔尖在原点(0,0)，笔身朝 -X 方向延伸，整体随腿方向旋转（RenderTransform）。
        /// 分段比例：短笔芯 + 短木锥 + 长笔杆（带白色箍线） + 略宽的方形笔尾。
        /// 纯视觉元素，不参与命中（移动热区在两条腿上，笔尖热区单独用于调半径）。
        /// </summary>
        private static WpfCanvas CreatePencil()
        {
            var ink = new SolidColorBrush(InkColor);
            var body = new SolidColorBrush(PencilFill);
            var root = new WpfCanvas
            {
                IsHitTestVisible = false,
                RenderTransform = new RotateTransform(0)
            };

            // 笔芯（蓝色笔尖，即出墨点所在的脚尖位置）
            root.Children.Add(new Polygon
            {
                Points = new PointCollection
                {
                    new Point(0, 0),
                    new Point(-PencilLeadEnd, -PencilLeadHalf),
                    new Point(-PencilLeadEnd, PencilLeadHalf)
                },
                Fill = new SolidColorBrush(PenBlue),
                Stroke = new SolidColorBrush(PenBlue),
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round
            });
            // 木锥（削出的木质段，比笔芯长、比笔杆短）
            root.Children.Add(new Polygon
            {
                Points = new PointCollection
                {
                    new Point(-PencilLeadEnd, -PencilLeadHalf),
                    new Point(-PencilLeadEnd, PencilLeadHalf),
                    new Point(-PencilConeEnd, PencilBarrelHalf),
                    new Point(-PencilConeEnd, -PencilBarrelHalf)
                },
                Fill = body,
                Stroke = ink,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round
            });
            // 笔杆（主体，最长）
            var barrel = new Rectangle
            {
                Width = PencilBarrelEnd - PencilConeEnd,
                Height = PencilBarrelHalf * 2,
                Fill = body,
                Stroke = ink,
                StrokeThickness = 2.5
            };
            SetCanvas(barrel, -PencilBarrelEnd, -PencilBarrelHalf);
            root.Children.Add(barrel);
            // 笔杆上的白色箍线
            var band = new Rectangle
            {
                Width = PencilBandTo - PencilBandFrom,
                Height = PencilBarrelHalf * 2 + 2,
                Fill = Brushes.White,
                Stroke = ink,
                StrokeThickness = 1.6
            };
            SetCanvas(band, -PencilBandTo, -(PencilBarrelHalf + 1));
            root.Children.Add(band);
            // 笔尾（略宽于笔杆的方形端头）
            var nub = new Rectangle
            {
                Width = PencilEnd - PencilBarrelEnd,
                Height = PencilNubHalf * 2,
                Fill = body,
                Stroke = ink,
                StrokeThickness = 2.5
            };
            SetCanvas(nub, -PencilEnd, -PencilNubHalf);
            root.Children.Add(nub);

            return root;
        }

        private static Ellipse CreateGuideCircle()
        {
            return new Ellipse
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = null,
                IsHitTestVisible = false,
                Visibility = System.Windows.Visibility.Collapsed
            };
        }

        /// <summary>
        /// 顶部手柄热区：透明矩形，比手柄本体（27×36）大一圈。
        /// 触摸屏上手指的落点中心常有偏移，手柄太小时触摸会落到圆规覆盖层之外并穿透给 inkCanvas，
        /// 表现为「一边转圆规一边画出一条线」。
        /// </summary>
        private static Rectangle CreateGripHitZone()
        {
            return new Rectangle
            {
                Width = 64,
                Height = 76,
                RadiusX = 8,
                RadiusY = 8,
                Fill = Brushes.Transparent,
                Cursor = RotateCursor
            };
        }

        /// <summary>笔尖热区：透明圆形，拖动调整半径</summary>
        private static Ellipse CreateTipZone()
        {
            return new Ellipse
            {
                Width = 26,
                Height = 26,
                Fill = Brushes.Transparent, // 透明但可命中
                Cursor = Cursors.Cross
            };
        }

        /// <summary>腿移动热区：透明长条，沿腿方向铺设并覆盖脚端，拖动整体移动</summary>
        private static Rectangle CreateLegHitZone()
        {
            return new Rectangle
            {
                Width = LegLength + FootOverhang * 2,
                Height = 40,
                RadiusX = 20,
                RadiusY = 20,
                Fill = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(0)
            };
        }

        private static TextBlock CreateAngleText()
        {
            // 不设固定宽高，随字号自动撑开，保证半径文本完整显示
            return new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                FontSize = 17,
                FontWeight = FontWeights.Medium,
                IsHitTestVisible = false
            };
        }

        private static Button CreateSmallButton()
        {
            // 尺寸按放大后的字号预留，避免文字被裁切
            return new Button
            {
                Width = 64,
                Height = 34,
                FontSize = 16,
                Padding = new Thickness(4, 0, 4, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
                Content = ""
            };
        }

        #endregion

        #region 布局辅助

        /// <summary>把本地坐标以 +X 为腿方向的造型（腿杆、夹笔钩）摆到腿上：
        /// 元素本地原点对齐铰接点，再按腿方向旋转（RenderTransform 原点即本地 (0,0)）
        /// </summary>
        private static void PositionAlongLeg(FrameworkElement el, Point from, Point to)
        {
            SetCanvas(el, from.X, from.Y);
            ((RotateTransform)el.RenderTransform).Angle =
                Math.Atan2(to.Y - from.Y, to.X - from.X) * 180 / Math.PI;
        }

        /// <summary>让腿热区长条沿腿方向铺开：中心取腿中点并向脚端外扩，长轴对齐腿方向</summary>
        private static void SetLegHitZone(Rectangle zone, Point from, Point to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) return;
            var ux = dx / len;
            var uy = dy / len;
            SetCenter(zone, new Point((from.X + to.X) / 2 + ux * FootOverhang,
                                      (from.Y + to.Y) / 2 + uy * FootOverhang));
            ((RotateTransform)zone.RenderTransform).Angle = Math.Atan2(dy, dx) * 180 / Math.PI;
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
