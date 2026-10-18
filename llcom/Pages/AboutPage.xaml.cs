using System;
using System.Windows;
using System.Windows.Controls;
using llcom.Services;

namespace llcom.Pages
{
    /// <summary>
    /// AboutPage.xaml 的交互逻辑。
    /// 本版本为个人维护的独立项目：更新检查/热更已关闭，链接全部指向自己的仓库，
    /// 并单独保留原作者仓库入口（不混在一起，避免误认作官方版本）。
    /// </summary>
    public partial class AboutPage : Page
    {
        /// <summary>本项目（LLCOM-Y）仓库</summary>
        private const string ProjectRepo = "https://github.com/Eureka12138github/llcom-y";

        /// <summary>上游原作者仓库（LLCOM）</summary>
        private const string OriginalRepo = "https://github.com/chenxuuu/llcom";

        public AboutPage()
        {
            InitializeComponent();
        }

        private static bool loaded = false;

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            //自动更新开关绑定到设置
            DataContext = Tools.Global.setting;

            //每次切回「关于」都回到顶部（避免上次滚动位置残留）
            aboutScrollViewer.ScrollToTop();

            if (loaded)
                return;
            loaded = true;
            versionTextBlock.Text = System
                .Reflection.Assembly.GetExecutingAssembly()
                .GetName()
                .Version.ToString();
        }

        /// <summary>手动检查更新（检查本项目的 GitHub Releases）。</summary>
        private void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            AppUpdateService.CheckForUpdate(Dispatcher, manual: true);
        }

        /// <summary>本项目反馈：GitHub Issues</summary>
        private void NewissueButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(ProjectRepo + "/issues/new/choose");
        }

        /// <summary>本项目仓库</summary>
        private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(ProjectRepo);
        }

        /// <summary>原作者仓库（上游 LLCOM）</summary>
        private void OriginalRepoButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(OriginalRepo);
        }
    }
}
