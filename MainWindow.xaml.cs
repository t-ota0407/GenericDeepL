using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
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
        // クリップボード監視の一時停止。パスワード等をコピーする前に手動で止めるための機能。
        // 一時的な措置なので設定には保存せず、起動時は常に監視ON。
        private bool _isMonitoringPaused = false;
        private System.Windows.Controls.MenuItem? _pauseMenuItem;
        // 実行中の翻訳。新しい要求が来たら古い方をキャンセルする。
        // 生成・参照・破棄はすべてUIスレッド上で行われるため排他は不要。
        private CancellationTokenSource? _translationCts;
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
            // スタートアップ登録の同期・修復は App.Application_Startup で済んでいるためここでは行わない
            _translationService = new TranslationService(_settings);
            _hotKeyHook = new GlobalHotKeyHook();
            _hotKeyHook.DoubleCtrlC += HotKeyHook_DoubleCtrlC;

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
                Visibility = Visibility.Visible
            };
            UpdateTrayToolTip();

            var contextMenu = new System.Windows.Controls.ContextMenu();

            var showMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "ウィンドウを表示"
            };
            showMenuItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(showMenuItem);

            var settingsMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "設定"
            };
            settingsMenuItem.Click += SettingsMenuItem_Click;
            contextMenu.Items.Add(settingsMenuItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            // クリップボードの内容は外部（Google）へ送信されるため、一時的に止められるようにする
            _pauseMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "クリップボード監視を一時停止",
                IsCheckable = true,
                IsChecked = _isMonitoringPaused
            };
            _pauseMenuItem.Click += PauseMenuItem_Click;
            contextMenu.Items.Add(_pauseMenuItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var exitMenuItem = new System.Windows.Controls.MenuItem
            {
                Header = "終了"
            };
            exitMenuItem.Click += ExitMenuItem_Click;
            contextMenu.Items.Add(exitMenuItem);

            _taskbarIcon.ContextMenu = contextMenu;
            _taskbarIcon.TrayMouseDoubleClick += (s, e) => ShowWindow();
        }

        private void PauseMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _isMonitoringPaused = _pauseMenuItem?.IsChecked == true;
            UpdateTrayToolTip();
        }

        /// <summary>
        /// トレイアイコンのツールチップを現在の状態に合わせて更新する。
        /// </summary>
        private void UpdateTrayToolTip()
        {
            if (_taskbarIcon == null)
            {
                return;
            }

            _taskbarIcon.ToolTipText = _isMonitoringPaused
                ? "GenericDeepL - クリップボード監視を一時停止中"
                : "GenericDeepL - Ctrl+C を素早く2回で翻訳";
        }

        /// <summary>ウィンドウを表示して前面に出す。2つ目のインスタンス起動時にも呼ばれる。</summary>
        public void ShowWindow()
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

        private async void HotKeyHook_DoubleCtrlC(object? sender, EventArgs e)
        {
            try
            {
                // 一時停止中はクリップボードに触れず、外部へも一切送信しない
                if (_isMonitoringPaused)
                {
                    return;
                }

                var clipboardText = TryGetClipboardText();
                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    return;
                }

                ShowWindow();
                OriginalTextBox.Text = clipboardText;
                await RunTranslationAsync(clipboardText);
            }
            catch (Exception ex)
            {
                TranslatingIndicator.Visibility = Visibility.Collapsed;
                TranslationTextBox.Text = $"エラー: {ex.Message}";
            }
        }

        /// <summary>
        /// クリップボードからテキストを取得する。取得できない場合は null。
        /// </summary>
        /// <remarks>
        /// クリップボードは他プロセスにロックされることがあり、その間の取得は例外になる。
        /// 短い間隔で数回リトライする。
        /// </remarks>
        private static string? TryGetClipboardText()
        {
            const int maxAttempts = 3;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    return System.Windows.Clipboard.ContainsText()
                        ? System.Windows.Clipboard.GetText()
                        : null;
                }
                catch (Exception)
                {
                    if (attempt == maxAttempts - 1)
                    {
                        return null;
                    }
                    System.Threading.Thread.Sleep(50);
                }
            }
            return null;
        }

        /// <summary>
        /// 翻訳を実行して結果を表示する。外部送信の前に文字数のしきい値チェックを行う。
        /// </summary>
        private async Task RunTranslationAsync(string text)
        {
            // クリップボードの内容は外部（Google）へ送信される。想定外に長い文章を
            // 気づかず送ってしまわないよう、しきい値を超えたときだけ確認をはさむ。
            if (text.Length > _settings.ConfirmThresholdLength && !ConfirmLargeTextSend(text.Length))
            {
                TranslationTextBox.Text = "送信をキャンセルしました。";
                return;
            }

            // 直前の翻訳がまだ走っていればキャンセルする。連続で実行したときに、
            // 古い要求の結果が後から返ってきて新しい結果を上書きするのを防ぐ。
            _translationCts?.Cancel();

            var cts = new CancellationTokenSource();
            _translationCts = cts;

            try
            {
                TranslateButton.IsEnabled = false;
                TranslationTextBox.Text = "";
                TranslatingIndicator.Visibility = Visibility.Visible;

                var translation = await _translationService.TranslateAsync(text, cts.Token);

                TranslationTextBox.Text = translation;
            }
            catch (OperationCanceledException)
            {
                // より新しい翻訳要求に差し替えられた。表示はそちらが行うので何もしない。
            }
            catch (Exception ex)
            {
                TranslationTextBox.Text = $"エラー: {ex.Message}";
            }
            finally
            {
                // 自分が最新の要求のときだけUIを元に戻す。
                // 古い要求が新しい要求の進行中表示を消してしまわないようにするため。
                if (ReferenceEquals(_translationCts, cts))
                {
                    TranslatingIndicator.Visibility = Visibility.Collapsed;
                    TranslateButton.IsEnabled = true;
                    _translationCts = null;
                }
                cts.Dispose();
            }
        }

        /// <summary>
        /// しきい値を超える長さのテキストを送信してよいかユーザーに確認する。
        /// </summary>
        private bool ConfirmLargeTextSend(int length)
        {
            var answer = MessageBox.Show(
                $"文字数がしきい値を超えています。このまま Google へ送信して翻訳しますか？{Environment.NewLine}{Environment.NewLine}" +
                $"文字数　: {length:N0}{Environment.NewLine}" +
                $"しきい値: {_settings.ConfirmThresholdLength:N0}{Environment.NewLine}{Environment.NewLine}" +
                "しきい値は設定画面から変更できます。",
                "送信の確認",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            return answer == MessageBoxResult.OK;
        }

        private async void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            var text = OriginalTextBox.Text;
            if (string.IsNullOrWhiteSpace(text) || text == "原文がここに表示されます...")
                return;

            await RunTranslationAsync(text);
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
