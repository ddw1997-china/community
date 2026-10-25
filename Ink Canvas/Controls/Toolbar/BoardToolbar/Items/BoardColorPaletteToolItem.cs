using Ink_Canvas.Properties;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Windows;
using System.Windows.Controls;

namespace Ink_Canvas.Controls.Toolbar.BoardToolbar.Items
{
    /// <summary>
    /// 白板工具栏"颜色选择"组件：内嵌与批注模式浮动栏相同的快捷调色盘
    /// （<see cref="QuickColorPaletteControl"/>），默认排在画笔按钮右侧，
    /// 选色后切换当前画笔颜色（含指示器与画笔图标联动）。
    /// </summary>
    internal sealed class BoardColorPaletteToolItem : IBoardToolbarItem
    {
        public string Id => "board.colorPalette";

        public string DisplayName => FloatingBarStrings.FloatingBar_QuickPaletteMode;

        public string Description => FloatingBarStrings.ToolbarItem_Desc_QuickColorPalette;

        public string IconGeometry => XamlGraphicsIconGeometries.ColorPaletteIconGeometry;

        public FontIconData? IconKey => null;

        public ButtonPosition DefaultPosition => ButtonPosition.Middle;

        public FrameworkElement BuildView(IBoardToolbarHost host)
        {
            var control = new QuickColorPaletteControl();
            control.ColorClicked += (s, e) =>
            {
                if (e.OriginalSource is string colorName)
                {
                    host.Window.ApplyQuickColorByName(colorName);
                }
            };
            host.Window.SetBoardQuickColorPalette(control);
            return control;
        }

        public void ApplyPosition(FrameworkElement view, ButtonPosition position)
        {
            // 内嵌调色盘不是按钮，无首/中/尾按钮样式需求
        }
    }
}
