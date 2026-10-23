using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using scrlog.UI;

namespace scrlog
{
    static class Program
    {
        public static readonly int ShowWindowMessage = RegisterWindowMessage("Scrlog.ShowWindow");

        [STAThread]
        static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, @"Local\Scrlog.SingleInstance", out bool isFirst))
            {
                if (!isFirst)
                {
                    // Программа уже запущена (например, свёрнута в трей) — показываем её окно
                    PostMessage((IntPtr)HWND_BROADCAST, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => ShowError("Ошибка в работе Scrlog", e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError("Критическая ошибка Scrlog", e.ExceptionObject as Exception);

                bool startHidden = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
                var settings = AppSettings.Load();
                Log.Write($"Scrlog {Application.ProductVersion} запущен");

                try
                {
                    using (var recorder = new Recorder())
                    using (var scheduler = new RecordingScheduler(recorder, () => settings))
                    {
                        Application.Run(new MainForm(settings, recorder, scheduler, startHidden));
                        scheduler.Dispose();
                        recorder.Stop();
                    }
                }
                catch (Exception ex)
                {
                    ShowError("Не удалось запустить Scrlog", ex);
                }

                Log.Write("Scrlog завершён");
            }
        }

        /// <summary>
        /// Пишет ошибку в журнал и показывает её. Без окна сбой выглядит как
        /// «программа просто не открылась» — понять причину на кассе невозможно.
        /// </summary>
        private static void ShowError(string title, Exception ex)
        {
            Log.Write($"{title}: {ex}");
            try
            {
                MessageBox.Show($"{title}:\n\n{ex?.GetType().Name}: {ex?.Message}\n\nПодробности в журнале:\n{AppSettings.DataFolder}\\scrlog.log",
                    "Scrlog", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
            }
        }

        private const int HWND_BROADCAST = 0xffff;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
