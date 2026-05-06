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
        private bool _isInitializing = true;

        public MainWindow()
        {
            _settings = Settings.Load();
            InitializeComponent();
            InitializeTargetLanguageSelection();
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
            _isInitializing = false;
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

        private void InitializeTargetLanguageSelection()
        {
            switch (_settings.TargetLanguage)
            {
                case "英語":
                    TargetEnglishButton.IsChecked = true;
                    break;
                case "日本語":
                    TargetJapaneseButton.IsChecked = true;
                    break;
                default:
                    TargetAutoButton.IsChecked = true;
                    break;
            }
        }

        private void TargetLanguage_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (sender == TargetEnglishButton)
                _settings.TargetLanguage = "英語";
            else if (sender == TargetJapaneseButton)
                _settings.TargetLanguage = "日本語";
            else
                _settings.TargetLanguage = "自動";
            _settings.Save();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            // 最小化ボタンはタスクバーに格納（非表示にしない）
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

        private async void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            var text = OriginalTextBox.Text;
            if (string.IsNullOrWhiteSpace(text) || text == "原文がここに表示されます...")
                return;

            try
            {
                TranslateButton.IsEnabled = false;
                TranslationTextBox.Text = "";
                TranslatingIndicator.Visibility = Visibility.Visible;

                var translation = await _translationService.TranslateAsync(text);

                TranslatingIndicator.Visibility = Visibility.Collapsed;
                TranslationTextBox.Text = translation;
            }
            catch (Exception ex)
            {
                TranslatingIndicator.Visibility = Visibility.Collapsed;
                TranslationTextBox.Text = $"エラー: {ex.Message}";
            }
            finally
            {
                TranslateButton.IsEnabled = true;
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
