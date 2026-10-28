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
    /// 双曲线“是否移除渐近线？”自建确认对话框。
    /// 相比 iNKORE 的 MessageBox，本窗口可自行置顶并强制前台/聚焦，
    /// 解决白板/触控环境下首次点击被“窗口激活”吞掉、需多次点击才能关闭的问题。
    /// </summary>
    public partial class AsymptoteConfirmWindow : Window
    {
        /// <summary>用户是否选择了“是”（移除渐近线）。</summary>
        public bool RemoveRequested { get; private set; }

        public AsymptoteConfirmWindow()
        {
            InitializeComponent();
            Topmost = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 延迟到 Background 优先级：确保 HWND 已完全创建后再激活/置前，
            // 否则此时调用 Activate 可能抛 InvalidOperationException。
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
                    // 让“是”按钮获得键盘/触摸焦点，确保单击即命中
                    ButtonYes.Focus();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"渐近线确认窗口激活失败: {ex.Message}", LogHelper.LogType.Warning);
                }
            }));
        }

        private void ButtonYes_Click(object sender, RoutedEventArgs e)
        {
            RemoveRequested = true;
            Close();
        }

        private void ButtonNo_Click(object sender, RoutedEventArgs e)
        {
            RemoveRequested = false;
            Close();
        }
    }
}
