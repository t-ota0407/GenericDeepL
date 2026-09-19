using System;
using System.Threading;
using System.Windows;

namespace GenericDeepL
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // 二重起動防止用の名前。Local\ 名前空間にすることでログオンセッション単位（= ユーザー単位）になる。
        // 設定もユーザー単位のため、別ユーザーが同時に使えるこの粒度が適切。
        private const string SingleInstanceMutexName = @"Local\GenericDeepL.SingleInstance";
        private const string ShowWindowEventName = @"Local\GenericDeepL.ShowWindow";

        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _showWindowEvent;
        private RegisteredWaitHandle? _showWindowRegistration;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 二重起動するとキーボードフックが2本刺さり、Ctrl+C 2回で翻訳が二重に走って
            // API も二重課金される。トレイアイコンも2つ出てしまう。
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                NotifyExistingInstance();
                Shutdown();
                return;
            }

            var settings = Settings.Load();

            // レジストリには登録されているが設定が false（MSIで「スタートアップで実行」をONにした等）
            // → Unregister せず、設定を true に同期する（インストーラの選択を尊重）
            if (!settings.RunAtStartup && StartupManager.IsRegistered())
            {
                settings.RunAtStartup = true;
                settings.Save();
            }

            // スタートアップ有効なのに未登録、または登録値が壊れている場合は登録し直す。
            // 旧バージョンは .exe ではなく .dll のパスを書き込んでいて自動起動できなかったため、
            // ここで正しい .exe のパスに修復する。
            if (settings.RunAtStartup && !StartupManager.IsRegisteredPathValid())
            {
                StartupManager.Register();
            }
            // スタートアップ無効・レジストリ登録ありで「設定でオフにした」場合は Settings 保存時に Unregister 済みの想定

            var mainWindow = new MainWindow();
            mainWindow.Show();

            // 2つ目のインスタンスが起動したときに、既存のウィンドウを前面に出すための待ち受け
            StartShowWindowListener(mainWindow);
        }

        /// <summary>
        /// 既に起動しているインスタンスにウィンドウの表示を依頼する。
        /// </summary>
        private static void NotifyExistingInstance()
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowWindowEventName, out var existing))
                {
                    using (existing)
                    {
                        existing.Set();
                    }
                }
            }
            catch (Exception)
            {
                // 通知できなくても、二重起動を防ぐこと自体は達成できているのでそのまま終了する
            }
        }

        /// <summary>
        /// 他のインスタンスからの表示要求を待ち受ける。
        /// </summary>
        private void StartShowWindowListener(MainWindow mainWindow)
        {
            try
            {
                _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
                _showWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
                    _showWindowEvent,
                    (state, timedOut) => Dispatcher.BeginInvoke(new Action(mainWindow.ShowWindow)),
                    null,
                    Timeout.Infinite,
                    false);
            }
            catch (Exception)
            {
                // 待ち受けに失敗しても本体の動作には影響しない
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _showWindowRegistration?.Unregister(null);
            _showWindowEvent?.Dispose();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
