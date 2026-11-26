using Ink_Canvas.Controls;
using Ink_Canvas.Controls.Toolbar.FloatingToolbar;
using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        #region Floating Bar Control

        /// <summary>
        /// 处理形状绘制按钮的鼠标抬起事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当形状绘制按钮被点击时：
        /// 1. 重置之前按下的面板背景
        /// 2. 检查是否是浮动栏按钮且不是当前按下的对象
        /// 3. 如果形状绘制面板可见，则隐藏它
        /// 4. 如果形状绘制面板不可见，则显示它
        /// </remarks>
        internal void ImageDrawShape_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("打开几何工具")) return;

            // 如果当前不在批注模式，先切换到批注模式
            if (!IsAnnotating)
            {
                PenIcon_Click(sender, e);
            }

            if (BorderDrawShape.IsOpen || BoardBorderDrawShape.IsOpen)
            {
                AnimationsHelper.HidePopupWithSlideAndFade(BorderDrawShape);
                AnimationsHelper.HidePopupWithSlideAndFade(BoardBorderDrawShape);
            }
            else
            {
                HideSubPanels();
                if (currentMode == 0)
                {
                    // 如果几何按钮不在浮动栏中，PlacementTarget 可能为 null，
                    // 回退到工具按钮作为定位目标
                    if (BorderDrawShape.PlacementTarget == null ||
                        !IsPlacementTargetVisible(BorderDrawShape.PlacementTarget))
                    {
                        BorderDrawShape.PlacementTarget = ToolsFloatingBarBtn ?? ShapeDrawFloatingBarBtn;
                    }
                    // 同步圆心显示开关状态
                    if (ShapeDrawPopupContent?.ShowCircleCenterToggle != null)
                        ShapeDrawPopupContent.ShowCircleCenterToggle.IsOn = Settings.Canvas.ShowCircleCenter;
                    // 同步抛物线一笔绘制开关状态
                    if (ShapeDrawPopupContent?.ParabolaSingleStrokeToggle != null)
                        ShapeDrawPopupContent.ParabolaSingleStrokeToggle.IsOn = Settings.Canvas.ParabolaSingleStroke;
                    AnimationsHelper.ShowPopupWithSlideAndFade(BorderDrawShape);
                    _popupManager?.BringToFront(BorderDrawShape);
                }
                else
                {
                    // 白板模式：如果几何按钮不在白板工具栏中，回退到白板工具按钮
                    if (BoardBorderDrawShape.PlacementTarget == null ||
                        !IsPlacementTargetVisible(BoardBorderDrawShape.PlacementTarget))
                    {
                        var boardToolsBtn = FindView("board.tools");
                        if (boardToolsBtn != null)
                            BoardBorderDrawShape.PlacementTarget = boardToolsBtn;
                    }
                    // 同步圆心显示开关状态
                    if (BoardShapeDrawPopupContent?.ShowCircleCenterToggle != null)
                        BoardShapeDrawPopupContent.ShowCircleCenterToggle.IsOn = Settings.Canvas.ShowCircleCenter;
                    // 同步抛物线一笔绘制开关状态
                    if (BoardShapeDrawPopupContent?.ParabolaSingleStrokeToggle != null)
                        BoardShapeDrawPopupContent.ParabolaSingleStrokeToggle.IsOn = Settings.Canvas.ParabolaSingleStroke;
                    AnimationsHelper.ShowPopupWithSlideAndFade(BoardBorderDrawShape);
                    _popupManager?.BringToFront(BoardBorderDrawShape);
                }

                // 打开面板时刷新按钮选中状态与油漆桶图标配色（主题切换后前景色会变）
                UpdateAllGeometryButtonsCheckedState();
                UpdatePaintBucketIconColors();
            }
        }

        #endregion Floating Bar Control

        /// <summary>
        /// 检查 Popup 的 PlacementTarget 是否在可视树中且可见
        /// </summary>
        private bool IsPlacementTargetVisible(UIElement target)
        {
            if (target == null) return false;
            // 检查元素是否连接到实际的渲染窗口（PresentationSource）
            if (PresentationSource.FromVisual(target) == null) return false;
            if (target is FrameworkElement fe)
            {
                // 检查元素是否可见
                if (fe.Visibility != Visibility.Visible) return false;
                // 检查元素是否在屏幕上有实际尺寸
                if (fe.ActualWidth <= 0 && fe.ActualHeight <= 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 形状绘制模式
        /// </summary>
        /// <remarks>
        /// 不同的值表示不同的形状绘制模式：
        /// 1: 直线
        /// 2: 箭头
        /// 3: 矩形
        /// 4: 椭圆
        /// 5: 圆形
        /// 6: 圆柱
        /// 7: 圆锥
        /// 8: 虚线
        /// 9: 长方体
        /// 10: 虚线圆形
        /// 11: 坐标系1
        /// 12: 多边形（单击添加顶点，点击起点闭合完成）
        /// 13: 过圆外一点作圆的切线（拖动式：按住圆拖到圆外一点后松手）
        /// 14: 直角三角形
        /// 15: 平行线
        /// 16: 中心椭圆
        /// 17: 坐标系5
        /// 18: 点线
        /// 19: 中心矩形
        /// 20: 抛物线1
        /// 21: 垂线（两步：第一步点选一条直线，第二步给一点）
        /// 22: 带焦点的抛物线
        /// 23: 带焦点的中心椭圆
        /// 24: 双曲线
        /// 圆规/三角板/直尺不占用 drawingShapeMode：它们是常驻覆盖层工具，
        /// 仅在点击自身关闭按钮、退出批注模式、退出白板模式时关闭。
        /// </remarks>
        private int _drawingShapeMode;

        /// <summary>
        /// 形状绘制模式
        /// </summary>
        private int drawingShapeMode
        {
            get => _drawingShapeMode;
            set
            {
                if (_drawingShapeMode == value) return;
                var previousMode = _drawingShapeMode;
                _drawingShapeMode = value;
                // 离开多边形模式时清理未完成的顶点与预览
                if (previousMode == 12)
                    CancelPolygonDrawing();
                // 离开圆的切线模式时清理选中圆的高亮与步骤状态（兜底所有直接改模式的入口，
                // 否则高亮圆环会残留在画布上）
                if (previousMode == 13)
                    CancelTangentCircleDrawing();
                // 图形对话框的选中状态变化后，同步白板工具栏「几何图形」按钮的高亮
                UpdateBoardShapeButtonHighlight();
            }
        }

        /// <summary>
        /// 用于存储是否是"选中"状态，便于后期抬笔后不做切换到笔的处理
        /// </summary>
        private bool isLongPressSelected;

        #region Buttons

        /// <summary>
        /// 处理形状绘制面板固定按钮的鼠标抬起事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当形状绘制面板固定按钮被点击时：
        /// 1. 检查是否是当前按下的对象
        /// 2. 切换自动隐藏开关的状态
        /// 3. 根据开关状态更新图标为固定或未固定状态
        /// </remarks>
        private void SymbolIconPinBorderDrawShape_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (lastBorderMouseDownObject != sender) return;

            ToggleSwitchDrawShapeBorderAutoHide.IsOn = !ToggleSwitchDrawShapeBorderAutoHide.IsOn;

            if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
                ((FontIcon)sender).Icon = SegoeFluentIcons.Pin;
            else
                ((FontIcon)sender).Icon = SegoeFluentIcons.Unpin;
        }

        /// <summary>
        /// 上一次鼠标按下的发送者
        /// </summary>
        private object lastMouseDownSender;

        /// <summary>
        /// 上一次鼠标按下的时间
        /// </summary>
        private DateTime lastMouseDownTime = DateTime.MinValue;

        /// <summary>
        /// 处理形状绘制按钮的鼠标按下事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当形状绘制按钮被长按（500毫秒）时：
        /// 1. 记录当前按下的对象和时间
        /// 2. 等待500毫秒检查是否仍然是同一个对象
        /// 3. 如果是同一个对象，设置透明度动画
        /// 4. 禁用橡皮擦覆盖
        /// 5. 设置强制橡皮擦模式
        /// 6. 根据不同的按钮设置不同的形状绘制模式
        /// 7. 更新工具模式缓存
        /// 8. 设置长按选中标志
        /// 9. 如果是单指拖拽模式，取消该模式
        /// </remarks>
        private async void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("绘制几何图形"))
            {
                e.Handled = true;
                return;
            }
            lastMouseDownSender = sender;
            lastMouseDownTime = DateTime.Now;

            await Task.Delay(500);

            if (lastMouseDownSender == sender)
            {
                lastMouseDownSender = null;
                var dA = new DoubleAnimation(1, 0.3, new Duration(TimeSpan.FromMilliseconds(100)));
                ((UIElement)sender).BeginAnimation(OpacityProperty, dA);

                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                if (sender == ImageDrawLine || sender == BoardImageDrawLine)
                    drawingShapeMode = 1;
                else if (sender == ImageDrawDashedLine || sender == BoardImageDrawDashedLine)
                    drawingShapeMode = 8;
                else if (sender == ImageDrawDotLine || sender == BoardImageDrawDotLine)
                    drawingShapeMode = 18;
                else if (sender == ImageDrawArrow || sender == BoardImageDrawArrow)
                    drawingShapeMode = 2;
                else if (sender == ImageDrawParallelLine || sender == BoardImageDrawParallelLine) drawingShapeMode = 15;

                // 更新模式缓存
                UpdateCurrentToolMode("shape");

                isLongPressSelected = true;
                if (isSingleFingerDragMode) ToggleFingerDragMode(null, null);
            }
        }

        /// <summary>
        /// 统一处理所有几何图形按钮的点击（Toggle 模式）
        /// </summary>
        /// <param name="sender">事件发送者（GeometryButton）</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 批注模式下几何工具被选中后会一直保持选中，直到：
        /// 1. 再次点击同一个几何按钮（退出图形绘制模式）
        /// 2. 点击另一个几何按钮（切换图形）
        /// 3. 点击画笔、橡皮、框选等其它工具按钮
        /// </remarks>
        private async void GeometryButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("绘制几何图形")) return;
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;

            var btn = sender as GeometryButton;
            if (btn == null) return;

            // 圆规/三角板/直尺是常驻覆盖层工具：点击只切换对应实例的显示/隐藏，不进入绘制模式、
            // 不改变书写状态，其它尺具不受影响（三角板可同时使用两块）。
            if (IsTriangleRulerButton(btn))
            {
                ToggleTriangleRuler(btn);
                return;
            }
            if (IsStraightRulerButton(btn))
            {
                ToggleStraightRuler();
                return;
            }
            if (IsCompassButton(btn))
            {
                ToggleCompass();
                return;
            }

            int mode = GetDrawingModeForButton(btn);
            if (mode == 0) return;

            if (drawingShapeMode == mode)
            {
                // 已经在该模式，再次点击退出
                ExitShapeDrawingMode();
                UpdateAllGeometryButtonsCheckedState();
                return;
            }

            // 进入新模式
            EnterShapeDrawingMode(mode);
        }

        /// <summary>
        /// 进入指定几何绘制模式：清理上一个多步绘制残留的状态、切换编辑模式并刷新按钮选中态。
        /// 供几何按钮点击与「填充色调色盘选色后自动选中填充工具」共用。
        /// </summary>
        /// <param name="mode">目标绘制模式号（与 <see cref="GetDrawingModeForButton"/> 返回值一致）</param>
        private void EnterShapeDrawingMode(int mode)
        {
            if (mode == 0 || drawingShapeMode == mode) return;

            if (drawingShapeMode == 15) CancelMidpointDrawing();
            if (drawingShapeMode == 17) CancelParallelDrawing();
            if (drawingShapeMode == 21) CancelPerpendicularDrawing();
            if (drawingShapeMode == 22) CancelAngleBisectorDrawing();
            if (drawingShapeMode == 26) CancelPerpendicularBisector();
            if (drawingShapeMode == TangentShapeMode) CancelTangentCircleDrawing();
            // 圆弧：切入该工具时清掉上一次的收尾锁定。锁定本该由落笔解除，但触摸落笔路径
            // 历史上漏了重置，切工具再切回来也仍然锁死；这里兜底，保证每次进入工具都能重新画。
            if (mode == 27)
            {
                _arcAwaitNewGesture = false;
                ClearArcHandleHighlight();
            }
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = mode;
            // 每次进入工具都从第一步开始，避免上一次多步绘制残留的步骤状态
            drawMultiStepShapeCurrentStep = 0;
            if (mode == TangentShapeMode)
            {
                // 圆的切线：清掉上一次的选中圆/高亮/预览，重新开始一次拖动
                ResetTangentCircleState();
                ShowCanvasToast("按住圆拖到圆外一点");
            }
            // 平行线 / 垂线：第一步改为点选一条已有直线作为基准
            if (mode == 17 || mode == 21)
                ShowCanvasToast("第一步：点击一条直线");
            // 角平分线：两步都是点选角的边
            if (mode == 22)
                ShowCanvasToast("第一步：点击角的一条边");
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();

            // 更新模式缓存
            UpdateCurrentToolMode("shape");

            // 保持选中：画完一笔后不自动回到画笔模式
            isLongPressSelected = true;

            // 更新所有按钮的选中状态
            UpdateAllGeometryButtonsCheckedState();

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 退出形状绘制模式
        /// </summary>
        private void ExitShapeDrawingMode()
        {
            if (drawingShapeMode == 15) CancelMidpointDrawing();
            if (drawingShapeMode == 17) CancelParallelDrawing();
            if (drawingShapeMode == 21) CancelPerpendicularDrawing();
            if (drawingShapeMode == 22) CancelAngleBisectorDrawing();
            if (drawingShapeMode == 26) CancelPerpendicularBisector();
            if (drawingShapeMode == TangentShapeMode) CancelTangentCircleDrawing();
            if (drawingShapeMode == AxisFlipShapeMode) CancelAxisFlip();
            ClearArcHandleHighlight();
            forceEraser = false;
            drawingShapeMode = 0;
            isLongPressSelected = false;
            inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();

            // 退出图形工具后回到默认笔：同步工具模式缓存与（浮动栏/白板工具栏的）选中态
            UpdateCurrentToolMode("pen");
            SetFloatingBarHighlightPosition("pen");
        }

        /// <summary>
        /// 刷新白板工具栏「几何图形」按钮的高亮：图形对话框中有选中对象时以蓝底呈现。
        /// 圆规 / 直尺 / 三角板是常驻覆盖层工具，不占用 <see cref="_drawingShapeMode"/>，
        /// 因此只有圆规/直尺/三角板被选中时不会触发高亮。
        /// </summary>
        internal void UpdateBoardShapeButtonHighlight()
        {
            try
            {
                if (FindView("board.shape") is not BoardToolbarButton btn) return;

                if (_drawingShapeMode != 0)
                {
                    var accent = Application.Current?.TryFindResource("FloatingBarAccentBrush") as Brush
                                 ?? new SolidColorBrush(Color.FromRgb(37, 99, 235));
                    btn.Background = accent;
                    btn.IconBrush = Brushes.White;
                    btn.Foreground = Brushes.White;
                }
                else if (ToolbarRegistry.GetUseRedStyle(btn))
                {
                    var red = Application.Current?.TryFindResource("RedBrush") as Brush ?? Brushes.Red;
                    btn.Background = Brushes.Transparent;
                    btn.IconBrush = red;
                    btn.LabelTextBlockControl?.SetValue(TextBlock.ForegroundProperty, red);
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    // 与 XAML 一致地重新建立动态资源引用，保证文字/图标恢复为主题前景色并跟随主题切换
                    btn.SetResourceReference(BoardToolbarButton.IconBrushProperty, "FloatingBarForegroundBrush");
                    btn.LabelTextBlockControl?.SetResourceReference(TextBlock.ForegroundProperty, "FloatingBarForegroundBrush");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"刷新几何图形按钮高亮失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        #region 圆规工具

        /// <summary>圆规当前正在绘制的弧线笔画（一次抓取一笔，整体撤销）</summary>
        private Stroke _compassStroke;
        private readonly List<Point> _compassStrokePoints = new List<Point>();

        /// <summary>拖动圆规期间是否临时关闭了 InkCanvas 出墨</summary>
        private bool _isCompassDragSuppressingInk;
        private InkCanvasEditingMode _compassEditingModeBeforeDrag = InkCanvasEditingMode.None;
        private int _compassStrokeCountBeforeDrag;

        /// <summary>本次拖动是否已记录过「拦截到画布触摸」的日志（每次拖动只记一条，便于现场排查）</summary>
        private bool _compassDragBlockedCanvasTouchLogged;

        /// <summary>记录一次「圆规拖动期间拦截画布触摸」，用于在触摸屏上确认多余线条的触发来源</summary>
        internal void NoteCompassBlockedCanvasTouch()
        {
            if (_compassDragBlockedCanvasTouchLogged) return;
            _compassDragBlockedCanvasTouchLogged = true;
            LogHelper.WriteLogToFile("圆规拖动期间拦截到画布触摸，已阻止画出多余线条", LogHelper.LogType.Warning);
        }

        /// <summary>
        /// 关闭圆规覆盖层（按钮关闭 / 退出批注模式 / 退出白板模式时调用）
        /// </summary>
        private void HideCompassTool()
        {
            // 若关闭时仍处于拖动中，先恢复 InkCanvas 出墨状态，避免恢复不了导致一直画不出线
            if (_isCompassDragSuppressingInk) CompassTool_DragEnded(null, EventArgs.Empty);

            if (CompassTool != null) CompassTool.Visibility = Visibility.Collapsed;
            _compassStroke = null;
            _compassStrokePoints.Clear();
        }

        /// <summary>
        /// 开始拖动圆规：临时关闭 InkCanvas 出墨。
        /// 触摸屏（鸿合白板等）上，圆规覆盖层的触摸/笔输入会同时被 InkCanvas 当成笔画，
        /// 拖动顶部手柄旋转时除了正常画圆还会多出一条画笔线；拖动期间关闭出墨即可避免。
        /// </summary>
        private void CompassTool_DragStarted(object sender, EventArgs e)
        {
            if (_isCompassDragSuppressingInk) return;

            _isCompassDragSuppressingInk = true;
            _compassDragBlockedCanvasTouchLogged = false;
            _compassStrokeCountBeforeDrag = inkCanvas.Strokes.Count;
            _compassEditingModeBeforeDrag = inkCanvas.EditingMode;

            if (inkCanvas.EditingMode != InkCanvasEditingMode.None)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
            }
        }

        /// <summary>结束拖动圆规：清理误产生的笔画并恢复出墨</summary>
        private void CompassTool_DragEnded(object sender, EventArgs e)
        {
            if (!_isCompassDragSuppressingInk) return;

            _isCompassDragSuppressingInk = false;
            RemoveStrayStrokesFromCompassDrag();
            inkCanvas.EditingMode = _compassEditingModeBeforeDrag;


        }

        /// <summary>
        /// 删除拖动圆规期间被 InkCanvas 误收集的笔画（保留圆规自身绘制的弧线笔画）。
        /// 按下瞬间 InkCanvas 可能已经开了一笔湿墨，切到 None 后这一笔会被提交，需要清掉。
        /// </summary>
        private void RemoveStrayStrokesFromCompassDrag()
        {
            try
            {
                // 拖动开始后新增的笔画里，除了圆规自己画的弧线，其余都是误收集的画笔笔画
                for (int i = inkCanvas.Strokes.Count - 1; i >= _compassStrokeCountBeforeDrag; i--)
                {
                    var stroke = inkCanvas.Strokes[i];
                    if (ReferenceEquals(stroke, _compassStroke)) continue;
                    inkCanvas.Strokes.Remove(stroke);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"清理圆规拖动时的多余笔画失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 圆规旋转画弧：把扫过的角度插值成密集点追加到当前笔画
        /// </summary>
        private void CompassTool_ArcDrawing(object sender, CompassArcEventArgs e)
        {
            if (TryBlockFrozenPageMutation("圆规绘制")) return;

            // 拖动过程中随时清掉 InkCanvas 误收集的笔画，避免多余线条在拖动期间一直可见
            if (_isCompassDragSuppressingInk) RemoveStrayStrokesFromCompassDrag();

            var newPoints = BuildCompassArcPoints(e.Pivot, e.Radius, e.FromAngleDeg, e.ToAngleDeg);

            if (e.StartNewStroke || _compassStroke == null)
            {
                _compassStrokePoints.Clear();
                _compassStrokePoints.AddRange(newPoints);
                _compassStroke = new Stroke(new StylusPointCollection(_compassStrokePoints))
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };
                _currentCommitType = CommitReason.ShapeDrawing;
                inkCanvas.Strokes.Add(_compassStroke);
            }
            else
            {
                _compassStrokePoints.AddRange(newPoints);
                _compassStroke.StylusPoints = new StylusPointCollection(_compassStrokePoints);
            }
        }

        /// <summary>
        /// 沿圆弧插值点（步长约 2px，走最短方向）
        /// </summary>
        private static IEnumerable<Point> BuildCompassArcPoints(Point pivot, double radius, double fromDeg, double toDeg)
        {
            double delta = ((toDeg - fromDeg + 540) % 360) - 180;
            double arcLen = Math.Abs(delta) / 180.0 * Math.PI * radius;
            int steps = Math.Max(1, (int)Math.Ceiling(arcLen / 2.0));
            var points = new List<Point>(steps);
            for (int i = 1; i <= steps; i++)
            {
                double a = (fromDeg + delta * i / steps) * Math.PI / 180;
                points.Add(new Point(pivot.X + radius * Math.Cos(a), pivot.Y - radius * Math.Sin(a)));
            }
            return points;
        }

        private void CompassTool_CloseRequested(object sender, EventArgs e)
        {
            // 只关闭圆规自身，其它常驻覆盖层工具保留
            HideCompassTool();
            UpdateAllGeometryButtonsCheckedState();
        }

        #endregion

        #region 常驻覆盖层工具（圆规 / 三角板 / 直尺）

        /// <summary>按钮是否属于圆规按钮</summary>
        private bool IsCompassButton(GeometryButton btn)
        {
            if (btn == null) return false;
            return btn == ShapeDrawPopupContent?.DrawCompassBtn
                || btn == BoardShapeDrawPopupContent?.DrawCompassBtn;
        }

        /// <summary>
        /// 圆规按钮点击：切换显示/隐藏。与三角板同策略（常驻覆盖层工具，关闭策略一致）。
        /// </summary>
        private void ToggleCompass()
        {
            if (CompassTool == null) return;

            if (CompassTool.Visibility == Visibility.Visible)
            {
                HideCompassTool();
            }
            else
            {
                CompassTool.Visibility = Visibility.Visible;
                CompassTool.ShowAtCenter();
            }

            UpdateAllGeometryButtonsCheckedState();
        }

        /// <summary>圆规按钮的选中态＝圆规是否可见</summary>
        private bool IsCompassChecked()
        {
            return CompassTool != null && CompassTool.Visibility == Visibility.Visible;
        }

        /// <summary>按钮是否对应第 2 块三角板（30-60°），否则为第 1 块（45°）</summary>
        private bool IsTriangleRuler2Button(GeometryButton btn)
        {
            if (btn == null) return false;
            return btn == ShapeDrawPopupContent?.DrawTriangleRuler2Btn
                || btn == BoardShapeDrawPopupContent?.DrawTriangleRuler2Btn;
        }

        /// <summary>按钮是否属于三角板按钮</summary>
        private bool IsTriangleRulerButton(GeometryButton btn)
        {
            if (btn == null) return false;
            return btn == ShapeDrawPopupContent?.DrawTriangleRulerBtn
                || btn == BoardShapeDrawPopupContent?.DrawTriangleRulerBtn
                || btn == ShapeDrawPopupContent?.DrawTriangleRuler2Btn
                || btn == BoardShapeDrawPopupContent?.DrawTriangleRuler2Btn;
        }

        /// <summary>按钮是否属于直尺按钮</summary>
        private bool IsStraightRulerButton(GeometryButton btn)
        {
            if (btn == null) return false;
            return btn == ShapeDrawPopupContent?.DrawStraightRulerBtn
                || btn == BoardShapeDrawPopupContent?.DrawStraightRulerBtn;
        }

        /// <summary>
        /// 三角板按钮点击：仅切换对应实例的显示/隐藏。
        /// 三角板是常驻覆盖层工具，不进入 drawingShapeMode、不改变书写状态，
        /// 除关闭按钮/退出批注模式/退出白板模式外一律保留（两块可同时打开）。
        /// </summary>
        private void ToggleTriangleRuler(GeometryButton btn)
        {
            bool isSecond = IsTriangleRuler2Button(btn);
            var ruler = isSecond ? TriangleRulerTool2 : TriangleRulerTool1;
            if (ruler == null) return;

            if (ruler.Visibility == Visibility.Visible)
            {
                ruler.Visibility = Visibility.Collapsed;
            }
            else
            {
                ruler.Visibility = Visibility.Visible;
                ruler.ShowAtCenter(isSecond ? TriangleRulerKind.ThirtySixty : TriangleRulerKind.IsoscelesRight);
            }

            UpdateAllGeometryButtonsCheckedState();
        }

        /// <summary>
        /// 直尺按钮点击：切换显示/隐藏。与三角板同策略（常驻覆盖层工具，关闭策略一致）。
        /// </summary>
        private void ToggleStraightRuler()
        {
            var ruler = StraightRulerTool1;
            if (ruler == null) return;

            if (ruler.Visibility == Visibility.Visible)
            {
                ruler.Visibility = Visibility.Collapsed;
            }
            else
            {
                ruler.Visibility = Visibility.Visible;
                ruler.ShowAtCenter();
            }

            UpdateAllGeometryButtonsCheckedState();
        }

        /// <summary>三角板按钮的选中态＝对应实例是否可见</summary>
        private bool IsTriangleRulerChecked(GeometryButton btn)
        {
            var ruler = IsTriangleRuler2Button(btn) ? TriangleRulerTool2 : TriangleRulerTool1;
            return ruler != null && ruler.Visibility == Visibility.Visible;
        }

        /// <summary>直尺按钮的选中态＝直尺是否可见</summary>
        private bool IsStraightRulerChecked()
        {
            return StraightRulerTool1 != null && StraightRulerTool1.Visibility == Visibility.Visible;
        }

        private void TriangleRulerTool_CloseRequested(object sender, EventArgs e)
        {
            // 只关闭被点击的那一块，另一块保留
            if (sender is TriangleRulerTool ruler) ruler.Visibility = Visibility.Collapsed;
            UpdateAllGeometryButtonsCheckedState();
        }

        private void StraightRulerTool_CloseRequested(object sender, EventArgs e)
        {
            // 只关闭直尺自身，三角板保留
            if (sender is StraightRulerTool ruler) ruler.Visibility = Visibility.Collapsed;
            UpdateAllGeometryButtonsCheckedState();
        }

        /// <summary>
        /// 关闭全部常驻覆盖层工具（圆规 + 两块三角板 + 直尺）：
        /// 仅用于「退出批注模式」与「退出白板模式」两个场景。
        /// </summary>
        internal void HideAllOverlayTools()
        {
            if (CompassTool != null) CompassTool.Visibility = Visibility.Collapsed;
            if (TriangleRulerTool1 != null) TriangleRulerTool1.Visibility = Visibility.Collapsed;
            if (TriangleRulerTool2 != null) TriangleRulerTool2.Visibility = Visibility.Collapsed;
            if (StraightRulerTool1 != null) StraightRulerTool1.Visibility = Visibility.Collapsed;
            _compassStroke = null;
            _compassStrokePoints.Clear();
            UpdateAllGeometryButtonsCheckedState();
        }

        #endregion

        /// <summary>
        /// 更新所有几何图形按钮的选中状态：普通图形按钮以 drawingShapeMode 为依据，
        /// 三角板/直尺按钮则以其覆盖层实例是否可见为依据（不占用 drawingShapeMode）。
        /// </summary>
        private void UpdateAllGeometryButtonsCheckedState()
        {
            foreach (var btn in GetAllVisibleGeometryButtons())
            {
                if (btn == null) continue;
                if (IsTriangleRulerButton(btn))
                {
                    btn.IsChecked = IsTriangleRulerChecked(btn);
                    continue;
                }
                if (IsStraightRulerButton(btn))
                {
                    btn.IsChecked = IsStraightRulerChecked();
                    continue;
                }
                if (IsCompassButton(btn))
                {
                    btn.IsChecked = IsCompassChecked();
                    continue;
                }
                int btnMode = GetDrawingModeForButton(btn);
                btn.IsChecked = (btnMode != 0 && btnMode == drawingShapeMode);
            }
        }

        /// <summary>
        /// 获取所有几何图形面板中的按钮
        /// </summary>
        private List<GeometryButton> GetAllVisibleGeometryButtons()
        {
            var list = new List<GeometryButton>();

            var content = ShapeDrawPopupContent;
            if (content != null)
            {
                list.Add(content.DrawLineBtn);
                list.Add(content.DrawDashedLineBtn);
                list.Add(content.DrawDotLineBtn);
                list.Add(content.DrawArrowBtn);
                list.Add(content.DrawParallelLineBtn);
                list.Add(content.DrawRectangleCenterBtn);
                list.Add(content.DrawCircleBtn);
                list.Add(content.DrawDashedCircleBtn);
                list.Add(content.DrawEllipseCenterBtn);
                list.Add(content.DrawEllipseCenterWithFocalPointBtn);
                list.Add(content.DrawCuboidBtn);
                list.Add(content.DrawRectangleBtn);
                list.Add(content.DrawCylinderBtn);
                list.Add(content.DrawConeBtn);
                list.Add(content.DrawPerpendicularBisectorBtn);
                list.Add(content.DrawArcBtn);
                list.Add(content.DrawBucketBtn);
                list.Add(content.DrawCoordinate1Btn);
                list.Add(content.DrawCoordinate2Btn);
                list.Add(content.DrawCoordinate3Btn);
                list.Add(content.DrawCoordinate4Btn);
                list.Add(content.DrawCoordinate5Btn);
                list.Add(content.DrawHyperbolaBtn);
                list.Add(content.DrawBezierBtn);
                list.Add(content.DrawParabola1Btn);
                list.Add(content.DrawParabolaWithFocalPointBtn);
                list.Add(content.DrawParabola2Btn);
                list.Add(content.DrawCompassBtn);
                list.Add(content.DrawTriangleRulerBtn);
                list.Add(content.DrawTriangleRuler2Btn);
                list.Add(content.DrawStraightRulerBtn);
            }

            var boardContent = BoardShapeDrawPopupContent;
            if (boardContent != null)
            {
                list.Add(boardContent.DrawLineBtn);
                list.Add(boardContent.DrawDashedLineBtn);
                list.Add(boardContent.DrawDotLineBtn);
                list.Add(boardContent.DrawArrowBtn);
                list.Add(boardContent.DrawParallelLineBtn);
                list.Add(boardContent.DrawRectangleCenterBtn);
                list.Add(boardContent.DrawCircleBtn);
                list.Add(boardContent.DrawDashedCircleBtn);
                list.Add(boardContent.DrawEllipseCenterBtn);
                list.Add(boardContent.DrawEllipseCenterWithFocalPointBtn);
                list.Add(boardContent.DrawCuboidBtn);
                list.Add(boardContent.DrawRectangleBtn);
                list.Add(boardContent.DrawCylinderBtn);
                list.Add(boardContent.DrawConeBtn);
                list.Add(boardContent.DrawPerpendicularBisectorBtn);
                list.Add(boardContent.DrawArcBtn);
                list.Add(boardContent.DrawBucketBtn);
                list.Add(boardContent.DrawCoordinate1Btn);
                list.Add(boardContent.DrawCoordinate2Btn);
                list.Add(boardContent.DrawCoordinate3Btn);
                list.Add(boardContent.DrawCoordinate4Btn);
                list.Add(boardContent.DrawCoordinate5Btn);
                list.Add(boardContent.DrawHyperbolaBtn);
                list.Add(boardContent.DrawBezierBtn);
                list.Add(boardContent.DrawParabola1Btn);
                list.Add(boardContent.DrawParabolaWithFocalPointBtn);
                list.Add(boardContent.DrawParabola2Btn);
                list.Add(boardContent.DrawCompassBtn);
                list.Add(boardContent.DrawTriangleRulerBtn);
                list.Add(boardContent.DrawTriangleRuler2Btn);
                list.Add(boardContent.DrawStraightRulerBtn);
            }

            return list;
        }

        /// <summary>
        /// 获取按钮对应的绘制模式（0 表示未知按钮）
        /// </summary>
        private int GetDrawingModeForButton(GeometryButton btn)
        {
            var content = ShapeDrawPopupContent;
            var boardContent = BoardShapeDrawPopupContent;

            if (btn == content?.DrawLineBtn || btn == boardContent?.DrawLineBtn) return 1;
            if (btn == content?.DrawArrowBtn || btn == boardContent?.DrawArrowBtn) return 2;
            if (btn == content?.DrawRectangleBtn || btn == boardContent?.DrawRectangleBtn) return 3;
            if (btn == content?.DrawCircleBtn || btn == boardContent?.DrawCircleBtn) return 5;
            if (btn == content?.DrawCylinderBtn || btn == boardContent?.DrawCylinderBtn) return 6;
            if (btn == content?.DrawConeBtn || btn == boardContent?.DrawConeBtn) return 7;
            if (btn == content?.DrawDashedLineBtn || btn == boardContent?.DrawDashedLineBtn) return 8;
            if (btn == content?.DrawCuboidBtn || btn == boardContent?.DrawCuboidBtn) return 9;
            if (btn == content?.DrawDashedCircleBtn || btn == boardContent?.DrawDashedCircleBtn) return 10;
            if (btn == content?.DrawCoordinate1Btn || btn == boardContent?.DrawCoordinate1Btn) return 11;
            if (btn == content?.DrawCoordinate2Btn || btn == boardContent?.DrawCoordinate2Btn) return 12;
            if (btn == content?.DrawCoordinate3Btn || btn == boardContent?.DrawCoordinate3Btn) return 13;
            if (btn == content?.DrawCoordinate4Btn || btn == boardContent?.DrawCoordinate4Btn) return 14;
            if (btn == content?.DrawParallelLineBtn || btn == boardContent?.DrawParallelLineBtn) return 15;
            if (btn == content?.DrawEllipseCenterBtn || btn == boardContent?.DrawEllipseCenterBtn) return 16;
            if (btn == content?.DrawCoordinate5Btn || btn == boardContent?.DrawCoordinate5Btn) return 17;
            if (btn == content?.DrawDotLineBtn || btn == boardContent?.DrawDotLineBtn) return 18;
            if (btn == content?.DrawRectangleCenterBtn || btn == boardContent?.DrawRectangleCenterBtn) return 19;
            if (btn == content?.DrawParabola1Btn || btn == boardContent?.DrawParabola1Btn) return 20;
            if (btn == content?.DrawParabola2Btn || btn == boardContent?.DrawParabola2Btn) return 21;
            if (btn == content?.DrawParabolaWithFocalPointBtn || btn == boardContent?.DrawParabolaWithFocalPointBtn) return 22;
            if (btn == content?.DrawEllipseCenterWithFocalPointBtn || btn == boardContent?.DrawEllipseCenterWithFocalPointBtn) return 23;
            if (btn == content?.DrawHyperbolaBtn || btn == boardContent?.DrawHyperbolaBtn) return 24;
            if (btn == content?.DrawBezierBtn || btn == boardContent?.DrawBezierBtn) return 25;
            if (btn == content?.DrawPerpendicularBisectorBtn || btn == boardContent?.DrawPerpendicularBisectorBtn) return 26;
            if (btn == content?.DrawArcBtn || btn == boardContent?.DrawArcBtn) return 27;
            if (btn == content?.DrawBucketBtn || btn == boardContent?.DrawBucketBtn) return PaintBucketShapeMode;
            // 圆规/三角板不占用绘制模式，由 IsCompassButton / IsTriangleRulerButton 单独处理（返回 0 = 非绘制模式按钮）

            return 0;
        }

        /// <summary>
        /// 处理画笔按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 当画笔按钮被点击时：
        /// 1. 禁用强制橡皮擦模式
        /// 2. 重置形状绘制模式
        /// 3. 设置墨水画布编辑模式为墨迹
        /// 4. 启用墨水画布的操作功能
        /// 5. 取消单指拖拽模式
        /// 6. 重置长按选中标志
        /// </remarks>
        private void BtnPen_Click(object sender, RoutedEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换到画笔")) return;
            if (drawingShapeMode == 15) CancelMidpointDrawing();
            if (drawingShapeMode == 17) CancelParallelDrawing();
            if (drawingShapeMode == 21) CancelPerpendicularDrawing();
            if (drawingShapeMode == 22) CancelAngleBisectorDrawing();
            if (drawingShapeMode == 26) CancelPerpendicularBisector();
            if (drawingShapeMode == TangentShapeMode) CancelTangentCircleDrawing();
            if (drawingShapeMode == AxisFlipShapeMode) CancelAxisFlip();
            ClearArcHandleHighlight();
            forceEraser = false;
            drawingShapeMode = 0;
            inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            isLongPressSelected = false;
            UpdateAllGeometryButtonsCheckedState();
        }

        /// <summary>
        /// 检查是否在多点触控模式下绘制形状
        /// </summary>
        /// <returns>返回一个表示操作成功的Task<bool></returns>
        /// <remarks>
        /// 检查多点触控模式并在需要时禁用：
        /// 1. 如果当前在多点触控模式下
        /// 2. 禁用多点触控模式
        /// 3. 记录之前的多点触控模式状态
        /// 4. 返回成功的任务
        /// </remarks>
        private Task<bool> CheckIsDrawingShapesInMultiTouchMode()
        {
            if (TryBlockFrozenPageMutation("绘制几何图形"))
                return Task.FromResult(false);

            if (isInMultiTouchMode)
            {
                ToggleSwitchEnableMultiTouchMode.IsOn = false;
                lastIsInMultiTouchMode = true;
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// 处理绘制直线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制直线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为直线
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        public async void BtnDrawLine_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 1;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制虚线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制虚线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为虚线
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawDashedLine_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 8;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制点线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制点线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为点线
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawDotLine_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 18;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制箭头按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制箭头按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为箭头
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawArrow_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 2;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制平行线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制平行线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为平行线
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawParallelLine_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 15;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制坐标系1按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制坐标系1按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为坐标系1
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCoordinate1_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 11;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制多边形按钮点击事件（模式 12）
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制多边形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为多边形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCoordinate2_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 12;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理「过圆外一点作圆的切线」按钮点击事件（模式 13）
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当该按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为圆的切线（拖动式：按住圆拖到圆外一点）
        /// 6. 清掉上一次的选中圆、高亮与预览
        /// 7. 设置墨水画布编辑模式为无
        /// 8. 启用墨水画布的操作功能
        /// 9. 取消单指拖拽模式
        /// 10. 提示操作方法
        /// </remarks>
        private async void BtnDrawCoordinate3_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = TangentShapeMode;
            ResetTangentCircleState();
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
            ShowCanvasToast("按住圆拖到圆外一点");
        }

        /// <summary>
        /// 处理绘制直角三角形按钮点击事件（模式 14）
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制直角三角形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为直角三角形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCoordinate4_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 14;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制坐标系5按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制平行线（原坐标系5按钮）被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为平行线
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCoordinate5_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 17;
            drawMultiStepShapeCurrentStep = 0;
            _parallelLastPreview = null;
            _parallelBaselineStroke = null;
            _parallelUpPoint = null;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
            ShowCanvasToast("第一步：点击一条直线");
        }

        /// <summary>
        /// 处理绘制矩形按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制矩形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为矩形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawRectangle_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 3;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制中心矩形按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制中心矩形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为中心矩形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawRectangleCenter_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 19;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制椭圆按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制椭圆按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为椭圆
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 4;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制圆形按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制圆形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为圆形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCircle_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 5;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制中心椭圆按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制中心椭圆按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为中心椭圆
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCenterEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 16;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制带焦点的中心椭圆按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制带焦点的中心椭圆按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为带焦点的中心椭圆
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCenterEllipseWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 23;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制虚线圆形按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制虚线圆形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为虚线圆形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawDashedCircle_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 10;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制双曲线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制双曲线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为双曲线
        /// 6. 重置多步形状绘制当前步骤
        /// 7. 设置墨水画布编辑模式为无
        /// 8. 启用墨水画布的操作功能
        /// 9. 取消单指拖拽模式
        /// 10. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawHyperbola_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 24;
            drawMultiStepShapeCurrentStep = 0;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制抛物线1按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制抛物线1按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为抛物线1
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawParabola1_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 20;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制角平分线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制角平分线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为角平分线
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawParabolaWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 22;
            // 点选式：每次进入工具都清掉上一次选中的边与高亮，从「点第一条边」开始
            CancelAngleBisectorDrawing();
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
            ShowCanvasToast("第一步：点击角的一条边");
        }

        /// <summary>
        /// 处理绘制垂线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制垂线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为垂线
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawParabola2_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 21;
            drawMultiStepShapeCurrentStep = 0;
            _perpendicularLastPreview = null;
            _perpendicularBaselineStroke = null;
            _perpendicularUpPoint = null;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
            ShowCanvasToast("第一步：点击一条直线");
        }

        /// <summary>
        /// 处理绘制圆柱按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制圆柱按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为圆柱
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCylinder_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 6;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制圆锥按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制圆锥按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为圆锥
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCone_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 7;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制长方体按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制长方体按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为长方体
        /// 6. 重置长方体绘制的首次触摸标志
        /// 7. 初始化长方体前方面矩形的起始点和结束点
        /// 8. 设置墨水画布编辑模式为无
        /// 9. 启用墨水画布的操作功能
        /// 10. 取消单指拖拽模式
        /// 11. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCuboid_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 9;
            isFirstTouchCuboid = true;
            CuboidFrontRectIniP = new Point();
            CuboidFrontRectEndP = new Point();
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        #endregion

        /// <summary>
        /// 处理墨水画布的触摸移动事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">触摸事件参数</param>
        /// <remarks>
        /// 当在墨水画布上发生触摸移动时：
        /// 1. 如果是单指拖拽模式，直接返回
        /// 2. 如果是形状绘制模式：
        ///    - 如果等待下一次触摸按下，直接返回
        ///    - 如果触摸点数量大于1，设置等待标志并清理临时笔画
        ///    - 确保墨水画布编辑模式为无
        /// 3. 调用MouseTouchMove方法处理触摸点位置
        /// </remarks>
        private void inkCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            // 视频展台特殊模式：触摸移动交给 VideoPresenterSpecialModeContainer 的 Manipulation 处理
            // 图形绘制模式例外：需要走正常绘制流程
            if (_isVideoPresenterSpecialMode && drawingShapeMode == 0) return;

            if (isSingleFingerDragMode) return;
            // 多边形：顶点由抬起时的点击事件维护，这里不参与拖动绘制（不拦截以便触摸提升为鼠标事件）
            if (drawingShapeMode == 12) return;
            if (drawingShapeMode != 0)
            {
                //EraserContainer.Background = null;
                //ImageEraser.Visibility = Visibility.Visible;
                if (isWaitUntilNextTouchDown) return;
                if (dec.Count > 1)
                {
                    isWaitUntilNextTouchDown = true;
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }

                    return;
                }

                if (inkCanvas.EditingMode != InkCanvasEditingMode.None)
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
            }

            MouseTouchMove(e.GetTouchPoint(inkCanvas).Position);
        }

        /// <summary>
        /// 多笔完成的图形 当前所处在的笔画步骤
        /// </summary>
        private int drawMultiStepShapeCurrentStep;

        /// <summary>
        /// 多笔完成的图形 当前所处在的笔画集合
        /// </summary>
        private StrokeCollection drawMultiStepShapeSpecialStrokeCollection = new StrokeCollection();

        /// <summary>
        /// 当前本次双曲线的渐近线笔画标记（每次绘制生成新 GUID）。
        /// 用标记而非引用集合来删除渐近线，规避引用跟踪失效导致“删不掉渐近线”的问题。
        /// </summary>
        private Guid _currentAsymptoteTagGuid;

        /// <summary>
        /// 给一批渐近线笔画打上指定标记。
        /// </summary>
        private void TagAsymptoteStrokes(StrokeCollection strokes, Guid tag)
        {
            if (strokes == null) return;
            foreach (var s in strokes)
            {
                try
                {
                    if (!s.ContainsPropertyData(tag)) s.AddPropertyData(tag, true);
                }
                catch { }
            }
        }

        /// <summary>
        /// 从画布移除所有带指定标记的渐近线笔画（仅当前本次双曲线）。
        /// </summary>
        private void RemoveAsymptotesByTag(Guid tag)
        {
            try
            {
                if (inkCanvas == null) return;
                var toRemove = new List<Stroke>();
                foreach (var s in inkCanvas.Strokes)
                {
                    if (s.ContainsPropertyData(tag)) toRemove.Add(s);
                }
                foreach (var s in toRemove) inkCanvas.Strokes.Remove(s);
            }
            catch { }
        }

        /// <summary>
        /// 双曲线绘制时上一帧预览（渐近线克隆体 + 双曲线），用于跨帧清理。
        /// 不能用 lastTempStrokeCollection：该方法末尾会将其置 null，导致拖动时上一帧双曲线无法被移除而堆叠。
        /// </summary>
        private StrokeCollection _hyperbolaLastPreview;

        //double drawMultiStepShapeSpecialParameter1 = 0.0; //多笔完成的图形 特殊参数 通常用于表示a
        //double drawMultiStepShapeSpecialParameter2 = 0.0; //多笔完成的图形 特殊参数 通常用于表示b

        /// <summary>
        /// 多笔完成的图形 特殊参数 通常用于表示k
        /// </summary>
        private double drawMultiStepShapeSpecialParameter3;

        // 贝塞尔曲线（模式 25）专用：跨帧预览清理跟踪与第二端点缓存
        private StrokeCollection _bezierLastPreview;
        private Point _bezierEndPoint = default;

        // 垂线（模式 21）专用：跨帧预览清理跟踪、基准线笔画与端点
        private StrokeCollection _perpendicularLastPreview;
        private Stroke _perpendicularBaselineStroke;
        private Point _perpendicularBaseStart = default;
        private Point _perpendicularBaseEnd = default;
        // 触摸/手写笔抬起时传入的落点（鼠标抬起直接取事件坐标）
        private Point? _perpendicularUpPoint;

        // 中垂线：触摸/手写笔抬起时的真实落点，用于补画最后一帧（避免中点停在旧位置）
        private Point? _perpendicularBisectorUpPoint;

        // 中垂线（模式 26）专用：拖动时的荧光笔线段与定稿笔画（平分线 + 直角标记）
        private Stroke _perpendicularBisectorGuide;
        private StrokeCollection _perpendicularBisectorResult;

        // 角平分线（模式 22）专用：两条边的高亮、预览（平分线 + 等角弧）与顶点/端点
        private StrokeCollection _angleBisectorLastPreview;
        private Stroke _angleBisectorSide1Stroke;
        private Stroke _angleBisectorSide2Stroke;
        private Point _angleBisectorVertex = default;
        private Point _angleBisectorSide1End = default;
        // 角平分线两条被点选的边：直线端点 + 点选位置（点选位置决定取哪个方向的射线）
        private Point _angleBisectorSide1LineStart = default;
        private Point _angleBisectorSide1LineEnd = default;
        private Point _angleBisectorSide1PickPoint = default;
        private Point _angleBisectorSide2LineStart = default;
        private Point _angleBisectorSide2LineEnd = default;
        private Point _angleBisectorSide2PickPoint = default;
        // 触摸/手写笔抬起时传入的落点（鼠标抬起直接取事件坐标）
        private Point? _angleBisectorUpPoint;

        // 平行线（模式 17）专用：跨帧预览清理跟踪、基准线笔画与端点（交互与垂线一致）
        private StrokeCollection _parallelLastPreview;
        private Stroke _parallelBaselineStroke;
        private Point _parallelBaseStart = default;
        private Point _parallelBaseEnd = default;
        // 触摸/手写笔抬起时传入的落点（鼠标抬起直接取事件坐标）
        private Point? _parallelUpPoint;

        // 中点（模式 15）专用：拖动时的荧光笔线段与定稿的中点标记（交互与中垂线一致）
        private Stroke _midpointGuide;
        private StrokeCollection _midpointResult;
        /// <summary>中点本次手势是否已经收尾（收尾后再补来的帧要丢弃）。</summary>
        private bool _midpointAwaitNewGesture;
        /// <summary>中点上一帧的笔画（荧光笔线段 + 中点标记预览），每帧开头据此清理旧帧。</summary>
        private StrokeCollection _midpointLastFrame;

        // 多边形（模式 12）专用：单击添加顶点，点击起点闭合完成
        private readonly List<Point> _polygonVertices = new List<Point>();
        private Stroke _polygonPreviewStroke;

        /// <summary>
        /// 判定“点击起点闭合”的命中半径（画布像素）
        /// </summary>
        private const double PolygonCloseHitRadius = 16;

        /// <summary>
        /// 多边形点位是否处于绘制过程中
        /// </summary>
        private bool IsPolygonDrawing => drawingShapeMode == 12 && _polygonVertices.Count > 0;

        #region 形状绘制主函数

        /// <summary>
        /// 处理鼠标或触摸移动事件，用于形状绘制
        /// </summary>
        /// <param name="endP">结束点坐标</param>
        /// <remarks>
        /// 当鼠标或触摸在画布上移动时：
        /// 1. 禁用原有的FitToCurve，使用新的高级贝塞尔曲线平滑
        /// 2. 在绘制过程中禁用浮动栏交互，避免干扰绘制
        /// 3. 根据当前的绘制模式，生成不同的形状：
        ///    - 直线
        ///    - 虚线
        ///    - 点线
        ///    - 箭头
        ///    - 平行线
        ///    - 坐标系（模式 11 / 17）
        ///    - 多边形（模式 12，单击加点、闭合完成，不走拖动绘制）
        ///    - 等边/直角三角形（模式 13 / 14）
        ///    - 矩形
        ///    - 中心矩形
        ///    - 椭圆
        ///    - 圆形
        ///    - 中心椭圆
        ///    - 带焦点的中心椭圆
        ///    - 虚线圆形
        ///    - 双曲线
        ///    - 带焦点的双曲线
        ///    - 抛物线
        ///    - 带焦点的抛物线
        ///    - 垂线（模式 21，两步：基准线 + 点）
        ///    - 圆柱
        ///    - 圆锥
        ///    - 长方体
        /// 4. 移除之前的临时笔画，添加新生成的笔画
        /// </remarks>
        private void MouseTouchMove(Point endP)
        {
            // 禁用原有的FitToCurve，使用新的高级贝塞尔曲线平滑
            if (Settings.Canvas.FitToCurve) drawingAttributes.FitToCurve = false;
            // 在绘制过程中禁用浮动栏交互，避免干扰绘制
            ViewboxFloatingBar.IsHitTestVisible = false;
            BlackboardUIGridForInkReplay.IsHitTestVisible = false;
            List<Point> pointList;
            StylusPointCollection point;
            Stroke stroke;
            var strokes = new StrokeCollection();
            var newIniP = iniP;
            switch (drawingShapeMode)
            {
                case 1:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    pointList = new List<Point> {
                        new Point(iniP.X, iniP.Y),
                        new Point(endP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);
                    break;
                case 8:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    strokes.Add(GenerateDashedLineStrokeCollection(iniP, endP));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }

                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 18:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    strokes.Add(GenerateDotLineStrokeCollection(iniP, endP));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }

                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 2:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    double w = 15, h = 10;
                    var theta = Math.Atan2(iniP.Y - endP.Y, iniP.X - endP.X);
                    var sint = Math.Sin(theta);
                    var cost = Math.Cos(theta);

                    pointList = new List<Point> {
                        new Point(iniP.X, iniP.Y),
                        new Point(endP.X, endP.Y),
                        new Point(endP.X + (w * cost - h * sint), endP.Y + (w * sint + h * cost)),
                        new Point(endP.X, endP.Y),
                        new Point(endP.X + (w * cost + h * sint), endP.Y - (h * cost - w * sint))
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);
                    break;
                case 15:
                    // 中点（交互与中垂线一致）：拖出一条线段（荧光笔辅助），实时给出它的中点标记；
                    // 松手时移除荧光笔线段，只保留中点标记
                    // 收尾后同一手势补来的帧直接丢弃，避免再落下一个点
                    if (_midpointAwaitNewGesture) return;
                    _currentCommitType = CommitReason.ShapeDrawing;
                    {
                        var segment = endP - iniP;
                        // 拖动距离过小（单击或手抖）不生成，避免落下退化标记
                        if (segment.Length < 8) return;

                        var mid = new Point((iniP.X + endP.X) / 2, (iniP.Y + endP.Y) / 2);
                        var result = BuildMidpointStrokes(mid);

                        // 拖动阶段的预览：荧光笔线段 + 中点标记
                        _midpointGuide = GenerateMidpointGuideStroke(iniP, endP);
                        _midpointResult = result;
                        strokes.Add(_midpointGuide);
                        foreach (Stroke resultStroke in result) strokes.Add(resultStroke);

                        // 逐条清理上一帧：用本模式自己的字段跟踪，避免旧帧残留
                        RemoveStrokesSafe(_midpointLastFrame);
                        RemoveStrokesSafe(lastTempStrokeCollection);

                        _midpointLastFrame = strokes;
                        lastTempStrokeCollection = strokes;
                        inkCanvas.Strokes.Add(strokes);
                        break;
                    }
                case 11:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    // 本次绘制的坐标系分组标记：绘制完成后据此把整组笔画注册为「轴长可调」
                    if (_pendingCoordinateTag == Guid.Empty) _pendingCoordinateTag = Guid.NewGuid();
                    {
                        // 拖拽方向决定两条轴的正方向，拖拽距离决定正方向臂长；
                        // 负方向臂长沿用原绘制规则：X 负臂比正臂短 20，Y 负臂比正臂长 20
                        var axisX = new Vector(endP.X < iniP.X ? -1 : 1, 0);
                        var axisY = new Vector(0, endP.Y < iniP.Y ? -1 : 1);
                        var armX = Math.Max(Math.Abs(endP.X - iniP.X), 24);
                        var armY = Math.Max(Math.Abs(endP.Y - iniP.Y), 24);
                        var arms = new CoordinateArms
                        {
                            XPlus = armX,
                            XMinus = Math.Max(armX - 20, 24),
                            YPlus = armY,
                            YMinus = armY + 20
                        };
                        strokes = BuildCoordinateSystemStrokes(iniP, axisX, axisY, arms, _pendingCoordinateTag);
                    }
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }

                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 12:
                    // 多边形：单击添加顶点、点击起点闭合完成，不使用拖动绘制，
                    // 顶点与预览由 HandlePolygonClick / UpdatePolygonPreview 维护。
                    return;
                case TangentShapeMode:
                    // 圆的切线：按住圆拖动时实时预览两条切线，松手由 CommitTangentDrag 提交
                    UpdateTangentPreview(endP);
                    return;
                case 14: // 直角三角形：起点为直角顶点，横纵拖拽距离为两条直角边
                    _currentCommitType = CommitReason.ShapeDrawing;
                    {
                        var rightWidth = endP.X - iniP.X;
                        var rightHeight = endP.Y - iniP.Y;
                        if (Math.Abs(rightWidth) < 0.01 || Math.Abs(rightHeight) < 0.01) return;

                        var rightTriangleVertices = new List<Point>
                        {
                            new Point(iniP.X, iniP.Y),
                            new Point(endP.X, iniP.Y),
                            new Point(iniP.X, endP.Y)
                        };

                        var triangleAttributes = inkCanvas.DefaultDrawingAttributes.Clone();
                        var triangleStrokes = new StrokeCollection();

                        // 三条边：内切圆的切点（圆心到边的垂足）把每条边切成两段，
                        // 与切线工具的分段方式一致，便于后续选中/擦除/分段操作
                        var hasIncircle = TryGetTriangleIncircle(rightTriangleVertices,
                            out var incircleCenter, out var incircleRadius);
                        if (hasIncircle)
                        {
                            var splitEdges = BuildEdgesSplitAtTangency(rightTriangleVertices,
                                incircleCenter, incircleRadius, triangleAttributes);
                            if (splitEdges != null)
                            {
                                foreach (Stroke edgeStroke in splitEdges) triangleStrokes.Add(edgeStroke);
                            }
                        }

                        if (triangleStrokes.Count == 0)
                        {
                            var closedTriangle = CreateClosedPolygonStroke(rightTriangleVertices, triangleAttributes);
                            if (closedTriangle != null) triangleStrokes.Add(closedTriangle);
                        }

                        // 直角标记：以直角顶点（起点）为角点，沿两条直角边各取一段，画成小方块
                        const double rightMarkSize = 14;
                        var markDirX = new Vector(Math.Sign(rightWidth), 0);
                        var markDirY = new Vector(0, Math.Sign(rightHeight));
                        var markP1 = iniP + markDirX * rightMarkSize;
                        var markP2 = iniP + markDirY * rightMarkSize;
                        var markCorner = markP1 + markDirY * rightMarkSize;
                        triangleStrokes.Add(new Stroke(new StylusPointCollection(new List<Point>
                        {
                            markP1,
                            markCorner,
                            markP2
                        }))
                        {
                            DrawingAttributes = triangleAttributes.Clone()
                        });

                        // 直角三角形带内切圆：切点同样把圆切成几段圆弧（与边的分段点一致）
                        if (hasIncircle)
                        {
                            var tangencyPoints = GetPolygonTangencyPoints(rightTriangleVertices, incircleCenter, incircleRadius);
                            var incircleArcs = BuildCircleArcsSplitAtTangency(incircleCenter, incircleRadius,
                                tangencyPoints, triangleAttributes);
                            if (incircleArcs != null)
                            {
                                foreach (Stroke arcStroke in incircleArcs) triangleStrokes.Add(arcStroke);
                            }
                        }

                        RemoveStrokesSafe(lastTempStrokeCollection);
                        lastTempStrokeCollection = triangleStrokes;
                        inkCanvas.Strokes.Add(triangleStrokes);
                        break;
                    }
                case 17:
                    // 平行线：第一步已在按下时点选一条已有直线作为基准（BeginBaselinePickAt），
                    // 这里只处理第二步——点击（或拖动）给出一点，过该点作基准线的平行线
                    if (drawMultiStepShapeCurrentStep == 0) return;

                    _currentCommitType = CommitReason.ShapeDrawing;
                    {
                        var baseVector = _parallelBaseEnd - _parallelBaseStart;
                        var baseLength = baseVector.Length;
                        if (baseLength < 0.01) return;
                        baseVector.Normalize();

                        // 只清理上一帧的平行线预览，基准辅助线保持到完成时再移除
                        try { inkCanvas.Strokes.Remove(_parallelLastPreview); }
                        catch { }

                        // 平行线：方向与基准线一致、过所给点，长度取基准线长度（至少 48px）并居中于该点
                        var half = Math.Max(baseLength / 2, 24);
                        strokes.Add(GenerateParallelStroke(endP - baseVector * half, endP + baseVector * half));

                        lastTempStrokeCollection = strokes;
                        inkCanvas.Strokes.Add(strokes);
                        _parallelLastPreview = strokes;
                        break;
                    }
                case 3:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    pointList = new List<Point> {
                        new Point(iniP.X, iniP.Y),
                        new Point(iniP.X, endP.Y),
                        new Point(endP.X, endP.Y),
                        new Point(endP.X, iniP.Y),
                        new Point(iniP.X, iniP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);
                    break;
                case 19:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    var a = iniP.X - endP.X;
                    var b = iniP.Y - endP.Y;
                    pointList = new List<Point> {
                        new Point(iniP.X - a, iniP.Y - b),
                        new Point(iniP.X - a, iniP.Y + b),
                        new Point(iniP.X + a, iniP.Y + b),
                        new Point(iniP.X + a, iniP.Y - b),
                        new Point(iniP.X - a, iniP.Y - b)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);
                    break;
                case 4:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    pointList = GenerateEllipseGeometry(iniP, endP);
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);
                    break;
                case 5:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    var R = GetDistance(iniP, endP);
                    pointList = GenerateEllipseGeometry(new Point(iniP.X - R, iniP.Y - R),
                        new Point(iniP.X + R, iniP.Y + R));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);

                    // 如果启用了圆心标记功能，则绘制圆心
                    if (Settings.Canvas.ShowCircleCenter)
                    {
                        DrawCircleCenter(iniP);
                    }
                    break;
                case 16:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    var halfA = endP.X - iniP.X;
                    var halfB = endP.Y - iniP.Y;
                    pointList = GenerateEllipseGeometry(new Point(iniP.X - halfA, iniP.Y - halfB),
                        new Point(iniP.X + halfA, iniP.Y + halfB));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                    lastTempStroke = stroke;
                    inkCanvas.Strokes.Add(stroke);
                    break;
                case 23:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    a = Math.Abs(endP.X - iniP.X);
                    b = Math.Abs(endP.Y - iniP.Y);
                    pointList = GenerateEllipseGeometry(new Point(iniP.X - a, iniP.Y - b),
                        new Point(iniP.X + a, iniP.Y + b));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke);
                    var c = Math.Sqrt(Math.Abs(a * a - b * b));
                    StylusPoint stylusPoint;
                    if (a > b)
                    {
                        stylusPoint = new StylusPoint(iniP.X + c, iniP.Y, (float)1.0);
                        point = new StylusPointCollection();
                        point.Add(stylusPoint);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                        stylusPoint = new StylusPoint(iniP.X - c, iniP.Y, (float)1.0);
                        point = new StylusPointCollection();
                        point.Add(stylusPoint);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                    }
                    else if (a < b)
                    {
                        stylusPoint = new StylusPoint(iniP.X, iniP.Y - c, (float)1.0);
                        point = new StylusPointCollection();
                        point.Add(stylusPoint);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                        stylusPoint = new StylusPoint(iniP.X, iniP.Y + c, (float)1.0);
                        point = new StylusPointCollection();
                        point.Add(stylusPoint);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                    }

                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 10:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    R = GetDistance(iniP, endP);
                    strokes = GenerateDashedLineEllipseStrokeCollection(new Point(iniP.X - R, iniP.Y - R),
                        new Point(iniP.X + R, iniP.Y + R));
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }

                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 24:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    //双曲线 x^2/a^2 - y^2/b^2 = 1
                    if (Math.Abs(iniP.X - endP.X) < 0.01 || Math.Abs(iniP.Y - endP.Y) < 0.01) return;
                    // 移除上一帧预览（lastTempStrokeCollection 在方法末尾会被置 null，无法跨帧清理，
                    // 故用专用字段 _hyperbolaLastPreview 跟踪，避免拖动时上一帧双曲线/渐近线不断堆叠残留）。
                    try { inkCanvas.Strokes.Remove(_hyperbolaLastPreview); }
                    catch { }
                    var pointList2 = new List<Point>();
                    var pointList3 = new List<Point>();
                    var pointList4 = new List<Point>();
                    if (drawMultiStepShapeCurrentStep == 0)
                    {
                        //第一笔：画渐近线
                        var k = Math.Abs((endP.Y - iniP.Y) / (endP.X - iniP.X));
                        strokes.Add(
                            GenerateDashedLineStrokeCollection(new Point(2 * iniP.X - endP.X, 2 * iniP.Y - endP.Y),
                                endP));
                        strokes.Add(GenerateDashedLineStrokeCollection(new Point(2 * iniP.X - endP.X, endP.Y),
                                new Point(endP.X, 2 * iniP.Y - endP.Y)));
                        drawMultiStepShapeSpecialParameter3 = k;
                        _currentAsymptoteTagGuid = Guid.NewGuid();
                        drawMultiStepShapeSpecialStrokeCollection = strokes;
                        // 给本次双曲线的渐近线打标记，结尾按标记可靠删除
                        TagAsymptoteStrokes(drawMultiStepShapeSpecialStrokeCollection, _currentAsymptoteTagGuid);
                    }
                    else
                    {
                        //第二笔：画双曲线
                        // 先将第一笔的渐近线添加到 strokes 中
                        if (drawMultiStepShapeSpecialStrokeCollection != null && drawMultiStepShapeSpecialStrokeCollection.Count > 0)
                        {
                            // 第一笔的渐近线原始笔画此刻仍在画布上（上一笔末尾 lastTempStrokeCollection 已被置空，
                            // 旧的上一帧预览清理不会再移除它们）。必须先显式从画布移除，否则：
                            // 1) 结尾删除（line ~3469）只能删到克隆体，原始渐近线会残留（表现为“删不掉渐近线”）；
                            // 2) 下面再次 Add 相同引用会抛“重复笔画”异常。
                            try { inkCanvas.Strokes.Remove(drawMultiStepShapeSpecialStrokeCollection); }
                            catch { }

                            // 记录真正要画到画布上的渐近线（克隆体），供结尾删除使用。
                            // 必须 Clone：每次鼠标移动都会重建 strokes 并重新 Add，复用原始引用会与画布上已有引用冲突。
                            var asymptoteStrokesOnCanvas = new StrokeCollection();
                            foreach (var asymptoteStroke in drawMultiStepShapeSpecialStrokeCollection)
                            {
                                var clone = asymptoteStroke.Clone();
                                strokes.Add(clone);
                                asymptoteStrokesOnCanvas.Add(clone);
                            }
                            // 让结尾的删除（line ~3469）命中画布上实际存在的克隆体渐近线
                            drawMultiStepShapeSpecialStrokeCollection = asymptoteStrokesOnCanvas;
                            // 克隆体也打上本次标记：结尾按标记删除，彻底摆脱引用跟踪
                            TagAsymptoteStrokes(drawMultiStepShapeSpecialStrokeCollection, _currentAsymptoteTagGuid);
                        }

                        var k = drawMultiStepShapeSpecialParameter3;
                        var isHyperbolaFocalPointOnXAxis = Math.Abs((endP.Y - iniP.Y) / (endP.X - iniP.X)) < k;
                        if (isHyperbolaFocalPointOnXAxis)
                        {
                            // 焦点在 x 轴上
                            a = Math.Sqrt(Math.Abs((endP.X - iniP.X) * (endP.X - iniP.X) -
                                                   (endP.Y - iniP.Y) * (endP.Y - iniP.Y) / (k * k)));
                            b = a * k;
                            pointList = new List<Point>();
                            for (var i = a; i <= Math.Abs(endP.X - iniP.X); i += 0.5)
                            {
                                var rY = Math.Sqrt(Math.Abs(k * k * i * i - b * b));
                                pointList.Add(new Point(iniP.X + i, iniP.Y - rY));
                                pointList2.Add(new Point(iniP.X + i, iniP.Y + rY));
                                pointList3.Add(new Point(iniP.X - i, iniP.Y - rY));
                                pointList4.Add(new Point(iniP.X - i, iniP.Y + rY));
                            }
                        }
                        else
                        {
                            // 焦点在 y 轴上
                            a = Math.Sqrt(Math.Abs((endP.Y - iniP.Y) * (endP.Y - iniP.Y) -
                                                   (endP.X - iniP.X) * (endP.X - iniP.X) * (k * k)));
                            b = a / k;
                            pointList = new List<Point>();
                            for (var i = a; i <= Math.Abs(endP.Y - iniP.Y); i += 0.5)
                            {
                                var rX = Math.Sqrt(Math.Abs(i * i / k / k - b * b));
                                pointList.Add(new Point(iniP.X - rX, iniP.Y + i));
                                pointList2.Add(new Point(iniP.X + rX, iniP.Y + i));
                                pointList3.Add(new Point(iniP.X - rX, iniP.Y - i));
                                pointList4.Add(new Point(iniP.X + rX, iniP.Y - i));
                            }
                        }

                        try
                        {
                            point = new StylusPointCollection(pointList);
                            stroke = new Stroke(point)
                            { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                            strokes.Add(stroke.Clone());
                            point = new StylusPointCollection(pointList2);
                            stroke = new Stroke(point)
                            { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                            strokes.Add(stroke.Clone());
                            point = new StylusPointCollection(pointList3);
                            stroke = new Stroke(point)
                            { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                            strokes.Add(stroke.Clone());
                            point = new StylusPointCollection(pointList4);
                            stroke = new Stroke(point)
                            { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                            strokes.Add(stroke.Clone());
                        }
                        catch
                        {
                            // 双曲线点列为空等异常时提前返回：上一帧预览已在 case 开头清理，这里保险再清一次
                            try { inkCanvas.Strokes.Remove(_hyperbolaLastPreview); }
                            catch { }
                            return;
                        }
                    }

                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    // 记录本帧预览（含渐近线克隆体 + 双曲线），供下一帧开头清理
                    // （lastTempStrokeCollection 在方法末尾会被置 null，无法用于跨帧清理）
                    _hyperbolaLastPreview = strokes;
                    break;
                case 25:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    // 二次贝塞尔曲线：第一步拖出两端点 P0→P2（弦），第二步拖出控制点 P1，实时预览
                    if (drawMultiStepShapeCurrentStep == 0)
                    {
                        // 第一笔：记录两端点（P0=iniP，P2=endP），并以辅助弦作为预览
                        if (Math.Abs(iniP.X - endP.X) < 0.01 && Math.Abs(iniP.Y - endP.Y) < 0.01) return;
                        // 移除上一帧预览，避免拖动时堆叠残留
                        try { inkCanvas.Strokes.Remove(_bezierLastPreview); }
                        catch { }

                        _bezierEndPoint = endP;
                        var bezierChord = new Stroke(new StylusPointCollection(new List<Point> { iniP, endP }))
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(bezierChord);
                        // 第一步的辅助弦在第二步会被重新克隆加入，结尾统一移除（只保留曲线本身）
                        drawMultiStepShapeSpecialStrokeCollection = strokes;
                        lastTempStrokeCollection = strokes;
                    }
                    else
                    {
                        // 第二笔：P0=iniP（第一步起点，固定），P2=_bezierEndPoint，P1=endP（控制点），绘制二次贝塞尔曲线
                        if (Math.Abs(iniP.X - _bezierEndPoint.X) < 0.01 && Math.Abs(iniP.Y - _bezierEndPoint.Y) < 0.01) return;
                        try { inkCanvas.Strokes.Remove(_bezierLastPreview); }
                        catch { }

                        var p0 = iniP;
                        var p2 = _bezierEndPoint;
                        var p1 = endP;
                        var bezPoints = new List<Point>();
                        const int bezierSegments = 64;
                        for (var i = 0; i <= bezierSegments; i++)
                        {
                            var t = (double)i / bezierSegments;
                            var u = 1 - t;
                            var bx = u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X;
                            var by = u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y;
                            bezPoints.Add(new Point(bx, by));
                        }

                        var bezierCurve = new Stroke(new StylusPointCollection(bezPoints))
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };

                        // 重新加入第一步的辅助弦（克隆），供结尾统一移除
                        var guideStrokesOnCanvas = new StrokeCollection();
                        if (drawMultiStepShapeSpecialStrokeCollection != null && drawMultiStepShapeSpecialStrokeCollection.Count > 0)
                        {
                            try { inkCanvas.Strokes.Remove(drawMultiStepShapeSpecialStrokeCollection); }
                            catch { }

                            foreach (var guideStroke in drawMultiStepShapeSpecialStrokeCollection)
                            {
                                var clone = guideStroke.Clone();
                                strokes.Add(clone);
                                guideStrokesOnCanvas.Add(clone);
                            }

                            drawMultiStepShapeSpecialStrokeCollection = guideStrokesOnCanvas;
                        }

                        strokes.Add(bezierCurve);
                        // 撤销历史只记录曲线本身，辅助弦（已统一移除）不进入历史，避免撤销时残留辅助线
                        lastTempStrokeCollection = new StrokeCollection { bezierCurve };
                    }

                    inkCanvas.Strokes.Add(strokes);
                    // 记录本帧预览（含辅助弦 + 曲线），供下一帧开头清理
                    // （lastTempStrokeCollection 方法末尾会被置 null，无法用于跨帧清理）
                    _bezierLastPreview = strokes;
                    break;
                case 20:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    //抛物线 y=ax^2
                    if (Math.Abs(iniP.X - endP.X) < 0.01 || Math.Abs(iniP.Y - endP.Y) < 0.01) return;
                    a = (iniP.Y - endP.Y) / ((iniP.X - endP.X) * (iniP.X - endP.X));
                    pointList = new List<Point>();
                    pointList2 = new List<Point>();
                    for (var i = 0.0; i <= Math.Abs(endP.X - iniP.X); i += 0.5)
                    {
                        pointList.Add(new Point(iniP.X + i, iniP.Y - a * i * i));
                        pointList2.Add(new Point(iniP.X - i, iniP.Y - a * i * i));
                    }

                    if (Settings.Canvas.ParabolaSingleStroke)
                    {
                        // 一笔绘制：左臂反序接顶点再接右臂，合成一条连续曲线（单个对象）
                        var merged = new List<Point>(pointList2);
                        merged.Reverse();
                        merged.AddRange(pointList);
                        point = new StylusPointCollection(merged);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                    }
                    else
                    {
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                        point = new StylusPointCollection(pointList2);
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                        };
                        strokes.Add(stroke.Clone());
                    }
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }

                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 21:
                    // 垂线：第一步已在按下时点选一条已有直线作为基准（BeginBaselinePickAt），
                    // 这里只处理第二步——点击（或拖动）给出一点，过该点作基准线的垂线并标直角
                    if (drawMultiStepShapeCurrentStep == 0) return;

                    _currentCommitType = CommitReason.ShapeDrawing;
                    {
                        var baseVector = _perpendicularBaseEnd - _perpendicularBaseStart;
                        var baseLengthSquared = baseVector.LengthSquared;
                        if (baseLengthSquared < 1e-6) return;

                        var externalPoint = endP;
                        var offset = externalPoint - _perpendicularBaseStart;
                        var t = Vector.Multiply(offset, baseVector) / baseLengthSquared;
                        var foot = _perpendicularBaseStart + baseVector * t;
                        var normal = externalPoint - foot;
                        // 点正好落在基准线上时，直接用基准线的法向作为垂线方向
                        if (normal.Length < 0.01)
                            normal = new Vector(-baseVector.Y, baseVector.X);
                        normal.Normalize();

                        // 只清理上一帧的垂线预览，基准辅助线保持到完成时再移除
                        try { inkCanvas.Strokes.Remove(_perpendicularLastPreview); }
                        catch { }

                        // 垂线：穿过垂足与所给点；垂足一侧再延长一段，视觉上是一条完整的垂线
                        const double externalOvershoot = 16;
                        var footOvershoot = Math.Max(60, GetDistance(foot, externalPoint) * 0.4);
                        strokes.Add(GeneratePerpendicularStroke(foot - normal * footOvershoot, externalPoint + normal * externalOvershoot));

                        // 直角符号：以垂足为角点，沿基准线与垂线各取一段边长，画两条直角边
                        const double markSize = 14;
                        var alongBase = baseVector;
                        alongBase.Normalize();
                        // 垂足落在基准线延长线上时让符号朝线段内侧，避免悬空
                        if (t > 1) alongBase = -alongBase;
                        var markCorner = foot + alongBase * markSize + normal * markSize;
                        strokes.Add(GeneratePerpendicularStroke(foot + alongBase * markSize, markCorner));
                        strokes.Add(GeneratePerpendicularStroke(markCorner, foot + normal * markSize));

                        lastTempStrokeCollection = strokes;
                        inkCanvas.Strokes.Add(strokes);
                        _perpendicularLastPreview = strokes;
                        break;
                    }
                case 22:
                    // 角平分线（点选式）：两步都在按下时点选角的边（第一步一条、第二步另一条），
                    // 抬起时按两条边的交点与点选方向画出平分线与两个等角弧；
                    // 完成时两条边的高亮移除，只保留平分线与等角弧
                    _currentCommitType = CommitReason.ShapeDrawing;
                    if (drawMultiStepShapeCurrentStep == 0) return;
                    {
                        var side1Vector = _angleBisectorSide1LineEnd - _angleBisectorSide1LineStart;
                        var side2Vector = _angleBisectorSide2LineEnd - _angleBisectorSide2LineStart;
                        if (side1Vector.Length < 0.01 || side2Vector.Length < 0.01) return;

                        if (!TryGetLineIntersection(_angleBisectorSide1LineStart, side1Vector,
                                _angleBisectorSide2LineStart, side2Vector, out var vertex))
                        {
                            ShowCanvasToast("两条边平行，画不出角平分线");
                            return;
                        }

                        // 点选位置决定每条边取哪个方向的射线：把点选位置投影到拟合直线上，
                        // 用「投影相对顶点的方向」定射线，比直接用点选坐标准（点选点可能离线 ≤12px）
                        side1Vector.Normalize();
                        side2Vector.Normalize();
                        var projection1 = Vector.Multiply(_angleBisectorSide1PickPoint - vertex, side1Vector);
                        var projection2 = Vector.Multiply(_angleBisectorSide2PickPoint - vertex, side2Vector);
                        var side1 = projection1 >= 0 ? side1Vector : -side1Vector;
                        var side2 = projection2 >= 0 ? side2Vector : -side2Vector;

                        // 只清理上一帧的平分线与等角弧；两条边的高亮保留到完成时再移除
                        RemoveAngleBisectorStrokes(_angleBisectorLastPreview);

                        var rayLength1 = Math.Max(Math.Abs(projection1), 1);
                        var rayLength2 = Math.Max(Math.Abs(projection2), 1);

                        // 角平分线方向 = 两条边的单位向量之和；两边接近 180° 时改用垂直方向
                        var bisector = side1 + side2;
                        if (bisector.Length < 0.01) bisector = new Vector(-side1.Y, side1.X);
                        bisector.Normalize();

                        // 平分线长度：跟两条边的可见长度走（取较长者再放 30%），并保证一个最小长度，
                        // 否则点选位置靠近顶点时平分线会短得看不出来
                        var reach1 = GetRayReachLength(_angleBisectorSide1LineStart, _angleBisectorSide1LineEnd, vertex, side1);
                        var reach2 = GetRayReachLength(_angleBisectorSide2LineStart, _angleBisectorSide2LineEnd, vertex, side2);
                        var bisectorLength = Math.Max(Math.Max(Math.Max(reach1, reach2), Math.Max(rayLength1, rayLength2)) * 1.3, 110);
                        strokes.Add(GenerateAngleBisectorStroke(vertex, vertex + bisector * bisectorLength));

                        // 等角弧：以顶点为圆心、同一半径，在「边1→平分线」「平分线→边2」各画一段
                        var arcRadius = Math.Max(Math.Min(Math.Min(rayLength1, rayLength2) * 0.32, 30), 14);
                        AddEqualAngleArc(strokes, vertex, side1, bisector, arcRadius);
                        AddEqualAngleArc(strokes, vertex, bisector, side2, arcRadius);

                        _angleBisectorVertex = vertex;
                        _angleBisectorSide1End = vertex + side1 * rayLength1;

                        lastTempStrokeCollection = strokes;
                        inkCanvas.Strokes.Add(strokes);
                        _angleBisectorLastPreview = strokes;
                        break;
                    }
                case 26:
                    // 中垂线：拖出一条线段（荧光笔辅助，与垂线第一步一致），同时实时给出它的
                    // 垂直平分线与直角标记；松手时移除荧光笔线段，只保留平分线与直角标记
                    // 本次手势已经收尾后又补来一帧（触摸/手写笔抬起时的鼠标提升事件）时直接丢弃，
                    // 否则会在已完成的中垂线旁边再画出第二条
                    if (_perpendicularBisectorAwaitNewGesture) return;
                    _currentCommitType = CommitReason.ShapeDrawing;
                    {
                        var dragSegment = endP - iniP;
                        // 拖动距离过小（单击或手抖）不生成，避免落下一条退化的中垂线
                        if (dragSegment.Length < 8) return;

                        // 精度：如果这次拖动是贴着某条已有直线拖的，就把拖动两端投影到该直线的拟合方向上，
                        // 线段仍取「两个拖动点之间」的部分（只纠正方向与位置，不会把整条直线都当成线段）
                        var segmentStart = iniP;
                        var segmentEnd = endP;
                        if (TryGetDragAlongExistingLine(iniP, endP, out var snappedStart, out var snappedEnd))
                        {
                            segmentStart = snappedStart;
                            segmentEnd = snappedEnd;
                        }

                        var segment = segmentEnd - segmentStart;
                        if (segment.Length < 8) return;

                        var mid = new Point((segmentStart.X + segmentEnd.X) / 2, (segmentStart.Y + segmentEnd.Y) / 2);
                        var normal = new Vector(-segment.Y, segment.X);
                        normal.Normalize();
                        var along = segment;
                        along.Normalize();

                        // 定稿笔画：垂直平分线（向两侧各延伸线段长度的 0.7 倍，至少 30）+ 直角标记
                        var result = new StrokeCollection();
                        var half = Math.Max(segment.Length * 0.7, 30);
                        result.Add(GeneratePerpendicularStroke(mid - normal * half, mid + normal * half));
                        const double markSize = 14;
                        var corner = mid + along * markSize + normal * markSize;
                        result.Add(GeneratePerpendicularStroke(mid + along * markSize, corner));
                        result.Add(GeneratePerpendicularStroke(corner, mid + normal * markSize));

                        // 拖动阶段的预览：荧光笔线段 + 定稿笔画
                        _perpendicularBisectorGuide =
                            GeneratePerpendicularBisectorGuideStroke(segmentStart, segmentEnd);
                        _perpendicularBisectorResult = result;
                        strokes.Add(_perpendicularBisectorGuide);
                        foreach (Stroke resultStroke in result) strokes.Add(resultStroke);

                        // 逐条清理上一帧：用本模式自己的字段跟踪，避免 lastTempStrokeCollection
                        // 被其它收尾路径提前清空后旧帧残留成「画了 2 条」
                        RemovePerpendicularBisectorFrame(_perpendicularBisectorLastFrame);
                        RemovePerpendicularBisectorFrame(lastTempStrokeCollection);

                        _perpendicularBisectorLastFrame = strokes;
                        lastTempStrokeCollection = strokes;
                        inkCanvas.Strokes.Add(strokes);
                        break;
                    }
                case 27:
                    // 画圆弧：按下处为圆心，拖动距离为半径；弧以拖动方向为中心、默认张角 90°，
                    // 因此拖动时能直接看到半径不断变化的圆弧。抬笔后由两个端点手柄改弧长（半径不变）。
                    _currentCommitType = CommitReason.ShapeDrawing;
                    {
                        // 本次手势已收尾后又补来一帧（触摸抬起时的鼠标提升事件）时直接丢弃，
                        // 否则会在已完成的圆弧旁边再落一条
                        if (_arcAwaitNewGesture) return;

                        var arcRadius = GetDistance(iniP, endP);
                        // 半径过小（单击或手抖）不生成，避免落下退化的圆弧
                        if (arcRadius < 6) return;

                        var dragAngle = Math.Atan2(endP.Y - iniP.Y, endP.X - iniP.X);
                        var halfSweep = Math.PI / 4; // 默认张角 90°
                        pointList = GenerateArcGeometry(iniP, arcRadius,
                            dragAngle - halfSweep, dragAngle + halfSweep, true);
                        if (pointList.Count < 3) return;

                        point = new StylusPointCollection(pointList);
                        // 与垂线/平行线等几何工具一致：荧光笔/激光笔下恢复最近使用的画笔颜色、宽度与透明度
                        stroke = new Stroke(point)
                        {
                            DrawingAttributes = CreatePerpendicularDrawingAttributes()
                        };
                        try
                        {
                            inkCanvas.Strokes.Remove(lastTempStroke);
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                        lastTempStroke = stroke;
                        inkCanvas.Strokes.Add(stroke);

                        // 拖动阶段同时显示半径（圆心 → 指针）；抬笔时移除，只保留弧线
                        RemoveArcRadiusGuide();
                        _arcRadiusGuideStroke = CreateArcRadiusGuide(iniP, endP);
                        if (_arcRadiusGuideStroke != null) inkCanvas.Strokes.Add(_arcRadiusGuideStroke);
                        break;
                    }
                case 28:
                    // 沿轴翻转：拖动阶段只显示一条对称轴线（荧光笔辅助线），松手时按该轴镜像选中的墨迹
                    if (GetDistance(iniP, endP) < AxisFlipMinLength) return;
                    {
                        // 复用同一根辅助线，只改两个端点：每帧 Remove/Add 笔画会让 InkPresenter
                        // 反复重建笔画收集，高回报率鼠标下会明显掉帧
                        if (_axisFlipGuideStroke == null)
                        {
                            _axisFlipGuideStroke = GenerateLineStroke(iniP, endP);
                            _axisFlipGuideStroke.DrawingAttributes = CreatePerpendicularGuideDrawingAttributes();
                            inkCanvas.Strokes.Add(_axisFlipGuideStroke);
                        }
                        else
                        {
                            var guidePoints = _axisFlipGuideStroke.StylusPoints;
                            if (guidePoints.Count >= 2)
                            {
                                guidePoints[0] = new StylusPoint(iniP.X, iniP.Y);
                                guidePoints[1] = new StylusPoint(endP.X, endP.Y);
                            }
                        }
                    }
                    // 记录终点：触摸抬笔时合成的鼠标事件拿不到触摸位置
                    _axisFlipAxisEnd = endP;
                    _axisFlipAxisEndValid = true;
                    break;
                case PaintBucketShapeMode:
                    // 油漆桶是单击工具：拖动不产生任何预览
                    return;
                case 6:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    newIniP = iniP;
                    if (iniP.Y > endP.Y)
                    {
                        newIniP = new Point(iniP.X, endP.Y);
                        endP = new Point(endP.X, iniP.Y);
                    }

                    var topA = Math.Abs(newIniP.X - endP.X);
                    var topB = topA / 2.646;
                    //顶部椭圆
                    pointList = GenerateEllipseGeometry(new Point(newIniP.X, newIniP.Y - topB / 2),
                        new Point(endP.X, newIniP.Y + topB / 2));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    //底部椭圆
                    pointList = GenerateEllipseGeometry(new Point(newIniP.X, endP.Y - topB / 2),
                        new Point(endP.X, endP.Y + topB / 2), false);
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    strokes.Add(GenerateDashedLineEllipseStrokeCollection(new Point(newIniP.X, endP.Y - topB / 2),
                        new Point(endP.X, endP.Y + topB / 2), true, false));
                    //左侧
                    pointList = new List<Point> {
                        new Point(newIniP.X, newIniP.Y),
                        new Point(newIniP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    //右侧
                    pointList = new List<Point> {
                        new Point(endP.X, newIniP.Y),
                        new Point(endP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }

                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 7:
                    _currentCommitType = CommitReason.ShapeDrawing;
                    if (iniP.Y > endP.Y)
                    {
                        newIniP = new Point(iniP.X, endP.Y);
                        endP = new Point(endP.X, iniP.Y);
                    }

                    var bottomA = Math.Abs(newIniP.X - endP.X);
                    var bottomB = bottomA / 2.646;
                    //底部椭圆
                    pointList = GenerateEllipseGeometry(new Point(newIniP.X, endP.Y - bottomB / 2),
                        new Point(endP.X, endP.Y + bottomB / 2), false);
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    strokes.Add(GenerateDashedLineEllipseStrokeCollection(new Point(newIniP.X, endP.Y - bottomB / 2),
                        new Point(endP.X, endP.Y + bottomB / 2), true, false));
                    //左侧
                    pointList = new List<Point> {
                        new Point((newIniP.X + endP.X) / 2, newIniP.Y),
                        new Point(newIniP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    //右侧
                    pointList = new List<Point> {
                        new Point((newIniP.X + endP.X) / 2, newIniP.Y),
                        new Point(endP.X, endP.Y)
                    };
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }

                    lastTempStrokeCollection = strokes;
                    inkCanvas.Strokes.Add(strokes);
                    break;
                case 9:
                    // 画长方体
                    _currentCommitType = CommitReason.ShapeDrawing;
                    if (isFirstTouchCuboid)
                    {
                        //分开画线条方便后期单独擦除某一条棱
                        strokes.Add(GenerateLineStroke(new Point(iniP.X, iniP.Y), new Point(iniP.X, endP.Y)));
                        strokes.Add(GenerateLineStroke(new Point(iniP.X, endP.Y), new Point(endP.X, endP.Y)));
                        strokes.Add(GenerateLineStroke(new Point(endP.X, endP.Y), new Point(endP.X, iniP.Y)));
                        strokes.Add(GenerateLineStroke(new Point(iniP.X, iniP.Y), new Point(endP.X, iniP.Y)));
                        try
                        {
                            inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                        }
                        catch
                        {
                            Trace.WriteLine("lastTempStrokeCollection failed.");
                        }

                        lastTempStrokeCollection = strokes;
                        inkCanvas.Strokes.Add(strokes);
                        CuboidFrontRectIniP = iniP;
                        CuboidFrontRectEndP = endP;
                    }
                    else
                    {
                        var d = CuboidFrontRectIniP.Y - endP.Y;
                        if (d < 0) d = -d; //就是懒不想做反向的，不要让我去做，想做自己做好之后 Pull Request
                        a = CuboidFrontRectEndP.X - CuboidFrontRectIniP.X; //正面矩形长
                        b = CuboidFrontRectEndP.Y - CuboidFrontRectIniP.Y; //正面矩形宽

                        //横上
                        var newLineIniP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectIniP.Y - d);
                        var newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectIniP.Y - d);
                        pointList = new List<Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());
                        //横下 (虚线)
                        newLineIniP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectEndP.Y - d);
                        newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectEndP.Y - d);
                        strokes.Add(GenerateDashedLineStrokeCollection(newLineIniP, newLineEndP));
                        //斜左上
                        newLineIniP = new Point(CuboidFrontRectIniP.X, CuboidFrontRectIniP.Y);
                        newLineEndP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectIniP.Y - d);
                        pointList = new List<Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());
                        //斜右上
                        newLineIniP = new Point(CuboidFrontRectEndP.X, CuboidFrontRectIniP.Y);
                        newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectIniP.Y - d);
                        pointList = new List<Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());
                        //斜左下 (虚线)
                        newLineIniP = new Point(CuboidFrontRectIniP.X, CuboidFrontRectEndP.Y);
                        newLineEndP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectEndP.Y - d);
                        strokes.Add(GenerateDashedLineStrokeCollection(newLineIniP, newLineEndP));
                        //斜右下
                        newLineIniP = new Point(CuboidFrontRectEndP.X, CuboidFrontRectEndP.Y);
                        newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectEndP.Y - d);
                        pointList = new List<Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());
                        //竖左 (虚线)
                        newLineIniP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectIniP.Y - d);
                        newLineEndP = new Point(CuboidFrontRectIniP.X + d, CuboidFrontRectEndP.Y - d);
                        strokes.Add(GenerateDashedLineStrokeCollection(newLineIniP, newLineEndP));
                        //竖右
                        newLineIniP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectIniP.Y - d);
                        newLineEndP = new Point(CuboidFrontRectEndP.X + d, CuboidFrontRectEndP.Y - d);
                        pointList = new List<Point> { newLineIniP, newLineEndP };
                        point = new StylusPointCollection(pointList);
                        stroke = new Stroke(point) { DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone() };
                        strokes.Add(stroke.Clone());

                        try
                        {
                            inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                        }
                        catch
                        {
                            Trace.WriteLine("lastTempStrokeCollection failed.");
                        }

                        lastTempStrokeCollection = strokes;
                        inkCanvas.Strokes.Add(strokes);
                    }

                    break;
            }
        }

        #endregion

        /// <summary>
        /// 长方体绘制的首次触摸标志
        /// </summary>
        /// <remarks>
        /// 用于标识是否是长方体绘制的第一次触摸，第一次触摸绘制正面矩形，第二次触摸绘制深度
        /// </remarks>
        private bool isFirstTouchCuboid = true;

        /// <summary>
        /// 长方体正面矩形的起始点
        /// </summary>
        private Point CuboidFrontRectIniP;

        /// <summary>
        /// 长方体正面矩形的结束点
        /// </summary>
        private Point CuboidFrontRectEndP;

        /// <summary>
        /// 上一次的临时笔画
        /// </summary>
        /// <remarks>
        /// 用于存储当前正在绘制的临时笔画，在绘制过程中实时更新
        /// </remarks>
        private Stroke lastTempStroke;

        /// <summary>
        /// 上一次的临时笔画集合
        /// </summary>
        /// <remarks>
        /// 用于存储当前正在绘制的临时笔画集合，在绘制复杂形状时使用
        /// </remarks>
        private StrokeCollection lastTempStrokeCollection = new StrokeCollection();

        /// <summary>
        /// 是否等待下一次触摸按下
        /// </summary>
        /// <remarks>
        /// 当触摸点数量大于1时，设置此标志并清理临时笔画
        /// </remarks>
        private bool isWaitUntilNextTouchDown;

        // 添加节流机制，减少更新频率
        /// <summary>
        /// 上一次更新时间
        /// </summary>
        private DateTime lastUpdateTime = DateTime.MinValue;

        /// <summary>
        /// 更新节流时间（毫秒）
        /// </summary>
        /// <remarks>
        /// 约60fps的更新频率，用于限制UI更新频率，提高性能
        /// </remarks>
        private const int UpdateThrottleMs = 16;

        /// <summary>
        /// 安全地更新临时笔画，减少预览闪烁
        /// </summary>
        /// <param name="newStroke">新的临时笔画</param>
        private void UpdateTempStrokeSafely(Stroke newStroke)
        {
            // 节流机制：限制更新频率
            var now = DateTime.Now;
            if ((now - lastUpdateTime).TotalMilliseconds < UpdateThrottleMs)
            {
                return;
            }
            lastUpdateTime = now;

            try
            {
                // 使用Dispatcher.BeginInvoke确保UI更新在UI线程上执行
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    try
                    {
                        // 先添加新笔画，再删除旧笔画，减少视觉闪烁
                        inkCanvas.Strokes.Add(newStroke);

                        if (lastTempStroke != null && inkCanvas.Strokes.Contains(lastTempStroke))
                        {
                            inkCanvas.Strokes.Remove(lastTempStroke);
                        }

                        lastTempStroke = newStroke;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UpdateTempStrokeSafely 失败: {ex.Message}");
                        // 如果更新失败，确保清理状态
                        if (lastTempStroke != null && inkCanvas.Strokes.Contains(lastTempStroke))
                        {
                            try { inkCanvas.Strokes.Remove(lastTempStroke); } catch (Exception innerEx) { System.Diagnostics.Debug.WriteLine(innerEx); }
                        }
                        lastTempStroke = newStroke;
                        try { inkCanvas.Strokes.Add(newStroke); } catch (Exception innerEx) { System.Diagnostics.Debug.WriteLine(innerEx); }
                    }
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateTempStrokeSafely Dispatcher 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 安全地更新临时笔画集合，减少预览闪烁
        /// </summary>
        /// <param name="newStrokeCollection">新的临时笔画集合</param>
        private void UpdateTempStrokeCollectionSafely(StrokeCollection newStrokeCollection)
        {
            // 节流机制：限制更新频率
            var now = DateTime.Now;
            if ((now - lastUpdateTime).TotalMilliseconds < UpdateThrottleMs)
            {
                return;
            }
            lastUpdateTime = now;

            try
            {
                // 使用Dispatcher.BeginInvoke确保UI更新在UI线程上执行
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    try
                    {
                        // 先添加新笔画集合，再删除旧笔画集合，减少视觉闪烁
                        inkCanvas.Strokes.Add(newStrokeCollection);

                        if (lastTempStrokeCollection != null && lastTempStrokeCollection.Count > 0)
                        {
                            foreach (var stroke in lastTempStrokeCollection)
                            {
                                if (inkCanvas.Strokes.Contains(stroke))
                                {
                                    inkCanvas.Strokes.Remove(stroke);
                                }
                            }
                        }

                        lastTempStrokeCollection = newStrokeCollection;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UpdateTempStrokeCollectionSafely 失败: {ex.Message}");
                        // 如果更新失败，确保清理状态
                        if (lastTempStrokeCollection != null && lastTempStrokeCollection.Count > 0)
                        {
                            foreach (var stroke in lastTempStrokeCollection)
                            {
                                try { inkCanvas.Strokes.Remove(stroke); } catch (Exception innerEx) { System.Diagnostics.Debug.WriteLine(innerEx); }
                            }
                        }
                        lastTempStrokeCollection = newStrokeCollection;
                        try { inkCanvas.Strokes.Add(newStrokeCollection); } catch (Exception innerEx) { System.Diagnostics.Debug.WriteLine(innerEx); }
                    }
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateTempStrokeCollectionSafely Dispatcher 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 生成椭圆几何图形的点列表
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <param name="isDrawTop">是否绘制上半部分</param>
        /// <param name="isDrawBottom">是否绘制下半部分</param>
        /// <returns>返回椭圆的点列表</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成椭圆的点列表：
        /// 1. 计算椭圆的半长轴和半短轴
        /// 2. 根据参数决定是否绘制上半部分和下半部分
        /// 3. 使用参数方程生成椭圆上的点
        /// </remarks>
        private List<Point> GenerateEllipseGeometry(Point st, Point ed, bool isDrawTop = true,
            bool isDrawBottom = true)
        {
            // 防御：画布被极端缩放/平移时识别出的椭圆中心/半轴可能非有限，直接返回空避免生成 NaN 点。
            if (!double.IsFinite(st.X) || !double.IsFinite(st.Y) ||
                !double.IsFinite(ed.X) || !double.IsFinite(ed.Y))
                return new List<Point>();

            var a = 0.5 * (ed.X - st.X);
            var b = 0.5 * (ed.Y - st.Y);
            var pointList = new List<Point>();
            if (isDrawTop && isDrawBottom)
            {
                for (double r = 0; r <= 2 * Math.PI; r = r + 0.01)
                    pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                        0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
            }
            else
            {
                if (isDrawBottom)
                    for (double r = 0; r <= Math.PI; r = r + 0.01)
                        pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                            0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                if (isDrawTop)
                    for (var r = Math.PI; r <= 2 * Math.PI; r = r + 0.01)
                        pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                            0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
            }

            return pointList;
        }

        /// <summary>
        /// 生成虚线椭圆的笔画集合
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <param name="isDrawTop">是否绘制上半部分</param>
        /// <param name="isDrawBottom">是否绘制下半部分</param>
        /// <returns>返回虚线椭圆的笔画集合</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成虚线椭圆的笔画集合：
        /// 1. 计算椭圆的半长轴和半短轴
        /// 2. 根据参数决定是否绘制上半部分和下半部分
        /// 3. 使用参数方程生成椭圆上的点，并将其分割为虚线段
        /// 4. 为每个虚线段创建一个笔画
        /// </remarks>
        private StrokeCollection GenerateDashedLineEllipseStrokeCollection(Point st, Point ed, bool isDrawTop = true,
            bool isDrawBottom = true)
        {
            var strokes = new StrokeCollection();
            // 防御：非有限坐标直接返回空集，避免生成 NaN 笔画。
            if (!double.IsFinite(st.X) || !double.IsFinite(st.Y) ||
                !double.IsFinite(ed.X) || !double.IsFinite(ed.Y))
                return strokes;

            var a = 0.5 * (ed.X - st.X);
            var b = 0.5 * (ed.Y - st.Y);
            var step = 0.05;
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;
            if (isDrawBottom)
                for (var i = 0.0; i < 1.0; i += step * 1.66)
                {
                    pointList = new List<Point>();
                    for (var r = Math.PI * i; r <= Math.PI * (i + step); r = r + 0.01)
                        pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                            0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                }

            if (isDrawTop)
                for (var i = 1.0; i < 2.0; i += step * 1.66)
                {
                    pointList = new List<Point>();
                    for (var r = Math.PI * i; r <= Math.PI * (i + step); r = r + 0.01)
                        pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                            0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                }

            return strokes;
        }

        private const double CoordinateCentimeterUnitLength = 37.7952755906;

        private void AddCoordinateUnitMarks(StrokeCollection strokes, Point origin, Point xAxisStart, Point xAxisEnd,
            Point yAxisStart, Point yAxisEnd, Point? zAxisEnd = null)
        {
            if (!Settings.Canvas.ShowCoordinateUnitMarks) return;

            AddCoordinateAxisUnitMarks(strokes, origin, xAxisStart, xAxisEnd, CoordinateCentimeterUnitLength, new Vector(0, 1));
            AddCoordinateAxisUnitMarks(strokes, origin, yAxisStart, yAxisEnd, CoordinateCentimeterUnitLength, new Vector(1, 0));
            if (zAxisEnd is Point zEnd)
                AddCoordinateAxisUnitMarks(strokes, origin, origin, zEnd, CoordinateCentimeterUnitLength, new Vector(1, 1));
        }

        private void AddCoordinateAxisUnitMarks(StrokeCollection strokes, Point origin, Point axisStart, Point axisEnd,
            double unitLength, Vector tickDirection)
        {
            var axisVector = axisEnd - origin;
            var axisLength = axisVector.Length;
            if (axisLength < 0.01) return;

            axisVector.Normalize();
            if (tickDirection.Length < 0.01)
                tickDirection = new Vector(-axisVector.Y, axisVector.X);
            tickDirection.Normalize();

            for (var distance = unitLength; distance <= axisLength; distance += unitLength)
            {
                var positiveUnitPoint = origin + axisVector * distance;
                if (IsPointOnSegment(positiveUnitPoint, axisStart, axisEnd))
                    strokes.Add(GenerateLineStroke(positiveUnitPoint - tickDirection * 5, positiveUnitPoint + tickDirection * 5));

                var negativeUnitPoint = origin - axisVector * distance;
                if (IsPointOnSegment(negativeUnitPoint, axisStart, axisEnd))
                    strokes.Add(GenerateLineStroke(negativeUnitPoint - tickDirection * 5, negativeUnitPoint + tickDirection * 5));
            }
        }

        private bool IsPointOnSegment(Point point, Point segmentStart, Point segmentEnd)
        {
            const double tolerance = 0.5;
            return point.X >= Math.Min(segmentStart.X, segmentEnd.X) - tolerance &&
                   point.X <= Math.Max(segmentStart.X, segmentEnd.X) + tolerance &&
                   point.Y >= Math.Min(segmentStart.Y, segmentEnd.Y) - tolerance &&
                   point.Y <= Math.Max(segmentStart.Y, segmentEnd.Y) + tolerance;
        }

        /// <summary>
        /// 生成直线笔画
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <returns>返回直线笔画</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成直线笔画：
        /// 1. 创建包含起始点和结束点的点列表
        /// 2. 将点列表转换为StylusPointCollection
        /// 3. 创建并返回带有默认绘图属性的笔画
        /// </remarks>
        private Stroke GenerateLineStroke(Point st, Point ed)
        {
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;
            pointList = new List<Point> {
                new Point(st.X, st.Y),
                new Point(ed.X, ed.Y)
            };
            point = new StylusPointCollection(pointList);
            stroke = new Stroke(point)
            {
                DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
            };
            return stroke;
        }

        /// <summary>
        /// 移除垂线第一步的红色荧光笔基准辅助线。
        /// </summary>
        private void RemovePerpendicularGuideStroke()
        {
            if (_perpendicularBaselineStroke == null) return;
            try { inkCanvas.Strokes.Remove(_perpendicularBaselineStroke); }
            catch { }
            _perpendicularBaselineStroke = null;
        }

        /// <summary>
        /// 取消未完成的垂线绘制：清理辅助线、预览与步骤状态。
        /// </summary>
        private void CancelPerpendicularDrawing()
        {
            RemovePerpendicularGuideStroke();
            try { inkCanvas.Strokes.Remove(_perpendicularLastPreview); }
            catch { }
            _perpendicularLastPreview = null;
            _perpendicularUpPoint = null;
            _perpendicularBaseStart = default;
            _perpendicularBaseEnd = default;
            drawMultiStepShapeCurrentStep = 0;
        }

        /// <summary>
        /// 生成平行线使用的画笔直线笔画（与垂线共用同一套画笔属性）。
        /// </summary>
        private Stroke GenerateParallelStroke(Point st, Point ed)
        {
            var stroke = GenerateLineStroke(st, ed);
            stroke.DrawingAttributes = CreatePerpendicularDrawingAttributes();
            return stroke;
        }

        /// <summary>
        /// 移除平行线第一步的红色荧光笔基准辅助线。
        /// </summary>
        private void RemoveParallelGuideStroke()
        {
            if (_parallelBaselineStroke == null) return;
            try
            {
                if (inkCanvas.Strokes.Contains(_parallelBaselineStroke)) inkCanvas.Strokes.Remove(_parallelBaselineStroke);
            }
            catch { }
            _parallelBaselineStroke = null;
        }

        /// <summary>
        /// 取消未完成的平行线绘制：清理辅助线、预览与步骤状态。
        /// </summary>
        private void CancelParallelDrawing()
        {
            RemoveParallelGuideStroke();
            try
            {
                if (_parallelLastPreview != null) inkCanvas.Strokes.Remove(_parallelLastPreview);
            }
            catch { }
            _parallelLastPreview = null;
            _parallelUpPoint = null;
            _parallelBaseStart = default;
            _parallelBaseEnd = default;
            drawMultiStepShapeCurrentStep = 0;
        }

        #region 平行线 / 垂线：第一步点选基准直线

        /// <summary>点选基准直线时，按下位置与直线的最大允许偏差（像素）。</summary>
        private const double BaselinePickHitTolerance = 12;

        /// <summary>
        /// 平行线 / 垂线的第一步：在按下位置选中一条已有直线作为基准，并用荧光笔高亮它。
        /// 基准线只作提示，完成或取消时移除，不写入撤销历史。
        /// </summary>
        private bool BeginBaselinePickAt(Point position)
        {
            if (inkCanvas == null) return false;

            if (!TryPickLineAt(position, out var lineStart, out var lineEnd))
            {
                ShowCanvasToast("第一步：请点击一条直线");
                return false;
            }

            var guide = GenerateLineStroke(lineStart, lineEnd);
            if (guide == null) return false;
            guide.DrawingAttributes = CreatePerpendicularGuideDrawingAttributes();

            var previousCommitType = _currentCommitType;
            _currentCommitType = CommitReason.CodeInput;
            try
            {
                if (drawingShapeMode == 17)
                {
                    RemoveParallelGuideStroke();
                    _parallelBaseStart = lineStart;
                    _parallelBaseEnd = lineEnd;
                    _parallelBaselineStroke = guide;
                    _parallelLastPreview = null;
                }
                else if (drawingShapeMode == 21)
                {
                    RemovePerpendicularGuideStroke();
                    _perpendicularBaseStart = lineStart;
                    _perpendicularBaseEnd = lineEnd;
                    _perpendicularBaselineStroke = guide;
                    _perpendicularLastPreview = null;
                }
                else
                {
                    return false;
                }

                inkCanvas.Strokes.Add(guide);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return false;
            }
            finally
            {
                _currentCommitType = previousCommitType;
            }

            ShowCanvasToast("已选基准线，请点击一点");
            return true;
        }

        /// <summary>
        /// 在按下位置附近找一条直线笔画：首尾连线足够长，且所有点都贴近这条连线。
        /// </summary>
        private bool TryPickLineAt(Point position, out Point lineStart, out Point lineEnd)
        {
            lineStart = default;
            lineEnd = default;
            if (inkCanvas == null) return false;

            StrokeCollection hits = null;
            try
            {
                hits = inkCanvas.Strokes.HitTest(position, BaselinePickHitTolerance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            if (hits == null) return false;

            var bestDistance = double.MaxValue;
            var found = false;
            foreach (Stroke hit in hits)
            {
                if (hit == null) continue;
                // 跳过本工具自己的高亮辅助笔画，避免把高亮再当成基准线
                if (IsShapeToolGuideStroke(hit)) continue;
                if (!TryGetStraightSegment(hit, out var start, out var end)) continue;

                var distance = DistanceToLineSegment(position, start, end);
                if (distance > BaselinePickHitTolerance || distance >= bestDistance) continue;

                bestDistance = distance;
                lineStart = start;
                lineEnd = end;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// 判断笔画是否是一条直线：用最小二乘（PCA）拟合方向，端点取所有点在拟合方向上的极值投影。
        /// 比直接取首尾点更稳：手绘线的首尾点常有点抖动，会让后续的平行/垂线/角平分线方向偏掉。
        /// </summary>
        private bool TryGetStraightSegment(Stroke stroke, out Point start, out Point end)
        {
            start = default;
            end = default;
            var points = stroke?.StylusPoints;
            if (points == null || points.Count < 2) return false;

            // 质心
            double sumX = 0;
            double sumY = 0;
            foreach (var stylusPoint in points)
            {
                var point = stylusPoint.ToPoint();
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
                sumX += point.X;
                sumY += point.Y;
            }

            var count = points.Count;
            var center = new Point(sumX / count, sumY / count);

            // 协方差矩阵的主方向即直线方向
            double sxx = 0;
            double syy = 0;
            double sxy = 0;
            foreach (var stylusPoint in points)
            {
                var offset = stylusPoint.ToPoint() - center;
                sxx += offset.X * offset.X;
                syy += offset.Y * offset.Y;
                sxy += offset.X * offset.Y;
            }

            var angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            if (direction.Length < 0.5) return false;

            var minProjection = double.MaxValue;
            var maxProjection = double.MinValue;
            double maxDeviation = 0;
            foreach (var stylusPoint in points)
            {
                var offset = stylusPoint.ToPoint() - center;
                var projection = Vector.Multiply(offset, direction);
                if (projection < minProjection) minProjection = projection;
                if (projection > maxProjection) maxProjection = projection;

                // 点到拟合直线的垂距
                var perpendicular = Math.Abs(direction.X * offset.Y - direction.Y * offset.X);
                if (perpendicular > maxDeviation) maxDeviation = perpendicular;
            }

            var length = maxProjection - minProjection;
            if (length < 24) return false;
            if (maxDeviation > Math.Max(3, length * 0.02)) return false;

            start = center + direction * minProjection;
            end = center + direction * maxProjection;
            return true;
        }

        /// <summary>
        /// 从顶点沿给定射线方向能到达的最远距离（取该边两端点在射线方向上的投影，负值忽略）。
        /// 用于给角平分线定长度：跟着边的可见长度走，不会贴着顶点就断。
        /// </summary>
        private static double GetRayReachLength(Point lineStart, Point lineEnd, Point vertex, Vector rayDirection)
        {
            var reach = Math.Max(Vector.Multiply(lineStart - vertex, rayDirection),
                Vector.Multiply(lineEnd - vertex, rayDirection));
            return Math.Max(reach, 0);
        }

        /// <summary>点到线段的最短距离。</summary>
        private static double DistanceToLineSegment(Point point, Point start, Point end)
        {
            var segment = end - start;
            var lengthSquared = segment.LengthSquared;
            if (lengthSquared < 1e-6) return (point - start).Length;

            var t = Vector.Multiply(point - start, segment) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            var projection = start + segment * t;
            return (point - projection).Length;
        }

        /// <summary>中垂线吸附容差：拖动两端都要贴着同一条直线才吸附（像素）。</summary>
        private const double PerpendicularBisectorSnapTolerance = 10;

        /// <summary>
        /// 中垂线精度吸附：拖动起点贴在某条已有直线上、且终点也贴着同一条直线时，
        /// 把两个拖动点投影到该直线的拟合方向上，返回「两点之间」的那一段。
        /// 这样中点与方向都跟着直线的拟合结果走（不受手拖偏差影响），
        /// 但被平分的仍是用户拖出的这一段，而不是整条直线。
        /// </summary>
        private bool TryGetDragAlongExistingLine(Point dragStart, Point dragEnd,
            out Point segmentStart, out Point segmentEnd)
        {
            segmentStart = default;
            segmentEnd = default;
            if (inkCanvas == null) return false;

            if (!TryPickLineAt(dragStart, out var lineStart, out var lineEnd)) return false;
            if (DistanceToInfiniteLine(dragEnd, lineStart, lineEnd) > PerpendicularBisectorSnapTolerance) return false;

            segmentStart = ProjectPointOntoLine(dragStart, lineStart, lineEnd);
            segmentEnd = ProjectPointOntoLine(dragEnd, lineStart, lineEnd);
            return (segmentEnd - segmentStart).Length >= 8;
        }

        /// <summary>点到无限长直线（点 + 方向）的垂距。</summary>
        private static double DistanceToInfiniteLine(Point point, Point lineStart, Point lineEnd)
        {
            var direction = lineEnd - lineStart;
            var length = direction.Length;
            if (length < 1e-6) return (point - lineStart).Length;

            direction /= length;
            var offset = point - lineStart;
            return Math.Abs(direction.X * offset.Y - direction.Y * offset.X);
        }

        /// <summary>把点投影到无限长直线（点 + 方向）上，不做端点截断。</summary>
        private static Point ProjectPointOntoLine(Point point, Point lineStart, Point lineEnd)
        {
            var direction = lineEnd - lineStart;
            var lengthSquared = direction.LengthSquared;
            if (lengthSquared < 1e-6) return lineStart;

            var t = Vector.Multiply(point - lineStart, direction) / lengthSquared;
            return lineStart + direction * t;
        }

        #endregion

        /// <summary>
        /// 逐条容错移除一组笔画。整集合 Remove 只要有一条已不在画布上就会整体失败，
        /// 导致上一帧预览残留，因此这里逐条判断后删除。
        /// </summary>
        private void RemoveStrokesSafe(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0 || inkCanvas == null) return;
            var snapshot = new List<Stroke>(strokes.Count);
            foreach (Stroke stroke in strokes) snapshot.Add(stroke);
            foreach (var stroke in snapshot)
            {
                try
                {
                    if (stroke != null && inkCanvas.Strokes.Contains(stroke)) inkCanvas.Strokes.Remove(stroke);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        /// <summary>
        /// 三角形内心与内切圆半径：圆心按顶点对边长度加权，半径 = 2·面积/周长。
        /// </summary>
        /// <param name="vertices">三角形的三个顶点（顺序不限）</param>
        /// <param name="center">内心（内切圆圆心）</param>
        /// <param name="radius">内切圆半径</param>
        /// <returns>退化三角形或半径过小时返回 false</returns>
        private static bool TryGetTriangleIncircle(IList<Point> vertices, out Point center, out double radius)
        {
            center = default;
            radius = 0;
            if (vertices == null || vertices.Count < 3) return false;

            var a = vertices[0];
            var b = vertices[1];
            var c = vertices[2];

            var sideOfA = (c - b).Length; // 顶点 A 的对边 BC
            var sideOfB = (a - c).Length; // 顶点 B 的对边 CA
            var sideOfC = (b - a).Length; // 顶点 C 的对边 AB
            var perimeter = sideOfA + sideOfB + sideOfC;
            if (!(perimeter > 1e-6)) return false;

            var area = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) * 0.5;
            if (!(area > 1e-6)) return false;

            var inradius = 2 * area / perimeter;
            if (!double.IsFinite(inradius) || inradius < 0.5) return false;

            var centerX = (sideOfA * a.X + sideOfB * b.X + sideOfC * c.X) / perimeter;
            var centerY = (sideOfA * a.Y + sideOfB * b.Y + sideOfC * c.Y) / perimeter;
            if (!double.IsFinite(centerX) || !double.IsFinite(centerY)) return false;

            center = new Point(centerX, centerY);
            radius = inradius;
            return true;
        }

        /// <summary>
        /// 把多边形的每条边在「内切圆的切点」处切成两段，返回所有边的分段笔画。
        /// 切点 = 圆心到该边的垂足；只有垂足落在线段内部、且垂足到圆心的距离与半径相符的边才切，
        /// 不规则多边形中与内切圆不相切的边保持整条（三角形、正多边形每条边都会被切）。
        /// </summary>
        private StrokeCollection BuildEdgesSplitAtTangency(IList<Point> vertices, Point center, double radius,
            DrawingAttributes attributes)
        {
            if (vertices == null || vertices.Count < 3 || !(radius > 0.5) || inkCanvas == null) return null;

            var result = new StrokeCollection();

            Stroke CreateEdgeStroke(Point edgeStart, Point edgeEnd)
            {
                return new Stroke(new StylusPointCollection(new List<Point> { edgeStart, edgeEnd }))
                {
                    DrawingAttributes = (attributes ?? inkCanvas.DefaultDrawingAttributes).Clone()
                };
            }

            for (var i = 0; i < vertices.Count; i++)
            {
                var edgeStart = vertices[i];
                var edgeEnd = vertices[(i + 1) % vertices.Count];

                // 垂足在线段内部、且到圆心的距离等于半径 → 该点就是切点，从切点处断开
                if (TryGetEdgeTangencyPoint(edgeStart, edgeEnd, center, radius, out var tangencyPoint))
                {
                    result.Add(CreateEdgeStroke(edgeStart, tangencyPoint));
                    result.Add(CreateEdgeStroke(tangencyPoint, edgeEnd));
                    continue;
                }

                result.Add(CreateEdgeStroke(edgeStart, edgeEnd));
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// 求一条边的切点（圆心到边的垂足）：垂足必须落在线段内部，且到圆心距离等于半径。
        /// </summary>
        private static bool TryGetEdgeTangencyPoint(Point edgeStart, Point edgeEnd, Point center, double radius,
            out Point tangencyPoint)
        {
            tangencyPoint = default;
            var edge = edgeEnd - edgeStart;
            var edgeLength = edge.Length;
            if (edgeLength < 0.01) return false;

            var direction = edge / edgeLength;
            var projection = Vector.Multiply(center - edgeStart, direction);
            if (projection <= 0.01 || projection >= edgeLength - 0.01) return false;

            var foot = edgeStart + direction * projection;
            var tolerance = Math.Max(1.0, radius * 0.02);
            if (Math.Abs((center - foot).Length - radius) > tolerance) return false;

            tangencyPoint = foot;
            return true;
        }

        /// <summary>收集多边形各边与内切圆的切点（与分段用的切点完全一致）。</summary>
        private static List<Point> GetPolygonTangencyPoints(IList<Point> vertices, Point center, double radius)
        {
            var tangencyPoints = new List<Point>();
            if (vertices == null || vertices.Count < 3) return tangencyPoints;

            for (var i = 0; i < vertices.Count; i++)
            {
                var edgeStart = vertices[i];
                var edgeEnd = vertices[(i + 1) % vertices.Count];
                if (TryGetEdgeTangencyPoint(edgeStart, edgeEnd, center, radius, out var tangencyPoint))
                    tangencyPoints.Add(tangencyPoint);
            }

            return tangencyPoints;
        }

        /// <summary>
        /// 把内切圆按切点切成若干段圆弧：每个切点都落在弧的端点上，
        /// 与「切点把边分成两段」对应起来；切点不足两个时退回整圆。
        /// </summary>
        private StrokeCollection BuildCircleArcsSplitAtTangency(Point center, double radius,
            IList<Point> tangencyPoints, DrawingAttributes attributes)
        {
            if (inkCanvas == null || !(radius > 0.5)) return null;

            var arcAttributes = (attributes ?? inkCanvas.DefaultDrawingAttributes).Clone();

            var angles = new List<double>();
            if (tangencyPoints != null)
            {
                foreach (var tangencyPoint in tangencyPoints)
                {
                    var angle = Math.Atan2(tangencyPoint.Y - center.Y, tangencyPoint.X - center.X);
                    if (angle < 0) angle += Math.PI * 2;
                    angles.Add(angle);
                }
            }

            if (angles.Count >= 2)
            {
                angles.Sort();

                // 去掉几乎重合的角度（退化顶点可能产生同一个切点）
                var distinctAngles = new List<double>();
                foreach (var angle in angles)
                {
                    if (distinctAngles.Count > 0 &&
                        Math.Abs(angle - distinctAngles[distinctAngles.Count - 1]) < 1e-3) continue;
                    distinctAngles.Add(angle);
                }

                // 0 与 2π 是同一个点
                if (distinctAngles.Count >= 2 &&
                    Math.Abs(distinctAngles[0] + Math.PI * 2 - distinctAngles[distinctAngles.Count - 1]) < 1e-3)
                    distinctAngles.RemoveAt(distinctAngles.Count - 1);

                if (distinctAngles.Count >= 2)
                {
                    var arcs = new StrokeCollection();
                    for (var i = 0; i < distinctAngles.Count; i++)
                    {
                        var startAngle = distinctAngles[i];
                        var endAngle = i + 1 < distinctAngles.Count
                            ? distinctAngles[i + 1]
                            : distinctAngles[0] + Math.PI * 2;

                        var arc = CreateArcStroke(center, radius, startAngle, endAngle, arcAttributes);
                        if (arc != null) arcs.Add(arc);
                    }

                    if (arcs.Count > 0) return arcs;
                }
            }

            // 切点不足：整圆无法分成多段，仍按整圆返回
            var circle = CreateCircleStroke(center, radius, attributes);
            return circle == null ? null : new StrokeCollection { circle };
        }

        /// <summary>按起止角度采样生成一段圆弧笔画。</summary>
        private static Stroke CreateArcStroke(Point center, double radius, double startAngle, double endAngle,
            DrawingAttributes attributes)
        {
            var sweep = endAngle - startAngle;
            if (sweep <= 1e-6) return null;

            var stepCount = Math.Max(6, (int)Math.Ceiling(sweep * radius / 3.0));
            var points = new List<Point>(stepCount + 1);
            for (var i = 0; i <= stepCount; i++)
            {
                var angle = startAngle + sweep * i / stepCount;
                points.Add(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
            }

            return new Stroke(new StylusPointCollection(points))
            {
                DrawingAttributes = attributes.Clone()
            };
        }

        /// <summary>
        /// 生成中点标记：用当前画笔的属性渲染的一个圆点。
        /// 圆点直径＝当前画笔粗细，颜色/透明度/笔尖与画笔设置完全一致（不额外加粗）。
        /// </summary>
        private StrokeCollection BuildMidpointStrokes(Point mid)
        {
            var strokes = new StrokeCollection();

            // 与垂线使用同一套「最近使用的画笔」属性：颜色、粗细、透明度都跟随画笔设置
            var attributes = CreatePerpendicularDrawingAttributes();
            attributes.IsHighlighter = false;
            attributes.StylusTip = StylusTip.Ellipse;
            attributes.IgnorePressure = true;
            attributes.FitToCurve = false;

            // 用一段极短的线段 + 圆笔尖渲染成点：两端半圆帽合起来正好是直径≈线宽的点，
            // 不会像小圆环那样比笔画粗一圈
            var half = Math.Max(attributes.Width, 0.01) * 0.1;
            var points = new StylusPointCollection
            {
                new StylusPoint(mid.X - half, mid.Y),
                new StylusPoint(mid.X + half, mid.Y)
            };

            strokes.Add(new Stroke(points)
            {
                DrawingAttributes = attributes
            });
            return strokes;
        }

        /// <summary>
        /// 生成中点工具拖动阶段的荧光笔辅助线段（与中垂线拖动阶段的线段同款）。
        /// </summary>
        private Stroke GenerateMidpointGuideStroke(Point st, Point ed)
        {
            var stroke = GenerateLineStroke(st, ed);
            stroke.DrawingAttributes = CreatePerpendicularGuideDrawingAttributes();
            return stroke;
        }

        /// <summary>
        /// 中点收尾：移除拖动阶段的荧光笔线段，只保留中点标记，并把它提交为一次撤销。
        /// 由 PreviewMouseUp / 触摸抬起 / 手写笔抬起 / 捕获丢失等多个时机调用，重复调用安全。
        /// </summary>
        private void CompleteMidpointNow()
        {
            var guide = _midpointGuide;
            var result = _midpointResult;
            if (guide == null && result == null) return;

            _midpointGuide = null;
            _midpointResult = null;

            // 移除荧光笔线段
            if (guide != null)
            {
                try
                {
                    if (inkCanvas.Strokes.Contains(guide)) inkCanvas.Strokes.Remove(guide);
                }
                catch { }
            }

            // 中点标记入撤销栈（避免依赖 inkCanvas_MouseUp 的提交块）
            if (result != null && result.Count > 0)
            {
                var alive = true;
                foreach (Stroke stroke in result)
                {
                    if (inkCanvas.Strokes.Contains(stroke)) continue;
                    alive = false;
                    break;
                }

                if (alive)
                {
                    var previousCommitType = _currentCommitType;
                    _currentCommitType = CommitReason.UserInput;
                    try
                    {
                        timeMachine.CommitStrokeUserInputHistory(result);
                    }
                    finally
                    {
                        _currentCommitType = previousCommitType;
                    }
                }
            }

            // 本次手势到此结束：后续同一手势补来的帧一律丢弃，避免再落一个点
            _midpointAwaitNewGesture = true;
            _midpointLastFrame = null;
            lastTempStrokeCollection = null;
        }

        /// <summary>
        /// 取消未完成的中点绘制：清掉拖动阶段的荧光笔线段与预览笔画。
        /// </summary>
        private void CancelMidpointDrawing()
        {
            var guide = _midpointGuide;
            var result = _midpointResult;
            _midpointGuide = null;
            _midpointResult = null;

            if (guide != null)
            {
                try
                {
                    if (inkCanvas.Strokes.Contains(guide)) inkCanvas.Strokes.Remove(guide);
                }
                catch { }
            }

            RemoveStrokesSafe(result);
            _midpointLastFrame = null;
        }

        /// <summary>
        /// 生成垂线第一步基准辅助线使用的红色荧光笔绘图属性。
        /// </summary>
        private DrawingAttributes CreatePerpendicularGuideDrawingAttributes()
        {
            var attributes = inkCanvas.DefaultDrawingAttributes.Clone();

            // 辅助线完全按荧光笔的设置绘制：粗细取荧光笔宽度，颜色取荧光笔当前
            // 选中的颜色与透明度，用户在笔面板里调整荧光笔时辅助线同步变化。
            var width = Settings.Canvas.HighlighterWidth;
            if (width <= 0) width = 20;

            var alpha = (byte)Math.Max(0, Math.Min(255, Settings.Canvas.HighlighterAlpha));
            attributes.Color = GetHighlighterGuideColor(alpha);
            attributes.StylusTip = StylusTip.Rectangle;
            attributes.Width = width / 2;
            attributes.Height = width;
            attributes.IsHighlighter = true;
            attributes.IgnorePressure = true;
            attributes.FitToCurve = false;
            return attributes;
        }

        /// <summary>
        /// 按荧光笔当前选中的颜色（<see cref="highlighterColor"/>）生成辅助线颜色。
        /// 取色表与 CheckColorTheme 中 penType == 1 的分支保持一致；未知索引时退回
        /// 当前画笔颜色的 RGB。
        /// </summary>
        private Color GetHighlighterGuideColor(byte alpha)
        {
            switch (highlighterColor)
            {
                case 100: return Color.FromArgb(alpha, 0, 0, 0);
                case 101: return Color.FromArgb(alpha, 250, 250, 250);
                case 102: return Color.FromArgb(alpha, 239, 68, 68);
                case 103: return Color.FromArgb(alpha, 253, 224, 71);
                case 104: return Color.FromArgb(alpha, 74, 222, 128);
                case 105: return Color.FromArgb(alpha, 113, 113, 122);
                case 106: return Color.FromArgb(alpha, 59, 130, 246);
                case 107: return Color.FromArgb(alpha, 168, 85, 247);
                case 108: return Color.FromArgb(alpha, 45, 212, 191);
                case 109: return Color.FromArgb(alpha, 249, 115, 22);
            }

            var current = inkCanvas.DefaultDrawingAttributes.Color;
            return Color.FromArgb(alpha, current.R, current.G, current.B);
        }

        /// <summary>
        /// 生成垂线第二步（垂线与直角符号）使用的画笔绘图属性。
        /// 当前处于荧光笔/激光笔时，恢复为最近使用的画笔颜色、宽度与透明度。
        /// </summary>
        private DrawingAttributes CreatePerpendicularDrawingAttributes()
        {
            var attributes = inkCanvas.DefaultDrawingAttributes.Clone();
            attributes.IsHighlighter = false;
            attributes.StylusTip = StylusTip.Ellipse;

            var width = Settings.Canvas.InkWidth;
            if (width <= 0) width = 2.5;
            attributes.Width = width;
            attributes.Height = width;
            attributes.IgnorePressure = true;
            attributes.FitToCurve = false;

            var alpha = (byte)Math.Max(0, Math.Min(255, Settings.Canvas.InkAlpha));
            if (penType == 0)
                attributes.Color = Color.FromArgb(alpha, attributes.Color.R, attributes.Color.G, attributes.Color.B);
            else
                attributes.Color = GetPenColorByIndex(inkColor, alpha);

            return attributes;
        }

        /// <summary>
        /// 按画笔颜色索引获取当前主题下的画笔颜色。
        /// </summary>
        private Color GetPenColorByIndex(int index, byte alpha)
        {
            if (index == 0) return Color.FromArgb(alpha, 0, 0, 0);
            if (index == 5) return Color.FromArgb(alpha, 255, 255, 255);

            if (isUselightThemeColor)
            {
                switch (index)
                {
                    case 1: return Color.FromArgb(alpha, 239, 68, 68);
                    case 2: return Color.FromArgb(alpha, 34, 197, 94);
                    case 3: return Color.FromArgb(alpha, 59, 130, 246);
                    case 4: return Color.FromArgb(alpha, 250, 204, 21);
                    case 6: return Color.FromArgb(alpha, 236, 72, 153);
                    case 7: return Color.FromArgb(alpha, 20, 184, 166);
                    case 8: return Color.FromArgb(alpha, 249, 115, 22);
                }
            }
            else
            {
                switch (index)
                {
                    case 1: return Color.FromArgb(alpha, 220, 38, 38);
                    case 2: return Color.FromArgb(alpha, 22, 163, 74);
                    case 3: return Color.FromArgb(alpha, 37, 99, 235);
                    case 4: return Color.FromArgb(alpha, 234, 179, 8);
                    case 6: return Color.FromArgb(alpha, 147, 51, 234);
                    case 7: return Color.FromArgb(alpha, 13, 148, 136);
                    case 8: return Color.FromArgb(alpha, 234, 88, 12);
                }
            }

            var current = inkCanvas.DefaultDrawingAttributes.Color;
            return Color.FromArgb(alpha, current.R, current.G, current.B);
        }

        /// <summary>
        /// 生成使用画笔属性的直线笔画（用于垂线与直角符号）。
        /// </summary>
        private Stroke GeneratePerpendicularStroke(Point st, Point ed)
        {
            var stroke = GenerateLineStroke(st, ed);
            stroke.DrawingAttributes = CreatePerpendicularDrawingAttributes();
            return stroke;
        }

        /// <summary>
        /// 生成角平分线使用的画笔绘图属性（与垂线共用同一套画笔属性）。
        /// </summary>
        private DrawingAttributes CreateAngleBisectorDrawingAttributes()
        {
            return CreatePerpendicularDrawingAttributes();
        }

        /// <summary>
        /// 生成使用画笔属性的直线笔画（用于角的两条边与角平分线）。
        /// </summary>
        private Stroke GenerateAngleBisectorStroke(Point st, Point ed)
        {
            var stroke = GenerateLineStroke(st, ed);
            stroke.DrawingAttributes = CreateAngleBisectorDrawingAttributes();
            return stroke;
        }

        /// <summary>
        /// 以顶点为圆心，在 from 与 to 两个方向之间画一段圆弧（用作等角标记）。
        /// </summary>
        private void AddEqualAngleArc(StrokeCollection strokes, Point vertex, Vector from, Vector to, double radius)
        {
            if (radius < 1) return;
            from.Normalize();
            to.Normalize();

            const int steps = 16;
            var pointList = new List<Point>(steps + 1);
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (double)steps;
                var direction = from * (1 - t) + to * t;
                if (direction.Length < 1e-6) direction = from;
                direction.Normalize();
                pointList.Add(vertex + direction * radius);
            }

            var stroke = new Stroke(new StylusPointCollection(pointList))
            {
                DrawingAttributes = CreateAngleBisectorDrawingAttributes()
            };
            strokes.Add(stroke);
        }

        /// <summary>
        /// 生成角的两条边使用的荧光笔辅助线笔画（与垂线基准辅助线同款）。
        /// </summary>
        private Stroke GenerateAngleBisectorGuideStroke(Point st, Point ed)
        {
            var stroke = GenerateLineStroke(st, ed);
            stroke.DrawingAttributes = CreatePerpendicularGuideDrawingAttributes();
            return stroke;
        }

        /// <summary>
        /// 生成中垂线第一步线段的荧光笔辅助线笔画（与垂线基准辅助线同款）。
        /// </summary>
        private Stroke GeneratePerpendicularBisectorGuideStroke(Point st, Point ed)
        {
            var stroke = GenerateLineStroke(st, ed);
            stroke.DrawingAttributes = CreatePerpendicularGuideDrawingAttributes();
            return stroke;
        }

        /// <summary>
        /// 中垂线本次手势是否已经收尾。收尾后若同一手势又补来一帧（触摸/手写笔抬起时
        /// 鼠标事件被提升、或多指抬起产生的事件），必须丢弃，否则会画出第二条中垂线。
        /// 每次按下笔/手指（新的一次手势）时复位。
        /// </summary>
        private bool _perpendicularBisectorAwaitNewGesture;

        /// <summary>中垂线上一帧的笔画（荧光笔线段 + 平分线预览），每帧开头据此清理旧帧。</summary>
        private StrokeCollection _perpendicularBisectorLastFrame;

        /// <summary>
        /// 逐条移除中垂线上一帧的笔画。整集合 Remove 只要有一条已不在画布上就会整体失败，
        /// 上一帧的平分线残留下来就会变成「画了 2 条」，因此这里逐条容错删除。
        /// </summary>
        private void RemovePerpendicularBisectorFrame(StrokeCollection strokes)
        {
            RemoveStrokesSafe(strokes);
        }

        /// <summary>
        /// 中垂线收尾：移除拖动阶段的荧光笔线段，只保留垂直平分线与直角标记，并把它们提交为一次撤销。
        /// 由 PreviewMouseUp / 触摸抬起 / 捕获丢失等多个时机调用，重复调用安全。
        /// </summary>
        private void CompletePerpendicularBisectorNow()
        {
            var guide = _perpendicularBisectorGuide;
            var result = _perpendicularBisectorResult;
            if (guide == null && result == null) return;

            _perpendicularBisectorGuide = null;
            _perpendicularBisectorResult = null;

            // 移除荧光笔线段
            if (guide != null)
            {
                try
                {
                    if (inkCanvas.Strokes.Contains(guide)) inkCanvas.Strokes.Remove(guide);
                }
                catch { }
            }

            // 平分线 + 直角标记照常入撤销栈（避免依赖 inkCanvas_MouseUp 的提交块）
            if (result != null && result.Count > 0)
            {
                var alive = true;
                foreach (Stroke stroke in result)
                {
                    if (inkCanvas.Strokes.Contains(stroke)) continue;
                    alive = false;
                    break;
                }

                if (alive)
                {
                    var previousCommitType = _currentCommitType;
                    _currentCommitType = CommitReason.UserInput;
                    try
                    {
                        timeMachine.CommitStrokeUserInputHistory(result);
                    }
                    finally
                    {
                        _currentCommitType = previousCommitType;
                    }

                    // 中垂线：登记主线笔画，选中后可拖动两端伸缩
                    RegisterGeometryLine(result, GeometryLineKind.PerpendicularBisector);
                }
            }

            // 本次手势到此结束：后续同一手势补来的帧一律丢弃，避免再画一条
            _perpendicularBisectorAwaitNewGesture = true;
            _perpendicularBisectorLastFrame = null;
            lastTempStrokeCollection = null;
        }

        /// <summary>
        /// 取消未完成的中垂线绘制：清掉拖动阶段的荧光笔线段与预览笔画。
        /// </summary>
        private void CancelPerpendicularBisector()
        {
            var guide = _perpendicularBisectorGuide;
            var result = _perpendicularBisectorResult;
            _perpendicularBisectorGuide = null;
            _perpendicularBisectorResult = null;

            if (guide != null)
            {
                try
                {
                    if (inkCanvas.Strokes.Contains(guide)) inkCanvas.Strokes.Remove(guide);
                }
                catch { }
            }

            if (result != null)
            {
                foreach (Stroke stroke in result)
                {
                    try
                    {
                        if (inkCanvas.Strokes.Contains(stroke)) inkCanvas.Strokes.Remove(stroke);
                    }
                    catch { }
                }
            }

            _perpendicularBisectorLastFrame = null;
        }



        /// <summary>
        /// 移除单条角平分线相关笔画（不存在时静默跳过）。
        /// </summary>
        private void RemoveAngleBisectorStroke(Stroke stroke)
        {
            if (stroke == null) return;
            try
            {
                if (inkCanvas.Strokes.Contains(stroke)) inkCanvas.Strokes.Remove(stroke);
            }
            catch { }
        }

        /// <summary>
        /// 移除一组角平分线相关笔画（逐条判断，避免整集合删除抛异常）。
        /// </summary>
        private void RemoveAngleBisectorStrokes(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0) return;
            var snapshot = new List<Stroke>(strokes.Count);
            foreach (Stroke stroke in strokes) snapshot.Add(stroke);
            foreach (var stroke in snapshot) RemoveAngleBisectorStroke(stroke);
        }

        /// <summary>
        /// 角平分线的点选入口：第一步点选角的一条边，第二步点选另一条边。
        /// 两条边都用荧光笔高亮，点选位置决定每条边取哪个方向的射线（同一个交点可以夹出四个角）。
        /// </summary>
        private bool BeginAngleBisectorSidePick(Point position)
        {
            if (inkCanvas == null) return false;

            var firstStep = drawMultiStepShapeCurrentStep == 0;
            if (!TryPickLineAt(position, out var lineStart, out var lineEnd))
            {
                ShowCanvasToast(firstStep ? "第一步：请点击角的一条边" : "第二步：请点击角的另一条边");
                return false;
            }

            var guide = GenerateAngleBisectorGuideStroke(lineStart, lineEnd);
            if (guide == null) return false;

            var previousCommitType = _currentCommitType;
            _currentCommitType = CommitReason.CodeInput; // 边的高亮不入撤销栈
            try
            {
                if (firstStep)
                {
                    RemoveAngleBisectorStroke(_angleBisectorSide1Stroke);
                    RemoveAngleBisectorStroke(_angleBisectorSide2Stroke);
                    _angleBisectorSide2Stroke = null;
                    _angleBisectorSide1Stroke = guide;
                    _angleBisectorSide1LineStart = lineStart;
                    _angleBisectorSide1LineEnd = lineEnd;
                    _angleBisectorSide1PickPoint = position;
                    _angleBisectorVertex = default;
                    _angleBisectorSide1End = default;
                }
                else
                {
                    RemoveAngleBisectorStroke(_angleBisectorSide2Stroke);
                    _angleBisectorSide2Stroke = guide;
                    _angleBisectorSide2LineStart = lineStart;
                    _angleBisectorSide2LineEnd = lineEnd;
                    _angleBisectorSide2PickPoint = position;
                }

                inkCanvas.Strokes.Add(guide);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return false;
            }
            finally
            {
                _currentCommitType = previousCommitType;
            }

            ShowCanvasToast(firstStep ? "已选第一条边，请点击角的另一条边" : "已选两条边，松手即可画出平分线");
            return true;
        }

        /// <summary>求两条直线（点 + 方向）的交点；平行或重合时返回 false。</summary>
        private static bool TryGetLineIntersection(Point point1, Vector direction1,
            Point point2, Vector direction2, out Point intersection)
        {
            intersection = default;
            var denominator = direction1.X * direction2.Y - direction1.Y * direction2.X;
            if (Math.Abs(denominator) < 1e-6) return false;

            var offset = point2 - point1;
            var t = (offset.X * direction2.Y - offset.Y * direction2.X) / denominator;
            intersection = point1 + direction1 * t;
            return double.IsFinite(intersection.X) && double.IsFinite(intersection.Y);
        }

        /// <summary>
        /// 取消未完成的角平分线绘制：清理两条边的高亮、预览与步骤状态。
        /// </summary>
        private void CancelAngleBisectorDrawing()
        {
            RemoveAngleBisectorStrokes(_angleBisectorLastPreview);
            _angleBisectorLastPreview = null;
            RemoveAngleBisectorStroke(_angleBisectorSide1Stroke);
            _angleBisectorSide1Stroke = null;
            RemoveAngleBisectorStroke(_angleBisectorSide2Stroke);
            _angleBisectorSide2Stroke = null;
            _angleBisectorUpPoint = null;
            _angleBisectorVertex = default;
            _angleBisectorSide1End = default;
            _angleBisectorSide1LineStart = default;
            _angleBisectorSide1LineEnd = default;
            _angleBisectorSide1PickPoint = default;
            _angleBisectorSide2LineStart = default;
            _angleBisectorSide2LineEnd = default;
            _angleBisectorSide2PickPoint = default;
            drawMultiStepShapeCurrentStep = 0;
        }

        /// <summary>
        /// 生成带箭头的直线笔画
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <returns>返回带箭头的直线笔画</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成带箭头的直线笔画：
        /// 1. 计算箭头的角度和方向
        /// 2. 创建包含起始点、结束点和箭头尖端的点列表
        /// 3. 将点列表转换为StylusPointCollection
        /// 4. 创建并返回带有默认绘图属性的笔画
        /// </remarks>
        private Stroke GenerateArrowLineStroke(Point st, Point ed)
        {
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;

            double w = 20, h = 7;
            var theta = Math.Atan2(st.Y - ed.Y, st.X - ed.X);
            var sint = Math.Sin(theta);
            var cost = Math.Cos(theta);

            pointList = new List<Point> {
                new Point(st.X, st.Y),
                new Point(ed.X, ed.Y),
                new Point(ed.X + (w * cost - h * sint), ed.Y + (w * sint + h * cost)),
                new Point(ed.X, ed.Y),
                new Point(ed.X + (w * cost + h * sint), ed.Y - (h * cost - w * sint))
            };
            point = new StylusPointCollection(pointList);
            stroke = new Stroke(point)
            {
                DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
            };
            return stroke;
        }


        /// <summary>
        /// 生成虚线笔画集合
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <returns>返回虚线笔画集合</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成虚线笔画集合：
        /// 1. 计算两点之间的距离和方向
        /// 2. 按照指定的步长将直线分割为虚线段
        /// 3. 为每个虚线段创建一个笔画
        /// 4. 返回包含所有虚线段笔画的集合
        /// </remarks>
        private StrokeCollection GenerateDashedLineStrokeCollection(Point st, Point ed)
        {
            double step = 5;
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;
            var strokes = new StrokeCollection();
            var d = GetDistance(st, ed);
            var sinTheta = (ed.Y - st.Y) / d;
            var cosTheta = (ed.X - st.X) / d;
            for (var i = 0.0; i < d; i += step * 2.76)
            {
                pointList = new List<Point> {
                    new Point(st.X + i * cosTheta, st.Y + i * sinTheta),
                    new Point(st.X + Math.Min(i + step, d) * cosTheta, st.Y + Math.Min(i + step, d) * sinTheta)
                };
                point = new StylusPointCollection(pointList);
                stroke = new Stroke(point)
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };
                strokes.Add(stroke.Clone());
            }

            return strokes;
        }

        /// <summary>
        /// 生成点线笔画集合
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <returns>返回点线笔画集合</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成点线笔画集合：
        /// 1. 计算两点之间的距离和方向
        /// 2. 按照指定的步长在直线上生成点
        /// 3. 为每个点创建一个笔画
        /// 4. 返回包含所有点笔画的集合
        /// </remarks>
        private StrokeCollection GenerateDotLineStrokeCollection(Point st, Point ed)
        {
            double step = 3;
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;
            var strokes = new StrokeCollection();
            var d = GetDistance(st, ed);
            var sinTheta = (ed.Y - st.Y) / d;
            var cosTheta = (ed.X - st.X) / d;
            for (var i = 0.0; i < d; i += step * 2.76)
            {
                var stylusPoint = new StylusPoint(st.X + i * cosTheta, st.Y + i * sinTheta, (float)0.8);
                point = new StylusPointCollection();
                point.Add(stylusPoint);
                stroke = new Stroke(point)
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };
                strokes.Add(stroke.Clone());
            }

            return strokes;
        }

        /// <summary>
        /// 鼠标按下状态标志
        /// </summary>
        /// <remarks>
        /// 用于标识鼠标是否处于按下状态，在绘制过程中使用
        /// </remarks>
        private bool isMouseDown;
        private bool _isMouseRealtimeInking;

        /// <summary>
        /// 触摸按下状态标志
        /// </summary>
        /// <remarks>
        /// 用于标识触摸是否处于按下状态，在绘制过程中使用
        /// </remarks>
        private bool isTouchDown;

        /// <summary>
        /// 处理墨水画布的鼠标按下事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当在墨水画布上按下鼠标时：
        /// 1. 捕获鼠标输入
        /// 2. 禁用浮动栏和黑板UI的命中测试，避免干扰绘制
        /// 3. 设置鼠标按下状态标志
        /// 4. 如果需要更新起始点，则更新起始点为当前鼠标位置
        /// </remarks>
        #region 多边形绘制（模式 12）

        /// <summary>
        /// 处理多边形的单击：添加顶点；再次点击起点时闭合并完成绘制。
        /// </summary>
        private void HandlePolygonClick(Point position)
        {
            if (_polygonVertices.Count >= 3 &&
                GetDistance(position, _polygonVertices[0]) <= PolygonCloseHitRadius)
            {
                CommitPolygon();
                return;
            }

            // 忽略与上一个顶点几乎重合的点击，避免产生零长度边
            if (_polygonVertices.Count > 0 &&
                GetDistance(position, _polygonVertices[_polygonVertices.Count - 1]) < 1)
                return;

            _polygonVertices.Add(position);
            UpdatePolygonPreview(position);
        }

        /// <summary>
        /// 刷新多边形预览：已确定顶点 + 到光标的动态线段；
        /// 光标靠近起点且顶点数足够时，预览直接连回起点提示可闭合。
        /// </summary>
        private void UpdatePolygonPreview(Point cursor)
        {
            RemovePolygonPreviewStroke();
            if (_polygonVertices.Count == 0) return;

            var points = new List<Point>(_polygonVertices);
            if (_polygonVertices.Count >= 3 &&
                GetDistance(cursor, _polygonVertices[0]) <= PolygonCloseHitRadius)
                points.Add(_polygonVertices[0]);
            else
                points.Add(cursor);

            var stroke = new Stroke(new StylusPointCollection(points))
            {
                DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
            };
            inkCanvas.Strokes.Add(stroke);
            _polygonPreviewStroke = stroke;
        }

        /// <summary>
        /// 闭合多边形并提交：移除预览，生成首尾相连的笔画，写入历史并回到笔模式。
        /// </summary>
        private void CommitPolygon()
        {
            if (_polygonVertices.Count < 3)
            {
                CancelPolygonDrawing();
                return;
            }

            RemovePolygonPreviewStroke();

            var vertices = new List<Point>(_polygonVertices);
            _polygonVertices.Clear();

            var polygonAttributes = inkCanvas.DefaultDrawingAttributes.Clone();
            var polygonStroke = CreateClosedPolygonStroke(vertices, polygonAttributes);
            if (polygonStroke == null) return;

            inkCanvas.Strokes.Add(polygonStroke);

            // 延迟到本次抬起事件处理完再弹窗：白板/触控下在抬起回调里直接开模态窗口，
            // 触摸状态与窗口激活会互相干扰（表现为第一次点击关不掉、选项点不中）
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    ApplyPolygonFinishOptions(polygonStroke, vertices, polygonAttributes);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }));

            // 多边形是连续绘制工具：完成一个后保持工具选中（图形按钮继续高亮），
            // 便于紧接着绘制下一个，直到用户主动切换到其他工具。
        }

        /// <summary>
        /// 多边形完成后的收尾：弹窗询问「纠正为正多边形」「显示内切圆」，
        /// 按选择替换/追加笔画，并把最终结果作为一次撤销提交。
        /// </summary>
        private void ApplyPolygonFinishOptions(Stroke polygonStroke, List<Point> vertices, DrawingAttributes attributes)
        {
            if (inkCanvas == null || polygonStroke == null) return;
            if (!inkCanvas.Strokes.Contains(polygonStroke)) return; // 期间被撤销/清屏

            bool confirmed;
            try
            {
                Activate();
                var dialog = new PolygonFinishWindow(vertices.Count, vertices.Count == 3) { Owner = this };
                confirmed = dialog.ShowDialog() == true;
                if (!confirmed)
                {
                    CommitPolygonHistory(polygonStroke, null);
                    return;
                }

                var resultPolygon = polygonStroke;
                var attachments = new StrokeCollection();

                var previousCommitType = _currentCommitType;
                _currentCommitType = CommitReason.CodeInput; // 抑制 StrokesChanged 的自动历史，统一在下面提交
                try
                {
                    var finalVertices = vertices;
                    var hasIncircle = false;
                    var incircleCenter = default(Point);
                    var incircleRadius = 0.0;

                    if (dialog.RegularizeRequested &&
                        TryBuildRegularPolygonVertices(vertices, out var regularVertices, out var regularCenter, out var apothem))
                    {
                        var regularStroke = CreateClosedPolygonStroke(regularVertices, attributes);
                        if (regularStroke != null)
                        {
                            if (inkCanvas.Strokes.Contains(polygonStroke)) inkCanvas.Strokes.Remove(polygonStroke);
                            inkCanvas.Strokes.Add(regularStroke);
                            resultPolygon = regularStroke;
                            finalVertices = regularVertices;

                            if (dialog.ShowIncircleRequested)
                            {
                                incircleCenter = regularCenter;
                                incircleRadius = apothem;
                                hasIncircle = true;
                            }
                        }
                    }
                    else if (dialog.ShowIncircleRequested)
                    {
                        hasIncircle = vertices.Count == 3
                            ? TryGetTriangleIncircle(vertices, out incircleCenter, out incircleRadius)
                            : TryGetPolygonIncircle(vertices, out incircleCenter, out incircleRadius);
                    }

                    if (hasIncircle)
                    {
                        // 内切圆的切点把每条边切成两段：先用分段边替换整条闭合边，再放内切圆
                        var splitEdges = BuildEdgesSplitAtTangency(finalVertices, incircleCenter, incircleRadius, attributes);
                        if (splitEdges != null && splitEdges.Count > 0)
                        {
                            if (resultPolygon != null && inkCanvas.Strokes.Contains(resultPolygon))
                                inkCanvas.Strokes.Remove(resultPolygon);

                            inkCanvas.Strokes.Add(splitEdges);
                            resultPolygon = splitEdges[0];
                            for (var i = 1; i < splitEdges.Count; i++) attachments.Add(splitEdges[i]);
                        }

                        // 内切圆同样按切点切成若干段圆弧，切点与边的分段点完全一致
                        var incircleTangencyPoints = GetPolygonTangencyPoints(finalVertices, incircleCenter, incircleRadius);
                        var incircleArcs = BuildCircleArcsSplitAtTangency(incircleCenter, incircleRadius,
                            incircleTangencyPoints, attributes);
                        if (incircleArcs != null)
                        {
                            inkCanvas.Strokes.Add(incircleArcs);
                            foreach (Stroke arcStroke in incircleArcs) attachments.Add(arcStroke);
                        }
                    }
                }
                finally
                {
                    _currentCommitType = previousCommitType;
                }

                CommitPolygonHistory(resultPolygon, attachments);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"多边形完成弹窗失败: {ex.Message}", LogHelper.LogType.Warning);
                CommitPolygonHistory(polygonStroke, null);
            }
        }

        /// <summary>把多边形与其附加笔画（内切圆）作为一次用户输入写入撤销历史。</summary>
        private void CommitPolygonHistory(Stroke polygon, StrokeCollection attachments)
        {
            if (polygon == null) return;

            var committed = new StrokeCollection { polygon };
            if (attachments != null)
            {
                foreach (Stroke stroke in attachments) committed.Add(stroke);
            }

            var previousCommitType = _currentCommitType;
            _currentCommitType = CommitReason.UserInput;
            try
            {
                timeMachine.CommitStrokeUserInputHistory(committed);
                MarkCurrentPageInkChanged();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                _currentCommitType = previousCommitType;
            }
        }

        /// <summary>用顶点列表生成首尾相连的闭合笔画。</summary>
        private Stroke CreateClosedPolygonStroke(IList<Point> vertices, DrawingAttributes attributes)
        {
            if (vertices == null || vertices.Count < 2 || inkCanvas == null) return null;

            var points = new List<Point>(vertices) { vertices[0] };
            return new Stroke(new StylusPointCollection(points))
            {
                DrawingAttributes = (attributes ?? inkCanvas.DefaultDrawingAttributes).Clone()
            };
        }

        /// <summary>以 center 为圆心、radius 为半径生成圆笔画。</summary>
        private Stroke CreateCircleStroke(Point center, double radius, DrawingAttributes attributes)
        {
            if (inkCanvas == null) return null;
            if (!double.IsFinite(center.X) || !double.IsFinite(center.Y)) return null;
            if (!double.IsFinite(radius) || radius < 0.5) return null;

            var points = GenerateEllipseGeometry(
                new Point(center.X - radius, center.Y - radius),
                new Point(center.X + radius, center.Y + radius));
            if (points == null || points.Count < 3) return null;

            return new Stroke(new StylusPointCollection(points))
            {
                DrawingAttributes = (attributes ?? inkCanvas.DefaultDrawingAttributes).Clone()
            };
        }

        /// <summary>
        /// 把一组顶点规整为正多边形：保留中心、平均外接半径与首顶点方向，
        /// 其余顶点按 360°/N 均匀分布；apothem 返回内切圆半径。
        /// </summary>
        private static bool TryBuildRegularPolygonVertices(IList<Point> vertices, out List<Point> result,
            out Point center, out double apothem)
        {
            result = null;
            center = default;
            apothem = 0;
            if (vertices == null || vertices.Count < 3) return false;

            var count = vertices.Count;
            double sumX = 0, sumY = 0;
            for (var i = 0; i < count; i++)
            {
                sumX += vertices[i].X;
                sumY += vertices[i].Y;
            }

            var cx = sumX / count;
            var cy = sumY / count;
            center = new Point(cx, cy);

            double sumRadius = 0;
            for (var i = 0; i < count; i++)
            {
                sumRadius += (vertices[i] - center).Length;
            }

            var circumRadius = sumRadius / count;
            if (!double.IsFinite(circumRadius) || circumRadius < 1) return false;

            var startAngle = Math.Atan2(vertices[0].Y - cy, vertices[0].X - cx);
            var step = 2 * Math.PI / count;

            result = new List<Point>(count);
            for (var i = 0; i < count; i++)
            {
                var angle = startAngle + step * i;
                result.Add(new Point(cx + circumRadius * Math.Cos(angle), cy + circumRadius * Math.Sin(angle)));
            }

            apothem = circumRadius * Math.Cos(Math.PI / count);
            return true;
        }

        /// <summary>
        /// 任意多边形的内切圆：圆心取顶点质心，半径取质心到各边距离的最小值。
        /// 正多边形时该圆与每条边都相切（即内切圆）；不规则多边形时是一个内接于边的最大同心圆。
        /// </summary>
        private static bool TryGetPolygonIncircle(IList<Point> vertices, out Point center, out double radius)
        {
            center = default;
            radius = 0;
            if (vertices == null || vertices.Count < 3) return false;

            double sumX = 0, sumY = 0;
            for (var i = 0; i < vertices.Count; i++)
            {
                sumX += vertices[i].X;
                sumY += vertices[i].Y;
            }

            center = new Point(sumX / vertices.Count, sumY / vertices.Count);

            var minDistance = double.MaxValue;
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                var distance = DistancePointToSegment(center, a, b);
                if (distance < minDistance) minDistance = distance;
            }

            if (!double.IsFinite(minDistance) || minDistance < 0.5) return false;
            radius = minDistance;
            return true;
        }

        private static double DistancePointToSegment(Point point, Point a, Point b)
        {
            var ab = b - a;
            var lengthSquared = ab.LengthSquared;
            if (lengthSquared <= 1e-12) return (point - a).Length;

            var t = Math.Max(0, Math.Min(1, ((point - a) * ab) / lengthSquared));
            var projection = a + ab * t;
            return (point - projection).Length;
        }

        /// <summary>
        /// 取消未完成的多边形绘制（清理顶点与预览笔画）。
        /// </summary>
        private void CancelPolygonDrawing()
        {
            RemovePolygonPreviewStroke();
            _polygonVertices.Clear();
        }

        private void RemovePolygonPreviewStroke()
        {
            if (_polygonPreviewStroke == null) return;
            try
            {
                inkCanvas.Strokes.Remove(_polygonPreviewStroke);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            _polygonPreviewStroke = null;
        }

        #endregion

        #region 过圆外一点作圆的切线（模式 13）

        /// <summary>圆的切线工具占用的绘制模式号（原「等边三角形」工具）。</summary>
        private const int TangentShapeMode = 13;

        /// <summary>按住选圆时，按下位置与圆周的最大允许偏差（像素）。</summary>
        private const double TangentCircleHitTolerance = 14;

        /// <summary>本次拖动选中的圆（圆心）。</summary>
        private Point _tangentCircleCenter;

        /// <summary>本次拖动选中的圆（半径）。</summary>
        private double _tangentCircleRadius;

        /// <summary>选中圆的高亮圆环（荧光笔绘制，仅在拖动过程中显示，完成/取消时移除）。</summary>
        private Stroke _tangentCircleGuide;

        /// <summary>拖动阶段的切线预览（按 ShapeDrawing 添加，不入撤销栈）。</summary>
        private StrokeCollection _tangentLastPreview;

        /// <summary>是否正处于「按住圆拖到圆外」的拖动状态。</summary>
        private bool _tangentCircleDragging;

        /// <summary>触摸/手写笔抬笔位置（触摸路径合成的鼠标事件拿不到真实触控坐标）。</summary>
        private Point? _tangentTapPoint;

        /// <summary>发起拖动的那根手指的触摸 Id（-1 表示非触摸输入），用于忽略手掌/第二指的抬起。</summary>
        private int _tangentPrimaryTouchId = -1;

        /// <summary>
        /// 圆的切线工具的一次拖动开始：按住圆（圆周或已登记圆的内部）即选中该圆并高亮，
        /// 随后拖到圆外一点松手，按「圆外点 → 两切点并延伸过切点」画出两条切线。
        /// </summary>
        private bool BeginTangentDragAt(Point pressPoint)
        {
            _tangentCircleDragging = false;
            _tangentTapPoint = null;
            _tangentPrimaryTouchId = -1;
            RemoveTangentPreview();
            RemoveTangentCircleGuide();

            if (inkCanvas == null) return false;

            if (!TryPickCircleAt(pressPoint, out var center, out var radius))
            {
                _tangentCircleCenter = default;
                _tangentCircleRadius = 0;
                ShowCanvasToast("请按住一个圆拖到圆外");
                return false;
            }

            _tangentCircleCenter = center;
            _tangentCircleRadius = radius;
            _tangentCircleDragging = true;
            ShowTangentCircleGuide(center, radius);
            ShowCanvasToast("按住圆拖到圆外一点后松手");
            return true;
        }

        /// <summary>
        /// 拖动结束：移除预览与高亮，按松手点生成最终的两条切线并作为一次用户输入提交。
        /// </summary>
        private void CommitTangentDrag(Point releasePoint)
        {
            var center = _tangentCircleCenter;
            var radius = _tangentCircleRadius;
            var dragging = _tangentCircleDragging;

            RemoveTangentPreview();
            RemoveTangentCircleGuide();
            _tangentCircleDragging = false;
            _tangentCircleCenter = default;
            _tangentCircleRadius = 0;

            if (!dragging || inkCanvas == null || radius <= 0.5) return;

            // 松手点必须在圆外，圆上以及圆内都不存在切线
            if (GetDistance(releasePoint, center) <= radius + 1)
            {
                ShowCanvasToast("请拖到圆外一点再松手");
                return;
            }

            var tangents = BuildTangentStrokes(releasePoint, center, radius);
            if (tangents == null || tangents.Count == 0) return;

            // 两条切线作为一次用户输入写入撤销历史（StrokesChanged 按 UserInput 自动提交）
            _currentCommitType = CommitReason.UserInput;
            inkCanvas.Strokes.Add(tangents);
        }

        /// <summary>拖动阶段的预览：把当前拖动位置当作圆外点，实时显示两条切线。</summary>
        private void UpdateTangentPreview(Point externalPoint)
        {
            RemoveTangentPreview();
            if (!_tangentCircleDragging || inkCanvas == null) return;

            var radius = _tangentCircleRadius;
            if (radius <= 0.5) return;
            if (GetDistance(externalPoint, _tangentCircleCenter) <= radius + 1) return;

            var preview = BuildTangentStrokes(externalPoint, _tangentCircleCenter, radius);
            if (preview == null || preview.Count == 0) return;

            _tangentLastPreview = preview;
            _currentCommitType = CommitReason.ShapeDrawing; // 预览不入撤销栈
            inkCanvas.Strokes.Add(preview);
        }

        /// <summary>移除拖动阶段的切线预览（预览按 ShapeDrawing 添加，删除时同样不产生历史）。</summary>
        private void RemoveTangentPreview()
        {
            var preview = _tangentLastPreview;
            _tangentLastPreview = null;
            if (preview == null || inkCanvas == null) return;

            var previousCommitType = _currentCommitType;
            _currentCommitType = CommitReason.ShapeDrawing;
            try
            {
                RemoveStrokesSafe(preview);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                _currentCommitType = previousCommitType;
            }
        }

        /// <summary>
        /// 过圆外一点作圆的两条切线：切线长 L = √(d² - r²)，
        /// 两个切点关于「圆心→圆外点」连线对称；线段从圆外点画到切点后再沿切线方向延伸一段，
        /// 视觉上是一条切过圆的切线而不是一条弦。
        /// </summary>
        private StrokeCollection BuildTangentStrokes(Point externalPoint, Point center, double radius)
        {
            if (inkCanvas == null || radius <= 0.5) return null;

            var toExternal = externalPoint - center;
            var distance = toExternal.Length;
            if (!double.IsFinite(distance) || distance <= radius + 1) return null;

            toExternal.Normalize();

            // 切点半径与圆心连线的夹角 θ 满足 cosθ = r / d
            var cos = radius / distance;
            var sin = Math.Sqrt(Math.Max(0, 1 - cos * cos));
            var normal = new Vector(-toExternal.Y, toExternal.X);

            var tangentPoint1 = center + (toExternal * cos + normal * sin) * radius;
            var tangentPoint2 = center + (toExternal * cos - normal * sin) * radius;

            // 延伸过切点：沿「圆外点 → 切点」方向再多画一截（约半个半径，至少 60px）
            var overshoot = Math.Max(60, radius * 0.6);

            var attributes = inkCanvas.DefaultDrawingAttributes.Clone();
            var result = new StrokeCollection();

            Stroke CreateSegment(Point segmentStart, Point segmentEnd)
            {
                var segment = GenerateLineStroke(segmentStart, segmentEnd);
                segment.DrawingAttributes = attributes.Clone();
                return segment;
            }

            foreach (var tangentPoint in new[] { tangentPoint1, tangentPoint2 })
            {
                var direction = tangentPoint - externalPoint;
                if (direction.Length < 0.01) continue;
                direction.Normalize();

                // 切点把切线分成两段：圆外点 → 切点、切点 → 延伸端，
                // 两段在切点处严格共点，便于后续选中/擦除/分段操作
                result.Add(CreateSegment(externalPoint, tangentPoint));
                result.Add(CreateSegment(tangentPoint, tangentPoint + direction * overshoot));
            }

            return result;
        }

        /// <summary>
        /// 在按下位置附近找一个圆：优先命中圆周，其次接受落在已登记圆内部的按下。
        /// </summary>
        private bool TryPickCircleAt(Point position, out Point center, out double radius)
        {
            center = default;
            radius = 0;
            if (inkCanvas == null) return false;

            var bestCenter = default(Point);
            var bestRadius = 0.0;
            var bestDelta = double.MaxValue;
            var found = false;

            // 1) 圆周附近命中的笔画：直接用最小二乘拟合圆心与半径。
            //    不能只信 circles 里的元数据——圆被移动/缩放后元数据不会同步更新
            StrokeCollection hits = null;
            try
            {
                hits = inkCanvas.Strokes.HitTest(position, TangentCircleHitTolerance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }

            if (hits != null)
            {
                foreach (Stroke hit in hits)
                {
                    if (IsShapeToolGuideStroke(hit)) continue;
                    if (!TryFitCircleFromStroke(hit, out var fitCenter, out var fitRadius)) continue;

                    var delta = Math.Abs(GetDistance(position, fitCenter) - fitRadius);
                    if (delta > TangentCircleHitTolerance || delta >= bestDelta) continue;

                    bestDelta = delta;
                    bestCenter = fitCenter;
                    bestRadius = fitRadius;
                    found = true;
                }
            }

            // 2) 已登记的圆（圆形工具 / 墨迹转形状）：能用笔画就重新拟合，元数据仅作兜底
            foreach (var circle in circles)
            {
                if (circle?.Stroke == null || circle.R <= 0.5) continue;
                if (!inkCanvas.Strokes.Contains(circle.Stroke)) continue;

                var candidateCenter = circle.Centroid;
                var candidateRadius = circle.R;
                if (TryFitCircleFromStroke(circle.Stroke, out var registeredCenter, out var registeredRadius))
                {
                    candidateCenter = registeredCenter;
                    candidateRadius = registeredRadius;
                }

                var delta = Math.Abs(GetDistance(position, candidateCenter) - candidateRadius);
                if (delta > TangentCircleHitTolerance || delta >= bestDelta) continue;

                bestDelta = delta;
                bestCenter = candidateCenter;
                bestRadius = candidateRadius;
                found = true;
            }

            if (found)
            {
                center = bestCenter;
                radius = bestRadius;
                return true;
            }

            // 3) 点在圆内：按「包含该点的最小圆」选中，符合「点选对象」的直觉
            var smallestRadius = double.MaxValue;
            foreach (var circle in circles)
            {
                if (circle?.Stroke == null || circle.R <= 0.5) continue;
                if (!inkCanvas.Strokes.Contains(circle.Stroke)) continue;

                var candidateCenter = circle.Centroid;
                var candidateRadius = circle.R;
                if (TryFitCircleFromStroke(circle.Stroke, out var insideCenter, out var insideRadius))
                {
                    candidateCenter = insideCenter;
                    candidateRadius = insideRadius;
                }

                if (GetDistance(position, candidateCenter) > candidateRadius || candidateRadius >= smallestRadius)
                    continue;

                smallestRadius = candidateRadius;
                bestCenter = candidateCenter;
                bestRadius = candidateRadius;
                found = true;
            }

            center = bestCenter;
            radius = bestRadius;
            return found;
        }

        /// <summary>
        /// 从一条闭合笔画拟合圆：先用包围盒做快速筛选，再用最小二乘（Kåsa）拟合圆心与半径，
        /// 最后按径向残差判断是否足够接近圆。切线的相切精度就取决于这里的圆心与半径。
        /// </summary>
        private bool TryFitCircleFromStroke(Stroke stroke, out Point center, out double radius)
        {
            center = default;
            radius = 0;
            if (stroke == null) return false;

            var points = stroke.StylusPoints;
            if (points == null || points.Count < 16) return false;

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;
            foreach (var stylusPoint in points)
            {
                var point = stylusPoint.ToPoint();
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            var width = maxX - minX;
            var height = maxY - minY;
            if (width < 24 || height < 24) return false;
            // 椭圆 / 多边形也贴合包围盒，先按长宽比粗筛
            if (Math.Abs(width - height) > Math.Max(width, height) * 0.25) return false;

            // 未闭合的圆弧不能当作圆
            if (GetDistance(points[0].ToPoint(), points[points.Count - 1].ToPoint()) >
                Math.Max(width, height) * 0.25) return false;

            if (!TryFitCircleLeastSquares(points, out center, out radius, out var rmsDeviation)) return false;
            if (radius < 8) return false;
            // 残差过大说明不是圆（椭圆、多边形在最小二乘拟合下残差会明显变大）
            if (rmsDeviation > Math.Max(2.5, radius * 0.05)) return false;

            return true;
        }

        /// <summary>
        /// 最小二乘圆拟合（Kåsa 代数法）：解 x²+y²+Dx+Ey+F=0，
        /// 圆心为 (-D/2, -E/2)，半径为 √((D/2)²+(E/2)²-F)。先平移到质心再解，数值更稳。
        /// </summary>
        private static bool TryFitCircleLeastSquares(StylusPointCollection points,
            out Point center, out double radius, out double rmsDeviation)
        {
            center = default;
            radius = 0;
            rmsDeviation = double.MaxValue;

            var count = points?.Count ?? 0;
            if (count < 16) return false;

            double meanX = 0;
            double meanY = 0;
            foreach (var stylusPoint in points)
            {
                var point = stylusPoint.ToPoint();
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return false;
                meanX += point.X;
                meanY += point.Y;
            }

            meanX /= count;
            meanY /= count;

            double suu = 0;
            double svv = 0;
            double suv = 0;
            double b1 = 0;
            double b2 = 0;
            double sumSquaredNorm = 0;
            foreach (var stylusPoint in points)
            {
                var u = stylusPoint.X - meanX;
                var v = stylusPoint.Y - meanY;
                var u2 = u * u;
                var v2 = v * v;
                suu += u2;
                svv += v2;
                suv += u * v;
                b1 += u * (u2 + v2);
                b2 += v * (u2 + v2);
                sumSquaredNorm += u2 + v2;
            }

            var determinant = suu * svv - suv * suv;
            if (Math.Abs(determinant) < 1e-9) return false;

            // 正规方程：D·suu + E·suv = -b1；D·suv + E·svv = -b2
            var rhs1 = -b1;
            var rhs2 = -b2;
            var d = (rhs1 * svv - suv * rhs2) / determinant;
            var e = (suu * rhs2 - suv * rhs1) / determinant;

            var offsetX = -d / 2;
            var offsetY = -e / 2;
            var constant = -sumSquaredNorm / count; // F
            var radiusSquared = offsetX * offsetX + offsetY * offsetY - constant;
            if (!double.IsFinite(radiusSquared) || radiusSquared <= 0) return false;

            center = new Point(meanX + offsetX, meanY + offsetY);
            radius = Math.Sqrt(radiusSquared);

            double sumSquaredError = 0;
            foreach (var stylusPoint in points)
            {
                var error = (stylusPoint.ToPoint() - center).Length - radius;
                sumSquaredError += error * error;
            }

            rmsDeviation = Math.Sqrt(sumSquaredError / count);
            return true;
        }

        /// <summary>是否为本工具自绘的高亮/辅助笔画（不应参与形状识别）。</summary>
        private bool IsShapeToolGuideStroke(Stroke stroke)
        {
            if (stroke == null) return false;
            return ReferenceEquals(stroke, _parallelBaselineStroke)
                   || ReferenceEquals(stroke, _perpendicularBaselineStroke)
                   || ReferenceEquals(stroke, _angleBisectorSide1Stroke)
                   || ReferenceEquals(stroke, _angleBisectorSide2Stroke)
                   || ReferenceEquals(stroke, _tangentCircleGuide)
                   || ReferenceEquals(stroke, _perpendicularBisectorGuide)
                   || ReferenceEquals(stroke, _midpointGuide);
        }

        /// <summary>按住圆后，用荧光笔圆环把选中的圆标示出来。</summary>
        private void ShowTangentCircleGuide(Point center, double radius)
        {
            RemoveTangentCircleGuide();
            if (inkCanvas == null) return;

            var guide = CreateCircleStroke(center, radius, CreatePerpendicularGuideDrawingAttributes());
            if (guide == null) return;

            var previousCommitType = _currentCommitType;
            _currentCommitType = CommitReason.CodeInput; // 高亮不是用户内容，不写入撤销历史
            try
            {
                inkCanvas.Strokes.Add(guide);
                _tangentCircleGuide = guide;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                _currentCommitType = previousCommitType;
            }
        }

        /// <summary>移除选中圆的高亮圆环。</summary>
        private void RemoveTangentCircleGuide()
        {
            var guide = _tangentCircleGuide;
            _tangentCircleGuide = null;
            if (guide == null || inkCanvas == null) return;

            var previousCommitType = _currentCommitType;
            _currentCommitType = CommitReason.CodeInput;
            try
            {
                if (inkCanvas.Strokes.Contains(guide)) inkCanvas.Strokes.Remove(guide);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                _currentCommitType = previousCommitType;
            }
        }

        /// <summary>清掉选中圆、高亮圆环、拖动预览与拖动状态。</summary>
        private void ResetTangentCircleState()
        {
            RemoveTangentCircleGuide();
            RemoveTangentPreview();
            _tangentCircleCenter = default;
            _tangentCircleRadius = 0;
            _tangentCircleDragging = false;
            _tangentTapPoint = null;
            _tangentPrimaryTouchId = -1;
        }

        /// <summary>离开圆的切线工具时收尾：移除高亮与预览。</summary>
        private void CancelTangentCircleDrawing()
        {
            ResetTangentCircleState();
        }

        #endregion

        private void inkCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 多边形：按下不绘制，顶点在抬起（单击）时确定
            if (drawingShapeMode == 12 && e.ChangedButton == MouseButton.Left)
            {
                e.Handled = true;
                return;
            }

            // 圆的切线：按下即尝试选中被按住的圆并进入拖动（不拦截，继续走通用捕获流程）
            if (drawingShapeMode == TangentShapeMode && e.ChangedButton == MouseButton.Left)
            {
                BeginTangentDragAt(e.GetPosition(inkCanvas));
            }

            // 平行线 / 垂线：第一步改为按下时点选一条已有直线作为基准
            if ((drawingShapeMode == 17 || drawingShapeMode == 21)
                && drawMultiStepShapeCurrentStep == 0
                && e.ChangedButton == MouseButton.Left)
            {
                BeginBaselinePickAt(e.GetPosition(inkCanvas));
            }

            // 角平分线：两步都是按下时点选角的边（第一步一条边、第二步另一条边）
            if (drawingShapeMode == 22 && e.ChangedButton == MouseButton.Left)
            {
                BeginAngleBisectorSidePick(e.GetPosition(inkCanvas));
            }

            // 油漆桶：单击（抬起）时按落点填充，按下不做任何事
            if (drawingShapeMode == PaintBucketShapeMode && e.ChangedButton == MouseButton.Left)
            {
                _paintBucketTapPointValid = false;
                e.Handled = true;
                return;
            }

            // 鼠标按下开始新的一笔，清掉上一次触摸落点
            if (drawingShapeMode == 17) _parallelUpPoint = null;
            if (drawingShapeMode == 21) _perpendicularUpPoint = null;
            if (drawingShapeMode == 22) _angleBisectorUpPoint = null;
            if (drawingShapeMode == 26) _perpendicularBisectorUpPoint = null;
            // 中垂线 / 中点：新的一次按下，允许重新绘制（上一次手势的收尾锁定到此解除）
            if (drawingShapeMode == 26) _perpendicularBisectorAwaitNewGesture = false;
            if (drawingShapeMode == 15) _midpointAwaitNewGesture = false;
            // 画圆弧：新的一笔开始，收起上一次圆弧的端点手柄并解除收尾锁定
            if (drawingShapeMode == 27)
            {
                _arcAwaitNewGesture = false;
                ClearArcHandleHighlight();
            }
            // 沿轴翻转：新的一次按下清掉上一帧的轴线（起点与 isMouseDown 由 Window_PreviewMouseDown
            // 调用 BeginAxisFlipDragAt 置位，这里不再重复提示，避免两条 ④ 提示叠在一起）
            if (drawingShapeMode == AxisFlipShapeMode)
            {
                RemoveAxisFlipGuide();
                _axisFlipAxisEndValid = false;
            }

            if (e.ChangedButton == MouseButton.Left && inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke)
            {
                BeginSecAgentStrokeErase(e.GetPosition(inkCanvas));
                if (MoveSecAgentStrokeErase(e.GetPosition(inkCanvas)))
                {
                    e.Handled = true;
                    return;
                }
            }
            if (IsBoardRoamingMode && e.ChangedButton == MouseButton.Left)
            {
                inkCanvas.CaptureMouse();
                ViewboxFloatingBar.IsHitTestVisible = false;
                BlackboardUIGridForInkReplay.IsHitTestVisible = false;
                BeginBoardRoaming(e.GetPosition(inkCanvas));
                e.Handled = true;
                return;
            }

            // 鼠标自由书写：启用实时笔锋（InkStyle==3）时走实时墨迹收集路径
            if (e.ChangedButton == MouseButton.Left && ShouldUseRealtimeVelocityBrushTipForMouse() && drawingShapeMode == 0)
            {
                _isMouseRealtimeInking = true;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                var p = e.GetPosition(inkCanvas);
                CancelPauseStraightenTimer(MouseRealtimeStrokeId);
                InitializeRealtimeBrushTipStateFromPoint(MouseRealtimeStrokeId, p);
                var sv = GetStrokeVisual(MouseRealtimeStrokeId);
                RealtimeInkPerformanceMonitor.BeginStroke(sv, RealtimeInkInputKind.Mouse);
                TryAppendRealtimeVelocityBrushTipPoint(sv, MouseRealtimeStrokeId, p);
                RealtimeInkFrameScheduler.RequestRedraw(sv);
            }

            inkCanvas.CaptureMouse();
            ViewboxFloatingBar.IsHitTestVisible = false;
            BlackboardUIGridForInkReplay.IsHitTestVisible = false;

            isMouseDown = true;
            // 坐标系每次拖拽都重新分组，避免与上一次绘制的坐标系混用同一个标记
            if (drawingShapeMode == 11) _pendingCoordinateTag = Guid.Empty;
            if (NeedUpdateIniP()) iniP = e.GetPosition(inkCanvas);
        }

        /// <summary>
        /// 处理墨水画布的鼠标移动事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标事件参数</param>
        /// <remarks>
        /// 当在墨水画布上移动鼠标时：
        /// 1. 如果鼠标处于按下状态，调用MouseTouchMove方法处理移动
        /// 2. 如果启用了光标显示，根据编辑模式设置光标
        /// </remarks>
        private void inkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            // 沿轴翻转兜底：若抬起事件没送到画布（拖到窗口外松手、丢失捕获等），
            // isMouseDown 会一直为 true，之后不按键的移动就会被当成拖轴持续重绘。
            // 这里发现左键已松开就立即收尾：有有效终点则按该轴完成镜像，否则直接取消。
            if (drawingShapeMode == AxisFlipShapeMode
                && isMouseDown
                && e.LeftButton == MouseButtonState.Released)
            {
                isMouseDown = false;
                if (_axisFlipAxisEndValid)
                    CompleteAxisFlip(_axisFlipAxisEnd);
                else
                    CancelAxisFlip();
                if (inkCanvas.IsMouseCaptured) inkCanvas.ReleaseMouseCapture();
                return;
            }

            // 多边形：仅刷新“已确定顶点 + 光标”的预览，不参与拖动绘制
            if (drawingShapeMode == 12)
            {
                if (IsPolygonDrawing)
                    UpdatePolygonPreview(e.GetPosition(inkCanvas));
                if (Settings.Canvas.IsShowCursor)
                    SetCursorBasedOnEditingMode(inkCanvas);
                return;
            }

            // 橡皮擦：填充是画布子元素（矢量路径），InkCanvas 原生擦除不会处理，这里单独命中检测
            if ((inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                 || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint)
                && e.LeftButton == MouseButtonState.Pressed)
            {
                ErasePaintFillElementsAt(e.GetPosition(inkCanvas));
            }

            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                && e.LeftButton == MouseButtonState.Pressed
                && MoveSecAgentStrokeErase(e.GetPosition(inkCanvas)))
            {
                e.Handled = true;
                return;
            }
            // 视频展台特殊模式：鼠标拖动摄像头预览画面
            if (_isBoothMouseDragging)
            {
                VideoPresenterSpecialMode_HandleMouseMove(e);
                e.Handled = true;
                return;
            }

            // 板漫游：鼠标拖动移动整个板内容
            if (_isBoardRoamingPointerDown)
            {
                MoveBoardRoaming(e.GetPosition(inkCanvas));
                e.Handled = true;
                return;
            }

            if (_isMouseRealtimeInking && isMouseDown)
            {
                var sv = GetStrokeVisual(MouseRealtimeStrokeId);
                if (TryAppendRealtimeVelocityBrushTipPoint(sv, MouseRealtimeStrokeId, e.GetPosition(inkCanvas)))
                {
                    RealtimeInkFrameScheduler.RequestRedraw(sv);
                    ResetPauseStraightenTimer(MouseRealtimeStrokeId);
                }
                else
                {
                    _isMouseRealtimeInking = false;
                    MouseTouchMove(e.GetPosition(inkCanvas));
                }
            }
            else if (isMouseDown)
            {
                MouseTouchMove(e.GetPosition(inkCanvas));
            }

            if (Settings.Canvas.IsShowCursor)
            {
                SetCursorBasedOnEditingMode(inkCanvas);
            }
        }

        /// <summary>
        /// 处理墨水画布的鼠标抬起事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当在墨水画布上抬起鼠标时：
        /// 1. 释放鼠标捕获
        /// 2. 启用浮动栏和黑板UI的命中测试
        /// 3. 根据不同的绘制模式进行处理：
        ///    - 圆形模式：创建圆形对象并添加到圆形集合
        ///    - 长方体模式：处理长方体绘制的两个步骤
        ///    - 双曲线/贝塞尔/垂线模式：处理多步绘制的步骤切换
        /// 4. 还原到笔模式（除特殊情况外）
        /// 5. 提交笔画历史记录
        /// 6. 清理临时笔画和状态
        /// 7. 恢复FitToCurve设置
        /// </remarks>
        /// <summary>
        /// 鼠标捕获丢失（松手、切换窗口等）：中垂线/中点在此收尾，确保荧光笔线段一定被移除。
        /// </summary>
        private void inkCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (drawingShapeMode == 26) CompletePerpendicularBisectorNow();
            if (drawingShapeMode == 15) CompleteMidpointNow();
            if (drawingShapeMode == 27) RemoveArcRadiusGuide();
            if (drawingShapeMode == AxisFlipShapeMode) CancelAxisFlip();
            // 圆的切线：拖动中丢失捕获（Alt+Tab、拖到窗口外等）时清掉预览与高亮，避免残留
            if (drawingShapeMode == TangentShapeMode && _tangentCircleDragging) CancelTangentCircleDrawing();
        }



        private void inkCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            EndSecAgentStrokeErase();

            // 圆的切线：松手即按拖动终点生成两条切线（触摸路径优先用抬笔位置）
            if (drawingShapeMode == TangentShapeMode)
            {
                // 触摸/手写笔路径下合成的事件拿不到真实触控坐标，优先用抬笔位置
                var releasePoint = _tangentTapPoint ?? e.GetPosition(inkCanvas);
                _tangentTapPoint = null;

                // 必须在 ReleaseMouseCapture 之前提交：释放捕获会同步触发 inkCanvas_LostMouseCapture，
                // 那里会把未收尾的拖动状态清掉，之后再生成切线就没有圆心/半径了
                if (e.ChangedButton == MouseButton.Left)
                    CommitTangentDrag(releasePoint);

                inkCanvas.ReleaseMouseCapture();
                ViewboxFloatingBar.IsHitTestVisible = true;
                BlackboardUIGridForInkReplay.IsHitTestVisible = true;
                isMouseDown = false;
                e.Handled = true;
                return;
            }

            // 多边形：抬起即视为一次单击，添加顶点或点击起点闭合完成
            if (drawingShapeMode == 12)
            {
                inkCanvas.ReleaseMouseCapture();
                ViewboxFloatingBar.IsHitTestVisible = true;
                BlackboardUIGridForInkReplay.IsHitTestVisible = true;
                isMouseDown = false;
                if (e.ChangedButton == MouseButton.Left)
                    HandlePolygonClick(e.GetPosition(inkCanvas));
                e.Handled = true;
                return;
            }
            // 油漆桶：抬起即视为一次单击，按落点填充闭合区域
            if (drawingShapeMode == PaintBucketShapeMode)
            {
                inkCanvas.ReleaseMouseCapture();
                ViewboxFloatingBar.IsHitTestVisible = true;
                BlackboardUIGridForInkReplay.IsHitTestVisible = true;
                isMouseDown = false;

                var fillPoint = _paintBucketTapPointValid ? _paintBucketTapPoint : e.GetPosition(inkCanvas);
                _paintBucketTapPointValid = false;
                if (e.ChangedButton == MouseButton.Left)
                    TryFillRegionAt(fillPoint);

                e.Handled = true;
                return;
            }

            // 视频展台特殊模式：结束鼠标拖动
            if (_isBoothMouseDragging)
            {
                VideoPresenterSpecialMode_HandleMouseUp(e);
                e.Handled = true;
                return;
            }

            // 板漫游：结束拖动
            if (_isBoardRoamingPointerDown)
            {
                EndBoardRoaming();
                inkCanvas.ReleaseMouseCapture();
                ViewboxFloatingBar.IsHitTestVisible = true;
                BlackboardUIGridForInkReplay.IsHitTestVisible = true;
                e.Handled = true;
                return;
            }

            // 平行线第二步：抬起时按落点补绘一次，保证“单击”也能完成绘制
            if (drawingShapeMode == 17 && drawMultiStepShapeCurrentStep == 1)
            {
                var point = _parallelUpPoint ?? e.GetPosition(inkCanvas);
                _parallelUpPoint = null;
                MouseTouchMove(point);
            }

            // 垂线第二步：抬起时按落点补绘一次，保证“单击”也能完成绘制
            if (drawingShapeMode == 21 && drawMultiStepShapeCurrentStep == 1)
            {
                var point = _perpendicularUpPoint ?? e.GetPosition(inkCanvas);
                _perpendicularUpPoint = null;
                MouseTouchMove(point);
            }

            // 角平分线第二步：抬起时按落点补绘一次，保证“单击”也能完成绘制；
            // 第二条边没点中时保持第二步，等下一次点击，不能把工具直接收尾
            if (drawingShapeMode == 22 && drawMultiStepShapeCurrentStep == 1
                && _angleBisectorSide2Stroke != null)
            {
                var point = _angleBisectorUpPoint ?? e.GetPosition(inkCanvas);
                _angleBisectorUpPoint = null;
                MouseTouchMove(point);
            }

            // 中垂线：先用真实松手点补画最后一帧（否则中点/方向会停在最后一次移动事件上），
            // 再移除荧光笔线段，只保留垂直平分线与直角标记
            if (drawingShapeMode == 26)
            {
                if (!_perpendicularBisectorAwaitNewGesture)
                {
                    var releasePoint = _perpendicularBisectorUpPoint ?? e.GetPosition(inkCanvas);
                    if (GetDistance(iniP, releasePoint) >= 8)
                    {
                        MouseTouchMove(releasePoint);
                    }
                    else
                    {
                        // 收尾点离起点太近：不生成退化的中垂线，也别把上一帧的旧位置提交上去
                        CancelPerpendicularBisector();
                        lastTempStrokeCollection = null;
                    }
                }

                _perpendicularBisectorUpPoint = null;
                CompletePerpendicularBisectorNow();
            }

            // 中点：松手后移除荧光笔线段，只保留中点标记
            if (drawingShapeMode == 15) CompleteMidpointNow();

            var completedShapeMode = drawingShapeMode;

            if (_isMouseRealtimeInking)
            {
                StrokeVisual sv = null;
                try
                {
                    sv = GetStrokeVisual(MouseRealtimeStrokeId);
                    RealtimeInkFrameScheduler.Flush(sv);
                    var stroke = sv?.Stroke;
                    if (stroke != null)
                    {
                        if (!stroke.ContainsPropertyData(RealtimeVelocityBrushTipAppliedGuid))
                            stroke.AddPropertyData(RealtimeVelocityBrushTipAppliedGuid, true);
                        inkCanvas.Strokes.Add(stroke);
                        inkCanvas_StrokeCollected(inkCanvas, new InkCanvasStrokeCollectedEventArgs(stroke));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
                finally
                {
                    RealtimeInkFrameScheduler.Cancel(sv);
                    if (VisualCanvasList.TryGetValue(MouseRealtimeStrokeId, out var vc) && inkCanvas.Children.Contains(vc))
                        inkCanvas.Children.Remove(vc);
                    StrokeVisualList.Remove(MouseRealtimeStrokeId);
                    VisualCanvasList.Remove(MouseRealtimeStrokeId);
                    TouchDownPointsList.Remove(MouseRealtimeStrokeId);
                    CleanupRealtimeBrushTipState(MouseRealtimeStrokeId);
                    CancelPauseStraightenTimer(MouseRealtimeStrokeId);
                    RealtimeInkPerformanceMonitor.EndStroke(sv);
                    _isMouseRealtimeInking = false;
                }
            }

            HandleEraserOperationEnded();

            // 沿轴翻转：抬手即按拖出的轴线镜像选中的墨迹（原图保留、轴线消失）。
            // 必须在 ReleaseMouseCapture 之前完成：释放捕获会同步触发 inkCanvas_LostMouseCapture，
            // 那里调用 CancelAxisFlip 把 drawingShapeMode 复位成 0，之后再判断就永远进不来。
            if (drawingShapeMode == AxisFlipShapeMode)
                CompleteAxisFlip(_axisFlipAxisEndValid ? _axisFlipAxisEnd : e.GetPosition(inkCanvas));

            inkCanvas.ReleaseMouseCapture();
            ViewboxFloatingBar.IsHitTestVisible = true;
            BlackboardUIGridForInkReplay.IsHitTestVisible = true;

            // 画圆弧：抬笔后只保留弧线，半径提示线移除
            if (drawingShapeMode == 27) RemoveArcRadiusGuide();

            if (drawingShapeMode == 5)
            {
                if (lastTempStroke != null)
                {
                    var circle = new Circle(new Point(), 0, lastTempStroke);
                    circle.R = GetDistance(circle.Stroke.StylusPoints[0].ToPoint(),
                        circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].ToPoint()) / 2;
                    circle.Centroid = new Point(
                        (circle.Stroke.StylusPoints[0].X +
                         circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].X) / 2,
                        (circle.Stroke.StylusPoints[0].Y +
                         circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].Y) / 2);
                    circles.Add(circle);
                }

                if (lastIsInMultiTouchMode)
                {
                    ToggleSwitchEnableMultiTouchMode.IsOn = true;
                    lastIsInMultiTouchMode = false;
                }
            }

            if (drawingShapeMode != 9 && drawingShapeMode != 0 && drawingShapeMode != 24 && drawingShapeMode != 25
                && drawingShapeMode != 21 && drawingShapeMode != 26 && drawingShapeMode != 17
                && drawingShapeMode != 15 && drawingShapeMode != 27)
            {
                if (isLongPressSelected) { }
                else
                {
                    BtnPen_Click(null, null); //画完一次还原到笔模式
                    if (lastIsInMultiTouchMode)
                    {
                        ToggleSwitchEnableMultiTouchMode.IsOn = true;
                        lastIsInMultiTouchMode = false;
                    }
                }
            }

            if (drawingShapeMode == 9)
            {
                if (isFirstTouchCuboid)
                {
                    if (CuboidStrokeCollection == null) CuboidStrokeCollection = new StrokeCollection();
                    isFirstTouchCuboid = false;
                    var newIniP = new Point(Math.Min(CuboidFrontRectIniP.X, CuboidFrontRectEndP.X),
                        Math.Min(CuboidFrontRectIniP.Y, CuboidFrontRectEndP.Y));
                    var newEndP = new Point(Math.Max(CuboidFrontRectIniP.X, CuboidFrontRectEndP.X),
                        Math.Max(CuboidFrontRectIniP.Y, CuboidFrontRectEndP.Y));
                    CuboidFrontRectIniP = newIniP;
                    CuboidFrontRectEndP = newEndP;
                    try
                    {
                        CuboidStrokeCollection.Add(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }
                }
                else
                {
                    BtnPen_Click(null, null); //画完还原到笔模式
                    if (lastIsInMultiTouchMode)
                    {
                        ToggleSwitchEnableMultiTouchMode.IsOn = true;
                        lastIsInMultiTouchMode = false;
                    }

                    if (_currentCommitType == CommitReason.ShapeDrawing)
                    {
                        try
                        {
                            CuboidStrokeCollection.Add(lastTempStrokeCollection);
                        }
                        catch
                        {
                            Trace.WriteLine("lastTempStrokeCollection failed.");
                        }

                        _currentCommitType = CommitReason.UserInput;
                        timeMachine.CommitStrokeUserInputHistory(CuboidStrokeCollection);
                        CuboidStrokeCollection = null;
                    }
                }
            }

            if (drawingShapeMode == 24 || drawingShapeMode == 25 || drawingShapeMode == 21 || drawingShapeMode == 22
                || drawingShapeMode == 26 || drawingShapeMode == 17 || drawingShapeMode == 15)
            {
                if (drawingShapeMode == 15)
                {
                    // 中点是单步图形，与中垂线同样「抬起即完成」
                    CompleteMidpointNow();
                    drawMultiStepShapeCurrentStep = 0;
                }
                else if (drawingShapeMode == 26)
                {
                    // 中垂线是单步图形，但沿用垂线这套「抬起即完成」的收尾位置：
                    // 移除荧光笔线段，只保留垂直平分线与直角标记，并提交一次撤销
                    CompletePerpendicularBisectorNow();
                    drawMultiStepShapeCurrentStep = 0;
                }
                else if (drawMultiStepShapeCurrentStep == 0)
                {
                    // 平行线 / 垂线 / 角平分线：单击未拖出基准（边）时不进入第二步
                    var canEnterNextStep = (drawingShapeMode != 17 || _parallelBaselineStroke != null)
                                           && (drawingShapeMode != 21 || _perpendicularBaselineStroke != null)
                                           && (drawingShapeMode != 22 || _angleBisectorSide1Stroke != null);
                    if (canEnterNextStep)
                    {
                        drawMultiStepShapeCurrentStep = 1;
                        if (drawingShapeMode == 17)
                        {
                            // 基准辅助线继续显示到第二步完成，但不能再被预览清理逻辑删除
                            _parallelLastPreview = null;
                        }
                        else if (drawingShapeMode == 21)
                        {
                            // 基准辅助线继续显示到第二步完成，但不能再被预览清理逻辑删除
                            _perpendicularLastPreview = null;
                        }
                        else if (drawingShapeMode == 22)
                        {
                            // 第一条边已完成并留在画布上，第二步的预览另行跟踪；
                            // 此时不能把第一步当成一笔入撤销栈，等第二步完成时整体提交
                            _angleBisectorLastPreview = null;
                            _currentCommitType = CommitReason.UserInput;
                        }
                    }
                }
                else
                {
                    drawMultiStepShapeCurrentStep = 0;
                    if (drawingShapeMode == 24)
                    {
                        // 普通双曲线：按设置决定是否保留渐近线
                        if (drawMultiStepShapeSpecialStrokeCollection != null)
                        {
                            switch (Settings.Canvas.HyperbolaAsymptoteOption)
                            {
                                case OptionalOperation.Yes:
                                    // 保留渐近线（不移除）
                                    break;
                                case OptionalOperation.No:
                                    RemoveAsymptotesByTag(_currentAsymptoteTagGuid);
                                    break;
                                case OptionalOperation.Ask:
                                    // 捕获本次双曲线的渐近线标记：延迟回调执行时按标记删除当前这次的渐近线，
                                    // 避免字段被后续流程重置或引用跟踪失效导致删不掉。
                                    var asymptoteTag = _currentAsymptoteTagGuid;
                                    // 延迟到当前触摸/鼠标抬起事件处理完成后再弹窗，并改用自建对话框：
                                    // 自建窗口可 Topmost + 强制前台 + 聚焦按钮，解决白板/触控下
                                    // “点一次关不掉、需多次点击”的问题（iNKORE MessageBox 无法控制这些）。
                                    this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                                    {
                                        try
                                        {
                                            this.Activate();
                                            this.Focus();
                                            var confirm = new AsymptoteConfirmWindow { Owner = this };
                                            confirm.ShowDialog();
                                            // 问句为“是否移除渐近线？”：选“是”=移除，选“否”=保留
                                            if (confirm.RemoveRequested) RemoveAsymptotesByTag(asymptoteTag);
                                        }
                                        catch { }
                                    }));
                                    break;
                            }
                        }

                        // 本次双曲线已提交：置空预览跟踪，避免下一笔开头清理时误删已完成的双曲线
                        _hyperbolaLastPreview = null;
                    }
                    else if (drawingShapeMode == 25)
                    {
                        // 贝塞尔曲线（模式 25）：辅助弦始终移除，只保留曲线本身
                        if (drawMultiStepShapeSpecialStrokeCollection != null && drawMultiStepShapeSpecialStrokeCollection.Count > 0)
                        {
                            try { inkCanvas.Strokes.Remove(drawMultiStepShapeSpecialStrokeCollection); }
                            catch { }
                        }

                        _bezierLastPreview = null;
                        _bezierEndPoint = default;
                    }
                    else if (drawingShapeMode == 22)
                    {
                        // 角平分线（模式 22）：完成时移除两条边的荧光笔辅助线，只提交平分线与等角弧
                        RemoveAngleBisectorStroke(_angleBisectorSide1Stroke);
                        RemoveAngleBisectorStroke(_angleBisectorSide2Stroke);
                        _angleBisectorSide1Stroke = null;
                        _angleBisectorSide2Stroke = null;

                        if (_angleBisectorLastPreview != null && _angleBisectorLastPreview.Count > 0)
                            lastTempStrokeCollection = _angleBisectorLastPreview;

                        _angleBisectorLastPreview = null;
                        _angleBisectorUpPoint = null;
                        _angleBisectorVertex = default;
                        _angleBisectorSide1End = default;
                    }
                    else if (drawingShapeMode == 17)
                    {
                        // 平行线（模式 17）：完成时移除第一步的红色荧光笔基准辅助线，只提交平行线
                        RemoveParallelGuideStroke();
                        _parallelLastPreview = null;
                        _parallelBaselineStroke = null;
                        _parallelBaseStart = default;
                        _parallelBaseEnd = default;
                        _parallelUpPoint = null;
                    }
                    else
                    {
                        // 垂线（模式 21）：完成时移除第一步的红色荧光笔基准辅助线，只提交垂线与直角符号
                        RemovePerpendicularGuideStroke();
                        _perpendicularLastPreview = null;
                        _perpendicularBaselineStroke = null;
                        _perpendicularBaseStart = default;
                        _perpendicularBaseEnd = default;
                        _perpendicularUpPoint = null;
                    }

                    // 平行线 / 垂线 / 角平分线工具完成后保持选中，便于连续绘制；其余多步图形仍还原到笔模式
                    if (drawingShapeMode != 15 && drawingShapeMode != 17 && drawingShapeMode != 21
                        && drawingShapeMode != 22)
                    {
                        BtnPen_Click(null, null); //画完还原到笔模式
                        if (lastIsInMultiTouchMode)
                        {
                            ToggleSwitchEnableMultiTouchMode.IsOn = true;
                            lastIsInMultiTouchMode = false;
                        }
                    }
                }
            }

            isMouseDown = false;
            if (ReplacedStroke != null || AddedStroke != null)
            {
                timeMachine.CommitStrokeEraseHistory(ReplacedStroke, AddedStroke);
                AddedStroke = null;
                ReplacedStroke = null;
            }

            if (_currentCommitType == CommitReason.ShapeDrawing && drawingShapeMode != 9)
            {
                _currentCommitType = CommitReason.UserInput;
                StrokeCollection collection = null;
                if (lastTempStrokeCollection != null && lastTempStrokeCollection.Count > 0)
                    collection = lastTempStrokeCollection;
                else if (lastTempStroke != null) collection = new StrokeCollection { lastTempStroke };
                if (collection != null) timeMachine.CommitStrokeUserInputHistory(collection);
                // 坐标系：登记整组笔画，选中后可拖动箭头端重新设定 X/Y 轴长度
                if (completedShapeMode == 11 && collection != null)
                    RegisterCoordinateSystem(collection, _pendingCoordinateTag);
                // 平行线 / 垂线 / 角平分线：登记主线笔画，选中后可拖动端点伸缩（角平分线只有远端）
                else if (completedShapeMode == 17 && collection != null)
                    RegisterGeometryLine(collection, GeometryLineKind.Parallel);
                else if (completedShapeMode == 21 && collection != null)
                    RegisterGeometryLine(collection, GeometryLineKind.Perpendicular);
                else if (completedShapeMode == 22 && collection != null)
                    RegisterGeometryLine(collection, GeometryLineKind.AngleBisector);
                // 画圆弧：登记圆弧笔画，选中或刚画完时可拖动两个端点改变弧长（半径不变）
                else if (completedShapeMode == 27 && collection != null)
                {
                    RegisterArc(collection);
                    // 收尾锁定：等下一次落笔再允许新建圆弧
                    _arcAwaitNewGesture = true;
                }
            }

            if (completedShapeMode == 1 && lastTempStroke != null &&
                inkCanvas.Strokes.Contains(lastTempStroke))
                PublishPluginCanvasLineCandidate(
                    lastTempStroke,
                    Plugins.CanvasLineSource.GeometryLine);

            lastTempStroke = null;
            lastTempStrokeCollection = null;

            if (StrokeManipulationHistory?.Count > 0)
            {
                timeMachine.CommitStrokeManipulationHistory(StrokeManipulationHistory);
                foreach (var item in StrokeManipulationHistory)
                {
                    StrokeInitialHistory[item.Key] = item.Value.Item2;
                }
                StrokeManipulationHistory = null;
            }

            if (DrawingAttributesHistory.Count > 0)
            {
                timeMachine.CommitStrokeDrawingAttributesHistory(DrawingAttributesHistory);
                DrawingAttributesHistory = new Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>>();
                foreach (var item in DrawingAttributesHistoryFlag)
                {
                    item.Value.Clear();
                }
            }

            if (Settings.Canvas.FitToCurve == true) drawingAttributes.FitToCurve = true;
        }

        /// <summary>
        /// 检查是否需要更新起始点
        /// </summary>
        /// <returns>返回是否需要更新起始点</returns>
        /// <remarks>
        /// 检查当前绘制模式和步骤，判断是否需要更新起始点：
        /// 1. 对于双曲线模式（24和25），如果是第二笔（步骤1），则不更新起点
        /// 2. 其他情况都需要更新起点
        /// </remarks>
        private bool NeedUpdateIniP()
        {
            if (drawingShapeMode == 24 || drawingShapeMode == 25)
            {
                if (drawMultiStepShapeCurrentStep == 1)
                    return false; // 第二笔不更新起点
            }
            return true;
        }
        /// <summary>
        /// 绘制圆心标记
        /// </summary>
        /// <param name="centerPoint">圆心位置</param>
        private void DrawCircleCenter(Point centerPoint)
        {
            try
            {
                // 创建一个点作为圆心标记
                var centerSize = 0.5; // 圆心标记的大小

                // 创建一个小圆作为圆心标记
                var circlePoints = new List<Point>();
                for (double angle = 0; angle <= 2 * Math.PI; angle += 0.1)
                {
                    circlePoints.Add(new Point(
                        centerPoint.X + centerSize * Math.Cos(angle),
                        centerPoint.Y + centerSize * Math.Sin(angle)
                    ));
                }

                // 绘制圆心点
                var point = new StylusPointCollection(circlePoints);
                var stroke = new Stroke(point)
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };

                // 设置圆心点的样式
                stroke.DrawingAttributes.Width = 2.0;
                stroke.DrawingAttributes.Height = 2.0;

                // 添加到画布
                inkCanvas.Strokes.Add(stroke);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"绘制圆心标记失败: {ex.Message}");
            }
        }
        /// <summary>
        /// 处理主窗口的鼠标移动事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标事件参数</param>
        /// <remarks>
        /// 当在主窗口上移动鼠标时：
        /// 1. 如果启用了光标显示，则显示光标并根据编辑模式设置光标
        /// 2. 如果禁用了光标显示：
        ///    - 如果没有触笔设备，则显示光标
        ///    - 如果有触笔设备，则隐藏光标
        /// </remarks>
        private void MainWindow_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (Settings.Canvas.IsShowCursor)
            {
                System.Windows.Forms.Cursor.Show();
                SetCursorBasedOnEditingMode(inkCanvas);
            }
            else
            {
                if (e.StylusDevice == null)
                {
                    System.Windows.Forms.Cursor.Show();
                }
                else
                {
                    System.Windows.Forms.Cursor.Hide();
                }
            }
        }

        #region 坐标系轴长调节

        /// <summary>
        /// 坐标系的一个笔画版本。撤销/重做会在画布上换回旧版本的笔画，
        /// 因此一个坐标系保留多个版本，取「当前仍在画布上」的最新一版作为生效版本。
        /// 原点与轴长不单独存字段，一律从笔画几何反推，这样整组被移动/旋转/缩放后依然正确。
        /// </summary>
        private sealed class CoordinateSystemVersion
        {
            public StrokeCollection Strokes;
        }

        /// <summary>可调节轴长的坐标系：整组笔画的分组标记 + 笔画版本列表。</summary>
        private sealed class CoordinateSystemRecord
        {
            public readonly List<CoordinateSystemVersion> Versions = new List<CoordinateSystemVersion>();
            public Guid Tag;
        }

        private readonly List<CoordinateSystemRecord> _coordinateSystems = new List<CoordinateSystemRecord>();

        /// <summary>本次绘制的坐标系分组标记（绘制完成后据此注册记录）。</summary>
        private Guid _pendingCoordinateTag = Guid.Empty;

        /// <summary>轴长手柄对应的臂（四条臂各一个手柄）。</summary>
        private enum CoordinateArmHandle
        {
            None,
            XPlus,
            XMinus,
            YPlus,
            YMinus
        }

        private CoordinateSystemRecord _coordinateHandleRecord;
        private bool _coordinateHandleDragging;
        private CoordinateArmHandle _coordinateHandleArm = CoordinateArmHandle.None;
        private StrokeCollection _coordinateDragOriginalStrokes;

        /// <summary>给坐标系的整组笔画打上分组标记。</summary>
        private static void TagCoordinateStrokes(StrokeCollection strokes, Guid tag)
        {
            if (strokes == null || tag == Guid.Empty) return;
            foreach (var stroke in strokes)
            {
                try
                {
                    if (!stroke.ContainsPropertyData(tag)) stroke.AddPropertyData(tag, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        /// <summary>坐标系四条臂（X 正/负、Y 正/负方向）的长度。</summary>
        private struct CoordinateArms
        {
            public double XPlus;
            public double XMinus;
            public double YPlus;
            public double YMinus;
        }

        /// <summary>
        /// 生成整组坐标系笔画（两条带箭头坐标轴 + 单位刻度）。
        /// 先在以原点为中心、X 轴向右 Y 轴向下的局部坐标系里生成，
        /// 再用 (axisX, axisY) 构成的矩阵映射到画布，因此支持整组被旋转/缩放后的重建。
        /// </summary>
        private StrokeCollection BuildCoordinateSystemStrokes(Point origin, Vector axisX, Vector axisY,
            CoordinateArms arms, Guid tag)
        {
            var strokes = BuildCanonicalCoordinateSystemStrokes(arms, tag);
            var matrix = new Matrix(axisX.X, axisX.Y, axisY.X, axisY.Y, origin.X, origin.Y);
            foreach (Stroke stroke in strokes)
            {
                try
                {
                    stroke.Transform(matrix, false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            return strokes;
        }

        /// <summary>
        /// 在局部坐标系（原点 0,0、正 X 向右、正 Y 向下）里生成整组笔画。
        /// 四条臂长各自独立，互不影响。
        /// </summary>
        private StrokeCollection BuildCanonicalCoordinateSystemStrokes(CoordinateArms arms, Guid tag)
        {
            var strokes = new StrokeCollection();
            var origin = new Point(0, 0);
            var xAxisEnd = new Point(arms.XPlus, 0);
            var yAxisEnd = new Point(0, arms.YPlus);
            var xAxisStart = new Point(-arms.XMinus, 0);
            var yAxisStart = new Point(0, -arms.YMinus);
            strokes.Add(GenerateArrowLineStroke(xAxisStart, xAxisEnd));
            strokes.Add(GenerateArrowLineStroke(yAxisStart, yAxisEnd));
            AddCoordinateUnitMarks(strokes, origin, xAxisStart, xAxisEnd, yAxisStart, yAxisEnd);
            TagCoordinateStrokes(strokes, tag);
            return strokes;
        }

        /// <summary>
        /// 从整组笔画反推坐标系的当前几何：原点（两轴交点）、X/Y 轴正方向单位向量、四条臂长。
        /// 依据是生成顺序——集合前两条笔画依次为 X 轴、Y 轴，
        /// 每条笔画的前两个点分别是负方向臂端与箭头端。
        /// </summary>
        private static bool TryGetCoordinateFrame(StrokeCollection strokes, out Point origin, out Vector axisX,
            out Vector axisY, out CoordinateArms arms)
        {
            origin = default;
            axisX = new Vector(1, 0);
            axisY = new Vector(0, 1);
            arms = default;

            if (strokes == null || strokes.Count < 2) return false;
            var xStroke = strokes[0];
            var yStroke = strokes[1];
            if (xStroke == null || yStroke == null) return false;
            if (xStroke.StylusPoints.Count < 2 || yStroke.StylusPoints.Count < 2) return false;

            var xStart = xStroke.StylusPoints[0].ToPoint();
            var xEnd = xStroke.StylusPoints[1].ToPoint();
            var yStart = yStroke.StylusPoints[0].ToPoint();
            var yEnd = yStroke.StylusPoints[1].ToPoint();

            if (!TryIntersectLines(xStart, xEnd, yStart, yEnd, out origin)) return false;

            var xArm = xEnd - origin;
            var yArm = yEnd - origin;
            if (xArm.Length < 0.01 || yArm.Length < 0.01) return false;

            arms.XPlus = xArm.Length;
            arms.XMinus = (xStart - origin).Length;
            arms.YPlus = yArm.Length;
            arms.YMinus = (yStart - origin).Length;

            xArm.Normalize();
            yArm.Normalize();
            axisX = xArm;
            axisY = yArm;
            return true;
        }

        /// <summary>求两条直线（由各两点确定）的交点。</summary>
        private static bool TryIntersectLines(Point p1, Point p2, Point p3, Point p4, out Point intersection)
        {
            intersection = default;
            var denominator = (p2.X - p1.X) * (p4.Y - p3.Y) - (p2.Y - p1.Y) * (p4.X - p3.X);
            if (Math.Abs(denominator) < 1e-6) return false;
            var t = ((p3.X - p1.X) * (p4.Y - p3.Y) - (p3.Y - p1.Y) * (p4.X - p3.X)) / denominator;
            intersection = new Point(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y));
            return true;
        }

        /// <summary>绘制完成后把整组笔画登记为「轴长可调」的坐标系。</summary>
        private void RegisterCoordinateSystem(StrokeCollection strokes, Guid tag)
        {
            if (strokes == null || strokes.Count == 0) return;
            if (tag == Guid.Empty) tag = Guid.NewGuid();
            TagCoordinateStrokes(strokes, tag);
            PruneCoordinateSystems();

            var record = new CoordinateSystemRecord { Tag = tag };
            record.Versions.Add(new CoordinateSystemVersion { Strokes = strokes });
            _coordinateSystems.Add(record);
            if (_coordinateSystems.Count > 64)
                _coordinateSystems.RemoveRange(0, _coordinateSystems.Count - 64);
        }

        /// <summary>画布当前笔画集合（用于批量判断某个版本是否仍在画布上，避免逐个 Contains 的 O(n) 扫描）。</summary>
        private HashSet<Stroke> BuildLiveStrokeSet()
        {
            var live = new HashSet<Stroke>();
            if (inkCanvas == null) return live;
            foreach (Stroke stroke in inkCanvas.Strokes) live.Add(stroke);
            return live;
        }

        /// <summary>取该坐标系当前仍在画布上的最新笔画版本（撤销/重做后也能找到正确的一版）。</summary>
        private static CoordinateSystemVersion GetLiveCoordinateVersion(CoordinateSystemRecord record,
            HashSet<Stroke> live)
        {
            if (record == null || live == null) return null;
            for (var i = record.Versions.Count - 1; i >= 0; i--)
            {
                var version = record.Versions[i];
                if (version?.Strokes == null || version.Strokes.Count == 0) continue;
                var alive = true;
                foreach (Stroke stroke in version.Strokes)
                {
                    if (live.Contains(stroke)) continue;
                    alive = false;
                    break;
                }

                if (alive) return version;
            }

            return null;
        }

        /// <summary>取该坐标系在画布上生效的笔画版本。</summary>
        private CoordinateSystemVersion GetLiveCoordinateVersion(CoordinateSystemRecord record)
        {
            return GetLiveCoordinateVersion(record, BuildLiveStrokeSet());
        }

        /// <summary>清理笔画已被擦除/撤销掉的坐标系记录。</summary>
        private void PruneCoordinateSystems()
        {
            if (_coordinateSystems.Count == 0) return;
            var live = BuildLiveStrokeSet();
            for (var i = _coordinateSystems.Count - 1; i >= 0; i--)
            {
                if (GetLiveCoordinateVersion(_coordinateSystems[i], live) == null)
                    _coordinateSystems.RemoveAt(i);
            }
        }

        /// <summary>找出「当前选中墨迹所覆盖」的坐标系（至少选中一半以上的笔画）。</summary>
        private CoordinateSystemRecord FindSelectedCoordinateSystem()
        {
            if (inkCanvas == null || _coordinateSystems.Count == 0) return null;
            var selected = inkCanvas.GetSelectedStrokes();
            if (selected == null || selected.Count == 0) return null;

            var live = BuildLiveStrokeSet();
            for (var i = _coordinateSystems.Count - 1; i >= 0; i--)
            {
                var record = _coordinateSystems[i];
                var version = GetLiveCoordinateVersion(record, live);
                if (version == null)
                {
                    _coordinateSystems.RemoveAt(i);
                    continue;
                }

                var hits = 0;
                foreach (Stroke stroke in version.Strokes)
                {
                    if (selected.Contains(stroke)) hits++;
                }

                if (hits > 0 && hits * 2 >= version.Strokes.Count) return record;
            }

            return null;
        }

        /// <summary>选中坐标系后，在两条正方向轴的箭头端显示轴长调节手柄。</summary>
        private void UpdateCoordinateAxisHandles()
        {
            if (CoordinateAxisHandleCanvas == null) return;

            var record = _coordinateHandleDragging ? _coordinateHandleRecord : FindSelectedCoordinateSystem();
            var version = GetLiveCoordinateVersion(record);
            if (!TryGetCoordinateFrame(version?.Strokes, out var origin, out var axisX, out var axisY, out var arms))
            {
                CoordinateAxisHandleCanvas.Visibility = Visibility.Collapsed;
                if (!_coordinateHandleDragging) _coordinateHandleRecord = null;
                return;
            }

            _coordinateHandleRecord = record;
            // 四条臂的端点各一个手柄
            PositionCoordinateAxisHandle(CoordinateAxisHandleX, origin + axisX * arms.XPlus);
            PositionCoordinateAxisHandle(CoordinateAxisHandleXMinus, origin - axisX * arms.XMinus);
            PositionCoordinateAxisHandle(CoordinateAxisHandleY, origin + axisY * arms.YPlus);
            PositionCoordinateAxisHandle(CoordinateAxisHandleYMinus, origin - axisY * arms.YMinus);
            CoordinateAxisHandleCanvas.Visibility = Visibility.Visible;
        }

        /// <summary>隐藏轴长调节手柄。</summary>
        private void HideCoordinateAxisHandles()
        {
            if (CoordinateAxisHandleCanvas == null) return;
            CoordinateAxisHandleCanvas.Visibility = Visibility.Collapsed;
            if (!_coordinateHandleDragging) _coordinateHandleRecord = null;
        }

        private static void PositionCoordinateAxisHandle(FrameworkElement handle, Point canvasPoint)
        {
            if (handle == null) return;
            System.Windows.Controls.Canvas.SetLeft(handle, canvasPoint.X - handle.Width / 2);
            System.Windows.Controls.Canvas.SetTop(handle, canvasPoint.Y - handle.Height / 2);
        }

        /// <summary>
        /// 逐条移除坐标系笔画。整集合 Remove 只要有一条已不在画布上就会抛 ArgumentException，
        /// 因此这里先拷贝一份再逐条判断，容错处理。
        /// </summary>
        private void RemoveCoordinateSystemStrokes(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0) return;
            var snapshot = new List<Stroke>(strokes.Count);
            foreach (Stroke stroke in strokes) snapshot.Add(stroke);
            foreach (var stroke in snapshot)
            {
                try
                {
                    if (inkCanvas.Strokes.Contains(stroke)) inkCanvas.Strokes.Remove(stroke);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        /// <summary>
        /// 用新的四条臂长重建整组坐标系笔画（删旧集合、加新集合），
        /// 重建期间抑制逐帧入撤销栈，抬笔时由调用方一次性提交。
        /// 未拖动臂的长度按原值原样传入，因此不会被牵连改变。
        /// </summary>
        private void RebuildCoordinateSystem(CoordinateSystemRecord record, Point origin, Vector axisX, Vector axisY,
            CoordinateArms arms)
        {
            var version = GetLiveCoordinateVersion(record);
            if (version == null) return;

            // 必须在替换版本字段之前取出旧笔画：lastVersion 与 version 通常是同一个对象，
            // 替换其 Strokes 会让 version.Strokes 也变成新笔画，导致 Remove 抛「笔画不存在」。
            var oldStrokes = version.Strokes;

            var newStrokes = BuildCoordinateSystemStrokes(origin, axisX, axisY, arms, record.Tag);
            if (newStrokes.Count == 0) return;

            // 拖动过程中的中间版本直接替换，只有"拖动前"那一版需要保留（撤销要能回去）
            var lastVersion = record.Versions.Count > 0 ? record.Versions[record.Versions.Count - 1] : null;
            if (lastVersion != null && !ReferenceEquals(lastVersion.Strokes, _coordinateDragOriginalStrokes))
                lastVersion.Strokes = newStrokes;
            else
                record.Versions.Add(new CoordinateSystemVersion { Strokes = newStrokes });

            if (record.Versions.Count > 16)
                record.Versions.RemoveRange(0, record.Versions.Count - 16);

            var previousCommitType = _currentCommitType;
            _currentCommitType = CommitReason.CodeInput;
            try
            {
                RemoveCoordinateSystemStrokes(oldStrokes);
                inkCanvas.Strokes.Add(newStrokes);
            }
            finally
            {
                _currentCommitType = previousCommitType;
            }

            // 换过笔画后选区会失效，重新选中新笔画，选择框与手柄才能继续跟随
            if (inkCanvas.EditingMode == InkCanvasEditingMode.Select)
            {
                try
                {
                    // 拖动过程中不触发选区事件（否则每帧都会重置旋转中心、挪动选择工具栏）
                    isProgramChangeStrokeSelection = true;
                    inkCanvas.Select(newStrokes);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                finally
                {
                    isProgramChangeStrokeSelection = false;
                }

                try
                {
                    UpdateSelectionDisplay();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            UpdateCoordinateAxisHandles();
        }

        /// <summary>把拖到的臂长限制为不小于 24 像素。</summary>
        private static double ClampCoordinateAxisLength(double length)
        {
            const double minLength = 24;
            return length < minLength ? minLength : length;
        }

        private void CoordinateAxisHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("调整坐标轴长度"))
            {
                e.Handled = true;
                return;
            }

            var record = _coordinateHandleRecord ?? FindSelectedCoordinateSystem();
            var version = GetLiveCoordinateVersion(record);
            if (record == null || version == null) return;

            var arm = ReferenceEquals(sender, CoordinateAxisHandleX) ? CoordinateArmHandle.XPlus
                : ReferenceEquals(sender, CoordinateAxisHandleXMinus) ? CoordinateArmHandle.XMinus
                : ReferenceEquals(sender, CoordinateAxisHandleY) ? CoordinateArmHandle.YPlus
                : ReferenceEquals(sender, CoordinateAxisHandleYMinus) ? CoordinateArmHandle.YMinus
                : CoordinateArmHandle.None;
            if (arm == CoordinateArmHandle.None) return;

            _coordinateHandleRecord = record;
            _coordinateHandleArm = arm;
            _coordinateDragOriginalStrokes = version.Strokes;
            _coordinateHandleDragging = true;

            if (sender is UIElement element) element.CaptureMouse();
            e.Handled = true;
        }

        private void CoordinateAxisHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_coordinateHandleDragging) return;
            if (!(sender is UIElement element) || !element.IsMouseCaptured) return;

            var record = _coordinateHandleRecord;
            var version = GetLiveCoordinateVersion(record);
            if (version == null) return;

            if (!TryGetCoordinateFrame(version.Strokes, out var origin, out var axisX, out var axisY, out var arms))
                return;

            // 把光标投影到被拖动那条臂的方向上，只改这条臂的长度，其余三条臂保持原值
            var position = e.GetPosition(CoordinateAxisHandleCanvas);
            var offset = position - origin;
            switch (_coordinateHandleArm)
            {
                case CoordinateArmHandle.XPlus:
                    arms.XPlus = ClampCoordinateAxisLength(offset.X * axisX.X + offset.Y * axisX.Y);
                    break;
                case CoordinateArmHandle.XMinus:
                    arms.XMinus = ClampCoordinateAxisLength(-(offset.X * axisX.X + offset.Y * axisX.Y));
                    break;
                case CoordinateArmHandle.YPlus:
                    arms.YPlus = ClampCoordinateAxisLength(offset.X * axisY.X + offset.Y * axisY.Y);
                    break;
                case CoordinateArmHandle.YMinus:
                    arms.YMinus = ClampCoordinateAxisLength(-(offset.X * axisY.X + offset.Y * axisY.Y));
                    break;
                default:
                    return;
            }

            RebuildCoordinateSystem(record, origin, axisX, axisY, arms);
            e.Handled = true;
        }

        private void CoordinateAxisHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_coordinateHandleDragging) return;
            _coordinateHandleDragging = false;
            if (sender is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();
            e.Handled = true;

            var version = GetLiveCoordinateVersion(_coordinateHandleRecord);
            var original = _coordinateDragOriginalStrokes;
            _coordinateDragOriginalStrokes = null;

            // 一次拖动只记一条撤销历史（形状替换语义：撤销可回到拖动前的轴长）
            if (original != null && version != null && !ReferenceEquals(original, version.Strokes))
                timeMachine.CommitStrokeShapeHistory(original, version.Strokes);

            UpdateCoordinateAxisHandles();
        }

        #endregion

        #region 几何线条端点伸缩

        /// <summary>可伸缩端点的几何线条类型。</summary>
        private enum GeometryLineKind
        {
            None,
            /// <summary>垂线（模式 21）：两端可伸缩，固定点是垂足。</summary>
            Perpendicular,
            /// <summary>中垂线（模式 26）：两端可伸缩，固定点是原线段的中点。</summary>
            PerpendicularBisector,
            /// <summary>角平分线（模式 22）：只有远端可伸缩，固定点是角的顶点。</summary>
            AngleBisector,
            /// <summary>平行线（模式 17）：两端可伸缩，固定点是所过的那个点（线中点）。</summary>
            Parallel
        }

        /// <summary>
        /// 一条端点可伸缩的几何线条。只保存主线笔画与固定点在主线上的参数位置，
        /// 端点坐标一律从笔画上实时读取，因此整组被移动/旋转/缩放后手柄依然跟随。
        /// </summary>
        private sealed class GeometryLineRecord
        {
            public Stroke MainStroke;
            public GeometryLineKind Kind;
            /// <summary>固定点（垂足/中点/顶点）在主线上的参数：0 = 首点，1 = 末点。</summary>
            public double AnchorT;
        }

        /// <summary>垂线两端各自多画出的长度（与绘制垂线时的 overshoot 保持一致）。</summary>
        private const double PerpendicularEndOvershoot = 16;

        /// <summary>端点到固定点的最小距离，避免拖动时把线条拖没。</summary>
        private const double GeometryLineMinArm = 8;

        private readonly List<GeometryLineRecord> _geometryLines = new List<GeometryLineRecord>();

        private GeometryLineRecord _geometryLineHandleRecord;
        private bool _geometryLineHandleDragging;
        private bool _geometryLineHandleAtStart;

        /// <summary>
        /// 绘制完成后把几何线条登记为「端点可伸缩」。
        /// 约定集合第一条笔画是主线（绘制顺序已保证），其余笔画（直角符号、等角弧）不参与伸缩。
        /// </summary>
        private void RegisterGeometryLine(StrokeCollection strokes, GeometryLineKind kind)
        {
            if (strokes == null || strokes.Count == 0 || kind == GeometryLineKind.None) return;
            if (inkCanvas == null) return;

            var mainStroke = strokes[0];
            if (mainStroke == null || mainStroke.StylusPoints.Count < 2) return;
            if (!inkCanvas.Strokes.Contains(mainStroke)) return;

            var start = mainStroke.StylusPoints[0].ToPoint();
            var end = mainStroke.StylusPoints[mainStroke.StylusPoints.Count - 1].ToPoint();
            var length = (end - start).Length;
            if (length < 0.01) return;

            foreach (var existing in _geometryLines)
            {
                if (ReferenceEquals(existing.MainStroke, mainStroke)) return;
            }

            // 固定点在主线上的参数位置：各类线条的几何在绘制时已经确定
            double anchorT;
            switch (kind)
            {
                case GeometryLineKind.Perpendicular:
                    // 垂线两端各多画 16，垂足就在首点往内 16 处
                    anchorT = Math.Max(0, Math.Min(1, PerpendicularEndOvershoot / length));
                    break;
                case GeometryLineKind.PerpendicularBisector:
                case GeometryLineKind.Parallel:
                    // 中垂线以原线段中点为心、平行线以所过点为心，都在线中点
                    anchorT = 0.5;
                    break;
                default:
                    // 角平分线的固定点是角的顶点，即首点
                    anchorT = 0;
                    break;
            }

            _geometryLines.Add(new GeometryLineRecord
            {
                MainStroke = mainStroke,
                Kind = kind,
                AnchorT = anchorT
            });
            if (_geometryLines.Count > 64)
                _geometryLines.RemoveRange(0, _geometryLines.Count - 64);
        }

        /// <summary>找出「当前选中墨迹中包含主线」的几何线条。</summary>
        private GeometryLineRecord FindSelectedGeometryLine()
        {
            if (inkCanvas == null || _geometryLines.Count == 0) return null;
            var selected = inkCanvas.GetSelectedStrokes();
            if (selected == null || selected.Count == 0) return null;

            for (var i = _geometryLines.Count - 1; i >= 0; i--)
            {
                var record = _geometryLines[i];
                if (record?.MainStroke != null && selected.Contains(record.MainStroke)) return record;
            }

            return null;
        }

        /// <summary>取线条两端在当前画布上的坐标（主线已不在画布上时返回 false）。</summary>
        private bool TryGetGeometryLinePoints(GeometryLineRecord record, out Point start, out Point end)
        {
            start = default;
            end = default;
            if (record?.MainStroke == null || inkCanvas == null) return false;
            if (!inkCanvas.Strokes.Contains(record.MainStroke)) return false;

            var points = record.MainStroke.StylusPoints;
            if (points.Count < 2) return false;

            start = points[0].ToPoint();
            end = points[points.Count - 1].ToPoint();
            return (end - start).Length > 0.01;
        }

        /// <summary>
        /// 选中垂线/中垂线/平行线/角平分线后，在线条端点显示伸缩手柄。
        /// 角平分线的首点是角的顶点，不可拖动，只显示远端手柄。
        /// </summary>
        private void UpdateGeometryLineHandles()
        {
            if (GeometryLineHandleCanvas == null) return;

            // 圆弧端点手柄优先：端点沿同一圆周移动，只改变弧长、不改变半径
            if (TryUpdateArcHandles()) return;

            var record = _geometryLineHandleDragging ? _geometryLineHandleRecord : FindSelectedGeometryLine();
            if (!TryGetGeometryLinePoints(record, out var start, out var end))
            {
                GeometryLineHandleCanvas.Visibility = Visibility.Collapsed;
                if (!_geometryLineHandleDragging) _geometryLineHandleRecord = null;
                return;
            }

            _geometryLineHandleRecord = record;
            if (record.Kind == GeometryLineKind.AngleBisector)
            {
                GeometryLineHandleStart.Visibility = Visibility.Collapsed;
            }
            else
            {
                GeometryLineHandleStart.Visibility = Visibility.Visible;
                PositionGeometryLineHandle(GeometryLineHandleStart, start);
            }

            PositionGeometryLineHandle(GeometryLineHandleEnd, end);
            GeometryLineHandleCanvas.Visibility = Visibility.Visible;
        }

        /// <summary>隐藏几何线条端点手柄。</summary>
        private void HideGeometryLineHandles()
        {
            if (GeometryLineHandleCanvas == null) return;
            GeometryLineHandleCanvas.Visibility = Visibility.Collapsed;
            _arcHandlesActive = false;
            if (!_geometryLineHandleDragging) _geometryLineHandleRecord = null;
            if (!_arcHandleDragging) _arcHandleRecord = null;
        }

        private static void PositionGeometryLineHandle(FrameworkElement handle, Point canvasPoint)
        {
            if (handle == null) return;
            System.Windows.Controls.Canvas.SetLeft(handle, canvasPoint.X - handle.Width / 2);
            System.Windows.Controls.Canvas.SetTop(handle, canvasPoint.Y - handle.Height / 2);
        }

        /// <summary>把主线笔画改成新的两端（只替换触笔点，绘图属性保持不变）。</summary>
        private static void SetGeometryLineEndpoints(Stroke stroke, Point start, Point end)
        {
            if (stroke == null) return;
            stroke.StylusPoints = new StylusPointCollection
            {
                new StylusPoint(start.X, start.Y),
                new StylusPoint(end.X, end.Y)
            };
        }

        private void GeometryLineHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("调整线条长度"))
            {
                e.Handled = true;
                return;
            }

            // 圆弧的两个端点：拖动时只改变弧长，半径保持不变
            if (_arcHandlesActive && TryBeginArcHandleDrag(sender))
            {
                e.Handled = true;
                return;
            }

            var record = _geometryLineHandleRecord ?? FindSelectedGeometryLine();
            if (!TryGetGeometryLinePoints(record, out _, out _)) return;

            _geometryLineHandleRecord = record;
            _geometryLineHandleAtStart = record.Kind != GeometryLineKind.AngleBisector
                                         && ReferenceEquals(sender, GeometryLineHandleStart);
            _geometryLineHandleDragging = true;

            if (sender is UIElement element) element.CaptureMouse();
            e.Handled = true;
        }

        private void GeometryLineHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_arcHandleDragging)
            {
                MoveArcHandle(sender, e);
                return;
            }

            if (!_geometryLineHandleDragging) return;
            if (!(sender is UIElement element) || !element.IsMouseCaptured) return;

            var record = _geometryLineHandleRecord;
            if (!TryGetGeometryLinePoints(record, out var start, out var end)) return;

            var direction = end - start;
            var length = direction.Length;
            if (length < 0.01) return;
            direction /= length;

            // 固定点（垂足/中点/顶点）按参数位置还原，拖动过程中保持不动，只改被拖那一端的长度
            var anchorT = Math.Max(0, Math.Min(1, record.AnchorT));
            var anchor = start + direction * (length * anchorT);
            var position = e.GetPosition(GeometryLineHandleCanvas);

            if (_geometryLineHandleAtStart)
            {
                var arm = Vector.Multiply(anchor - position, direction);
                if (arm < GeometryLineMinArm) arm = GeometryLineMinArm;
                var otherArm = (end - anchor).Length;
                if (arm + otherArm < 0.01) return;

                SetGeometryLineEndpoints(record.MainStroke, anchor - direction * arm, end);
                record.AnchorT = arm / (arm + otherArm);
            }
            else
            {
                var arm = Vector.Multiply(position - anchor, direction);
                if (arm < GeometryLineMinArm) arm = GeometryLineMinArm;
                var otherArm = (anchor - start).Length;
                if (arm + otherArm < 0.01) return;

                SetGeometryLineEndpoints(record.MainStroke, start, anchor + direction * arm);
                record.AnchorT = otherArm / (otherArm + arm);
            }

            // 换过端点后选区边界会变，同步刷选择框与手柄位置
            try
            {
                UpdateSelectionDisplay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            e.Handled = true;
        }

        private void GeometryLineHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_arcHandleDragging)
            {
                FinishArcHandleDrag(sender);
                e.Handled = true;
                return;
            }

            if (!_geometryLineHandleDragging) return;
            FinishGeometryLineHandleDrag(sender);
            e.Handled = true;
        }

        private void GeometryLineHandle_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_arcHandleDragging)
            {
                FinishArcHandleDrag(sender);
                return;
            }

            if (!_geometryLineHandleDragging) return;
            FinishGeometryLineHandleDrag(sender);
        }

        private void FinishGeometryLineHandleDrag(object sender)
        {
            _geometryLineHandleDragging = false;
            if (sender is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();

            // 拖动期间的触笔点变化被 _geometryLineHandleDragging 抑制，
            // 这里一次性提交，保证一次拖动只产生一条撤销记录
            CommitPendingStrokeManipulationHistory();

            UpdateGeometryLineHandles();
            try
            {
                UpdateSelectionDisplay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        #endregion

        #region 画圆弧（模式 27）

        /// <summary>
        /// 用圆弧工具画出的圆弧。圆心/半径/起止角一律从笔画本身反算，
        /// 因此整组被移动/旋转/缩放后，端点手柄依然贴着圆弧两端。
        /// </summary>
        private sealed class ArcStrokeRecord
        {
            public Stroke MainStroke;

            /// <summary>弧的生成方向：true 表示角度递增（屏幕坐标下视觉为顺时针）。</summary>
            public bool Clockwise = true;
        }

        private const double ArcMinSweepDegrees = 5;
        private const double ArcMaxSweepDegrees = 355;
        private const double ArcMinRadius = 4;

        private readonly List<ArcStrokeRecord> _arcStrokes = new List<ArcStrokeRecord>();
        private ArcStrokeRecord _arcHandleRecord;
        /// <summary>本次手势已收尾（触摸抬起后可能补来一帧移动，用它挡住重复生成）。</summary>
        private bool _arcAwaitNewGesture;
        private bool _arcHandlesActive;
        private bool _arcHandleDragging;
        private bool _arcHandleAtStart;

        /// <summary>
        /// 生成圆弧采样点。角度单位为弧度；<paramref name="clockwise"/> 为 true 时角度由 startAngle 递增到 endAngle。
        /// </summary>
        private static List<Point> GenerateArcGeometry(Point center, double radius, double startAngle, double endAngle, bool clockwise)
        {
            var points = new List<Point>();
            if (radius < ArcMinRadius) return points;

            var sweep = clockwise ? endAngle - startAngle : startAngle - endAngle;
            sweep = NormalizeRadians(sweep);
            if (sweep < 0.01) return points;
            if (!clockwise) sweep = -sweep;

            // 每段不超过 3°，保证圆弧足够平滑
            var segments = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 60)));
            for (var i = 0; i <= segments; i++)
            {
                var angle = startAngle + sweep * i / segments;
                points.Add(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
            }

            return points;
        }

        /// <summary>把角度规整到 [0, 2π)。</summary>
        private static double NormalizeRadians(double radians)
        {
            var twoPi = Math.PI * 2;
            radians %= twoPi;
            if (radians < 0) radians += twoPi;
            return radians;
        }

        /// <summary>从圆弧笔画反算圆心、半径与起止角。</summary>
        private static bool TryGetArcGeometry(Stroke stroke, out Point center, out double radius,
            out double startAngle, out double endAngle)
        {
            center = default;
            radius = 0;
            startAngle = 0;
            endAngle = 0;
            if (stroke == null) return false;

            var points = stroke.StylusPoints;
            if (points == null || points.Count < 3) return false;

            var first = points[0].ToPoint();
            var middle = points[points.Count / 2].ToPoint();
            var last = points[points.Count - 1].ToPoint();
            if (!TryGetCircumcenter(first, middle, last, out center)) return false;

            radius = (first - center).Length;
            if (radius < ArcMinRadius) return false;

            startAngle = Math.Atan2(first.Y - center.Y, first.X - center.X);
            endAngle = Math.Atan2(last.Y - center.Y, last.X - center.X);
            return true;
        }

        /// <summary>三点外接圆圆心（三点共线时返回 false）。</summary>
        private static bool TryGetCircumcenter(Point a, Point b, Point c, out Point center)
        {
            center = default;
            var d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
            if (Math.Abs(d) < 1e-6) return false;

            var a2 = a.X * a.X + a.Y * a.Y;
            var b2 = b.X * b.X + b.Y * b.Y;
            var c2 = c.X * c.X + c.Y * c.Y;
            center = new Point(
                (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d,
                (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
            return true;
        }

        /// <summary>绘制完成后登记圆弧笔画，并立刻显示两个端点手柄。</summary>
        private void RegisterArc(StrokeCollection strokes)
        {
            if (strokes == null || strokes.Count == 0 || inkCanvas == null) return;

            var mainStroke = strokes[0];
            if (mainStroke == null) return;
            if (!inkCanvas.Strokes.Contains(mainStroke)) return;
            if (!TryGetArcGeometry(mainStroke, out _, out _, out _, out _)) return;

            foreach (var existing in _arcStrokes)
            {
                if (ReferenceEquals(existing.MainStroke, mainStroke)) return;
            }

            var record = new ArcStrokeRecord { MainStroke = mainStroke };
            _arcStrokes.Add(record);
            if (_arcStrokes.Count > 64)
                _arcStrokes.RemoveRange(0, _arcStrokes.Count - 64);

            // 端点手柄只在圆弧被选中时显示（选中后由 UpdateSelectionDisplay → UpdateGeometryLineHandles 刷新）
        }

        /// <summary>找出「当前选中墨迹中包含圆弧」的记录。</summary>
        private ArcStrokeRecord FindSelectedArc()
        {
            if (inkCanvas == null || _arcStrokes.Count == 0) return null;

            var selected = inkCanvas.GetSelectedStrokes();
            if (selected == null || selected.Count == 0) return null;

            for (var i = _arcStrokes.Count - 1; i >= 0; i--)
            {
                var record = _arcStrokes[i];
                if (record?.MainStroke != null && selected.Contains(record.MainStroke)) return record;
            }

            return null;
        }

        /// <summary>把两个端点手柄摆到圆弧两端；没有可显示的圆弧时返回 false。</summary>
        private bool TryUpdateArcHandles()
        {
            if (GeometryLineHandleCanvas == null) return false;

            // 只有被选中的圆弧（或正在拖动手柄）才显示端点
            var record = _arcHandleDragging ? _arcHandleRecord : FindSelectedArc();

            if (record?.MainStroke == null || inkCanvas == null || !inkCanvas.Strokes.Contains(record.MainStroke) ||
                !TryGetArcGeometry(record.MainStroke, out var center, out var radius, out var startAngle, out var endAngle))
            {
                if (!_arcHandleDragging) _arcHandlesActive = false;

                return false;
            }

            _arcHandleRecord = record;
            _arcHandlesActive = true;

            PositionGeometryLineHandle(GeometryLineHandleStart,
                new Point(center.X + radius * Math.Cos(startAngle), center.Y + radius * Math.Sin(startAngle)));
            PositionGeometryLineHandle(GeometryLineHandleEnd,
                new Point(center.X + radius * Math.Cos(endAngle), center.Y + radius * Math.Sin(endAngle)));
            GeometryLineHandleStart.Visibility = Visibility.Visible;
            GeometryLineHandleEnd.Visibility = Visibility.Visible;
            GeometryLineHandleCanvas.Visibility = Visibility.Visible;
            return true;
        }

        /// <summary>拖动阶段显示的半径提示线（只存在于预览帧，抬笔后移除）。</summary>
        private Stroke _arcRadiusGuideStroke;

        /// <summary>移除半径提示线。</summary>
        private void RemoveArcRadiusGuide()
        {
            var guide = _arcRadiusGuideStroke;
            _arcRadiusGuideStroke = null;
            if (guide == null || inkCanvas == null) return;

            try
            {
                if (inkCanvas.Strokes.Contains(guide)) inkCanvas.Strokes.Remove(guide);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        /// <summary>生成半径提示线：圆心 → 圆周（红色荧光辅助线，属于预览而不进入正式墨迹）。</summary>
        private Stroke CreateArcRadiusGuide(Point center, Point edge)
        {
            if (inkCanvas == null) return null;

            // 与垂线第一步的基准辅助线同款：固定红色荧光笔属性，
            // 不跟随当前荧光笔/激光笔的颜色、宽度与透明度
            var attributes = CreatePerpendicularGuideDrawingAttributes();

            return new Stroke(new StylusPointCollection
            {
                new StylusPoint(center.X, center.Y),
                new StylusPoint((center.X + edge.X) / 2, (center.Y + edge.Y) / 2),
                new StylusPoint(edge.X, edge.Y)
            })
            {
                DrawingAttributes = attributes
            };
        }

        /// <summary>收起圆弧端点手柄（选中态由 UpdateGeometryLineHandles 重新决定）。</summary>
        private void ClearArcHandleHighlight()
        {
            RemoveArcRadiusGuide();
            if (!_arcHandlesActive || _arcHandleDragging) return;
            _arcHandlesActive = false;
            _arcHandleRecord = null;
            HideGeometryLineHandles();
        }

        private bool TryBeginArcHandleDrag(object sender)
        {
            var record = _arcHandleRecord;
            if (record?.MainStroke == null) return false;
            if (!TryGetArcGeometry(record.MainStroke, out _, out _, out _, out _)) return false;

            _arcHandleRecord = record;
            _arcHandleAtStart = ReferenceEquals(sender, GeometryLineHandleStart);
            _arcHandleDragging = true;
            if (sender is UIElement element) element.CaptureMouse();
            return true;
        }

        private void MoveArcHandle(object sender, MouseEventArgs e)
        {
            if (!(sender is UIElement element) || !element.IsMouseCaptured) return;

            var record = _arcHandleRecord;
            if (record?.MainStroke == null) return;
            if (!TryGetArcGeometry(record.MainStroke, out var center, out var radius,
                    out var startAngle, out var endAngle)) return;

            var position = e.GetPosition(GeometryLineHandleCanvas);
            var pointerAngle = Math.Atan2(position.Y - center.Y, position.X - center.X);

            var newStart = _arcHandleAtStart ? pointerAngle : startAngle;
            var newEnd = _arcHandleAtStart ? endAngle : pointerAngle;

            // 方向沿用当前弧，张角限制在 [5°, 355°]，避免拖成一条直线或整圆
            var clockwise = record.Clockwise;
            var sweep = NormalizeRadians(clockwise ? newEnd - newStart : newStart - newEnd);
            var minSweep = ArcMinSweepDegrees * Math.PI / 180;
            var maxSweep = ArcMaxSweepDegrees * Math.PI / 180;
            if (sweep < minSweep) sweep = minSweep;
            if (sweep > maxSweep) sweep = maxSweep;

            var points = GenerateArcGeometry(center, radius, newStart,
                clockwise ? newStart + sweep : newStart - sweep, clockwise);
            if (points.Count < 3) return;

            record.MainStroke.StylusPoints = new StylusPointCollection(points);
            UpdateGeometryLineHandles();

            try
            {
                UpdateSelectionDisplay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            e.Handled = true;
        }

        private void FinishArcHandleDrag(object sender)
        {
            _arcHandleDragging = false;
            if (sender is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();

            // 一次拖动只产生一条撤销记录
            CommitPendingStrokeManipulationHistory();
            UpdateGeometryLineHandles();
            try
            {
                UpdateSelectionDisplay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        #endregion
    }
}
