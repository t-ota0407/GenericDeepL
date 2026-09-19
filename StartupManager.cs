using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GenericDeepL
{
    /// <summary>
    /// Windowsスタートアップ登録を管理するクラス
    /// </summary>
    public static class StartupManager
    {
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "GenericDeepL";

        /// <summary>
        /// スタートアップに登録されているか確認
        /// </summary>
        public static bool IsRegistered()
        {
            return ReadRegisteredValue() != null;
        }

        /// <summary>
        /// 登録済みの値が、実在する本アプリの .exe を指しているか確認する。
        /// 旧バージョンは .dll のパスを書き込んでいたため（自動起動できない）、
        /// その壊れた値を検出して登録し直すために使う。
        /// </summary>
        public static bool IsRegisteredPathValid()
        {
            var value = ReadRegisteredValue();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // 値は "C:\...\GenericDeepL.exe" のように引用符付きで格納されている
            var path = value.Trim().Trim('"');

            try
            {
                return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
            }
            catch (Exception)
            {
                // パスとして不正な文字列が入っていた場合は無効扱いにする
                return false;
            }
        }

        /// <summary>
        /// スタートアップに登録
        /// </summary>
        public static bool Register()
        {
            try
            {
                var exePath = GetExecutablePath();
                if (exePath == null)
                    return false;

                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key == null)
                        return false;

                    key.SetValue(AppName, $"\"{exePath}\"");
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// スタートアップから削除
        /// </summary>
        public static bool Unregister()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true))
                {
                    if (key == null)
                        return false;

                    key.DeleteValue(AppName, false);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Run キーに格納されている本アプリの値を読み出す（未登録なら null）
        /// </summary>
        private static string? ReadRegisteredValue()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false))
                {
                    return key?.GetValue(AppName) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 自プロセスの実行ファイル（.exe）のフルパスを取得する。取得できない場合は null。
        /// </summary>
        /// <remarks>
        /// Assembly.GetExecutingAssembly().Location は .NET 6 の apphost 起動では
        /// マネージド DLL（GenericDeepL.dll）のパスを返すため使用しない。
        /// Run キーに .dll を登録すると Windows はアプリを起動できない。
        /// </remarks>
        private static string? GetExecutablePath()
        {
            string? path = Environment.ProcessPath;

            if (string.IsNullOrEmpty(path))
            {
                try
                {
                    using (var process = Process.GetCurrentProcess())
                    {
                        path = process.MainModule?.FileName;
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            }

            if (string.IsNullOrEmpty(path))
                return null;

            // `dotnet run` 経由の場合は dotnet.exe が返るため、自アプリの exe 以外は登録しない
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), AppName, StringComparison.OrdinalIgnoreCase))
                return null;

            return path;
        }
    }
}
