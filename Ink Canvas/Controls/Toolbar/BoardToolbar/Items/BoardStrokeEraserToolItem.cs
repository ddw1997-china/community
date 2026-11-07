using System.Windows.Input;

namespace Ink_Canvas.Controls.Toolbar.BoardToolbar.Items
{
    internal sealed class BoardStrokeEraserToolItem : BoardToolbarImageButtonItemBase
    {
        public override string Id => "board.strokeEraser";
        public override string LocalizationKey => "Board_StrokeEraser";
        public override string Description => "笔画橡皮擦";

        public override string IconGeometry => XamlGraphicsIconGeometries.ScissorsIcon;

        protected override void OnClick(IBoardToolbarHost host, object sender, MouseButtonEventArgs e)
            => host.SelectStrokeEraser();

        protected override void AfterBuild(IBoardToolbarHost host, BoardToolbarButton view)
            => host.RegisterView(Id, view);
    }
}
