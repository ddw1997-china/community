using System.Windows.Input;

namespace Ink_Canvas.Controls.Toolbar.BoardToolbar.Items
{
    /// <summary>
    /// 白板工具栏「清空」按钮：清空当前页的墨迹与元素。
    /// 复用浮动栏清屏逻辑，因此同样会进入时间机器历史（可撤销）并遵守自动截图等设置。
    /// </summary>
    internal sealed class BoardClearToolItem : BoardToolbarImageButtonItemBase
    {
        public override string Id => "board.clear";
        public override string LocalizationKey => "MiniWhiteboard_Clear";
        public override string Description => "清空画布";

        public override string IconGeometry => XamlGraphicsIconGeometries.DeleteIcon;

        // 破坏性操作：图标与文字统一使用红色
        protected override string IconBrushResourceKey => "RedBrush";
        protected override string LabelBrushResourceKey => "RedBrush";

        protected override void OnClick(IBoardToolbarHost host, object sender, MouseButtonEventArgs e)
            => host.Window.SymbolIconDelete_MouseUp(null, e);
    }
}
