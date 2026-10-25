using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ink_Canvas.Controls
{
    public partial class QuickColorPaletteControl : System.Windows.Controls.UserControl, INotifyPropertyChanged
    {
        public static readonly RoutedEvent ColorClickedEvent = EventManager.RegisterRoutedEvent(
            nameof(ColorClicked), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(QuickColorPaletteControl));

        public event RoutedEventHandler ColorClicked
        {
            add => AddHandler(ColorClickedEvent, value);
            remove => RemoveHandler(ColorClickedEvent, value);
        }

        public static readonly DependencyProperty DisplayModeProperty =
            DependencyProperty.Register(nameof(DisplayMode), typeof(int), typeof(QuickColorPaletteControl),
                new PropertyMetadata(1, OnDisplayModeChanged));

        public int DisplayMode
        {
            get => (int)GetValue(DisplayModeProperty);
            set => SetValue(DisplayModeProperty, value);
        }

        private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (QuickColorPaletteControl)d;
            control.ApplyDisplayMode();
        }

        public QuickColorPaletteControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyDisplayMode();
        }

        private void ApplyDisplayMode()
        {
            if (QuickColorPalettePanel == null || QuickColorPaletteSingleRowPanel == null || QuickColorPaletteContainer == null) return;

            if (DisplayMode == 0)
            {
                QuickColorPalettePanel.Visibility = Visibility.Collapsed;
                QuickColorPaletteSingleRowPanel.Visibility = Visibility.Visible;
            }
            else
            {
                QuickColorPalettePanel.Visibility = Visibility.Visible;
                QuickColorPaletteSingleRowPanel.Visibility = Visibility.Collapsed;
            }

            QuickColorPaletteContainer.Visibility = Visibility.Visible;
        }

        public void SyncFromSettings()
        {
            var settings = SettingsManager.Settings;
            if (settings?.Appearance == null) return;
            DisplayMode = settings.Appearance.QuickColorPaletteDisplayMode;
        }

        /// <summary>
        /// 强制应用显示模式，确保即使在控件初始化期间也能正确显示
        /// </summary>
        public void ForceApplyDisplayMode()
        {
            if (IsLoaded)
            {
                ApplyDisplayMode();
            }
            else
            {
                void handler(object s, RoutedEventArgs args)
                {
                    Loaded -= handler;
                    ApplyDisplayMode();
                }
                Loaded += handler;
            }
        }

        /// <summary>用户最后点击的色块，用于颜色匹配失败时兜底显示勾选。</summary>
        private ColorPickerButton _lastClickedButton;

        private ColorPickerButton[] AllColorButtons => new[]
        {
            QuickColorBlack, QuickColorWhite, QuickColorRed, QuickColorOrange,
            QuickColorYellow, QuickColorGreen, QuickColorBlue, QuickColorPurple,
            QuickColorBlackSingle, QuickColorWhiteSingle, QuickColorRedSingle,
            QuickColorOrangeSingle, QuickColorYellowSingle, QuickColorGreenSingle
        };

        public void ClearAllChecked()
        {
            foreach (var button in AllColorButtons)
            {
                if (button != null) button.IsChecked = false;
            }
        }

        private bool IsAnyChecked()
        {
            foreach (var button in AllColorButtons)
            {
                if (button != null && button.IsChecked) return true;
            }
            return false;
        }

        /// <summary>
        /// 按墨水颜色索引（0 黑、1 红、2 绿、3 蓝、4 黄、5 白、6 紫/粉、7 青、8 橙）更新勾选。
        /// 颜色索引是画笔状态的权威来源，可避开主题化 RGB 与预设色不完全一致导致的匹配失败。
        /// </summary>
        public void SetCheckedByIndex(int index)
        {
            ClearAllChecked();

            switch (index)
            {
                case 0:
                    QuickColorBlack.IsChecked = true;
                    QuickColorBlackSingle.IsChecked = true;
                    return;
                case 1:
                    QuickColorRed.IsChecked = true;
                    QuickColorRedSingle.IsChecked = true;
                    return;
                case 2:
                    QuickColorGreen.IsChecked = true;
                    QuickColorGreenSingle.IsChecked = true;
                    return;
                case 3:
                    QuickColorBlue.IsChecked = true;
                    return;
                case 4:
                    QuickColorYellow.IsChecked = true;
                    QuickColorYellowSingle.IsChecked = true;
                    return;
                case 5:
                    QuickColorWhite.IsChecked = true;
                    QuickColorWhiteSingle.IsChecked = true;
                    return;
                case 6:
                    QuickColorPurple.IsChecked = true;
                    return;
                case 8:
                    QuickColorOrange.IsChecked = true;
                    QuickColorOrangeSingle.IsChecked = true;
                    return;
            }

            // 索引没有对应色块（例如青色）时，退回用户最后点击的色块，避免一个勾都没有。
            if (_lastClickedButton != null) _lastClickedButton.IsChecked = true;
        }

        public void SetCheckedByColor(Color color, int tolerance = 15)
        {
            if (IsColorSimilar(color, Colors.White, tolerance) || IsColorSimilar(color, Color.FromRgb(250, 250, 250), tolerance))
            {
                QuickColorWhite.IsChecked = true;
                QuickColorWhiteSingle.IsChecked = true;
            }
            else if (IsColorSimilar(color, Colors.Black, tolerance))
            {
                QuickColorBlack.IsChecked = true;
                QuickColorBlackSingle.IsChecked = true;
            }
            else if (IsColorSimilar(color, Colors.Yellow, tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(234, 179, 8), tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(250, 204, 21), tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(253, 224, 71), tolerance))
            {
                QuickColorYellow.IsChecked = true;
                QuickColorYellowSingle.IsChecked = true;
            }
            else if (IsColorSimilar(color, Color.FromRgb(255, 165, 0), tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(251, 150, 80), tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(249, 115, 22), tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(234, 88, 12), tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(251, 146, 60), tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(253, 126, 20), tolerance))
            {
                QuickColorOrange.IsChecked = true;
                QuickColorOrangeSingle.IsChecked = true;
            }
            else if (IsColorSimilar(color, Color.FromRgb(37, 99, 235), tolerance))
            {
                QuickColorBlue.IsChecked = true;
            }
            else if (IsColorSimilar(color, Colors.Red, tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(220, 38, 38), tolerance) ||
                     IsColorSimilar(color, Color.FromRgb(239, 68, 68), tolerance))
            {
                QuickColorRed.IsChecked = true;
                QuickColorRedSingle.IsChecked = true;
            }
            else if (IsColorSimilar(color, Color.FromRgb(22, 163, 74), tolerance))
            {
                QuickColorGreen.IsChecked = true;
                QuickColorGreenSingle.IsChecked = true;
            }
            else if (IsColorSimilar(color, Color.FromRgb(147, 51, 234), tolerance))
            {
                QuickColorPurple.IsChecked = true;
            }

            // 主题化后的颜色可能落在所有预设之外，退回用户最后点击的色块，避免一个勾都没有。
            if (!IsAnyChecked() && _lastClickedButton != null)
                _lastClickedButton.IsChecked = true;
        }

        private static bool IsColorSimilar(Color c1, Color c2, int tolerance)
        {
            return Math.Abs(c1.R - c2.R) <= tolerance &&
                   Math.Abs(c1.G - c2.G) <= tolerance &&
                   Math.Abs(c1.B - c2.B) <= tolerance;
        }

        private void QuickColorBlack_Click(object sender, RoutedEventArgs e)
            => SelectColor(sender, "Black");

        private void QuickColorWhite_Click(object sender, RoutedEventArgs e)
            => SelectColor(sender, "White");

        private void QuickColorRed_Click(object sender, RoutedEventArgs e)
            => SelectColor(sender, "Red");

        private void QuickColorOrange_Click(object sender, RoutedEventArgs e)
            => SelectColor(sender, "Orange");

        private void QuickColorYellow_Click(object sender, RoutedEventArgs e)
            => SelectColor(sender, "Yellow");

        private void QuickColorGreen_Click(object sender, RoutedEventArgs e)
            => SelectColor(sender, "Green");

        private void QuickColorBlue_Click(object sender, RoutedEventArgs e)
            => SelectColor(sender, "Blue");

        private void QuickColorPurple_Click(object sender, RoutedEventArgs e)
            => SelectColor(sender, "Purple");

        /// <summary>
        /// 点击色块时先就地更新勾选，再抛出颜色事件。
        /// 不依赖外部颜色状态刷新链路，避免上下两排色块出现「只有一排有勾」的问题。
        /// </summary>
        private void SelectColor(object sender, string colorName)
        {
            ClearAllChecked();
            _lastClickedButton = sender as ColorPickerButton;
            if (_lastClickedButton != null) _lastClickedButton.IsChecked = true;
            RaiseEvent(new RoutedEventArgs(ColorClickedEvent, colorName));
        }

        public void ApplyOrientation(bool isVertical)
        {
            if (QuickColorPaletteContainer == null) return;

            if (isVertical)
            {
                QuickColorPaletteContainer.Orientation = Orientation.Horizontal;
                QuickColorPalettePanel.Orientation = Orientation.Horizontal;
                if (Row1 != null)
                {
                    Row1.Orientation = Orientation.Vertical;
                    Row1.Margin = new Thickness(0, 0, 1, 0);
                }
                if (Row2 != null)
                {
                    Row2.Orientation = Orientation.Vertical;
                    Row2.Margin = new Thickness(1, 0, 0, 0);
                }
                QuickColorPaletteSingleRowPanel.Orientation = Orientation.Vertical;
            }
            else
            {
                QuickColorPaletteContainer.Orientation = Orientation.Vertical;
                QuickColorPalettePanel.Orientation = Orientation.Vertical;
                if (Row1 != null)
                {
                    Row1.Orientation = Orientation.Horizontal;
                    Row1.Margin = new Thickness(0, 0, 0, 1);
                }
                if (Row2 != null)
                {
                    Row2.Orientation = Orientation.Horizontal;
                    Row2.Margin = new Thickness(0, 1, 0, 0);
                }
                QuickColorPaletteSingleRowPanel.Orientation = Orientation.Horizontal;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
