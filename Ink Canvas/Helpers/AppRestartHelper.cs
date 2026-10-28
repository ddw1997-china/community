using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Ink_Canvas.Helpers
{
    public static class AppRestartHelper
    {
        /// <summary>
        /// 顺序重启：释放单实例互斥体后，交给一个隐藏的中转进程等待"当前进程完全退出"再启动新实例。
        /// 不允许新旧实例并存：托盘图标在系统通知区是全局唯一的身份，两个实例同时注册时，
        /// 先退出的那个会把自己的图标一起注销，导致仍在运行的实例变成"进程在跑、托盘没有图标"，
        /// 用户再也无法操作程序。
        /// </summary>
        /// <param name="asAdmin">true 表示以管理员身份启动新实例（会触发 UAC）。</param>
        /// <returns>中转进程是否已成功启动。</returns>
        public static bool BeginSequentialRestart(bool asAdmin)
        {
            try
            {
                App.IsAppExitByUser = true;

                // 必须先释放并销毁互斥体，否则新实例会被单实例检查拦下并直接退出
                (Application.Current as App)?.ReleaseMutexForRestart();

                int pid = Process.GetCurrentProcess().Id;
                string exePath = Process.GetCurrentProcess().MainModule.FileName;

                // -Timeout 后仍存活说明退出流程被卡住：强制结束，绝不让两个实例并存
                string command =
                    "Wait-Process -Id " + pid + " -Timeout 20 -ErrorAction SilentlyContinue; " +
                    "if (Get-Process -Id " + pid + " -ErrorAction SilentlyContinue) { Stop-Process -Id " + pid + " -Force -ErrorAction SilentlyContinue }; " +
                    "Start-Process -FilePath '" + exePath.Replace("'", "''") + "'" + (asAdmin ? " -Verb RunAs" : "");

                var psi = new ProcessStartInfo("powershell.exe")
                {
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + command + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                Process.Start(psi);
                LogHelper.WriteLogToFile($"重启 | 已启动顺序重启中转进程，等待 PID {pid} 退出后启动新实例" + (asAdmin ? "（管理员）" : ""),
                    LogHelper.LogType.Event);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"重启 | 启动顺序重启中转进程失败: {ex.Message}", LogHelper.LogType.Error);

                // 中转进程启动失败时退化为立即启动，至少保证能重启
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    var psi = new ProcessStartInfo(exePath) { UseShellExecute = true };
                    if (asAdmin) psi.Verb = "runas";
                    Process.Start(psi);
                    return true;
                }
                catch (Exception inner)
                {
                    LogHelper.WriteLogToFile($"重启 | 立即启动新实例也失败: {inner.Message}", LogHelper.LogType.Error);
                    App.IsAppExitByUser = false;
                    return false;
                }
            }
        }

        public static bool IsRunningAsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static void RestartApp(bool asAdmin)
        {
            try
            {
                // 当前已是管理员却要以普通权限重启：需要复制用户令牌，走专用链路
                if (!asAdmin && IsRunningAsAdmin() && UIAccessHelper.RestartAsNormalUser())
                {
                    App.IsAppExitByUser = true;
                    Application.Current.Shutdown();
                    return;
                }

                if (!BeginSequentialRestart(asAdmin)) return;

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"重启应用时出错: {ex.Message}");
            }
        }

        public static void RestartWithCurrentPrivileges()
        {
            RestartApp(IsRunningAsAdmin());
        }

        public static void RestartAsAdmin()
        {
            RestartApp(true);
        }

        public static void RestartAsNormal()
        {
            RestartApp(false);
        }

        public static void SwitchToUIATopMostAndRestart()
        {
            try
            {
                SettingsManager.Settings.Advanced.EnableUIAccessTopMost = true;

                if (!SettingsManager.Settings.Advanced.IsAlwaysOnTop)
                {
                    SettingsManager.Settings.Advanced.IsAlwaysOnTop = true;
                }

                SettingsManager.SaveSettingsToFile();

                App.IsUIAccessTopMostEnabled = true;
                App.IsAppExitByUser = true;
                (Application.Current as App)?.ReleaseMutexForRestart();

                bool started;
                bool useProcessToken = SettingsManager.Settings.Advanced.UIAMode == UIAMode.ProcessToken;

                if (IsRunningAsAdmin())
                {
                    if (useProcessToken)
                    {
                        started = UIAccessHelper.RestartAsNormalUserWithUIAccess_ProcessToken(sourcePid: (uint)Process.GetCurrentProcess().Id);
                    }
                    else
                    {
                        started = UIAccessHelper.RestartAsNormalUserWithUIAccess();
                    }
                }
                else
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    ProcessStartInfo psi;

                    if (useProcessToken)
                    {
                        int currentPid = Process.GetCurrentProcess().Id;
                        psi = new ProcessStartInfo(exePath)
                        {
                            Arguments = $"--enable-uia-topmost-helper --uia-source-pid {currentPid}",
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(psi);
                        // 保持原进程短暂存活，确保提升的 helper 可以复制当前进程令牌。
                        System.Threading.Thread.Sleep(2000);
                    }
                    else
                    {
                        psi = new ProcessStartInfo(exePath)
                        {
                            Arguments = "--enable-uia-topmost-helper",
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(psi);
                    }
                    started = true;
                }

                if (started)
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    App.IsAppExitByUser = false;
                    App.IsUIAccessTopMostEnabled = false;
                }
            }
            catch (Exception ex)
            {
                App.IsAppExitByUser = false;
                App.IsUIAccessTopMostEnabled = false;
                Debug.WriteLine($"切换到UIA置顶模式时出错: {ex.Message}");
            }
        }

        public static void SwitchToNormalTopMostAndRestart()
        {
            try
            {
                SettingsManager.Settings.Advanced.EnableUIAccessTopMost = false;
                SettingsManager.SaveSettingsToFile();

                App.IsUIAccessTopMostEnabled = false;
                RestartApp(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"切换到普通置顶模式时出错: {ex.Message}");
            }
        }
    }
}
