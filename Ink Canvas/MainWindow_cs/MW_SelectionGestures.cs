using Ink_Canvas.Controls;
using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        #region Floating Control

        /// <summary>
        /// 存储最后一次鼠标按下的边界对象
        /// </summary>
        private object lastBorderMouseDownObject;

        /// <summary>
        /// 暂存待插入白板的克隆墨迹（在弹出目标选择菜单期间使用）
        /// </summary>
        private StrokeCollection _pendingInsertStrokes;

        /// <summary>
        /// 处理边界鼠标按下事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 如果发送者是 BoardRandomDrawToolBtn 或 BoardSingleDrawToolBtn，且它们被隐藏，则不处理事件
        /// 否则存储当前鼠标按下的对象
        /// </remarks>
        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 如果发送者是 BoardRandomDrawToolBtn 或 BoardSingleDrawToolBtn，且它们被隐藏，则不处理事件
            if (sender is FrameworkElement element)
            {
                if ((BoardRandomDrawToolBtn != null && element == BoardRandomDrawToolBtn ||
                     BoardSingleDrawToolBtn != null && element == BoardSingleDrawToolBtn) &&
                    element.Visibility != Visibility.Visible)
                {
                    return;
                }
            }

            lastBorderMouseDownObject = sender;

            // 按下选中栏上其他按钮时收起颜色弹窗（颜色按钮自身由 MouseUp 切换开合）
            if (!ReferenceEquals(sender, BorderStrokeSelectionColor))
            {
                CloseStrokeSelectionColorPopup();
            }
        }

        /// <summary>
        /// 处理墨迹选择克隆鼠标释放事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 只有当鼠标按下和释放的是同一个对象时才处理
        /// 执行墨迹克隆操作并记录日志
        /// </remarks>
        private void BorderStrokeSelectionClone_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("克隆墨迹")) return;
            if (lastBorderMouseDownObject != sender) return;

            // 填充块：克隆元素本身；若同时选中了笔迹，继续走下面的笔迹克隆
            if (HasPaintFillSelection)
            {
                CloneSelectedPaintFill();
                if (inkCanvas.GetSelectedStrokes().Count == 0) return;
            }

            try
            {
                var strokes = inkCanvas.GetSelectedStrokes();
                if (strokes.Count > 0)
                {
                    // 直接执行克隆操作，与图片克隆保持一致
                    CloneStrokes(strokes);
                    LogHelper.WriteLogToFile($"墨迹克隆完成: {strokes.Count} 个墨迹");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"墨迹克隆失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理墨迹选择克隆到新画板鼠标释放事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 只有当鼠标按下和释放的是同一个对象时才处理
        /// 克隆选中的墨迹到新画板并清除当前选择
        /// </remarks>
        private void BorderStrokeSelectionCloneToNewBoard_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("克隆墨迹到新页面")) return;
            if (lastBorderMouseDownObject != sender) return;

            // 填充块：复制一份到新页面（同时选中的笔迹也一起复制到同一页）
            if (HasPaintFillSelection)
            {
                CloneSelectedPaintFillToNewBoard(includeStrokes: true);
                return;
            }

            var strokes = inkCanvas.GetSelectedStrokes();
            inkCanvas.Select(new StrokeCollection());
            CloneStrokesToNewBoard(strokes);
        }

        /// <summary>
        /// 处理墨迹选择插入白板鼠标释放事件
        /// 克隆选中墨迹，弹出目标选择菜单（小白板/常规白板），确认后插入
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void BorderStrokeSelectionInsertToWhiteboard_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("插入墨迹到白板")) return;
            if (lastBorderMouseDownObject != sender) return;

            // 填充块：直接插入常规白板（小白板只接受墨迹），同时选中的笔迹一并插入
            if (HasPaintFillSelection)
            {
                InsertSelectedPaintFillToWhiteboard(includeStrokes: true);
                return;
            }

            var strokes = inkCanvas.GetSelectedStrokes();
            if (strokes.Count == 0) return;

            // 参考ICA模式：先取消选中，再克隆（确保克隆出的墨迹不携带选中态）
            inkCanvas.Select(new StrokeCollection());
            _pendingInsertStrokes = strokes.Clone();

            // 在发送者附近弹出目标选择菜单
            if (sender is FrameworkElement fe)
            {
                InsertToWhiteboardPopup.PlacementTarget = fe;
                InsertToWhiteboardPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            }
            InsertToWhiteboardPopup.IsOpen = true;
        }

        /// <summary>
        /// 将暂存的墨迹插入常规白板（当前页面）
        /// 参考ICA的BorderStrokeSelectionCloneToBoardOrNewPage_Click模式
        /// </summary>
        internal void ExecuteInsertStrokesToRegularWhiteboard()
        {
            if (_pendingInsertStrokes == null || _pendingInsertStrokes.Count == 0) return;

            try
            {
                // 确保主画布上没有选中状态（防止选中态渲染污染克隆墨迹）
                inkCanvas.Select(new StrokeCollection());
                HideSelectionDisplay();
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

                if (currentMode != 1)
                {
                    // 从屏幕批注模式切换到白板模式（内部会 save→clear→restore）
                    ImageBlackboard_MouseUp(null, null);
                }

                // 切换模式后，白板画布已就绪，此时加入克隆的墨迹
                inkCanvas.Strokes.Add(_pendingInsertStrokes);
                timeMachine.CommitStrokeUserInputHistory(_pendingInsertStrokes);

                LogHelper.WriteLogToFile($"墨迹插入常规白板完成: {_pendingInsertStrokes.Count} 个墨迹");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"墨迹插入常规白板失败: {ex.Message}", LogHelper.LogType.Error);
            }
            finally
            {
                _pendingInsertStrokes = null;
            }
        }

        /// <summary>
        /// 将暂存的墨迹插入小白板（浮窗）的当前页面
        /// </summary>
        internal void ExecuteInsertStrokesToMiniWhiteboard()
        {
            if (_pendingInsertStrokes == null || _pendingInsertStrokes.Count == 0) return;

            try
            {
                // 确保主画布上没有选中状态
                inkCanvas.Select(new StrokeCollection());
                HideSelectionDisplay();
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

                if (_miniWhiteboardWindow == null || !_miniWhiteboardWindow.IsLoaded)
                {
                    OpenMiniWhiteboard();
                }
                else if (!_miniWhiteboardWindow.IsVisible)
                {
                    _miniWhiteboardWindow.Show();
                    _miniWhiteboardWindow.Activate();
                }

                _miniWhiteboardWindow.InsertStrokes(_pendingInsertStrokes);

                LogHelper.WriteLogToFile($"墨迹插入小白板完成: {_pendingInsertStrokes.Count} 个墨迹");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"墨迹插入小白板失败: {ex.Message}", LogHelper.LogType.Error);
            }
            finally
            {
                _pendingInsertStrokes = null;
            }
        }

        /// <summary>
        /// 处理墨迹选择删除鼠标释放事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 只有当鼠标按下和释放的是同一个对象时才处理
        /// 调用 SymbolIconDelete_MouseUp 方法执行删除操作
        /// </remarks>
        private void BorderStrokeSelectionDelete_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (lastBorderMouseDownObject != sender) return;

            // 填充块：只删除这个元素，绝不能落到下面的「清空画布」逻辑
            if (HasPaintFillSelection)
            {
                DeleteSelectedPaintFill();
                // 同时选中了笔迹：继续走下面的删除流程，一次删掉两者
                if (inkCanvas.GetSelectedStrokes().Count == 0) return;
            }

            SymbolIconDelete_MouseUp(sender, e);
        }

        /// <summary>
        /// 处理笔宽减小鼠标释放事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 只有当鼠标按下和释放的是同一个对象时才处理
        /// 调用 ChangeStrokeThickness 方法减小笔宽
        /// </remarks>
        private void GridPenWidthDecrease_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("修改墨迹粗细")) return;
            if (lastBorderMouseDownObject != sender) return;
            // 填充块：粗细映射为填充不透明度
            if (HasPaintFillSelection)
            {
                ChangeSelectedPaintFillOpacity(0.8, false);
                if (inkCanvas.GetSelectedStrokes().Count == 0) return;
            }

            ChangeStrokeThickness(0.8);
        }

        /// <summary>
        /// 处理笔宽增大鼠标释放事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 只有当鼠标按下和释放的是同一个对象时才处理
        /// 调用 ChangeStrokeThickness 方法增大笔宽
        /// </remarks>
        private void GridPenWidthIncrease_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("修改墨迹粗细")) return;
            if (lastBorderMouseDownObject != sender) return;
            // 填充块：粗细映射为填充不透明度
            if (HasPaintFillSelection)
            {
                ChangeSelectedPaintFillOpacity(1.25, false);
                if (inkCanvas.GetSelectedStrokes().Count == 0) return;
            }

            ChangeStrokeThickness(1.25);
        }

        /// <summary>
        /// 更改选中墨迹的粗细
        /// </summary>
        /// <param name="multipler">缩放倍数</param>
        /// <remarks>
        /// 对选中的每个墨迹应用缩放倍数
        /// 确保新的粗细在允许的范围内
        /// 如果有 DrawingAttributesHistory，则提交历史记录
        /// </remarks>
        private void ChangeStrokeThickness(double multipler)
        {
            if (TryBlockFrozenPageMutation("修改墨迹粗细")) return;
            foreach (var stroke in inkCanvas.GetSelectedStrokes())
            {
                var newWidth = stroke.DrawingAttributes.Width * multipler;
                var newHeight = stroke.DrawingAttributes.Height * multipler;
                if (!(newWidth >= DrawingAttributes.MinWidth) || !(newWidth <= DrawingAttributes.MaxWidth)
                                                              || !(newHeight >= DrawingAttributes.MinHeight) ||
                                                              !(newHeight <= DrawingAttributes.MaxHeight)) continue;
                stroke.DrawingAttributes.Width = newWidth;
                stroke.DrawingAttributes.Height = newHeight;
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
        }

        /// <summary>
        /// 处理笔宽恢复默认鼠标释放事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 只有当鼠标按下和释放的是同一个对象时才处理
        /// 将选中墨迹的粗细恢复为默认值
        /// </remarks>
        private void GridPenWidthRestore_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("修改墨迹粗细")) return;
            if (lastBorderMouseDownObject != sender) return;

            // 填充块：恢复默认的不透明度
            if (HasPaintFillSelection)
            {
                ChangeSelectedPaintFillOpacity(1.0, true);
                // 恢复默认笔宽：有笔迹时继续走下面的流程
                if (inkCanvas.GetSelectedStrokes().Count == 0) return;
            }

            foreach (var stroke in inkCanvas.GetSelectedStrokes())
            {
                stroke.DrawingAttributes.Width = inkCanvas.DefaultDrawingAttributes.Width;
                stroke.DrawingAttributes.Height = inkCanvas.DefaultDrawingAttributes.Height;
            }
        }

        /// <summary>
        /// 选中墨迹颜色弹窗（复用批注模式快捷调色盘控件）
        /// </summary>
        private Popup _strokeSelectionColorPopup;

        /// <summary>
        /// 处理选中栏"颜色"按钮鼠标释放事件：弹出快捷调色盘
        /// </summary>
        /// <remarks>
        /// 只有当鼠标按下和释放的是同一个对象时才处理
        /// 打开前将选中墨迹的当前颜色同步到调色盘勾选状态
        /// </remarks>
        private void BorderStrokeSelectionColor_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("修改墨迹颜色")) return;
            if (lastBorderMouseDownObject != sender) return;

            if (_strokeSelectionColorPopup == null)
            {
                var palette = new QuickColorPaletteControl();
                palette.ColorClicked += StrokeSelectionColorPalette_ColorClicked;
                var border = new Border
                {
                    Background = (Brush)FindResource("FloatingBarBackgroundBrush"),
                    BorderBrush = (Brush)FindResource("FloatingBarBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 6, 8, 6),
                    Child = palette
                };
                _strokeSelectionColorPopup = new Popup
                {
                    Child = border,
                    Placement = PlacementMode.Top,
                    PlacementTarget = (UIElement)sender,
                    StaysOpen = true,
                    AllowsTransparency = true,
                    PopupAnimation = PopupAnimation.Fade
                };
            }

            var selectedStrokes = inkCanvas.GetSelectedStrokes();
            if (selectedStrokes.Count > 0
                && _strokeSelectionColorPopup.Child is Border popupBorder
                && popupBorder.Child is QuickColorPaletteControl paletteControl)
            {
                paletteControl.ClearAllChecked();
                paletteControl.SetCheckedByColor(selectedStrokes[0].DrawingAttributes.Color);
            }
            else if (GetSelectedPaintFillColor() is Color paintFillColor
                     && _strokeSelectionColorPopup.Child is Border paintFillBorder
                     && paintFillBorder.Child is QuickColorPaletteControl paintFillPalette)
            {
                // 填充块：勾选当前填充色
                paintFillPalette.ClearAllChecked();
                paintFillPalette.SetCheckedByColor(paintFillColor);
            }

            _strokeSelectionColorPopup.IsOpen = !_strokeSelectionColorPopup.IsOpen;
        }

        /// <summary>
        /// 快捷调色盘选色后应用到选中墨迹
        /// </summary>
        /// <remarks>
        /// 仅改选中墨迹的颜色，保留各自原有的透明度（荧光笔仍为半透明）
        /// 不切换当前画笔、不退出选择模式；提交撤销历史
        /// </remarks>
        private void StrokeSelectionColorPalette_ColorClicked(object sender, RoutedEventArgs e)
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

            // 填充块：直接改填充色，保留原有不透明度
            if (HasPaintFillSelection)
            {
                ApplyColorToSelectedPaintFill(color);
                // 同时选中了笔迹：把颜色也应用到笔迹
                if (inkCanvas.GetSelectedStrokes().Count == 0) return;
            }

            var changed = false;
            foreach (var stroke in inkCanvas.GetSelectedStrokes())
            {
                var current = stroke.DrawingAttributes.Color;
                if (current.R == color.R && current.G == color.G && current.B == color.B) continue;
                stroke.DrawingAttributes.Color = Color.FromArgb(current.A, color.R, color.G, color.B);
                changed = true;
            }

            if (changed && DrawingAttributesHistory.Count > 0)
            {
                timeMachine.CommitStrokeDrawingAttributesHistory(DrawingAttributesHistory);
                DrawingAttributesHistory = new Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>>();
                foreach (var item in DrawingAttributesHistoryFlag)
                {
                    item.Value.Clear();
                }
            }

            CloseStrokeSelectionColorPopup();
        }

        /// <summary>
        /// 关闭选中墨迹颜色弹窗（如已打开）
        /// </summary>
        private void CloseStrokeSelectionColorPopup()
        {
            if (_strokeSelectionColorPopup != null && _strokeSelectionColorPopup.IsOpen)
                _strokeSelectionColorPopup.IsOpen = false;
        }

        /// <summary>
        /// 选中对象工具栏的「水平翻转」：进入沿轴翻转状态，随后在画布上拖出一条对称轴，
        /// 松手时按该轴镜像选中的墨迹（原图保留、轴线消失）。
        /// </summary>
        private void ImageFlipHorizontal_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("翻转墨迹")) return;
            if (lastBorderMouseDownObject != sender) return;

            BeginAxisFlip();
        }

        /// <summary>沿轴翻转的提示浮层（复用同一个 Popup，避免每次提示都新建窗口句柄）。</summary>
        private Popup _axisFlipToastPopup;
        private TextBlock _axisFlipToastText;
        private System.Windows.Threading.DispatcherTimer _axisFlipToastTimer;

        /// <summary>沿轴翻转进入拖轴模式时在窗口中央给一次提示。</summary>
        private void ShowAxisFlipToast(string message)
        {
            try
            {
                if (_axisFlipToastPopup == null)
                {
                    _axisFlipToastText = new TextBlock { Foreground = Brushes.White, FontSize = 15 };

                    _axisFlipToastPopup = new Popup
                    {
                        Child = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(235, 24, 24, 27)),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(16, 10, 16, 10),
                            Child = _axisFlipToastText
                        },
                        Placement = PlacementMode.Center,
                        PlacementTarget = this,
                        StaysOpen = true,
                        AllowsTransparency = true,
                        IsHitTestVisible = false
                    };

                    _axisFlipToastTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(1800)
                    };
                    _axisFlipToastTimer.Tick += (s, args) =>
                    {
                        _axisFlipToastTimer.Stop();
                        if (_axisFlipToastPopup != null) _axisFlipToastPopup.IsOpen = false;
                    };
                }

                _axisFlipToastText.Text = message;
                if (!_axisFlipToastPopup.IsOpen) _axisFlipToastPopup.IsOpen = true;

                // 重复提示时重新计时，避免旧计时器提前把新提示关掉
                _axisFlipToastTimer.Stop();
                _axisFlipToastTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        #region 沿轴翻转（选中对象工具栏）

        /// <summary>沿轴翻转占用的绘制模式号（不占用图形面板的 1~27）。</summary>
        private const int AxisFlipShapeMode = 28;

        /// <summary>轴线太短（单击或手抖）视为放弃，不生成镜像副本。</summary>
        private const double AxisFlipMinLength = 10;

        /// <summary>等待画对称轴的源墨迹。原图保留，最终只新增一份镜像副本。</summary>
        private List<Stroke> _axisFlipSourceStrokes;

        /// <summary>等待沿轴镜像的填充块（可与 _axisFlipSourceStrokes 同时存在）。</summary>
        private List<FrameworkElement> _axisFlipSourceElements;

        /// <summary>拖动阶段显示的对称轴（荧光笔辅助线，只属于预览，结束时移除）。</summary>
        private Stroke _axisFlipGuideStroke;

        /// <summary>轴线终点（取最后一次拖动位置，触摸路径下不能用鼠标位置代替）。</summary>
        private Point _axisFlipAxisEnd;
        private bool _axisFlipAxisEndValid;

        /// <summary>进入沿轴翻转状态：等待用户拖出一条对称轴。笔迹与填充块一起处理。</summary>
        private void BeginAxisFlip()
        {
            if (inkCanvas == null) return;

            // 填充块：记录元素本身，拖出轴线后逐块镜像
            _axisFlipSourceElements = HasPaintFillSelection
                ? GetSelectedPaintFills().ToList()
                : null;

            _axisFlipSourceStrokes = new List<Stroke>();
            var selected = inkCanvas.GetSelectedStrokes();
            if (selected != null)
            {
                foreach (var stroke in selected)
                {
                    if (stroke != null) _axisFlipSourceStrokes.Add(stroke);
                }
            }
            if (_axisFlipSourceStrokes.Count == 0) _axisFlipSourceStrokes = null;

            // 两个来源都为空：没有可镜像的内容
            if (_axisFlipSourceStrokes == null && (_axisFlipSourceElements == null || _axisFlipSourceElements.Count == 0))
            {
                _axisFlipSourceElements = null;
                return;
            }

            RemoveAxisFlipGuide();
            _axisFlipAxisEndValid = false;

            forceEraser = false;
            drawingShapeMode = AxisFlipShapeMode;
            drawMultiStepShapeCurrentStep = 0;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;

            // 不能直接调用 CancelSingleFingerDragMode()：它会把 GridInkCanvasSelectionCover 收起，
            // 而选中对象工具栏（BorderStrokeSelectionControl）的可见性绑在这个覆盖层上，
            // 一收起工具窗口就跟着消失了。这里只做单指拖拽相关的收尾，
            // 覆盖层保持显示（工具栏常驻），但翻转期间设为不参与命中测试，
            // 保证拖对称轴的手势仍然落在 inkCanvas 上。
            if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
            if (isSingleFingerDragMode) ToggleFingerDragMode(null, null);
            isLongPressSelected = true;

            SetAxisFlipSelectionChromePinned(true);
            ShowAxisFlipToast("请按下鼠标绘制对称轴");
        }

        /// <summary>
        /// 拖轴阶段画布按下的接管入口（由 Window_PreviewMouseDown 调用）。
        /// InkCanvas 在「选中元素 + 拖轴模式」下会把 MouseDown 吃掉，inkCanvas_MouseDown 不会执行，
        /// 于是 iniP 仍是上一次落笔的位置、isMouseDown 仍为 false：轴线画不出来，松手时按旧起点镜像。
        /// 这里用按下位置重置起点并置位 isMouseDown，后续 MouseMove/MouseUp 走原有链接即可正常工作。
        /// </summary>
        private void BeginAxisFlipDragAt(Point pressPoint)
        {
            if (drawingShapeMode != AxisFlipShapeMode) return;

            RemoveAxisFlipGuide();
            _axisFlipAxisEnd = pressPoint;
            _axisFlipAxisEndValid = false;
            iniP = pressPoint;
            isMouseDown = true;

            // 捕获鼠标：在窗口外抬起（或 Alt+Tab 切走）时也能收到抬起 / 丢失捕获，
            // 避免 isMouseDown 卡在 true，让之后不按键的鼠标移动被当成拖轴持续重绘。
            if (inkCanvas != null && !inkCanvas.IsMouseCaptured) inkCanvas.CaptureMouse();
        }

        /// <summary>
        /// 沿轴翻转期间固定/恢复「选中对象工具栏」。
        /// 工具栏可见性绑定在 GridInkCanvasSelectionCover 上，所以翻转时让覆盖层保持可见，
        /// 仅关闭其命中测试（覆盖层是铺满画布的透明层，可命中会拦截拖轴手势）。
        /// </summary>
        private void SetAxisFlipSelectionChromePinned(bool pinned)
        {
            if (GridInkCanvasSelectionCover == null) return;

            if (pinned)
            {
                GridInkCanvasSelectionCover.IsHitTestVisible = false;
                // 元素选择框（图片/填充块）同样会拦截鼠标：不关掉它，从元素上方开始拖轴
                // 就会落在 MoveSurface 上，结果既没有轴线预览，抬手时又用旧起点生成了副本
                if (ImageSelectionOverlay != null) ImageSelectionOverlay.IsHitTestVisible = false;
                // 显示选择框与工具栏，但保持原位不重新定位（用户可能刚拖动过工具栏）
                RefreshStrokeSelectionChrome();
            }
            else
            {
                GridInkCanvasSelectionCover.IsHitTestVisible = true;
                if (ImageSelectionOverlay != null) ImageSelectionOverlay.IsHitTestVisible = true;
            }
        }

        /// <summary>移除拖动阶段的对称轴。</summary>
        private void RemoveAxisFlipGuide()
        {
            var guide = _axisFlipGuideStroke;
            _axisFlipGuideStroke = null;
            if (guide == null || inkCanvas == null) return;

            try
            {
                if (inkCanvas.Strokes.Contains(guide)) inkCanvas.Strokes.Remove(guide);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// 按轴线的无限延长线镜像选中的内容（笔迹与填充块一起）：原图不动，另加一份镜像副本；
        /// 轴线只用于预览，结束后移除；随后回到选择模式并保持原选中。
        /// </summary>
        private void CompleteAxisFlip(Point axisEnd)
        {
            var source = _axisFlipSourceStrokes;
            var sourceElements = _axisFlipSourceElements;
            _axisFlipSourceStrokes = null;
            _axisFlipSourceElements = null;

            RemoveAxisFlipGuide();

            var axis = axisEnd - iniP;
            if (axis.Length >= AxisFlipMinLength)
            {
                axis.Normalize();

                // 填充块：逐块生成镜像副本
                if (sourceElements != null)
                {
                    foreach (var element in sourceElements)
                    {
                        if (element is System.Windows.Shapes.Path fill && IsPaintBucketFillElement(fill))
                            MirrorPaintFillElement(fill, iniP, axis);
                    }
                }

                // 笔迹：克隆后整体做反射变换
                if (source != null && source.Count > 0)
                {
                    var matrix = CreateAxisReflectionMatrix(iniP, axis);

                    var mirrored = new StrokeCollection();
                    foreach (var stroke in source)
                    {
                        if (stroke == null) continue;
                        var clone = stroke.Clone();
                        clone.Transform(matrix, false);
                        mirrored.Add(clone);
                    }

                    if (mirrored.Count > 0 && inkCanvas != null)
                    {
                        inkCanvas.Strokes.Add(mirrored);
                        timeMachine.CommitStrokeUserInputHistory(mirrored);
                    }
                }
            }

            ExitAxisFlipMode(source);
        }

        /// <summary>放弃沿轴翻转：移除轴线并回到选择模式（原有选中保持不变）。</summary>
        private void CancelAxisFlip()
        {
            _axisFlipSourceStrokes = null;
            _axisFlipSourceElements = null;
            RemoveAxisFlipGuide();
            ExitAxisFlipMode(null);
        }

        /// <summary>退出沿轴翻转状态：回到选择模式，并尽量恢复原来的选中。</summary>
        private void ExitAxisFlipMode(List<Stroke> source)
        {
            if (drawingShapeMode != AxisFlipShapeMode) return;

            drawingShapeMode = 0;
            isLongPressSelected = false;
            // 恢复覆盖层的命中测试（翻转期间为让工具窗口常驻而被临时关闭）
            SetAxisFlipSelectionChromePinned(false);
            if (inkCanvas == null) return;

            inkCanvas.EditingMode = InkCanvasEditingMode.Select;

            if (source == null || source.Count == 0) return;

            var selection = new StrokeCollection();
            foreach (var stroke in source)
            {
                if (stroke != null && inkCanvas.Strokes.Contains(stroke)) selection.Add(stroke);
            }

            if (selection.Count == 0)
            {
                // 源墨迹已被删除：没有选中内容，收起覆盖层与工具窗口，避免留下空工具栏
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                HideSelectionDisplay();
                return;
            }

            try
            {
                inkCanvas.Select(selection);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>构造「关于过 pointOnAxis、方向 unitDirection 的直线」的反射矩阵。</summary>
        private static Matrix CreateAxisReflectionMatrix(Point pointOnAxis, Vector unitDirection)
        {
            var ux = unitDirection.X;
            var uy = unitDirection.Y;
            var m11 = ux * ux - uy * uy;
            var m12 = 2 * ux * uy;
            var m22 = uy * uy - ux * ux;

            return new Matrix(
                m11, m12, m12, m22,
                pointOnAxis.X - (m11 * pointOnAxis.X + m12 * pointOnAxis.Y),
                pointOnAxis.Y - (m12 * pointOnAxis.X + m22 * pointOnAxis.Y));
        }

        #endregion

        // ... existing code ...
        /// <summary>
        /// 处理顺时针旋转45度鼠标释放事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 只有当鼠标按下和释放的是同一个对象时才处理
        /// 对选中的墨迹应用45度旋转变换
        /// 如果有 DrawingAttributesHistory，则提交历史记录
        /// </remarks>
        private void ImageRotate45_MouseUp(object sender, MouseButtonEventArgs e)
        {
            TryRotateSelectedStrokes(sender, e, 45);
        }

        /// <summary>
        /// 处理顺时针旋转30度鼠标释放事件
        /// </summary>
        private void ImageRotate30_MouseUp(object sender, MouseButtonEventArgs e)
        {
            TryRotateSelectedStrokes(sender, e, 30);
        }

        /// <summary>
        /// 处理顺时针旋转60度鼠标释放事件
        /// </summary>
        private void ImageRotate60_MouseUp(object sender, MouseButtonEventArgs e)
        {
            TryRotateSelectedStrokes(sender, e, 60);
        }

        /// <summary>
        /// 旋转按钮统一入口：只有按下与抬起为同一按钮时才旋转。
        /// </summary>
        private void TryRotateSelectedStrokes(object sender, MouseButtonEventArgs e, double angleDegrees)
        {
            if (TryBlockFrozenPageMutation("旋转墨迹")) return;
            if (lastBorderMouseDownObject != sender) return;

            if (!HasPaintFillSelection && inkCanvas.GetSelectedStrokes().Count == 0) return;

            // 旋转中心只算一次：填充块与笔迹必须绕同一个点转，否则填充块只会原地自转
            var pivot = GetSelectionRotationCenter();

            if (HasPaintFillSelection) RotateSelectedPaintFill(angleDegrees, pivot);

            // 同时选中了笔迹：一起旋转，保持相对布局
            if (inkCanvas.GetSelectedStrokes().Count > 0) RotateSelectedStrokesBy(angleDegrees, pivot);
        }

        /// <summary>
        /// 当前选区的旋转中心：有自定义旋转中心时用它，否则用「笔迹选区 ∪ 填充块包围盒」的中心。
        /// 笔迹与填充块共用同一个中心，旋转后相对位置才不会错。
        /// </summary>
        private Point GetSelectionRotationCenter()
        {
            if (ImageSelectionOverlay != null && ImageSelectionOverlay.HasCustomPivot)
                return ImageSelectionOverlay.PivotCanvasPoint;

            var bounds = inkCanvas.GetSelectionBounds();
            if (HasPaintFillSelection)
            {
                var fillBounds = GetPaintFillSelectionBounds();
                if (bounds.IsEmpty) bounds = fillBounds;
                else if (!fillBounds.IsEmpty) bounds = Rect.Union(bounds, fillBounds);
            }

            if (bounds.IsEmpty) return new Point(0, 0);
            return new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        }

        /// <summary>
        /// 顺时针旋转选中的墨迹。旋转中心优先使用用户拖动设置的旋转中心
        /// （ImageSelectionOverlay 的枢轴点），未设置时使用选区中心。
        /// </summary>
        private void RotateSelectedStrokesBy(double angleDegrees, Point? pivotCanvas = null)
        {
            var targetStrokes = inkCanvas.GetSelectedStrokes();
            if (targetStrokes == null || targetStrokes.Count == 0) return;

            var center = pivotCanvas ?? GetSelectionRotationCenter();

            var m = new Matrix();
            m.RotateAt(angleDegrees, center.X, center.Y);

            foreach (var stroke in targetStrokes) stroke.Transform(m, false);

            // 墨迹已转动：选区框/句柄按新的选区边界重建（自定义旋转中心保持不变）
            _isStrokeRotationOverlayActive = false;
            _strokeRotationAngle = 0;
            UpdateSelectionDisplay();

            if (DrawingAttributesHistory.Count > 0)
            {
                timeMachine.CommitStrokeDrawingAttributesHistory(DrawingAttributesHistory);
                DrawingAttributesHistory = new Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>>();
                foreach (var item in DrawingAttributesHistoryFlag)
                {
                    item.Value.Clear();
                }
            }
        }

        /// <summary>
        /// 处理顺时针旋转90度鼠标释放事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 只有当鼠标按下和释放的是同一个对象时才处理
        /// 对选中的墨迹应用90度旋转变换
        /// 如果有 DrawingAttributesHistory，则提交历史记录
        /// </remarks>
        private void ImageRotate90_MouseUp(object sender, MouseButtonEventArgs e)
        {
            TryRotateSelectedStrokes(sender, e, 90);
        }

        #endregion

        /// <summary>
        /// 墨迹选择覆盖层鼠标按下状态
        /// </summary>
        private bool isGridInkCanvasSelectionCoverMouseDown;
        /// <summary>
        /// 墨迹拖动状态
        /// </summary>
        private bool isStrokeDragging = false;
        /// <summary>
        /// 墨迹拖动起始点
        /// </summary>
        private Point strokeDragStartPoint;
        /// <summary>
        /// 墨迹选择克隆集合
        /// </summary>
        private StrokeCollection StrokesSelectionClone = new StrokeCollection();

        // 选择框和选择点相关变量
        /// <summary>
        /// 调整大小状态
        /// </summary>
        private bool isResizing = false;
        /// <summary>
        /// 当前调整把手
        /// </summary>
        private string currentResizeHandle = "";
        /// <summary>
        /// 调整起始点
        /// </summary>
        private Point resizeStartPoint;
        /// <summary>
        /// 原始选择边界
        /// </summary>
        private Rect originalSelectionBounds;

        /// <summary>
        /// 处理墨迹选择覆盖层鼠标按下事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 如果有选中的墨迹，检查点击位置是否在选择框边界内
        /// 如果在边界内，开始拖动墨迹
        /// 如果在边界外，取消选择
        /// </remarks>
        private void GridInkCanvasSelectionCover_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // The lasso cover is above InkCanvas. Give editable SVG scenes priority so
            // their own selection frame handles the click instead of stroke selection.
            var scenePoint = e.GetPosition(inkCanvas);
            var sceneElement = FindSecAgentSceneElementAtCanvasPoint(scenePoint);
            if (sceneElement != null)
            {
                isGridInkCanvasSelectionCoverMouseDown = false;
                isStrokeDragging = false;
                GridInkCanvasSelectionCover.ReleaseMouseCapture();
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                Element_MouseLeftButtonDown(sceneElement, e);
                e.Handled = true;
                return;
            }

            if (TryBlockFrozenPageMutation("移动墨迹"))
            {
                e.Handled = true;
                return;
            }

            // 填充块同样需要在覆盖层上做命中测试：命中则交给元素拖动流程；
            // 未命中且当前选中的正是填充块时取消选中，否则铺满画布的覆盖层会吞掉所有点击
            var paintFillElement = FindPaintBucketFillAtCanvasPoint(scenePoint);
            if (paintFillElement != null)
            {
                isGridInkCanvasSelectionCoverMouseDown = false;
                isStrokeDragging = false;
                GridInkCanvasSelectionCover.ReleaseMouseCapture();
                Element_MouseLeftButtonDown(paintFillElement, e);
                e.Handled = true;
                return;
            }

            // 已有选区（笔迹 / 填充块 / 两者同时）：在合并包围盒内按下就开始拖动，
            // 点在外面则整体取消选中。这样套索一起选中的填充块能跟着笔迹一起移动。
            var selectedStrokeCount = inkCanvas.GetSelectedStrokes().Count;
            if (selectedStrokeCount > 0 || HasPaintFillSelection)
            {
                var clickPoint = e.GetPosition(inkCanvas);
                var selectionBounds = inkCanvas.GetSelectionBounds();

                if (HasPaintFillSelection)
                {
                    var fillBounds = GetPaintFillSelectionBounds();
                    if (selectionBounds.IsEmpty) selectionBounds = fillBounds;
                    else if (!fillBounds.IsEmpty) selectionBounds.Union(fillBounds);
                }

                // 检查点击位置是否在（笔迹 + 填充块的）合并选区边界内
                if (!selectionBounds.IsEmpty
                    && clickPoint.X >= selectionBounds.Left
                    && clickPoint.X <= selectionBounds.Right
                    && clickPoint.Y >= selectionBounds.Top
                    && clickPoint.Y <= selectionBounds.Bottom)
                {
                    // 只有在选区边界内才允许拖动
                    isGridInkCanvasSelectionCoverMouseDown = true;
                    isStrokeDragging = true;
                    strokeDragStartPoint = clickPoint;
                    GridInkCanvasSelectionCover.CaptureMouse();
                    GridInkCanvasSelectionCover.Cursor = Cursors.SizeAll;
                    return;
                }

                // 点击在选择框外，取消全部选择
                var previousSelectedFill = (currentSelectedElement != null && IsPaintBucketFillElement(currentSelectedElement))
                    ? currentSelectedElement
                    : null;
                currentSelectedElement = null;
                ClearPaintFillSelection();
                if (previousSelectedFill != null) UnselectElement(previousSelectedFill);
                inkCanvas.Select(new StrokeCollection());
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                return;
            }

            isGridInkCanvasSelectionCoverMouseDown = true;
        }

        /// <summary>
        /// 处理墨迹选择覆盖层鼠标移动事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标事件参数</param>
        /// <remarks>
        /// 如果正在拖动墨迹，执行拖动操作
        /// 如果鼠标在选中区域移动，更新墨迹选中栏位置
        /// </remarks>
        private FrameworkElement FindSecAgentSceneElementAtCanvasPoint(Point canvasPoint)
        {
            if (inkCanvas == null) return null;

            foreach (var child in inkCanvas.Children.OfType<FrameworkElement>().Reverse())
            {
                if (!IsSecAgentEditableSceneElement(child) && !IsSecAgentEditableSceneGroup(child))
                    continue;

                var bounds = GetSceneElementBounds(child);
                if (bounds.Contains(canvasPoint))
                {
                    return child;
                }
            }

            return null;
        }

        private void GridInkCanvasSelectionCover_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isGridInkCanvasSelectionCoverMouseDown) return;

            // 如果正在拖动墨迹，执行拖动操作
            if (isStrokeDragging && GridInkCanvasSelectionCover.IsMouseCaptured)
            {
                var currentPoint = e.GetPosition(inkCanvas);
                var delta = currentPoint - strokeDragStartPoint;

                // 创建变换矩阵
                var matrix = new Matrix();
                matrix.Translate(delta.X, delta.Y);

                // 对选中的墨迹应用变换
                var selectedStrokes = inkCanvas.GetSelectedStrokes();
                foreach (var stroke in selectedStrokes)
                {
                    stroke.Transform(matrix, false);
                }

                // 一起被套索选中的填充块同步平移，保持与笔迹的相对位置
                foreach (var fill in GetSelectedPaintFills())
                {
                    MovePaintFillByDelta(fill, delta);
                }

                // 更新选择框和旋转锚点位置（选中栏保持原位，除非拖动拖动柄）
                UpdateSelectionDisplay();

                // 更新起始点
                strokeDragStartPoint = currentPoint;
            }
        }

        /// <summary>
        /// 处理墨迹选择覆盖层鼠标释放事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 结束墨迹拖动
        /// 只有在没有选中墨迹时才隐藏选中栏
        /// </remarks>
        private void GridInkCanvasSelectionCover_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!isGridInkCanvasSelectionCoverMouseDown) return;

            // 结束墨迹拖动
            if (isStrokeDragging)
            {
                isStrokeDragging = false;
                GridInkCanvasSelectionCover.ReleaseMouseCapture();
                GridInkCanvasSelectionCover.Cursor = Cursors.Arrow;
            }

            isGridInkCanvasSelectionCoverMouseDown = false;

            // 只有在既没有墨迹、也没有填充块被选中时才隐藏选中栏
            if (inkCanvas.GetSelectedStrokes().Count == 0 && !HasPaintFillSelection)
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                HideSelectionDisplay();
            }
            else
            {
                CommitPendingStrokeManipulationHistory();
                if (inkCanvas.GetSelectedStrokes().Count > 0) RefreshStrokeSelectionChrome();
                if (HasPaintFillSelection) RefreshPaintFillSelectionChrome();
            }
        }

        /// <summary>
        /// 处理选择按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 如果当前是选择模式，检查是否全选
        /// 如果全选，则切换到墨迹模式再切换回选择模式
        /// 如果不是全选，则选择所有有效墨迹
        /// 如果当前不是选择模式，则切换到选择模式
        /// </remarks>
        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            forceEraser = true;
            drawingShapeMode = 0;
            // 视频展台特殊模式下必须保持 IsManipulationEnabled=true，
            // 否则 WPF 不会把触摸提升为 Manipulation 事件，VideoPresenterSpecialMode_ManipulationDelta
            // 收不到拖动/缩放事件，用户切到"选择"后无法拖动摄像头画面（Q7 根因之二）。
            // 普通模式下仍然设为 false（保持原有行为：选择模式下不让 InkCanvas 触发 Manipulation）。
            if (!_isVideoPresenterSpecialMode)
            {
                inkCanvas.IsManipulationEnabled = false;
            }
            if (inkCanvas.EditingMode == InkCanvasEditingMode.Select)
            {
                if (inkCanvas.GetSelectedStrokes().Count == inkCanvas.Strokes.Count)
                {
                    // 使用集中化的工具模式切换方法
                    SetCurrentToolMode(InkCanvasEditingMode.Ink,
                        () => { SetCurrentToolMode(InkCanvasEditingMode.Select); });
                }
                else
                {
                    var selectedStrokes = new StrokeCollection();
                    foreach (var stroke in inkCanvas.Strokes)
                        if (stroke.GetBounds().Width > 0 && stroke.GetBounds().Height > 0)
                            selectedStrokes.Add(stroke);
                    inkCanvas.Select(selectedStrokes);
                }
            }
            else
            {
                // 使用集中化的工具模式切换方法
                SetCurrentToolMode(InkCanvasEditingMode.Select);
            }
        }

        /// <summary>
        /// 墨迹选择控件宽度
        /// </summary>
        private double BorderStrokeSelectionControlWidth = 580.0;
        /// <summary>
        /// 墨迹选择控件高度
        /// </summary>
        private double BorderStrokeSelectionControlHeight = 80.0;
        /// <summary>
        /// 程序更改墨迹选择状态
        /// </summary>
        private bool isProgramChangeStrokeSelection;

        /// <summary>
        /// 处理墨迹画布选择更改事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// 优先检查墨迹选择状态
        /// 如果有墨迹被选中，显示墨迹选择栏和选择框
        /// 如果有图片元素被选中，不显示选择框
        /// 如果没有选中任何内容，隐藏选择框
        /// </remarks>
        private void inkCanvas_SelectionChanged(object sender, EventArgs e)
        {
            if (isProgramChangeStrokeSelection) return;

            // 优先检查墨迹选择状态
            if (inkCanvas.GetSelectedStrokes().Count > 0)
            {
                // 从填充块切回墨迹选择：恢复工具栏按钮可用性
                SetStrokeSelectionBarPaintFillMode(false);

                // 有墨迹被选中，清除图片选择状态
                if (currentSelectedElement != null)
                {
                    currentSelectedElement = null;
                    // 隐藏图片选择工具栏
                    if (BorderImageSelectionControl != null)
                    {
                        BorderImageSelectionControl.Visibility = Visibility.Collapsed;
                    }
                }

                // 显示墨迹选择栏和选择框
                GridInkCanvasSelectionCover.Visibility = Visibility.Visible;
                BorderStrokeSelectionClone.Background = Brushes.Transparent;
                updateBorderStrokeSelectionControlLocation();
                // 新的墨迹选区：旋转中心复位到选区中心
                ImageSelectionOverlay?.ResetPivot();
                UpdateSelectionDisplay();

                // 套索/框选：把与笔迹选区相交的填充块也纳入选中，实现「一起被选中」
                SyncPaintFillSelectionFromStrokeSelection();
                return;
            }

            // 检查是否有图片元素被选中（通过InkCanvas的选中元素）
            var selectedElements = inkCanvas.GetSelectedElements();
            var selectedImageElement = selectedElements
                .FirstOrDefault(element => element is Image || element is PdfEmbeddedView || IsPaintBucketFillElement(element)) as FrameworkElement;

            // InkCanvas 首次点击可能直接接管图片选择，这里把选中结果立即交给新版图片选中框，
            // 避免先显示旧版选择锚点，等第二次点击/拖拽时才进入自定义流程。
            if (selectedImageElement != null)
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                HideSelectionDisplay();

                isProgramChangeStrokeSelection = true;
                try
                {
                    if (currentSelectedElement != null && !ReferenceEquals(currentSelectedElement, selectedImageElement))
                    {
                        var previousEditingMode = inkCanvas.EditingMode;
                        UnselectElement(currentSelectedElement);
                        inkCanvas.EditingMode = previousEditingMode;
                    }
                    SelectElement(selectedImageElement);
                }
                finally
                {
                    isProgramChangeStrokeSelection = false;
                }
                return;
            }

            // 检查是否有图片元素被选中（通过currentSelectedElement）
            if (currentSelectedElement != null && IsBitmapLikeCanvasElement(currentSelectedElement))
            {
                // 填充块用的是墨迹那套工具栏：选中过程中 InkCanvas 清空墨迹选择会触发本事件，
                // 这里必须保持工具栏显示，不能折叠覆盖层
                if (IsPaintBucketFillElement(currentSelectedElement))
                {
                    return;
                }

                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                HideSelectionDisplay();
                return;
            }

            // 没有选中任何内容，隐藏选择框
            GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
            HideSelectionDisplay();
            // 笔迹选区清空后，填充块的随动选中也要一起清掉
            ClearPaintFillSelection();
        }



        /// <summary>
        /// 更新墨迹选中栏位置
        /// </summary>
        /// <remarks>
        /// 计算墨迹选中栏的位置，确保在墨迹下方显示
        /// 如果选中栏会超出屏幕底部，则显示在墨迹上方
        /// 如果上方也没有空间，则显示在顶部
        /// </remarks>
        private void updateBorderStrokeSelectionControlLocation()
        {
            var borderLeft = (inkCanvas.GetSelectionBounds().Left + inkCanvas.GetSelectionBounds().Right -
                              BorderStrokeSelectionControlWidth) / 2;
            var borderTop = inkCanvas.GetSelectionBounds().Bottom + 10; // 在墨迹下方10像素处显示
            if (borderLeft < 0) borderLeft = 0;
            if (borderTop < 0) borderTop = 0;
            if (Width - borderLeft < BorderStrokeSelectionControlWidth || double.IsNaN(borderLeft))
                borderLeft = Width - BorderStrokeSelectionControlWidth;
            if (Height - borderTop < BorderStrokeSelectionControlHeight || double.IsNaN(borderTop))
                borderTop = Height - BorderStrokeSelectionControlHeight;

            // 确保墨迹选中栏始终显示在墨迹下方
            // 如果选中栏会超出屏幕底部，则显示在墨迹上方
            if (borderTop + BorderStrokeSelectionControlHeight > Height)
            {
                borderTop = inkCanvas.GetSelectionBounds().Top - BorderStrokeSelectionControlHeight - 10;
                if (borderTop < 0) borderTop = 10; // 如果上方也没有空间，则显示在顶部
            }

            // 记录自动定位结果，叠加用户拖动产生的偏移后仍约束在窗口范围内
            _strokeSelectionBarAutoPosition = new Point(borderLeft, borderTop);
            var finalLeft = ClampSelectionBarLeft(borderLeft + _strokeSelectionBarUserOffset.X);
            var finalTop = ClampSelectionBarTop(borderTop + _strokeSelectionBarUserOffset.Y);
            BorderStrokeSelectionControl.Margin = new Thickness(finalLeft, finalTop, 0, 0);
        }

        /// <summary>
        /// 墨迹选中栏自动定位结果（叠加用户偏移前）
        /// </summary>
        private Point _strokeSelectionBarAutoPosition;

        /// <summary>
        /// 用户拖动选中栏产生的偏移量，叠加在自动定位之上（本次运行期间一直生效）
        /// </summary>
        private Vector _strokeSelectionBarUserOffset;

        /// <summary>
        /// 拖动起点与拖动开始时的选中栏位置
        /// </summary>
        private Point _strokeSelectionBarDragStartPoint;
        private Thickness _strokeSelectionBarDragStartMargin;

        /// <summary>
        /// 将选中栏横坐标约束在窗口范围内
        /// </summary>
        private double ClampSelectionBarLeft(double left)
        {
            if (left < 0) return 0;
            if (Width - left < BorderStrokeSelectionControlWidth) return Width - BorderStrokeSelectionControlWidth;
            return left;
        }

        /// <summary>
        /// 将选中栏纵坐标约束在窗口范围内
        /// </summary>
        private double ClampSelectionBarTop(double top)
        {
            if (top < 0) return 0;
            if (Height - top < BorderStrokeSelectionControlHeight) return Height - BorderStrokeSelectionControlHeight;
            return top;
        }

        /// <summary>
        /// 处理选中栏左侧拖动柄按下事件：记录拖动起点并捕获鼠标
        /// </summary>
        private void BorderStrokeSelectionDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement handle) return;
            _strokeSelectionBarDragStartPoint = e.GetPosition(this);
            _strokeSelectionBarDragStartMargin = BorderStrokeSelectionControl.Margin;
            handle.CaptureMouse();
            e.Handled = true;
        }

        /// <summary>
        /// 处理选中栏拖动事件：按位移更新选中栏位置并记录偏移量
        /// </summary>
        private void BorderStrokeSelectionDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement handle) return;
            if (!handle.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;

            var position = e.GetPosition(this);
            var left = _strokeSelectionBarDragStartMargin.Left + (position.X - _strokeSelectionBarDragStartPoint.X);
            var top = _strokeSelectionBarDragStartMargin.Top + (position.Y - _strokeSelectionBarDragStartPoint.Y);
            left = ClampSelectionBarLeft(left);
            top = ClampSelectionBarTop(top);
            BorderStrokeSelectionControl.Margin = new Thickness(left, top, 0, 0);

            // 记录相对自动定位的偏移，后续自动重定位（换选区、移动墨迹等）时保持用户摆放的位置
            _strokeSelectionBarUserOffset = new Vector(
                left - _strokeSelectionBarAutoPosition.X,
                top - _strokeSelectionBarAutoPosition.Y);
            e.Handled = true;
        }

        /// <summary>
        /// 处理选中栏拖动柄释放事件：结束拖动
        /// </summary>
        private void BorderStrokeSelectionDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement handle && handle.IsMouseCaptured)
            {
                handle.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 处理墨迹选择覆盖层操作开始事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">操作开始事件参数</param>
        /// <remarks>
        /// 设置操作模式为所有模式
        /// </remarks>
        private void GridInkCanvasSelectionCover_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.Mode = ManipulationModes.All;
        }

        /// <summary>
        /// 处理墨迹选择覆盖层操作完成事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">操作完成事件参数</param>
        /// <remarks>
        /// 如果有 StrokeManipulationHistory，则提交历史记录
        /// 如果有 DrawingAttributesHistory，则提交历史记录
        /// </remarks>
        private void GridInkCanvasSelectionCover_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            CommitPendingStrokeManipulationHistory();

            if (DrawingAttributesHistory.Count > 0)
            {
                timeMachine.CommitStrokeDrawingAttributesHistory(DrawingAttributesHistory);
                DrawingAttributesHistory = new Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>>();
                foreach (var item in DrawingAttributesHistoryFlag)
                {
                    item.Value.Clear();
                }
            }

            RefreshStrokeSelectionChrome();
        }

        /// <summary>
        /// 处理选择覆盖层的操作增量事件，将来自触摸或操作的平移、缩放和旋转应用到当前选中的墨迹上并更新选择控件位置。
        /// </summary>
        /// <param name="sender">触发事件的元素（通常为选择覆盖层）。</param>
        /// <param name="e">包含平移、缩放和旋转增量的 ManipulationDeltaEventArgs。</param>
        /// <remarks>
        /// - 当只有单指触摸且已有选中墨迹时，不在此处处理拖动（由 TouchMove 处理）。
        /// - 三指及以上触摸时禁用缩放。 
        /// - 若 StrokesSelectionClone 非空，则对其内的墨迹应用变换；否则在允许两指旋转时也会应用旋转变换。 
        /// - 处理完成后会刷新并更新边框/选择控件的位置。
        /// </remarks>
        private void GridInkCanvasSelectionCover_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            if (TryBlockFrozenPageMutation("移动或缩放墨迹"))
            {
                e.Handled = true;
                return;
            }
            try
            {
                if (dec.Count >= 1)
                {
                    // 单指时，让TouchMove处理拖动
                    if (dec.Count == 1 && inkCanvas.GetSelectedStrokes().Count > 0)
                    {
                        return;
                    }

                    bool disableScale = dec.Count >= 3;
                    var md = e.DeltaManipulation;
                    var trans = md.Translation; // 获得位移矢量
                    var rotate = md.Rotation; // 获得旋转角度
                    var scale = md.Scale; // 获得缩放倍数

                    var m = new Matrix();

                    // Find center of element and then transform to get current location of center
                    var fe = e.Source as FrameworkElement;
                    var center = new Point(fe.ActualWidth / 2, fe.ActualHeight / 2);
                    center = new Point(inkCanvas.GetSelectionBounds().Left + inkCanvas.GetSelectionBounds().Width / 2,
                        inkCanvas.GetSelectionBounds().Top + inkCanvas.GetSelectionBounds().Height / 2);
                    center = m.Transform(center); // 转换为矩阵缩放和旋转的中心点

                    // Update matrix to reflect translation/rotation
                    m.Translate(trans.X, trans.Y); // 移动
                    if (!disableScale)
                        m.ScaleAt(scale.X, scale.Y, center.X, center.Y); // 缩放

                    var strokes = inkCanvas.GetSelectedStrokes();
                    if (StrokesSelectionClone.Count != 0)
                        strokes = StrokesSelectionClone;
                    else if (Settings.Gesture.IsEnableTwoFingerRotationOnSelection)
                        m.RotateAt(rotate, center.X, center.Y); // 旋转

                    // 应用变换到选中的墨迹
                    foreach (var stroke in strokes)
                    {
                        stroke.Transform(m, false);
                    }

                    UpdateSelectionDisplay();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"墨迹ManipulationDelta错误: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理选区覆盖层的触摸按下事件：记录触摸设备 ID，并在第一个触点时保存选择中心与用于拖拽的初始触点位置。
        /// </summary>
        /// <param name="sender">事件源（触摸事件的发送者）。</param>
        /// <param name="e">触摸事件参数，包含触点位置与设备 ID。</param>
        private void GridInkCanvasSelectionCover_TouchDown(object sender, TouchEventArgs e)
        {
            if (TryBlockFrozenPageMutation("移动墨迹"))
            {
                e.Handled = true;
                return;
            }
            dec.Add(e.TouchDevice.Id);
            //设备1个的时候，记录中心点
            if (dec.Count == 1)
            {
                var touchPoint = e.GetTouchPoint(null);
                centerPoint = touchPoint.Position;
                lastTouchPointOnGridInkCanvasCover = touchPoint.Position;

                var touchPointInCanvas = e.GetTouchPoint(inkCanvas);
                lastDragPointInCanvas = touchPointInCanvas.Position;
            }
        }

        /// <summary>
        /// 处理选区覆盖层的触摸结束事件：更新触摸跟踪状态并根据触摸位置和当前选区状态显示或隐藏选区覆盖层与克隆集合。
        /// </summary>
        /// <param name="sender">触发事件的对象（通常为选区覆盖层）。</param>
        /// <param name="e">触摸事件参数，包含触点信息。</param>
        private void GridInkCanvasSelectionCover_TouchUp(object sender, TouchEventArgs e)
        {
            dec.Remove(e.TouchDevice.Id);
            if (dec.Count >= 1) return;
            isProgramChangeStrokeSelection = false;

            lastDragPointInCanvas = new Point(0, 0);

            var touchUpPoint = e.GetTouchPoint(null).Position;
            if (lastTouchPointOnGridInkCanvasCover == touchUpPoint)
            {
                var touchPointInCanvas = e.GetTouchPoint(inkCanvas).Position;
                var selectionBounds = inkCanvas.GetSelectionBounds();

                if (selectionBounds.Contains(touchPointInCanvas))
                {
                    StrokesSelectionClone = new StrokeCollection();
                    CommitPendingStrokeManipulationHistory();
                    RefreshStrokeSelectionChrome();
                    return;
                }

                isProgramChangeStrokeSelection = true;
                inkCanvas.Select(new StrokeCollection());
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                isProgramChangeStrokeSelection = false;
                StrokesSelectionClone = new StrokeCollection();
                HideSelectionDisplay();
            }
            else if (inkCanvas.GetSelectedStrokes().Count == 0)
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                StrokesSelectionClone = new StrokeCollection();
                HideSelectionDisplay();
            }
            else
            {
                StrokesSelectionClone = new StrokeCollection();
                CommitPendingStrokeManipulationHistory();
                RefreshStrokeSelectionChrome();
            }
        }

        /// <summary>
        /// 处理触摸移动事件，按单指拖动并平移当前选中的墨迹，同时更新选择控件的位置。
        /// </summary>
        /// <remarks>仅在存在选中墨迹且当前仅有一个触摸点时生效；若起始触摸点未记录（lastDragPointInCanvas 为 (0,0)）则不移动。只有当触摸位移在任一方向超过 1 像素时，才对每条选中墨迹应用平移变换，并更新选择控件位置与最后触摸点。</remarks>
        private void GridInkCanvasSelectionCover_TouchMove(object sender, TouchEventArgs e)
        {
            if (TryBlockFrozenPageMutation("移动墨迹"))
            {
                e.Handled = true;
                return;
            }
            // 处理触摸移动事件 - 用于拖动选中的墨迹
            if (inkCanvas.GetSelectedStrokes().Count > 0 && dec.Count == 1)
            {
                var currentTouchPoint = e.GetTouchPoint(inkCanvas).Position;

                // 检查是否有有效的起始触摸点
                if (lastDragPointInCanvas != new Point(0, 0))
                {
                    var delta = currentTouchPoint - lastDragPointInCanvas;

                    // 只有当移动距离足够大时才进行拖动（避免微小移动造成的抖动）
                    if (Math.Abs(delta.X) > 1 || Math.Abs(delta.Y) > 1)
                    {
                        // 创建变换矩阵
                        var matrix = new Matrix();
                        matrix.Translate(delta.X, delta.Y);

                        // 对选中的墨迹应用变换
                        var selectedStrokes = inkCanvas.GetSelectedStrokes();
                        foreach (var stroke in selectedStrokes)
                        {
                            stroke.Transform(matrix, false);
                        }

                        // 更新选择框和旋转锚点位置（选中栏保持原位，除非拖动拖动柄）
                        UpdateSelectionDisplay();

                        // 更新最后触摸点
                        lastDragPointInCanvas = currentTouchPoint;
                    }
                }
            }
        }

        private Point lastTouchPointOnGridInkCanvasCover = new Point(0, 0);
        private Point lastDragPointInCanvas = new Point(0, 0);

        /// <summary>
        /// 切换到选择（套索）工具模式并同步光标显示。
        /// </summary>
        /// <remarks>
        /// 同时取消强制橡皮擦和点式橡皮擦状态，并将绘制形状模式重置为默认（0）。
        /// </remarks>
        private void LassoSelect_Click(object sender, RoutedEventArgs e)
        {
            forceEraser = false;
            forcePointEraser = false;
            drawingShapeMode = 0;
            // 使用集中化的工具模式切换方法
            SetCurrentToolMode(InkCanvasEditingMode.Select);
            SetCursorBasedOnEditingMode(inkCanvas);
        }

        /// <summary>
        /// 处理套索选择按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 设置工具模式为选择模式
        /// 启用墨迹画布的操作支持
        /// 设置光标为选择模式光标
        /// </remarks>
        private void BtnLassoSelect_Click(object sender, RoutedEventArgs e)
        {
            forceEraser = false;
            forcePointEraser = false;
            drawingShapeMode = 0;
            // 使用集中化的工具模式切换方法
            SetCurrentToolMode(InkCanvasEditingMode.Select);
            inkCanvas.IsManipulationEnabled = true;
            SetCursorBasedOnEditingMode(inkCanvas);
        }

        #region UIElement Selection and Resize

        /// <summary>
        /// 获取UI元素的边界
        /// </summary>
        /// <param name="element">UI元素</param>
        /// <returns>UI元素的边界矩形</returns>
        /// <remarks>
        /// 如果元素是FrameworkElement，获取其位置和大小
        /// 如果元素有RenderTransform，尝试使用变换后的边界
        /// 如果变换失败，回退到简单计算
        /// 如果元素不是FrameworkElement，返回空矩形
        /// </remarks>
        private Rect GetUIElementBounds(UIElement element)
        {
            if (element is FrameworkElement fe)
            {
                var left = InkCanvas.GetLeft(element);
                var top = InkCanvas.GetTop(element);

                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                var width = fe.ActualWidth > 0 ? fe.ActualWidth : fe.Width;
                var height = fe.ActualHeight > 0 ? fe.ActualHeight : fe.Height;

                // 检查是否有RenderTransform
                if (fe.RenderTransform != null && fe.RenderTransform != Transform.Identity)
                {
                    try
                    {
                        // 如果有变换，使用变换后的边界
                        var transform = element.TransformToAncestor(inkCanvas);
                        var elementBounds = new Rect(0, 0, width, height);
                        var transformedBounds = transform.TransformBounds(elementBounds);
                        return transformedBounds;
                    }
                    catch
                    {
                        // 变换失败时回退到简单计算
                        return new Rect(left, top, width, height);
                    }
                }

                // 没有变换时直接使用位置和大小
                return new Rect(left, top, width, height);
            }

            return new Rect(0, 0, 0, 0);
        }

        #endregion

        #region Selection Display and Resize Handles

        private void GridInkCanvasSelectionCover_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (GridInkCanvasSelectionCover.IsVisible && inkCanvas.GetSelectedStrokes().Count > 0)
            {
                updateBorderStrokeSelectionControlLocation();
                UpdateSelectionDisplay();
            }
            else if (currentSelectedElement == null)
            {
                HideSelectionDisplay();
            }
        }

        private void RefreshStrokeSelectionChrome()
        {
            if (inkCanvas.GetSelectedStrokes().Count == 0)
            {
                HideSelectionDisplay();
                return;
            }

            GridInkCanvasSelectionCover.Visibility = Visibility.Visible;
            // 仅刷新选择框和旋转锚点；选中栏保持原位，只在产生新选区时自动定位
            UpdateSelectionDisplay();
        }

        /// <summary>
        /// 在画布上显示当前选中墨迹的可视选择框与调整控件（如有选中墨迹则显示，否则隐藏）。
        /// </summary>
        /// <remarks>
        /// 如果没有选中任何墨迹，隐藏选择显示；否则计算选区边界，将选择框在所有方向各扩展 8 像素，设置选择框的位置与尺寸，并更新调整句柄的位置后显示句柄画布。
        /// </remarks>
        private void UpdateSelectionDisplay()
        {
            if (inkCanvas.GetSelectedStrokes().Count == 0)
            {
                HideSelectionDisplay();
                return;
            }

            var selectionBounds = inkCanvas.GetSelectionBounds();

            // 向外扩展8像素
            double expandOffset = 8;

            // 更新选择框，向外扩展8像素
            SelectionRectangle.Visibility = Visibility.Visible;
            SelectionRectangle.Margin = new Thickness(selectionBounds.Left - expandOffset, selectionBounds.Top - expandOffset, 0, 0);
            SelectionRectangle.Width = selectionBounds.Width + expandOffset * 2;
            SelectionRectangle.Height = selectionBounds.Height + expandOffset * 2;

            // 更新选择点位置
            UpdateSelectionHandles(selectionBounds);
            SelectionHandlesCanvas.Visibility = Visibility.Visible;

            ShowStrokeRotationHandle(selectionBounds);

            // 选中的是坐标系时，额外显示轴长调节手柄
            UpdateCoordinateAxisHandles();

            // 选中的是垂线/中垂线/角平分线时，显示端点伸缩手柄
            UpdateGeometryLineHandles();
        }

        /// <summary>
        /// 隐藏选择显示
        /// </summary>
        /// <remarks>
        /// 隐藏选择矩形和选择把手画布
        /// </remarks>
        private void HideSelectionDisplay()
        {
            CloseStrokeSelectionColorPopup();
            SelectionRectangle.Visibility = Visibility.Collapsed;
            SelectionHandlesCanvas.Visibility = Visibility.Collapsed;
            HideCoordinateAxisHandles();
            HideGeometryLineHandles();
            if (currentSelectedElement == null)
            {
                if (_isStrokeRotationOverlayActive)
                {
                    _isStrokeRotationOverlayActive = false;
                    CommitPendingStrokeManipulationHistory();
                }

                _strokeRotationAngle = 0;
                HideImageResizeHandles();
            }
        }

        /// <summary>
        /// 根据给定的选择边界定位并设置八个缩放/移动把手的位置，四个把手在各边外扩展 8 像素，四个角把手完全位于外部。
        /// </summary>
        /// <param name="bounds">当前选区在画布坐标系中的边界矩形（未包含用于显示的 8 像素外扩展）。</param>
        private void UpdateSelectionHandles(Rect bounds)
        {
            // 四个边选择点，向外扩展8像素
            TopHandle.Margin = new Thickness(bounds.Left + bounds.Width / 2 - 4, bounds.Top - 12, 0, 0);
            BottomHandle.Margin = new Thickness(bounds.Left + bounds.Width / 2 - 4, bounds.Bottom + 4, 0, 0);
            LeftHandle.Margin = new Thickness(bounds.Left - 12, bounds.Top + bounds.Height / 2 - 4, 0, 0);
            RightHandle.Margin = new Thickness(bounds.Right + 4, bounds.Top + bounds.Height / 2 - 4, 0, 0);

            // 四个角选择点，完全位于选择框外部
            TopLeftHandle.Margin = new Thickness(bounds.Left - 12, bounds.Top - 12, 0, 0);
            TopRightHandle.Margin = new Thickness(bounds.Right + 4, bounds.Top - 12, 0, 0);
            BottomLeftHandle.Margin = new Thickness(bounds.Left - 12, bounds.Bottom + 4, 0, 0);
            BottomRightHandle.Margin = new Thickness(bounds.Right + 4, bounds.Bottom + 4, 0, 0);
        }

        /// <summary>
        /// 在用户按下选择框的缩放把手时开始缩放操作。
        /// </summary>
        /// <remarks>
        /// 记录缩放开始位置和当前选区的初始边界，设置当前活动把手并捕获鼠标以便后续移动/释放事件处理。
        /// </remarks>
        /// <param name="sender">触发事件的缩放把手，预期为一个 Rectangle。</param>
        /// <param name="e">包含鼠标按下事件的位置信息和处理标志的 <see cref="MouseButtonEventArgs"/> 实例。</param>
        private void SelectionHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("调整墨迹大小"))
            {
                e.Handled = true;
                return;
            }
            if (sender is Rectangle handle)
            {
                isResizing = true;
                currentResizeHandle = handle.Name;
                resizeStartPoint = e.GetPosition(inkCanvas);
                originalSelectionBounds = inkCanvas.GetSelectionBounds();
                handle.CaptureMouse();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 处理选择把手鼠标移动事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标事件参数</param>
        /// <remarks>
        /// 如果正在调整大小，计算新的边界
        /// 应用新的边界到选中的墨迹
        /// 更新选择框显示
        /// </remarks>
        private void SelectionHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isResizing || !(sender is Rectangle handle)) return;

            var currentPoint = e.GetPosition(inkCanvas);
            var delta = new Point(currentPoint.X - resizeStartPoint.X, currentPoint.Y - resizeStartPoint.Y);

            var newBounds = CalculateNewBounds(originalSelectionBounds, delta, currentResizeHandle);

            // 应用新的边界到选中的墨迹
            ApplyBoundsToStrokes(newBounds);

            // 更新选择框显示
            UpdateSelectionDisplay();
        }

        /// <summary>
        /// 在选择框的大小调整句柄上释放鼠标时结束调整操作、释放句柄的鼠标捕获并将事件标记为已处理。
        /// </summary>
        /// <param name="sender">触发事件的对象，应为表示调整句柄的 <see cref="System.Windows.Shapes.Rectangle"/>。</param>
        /// <param name="e">鼠标事件参数；在处理后该事件会被标记为已处理。</param>
        private void SelectionHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Rectangle handle)
            {
                isResizing = false;
                currentResizeHandle = "";
                handle.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 在触摸按下选择调整句柄时开始调整操作并记录初始状态（活动句柄、起始点和当前选区边界）。
        /// </summary>
        /// <param name="sender">触发事件的调整句柄（应为 Rectangle）。</param>
        /// <param name="e">触摸事件数据；方法会标记事件为已处理并使用其触点相对于 inkCanvas 的位置。</param>
        private void SelectionHandle_TouchDown(object sender, TouchEventArgs e)
        {
            if (TryBlockFrozenPageMutation("调整墨迹大小"))
            {
                e.Handled = true;
                return;
            }
            if (sender is Rectangle handle)
            {
                isResizing = true;
                currentResizeHandle = handle.Name;
                var touchPoint = e.GetTouchPoint(inkCanvas);
                resizeStartPoint = touchPoint.Position;
                originalSelectionBounds = inkCanvas.GetSelectionBounds();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 在触摸移动时根据拖动更新选区的边界并将其应用到当前所选的墨迹。
        /// </summary>
        /// <param name="sender">触发事件的调整句柄（应为一个 Rectangle）。</param>
        /// <param name="e">包含触摸位置和状态的事件参数。</param>
        /// <remarks>计算新的选择边界、将其应用到所选墨迹并刷新选择显示；将事件标记为已处理。</remarks>
        private void SelectionHandle_TouchMove(object sender, TouchEventArgs e)
        {
            if (!isResizing || !(sender is Rectangle handle)) return;

            var touchPoint = e.GetTouchPoint(inkCanvas);
            var currentPoint = touchPoint.Position;
            var delta = new Point(currentPoint.X - resizeStartPoint.X, currentPoint.Y - resizeStartPoint.Y);

            var newBounds = CalculateNewBounds(originalSelectionBounds, delta, currentResizeHandle);

            // 应用新的边界到选中的墨迹
            ApplyBoundsToStrokes(newBounds);

            // 更新选择框显示
            UpdateSelectionDisplay();

            e.Handled = true;
        }

        /// <summary>
        /// 结束触摸对选择框调整大小的交互，重置调整状态并标记事件已处理。
        /// </summary>
        /// <param name="sender">触发事件的调整句柄（Rectangle）。</param>
        /// <param name="e">触摸事件数据；方法会将其标记为已处理。</param>
        private void SelectionHandle_TouchUp(object sender, TouchEventArgs e)
        {
            if (sender is Rectangle handle)
            {
                isResizing = false;
                currentResizeHandle = "";
                e.Handled = true;
            }
        }

        /// <summary>
        /// 根据指定的拖动增量和所操作的调整柄，计算并返回调整后的边界矩形。
        /// </summary>
        /// <param name="originalBounds">调整前的原始边界矩形。</param>
        /// <param name="delta">从初始位置到当前拖动位置的偏移量，用于更新对应边或角的位置和尺寸。</param>
        /// <param name="handleName">被拖动的调整柄的名称，支持的值：`TopLeftHandle`、`TopRightHandle`、`BottomLeftHandle`、`BottomRightHandle`、`TopHandle`、`BottomHandle`、`LeftHandle`、`RightHandle`。</param>
        /// <returns>应用偏移并强制最小宽高限制（10×10）后的新的边界矩形。</returns>
        private Rect CalculateNewBounds(Rect originalBounds, Point delta, string handleName)
        {
            var newBounds = originalBounds;
            double newWidth = originalBounds.Width;
            double newHeight = originalBounds.Height;
            double newX = originalBounds.X;
            double newY = originalBounds.Y;

            switch (handleName)
            {
                case "TopLeftHandle":
                    newX = originalBounds.X + delta.X;
                    newY = originalBounds.Y + delta.Y;
                    newWidth = originalBounds.Width - delta.X;
                    newHeight = originalBounds.Height - delta.Y;
                    break;
                case "TopRightHandle":
                    newY = originalBounds.Y + delta.Y;
                    newWidth = originalBounds.Width + delta.X;
                    newHeight = originalBounds.Height - delta.Y;
                    break;
                case "BottomLeftHandle":
                    newX = originalBounds.X + delta.X;
                    newWidth = originalBounds.Width - delta.X;
                    newHeight = originalBounds.Height + delta.Y;
                    break;
                case "BottomRightHandle":
                    newWidth = originalBounds.Width + delta.X;
                    newHeight = originalBounds.Height + delta.Y;
                    break;
                case "TopHandle":
                    newY = originalBounds.Y + delta.Y;
                    newHeight = originalBounds.Height - delta.Y;
                    break;
                case "BottomHandle":
                    newHeight = originalBounds.Height + delta.Y;
                    break;
                case "LeftHandle":
                    newX = originalBounds.X + delta.X;
                    newWidth = originalBounds.Width - delta.X;
                    break;
                case "RightHandle":
                    newWidth = originalBounds.Width + delta.X;
                    break;
            }

            // 确保最小尺寸和正值
            if (newWidth < 10) newWidth = 10;
            if (newHeight < 10) newHeight = 10;

            // 创建新的Rect，确保所有值都是有效的
            newBounds = new Rect(newX, newY, newWidth, newHeight);

            return newBounds;
        }

        /// <summary>
        /// 应用新的边界到选中的墨迹
        /// </summary>
        /// <param name="newBounds">新的边界矩形</param>
        /// <remarks>
        /// 计算缩放比例和平移量
        /// 创建变换矩阵
        /// 应用变换到选中的墨迹
        /// </remarks>
        private void ApplyBoundsToStrokes(Rect newBounds)
        {
            var selectedStrokes = inkCanvas.GetSelectedStrokes();
            if (selectedStrokes.Count == 0) return;

            var originalBounds = inkCanvas.GetSelectionBounds();

            // 计算缩放比例
            var scaleX = newBounds.Width / originalBounds.Width;
            var scaleY = newBounds.Height / originalBounds.Height;

            // 计算平移量
            var translateX = newBounds.X - originalBounds.X;
            var translateY = newBounds.Y - originalBounds.Y;

            // 创建变换矩阵
            var matrix = new Matrix();
            matrix.Translate(translateX, translateY);
            matrix.ScaleAt(scaleX, scaleY, originalBounds.X + originalBounds.Width / 2, originalBounds.Y + originalBounds.Height / 2);

            // 应用变换到选中的墨迹
            foreach (var stroke in selectedStrokes)
            {
                stroke.Transform(matrix, false);
            }
        }

        #endregion
    }
}
