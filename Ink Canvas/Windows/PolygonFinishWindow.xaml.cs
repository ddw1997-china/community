using Ink_Canvas.Helpers;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Ink_Canvas
{
    /// <summary>
    /// 多边形绘制完成后的选项窗口：是否纠正为正多边形、是否显示内切圆。
    /// 与渐近线确认窗一致，自建窗口可强制置顶/前台并聚焦按钮，
    /// 解决白板/触控环境下首次点击被“窗口激活”吞掉、需多次点击的问题。
    /// </summary>
    public partial class PolygonFinishWindow : Window
    {
        /// <summary>是否把当前多边形纠正为正多边形。</summary>
        public bool RegularizeRequested { get; private set; }

        /// <summary>是否额外绘制内切圆。</summary>
        public bool ShowIncircleRequested { get; private set; }

        public PolygonFinishWindow(int sideCount, bool defaultShowIncircle)
        {
            InitializeComponent();
            Topmost = true;

            HeaderText.Text = $"已完成 {sideCount} 边形绘制，选择完成方式：";
            // 三角形一定存在唯一内切圆，默认勾选；其余多边形需要先纠正成正多边形才谈得上内切圆
            IncircleCheckBox.IsChecked = defaultShowIncircle;
            RegularizeCheckBox.IsChecked = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 延迟到 Background 优先级：确保 HWND 已完全创建后再激活/置前
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    if (!IsVisible) return;
                    Activate();
                    Focus();
                    Topmost = true;
                    var hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        PInvoke.SetForegroundWindow(new HWND(hwnd));
                    }
                    ButtonOk.Focus();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"多边形完成窗口激活失败: {ex.Message}", LogHelper.LogType.Warning);
                }
            }));
        }

        private void ButtonOk_Click(object sender, RoutedEventArgs e)
        {
            RegularizeRequested = RegularizeCheckBox.IsChecked == true;
            ShowIncircleRequested = IncircleCheckBox.IsChecked == true;
            DialogResult = true;
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            RegularizeRequested = false;
            ShowIncircleRequested = false;
            DialogResult = false;
        }
    }
}
