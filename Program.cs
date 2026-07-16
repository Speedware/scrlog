using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace scrlog
{
    class Program
    {
        // =========   Settings  ==========
        private const int CaptureWidth = 1280;
        private const int CaptureHeight = 1024;
        private const int Fps = 15;
        private const int SegmentSeconds = 1800;
        private const string StorageFolder = @"C:\ScreenLogs";
        private static readonly TimeSpan WorkStart = new TimeSpan(9, 0, 0);
        private static readonly TimeSpan WorkEnd = new TimeSpan(23, 0, 0);
        // ================================

        private static Process _ffmpeg;
        private static Stream _ffmpegInput;
        private static readonly object _locker = new object();
        private static volatile bool _isRecording = false;
        private static volatile bool _shouldStop = false;

        private static Point _lastClick = Point.Empty;
        private static DateTime _lastClickTime = DateTime.MinValue;

        static void Main()
        {
            Console.WriteLine("Screen Recorder starting...");
            Directory.CreateDirectory(StorageFolder);
            FileManager.CleanupOldFiles(StorageFolder);

            var cleanupTimer = new System.Threading.Timer(_ => FileManager.CleanupOldFiles(StorageFolder), null,
                                                          TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));

            Thread hookThread = new Thread(() =>
            {
                Application.Run(new HiddenForm());
            });
            hookThread.SetApartmentState(ApartmentState.STA);
            hookThread.Start();

            Console.WriteLine("Press Ctrl+C to stop...");

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _shouldStop = true;
                Console.WriteLine("Stopping...");
            };

            while (!_shouldStop)
            {
                bool shouldRun = IsScheduledTime();

                if (shouldRun && !_isRecording)
                    StartRecording();
                else if (!shouldRun && _isRecording)
                    StopRecording();

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
                                    g.DrawEllipse(new Pen(Color.Red, 4), _lastClick.X - 15, _lastClick.Y - 15, 30, 30);
                                    g.FillEllipse(Brushes.Red, _lastClick.X - 4, _lastClick.Y - 4, 8, 8);
                                }
                            }

                            var bytes = BitmapToBgr24(bitmap);
                            lock (_locker)
                            {
                                if (_ffmpeg != null && !_ffmpeg.HasExited)
                                    _ffmpegInput.Write(bytes, 0, bytes.Length);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(Path.Combine(StorageFolder, "error.log"),
                            $"{DateTime.Now}: {ex.Message}\n{ex.StackTrace}\n\n");
                    }

                    Thread.Sleep(1000 / Fps);
                }
                else
                {
                    Thread.Sleep(5000);
                }
            }

            StopRecording();
            hookThread.Abort();
            cleanupTimer.Dispose();
            Console.WriteLine("Screen Recorder stopped.");
        }

        private static bool IsScheduledTime()
        {
            var now = DateTime.Now.TimeOfDay;
            return now >= WorkStart && now < WorkEnd;
        }

        private static Bitmap CaptureScreen()
        {
            var bounds = Screen.PrimaryScreen.Bounds;
            var bitmap = new Bitmap(CaptureWidth, CaptureHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                using (var full = CaptureFullScreen())
                {
                    g.DrawImage(full, new Rectangle(0, 0, CaptureWidth, CaptureHeight));
                }
            }
            return bitmap;
        }

        private static Bitmap CaptureFullScreen()
        {
            var bounds = Screen.PrimaryScreen.Bounds;
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            }
            return bmp;
        }

        private static byte[] BitmapToBgr24(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bmp.UnlockBits(data);
            return bytes;
        }

        private static void StartRecording()
        {
            lock (_locker)
            {
                if (_isRecording) return;

                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    Console.WriteLine("ffmpeg.exe not found!");
                    return;
                }

                string args = $"-f rawvideo -pix_fmt bgr24 -s {CaptureWidth}x{CaptureHeight} -r {Fps} -i - " +
                              $"-c:v libx264 -preset ultrafast -tune zerolatency " +
                              $"-crf 38 -maxrate 600k -bufsize 1200k " +
                              $"-pix_fmt yuv420p -flush_packets 1 " +
                              $"-f segment -segment_time {SegmentSeconds} -segment_format mkv -reset_timestamps 1 " +
                              $"-strftime 1 \"{StorageFolder}/rec_%d-%m-%Y_%H-%M.mkv\"";

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
                    Console.WriteLine($"Recording started at {DateTime.Now}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to start FFmpeg: {ex.Message}");
                }
            }
        }

        private static void StopRecording()
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
                    Console.WriteLine($"Stop error: {ex.Message}");
                }
                finally
                {
                    _ffmpeg?.Dispose();
                    _ffmpeg = null;
                    _ffmpegInput = null;
                    _isRecording = false;
                    Console.WriteLine($"Recording stopped at {DateTime.Now}");
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