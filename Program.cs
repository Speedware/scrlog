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
                Application.ThreadException += (s, e) => Log.Write($"Ошибка UI: {e.Exception}");
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write($"Необработанная ошибка: {e.ExceptionObject}");

                bool startHidden = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
                var settings = AppSettings.Load();
                Log.Write($"Scrlog {Application.ProductVersion} запущен");

                using (var recorder = new Recorder())
                using (var scheduler = new RecordingScheduler(recorder, () => settings))
                {
                    Application.Run(new MainForm(settings, recorder, scheduler, startHidden));
                    scheduler.Dispose();
                    recorder.Stop();
                }

                Log.Write("Scrlog завершён");
            }
        }

        private const int HWND_BROADCAST = 0xffff;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
