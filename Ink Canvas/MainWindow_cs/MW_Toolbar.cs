using Ink_Canvas.Controls;
using Ink_Canvas.Controls.Toolbar.FloatingToolbar;
using Ink_Canvas.Helpers;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace Ink_Canvas
{
    public partial class MainWindow
    {
        internal ToolbarImageButton SymbolIconDelete { get; private set; }
        internal ToolbarImageButton Eraser_Icon { get; private set; }
        internal ToolbarImageButton EraserByStrokes_Icon { get; private set; }
        internal ToolbarImageButton SymbolIconSelect { get; private set; }
        internal ToolbarImageButton ShapeDrawFloatingBarBtn { get; private set; }
        internal ToolbarImageButton SymbolIconUndo { get; private set; }
        internal ToolbarImageButton SymbolIconRedo { get; private set; }
        internal ToolbarImageButton CursorWithDelFloatingBarBtn { get; private set; }
        internal ToolbarImageButton WhiteboardFloatingBarBtn { get; private set; }
        internal ToolbarImageButton ToolsFloatingBarBtn { get; private set; }
        internal ToolbarImageButton Fold_Icon { get; private set; }
        internal ToolbarImageButton Freeze_Icon { get; private set; }
        internal ToolbarImageButton Gesture_Icon { get; private set; }
        internal ToolbarImageButton Exit_Icon { get; private set; }

        internal Panel FloatingBarRootPanel => StackPanelFloatingBarRoot;

        internal double FloatingBarSelectionBGLeft => GetSelectionBGLeft();
        internal bool FloatingBarSelectionBGIsHidden
        {
            get
            {
                var (selectionBG, _, _) = GetFirstContentBorderElements();
                return selectionBG == null || selectionBG.Visibility != Visibility.Visible;
            }
        }
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch ToggleSwitchDrawShapeBorderAutoHide { get; } =
            new iNKORE.UI.WPF.Modern.Controls.ToggleSwitch { IsOn = true };

        internal GeometryButton ImageDrawLine => ShapeDrawPopupContent?.DrawLineBtn;
        internal GeometryButton ImageDrawDashedLine => ShapeDrawPopupContent?.DrawDashedLineBtn;
        internal GeometryButton ImageDrawDotLine => ShapeDrawPopupContent?.DrawDotLineBtn;
        internal GeometryButton ImageDrawArrow => ShapeDrawPopupContent?.DrawArrowBtn;
        internal GeometryButton ImageDrawParallelLine => ShapeDrawPopupContent?.DrawParallelLineBtn;

        internal GeometryButton BoardImageDrawLine => BoardShapeDrawPopupContent?.DrawLineBtn;
        internal GeometryButton BoardImageDrawDashedLine => BoardShapeDrawPopupContent?.DrawDashedLineBtn;
        internal GeometryButton BoardImageDrawDotLine => BoardShapeDrawPopupContent?.DrawDotLineBtn;
        internal GeometryButton BoardImageDrawArrow => BoardShapeDrawPopupContent?.DrawArrowBtn;
        internal GeometryButton BoardImageDrawParallelLine => BoardShapeDrawPopupContent?.DrawParallelLineBtn;

        internal void AttachCursorIconView(ToolbarImageButton btn) => Cursor_Icon = btn;
        internal void AttachPenIconView(ToolbarImageButton btn) { Pen_Icon = btn; PenPalette.PlacementTarget = btn; }
        internal void AttachSymbolIconDelete(ToolbarImageButton btn) => SymbolIconDelete = btn;
        internal void AttachEraserIcon(ToolbarImageButton btn) { Eraser_Icon = btn; EraserSizePanel.PlacementTarget = btn; }
        internal void AttachEraserByStrokesIcon(ToolbarImageButton btn) => EraserByStrokes_Icon = btn;
        internal void AttachSymbolIconSelect(ToolbarImageButton btn) => SymbolIconSelect = btn;
        internal void AttachShapeDrawBtn(ToolbarImageButton btn)
        {
            ShapeDrawFloatingBarBtn = btn;
            BorderDrawShape.PlacementTarget = btn;
        }
        internal void AttachSymbolIconUndo(ToolbarImageButton btn)
        {
            SymbolIconUndo = btn;
            AttachUndoLongPressHandlers(btn);
        }
        internal void AttachSymbolIconRedo(ToolbarImageButton btn) => SymbolIconRedo = btn;
        internal void AttachCursorWithDelBtn(ToolbarImageButton btn) => CursorWithDelFloatingBarBtn = btn;
        internal void AttachWhiteboardBtn(ToolbarImageButton btn) => WhiteboardFloatingBarBtn = btn;
        internal void AttachToolsBtn(ToolbarImageButton btn)
        {
            ToolsFloatingBarBtn = btn;
            BorderTools.PlacementTarget = btn;
        }
        internal void AttachFoldIcon(ToolbarImageButton btn) => Fold_Icon = btn;
        internal void AttachGestureBtn(ToolbarImageButton btn) { Gesture_Icon = btn; TwoFingerGestureBorder.PlacementTarget = btn; }
        internal void AttachExitBtn(ToolbarImageButton btn) => Exit_Icon = btn;

        #region PenPalette property mappings
        internal ComboBox ComboBoxPenStyle => PenPalettePopupContent?.PenStyleComboBox ?? BoardPenPalettePopupContent?.PenStyleComboBox;
        internal ComboBox BoardComboBoxPenStyle => BoardPenPalettePopupContent?.PenStyleComboBox;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch ToggleSwitchEnableNibMode => PenPalettePopupContent?.NibModeToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch BoardToggleSwitchEnableNibMode => BoardPenPalettePopupContent?.NibModeToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch FloatingBarToggleSwitchEnableInkToShape => PenPalettePopupContent?.InkToShapeToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch BoardToggleSwitchEnableInkToShape => BoardPenPalettePopupContent?.InkToShapeToggle;
        internal Slider PenWidthSlider => PenPalettePopupContent?.PenWidthSlider;
        internal Slider BoardPenWidthSlider => BoardPenPalettePopupContent?.PenWidthSlider;
        internal Slider PenAlphaSlider => PenPalettePopupContent?.PenAlphaSlider;
        internal Slider BoardPenAlphaSlider => BoardPenPalettePopupContent?.PenAlphaSlider;
        internal Slider LaserPenFadeTimeSlider => PenPalettePopupContent?.LaserPenFadeTimeSlider ?? BoardPenPalettePopupContent?.LaserPenFadeTimeSlider;
        internal Slider BoardLaserPenFadeTimeSlider => BoardPenPalettePopupContent?.LaserPenFadeTimeSlider;
        internal Slider LaserPenFadeSpeedSlider => PenPalettePopupContent?.LaserPenFadeSpeedSlider ?? BoardPenPalettePopupContent?.LaserPenFadeSpeedSlider;
        internal Slider BoardLaserPenFadeSpeedSlider => BoardPenPalettePopupContent?.LaserPenFadeSpeedSlider;
        internal TextBlock PenWidthText => PenPalettePopupContent?.PenWidthText ?? BoardPenPalettePopupContent?.PenWidthText;
        internal TextBlock BoardPenWidthText => BoardPenPalettePopupContent?.PenWidthText;
        internal TextBlock PenAlphaText => PenPalettePopupContent?.PenAlphaText ?? BoardPenPalettePopupContent?.PenAlphaText;
        internal TextBlock BoardPenAlphaText => BoardPenPalettePopupContent?.PenAlphaText;
        internal TextBlock LaserPenFadeTimeText => PenPalettePopupContent?.LaserPenFadeTimeText ?? BoardPenPalettePopupContent?.LaserPenFadeTimeText;
        internal TextBlock BoardLaserPenFadeTimeText => BoardPenPalettePopupContent?.LaserPenFadeTimeText;
        internal TextBlock LaserPenFadeSpeedText => PenPalettePopupContent?.LaserPenFadeSpeedText ?? BoardPenPalettePopupContent?.LaserPenFadeSpeedText;
        internal TextBlock BoardLaserPenFadeSpeedText => BoardPenPalettePopupContent?.LaserPenFadeSpeedText;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch HighlighterOverlapToggle => PenPalettePopupContent?.HighlighterOverlapToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch BoardHighlighterOverlapToggle => BoardPenPalettePopupContent?.HighlighterOverlapToggle;

        internal PopupTabTitleBar PenTabTitleBar => PenPalettePopupContent?.TabBar ?? BoardPenPalettePopupContent?.TabBar;
        internal PopupTabTitleBar BoardPenTabTitleBar => BoardPenPalettePopupContent?.TabBar;
        internal int PenSelectedTabIndex
        {
            get => PenPalettePopupContent?.SelectedTabIndex ?? BoardPenPalettePopupContent?.SelectedTabIndex ?? 0;
            set
            {
                if (PenPalettePopupContent != null) PenPalettePopupContent.SelectedTabIndex = value;
                if (BoardPenPalettePopupContent != null) BoardPenPalettePopupContent.SelectedTabIndex = value;
            }
        }
        internal int BoardPenSelectedTabIndex
        {
            get => BoardPenPalettePopupContent?.SelectedTabIndex ?? 0;
            set { if (BoardPenPalettePopupContent != null) BoardPenPalettePopupContent.SelectedTabIndex = value; }
        }

        internal FrameworkElement CommonPropsPanel => PenPalettePopupContent?.CommonPropsPanel ?? BoardPenPalettePopupContent?.CommonPropsPanel;
        internal FrameworkElement LaserPenFadePanel => PenPalettePopupContent?.LaserPenFadePanel ?? BoardPenPalettePopupContent?.LaserPenFadePanel;
        internal FrameworkElement LaserPenFadeSpeedPanel => PenPalettePopupContent?.LaserPenFadeSpeedPanel ?? BoardPenPalettePopupContent?.LaserPenFadeSpeedPanel;
        internal FrameworkElement InkToShapePanel => PenPalettePopupContent?.InkToShapePanel ?? BoardPenPalettePopupContent?.InkToShapePanel;
        internal FrameworkElement HighlighterOverlapPanel => PenPalettePopupContent?.HighlighterOverlapPanel ?? BoardPenPalettePopupContent?.HighlighterOverlapPanel;
        internal FrameworkElement DefaultPenColorsPanel => PenPalettePopupContent?.DefaultPenColorsPanel ?? BoardPenPalettePopupContent?.DefaultPenColorsPanel;
        internal FrameworkElement HighlighterPenColorsPanel => PenPalettePopupContent?.HighlighterPenColorsPanel ?? BoardPenPalettePopupContent?.HighlighterPenColorsPanel;
        internal FrameworkElement LaserPenColorsPanel => PenPalettePopupContent?.LaserPenColorsPanel ?? BoardPenPalettePopupContent?.LaserPenColorsPanel;

        internal FrameworkElement BoardCommonPropsPanel => BoardPenPalettePopupContent?.CommonPropsPanel;
        internal FrameworkElement BoardLaserPenFadePanel => BoardPenPalettePopupContent?.LaserPenFadePanel;
        internal FrameworkElement BoardLaserPenFadeSpeedPanel => BoardPenPalettePopupContent?.LaserPenFadeSpeedPanel;
        internal FrameworkElement BoardInkToShapePanel => BoardPenPalettePopupContent?.InkToShapePanel;
        internal FrameworkElement BoardHighlighterOverlapPanel => BoardPenPalettePopupContent?.HighlighterOverlapPanel;
        internal FrameworkElement BoardDefaultPenColorsPanel => BoardPenPalettePopupContent?.DefaultPenColorsPanel;
        internal FrameworkElement BoardHighlighterPenColorsPanel => BoardPenPalettePopupContent?.HighlighterPenColorsPanel;
        internal FrameworkElement BoardLaserPenColorsPanel => BoardPenPalettePopupContent?.LaserPenColorsPanel;



        internal PenColorButton BorderPenColorBlack => (PenPalettePopupContent ?? BoardPenPalettePopupContent)?.DefaultPenColorBlack;
        internal PenColorButton BorderPenColorWhite => (PenPalettePopupContent ?? BoardPenPalettePopupContent)?.DefaultPenColorWhite;
        internal PenColorButton BorderPenColorRed => (PenPalettePopupContent ?? BoardPenPalettePopupContent)?.DefaultPenColorRed;
        internal PenColorButton BorderPenColorYellow => (PenPalettePopupContent ?? BoardPenPalettePopupContent)?.DefaultPenColorYellow;
        internal PenColorButton BorderPenColorGreen => (PenPalettePopupContent ?? BoardPenPalettePopupContent)?.DefaultPenColorGreen;
        internal PenColorButton BorderPenColorBlue => (PenPalettePopupContent ?? BoardPenPalettePopupContent)?.DefaultPenColorBlue;
        internal PenColorButton BorderPenColorPink => (PenPalettePopupContent ?? BoardPenPalettePopupContent)?.DefaultPenColorPink;
        internal PenColorButton BorderPenColorTeal => (PenPalettePopupContent ?? BoardPenPalettePopupContent)?.DefaultPenColorTeal;
        internal PenColorButton BorderPenColorOrange => (PenPalettePopupContent ?? BoardPenPalettePopupContent)?.DefaultPenColorOrange;

        internal PenColorButton BoardBorderPenColorBlack => BoardPenPalettePopupContent?.DefaultPenColorBlack;
        internal PenColorButton BoardBorderPenColorWhite => BoardPenPalettePopupContent?.DefaultPenColorWhite;
        internal PenColorButton BoardBorderPenColorRed => BoardPenPalettePopupContent?.DefaultPenColorRed;
        internal PenColorButton BoardBorderPenColorYellow => BoardPenPalettePopupContent?.DefaultPenColorYellow;
        internal PenColorButton BoardBorderPenColorGreen => BoardPenPalettePopupContent?.DefaultPenColorGreen;
        internal PenColorButton BoardBorderPenColorBlue => BoardPenPalettePopupContent?.DefaultPenColorBlue;
        internal PenColorButton BoardBorderPenColorPink => BoardPenPalettePopupContent?.DefaultPenColorPink;
        internal PenColorButton BoardBorderPenColorTeal => BoardPenPalettePopupContent?.DefaultPenColorTeal;
        internal PenColorButton BoardBorderPenColorOrange => BoardPenPalettePopupContent?.DefaultPenColorOrange;

        internal PenColorButton HighlighterPenColorBlack => PenPalettePopupContent?.HighlighterPenColorBlack ?? BoardPenPalettePopupContent?.HighlighterPenColorBlack;
        internal PenColorButton HighlighterPenColorWhite => PenPalettePopupContent?.HighlighterPenColorWhite ?? BoardPenPalettePopupContent?.HighlighterPenColorWhite;
        internal PenColorButton HighlighterPenColorRed => PenPalettePopupContent?.HighlighterPenColorRed ?? BoardPenPalettePopupContent?.HighlighterPenColorRed;
        internal PenColorButton HighlighterPenColorYellow => PenPalettePopupContent?.HighlighterPenColorYellow ?? BoardPenPalettePopupContent?.HighlighterPenColorYellow;
        internal PenColorButton HighlighterPenColorGreen => PenPalettePopupContent?.HighlighterPenColorGreen ?? BoardPenPalettePopupContent?.HighlighterPenColorGreen;
        internal PenColorButton HighlighterPenColorZinc => PenPalettePopupContent?.HighlighterPenColorZinc ?? BoardPenPalettePopupContent?.HighlighterPenColorZinc;
        internal PenColorButton HighlighterPenColorBlue => PenPalettePopupContent?.HighlighterPenColorBlue ?? BoardPenPalettePopupContent?.HighlighterPenColorBlue;
        internal PenColorButton HighlighterPenPenColorPurple => PenPalettePopupContent?.HighlighterPenColorPurple ?? BoardPenPalettePopupContent?.HighlighterPenColorPurple;
        internal PenColorButton HighlighterPenColorTeal => PenPalettePopupContent?.HighlighterPenColorTeal ?? BoardPenPalettePopupContent?.HighlighterPenColorTeal;
        internal PenColorButton HighlighterPenColorOrange => PenPalettePopupContent?.HighlighterPenColorOrange ?? BoardPenPalettePopupContent?.HighlighterPenColorOrange;

        internal PenColorButton BoardHighlighterPenColorBlack => BoardPenPalettePopupContent?.HighlighterPenColorBlack;
        internal PenColorButton BoardHighlighterPenColorWhite => BoardPenPalettePopupContent?.HighlighterPenColorWhite;
        internal PenColorButton BoardHighlighterPenColorRed => BoardPenPalettePopupContent?.HighlighterPenColorRed;
        internal PenColorButton BoardHighlighterPenColorYellow => BoardPenPalettePopupContent?.HighlighterPenColorYellow;
        internal PenColorButton BoardHighlighterPenColorGreen => BoardPenPalettePopupContent?.HighlighterPenColorGreen;
        internal PenColorButton BoardHighlighterPenColorZinc => BoardPenPalettePopupContent?.HighlighterPenColorZinc;
        internal PenColorButton BoardHighlighterPenColorBlue => BoardPenPalettePopupContent?.HighlighterPenColorBlue;
        internal PenColorButton BoardHighlighterPenPenColorPurple => BoardPenPalettePopupContent?.HighlighterPenColorPurple;
        internal PenColorButton BoardHighlighterPenColorTeal => BoardPenPalettePopupContent?.HighlighterPenColorTeal;
        internal PenColorButton BoardHighlighterPenColorOrange => BoardPenPalettePopupContent?.HighlighterPenColorOrange;

        internal PenColorButton LaserPenColorBlack => PenPalettePopupContent?.LaserPenColorBlack ?? BoardPenPalettePopupContent?.LaserPenColorBlack;
        internal PenColorButton LaserPenColorWhite => PenPalettePopupContent?.LaserPenColorWhite ?? BoardPenPalettePopupContent?.LaserPenColorWhite;
        internal PenColorButton LaserPenColorRed => PenPalettePopupContent?.LaserPenColorRed ?? BoardPenPalettePopupContent?.LaserPenColorRed;
        internal PenColorButton LaserPenColorYellow => PenPalettePopupContent?.LaserPenColorYellow ?? BoardPenPalettePopupContent?.LaserPenColorYellow;
        internal PenColorButton LaserPenColorGreen => PenPalettePopupContent?.LaserPenColorGreen ?? BoardPenPalettePopupContent?.LaserPenColorGreen;
        internal PenColorButton LaserPenColorBlue => PenPalettePopupContent?.LaserPenColorBlue ?? BoardPenPalettePopupContent?.LaserPenColorBlue;
        internal PenColorButton LaserPenColorPink => PenPalettePopupContent?.LaserPenColorPink ?? BoardPenPalettePopupContent?.LaserPenColorPink;
        internal PenColorButton LaserPenColorTeal => PenPalettePopupContent?.LaserPenColorTeal ?? BoardPenPalettePopupContent?.LaserPenColorTeal;
        internal PenColorButton LaserPenColorOrange => PenPalettePopupContent?.LaserPenColorOrange ?? BoardPenPalettePopupContent?.LaserPenColorOrange;

        internal PenColorButton BoardLaserPenColorBlack => BoardPenPalettePopupContent?.LaserPenColorBlack;
        internal PenColorButton BoardLaserPenColorWhite => BoardPenPalettePopupContent?.LaserPenColorWhite;
        internal PenColorButton BoardLaserPenColorRed => BoardPenPalettePopupContent?.LaserPenColorRed;
        internal PenColorButton BoardLaserPenColorYellow => BoardPenPalettePopupContent?.LaserPenColorYellow;
        internal PenColorButton BoardLaserPenColorGreen => BoardPenPalettePopupContent?.LaserPenColorGreen;
        internal PenColorButton BoardLaserPenColorBlue => BoardPenPalettePopupContent?.LaserPenColorBlue;
        internal PenColorButton BoardLaserPenColorPink => BoardPenPalettePopupContent?.LaserPenColorPink;
        internal PenColorButton BoardLaserPenColorTeal => BoardPenPalettePopupContent?.LaserPenColorTeal;
        internal PenColorButton BoardLaserPenColorOrange => BoardPenPalettePopupContent?.LaserPenColorOrange;

        internal Border ColorThemeSwitch => PenPalettePopupContent?.ColorThemeSwitch ?? BoardPenPalettePopupContent?.ColorThemeSwitch;
        internal Image ColorThemeSwitchIcon => PenPalettePopupContent?.ColorThemeSwitchIcon ?? BoardPenPalettePopupContent?.ColorThemeSwitchIcon;
        internal TextBlock ColorThemeSwitchTextBlock => PenPalettePopupContent?.ColorThemeSwitchText ?? BoardPenPalettePopupContent?.ColorThemeSwitchText;
        internal Border BoardColorThemeSwitch => BoardPenPalettePopupContent?.ColorThemeSwitch;
        internal Image BoardColorThemeSwitchIcon => BoardPenPalettePopupContent?.ColorThemeSwitchIcon;
        internal TextBlock BoardColorThemeSwitchTextBlock => BoardPenPalettePopupContent?.ColorThemeSwitchText;
        internal Border LaserPenColorThemeSwitch => PenPalettePopupContent?.LaserPenColorThemeSwitch ?? BoardPenPalettePopupContent?.LaserPenColorThemeSwitch;
        internal Image LaserPenColorThemeSwitchIcon => PenPalettePopupContent?.LaserPenColorThemeSwitchIcon ?? BoardPenPalettePopupContent?.LaserPenColorThemeSwitchIcon;
        internal TextBlock LaserPenColorThemeSwitchTextBlock => PenPalettePopupContent?.LaserPenColorThemeSwitchText ?? BoardPenPalettePopupContent?.LaserPenColorThemeSwitchText;
        internal Border BoardLaserPenColorThemeSwitch => BoardPenPalettePopupContent?.LaserPenColorThemeSwitch;
        internal Image BoardLaserPenColorThemeSwitchIcon => BoardPenPalettePopupContent?.LaserPenColorThemeSwitchIcon;
        internal TextBlock BoardLaserPenColorThemeSwitchTextBlock => BoardPenPalettePopupContent?.LaserPenColorThemeSwitchText;

        internal FrameworkElement NibModeSimpleStackPanel => PenPalettePopupContent?.NibModePanel ?? BoardPenPalettePopupContent?.NibModePanel;
        internal FrameworkElement BoardNibModeSimpleStackPanel => BoardPenPalettePopupContent?.NibModePanel;
        #endregion

        #region Eraser property mappings
        internal ComboBox ComboBoxEraserSizeFloatingBar => EraserPopupContent?.EraserSizeComboBox ?? BoardEraserPopupContent?.EraserSizeComboBox;
        internal ComboBox BoardComboBoxEraserSize => BoardEraserPopupContent?.EraserSizeComboBox;
        internal TabControl EraserTypeTab => EraserPopupContent?.EraserTypeTab ?? BoardEraserPopupContent?.EraserTypeTab;
        internal TabControl BoardEraserTypeTab => BoardEraserPopupContent?.EraserTypeTab;
        #endregion

        #region Gesture property mappings
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch ToggleSwitchEnableMultiTouchMode => FloatingBarGesturePopupContent?.MultiTouchToggle ?? BoardGesturePopupContent?.MultiTouchToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch BoardToggleSwitchEnableMultiTouchMode => BoardGesturePopupContent?.MultiTouchToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch ToggleSwitchEnableTwoFingerTranslate => FloatingBarGesturePopupContent?.TwoFingerTranslateToggle ?? BoardGesturePopupContent?.TwoFingerTranslateToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch BoardToggleSwitchEnableTwoFingerTranslate => BoardGesturePopupContent?.TwoFingerTranslateToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch ToggleSwitchEnableTwoFingerZoom => FloatingBarGesturePopupContent?.TwoFingerZoomToggle ?? BoardGesturePopupContent?.TwoFingerZoomToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch BoardToggleSwitchEnableTwoFingerZoom => BoardGesturePopupContent?.TwoFingerZoomToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch ToggleSwitchEnableTwoFingerRotation => FloatingBarGesturePopupContent?.TwoFingerRotationToggle ?? BoardGesturePopupContent?.TwoFingerRotationToggle;
        internal iNKORE.UI.WPF.Modern.Controls.ToggleSwitch BoardToggleSwitchEnableTwoFingerRotation => BoardGesturePopupContent?.TwoFingerRotationToggle;
        internal FrameworkElement TwoFingerGestureSimpleStackPanel => FloatingBarGesturePopupContent?.TwoFingerGestureSimpleStackPanel;
        #endregion

        #region BackgroundPalette property mappings
        internal Slider BackgroundRSlider => BackgroundPalettePopupContent?.RSlider;
        internal Slider BackgroundGSlider => BackgroundPalettePopupContent?.GSlider;
        internal Slider BackgroundBSlider => BackgroundPalettePopupContent?.BSlider;
        internal TextBlock BackgroundRValue => BackgroundPalettePopupContent?.RValue;
        internal TextBlock BackgroundGValue => BackgroundPalettePopupContent?.GValue;
        internal TextBlock BackgroundBValue => BackgroundPalettePopupContent?.BValue;
        internal Border BackgroundColorPreview => BackgroundPalettePopupContent?.ColorPreview;
        internal Button ApplyBackgroundColorBtn => BackgroundPalettePopupContent?.ApplyBtn;
        internal Border WhiteboardModeBtn => BackgroundPalettePopupContent?.WhiteboardBtn;
        internal Border BlackboardModeBtn => BackgroundPalettePopupContent?.BlackboardBtn;
        internal Border DarkModeBtn => BackgroundPalettePopupContent?.DarkModeBtnControl;
        #endregion

        #region QuickColorPalette property mappings
        private QuickColorPaletteControl _quickColorPalette;

        internal QuickColorPaletteControl QuickColorPalette
        {
            get
            {
                if (_quickColorPalette != null) return _quickColorPalette;
                if (ToolbarHost != null)
                {
                    _quickColorPalette = ToolbarHost.FindView("builtin.quickColorPalette") as QuickColorPaletteControl;
                    if (_quickColorPalette != null) return _quickColorPalette;
                }
                if (StackPanelFloatingBarRoot != null)
                {
                    _quickColorPalette = FindDescendant<QuickColorPaletteControl>(StackPanelFloatingBarRoot);
                }
                return _quickColorPalette;
            }
        }

        private static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            var childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var descendant = FindDescendant<T>(child);
                if (descendant != null) return descendant;
            }
            return null;
        }

        /// <summary>
        /// 白板工具栏内嵌的快捷调色盘（board.colorPalette 组件）
        /// </summary>
        private QuickColorPaletteControl _boardQuickColorPalette;

        internal QuickColorPaletteControl BoardQuickColorPalette
        {
            get
            {
                if (_boardQuickColorPalette != null) return _boardQuickColorPalette;
                _boardQuickColorPalette = FindView("board.colorPalette") as QuickColorPaletteControl;
                return _boardQuickColorPalette;
            }
        }

        /// <summary>
        /// 白板工具栏构建调色盘组件时回写引用，供颜色指示器联动
        /// </summary>
        internal void SetBoardQuickColorPalette(QuickColorPaletteControl control)
        {
            _boardQuickColorPalette = control;

            // 白板工具栏可能在颜色状态初始化之后才构建，这里补一次勾选同步，
            // 保证新建的调色盘立刻反映当前画笔颜色。
            if (control == null || inkCanvas == null) return;
            int tolerance = (penType == 1) ? 25 : 15;
            control.ClearAllChecked();
            control.SetCheckedByColor(inkCanvas.DefaultDrawingAttributes.Color, tolerance);
        }
        #endregion

        internal void InitializeToolbarPlugins()
        {
            try
            {
                ToolbarRegistry.EnsureDefaultConfigExists();
                ToolbarHost = new ToolbarHost(this);
                var layout = ToolbarRegistry.LoadActiveConfig();

                // 根据设置确定工具栏方向
                var position = Settings.Appearance.ToolbarPosition;
                var orientation = (position == ToolbarPosition.Top || position == ToolbarPosition.Bottom)
                    ? Orientation.Vertical
                    : Orientation.Horizontal;

                // 设置根面板的方向和尺寸
                if (StackPanelFloatingBarRoot != null)
                {
                    StackPanelFloatingBarRoot.Orientation = orientation;
                    UpdateToolbarDimensions(orientation);
                }

                // 填充工具栏组件
                ToolbarRegistry.Populate(ToolbarHost, StackPanelFloatingBarRoot, layout, orientation);

                // 根据位置设置拖动图标的位置
                SetToolbarHeadPosition(position);

                ApplyHideFloatingBarBorder(Settings.Appearance.HideFloatingBarBorder);
                ApplyFloatingBarBorderColor();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"MW_Toolbar: InitializeToolbarPlugins 异常: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", LogHelper.LogType.Error);
            }
        }

        private void SetToolbarHeadPosition(ToolbarPosition position)
        {
            if (FloatingBarRootPanel == null) return;

            var rootChildren = FloatingBarRootPanel.Children;
            var rootList = rootChildren.OfType<FrameworkElement>().ToList();
            var dragElement = FindDragHandleInRoot();
            var otherElements = rootList.Where(c => c != dragElement).ToList();

            rootChildren.Clear();

            var reverseContent = Settings.Appearance.ReverseToolbarContent;

            switch (position)
            {
                case ToolbarPosition.Right:
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(0);
                        rootChildren.Add(dragElement);
                    }
                    foreach (var elem in otherElements)
                    {
                        rootChildren.Add(elem);
                    }
                    // 根据用户设置决定是否翻转内容面板
                    if (reverseContent)
                        ReverseAllContentPanels();
                    else
                        RestoreAllContentPanels();
                    isFloatingBarHeadOnRight = false;
                    isFloatingBarHeadOnBottom = false;
                    break;

                case ToolbarPosition.Left:
                    foreach (var elem in otherElements.AsEnumerable().Reverse())
                    {
                        rootChildren.Add(elem);
                    }
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(3, 0, 0, 0);
                        rootChildren.Add(dragElement);
                    }
                    // 根据用户设置决定是否翻转内容面板（注意：这里默认是翻转的，所以用户设置要反过来）
                    if (reverseContent)
                        RestoreAllContentPanels();
                    else
                        ReverseAllContentPanels();
                    isFloatingBarHeadOnRight = true;
                    isFloatingBarHeadOnBottom = false;
                    break;

                case ToolbarPosition.Top:
                    foreach (var elem in otherElements.AsEnumerable().Reverse())
                    {
                        rootChildren.Add(elem);
                    }
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(0, 3, 0, 0);
                        rootChildren.Add(dragElement);
                    }
                    // 根据用户设置决定是否翻转内容面板（注意：这里默认是翻转的，所以用户设置要反过来）
                    if (reverseContent)
                        RestoreAllContentPanels();
                    else
                        ReverseAllContentPanels();
                    isFloatingBarHeadOnRight = false;
                    isFloatingBarHeadOnBottom = true;
                    break;

                case ToolbarPosition.Bottom:
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(0);
                        rootChildren.Add(dragElement);
                    }
                    foreach (var elem in otherElements)
                    {
                        rootChildren.Add(elem);
                    }
                    // 根据用户设置决定是否翻转内容面板
                    if (reverseContent)
                        ReverseAllContentPanels();
                    else
                        RestoreAllContentPanels();
                    isFloatingBarHeadOnRight = false;
                    isFloatingBarHeadOnBottom = false;
                    break;
            }

            SetFloatingBarHighlightPosition(_currentToolMode);
        }

        internal void RebuildToolbar()
        {
            LogHelper.WriteLogToFile("MW_Toolbar: RebuildToolbar 开始", LogHelper.LogType.Info);
            try
            {
                _lastHighlightButton = null;
                _quickColorPalette = null;
                ToolbarRegistry.ClearInjected(StackPanelFloatingBarRoot);
                InitializeToolbarPlugins();
                UpdateToolbarComponentVisibility();
                ApplyFloatingBarIconHighlightImmediate(_currentToolMode);
                RefreshFloatingBarButtonColors();
                RefreshGestureButtonIcon();
                SetFloatingBarHighlightPosition(_currentToolMode);
                ApplyCompactFloatingBarMode(Settings.Appearance.CompactFloatingBar);
                ApplyHideFloatingBarBorder(Settings.Appearance.HideFloatingBarBorder);
                ApplyFloatingBarBorderColor();
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    UpdateQuickColorPaletteIndicator(inkCanvas.DefaultDrawingAttributes.Color);
                }));

                // 布局完成后再按「显示比例 + 屏幕可用空间」统一校准一次所有工具栏的缩放
                // （启动早期控件还没测量出尺寸且窗口可能尚未连接到呈现源，此处补齐）
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => ApplyAllToolbarDisplayScale()));
                LogHelper.WriteLogToFile("MW_Toolbar: RebuildToolbar 完成", LogHelper.LogType.Info);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"MW_Toolbar: RebuildToolbar 异常: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 插件注册/注销后重建浮动与白板两套插件工具栏（调用方通常经 Dispatcher 排队调用）。
        /// </summary>
        internal void RebuildPluginToolbars()
        {
            RebuildToolbar();
            RebuildBoardToolbar();
        }

        /// <summary>
        /// 紧凑模式浮动栏整体缩放倍率（相对用户设置的倍率再缩小至此比例，保持纵横比）。
        /// </summary>
        public const double CompactFloatingBarScaleFactor = 0.85;

        /// <summary>
        /// 应用紧凑浮动栏模式：遍历浮动栏中的所有 ToolbarImageButton，
        /// 开启时隐藏常驻文字标签并让图标按纵横比拉伸填满，同时整体等比缩小，关闭时恢复默认外观。
        /// </summary>
        internal void ApplyCompactFloatingBarMode(bool compact)
        {
            if (StackPanelFloatingBarRoot == null) return;
            foreach (var btn in FindVisualChildren<Controls.ToolbarImageButton>(StackPanelFloatingBarRoot))
            {
                btn.ApplyCompactMode(compact);
            }

            // 紧凑模式在用户设置的倍率上再等比缩小
            _floatingBarCompactFactor = compact ? CompactFloatingBarScaleFactor : 1.0;
            ApplyEffectiveFloatingBarScale();
        }

        /// <summary>当前生效的紧凑模式缩放系数（1 表示未启用紧凑模式）。</summary>
        private double _floatingBarCompactFactor = 1.0;

        /// <summary>
        /// 按「用户设置 × 紧凑系数 × 显示比例补偿 × 自适应适配」重新计算并应用浮动工具栏缩放。
        /// 自适应适配会参考当前屏幕可用空间（受分辨率与系统显示比例影响），
        /// 避免高显示比例 / 低分辨率下工具栏超出屏幕、与白板底部的左右按钮重叠。
        /// </summary>
        internal void ApplyEffectiveFloatingBarScale()
        {
            if (ViewboxFloatingBarScaleTransform == null) return;

            double userScale = Settings.Appearance.ViewboxFloatingBarScaleTransformValue;
            if (userScale <= 0 || double.IsNaN(userScale) || double.IsInfinity(userScale)) userScale = 1.0;

            // 与设置页滑块的有效范围保持一致：0.5x ~ 1.25x
            userScale = userScale <= 0.5 ? 0.5 : userScale >= 1.25 ? 1.25 : userScale;

            ApplyFloatingBarScaleWithAutoFit(userScale * _floatingBarCompactFactor);
        }

        /// <summary>
        /// 在给定目标缩放上叠加「显示比例补偿」与「自适应适配」系数后应用（不改动 Settings 中的用户值）。
        /// </summary>
        internal void ApplyFloatingBarScaleWithAutoFit(double targetScale)
        {
            if (ViewboxFloatingBarScaleTransform == null) return;
            if (targetScale <= 0 || double.IsNaN(targetScale) || double.IsInfinity(targetScale)) return;

            // 先按系统显示比例补偿，再按屏幕可用空间自适应
            var displayScale = targetScale * GetToolbarDisplayFitFactor();
            var finalScale = displayScale * ComputeFloatingBarAutoFitScale(displayScale);

            // 缩放没有变化时不要重复触发定位动画，避免抖动
            if (Math.Abs(ViewboxFloatingBarScaleTransform.ScaleX - finalScale) < 0.001 &&
                Math.Abs(ViewboxFloatingBarScaleTransform.ScaleY - finalScale) < 0.001)
                return;

            ApplyRawFloatingBarScale(finalScale);
        }

        #region 显示比例（DPI）自适应缩放

        /// <summary>工具栏的“设计宽度”（DIP）。可用宽度达到它时按 1.0 原尺寸显示。</summary>
        private const double ToolbarDesignWidth = 1920.0;

        /// <summary>工具栏缩放下限，避免小屏/超高缩放下缩得无法点击。</summary>
        private const double MinToolbarDisplayFitFactor = 0.667;

        /// <summary>
        /// 当前主窗口所在显示器的系统显示比例（1.0 = 100%，1.5 = 150%）。
        /// </summary>
        internal double GetDisplayDpiScale(double? dpiScaleOverride = null)
        {
            if (dpiScaleOverride.HasValue && dpiScaleOverride.Value > 0 &&
                !double.IsNaN(dpiScaleOverride.Value) && !double.IsInfinity(dpiScaleOverride.Value))
                return dpiScaleOverride.Value;

            try
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                var scale = dpi.DpiScaleX;
                if (scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale)) return scale;
            }
            catch
            {
            }

            return 1.0;
        }

        /// <summary>
        /// 计算工具栏缩放系数（按「可用宽度」而不是按显示比例）：
        /// <list type="bullet">
        /// <item>可用宽度 ≥ 1920 DIP（游戏本 1080p@100%、2K@125%、4K@200% 等）→ 1.0，完全不缩；</item>
        /// <item>可用宽度不足 1920 DIP 时按比例缩小：1080p@150% = 1280 DIP → 0.667，
        ///       1080p@125% = 1536 DIP → 0.8，1366@100% = 1366 DIP → 0.71；</item>
        /// <item>下限 0.667，避免小屏 + 高缩放下按钮小到点不中。</item>
        /// </list>
        /// WPF 以 DIP 布局，工具栏是固定 DIP 尺寸，真正决定会不会挤在一起的是「可用 DIP 宽度」，
        /// 而不是显示比例本身——4K@200% 和 1080p@100% 的可用宽度都是 1920 DIP，都不需要缩。
        /// </summary>
        internal double GetToolbarDisplayFitFactor(double? dpiScaleOverride = null)
        {
            var available = GetToolbarAvailableWidth();
            if (available <= 0) return 1.0;

            var factor = available / ToolbarDesignWidth;
            if (factor >= 1.0) return 1.0;
            if (factor < MinToolbarDisplayFitFactor) factor = MinToolbarDisplayFitFactor;

            return factor;
        }

        /// <summary>当前可用的 DIP 宽度（优先用画布宽度，取不到时退回窗口宽度）</summary>
        private double GetToolbarAvailableWidth()
        {
            try
            {
                if (inkCanvas != null && inkCanvas.ActualWidth > 0) return inkCanvas.ActualWidth;
                if (ActualWidth > 0) return ActualWidth;
            }
            catch
            {
            }

            return 0;
        }

        /// <summary>
        /// 按「用户设置 × 显示比例补偿」重新应用白板三栏（左/中/右）的缩放。
        /// 三栏内的按钮与图标随之等比缩小，避免 150% 显示比例下三栏与浮动栏互相重叠。
        /// </summary>
        internal void ApplyBoardToolbarDisplayScale(double? dpiScaleOverride = null)
        {
            try
            {
                if (Settings?.Appearance == null) return;

                var factor = GetToolbarDisplayFitFactor(dpiScaleOverride);
                ApplyBoardBarScale(ViewboxBlackboardLeftSideScaleTransform,
                    Settings.Appearance.ViewboxBlackBoardLeftScaleTransformValue, factor);
                ApplyBoardBarScale(ViewboxBlackboardCenterSideScaleTransform,
                    Settings.Appearance.ViewboxBlackBoardScaleTransformValue, factor);
                ApplyBoardBarScale(ViewboxBlackboardRightSideScaleTransform,
                    Settings.Appearance.ViewboxBlackBoardRightScaleTransformValue, factor);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用白板工具栏显示比例缩放失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private static void ApplyBoardBarScale(System.Windows.Media.ScaleTransform transform, double userScale, double factor)
        {
            if (transform == null) return;
            if (userScale <= 0 || double.IsNaN(userScale) || double.IsInfinity(userScale)) userScale = 1.0;

            var scale = userScale * factor;
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }

        /// <summary>上一次记录的显示比例缩放系数，仅用于避免重复写日志。</summary>
        private double _lastLoggedToolbarDisplayFactor = -1;

        /// <summary>上一次应用工具栏缩放时的窗口宽度（DIP），用于多显示器/改分辨率后判断是否需要重算</summary>
        private double _lastToolbarScaleWidth = -1;

        /// <summary>
        /// 按当前显示比例统一刷新所有工具栏（浮动工具栏、白板三栏、PPT 翻页栏）的缩放。
        /// 启动、显示比例变化、分辨率变化后都应调用一次。
        /// </summary>
        internal void ApplyAllToolbarDisplayScale(double? dpiScaleOverride = null)
        {
            var dpiScale = GetDisplayDpiScale(dpiScaleOverride);
            var factor = GetToolbarDisplayFitFactor(dpiScaleOverride);
            if (Math.Abs(factor - _lastLoggedToolbarDisplayFactor) > 0.0001)
            {
                _lastLoggedToolbarDisplayFactor = factor;
                LogHelper.WriteLogToFile($"工具栏按显示比例缩放: 显示比例={dpiScale * 100:0}% 缩放系数={factor:0.###}",
                    LogHelper.LogType.Info);
            }

            ApplyBoardToolbarDisplayScale(dpiScaleOverride);
            ApplyEffectiveFloatingBarScale();

            try
            {
                PPTUIManager?.UpdateNavigationButtonStyles();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用 PPT 翻页栏显示比例缩放失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        #endregion

        /// <summary>
        /// 直接设置浮动栏 ScaleTransform 的绝对值，不经过 Settings 保存。
        /// </summary>
        private void ApplyRawFloatingBarScale(double scale)
        {
            if (ViewboxFloatingBarScaleTransform == null) return;
            _userHasDraggedFloatingBar = false;
            pointDesktop = new Point(-1, -1);
            pointPPT = new Point(-1, -1);
            ViewboxFloatingBarScaleTransform.ScaleX = scale;
            ViewboxFloatingBarScaleTransform.ScaleY = scale;
            if (IsInPPTPresentationMode)
                ViewboxFloatingBarMarginAnimation(60);
            else
                ViewboxFloatingBarMarginAnimation(100, true);
        }

        /// <summary>
        /// 缓存浮动栏各 Border 的原始边框厚度，用于关闭无白边后恢复。
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<System.Windows.Controls.Border, System.Windows.Thickness> _floatingBarBorderThicknessCache
            = new System.Collections.Generic.Dictionary<System.Windows.Controls.Border, System.Windows.Thickness>();

        /// <summary>
        /// 应用隐藏浮动栏边框：开启时只隐藏浮动栏外层容器边框（实现无白边），
        /// 关闭时从缓存恢复各容器 Border 原始厚度。
        /// </summary>
        internal void ApplyHideFloatingBarBorder(bool hide)
        {
            if (StackPanelFloatingBarRoot == null) return;
            foreach (var border in FindVisualChildren<System.Windows.Controls.Border>(StackPanelFloatingBarRoot))
            {
                if (border.Tag as string != Controls.Toolbar.FloatingToolbar.ToolbarRegistry.ContentBorderTag && border != BorderFloatingBarMoveControls) continue;

                if (hide)
                {
                    if (!_floatingBarBorderThicknessCache.ContainsKey(border))
                        _floatingBarBorderThicknessCache[border] = border.BorderThickness;
                    border.BorderThickness = new System.Windows.Thickness(0);
                }
                else if (_floatingBarBorderThicknessCache.TryGetValue(border, out var original))
                {
                    border.BorderThickness = original;
                }
            }
        }

        /// <summary>
        /// 边框颜色模式：0=默认（主题色），1=跟随背景颜色，2=自定义。
        /// </summary>
        private const int BorderColorMode_Default = 0;
        private const int BorderColorMode_FollowBackground = 1;
        private const int BorderColorMode_Custom = 2;

        /// <summary>
        /// 标记是否曾应用过非默认边框颜色，用于判断"默认"模式下是否需要恢复。
        /// 从未修改过时保持 false，"默认"模式完全不动 BorderBrush，与修改前行为完全一致。
        /// </summary>
        private bool _hasAppliedNonDefaultFloatingBarBorderColor = false;

        /// <summary>
        /// 应用浮动栏边框颜色：根据模式设置 BorderBrush。
        /// - 默认：若从未应用过非默认色则完全不操作（与修改前一致），否则恢复主题资源绑定 FloatBarBorderBrush
        /// - 跟随背景颜色：绑定 FloatBarBackground（亮色白/暗色深色），与背景融为一体
        /// - 自定义：直接使用用户保存的 hex 颜色
        /// 仅作用于浮动栏拖动图标 Border 及 ToolbarRegistry 创建的内容 Border，与 ApplyHideFloatingBarBorder 作用域一致。
        /// </summary>
        internal void ApplyFloatingBarBorderColor()
        {
            if (StackPanelFloatingBarRoot == null) return;

            int mode = Settings.Appearance.FloatingBarBorderColorMode;
            if (mode < 0 || mode > 2) mode = BorderColorMode_Default;

            // 默认模式下，若从未应用过非默认色，完全不操作 BorderBrush，保持修改前的原始行为
            if (mode == BorderColorMode_Default && !_hasAppliedNonDefaultFloatingBarBorderColor) return;

            _hasAppliedNonDefaultFloatingBarBorderColor = (mode != BorderColorMode_Default);

            foreach (var border in FindVisualChildren<System.Windows.Controls.Border>(StackPanelFloatingBarRoot))
            {
                if (border.Tag as string != Controls.Toolbar.FloatingToolbar.ToolbarRegistry.ContentBorderTag
                    && border != BorderFloatingBarMoveControls) continue;

                switch (mode)
                {
                    case BorderColorMode_FollowBackground:
                        border.SetResourceReference(
                            System.Windows.Controls.Border.BorderBrushProperty, "FloatingBarBackgroundBrush");
                        break;
                    case BorderColorMode_Custom:
                        var customColor = TryParseFloatingBarBorderColor(Settings.Appearance.FloatingBarBorderColor);
                        if (customColor.HasValue)
                            border.BorderBrush = new System.Windows.Media.SolidColorBrush(customColor.Value);
                        else
                            border.SetResourceReference(
                                System.Windows.Controls.Border.BorderBrushProperty, "FloatingBarBorderBrush");
                        break;
                    default:
                        border.SetResourceReference(
                            System.Windows.Controls.Border.BorderBrushProperty, "FloatingBarBorderBrush");
                        break;
                }
            }
        }

        /// <summary>
        /// 解析用户保存的浮动栏边框颜色 hex 字符串，失败返回 null。
        /// </summary>
        private static System.Windows.Media.Color? TryParseFloatingBarBorderColor(string saved)
        {
            if (string.IsNullOrWhiteSpace(saved)) return null;
            try
            {
                var text = saved.Trim();
                if (text.StartsWith("#")) text = text.Substring(1);
                if (text.Length == 6)
                    text = "FF" + text;
                return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#" + text);
            }
            catch
            {
                return null;
            }
        }

        internal bool IsAnnotating => _currentToolMode != "cursor";

        internal void UpdateToolbarComponentVisibility()
        {
            var isPPT = IsInPPTPresentationMode;
            ToolbarRegistry.UpdateVisibilityByMode(StackPanelFloatingBarRoot, IsAnnotating, isPPT);

            // 条目显隐会改变工具栏整体宽度，布局完成后按屏幕可用空间重新校准缩放
            // （缩放无变化时内部会直接返回，不会重复触发定位动画）
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(ApplyEffectiveFloatingBarScale));
        }

        private void UpdateToolbarDimensions(Orientation orientation)
        {
            if (StackPanelFloatingBarRoot == null) return;

            if (orientation == Orientation.Horizontal)
            {
                // 左右位置：高度固定，宽度自动
                StackPanelFloatingBarRoot.MaxHeight = 58;
                StackPanelFloatingBarRoot.ClearValue(System.Windows.FrameworkElement.MaxWidthProperty);
            }
            else
            {
                // 上下位置：宽度固定，高度自动
                StackPanelFloatingBarRoot.MaxWidth = 58;
                StackPanelFloatingBarRoot.ClearValue(System.Windows.FrameworkElement.MaxHeightProperty);
            }
        }
    }
}
