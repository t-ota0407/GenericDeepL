using System;
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
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false))
                {
                    if (key == null)
                        return false;

                    var value = key.GetValue(AppName);
                    return value != null;
                }
            }
            catch (Exception)
            {
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
                var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                
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
    }
}