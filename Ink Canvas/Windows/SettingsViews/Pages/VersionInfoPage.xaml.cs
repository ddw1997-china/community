using System;
using System.Reflection;
using System.Windows;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    /// <summary>
    /// 设置窗口「关于」页面：仅显示当前版本信息。
    /// </summary>
    public partial class VersionInfoPage : iNKORE.UI.WPF.Modern.Controls.Page
    {
        public VersionInfoPage()
        {
            InitializeComponent();
            Loaded += VersionInfoPage_Loaded;
        }

        private void VersionInfoPage_Loaded(object sender, RoutedEventArgs e)
        {
            AppVersionTextBlock.Text = GetVersionText();
        }

        private static string GetVersionText()
        {
            if (!string.IsNullOrEmpty(App.AppVersion))
                return App.AppVersion;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null
                ? string.Empty
                : $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }
}
