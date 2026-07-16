using System;
using System.IO;

namespace scrlog
{
    public static class FileManager
    {
        public static void CleanupOldFiles(string folder)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-3);
                foreach (var file in Directory.GetFiles(folder, "*.mkv"))
                {
                    if (File.GetCreationTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
             
            }
        }
    }
}