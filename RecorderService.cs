using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;

namespace scrlog
{
    public partial class RecorderService : ServiceBase
    {
        private Thread _captureThread;
        private Thread _hookThread;
        private System.Threading.Timer _cleanupTimer;
        private volatile bool _shouldStop = false;
        private volatile bool _isRecording = false;
        private Process _ffmpeg;
        private Stream _ffmpegInput;
        private readonly object _locker = new object();

        // Настройки
        private const int CaptureWidth = 1280;
        private const int CaptureHeight = 1024;
        private const int Fps = 15;
        private const int SegmentSeconds = 60;
        private const string StorageFolder = @"C:\ScreenLogs";

        private static readonly TimeSpan WorkStart = new TimeSpan(9, 0, 0);
        private static readonly TimeSpan WorkEnd = new TimeSpan(23, 0, 0);

        // Для отображения кликов
        private static Point _lastClick = Point.Empty;
        private static DateTime _lastClickTime = DateTime.MinValue;

        public RecorderService()
        {
            ServiceName = "ScreenRecorder";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            Directory.CreateDirectory(StorageFolder);
            FileManager.CleanupOldFiles(StorageFolder);

            _cleanupTimer = new System.Threading.Timer(_ => FileManager.CleanupOldFiles(StorageFolder), null,
                                      TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));

            _captureThread = new Thread(CaptureLoop) { IsBackground = true };
            _captureThread.Start();

            _hookThread = new Thread(() =>
            {
                Application.Run(new HiddenForm());
            });
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();

            EventLog.WriteEntry("ScreenRecorder service started.");
        }

        protected override void OnStop()
        {
            _shouldStop = true;
            StopRecording();
            _captureThread?.Join(5000);
            _hookThread?.Abort();
            _cleanupTimer?.Dispose();
            EventLog.WriteEntry("ScreenRecorder service stopped.");
        }

        private void CaptureLoop()
        {
            while (!_shouldStop)
            {
                bool shouldRun = IsScheduledTime();

                if (shouldRun && !_isRecording)
                {
                    StartRecording();
                }
                else if (!shouldRun && _isRecording)
                {
                    StopRecording();
                }

                if (_isRecording)
                {
                    try
                    {
                        using (var bitmap = CaptureScreen())
                        {
                            if ((DateTime.Now - _lastClickTime).TotalSeconds < 0.5)
                            {
                                using (Graphics g = Graphics.FromImage(bitmap))
                                {
                                    var pen = new Pen(Color.Red, 4);
                                    g.DrawEllipse(pen, _lastClick.X - 15, _lastClick.Y - 15, 30, 30);
                                    g.FillEllipse(Brushes.Red, _lastClick.X - 4, _lastClick.Y - 4, 8, 8);
                                }
                            }

                            var bytes = BitmapToBgr24(bitmap);
                            lock (_locker)
                            {
                                if (_ffmpeg != null && !_ffmpeg.HasExited)
                                {
                                    _ffmpegInput.Write(bytes, 0, bytes.Length);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        EventLog.WriteEntry($"Capture error: {ex.Message}", EventLogEntryType.Error);
                    }

                    Thread.Sleep(1000 / Fps);
                }
                else
                {
                    Thread.Sleep(5000);
                }
            }
        }

        private bool IsScheduledTime()
        {
            var now = DateTime.Now.TimeOfDay;
            return now >= WorkStart && now < WorkEnd;
        }

        private Bitmap CaptureScreen()
        {
            var bounds = Screen.PrimaryScreen.Bounds;
            var bitmap = new Bitmap(CaptureWidth, CaptureHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                using (var full = ScreenCapture.CaptureFullScreen())
                {
                    g.DrawImage(full, new Rectangle(0, 0, CaptureWidth, CaptureHeight));
                }
            }
            return bitmap;
        }

        private static class ScreenCapture
        {
            public static Bitmap CaptureFullScreen()
            {
                var bounds = Screen.PrimaryScreen.Bounds;
                var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
                }
                return bmp;
            }
        }

        private byte[] BitmapToBgr24(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bmp.UnlockBits(data);
            return bytes;
        }

        private void StartRecording()
        {
            lock (_locker)
            {
                if (_isRecording) return;

                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    EventLog.WriteEntry("ffmpeg.exe not found!", EventLogEntryType.Error);
                    return;
                }

                string args = $"-f rawvideo -pix_fmt bgr24 -s {CaptureWidth}x{CaptureHeight} -r {Fps} -i - " +
                              $"-c:v libx264 -preset ultrafast -tune zerolatency " +
                              $"-crf 38 -maxrate 600k -bufsize 1200k " +
                              $"-pix_fmt yuv420p -flush_packets 1 " +
                              $"-f segment -segment_time {SegmentSeconds} -segment_format mkv -reset_timestamps 1 " +
                              $"-strftime 1 \"{StorageFolder}/record_%Y-%m-%d_%H-%M-%S.mkv\"";

                var psi = new ProcessStartInfo(ffmpegPath, args)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                try
                {
                    _ffmpeg = Process.Start(psi);
                    _ffmpegInput = _ffmpeg.StandardInput.BaseStream;
                    _isRecording = true;
                    EventLog.WriteEntry($"Recording started at {DateTime.Now}");
                }
                catch (Exception ex)
                {
                    EventLog.WriteEntry($"Failed to start FFmpeg: {ex.Message}", EventLogEntryType.Error);
                }
            }
        }

        private void StopRecording()
        {
            lock (_locker)
            {
                if (!_isRecording || _ffmpeg == null) return;

                try
                {
                    _ffmpegInput?.Close();
                    if (!_ffmpeg.WaitForExit(10000))
                        _ffmpeg.Kill();
                }
                catch (Exception ex)
                {
                    EventLog.WriteEntry($"Stop error: {ex.Message}", EventLogEntryType.Error);
                }
                finally
                {
                    _ffmpeg?.Dispose();
                    _ffmpeg = null;
                    _ffmpegInput = null;
                    _isRecording = false;
                    EventLog.WriteEntry($"Recording stopped at {DateTime.Now}");
                }
            }
        }

        private class HiddenForm : Form
        {
            private MouseHook _mouseHook;

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                _mouseHook = new MouseHook();
                _mouseHook.OnMouseDown += pos =>
                {
                    var bounds = Screen.PrimaryScreen.Bounds;
                    int x = pos.X * CaptureWidth / bounds.Width;
                    int y = pos.Y * CaptureHeight / bounds.Height;
                    _lastClick = new Point(x, y);
                    _lastClickTime = DateTime.Now;
                };
                _mouseHook.Start();
            }

            protected override void OnClosed(EventArgs e)
            {
                _mouseHook?.Stop();
                base.OnClosed(e);
            }
        }
    }
}