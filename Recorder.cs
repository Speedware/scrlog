using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace scrlog
{
    /// <summary>
    /// Захват основного экрана и передача кадров в ffmpeg (H.264, нарезка на сегменты MKV).
    /// </summary>
    public class Recorder : IDisposable
    {
        private readonly object _locker = new object();
        private Thread _captureThread;
        private CaptureSession _session;
        private Process _ffmpeg;
        private Stream _ffmpegInput;

        private Point _lastClick;
        private long _lastClickTicks;

        private string _currentFile;
        private DateTime _currentFileCheckedAt;

        public bool IsRecording { get; private set; }
        public DateTime StartedAt { get; private set; }
        public string OutputFolder { get; private set; }
        public string LastError { get; private set; }

        public event Action StateChanged;

        public static Size ScreenSize => Screen.PrimaryScreen.Bounds.Size;

        public TimeSpan Elapsed => IsRecording ? DateTime.Now - StartedAt : TimeSpan.Zero;

        public static string FfmpegPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

        /// <summary>Файл, в который сейчас пишет ffmpeg (самый свежий сегмент).</summary>
        public string CurrentFile
        {
            get
            {
                if (!IsRecording) return null;
                if ((DateTime.Now - _currentFileCheckedAt).TotalSeconds < 3) return _currentFile;
                _currentFileCheckedAt = DateTime.Now;
                try
                {
                    _currentFile = new DirectoryInfo(OutputFolder)
                        .GetFiles("rec_*.mkv")
                        .Where(f => f.LastWriteTime >= StartedAt.AddSeconds(-5))
                        .OrderByDescending(f => f.CreationTime)
                        .Select(f => f.Name)
                        .FirstOrDefault();
                }
                catch
                {
                    _currentFile = null;
                }
                return _currentFile;
            }
        }

        public void RegisterClick(Point screenPos)
        {
            _lastClick = screenPos;
            Interlocked.Exchange(ref _lastClickTicks, DateTime.UtcNow.Ticks);
        }

        public static Size GetOutputSize(VideoQuality quality)
        {
            var screen = ScreenSize;
            int maxWidth;
            switch (quality)
            {
                case VideoQuality.Low: maxWidth = 1024; break;
                case VideoQuality.High: maxWidth = int.MaxValue; break;
                default: maxWidth = 1280; break;
            }
            int width = Math.Min(screen.Width, maxWidth);
            int height = (int)Math.Round(screen.Height * (double)width / screen.Width);
            // yuv420p требует чётные размеры
            return new Size(width & ~1, height & ~1);
        }

        private static string EncoderArgs(VideoQuality quality)
        {
            switch (quality)
            {
                case VideoQuality.Low: return "-crf 38 -maxrate 500k -bufsize 1000k";
                case VideoQuality.High: return "-crf 28 -maxrate 2500k -bufsize 5000k";
                default: return "-crf 33 -maxrate 1000k -bufsize 2000k";
            }
        }

        public bool Start(AppSettings settings)
        {
            lock (_locker)
            {
                if (IsRecording) return true;
                LastError = null;

                if (!File.Exists(FfmpegPath))
                    return Fail("Не найден ffmpeg.exe рядом с программой");

                try
                {
                    Directory.CreateDirectory(settings.LocalFolder);
                }
                catch (Exception ex)
                {
                    return Fail($"Нет доступа к папке {settings.LocalFolder}: {ex.Message}");
                }

                var size = GetOutputSize(settings.Quality);
                int fps = Math.Max(1, Math.Min(30, settings.Fps));
                int segmentSeconds = Math.Max(1, settings.SegmentMinutes) * 60;
                string pattern = Path.Combine(settings.LocalFolder, "rec_%d-%m-%Y_%H-%M-%S.mkv");

                string args = $"-hide_banner -loglevel error -y " +
                              $"-f rawvideo -pix_fmt bgr24 -s {size.Width}x{size.Height} -r {fps} -i - " +
                              $"-c:v libx264 -preset ultrafast -tune zerolatency {EncoderArgs(settings.Quality)} " +
                              $"-pix_fmt yuv420p -g {fps * 10} " +
                              $"-f segment -segment_time {segmentSeconds} -segment_format mkv -reset_timestamps 1 " +
                              $"-strftime 1 \"{pattern}\"";

                var psi = new ProcessStartInfo(FfmpegPath, args)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                try
                {
                    _ffmpeg = Process.Start(psi);
                    _ffmpeg.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data)) Log.Write("ffmpeg: " + e.Data);
                    };
                    _ffmpeg.BeginErrorReadLine();
                    _ffmpegInput = _ffmpeg.StandardInput.BaseStream;
                }
                catch (Exception ex)
                {
                    return Fail($"Не удалось запустить ffmpeg: {ex.Message}");
                }

                OutputFolder = settings.LocalFolder;
                StartedAt = DateTime.Now;
                _currentFileCheckedAt = DateTime.MinValue;
                IsRecording = true;

                // У каждой сессии свой флаг остановки и свой поток ffmpeg: зависший поток
                // прошлой сессии не сможет ни писать в новый ffmpeg, ни остановить его
                var session = new CaptureSession { Input = _ffmpegInput };
                _session = session;
                bool highlightClicks = settings.HighlightClicks;
                _captureThread = new Thread(() => CaptureLoop(session, size, fps, highlightClicks))
                {
                    IsBackground = true,
                    Name = "Capture",
                    Priority = ThreadPriority.AboveNormal
                };
                _captureThread.Start();

                Log.Write($"Запись начата: {size.Width}x{size.Height}, {fps} fps, {settings.Quality}, папка {settings.LocalFolder}");
            }
            StateChanged?.Invoke();
            return true;
        }

        public void Stop()
        {
            Thread thread;
            lock (_locker)
            {
                if (!IsRecording) return;
                _session.StopRequested = true;
                thread = _captureThread;
            }

            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(3000);

            lock (_locker)
            {
                if (!IsRecording) return;
                try
                {
                    _ffmpegInput?.Close();
                    if (_ffmpeg != null && !_ffmpeg.WaitForExit(10000))
                        _ffmpeg.Kill();
                }
                catch (Exception ex)
                {
                    Log.Write($"Ошибка остановки ffmpeg: {ex.Message}");
                }
                finally
                {
                    _ffmpeg?.Dispose();
                    _ffmpeg = null;
                    _ffmpegInput = null;
                    _captureThread = null;
                    _session = null;
                    IsRecording = false;
                    Log.Write("Запись остановлена");
                }
            }
            StateChanged?.Invoke();
        }

        private bool Fail(string error)
        {
            LastError = error;
            Log.Write(error);
            ThreadPool.QueueUserWorkItem(_ => StateChanged?.Invoke());
            return false;
        }

        private class CaptureSession
        {
            public volatile bool StopRequested;
            public Stream Input;
        }

        private void CaptureLoop(CaptureSession session, Size size, int fps, bool highlightClicks)
        {
            Bitmap full = null;
            var frame = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            var buffer = new byte[size.Width * size.Height * 3];
            var clock = Stopwatch.StartNew();
            long framesWritten = 0;

            try
            {
                while (!session.StopRequested)
                {
                    try
                    {
                        var bounds = Screen.PrimaryScreen.Bounds;
                        if (full == null || full.Size != bounds.Size)
                        {
                            full?.Dispose();
                            full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                        }

                        using (var g = Graphics.FromImage(full))
                        {
                            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
                            DrawCursor(g, bounds);
                        }

                        using (var g = Graphics.FromImage(frame))
                        {
                            g.InterpolationMode = InterpolationMode.Bilinear;
                            g.DrawImage(full, new Rectangle(0, 0, size.Width, size.Height));

                            if (highlightClicks)
                                DrawClick(g, bounds, size);
                        }

                        CopyPixels(frame, buffer);
                    }
                    catch (Exception ex)
                    {
                        // Например, экран заблокирован (Win+L) или UAC: пишем последний кадр
                        Debug.WriteLine(ex.Message);
                    }

                    // Держим реальное время: если захват отстаёт, дублируем кадр
                    long due = (long)(clock.Elapsed.TotalSeconds * fps) + 1;
                    long toWrite = Math.Max(1, Math.Min(due - framesWritten, fps * 2));
                    for (long i = 0; i < toWrite; i++)
                        session.Input.Write(buffer, 0, buffer.Length);
                    framesWritten += toWrite;

                    long sleep = framesWritten * 1000 / fps - clock.ElapsedMilliseconds;
                    if (sleep > 0)
                        Thread.Sleep((int)sleep);
                }
            }
            catch (Exception ex)
            {
                if (!session.StopRequested)
                {
                    LastError = "ffmpeg неожиданно завершился";
                    Log.Write($"Ошибка записи: {ex.Message}");
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        if (_session == session) Stop();
                    });
                }
            }
            finally
            {
                full?.Dispose();
                frame.Dispose();
            }
        }

        private void DrawClick(Graphics g, Rectangle bounds, Size size)
        {
            var clickAge = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastClickTicks));
            if (clickAge.TotalSeconds >= 0.5) return;

            float x = (_lastClick.X - bounds.X) * (float)size.Width / bounds.Width;
            float y = (_lastClick.Y - bounds.Y) * (float)size.Height / bounds.Height;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.Red, 3))
                g.DrawEllipse(pen, x - 15, y - 15, 30, 30);
            g.FillEllipse(Brushes.Red, x - 4, y - 4, 8, 8);
        }

        private static void CopyPixels(Bitmap bmp, byte[] buffer)
        {
            var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                int rowBytes = bmp.Width * 3;
                if (data.Stride == rowBytes)
                {
                    Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                }
                else
                {
                    // Строки Bitmap выровнены по 4 байта, ffmpeg ждёт плотную упаковку
                    for (int y = 0; y < bmp.Height; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buffer, y * rowBytes, rowBytes);
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static void DrawCursor(Graphics g, Rectangle bounds)
        {
            var info = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            if (!GetCursorInfo(ref info) || info.flags != CURSOR_SHOWING) return;
            if (!GetIconInfo(info.hCursor, out var iconInfo)) return;

            int x = info.ptScreenPos.X - iconInfo.xHotspot - bounds.X;
            int y = info.ptScreenPos.Y - iconInfo.yHotspot - bounds.Y;
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);

            IntPtr hdc = g.GetHdc();
            try
            {
                DrawIconEx(hdc, x, y, info.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private const int CURSOR_SHOWING = 0x00000001;
        private const int DI_NORMAL = 0x0003;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public POINT ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CURSORINFO pci);

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

        [DllImport("user32.dll")]
        private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy,
            int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
