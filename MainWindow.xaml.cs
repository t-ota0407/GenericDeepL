using System;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using System.Drawing;

namespace GenericDeepL
{
    public partial class MainWindow : Window
    {
        private readonly Settings _settings;
        private readonly GlobalHotKeyHook _hotKeyHook;
        private readonly TranslationService _translationService;
        private TaskbarIcon? _taskbarIcon;
        private bool _isExiting = false;

        public MainWindow()
        {
            InitializeComponent();
            _settings = Settings.Load();
            // スタートアップが有効なのに未登録ならレジストリに登録（標準でスタートアップ有効のため）
            if (_settings.RunAtStartup && !StartupManager.IsRegistered())
            {
                StartupManager.Register();
            }
            _translationService = new TranslationService(_settings);
            _hotKeyHook = new GlobalHotKeyHook();
            _hotKeyHook.TripleCtrlC += HotKeyHook_TripleCtrlC;

            // システムトレイアイコンを設定
            SetupTaskbarIcon();

            // グローバルホットキーを開始
            _hotKeyHook.Start();

            // ウィンドウを非表示にする（システムトレイのみ表示）
            this.Hide();
        }

        private void SetupTaskbarIcon()
        {
            _taskbarIcon = new TaskbarIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                ToolTipText = "GenericDeepL - Press Ctrl+C twice to translate",
                Visibility = Visibility.Visible
            };

            var contextMenu = new System.Windows.Controls.ContextMenu();
            
            var showMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "Show Window"
            };
            showMenuItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(showMenuItem);

            var settingsMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "Settings"
            };
            settingsMenuItem.Click += SettingsMenuItem_Click;
            contextMenu.Items.Add(settingsMenuItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "Exit"
            };
            exitMenuItem.Click += ExitMenuItem_Click;
            contextMenu.Items.Add(exitMenuItem);

            _taskbarIcon.ContextMenu = contextMenu;
            _taskbarIcon.TrayMouseDoubleClick += (s, e) => ShowWindow();
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Topmost = true;
            this.Topmost = false;
            this.Focus();
        }

        private async void HotKeyHook_TripleCtrlC(object? sender, EventArgs e)
        {
            try
            {
                // クリップボードからテキストを取得
                string? clipboardText = null;
                Dispatcher.Invoke(() =>
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        clipboardText = System.Windows.Clipboard.GetText();
                    }
                });

                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    return;
                }

                // ウィンドウを表示して最前面に
                Dispatcher.Invoke(() =>
                {
                    ShowWindow();
                    OriginalTextBox.Text = clipboardText;
                    TranslationTextBox.Text = "";
                    TranslatingIndicator.Visibility = Visibility.Visible;
                });

                // 翻訳を実行
                var translation = await _translationService.TranslateAsync(clipboardText);

                Dispatcher.Invoke(() =>
                {
                    TranslatingIndicator.Visibility = Visibility.Collapsed;
                    TranslationTextBox.Text = translation;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    TranslatingIndicator.Visibility = Visibility.Collapsed;
                    TranslationTextBox.Text = $"エラー: {ex.Message}";
                });
            }
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_settings);
            if (settingsWindow.ShowDialog() == true)
            {
                // 設定が保存されたので、TranslationServiceを再初期化
                // （実際にはSettingsオブジェクトを共有しているので不要だが、念のため）
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _isExiting = true;
            _hotKeyHook.Stop();
            _taskbarIcon?.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // メニューバーから終了する場合は、アプリを完全に終了
            if (_isExiting)
            {
                return; // キャンセルしないので、ウィンドウが閉じられてアプリが終了する
            }
            
            // ×ボタンから閉じる場合は、非表示にするだけ
            e.Cancel = true; // ウィンドウを閉じるのをキャンセル
            this.Hide(); // 代わりに非表示にする
        }

        protected override void OnClosed(EventArgs e)
        {
            _hotKeyHook.Stop();
            _taskbarIcon?.Dispose();
            base.OnClosed(e);
        }
    }
}
