using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace scrlog
{
    /// <summary>
    /// Автозапуск через ключ Run. Установщик пишет в HKLM (для всех пользователей),
    /// из настроек программы можно включить только для текущего пользователя (HKCU).
    /// </summary>
    public static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Scrlog";

        public static bool EnabledForAllUsers => HasValue(Registry.LocalMachine);

        public static bool EnabledForCurrentUser => HasValue(Registry.CurrentUser);

        public static void SetForCurrentUser(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled)
                        key.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --minimized");
                    else
                        key.DeleteValue(ValueName, false);
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Не удалось изменить автозапуск: {ex.Message}");
            }
        }

        private static bool HasValue(RegistryKey root)
        {
            try
            {
                using (var key = root.OpenSubKey(RunKey))
                    return key?.GetValue(ValueName) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
