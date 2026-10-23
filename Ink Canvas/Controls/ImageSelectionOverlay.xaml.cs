using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ink_Canvas.Controls
{
    public enum ImageResizeCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public enum SelectionOverlayMode
    {
        Element,
        Stroke
    }

    public class ImageResizeDeltaEventArgs : EventArgs
    {
        public ImageResizeCorner Corner { get; }
        public Vector CanvasDelta { get; }
        public bool LockAspectRatio { get; }

        public ImageResizeDeltaEventArgs(ImageResizeCorner corner, Vector canvasDelta, bool lockAspect)
        {
            Corner = corner;
            CanvasDelta = canvasDelta;
            LockAspectRatio = lockAspect;
        }
    }

    public class ImageMoveDeltaEventArgs : EventArgs
    {
        public Vector CanvasDelta { get; }
        public ImageMoveDeltaEventArgs(Vector delta) { CanvasDelta = delta; }
    }

    public class ImageRotateDeltaEventArgs : EventArgs
    {
        public double AngleDelta { get; }
        public ImageRotateDeltaEventArgs(double angleDelta) { AngleDelta = angleDelta; }
    }

    /// <summary>旋转中心（pivot）位置变化事件参数，坐标为 inkCanvas 画布坐标。</summary>
    public class ImagePivotChangedEventArgs : EventArgs
    {
        public Point CanvasPoint { get; }
        public ImagePivotChangedEventArgs(Point canvasPoint) { CanvasPoint = canvasPoint; }
    }

    public partial class ImageSelectionOverlay : UserControl
    {
        private const double HandleSize = 12;
        private const double RotationHandleHitSize = 28;
        private const double RotationHandleOffset = 28;

        public event EventHandler<ImageResizeDeltaEventArgs> ResizeDelta;
        public event EventHandler<ImageMoveDeltaEventArgs> MoveDelta;
        public event EventHandler<ImageRotateDeltaEventArgs> RotateDelta;
        public event EventHandler<ImagePivotChangedEventArgs> PivotChanged;
        public event EventHandler InteractionStarted;
        public event EventHandler InteractionEnded;

        public IInputElement CoordinateSource { get; set; }

        private Point _rotationCenterCanvas;
        private readonly RotateTransform _overlayRotation = new RotateTransform(0);

        private Point _pivotCanvas;
        private bool _hasCustomPivot;
        private bool _isPivotDragging;
        private bool _pivotStartedByTouch;
        private int? _pivotTouchDeviceId;
        private Vector _pivotDragOffset;

        private bool _isResizing;
        private bool _isRotating;
        private bool _isMoving;
        private bool _rotationStartedByTouch;
        private int? _rotationTouchDeviceId;
        private ImageResizeCorner _activeCorner;
        private Point _lastPoint;
        private double _lastRotationAngle;

        public ImageSelectionOverlay()
        {
            InitializeComponent();
            RenderTransform = _overlayRotation;

            TopLeftHandle.MouseLeftButtonDown += (s, e) => BeginResize(ImageResizeCorner.TopLeft, e, TopLeftHandle);
            TopRightHandle.MouseLeftButtonDown += (s, e) => BeginResize(ImageResizeCorner.TopRight, e, TopRightHandle);
            BottomLeftHandle.MouseLeftButtonDown += (s, e) => BeginResize(ImageResizeCorner.BottomLeft, e, BottomLeftHandle);
            BottomRightHandle.MouseLeftButtonDown += (s, e) => BeginResize(ImageResizeCorner.BottomRight, e, BottomRightHandle);

            TopLeftHandle.MouseMove += ResizeMove;
            TopRightHandle.MouseMove += ResizeMove;
            BottomLeftHandle.MouseMove += ResizeMove;
            BottomRightHandle.MouseMove += ResizeMove;

            TopLeftHandle.MouseLeftButtonUp += EndResize;
            TopRightHandle.MouseLeftButtonUp += EndResize;
            BottomLeftHandle.MouseLeftButtonUp += EndResize;
            BottomRightHandle.MouseLeftButtonUp += EndResize;

            RotationHandle.MouseLeftButtonDown += BeginRotate;
            RotationHandle.MouseMove += RotateMove;
            RotationHandle.MouseLeftButtonUp += EndRotate;
            RotationHandle.LostMouseCapture += RotationHandle_LostMouseCapture;
            RotationHandle.TouchDown += BeginRotateTouch;
            RotationHandle.TouchMove += RotateTouchMove;
            RotationHandle.TouchUp += EndRotateTouch;
            RotationHandle.LostTouchCapture += RotationHandle_LostTouchCapture;

            MoveSurface.MouseLeftButtonDown += BeginMove;
            MoveSurface.MouseMove += MoveMove;
            MoveSurface.MouseLeftButtonUp += EndMove;

            RotationCenterHandle.MouseLeftButtonDown += BeginPivotDrag;
            RotationCenterHandle.MouseMove += PivotDragMove;
            RotationCenterHandle.MouseLeftButtonUp += EndPivotDrag;
            RotationCenterHandle.LostMouseCapture += RotationCenterHandle_LostMouseCapture;
            RotationCenterHandle.TouchDown += BeginPivotDragTouch;
            RotationCenterHandle.TouchMove += PivotDragTouchMove;
            RotationCenterHandle.TouchUp += EndPivotDragTouch;
            RotationCenterHandle.LostTouchCapture += RotationCenterHandle_LostTouchCapture;
        }

        /// <summary>当前旋转中心（inkCanvas 画布坐标）。未自定义时等于选区中心。</summary>
        public Point PivotCanvasPoint => _hasCustomPivot ? _pivotCanvas : _rotationCenterCanvas;

        /// <summary>旋转中心是否已被用户拖动到自定义位置。</summary>
        public bool HasCustomPivot => _hasCustomPivot;

        /// <summary>显式指定旋转中心（画布坐标），不触发 PivotChanged。</summary>
        public void SetPivot(Point canvasPoint)
        {
            _pivotCanvas = canvasPoint;
            _hasCustomPivot = true;
            UpdatePivotHandlePosition();
        }

        /// <summary>恢复默认旋转中心（选区中心）。</summary>
        public void ResetPivot()
        {
            _hasCustomPivot = false;
            UpdatePivotHandlePosition();
        }

        public void SetMode(SelectionOverlayMode mode)
        {
            bool isElementMode = mode == SelectionOverlayMode.Element;

            OverlayRoot.Background = isElementMode ? Brushes.Transparent : null;
            MoveSurface.IsHitTestVisible = isElementMode;
            FrameBorder.Visibility = isElementMode ? Visibility.Visible : Visibility.Collapsed;
            TopLeftHandle.Visibility = isElementMode ? Visibility.Visible : Visibility.Collapsed;
            TopRightHandle.Visibility = isElementMode ? Visibility.Visible : Visibility.Collapsed;
            BottomLeftHandle.Visibility = isElementMode ? Visibility.Visible : Visibility.Collapsed;
            BottomRightHandle.Visibility = isElementMode ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Position overlay so its logical rect (width × height) is centered at centerCanvas,
        /// then rotated by rotationAngleDegrees around that center to match the target element.
        /// </summary>
        public void UpdateFrame(Point centerCanvas, double width, double height, double rotationAngleDegrees)
        {
            if (width <= 0 || height <= 0) return;

            _rotationCenterCanvas = centerCanvas;

            double left = centerCanvas.X - width / 2;
            double top = centerCanvas.Y - height / 2;
            Margin = new Thickness(left, top, 0, 0);
            Width = width;
            Height = height;

            RenderTransformOrigin = new Point(0, 0);
            _overlayRotation.Angle = rotationAngleDegrees;
            _overlayRotation.CenterX = width / 2;
            _overlayRotation.CenterY = height / 2;

            FrameBorder.Width = width;
            FrameBorder.Height = height;
            System.Windows.Controls.Canvas.SetLeft(FrameBorder, 0);
            System.Windows.Controls.Canvas.SetTop(FrameBorder, 0);

            MoveSurface.Width = width;
            MoveSurface.Height = height;
            System.Windows.Controls.Canvas.SetLeft(MoveSurface, 0);
            System.Windows.Controls.Canvas.SetTop(MoveSurface, 0);

            double h = HandleSize / 2;
            System.Windows.Controls.Canvas.SetLeft(TopLeftHandle, -h);
            System.Windows.Controls.Canvas.SetTop(TopLeftHandle, -h);
            System.Windows.Controls.Canvas.SetLeft(TopRightHandle, width - h);
            System.Windows.Controls.Canvas.SetTop(TopRightHandle, -h);
            System.Windows.Controls.Canvas.SetLeft(BottomLeftHandle, -h);
            System.Windows.Controls.Canvas.SetTop(BottomLeftHandle, height - h);
            System.Windows.Controls.Canvas.SetLeft(BottomRightHandle, width - h);
            System.Windows.Controls.Canvas.SetTop(BottomRightHandle, height - h);

            double rh = RotationHandleHitSize / 2;
            double midX = width / 2;
            System.Windows.Controls.Canvas.SetLeft(RotationHandle, midX - rh);
            System.Windows.Controls.Canvas.SetTop(RotationHandle, -RotationHandleOffset - rh);

            RotationLine.X1 = midX;
            RotationLine.Y1 = 0;
            RotationLine.X2 = midX;
            RotationLine.Y2 = -RotationHandleOffset;

            UpdatePivotHandlePosition();
        }

        private void UpdatePivotHandlePosition()
        {
            double width = Width, height = Height;
            if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0) return;

            Point local = CanvasToLocal(PivotCanvasPoint, width, height);
            double half = RotationCenterHandle.Width / 2;
            if (double.IsNaN(half) || half <= 0) half = RotationHandleHitSize / 2;

            System.Windows.Controls.Canvas.SetLeft(RotationCenterHandle, local.X - half);
            System.Windows.Controls.Canvas.SetTop(RotationCenterHandle, local.Y - half);
        }

        // canvas → overlay local。overlay 以自身的 (width/2, height/2) 为中心旋转。
        private Point CanvasToLocal(Point canvasPoint, double width, double height)
        {
            double rad = _overlayRotation.Angle * Math.PI / 180.0;
            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);
            double cx = width / 2, cy = height / 2;
            double dx = canvasPoint.X - Margin.Left - cx;
            double dy = canvasPoint.Y - Margin.Top - cy;
            return new Point(cx + dx * cos + dy * sin, cy - dx * sin + dy * cos);
        }

        private IInputElement GetSource() => CoordinateSource ?? (IInputElement)Parent;

        private void BeginPivotDrag(object sender, MouseButtonEventArgs e)
        {
            if (_pivotStartedByTouch) return;
            var source = GetSource();
            if (source == null) return;

            // 双击旋转中心手柄恢复默认（选区中心）。
            if (e.ClickCount >= 2)
            {
                ResetPivot();
                e.Handled = true;
                return;
            }

            Point p = e.GetPosition(source);
            _pivotDragOffset = PivotCanvasPoint - p;
            _isPivotDragging = true;
            RotationCenterHandle.CaptureMouse();
            e.Handled = true;
        }

        private void PivotDragMove(object sender, MouseEventArgs e)
        {
            if (_pivotStartedByTouch || !_isPivotDragging || !RotationCenterHandle.IsMouseCaptured) return;
            var source = GetSource();
            if (source == null) return;

            MovePivotTo(e.GetPosition(source) + _pivotDragOffset);
            e.Handled = true;
        }

        private void EndPivotDrag(object sender, MouseButtonEventArgs e)
        {
            if (_pivotStartedByTouch || !_isPivotDragging) return;
            FinishPivotDrag();
            e.Handled = true;
        }

        private void RotationCenterHandle_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isPivotDragging && !_pivotStartedByTouch) FinishPivotDrag();
        }

        private void BeginPivotDragTouch(object sender, TouchEventArgs e)
        {
            if (_isPivotDragging) return;
            var source = GetSource();
            if (source == null) return;

            _isPivotDragging = true;
            _pivotStartedByTouch = true;
            _pivotTouchDeviceId = e.TouchDevice.Id;
            _pivotDragOffset = PivotCanvasPoint - e.GetTouchPoint(source).Position;
            RotationCenterHandle.CaptureTouch(e.TouchDevice);
            e.Handled = true;
        }

        private void PivotDragTouchMove(object sender, TouchEventArgs e)
        {
            if (!_isPivotDragging || !_pivotStartedByTouch
                || _pivotTouchDeviceId != e.TouchDevice.Id
                || !RotationCenterHandle.AreAnyTouchesCaptured)
            {
                return;
            }

            var source = GetSource();
            if (source == null) return;

            MovePivotTo(e.GetTouchPoint(source).Position + _pivotDragOffset);
            e.Handled = true;
        }

        private void EndPivotDragTouch(object sender, TouchEventArgs e)
        {
            if (!_isPivotDragging || !_pivotStartedByTouch
                || _pivotTouchDeviceId != e.TouchDevice.Id)
            {
                return;
            }

            FinishPivotDragTouch();
            e.Handled = true;
        }

        private void RotationCenterHandle_LostTouchCapture(object sender, TouchEventArgs e)
        {
            if (_isPivotDragging && _pivotStartedByTouch
                && _pivotTouchDeviceId == e.TouchDevice.Id)
            {
                FinishPivotDragTouch();
            }
        }

        private void MovePivotTo(Point canvasPoint)
        {
            _pivotCanvas = canvasPoint;
            _hasCustomPivot = true;
            UpdatePivotHandlePosition();
            PivotChanged?.Invoke(this, new ImagePivotChangedEventArgs(canvasPoint));
        }

        private void FinishPivotDrag()
        {
            _isPivotDragging = false;
            if (RotationCenterHandle.IsMouseCaptured)
                RotationCenterHandle.ReleaseMouseCapture();
        }

        private void FinishPivotDragTouch()
        {
            int? touchDeviceId = _pivotTouchDeviceId;
            _isPivotDragging = false;
            _pivotStartedByTouch = false;
            _pivotTouchDeviceId = null;

            if (touchDeviceId != null)
            {
                foreach (TouchDevice touch in RotationCenterHandle.TouchesCaptured)
                {
                    if (touch.Id == touchDeviceId.Value)
                    {
                        RotationCenterHandle.ReleaseTouchCapture(touch);
                        break;
                    }
                }
            }
        }

        private void BeginResize(ImageResizeCorner corner, MouseButtonEventArgs e, Ellipse handle)
        {
            var source = GetSource();
            if (source == null) return;
            _isResizing = true;
            _activeCorner = corner;
            _lastPoint = e.GetPosition(source);
            handle.CaptureMouse();
            InteractionStarted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void ResizeMove(object sender, MouseEventArgs e)
        {
            if (!_isResizing || !(sender is Ellipse handle) || !handle.IsMouseCaptured) return;
            var source = GetSource();
            if (source == null) return;
            var current = e.GetPosition(source);
            var delta = current - _lastPoint;
            bool lockAspect = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            ResizeDelta?.Invoke(this, new ImageResizeDeltaEventArgs(_activeCorner, delta, lockAspect));
            _lastPoint = current;
            e.Handled = true;
        }

        private void EndResize(object sender, MouseButtonEventArgs e)
        {
            if (!_isResizing) return;
            if (sender is Ellipse handle) handle.ReleaseMouseCapture();
            _isResizing = false;
            InteractionEnded?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void BeginRotate(object sender, MouseButtonEventArgs e)
        {
            if (_rotationStartedByTouch) return;
            var source = GetSource();
            if (source == null) return;
            _isRotating = true;
            var p = e.GetPosition(source);
            _lastRotationAngle = AngleFromCenter(p);
            RotationHandle.CaptureMouse();
            InteractionStarted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void RotateMove(object sender, MouseEventArgs e)
        {
            if (_rotationStartedByTouch || !_isRotating || !RotationHandle.IsMouseCaptured) return;
            var source = GetSource();
            if (source == null) return;
            RaiseRotateDelta(e.GetPosition(source));
            e.Handled = true;
        }

        private void EndRotate(object sender, MouseButtonEventArgs e)
        {
            if (_rotationStartedByTouch || !_isRotating) return;
            FinishRotation();
            e.Handled = true;
        }

        private void RotationHandle_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isRotating && !_rotationStartedByTouch)
                FinishRotation();
        }

        private void BeginRotateTouch(object sender, TouchEventArgs e)
        {
            if (_isRotating) return;
            var source = GetSource();
            if (source == null) return;

            _isRotating = true;
            _rotationStartedByTouch = true;
            _rotationTouchDeviceId = e.TouchDevice.Id;
            _lastRotationAngle = AngleFromCenter(e.GetTouchPoint(source).Position);
            RotationHandle.CaptureTouch(e.TouchDevice);
            InteractionStarted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void RotateTouchMove(object sender, TouchEventArgs e)
        {
            if (!_isRotating || !_rotationStartedByTouch
                || _rotationTouchDeviceId != e.TouchDevice.Id
                || !RotationHandle.AreAnyTouchesCaptured)
            {
                return;
            }

            var source = GetSource();
            if (source == null) return;
            RaiseRotateDelta(e.GetTouchPoint(source).Position);
            e.Handled = true;
        }

        private void EndRotateTouch(object sender, TouchEventArgs e)
        {
            if (!_isRotating || !_rotationStartedByTouch
                || _rotationTouchDeviceId != e.TouchDevice.Id)
            {
                return;
            }

            FinishRotation();
            e.Handled = true;
        }

        private void RotationHandle_LostTouchCapture(object sender, TouchEventArgs e)
        {
            if (_isRotating && _rotationStartedByTouch
                && _rotationTouchDeviceId == e.TouchDevice.Id)
            {
                FinishRotation();
            }
        }

        private void RaiseRotateDelta(Point point)
        {
            double angle = AngleFromCenter(point);
            double delta = angle - _lastRotationAngle;
            if (delta > 180) delta -= 360;
            else if (delta < -180) delta += 360;
            _lastRotationAngle = angle;
            RotateDelta?.Invoke(this, new ImageRotateDeltaEventArgs(delta));
        }

        private void FinishRotation()
        {
            if (!_isRotating) return;

            bool startedByTouch = _rotationStartedByTouch;
            int? touchDeviceId = _rotationTouchDeviceId;
            _isRotating = false;
            _rotationStartedByTouch = false;
            _rotationTouchDeviceId = null;

            if (startedByTouch)
            {
                foreach (TouchDevice touch in RotationHandle.TouchesCaptured)
                {
                    if (touchDeviceId == touch.Id)
                    {
                        RotationHandle.ReleaseTouchCapture(touch);
                        break;
                    }
                }
            }
            else if (RotationHandle.IsMouseCaptured)
            {
                RotationHandle.ReleaseMouseCapture();
            }

            InteractionEnded?.Invoke(this, EventArgs.Empty);
        }

        private void BeginMove(object sender, MouseButtonEventArgs e)
        {
            var source = GetSource();
            if (source == null) return;
            _isMoving = true;
            _lastPoint = e.GetPosition(source);
            MoveSurface.CaptureMouse();
            InteractionStarted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void MoveMove(object sender, MouseEventArgs e)
        {
            if (!_isMoving || !MoveSurface.IsMouseCaptured) return;
            var source = GetSource();
            if (source == null) return;
            var current = e.GetPosition(source);
            var delta = current - _lastPoint;
            _lastPoint = current;
            MoveDelta?.Invoke(this, new ImageMoveDeltaEventArgs(delta));
            e.Handled = true;
        }

        private void EndMove(object sender, MouseButtonEventArgs e)
        {
            if (!_isMoving) return;
            MoveSurface.ReleaseMouseCapture();
            _isMoving = false;
            InteractionEnded?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        private double AngleFromCenter(Point p)
        {
            Point pivot = PivotCanvasPoint;
            double dx = p.X - pivot.X;
            double dy = p.Y - pivot.Y;
            return Math.Atan2(dy, dx) * 180.0 / Math.PI;
        }
    }
}