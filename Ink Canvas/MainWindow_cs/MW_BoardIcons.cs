using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        /// <summary>
        /// 处理背景颜色按钮点击事件，显示或隐藏背景颜色选项面板
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 检查应用是否已加载
        /// - 创建背景选项面板（如果不存在）
        /// - 显示或隐藏背景选项面板
        /// - 隐藏其他可能显示的面板
        /// - 处理白板/黑板模式切换
        /// - 更新背景颜色和墨迹颜色
        /// </remarks>
        private void BoardChangeBackgroundColorBtn_MouseUp(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            if (BackgroundPalette.IsOpen)
            {
                AnimationsHelper.HidePopupWithSlideAndFade(BackgroundPalette);
            }
            else
            {
                HideSubPanels();
                LoadCustomBackgroundColor();
                LoadGridLinesSetting();
                UpdateBackgroundButtonsState();
                AnimationsHelper.ShowPopupWithSlideAndFade(BackgroundPalette);
                _popupManager?.BringToFront(BackgroundPalette);
            }
        }

        private void WhiteboardModeBtn_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Settings.Canvas.UsingWhiteboard = true;
            SaveSettingsToFile();
            ICCWaterMarkDark.Visibility = Visibility.Visible;
            ICCWaterMarkWhite.Visibility = Visibility.Collapsed;

            Color defaultWhiteboardColor = Color.FromRgb(255, 255, 255);

            if (currentMode == 1)
            {
                GridBackgroundCover.Background = new SolidColorBrush(defaultWhiteboardColor);
                UpdateRGBSliders(defaultWhiteboardColor);
                CustomBackgroundColor = defaultWhiteboardColor;
                string colorHex = $"#{defaultWhiteboardColor.R:X2}{defaultWhiteboardColor.G:X2}{defaultWhiteboardColor.B:X2}";
                Settings.Canvas.CustomBackgroundColor = colorHex;
                SaveSettingsToFile();
            }

            CheckLastColor(0);
            forceEraser = false;
            CheckColorTheme(true);
            UpdateBackgroundButtonsState();
        }

        private void BlackboardModeBtn_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Settings.Canvas.UsingWhiteboard = false;
            SaveSettingsToFile();
            ICCWaterMarkWhite.Visibility = Visibility.Visible;
            ICCWaterMarkDark.Visibility = Visibility.Collapsed;

            Color defaultBlackboardColor = Color.FromRgb(22, 41, 36);

            if (currentMode == 1)
            {
                GridBackgroundCover.Background = new SolidColorBrush(defaultBlackboardColor);
                UpdateRGBSliders(defaultBlackboardColor);
                CustomBackgroundColor = defaultBlackboardColor;
                string colorHex = $"#{defaultBlackboardColor.R:X2}{defaultBlackboardColor.G:X2}{defaultBlackboardColor.B:X2}";
                Settings.Canvas.CustomBackgroundColor = colorHex;
                SaveSettingsToFile();
            }

            CheckLastColor(5);
            forceEraser = false;
            CheckColorTheme(true);
            UpdateBackgroundButtonsState();
        }

        private void DarkModeBtn_MouseUp(object sender, MouseButtonEventArgs e)
        {
            Settings.Canvas.UsingWhiteboard = false;
            SaveSettingsToFile();
            ICCWaterMarkWhite.Visibility = Visibility.Visible;
            ICCWaterMarkDark.Visibility = Visibility.Collapsed;

            Color darkModeColor = Color.FromRgb(0, 0, 0);

            if (currentMode == 1)
            {
                GridBackgroundCover.Background = new SolidColorBrush(darkModeColor);
                UpdateRGBSliders(darkModeColor);
                CustomBackgroundColor = darkModeColor;
                string colorHex = $"#{darkModeColor.R:X2}{darkModeColor.G:X2}{darkModeColor.B:X2}";
                Settings.Canvas.CustomBackgroundColor = colorHex;
                SaveSettingsToFile();
            }

            CheckLastColor(5);
            forceEraser = false;
            CheckColorTheme(true);
            UpdateBackgroundButtonsState();
        }

        private void BackgroundRSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BackgroundRValue != null)
            {
                BackgroundRValue.Text = ((int)e.NewValue).ToString();
                UpdateColorPreviewFromSliders();
            }
        }

        private void BackgroundGSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BackgroundGValue != null)
            {
                BackgroundGValue.Text = ((int)e.NewValue).ToString();
                UpdateColorPreviewFromSliders();
            }
        }

        private void BackgroundBSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BackgroundBValue != null)
            {
                BackgroundBValue.Text = ((int)e.NewValue).ToString();
                UpdateColorPreviewFromSliders();
            }
        }

        private void ApplyBackgroundColorBtn_Click(object sender, RoutedEventArgs e)
        {
            Color selectedColor = Color.FromRgb(
                (byte)BackgroundRSlider.Value,
                (byte)BackgroundGSlider.Value,
                (byte)BackgroundBSlider.Value
            );
            ApplyCustomBackgroundColor(selectedColor);
        }

        private void UpdateColorPreviewFromSliders()
        {
            if (BackgroundColorPreview != null)
            {
                Color previewColor = Color.FromRgb(
                    (byte)BackgroundRSlider.Value,
                    (byte)BackgroundGSlider.Value,
                    (byte)BackgroundBSlider.Value
                );
                BackgroundColorPreview.Background = new SolidColorBrush(previewColor);
            }
        }

        private void UpdateBackgroundButtonsState()
        {
            // 判断当前背景色最接近哪个预设
            bool isWhiteboard = Settings.Canvas.UsingWhiteboard;
            bool isDarkMode = false;
            if (!isWhiteboard && CustomBackgroundColor.HasValue)
            {
                Color c = CustomBackgroundColor.Value;
                // 如果背景色接近纯黑（RGB都很低），则认为是黑底模式
                if (c.R <= 10 && c.G <= 10 && c.B <= 10)
                    isDarkMode = true;
            }

            if (WhiteboardModeBtn != null)
            {
                WhiteboardModeBtn.Background = isWhiteboard ?
                    new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xeb)) :
                    new SolidColorBrush(Colors.LightGray);
                if (WhiteboardModeBtn.Child is TextBlock whiteboardText)
                {
                    whiteboardText.Foreground = isWhiteboard ?
                        new SolidColorBrush(Colors.White) :
                        new SolidColorBrush(Colors.Black);
                }
            }

            if (BlackboardModeBtn != null)
            {
                BlackboardModeBtn.Background = (!isWhiteboard && !isDarkMode) ?
                    new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xeb)) :
                    new SolidColorBrush(Colors.LightGray);
                if (BlackboardModeBtn.Child is TextBlock blackboardText)
                {
                    blackboardText.Foreground = (!isWhiteboard && !isDarkMode) ?
                        new SolidColorBrush(Colors.White) :
                        new SolidColorBrush(Colors.Black);
                }
            }

            if (DarkModeBtn != null)
            {
                DarkModeBtn.Background = isDarkMode ?
                    new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xeb)) :
                    new SolidColorBrush(Colors.LightGray);
                if (DarkModeBtn.Child is TextBlock darkModeText)
                {
                    darkModeText.Foreground = isDarkMode ?
                        new SolidColorBrush(Colors.White) :
                        new SolidColorBrush(Colors.Black);
                }
            }
        }

        /// <summary>
        /// 当前自定义背景色
        /// </summary>
        private Color? CustomBackgroundColor { get; set; }

        private bool _isGridLinesVisible;
        private int _gridDensity = 20;
        private int _gridBrightness = 229;
        private bool _showGridHorizontalLines = true;
        private bool _showGridVerticalLines = true;
        private Color? _originalBackgroundColor;

        /// <summary>
        /// 更新颜色预览框的颜色
        /// </summary>
        private void UpdateColorPreview(Border colorPreview, Slider rSlider, Slider gSlider, Slider bSlider)
        {
            Color previewColor = Color.FromRgb(
                (byte)rSlider.Value,
                (byte)gSlider.Value,
                (byte)bSlider.Value
            );
            colorPreview.Background = new SolidColorBrush(previewColor);
        }

        /// <summary>
        /// 应用自定义背景颜色
        /// </summary>
        private void ApplyCustomBackgroundColor(Color color)
        {
            // 保存当前选择的颜色
            CustomBackgroundColor = color;

            // 将颜色转换为十六进制字符串并保存到设置中
            string colorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            Settings.Canvas.CustomBackgroundColor = colorHex;

            // 只在白板或黑板模式下应用自定义背景色
            if (currentMode == 1) // 白板或黑板模式
            {
                // 设置白板/黑板模式下的背景（网格开启时使用网格画刷）
                if (_isGridLinesVisible && GridBackgroundCover != null)
                {
                    GridBackgroundCover.Background = CreateGridBackgroundBrush(color);
                }
                else
                {
                    GridBackgroundCover.Background = new SolidColorBrush(color);
                }
            }

            // 保存设置
            SaveSettingsToFile();

            // 立即更新界面
            if (BackgroundPalette != null)
            {
                UpdateBackgroundButtonsState();
                UpdateRGBSliders(color); // 更新RGB滑块的值
            }
        }

        /// <summary>
        /// 从设置中加载自定义背景色
        /// </summary>
        private void LoadCustomBackgroundColor()
        {
            if (!string.IsNullOrEmpty(Settings.Canvas.CustomBackgroundColor))
            {
                try
                {
                    // 解析颜色字符串
                    string colorHex = Settings.Canvas.CustomBackgroundColor;
                    if (colorHex.StartsWith("#") && colorHex.Length == 7) // #RRGGBB 格式
                    {
                        byte r = Convert.ToByte(colorHex.Substring(1, 2), 16);
                        byte g = Convert.ToByte(colorHex.Substring(3, 2), 16);
                        byte b = Convert.ToByte(colorHex.Substring(5, 2), 16);

                        // 保存到内存中
                        CustomBackgroundColor = Color.FromRgb(r, g, b);
                    }
                    else
                    {
                        // 非法格式（非 #RRGGBB）同样走解析失败分支，避免 CustomBackgroundColor 保持为空导致网格背景无法重建
                        throw new FormatException($"自定义背景色不是 #RRGGBB 格式: {colorHex}");
                    }
                }
                catch (Exception ex)
                {
                    // 解析失败，根据当前模式设置默认颜色
                    if (!Settings.Canvas.UsingWhiteboard)
                    {
                        // 黑板模式默认颜色
                        CustomBackgroundColor = Color.FromRgb(22, 41, 36);
                    }
                    else
                    {
                        // 白板模式默认颜色
                        CustomBackgroundColor = Color.FromRgb(234, 235, 237);
                    }

                    // 可以在这里记录日志
                    Console.WriteLine($"解析自定义背景色失败: {ex.Message}");
                }
            }
            else
            {
                // 如果没有设置自定义背景色，根据当前模式设置默认颜色
                if (!Settings.Canvas.UsingWhiteboard)
                {
                    // 黑板模式默认颜色
                    CustomBackgroundColor = Color.FromRgb(22, 41, 36);
                }
                else
                {
                    // 白板模式默认颜色
                    CustomBackgroundColor = Color.FromRgb(234, 235, 237);
                }
            }

            // 只在白板或黑板模式下应用自定义背景色
            if (currentMode == 1 && CustomBackgroundColor.HasValue) // 白板或黑板模式
            {
                // 设置白板/黑板模式下的背景
                GridBackgroundCover.Background = new SolidColorBrush(CustomBackgroundColor.Value);

                // 更新RGB滑块的值（如果调色板已经创建）
                if (BackgroundPalette != null && BackgroundPalette.Visibility == Visibility.Visible)
                {
                    UpdateRGBSliders(CustomBackgroundColor.Value);
                }
            }
        }

        #region 网格背景功能

        /// <summary>
        /// 创建带有网格线的背景画刷
        /// </summary>
        private Brush CreateGridBackgroundBrush(Color backgroundColor)
        {
            var drawingGroup = new DrawingGroup();

            drawingGroup.Children.Add(new GeometryDrawing(new SolidColorBrush(backgroundColor), null,
                new RectangleGeometry(new Rect(0, 0, _gridDensity, _gridDensity))));

            var lineGeometry = new GeometryGroup();

            if (_showGridHorizontalLines)
            {
                lineGeometry.Children.Add(new LineGeometry(new Point(_gridDensity, 0), new Point(0, 0)));
            }

            if (_showGridVerticalLines)
            {
                lineGeometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, _gridDensity)));
            }

            if (lineGeometry.Children.Count > 0)
            {
                byte brightness = (byte)_gridBrightness;
                drawingGroup.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(brightness, brightness, brightness)), 1), lineGeometry));
            }

            return new DrawingBrush(drawingGroup)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, _gridDensity, _gridDensity),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, _gridDensity, _gridDensity),
                ViewboxUnits = BrushMappingMode.Absolute
            };
        }

        /// <summary>
        /// 切换网格线显示状态
        /// </summary>
        private void ToggleGridLines(bool isVisible)
        {
            _isGridLinesVisible = isVisible;

            if (GridBackgroundCover != null)
            {
                if (isVisible)
                {
                    if (GridBackgroundCover.Background is SolidColorBrush solidBrush)
                    {
                        _originalBackgroundColor = solidBrush.Color;
                    }
                    else if (CustomBackgroundColor.HasValue)
                    {
                        _originalBackgroundColor = CustomBackgroundColor.Value;
                    }

                    if (_originalBackgroundColor.HasValue)
                    {
                        GridBackgroundCover.Background = CreateGridBackgroundBrush(_originalBackgroundColor.Value);
                    }
                }
                else
                {
                    if (_originalBackgroundColor.HasValue)
                    {
                        GridBackgroundCover.Background = new SolidColorBrush(_originalBackgroundColor.Value);
                    }
                    else if (CustomBackgroundColor.HasValue)
                    {
                        GridBackgroundCover.Background = new SolidColorBrush(CustomBackgroundColor.Value);
                    }
                }
            }

            Settings.Canvas.ShowGridLines = isVisible;
            SaveSettingsToFile();
        }

        private void GridLinesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ToggleGridLines(true);
        }

        private void GridLinesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ToggleGridLines(false);
        }

        private void LoadGridLinesSetting()
        {
            _isGridLinesVisible = Settings.Canvas.ShowGridLines;
            _gridDensity = Settings.Canvas.GridDensity;
            _gridBrightness = Settings.Canvas.GridBrightness;
            _showGridHorizontalLines = Settings.Canvas.ShowGridHorizontalLines;
            _showGridVerticalLines = Settings.Canvas.ShowGridVerticalLines;

            var content = BackgroundPalettePopupContent;
            if (content?.GridLinesCheckBoxControl != null)
                content.GridLinesCheckBoxControl.IsChecked = _isGridLinesVisible;
            if (content?.GridDensitySliderControl != null)
                content.GridDensitySliderControl.Value = _gridDensity;
            if (content?.GridDensityValueControl != null)
                content.GridDensityValueControl.Text = $"{_gridDensity}px";
            if (content?.GridBrightnessSliderControl != null)
                content.GridBrightnessSliderControl.Value = _gridBrightness;
            if (content?.GridBrightnessValueControl != null)
                content.GridBrightnessValueControl.Text = _gridBrightness.ToString();
            if (content?.GridHorizontalLinesCheckBoxControl != null)
                content.GridHorizontalLinesCheckBoxControl.IsChecked = _showGridHorizontalLines;
            if (content?.GridVerticalLinesCheckBoxControl != null)
                content.GridVerticalLinesCheckBoxControl.IsChecked = _showGridVerticalLines;

            if (_isGridLinesVisible && GridBackgroundCover != null && currentMode == 1 && CustomBackgroundColor.HasValue)
            {
                GridBackgroundCover.Background = CreateGridBackgroundBrush(CustomBackgroundColor.Value);
            }
        }

        private void UpdateGridBackgroundWithColor(Color color)
        {
            if (_isGridLinesVisible && GridBackgroundCover != null)
            {
                GridBackgroundCover.Background = CreateGridBackgroundBrush(color);
                _originalBackgroundColor = color;
            }
        }

        private void GridDensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _gridDensity = (int)e.NewValue;

            if (BackgroundPalettePopupContent?.GridDensityValueControl != null)
                BackgroundPalettePopupContent.GridDensityValueControl.Text = $"{_gridDensity}px";

            Settings.Canvas.GridDensity = _gridDensity;
            SaveSettingsToFile();

            if (_isGridLinesVisible && GridBackgroundCover != null && CustomBackgroundColor.HasValue)
            {
                GridBackgroundCover.Background = CreateGridBackgroundBrush(CustomBackgroundColor.Value);
            }
        }

        private void GridBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _gridBrightness = (int)e.NewValue;

            if (BackgroundPalettePopupContent?.GridBrightnessValueControl != null)
                BackgroundPalettePopupContent.GridBrightnessValueControl.Text = _gridBrightness.ToString();

            Settings.Canvas.GridBrightness = _gridBrightness;
            SaveSettingsToFile();

            if (_isGridLinesVisible && GridBackgroundCover != null && CustomBackgroundColor.HasValue)
            {
                GridBackgroundCover.Background = CreateGridBackgroundBrush(CustomBackgroundColor.Value);
            }
        }

        private void GridHorizontalLinesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _showGridHorizontalLines = true;
            Settings.Canvas.ShowGridHorizontalLines = true;
            SaveSettingsToFile();

            if (_isGridLinesVisible && GridBackgroundCover != null && CustomBackgroundColor.HasValue)
            {
                GridBackgroundCover.Background = CreateGridBackgroundBrush(CustomBackgroundColor.Value);
            }
        }

        private void GridHorizontalLinesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _showGridHorizontalLines = false;
            Settings.Canvas.ShowGridHorizontalLines = false;
            SaveSettingsToFile();

            if (_isGridLinesVisible && GridBackgroundCover != null && CustomBackgroundColor.HasValue)
            {
                GridBackgroundCover.Background = CreateGridBackgroundBrush(CustomBackgroundColor.Value);
            }
        }

        private void GridVerticalLinesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _showGridVerticalLines = true;
            Settings.Canvas.ShowGridVerticalLines = true;
            SaveSettingsToFile();

            if (_isGridLinesVisible && GridBackgroundCover != null && CustomBackgroundColor.HasValue)
            {
                GridBackgroundCover.Background = CreateGridBackgroundBrush(CustomBackgroundColor.Value);
            }
        }

        private void GridVerticalLinesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _showGridVerticalLines = false;
            Settings.Canvas.ShowGridVerticalLines = false;
            SaveSettingsToFile();

            if (_isGridLinesVisible && GridBackgroundCover != null && CustomBackgroundColor.HasValue)
            {
                GridBackgroundCover.Background = CreateGridBackgroundBrush(CustomBackgroundColor.Value);
            }
        }

        #endregion

        /// <summary>
        /// 处理套索工具图标点击事件，切换到选择模式
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 禁用橡皮擦模式
        /// - 禁用形状绘制模式
        /// - 设置当前工具模式为选择模式
        /// - 根据编辑模式设置光标
        /// </remarks>
        private void BoardLassoIcon_Click(object sender, RoutedEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换到选择工具")) return;

            BtnSelect_Click(null, null);

            UpdateCurrentToolMode("select");
            SetCursorBasedOnEditingMode(inkCanvas);
            HideSubPanels("select");
        }

        /// <summary>
        /// 处理橡皮擦图标点击事件，切换到按笔画擦除模式
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 禁用高级橡皮擦系统
        /// - 启用橡皮擦模式
        /// - 设置橡皮擦形状为圆形
        /// - 设置当前工具模式为按笔画擦除

        /// 处理删除图标点击事件，清空画布内容
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 调用钢笔图标点击事件
        /// - 调用符号删除鼠标抬起事件
        /// - 根据设置决定是否清空图片
        /// - 如果设置为清空图片，则清空所有子元素
        /// </remarks>
        private void BoardSymbolIconDelete_MouseUp(object sender, RoutedEventArgs e)
        {
            if (TryBlockFrozenPageMutation("清除冻结页面内容")) return;
            PenIcon_Click(null, null);
            // SymbolIconDelete_MouseUp 内部（BtnClear_Click → PerformCanvasClear）已完成
            // 保留非笔画元素→清空→恢复及 SecAgent 元素清理，此处不再重复执行一遍相同流程。
            SymbolIconDelete_MouseUp(null, null);

            // 根据设置决定是否清空图片（PerformCanvasClear 会保留非笔画元素，需在此额外移除）
            if (Settings.Canvas.ClearCanvasAlsoClearImages)
            {
                ShutdownAllCanvasMediaElements();
                inkCanvas.Children.Clear();
            }
        }
        /// <summary>
        /// 处理删除墨迹和历史记录图标点击事件，清空画布内容和时间机器历史
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 调用钢笔图标点击事件
        /// - 调用符号删除鼠标抬起事件
        /// - 根据设置决定是否清空时间机器历史
        /// - 根据设置决定是否清空图片
        /// - 如果设置为清空图片，则清空所有子元素
        /// </remarks>
        private void BoardSymbolIconDeleteInkAndHistories_MouseUp(object sender, RoutedEventArgs e)
        {
            if (TryBlockFrozenPageMutation("清除冻结页面内容")) return;
            PenIcon_Click(null, null);
            // SymbolIconDelete_MouseUp 内部（PerformCanvasClear）已在「清空时删除墨迹历史记录」开启时清空历史；
            // 此按钮无论设置如何都要清空历史，故设置关闭时在此补一次。
            SymbolIconDelete_MouseUp(null, null);
            if (!Settings.Canvas.ClearCanvasAndClearTimeMachine) timeMachine.ClearStrokeHistory();

            // 根据设置决定是否清空图片（PerformCanvasClear 会保留非笔画元素，需在此额外移除）
            if (Settings.Canvas.ClearCanvasAlsoClearImages)
            {
                ShutdownAllCanvasMediaElements();
                inkCanvas.Children.Clear();
            }
        }

        /// <summary>
        /// 处理启动希沃视频展台图标点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 调用图片黑板鼠标抬起事件
        /// - 启动希沃视频展台软件
        /// </remarks>
        private void BoardLaunchEasiCamera_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ImageBlackboard_MouseUp(null, null);
            SoftwareLauncher.LaunchEasiCamera("希沃视频展台");
        }

        /// <summary>
        /// 处理启动Desmos计算器图标点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        /// <remarks>
        /// - 立即隐藏所有子面板
        /// - 调用图片黑板鼠标抬起事件
        /// - 打开Desmos计算器网页
        /// </remarks>
        private void BoardLaunchDesmos_MouseUp(object sender, MouseButtonEventArgs e)
        {
            HideSubPanelsImmediately();
            ImageBlackboard_MouseUp(null, null);
            Process.Start(new ProcessStartInfo("https://www.desmos.com/calculator?lang=zh-CN") { UseShellExecute = true });
        }

        /// <summary>
        /// 根据当前背景颜色更新RGB滑块的值
        /// </summary>
        private void UpdateRGBSliders(Color color)
        {
            if (BackgroundRSlider != null) BackgroundRSlider.Value = color.R;
            if (BackgroundGSlider != null) BackgroundGSlider.Value = color.G;
            if (BackgroundBSlider != null) BackgroundBSlider.Value = color.B;
        }
    }
}
