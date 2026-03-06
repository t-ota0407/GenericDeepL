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
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int VK_CONTROL = 0x11;
        private const int VK_C = 0x43;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private DateTime _lastKeyPress = DateTime.MinValue;
        private int _keyPressCount = 0;
        private readonly DispatcherTimer _resetTimer;

        public event EventHandler? TripleCtrlC;

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
                Interval = TimeSpan.FromSeconds(1)
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
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Ctrlキーが押されているかチェック
                bool ctrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

                if (ctrlPressed && vkCode == VK_C)
                {
                    var now = DateTime.Now;
                    var timeSinceLastPress = (now - _lastKeyPress).TotalSeconds;

                    if (timeSinceLastPress < 1.0)
                    {
                        _keyPressCount++;
                        if (_keyPressCount >= 2)
                        {
                            _keyPressCount = 0;
                            _resetTimer.Stop();
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                TripleCtrlC?.Invoke(this, EventArgs.Empty);
                            });
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
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
