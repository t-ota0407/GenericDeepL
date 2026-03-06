using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace GenericDeepL
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var settings = Settings.Load();
            var isRegistered = StartupManager.IsRegistered();

            // スタートアップ有効・レジストリ未登録 → 登録する
            if (settings.RunAtStartup && !isRegistered)
            {
                StartupManager.Register();
            }
            // レジストリには登録されているが設定が false（MSIで「スタートアップで実行」をONにした等）
            // → Unregister せず、設定を true に同期する（インストーラの選択を尊重）
            else if (!settings.RunAtStartup && isRegistered)
            {
                settings.RunAtStartup = true;
                settings.Save();
            }
            // スタートアップ無効・レジストリ登録ありで「設定でオフにした」場合は Settings 保存時に Unregister 済みの想定

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}
