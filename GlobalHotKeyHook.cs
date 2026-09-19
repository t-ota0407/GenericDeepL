using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace GenericDeepL
{
    public class GlobalHotKeyHook : IDisposable
    {
        /// <summary>翻訳を発火させるのに必要な Ctrl+C の連続押下回数。
        /// 変更した場合は docs/使い方.md・CLAUDE.md・トレイのツールチップも合わせること。</summary>
        private const int RequiredPressCount = 2;

        /// <summary>連続押下とみなす、前回の Ctrl+C からの最大間隔。</summary>
        private static readonly TimeSpan PressInterval = TimeSpan.FromSeconds(1);

        /// <summary>キーのチャタリングやソフトウェアによる多重入力を無視する最小間隔。
        /// 人間の素早い2連打でも 80ms 程度はあくため、これより短い間隔は同一押下の重複とみなす。</summary>
        private static readonly TimeSpan MinPressInterval = TimeSpan.FromMilliseconds(40);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int VK_CONTROL = 0x11;
        private const int VK_C = 0x43;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private DateTime _lastKeyPress = DateTime.MinValue;
        private int _keyPressCount = 0;
        // C キーが現在押し下げられているか。オートリピート判定に使う
        private bool _isCKeyDown = false;
        // C キーの直近の押し下げ時刻（押下回数に数えたかどうかに関わらず記録する）
        private DateTime _lastCKeyDown = DateTime.MinValue;
        private readonly DispatcherTimer _resetTimer;

        public event EventHandler? DoubleCtrlC;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public GlobalHotKeyHook()
        {
            _proc = HookCallback;
            _resetTimer = new DispatcherTimer
            {
                Interval = PressInterval
            };
            _resetTimer.Tick += (s, e) =>
            {
                _keyPressCount = 0;
                _resetTimer.Stop();
            };
        }

        public void Start()
        {
            _hookID = SetHook(_proc);
        }

        public void Stop()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = (int)wParam;
                if (message == WM_KEYDOWN || message == WM_KEYUP)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    if (vkCode == VK_C)
                    {
                        if (message == WM_KEYUP)
                        {
                            // キーが離された。次の WM_KEYDOWN は新しい押下として数える
                            _isCKeyDown = false;
                        }
                        else
                        {
                            HandleCKeyDown();
                        }
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        /// <summary>
        /// C キーの押し下げを処理し、Ctrl+C の連続押下回数を数える。
        /// </summary>
        private void HandleCKeyDown()
        {
            var now = DateTime.Now;

            // キーを押しっぱなしにしたときのオートリピートは、間に WM_KEYUP が入らない。
            // これを 1 回の押下として扱い、勝手に翻訳が走るのを防ぐ。
            // 何らかの理由で WM_KEYUP を取りこぼしてもホットキーが死んだままにならないよう、
            // 前回の押し下げから PressInterval 以上あいていれば新しい押下として復帰させる。
            // （オートリピートの間隔は Windows の設定でも最長 400ms 程度のため誤判定しない）
            bool isAutoRepeat = _isCKeyDown && (now - _lastCKeyDown) < PressInterval;
            _isCKeyDown = true;
            _lastCKeyDown = now;
            if (isAutoRepeat)
            {
                return;
            }

            // Ctrlキーが押されているかチェック
            bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            if (!ctrlPressed)
            {
                return;
            }

            var timeSinceLastPress = now - _lastKeyPress;

            // チャタリング等による多重入力は 1 回の押下として扱う
            if (timeSinceLastPress < MinPressInterval)
            {
                return;
            }

            if (timeSinceLastPress < PressInterval)
            {
                _keyPressCount++;
                if (_keyPressCount >= RequiredPressCount)
                {
                    _keyPressCount = 0;
                    _resetTimer.Stop();

                    // 低レベルフックのコールバックは、フックを登録したスレッド（UIスレッド）上で
                    // 同期的に呼び出される。ここで Invoke するとウィンドウ表示やクリップボード読み取りが
                    // 終わるまでフックが返らず、LowLevelHooksTimeout（既定 300ms）を超えると
                    // Windows にフックを解除されてホットキーが反応しなくなる。
                    // さらに、その間はシステム全体のキー入力がブロックされる。
                    // そのため BeginInvoke でディスパッチャのキューに積むだけにして、
                    // ただちに CallNextHookEx まで抜ける。
                    Application.Current?.Dispatcher.BeginInvoke(
                        new Action(() => DoubleCtrlC?.Invoke(this, EventArgs.Empty)));
                }
                else
                {
                    _resetTimer.Stop();
                    _resetTimer.Start();
                }
            }
            else
            {
                _keyPressCount = 1;
                _resetTimer.Stop();
                _resetTimer.Start();
            }

            _lastKeyPress = now;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
