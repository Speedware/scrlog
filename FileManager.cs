using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace scrlog
{
    public static class FileManager
    {
        public static List<FileInfo> ListRecordings(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return new List<FileInfo>();
                return new DirectoryInfo(folder).GetFiles("*.mkv")
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Write($"Не удалось прочитать папку {folder}: {ex.Message}");
                return new List<FileInfo>();
            }
        }

        /// <summary>
        /// Удаляет записи старше <paramref name="days"/> дней. Файлы, ещё не скопированные
        /// в сетевую папку, не трогаем.
        /// </summary>
        public static void CleanupOldFiles(string folder, int days, string networkFolder)
        {
            if (days <= 0) return;
            var cutoff = DateTime.Now.AddDays(-days);
            foreach (var file in ListRecordings(folder))
            {
                try
                {
                    if (file.LastWriteTime >= cutoff || IsLocked(file)) continue;
                    if (!string.IsNullOrWhiteSpace(networkFolder) && !IsCopied(file, networkFolder)) continue;
                    file.Delete();
                    Log.Write($"Удалена старая запись {file.Name}");
                }
                catch (Exception ex)
                {
                    Log.Write($"Не удалось удалить {file.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Копирует завершённые сегменты в сетевую папку. Возвращает false, если папка недоступна.
        /// </summary>
        public static bool SyncToNetwork(string folder, string networkFolder)
        {
            if (string.IsNullOrWhiteSpace(networkFolder)) return true;
            try
            {
                Directory.CreateDirectory(networkFolder);
            }
            catch (Exception ex)
            {
                Log.Write($"Сетевая папка недоступна ({networkFolder}): {ex.Message}");
                return false;
            }

            foreach (var file in ListRecordings(folder))
            {
                try
                {
                    if (IsLocked(file) || IsCopied(file, networkFolder)) continue;
                    var target = Path.Combine(networkFolder, file.Name);
                    var tmp = target + ".part";
                    File.Copy(file.FullName, tmp, true);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                }
                catch (Exception ex)
                {
                    Log.Write($"Не удалось скопировать {file.Name} в сетевую папку: {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        public static bool IsCopied(FileInfo file, string networkFolder)
        {
            var target = new FileInfo(Path.Combine(networkFolder, file.Name));
            return target.Exists && target.Length == file.Length;
        }

        /// <summary>Файл, в который ещё пишет ffmpeg, открыт без FileShare.Write.</summary>
        public static bool IsLocked(FileInfo file)
        {
            try
            {
                using (file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                    return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.0} ГБ";
            if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} МБ";
            return $"{Math.Max(1, bytes / 1024)} КБ";
        }
    }
}
