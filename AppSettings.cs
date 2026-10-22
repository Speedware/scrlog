using System;
using System.IO;
using System.Xml.Serialization;

namespace scrlog
{
    public enum VideoQuality
    {
        Low,
        Standard,
        High
    }

    public class AppSettings
    {
        public static readonly string DataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Scrlog");

        private static readonly string SettingsPath = Path.Combine(DataFolder, "settings.xml");

        public string PcName { get; set; } = Environment.MachineName;
        public bool ScheduleEnabled { get; set; } = true;
        public string ScheduleStart { get; set; } = "08:00";
        public string ScheduleEnd { get; set; } = "23:30";
        public int RetentionDays { get; set; } = 30;
        public string LocalFolder { get; set; } = @"C:\ScreenLogs";
        public string NetworkFolder { get; set; } = "";
        public VideoQuality Quality { get; set; } = VideoQuality.Standard;
        public int Fps { get; set; } = 15;
        public int SegmentMinutes { get; set; } = 30;
        public bool HighlightClicks { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;

        [XmlIgnore]
        public TimeSpan StartTime => ParseTime(ScheduleStart, new TimeSpan(8, 0, 0));

        [XmlIgnore]
        public TimeSpan EndTime => ParseTime(ScheduleEnd, new TimeSpan(23, 30, 0));

        public bool IsScheduledNow()
        {
            var now = DateTime.Now.TimeOfDay;
            if (StartTime <= EndTime)
                return now >= StartTime && now < EndTime;
            // Ночная смена: например 20:00 – 06:00
            return now >= StartTime || now < EndTime;
        }

        public static string FormatTime(TimeSpan t) => t.ToString(@"hh\:mm");

        private static TimeSpan ParseTime(string s, TimeSpan fallback)
        {
            return TimeSpan.TryParse(s, out var t) ? t : fallback;
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    using (var stream = File.OpenRead(SettingsPath))
                        return (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(stream);
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Не удалось прочитать настройки: {ex.Message}");
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                var tmp = SettingsPath + ".tmp";
                using (var stream = File.Create(tmp))
                    new XmlSerializer(typeof(AppSettings)).Serialize(stream, this);
                if (File.Exists(SettingsPath))
                    File.Delete(SettingsPath);
                File.Move(tmp, SettingsPath);
            }
            catch (Exception ex)
            {
                Log.Write($"Не удалось сохранить настройки: {ex.Message}");
            }
        }
    }

    public static class Log
    {
        private static readonly object _locker = new object();

        public static void Write(string message)
        {
            try
            {
                lock (_locker)
                {
                    Directory.CreateDirectory(AppSettings.DataFolder);
                    File.AppendAllText(Path.Combine(AppSettings.DataFolder, "scrlog.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }
    }
}
