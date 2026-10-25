using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ink_Canvas.Controls
{
    public partial class GeometryButton : UserControl
    {
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(GeometryButton),
            new PropertyMetadata(string.Empty, OnLabelChanged));

        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = (GeometryButton)d;
            if (button.LabelControl != null)
                button.LabelControl.Content = (string)e.NewValue;
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(
            nameof(IconSource), typeof(ImageSource), typeof(GeometryButton),
            new PropertyMetadata(null, OnIconSourceChanged));

        private static void OnIconSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = (GeometryButton)d;
            if (button.ButtonImage != null)
                button.ButtonImage.Source = (ImageSource)e.NewValue;
        }

        public ImageSource IconSource
        {
            get => (ImageSource)GetValue(IconSourceProperty);
            set => SetValue(IconSourceProperty, value);
        }

        public static readonly DependencyProperty IconRotationProperty = DependencyProperty.Register(
            nameof(IconRotation), typeof(double), typeof(GeometryButton),
            new PropertyMetadata(0.0, OnIconRotationChanged));

        private static void OnIconRotationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = (GeometryButton)d;
            if (button.ButtonImage != null)
            {
                var angle = (double)e.NewValue;
                if (angle != 0)
                {
                    button.ButtonImage.RenderTransformOrigin = new Point(0.5, 0.5);
                    button.ButtonImage.RenderTransform = new RotateTransform(angle);
                }
                else
                {
                    button.ButtonImage.RenderTransform = Transform.Identity;
                }
            }
        }

        public double IconRotation
        {
            get => (double)GetValue(IconRotationProperty);
            set => SetValue(IconRotationProperty, value);
        }

        /// <summary>
        /// 是否处于选中状态（批注模式下几何工具被选中后会一直保持，直到点击其它按钮）
        /// </summary>
        public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(
            nameof(IsChecked), typeof(bool), typeof(GeometryButton),
            new PropertyMetadata(false, OnIsCheckedChanged));

        private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = (GeometryButton)d;
            button.UpdateVisualState();
        }

        public bool IsChecked
        {
            get => (bool)GetValue(IsCheckedProperty);
            set => SetValue(IsCheckedProperty, value);
        }

        /// <summary>
        /// 选中时的边框画刷
        /// </summary>
        public static readonly DependencyProperty CheckedBorderBrushProperty = DependencyProperty.Register(
            nameof(CheckedBorderBrush), typeof(Brush), typeof(GeometryButton),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)), OnCheckedAppearanceChanged));

        public Brush CheckedBorderBrush
        {
            get => (Brush)GetValue(CheckedBorderBrushProperty);
            set => SetValue(CheckedBorderBrushProperty, value);
        }

        /// <summary>
        /// 选中时的背景画刷
        /// </summary>
        public static readonly DependencyProperty CheckedBackgroundProperty = DependencyProperty.Register(
            nameof(CheckedBackground), typeof(Brush), typeof(GeometryButton),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x30, 0x33, 0x99, 0xFF)), OnCheckedAppearanceChanged));

        public Brush CheckedBackground
        {
            get => (Brush)GetValue(CheckedBackgroundProperty);
            set => SetValue(CheckedBackgroundProperty, value);
        }

        private static void OnCheckedAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = (GeometryButton)d;
            button.UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            if (ButtonBorder == null) return;

            if (IsChecked)
            {
                ButtonBorder.BorderBrush = CheckedBorderBrush;
                ButtonBorder.Background = CheckedBackground;
            }
            else
            {
                ButtonBorder.BorderBrush = Brushes.Transparent;
                ButtonBorder.Background = Brushes.Transparent;
            }
        }

        public event MouseButtonEventHandler ButtonMouseDown;
        public event MouseButtonEventHandler ButtonMouseUp;

        public GeometryButton()
        {
            InitializeComponent();
        }

        private void ButtonPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ButtonMouseDown?.Invoke(this, e);
        }

        private void ButtonPanel_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ButtonMouseUp?.Invoke(this, e);
        }
    }
}
