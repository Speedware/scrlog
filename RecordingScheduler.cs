using System;
using System.Threading;

namespace scrlog
{
    /// <summary>
    /// Запускает и останавливает запись по расписанию, чистит старые файлы
    /// и копирует готовые сегменты в сетевую папку.
    /// </summary>
    public class RecordingScheduler : IDisposable
    {
        private readonly Recorder _recorder;
        private readonly Func<AppSettings> _settings;
        private readonly System.Threading.Timer _scheduleTimer;
        private readonly System.Threading.Timer _maintenanceTimer;
        private int _maintenanceRunning;

        // Пользователь остановил запись внутри окна расписания — не перезапускаем до следующего окна
        private bool _suppressedUntilWindowEnds;
        private bool _startedBySchedule;

        public bool? NetworkAvailable { get; private set; }

        public event Action NetworkStateChanged;

        public RecordingScheduler(Recorder recorder, Func<AppSettings> settings)
        {
            _recorder = recorder;
            _settings = settings;
            _scheduleTimer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            _maintenanceTimer = new System.Threading.Timer(_ => RunMaintenance(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));
        }

        public void StartManually()
        {
            lock (this)
            {
                _startedBySchedule = false;
                _suppressedUntilWindowEnds = false;
            }
            _recorder.Start(_settings());
        }

        public void StopManually()
        {
            lock (this)
            {
                if (_settings().IsScheduledNow())
                    _suppressedUntilWindowEnds = true;
                _startedBySchedule = false;
            }
            _recorder.Stop();
            ThreadPool.QueueUserWorkItem(_ => RunMaintenance());
        }

        private void Tick()
        {
            var settings = _settings();
            bool inWindow = settings.IsScheduledNow();
            bool start = false, stop = false;

            lock (this)
            {
                if (!inWindow)
                    _suppressedUntilWindowEnds = false;

                if (settings.ScheduleEnabled && inWindow && !_recorder.IsRecording && !_suppressedUntilWindowEnds)
                {
                    start = true;
                    _startedBySchedule = true;
                }
                else if (_recorder.IsRecording && _startedBySchedule && (!inWindow || !settings.ScheduleEnabled))
                {
                    stop = true;
                    _startedBySchedule = false;
                }
            }

            if (start)
            {
                // Если ffmpeg не стартовал, не долбим его каждые 5 секунд — повторим через минуту
                if (!_recorder.Start(settings))
                    _scheduleTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));
            }
            else if (stop)
            {
                _recorder.Stop();
                ThreadPool.QueueUserWorkItem(_ => RunMaintenance());
            }
        }

        public void RunMaintenance()
        {
            if (Interlocked.Exchange(ref _maintenanceRunning, 1) == 1) return;
            try
            {
                var settings = _settings();
                bool? available = string.IsNullOrWhiteSpace(settings.NetworkFolder)
                    ? (bool?)null
                    : FileManager.SyncToNetwork(settings.LocalFolder, settings.NetworkFolder);
                if (available != NetworkAvailable)
                {
                    NetworkAvailable = available;
                    NetworkStateChanged?.Invoke();
                }
                FileManager.CleanupOldFiles(settings.LocalFolder, settings.RetentionDays, settings.NetworkFolder);
            }
            finally
            {
                Interlocked.Exchange(ref _maintenanceRunning, 0);
            }
        }

        public void Dispose()
        {
            _scheduleTimer.Dispose();
            _maintenanceTimer.Dispose();
        }
    }
}
