using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        // 最小化アニメーション中に WindowState を変更する際の再入を防ぐフラグ
        private bool _isMinimizing = false;
        // 吸い込み時に縮小しきったときのスケール（0 は描画が破綻するため極小値にする）
        private const double CollapsedScale = 0.02;
        // ドロップシャドウの通常時の不透明度（XAML の WindowShadow と一致させる）
        private const double ShadowOpacity = 0.3;
        // 通常時の角丸半径（XAML の ShadowBorder/BorderOverlay と一致させる）
        private const double CornerRadiusValue = 8;
        // 直前のウィンドウ状態（最小化からの復元時のみアニメーションするために使う）
        private WindowState _previousState = WindowState.Normal;
        // 現在適用する角丸半径（最大化時は 0）
        private double _currentCornerRadius = CornerRadiusValue;
        // アニメーションの再生時間（スッと素早く消える/現れるように短めにする）
        private static readonly Duration MinimizeDuration = new Duration(TimeSpan.FromMilliseconds(120));
        private static readonly Duration RestoreDuration = new Duration(TimeSpan.FromMilliseconds(130));

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
            var iconStream = Application.GetResourceStream(
                new Uri("pack://application:,,,/assets/icon/GenericDeepL.ico"))?.Stream;

            _taskbarIcon = new TaskbarIcon
            {
                Icon = iconStream != null
                    ? new System.Drawing.Icon(iconStream)
                    : System.Drawing.SystemIcons.Application,
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
            // ダブルクリックで最大化⇔元のサイズをトグルする
            if (e.ClickCount == 2)
            {
                ToggleMaximizeRestore();
                return;
            }

            // 通常状態のときのみドラッグ移動を許可する
            if (this.WindowState == WindowState.Normal)
            {
                DragMove();
            }
        }

        private void ToggleMaximizeRestore()
        {
            this.WindowState = this.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            AnimateMinimize();
        }

        /// <summary>
        /// ウィンドウをタスクバー方向へ縮小・フェードアウトさせて吸い込まれるように最小化する。
        /// </summary>
        private void AnimateMinimize()
        {
            // 既に最小化アニメーション中、または最小化済みなら何もしない
            if (_isMinimizing || this.WindowState == WindowState.Minimized)
            {
                return;
            }
            _isMinimizing = true;

            // 縮小中はシャドウが不自然に見えるため、アニメーション前に消しておく
            WindowShadow.Opacity = 0;

            // タスクバーの方向へ収束するように縮小中心を設定する
            UpdateScaleCenter();

            // 出だしから素早く動くように EaseOut にする
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var shrinkX = new DoubleAnimation(1.0, CollapsedScale, MinimizeDuration) { EasingFunction = easing };
            var shrinkY = new DoubleAnimation(1.0, CollapsedScale, MinimizeDuration) { EasingFunction = easing };
            var fadeOut = new DoubleAnimation(1.0, 0.0, MinimizeDuration) { EasingFunction = easing };

            fadeOut.Completed += (s, args) =>
            {
                // アニメーション完了後に実際に最小化する
                this.WindowState = WindowState.Minimized;
                _isMinimizing = false;
            };

            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkX);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkY);
            this.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        /// <summary>
        /// タスクバーから復元された際に、タスクバー方向から広がるように拡大・フェードインで表示する。
        /// </summary>
        private void AnimateRestore()
        {
            // 復元時もタスクバー方向を起点に拡大する
            UpdateScaleCenter();

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var growX = new DoubleAnimation(CollapsedScale, 1.0, RestoreDuration) { EasingFunction = easing };
            var growY = new DoubleAnimation(CollapsedScale, 1.0, RestoreDuration) { EasingFunction = easing };
            var fadeIn = new DoubleAnimation(0.0, 1.0, RestoreDuration) { EasingFunction = easing };

            // 展開アニメーションが終わってからシャドウを戻す
            fadeIn.Completed += (s, args) => WindowShadow.Opacity = ShadowOpacity;

            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, growX);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, growY);
            this.BeginAnimation(Window.OpacityProperty, fadeIn);
        }

        /// <summary>
        /// タスクバーの位置を推定し、その方向へ収束するように縮小中心点（ウィンドウ内座標）を設定する。
        /// </summary>
        private void UpdateScaleCenter()
        {
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            var work = SystemParameters.WorkArea;

            // 作業領域と画面サイズの差からタスクバーのある辺を判定し、画面上の収束先を決める
            double targetXScreen;
            double targetYScreen;
            if (work.Top > 0)
            {
                // 上辺
                targetXScreen = screenW / 2;
                targetYScreen = 0;
            }
            else if (work.Left > 0)
            {
                // 左辺
                targetXScreen = 0;
                targetYScreen = screenH / 2;
            }
            else if (work.Right < screenW)
            {
                // 右辺
                targetXScreen = screenW;
                targetYScreen = screenH / 2;
            }
            else
            {
                // 下辺（既定）
                targetXScreen = screenW / 2;
                targetYScreen = screenH;
            }

            // 画面座標からウィンドウ左上を原点とした相対座標へ変換する
            RootScale.CenterX = targetXScreen - this.Left;
            RootScale.CenterY = targetYScreen - this.Top;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            var current = this.WindowState;

            if (current == WindowState.Maximized)
            {
                // 最大化時は余白・角丸・影をなくして画面いっぱいに表示する
                ShadowBorder.Margin = new Thickness(0);
                ShadowBorder.CornerRadius = new CornerRadius(0);
                BorderOverlay.CornerRadius = new CornerRadius(0);
                WindowShadow.Opacity = 0;
                _currentCornerRadius = 0;
                UpdateContentClip();
            }
            else if (current == WindowState.Normal)
            {
                // 通常サイズに戻すときは余白・角丸を復元する
                ShadowBorder.Margin = new Thickness(12);
                ShadowBorder.CornerRadius = new CornerRadius(CornerRadiusValue);
                BorderOverlay.CornerRadius = new CornerRadius(CornerRadiusValue);
                _currentCornerRadius = CornerRadiusValue;
                UpdateContentClip();

                if (_previousState == WindowState.Minimized && !_isInitializing && !_isMinimizing)
                {
                    // 最小化から戻ったときだけ、タスクバーから広がる表示アニメーションを再生する
                    // （影はアニメーション完了後に AnimateRestore 内で戻す）
                    AnimateRestore();
                }
                else
                {
                    // 最大化からの復元など、それ以外は影をすぐに表示する
                    WindowShadow.Opacity = ShadowOpacity;
                }
            }

            _previousState = current;
        }

        private void ContentRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateContentClip();
        }

        /// <summary>
        /// コンテンツを角丸の矩形でクリップし、子要素が角からはみ出さないようにする。
        /// </summary>
        private void UpdateContentClip()
        {
            if (ContentRoot.ActualWidth <= 0 || ContentRoot.ActualHeight <= 0)
            {
                return;
            }

            ContentRoot.Clip = new RectangleGeometry(
                new Rect(0, 0, ContentRoot.ActualWidth, ContentRoot.ActualHeight),
                _currentCornerRadius, _currentCornerRadius);
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // WindowStyle=None + AllowsTransparency では最大化時にタスクバーを覆ってしまうため、
            // WM_GETMINMAXINFO を処理して作業領域内に収める
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 最大化サイズ・位置をモニターの作業領域（タスクバーを除いた領域）に制限する。
        /// </summary>
        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            const int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                GetMonitorInfo(monitor, ref monitorInfo);

                RECT work = monitorInfo.rcWork;
                RECT screen = monitorInfo.rcMonitor;
                mmi.ptMaxPosition.X = Math.Abs(work.Left - screen.Left);
                mmi.ptMaxPosition.Y = Math.Abs(work.Top - screen.Top);
                mmi.ptMaxSize.X = Math.Abs(work.Right - work.Left);
                mmi.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
    }
}
