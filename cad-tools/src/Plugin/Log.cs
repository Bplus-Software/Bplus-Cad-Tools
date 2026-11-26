using System;
using System.IO;

namespace BPlus.LiveMap
{
    static class Paths
    {
        public static string Dir { get { string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPlus", "LiveMap"); Directory.CreateDirectory(d); return d; } }
        public static string Settings { get { return Path.Combine(Dir, "settings.txt"); } }
        public static string Usage { get { return Path.Combine(Dir, "usage.txt"); } }
        public static string KeyFile { get { return Path.Combine(Dir, "key.bin"); } }
        public static string LogFile { get { return Path.Combine(Dir, "log.txt"); } }
    }

    static class Log
    {
        static readonly object L = new object();
        public static void Write(string msg)
        {
            try
            {
                lock (L)
                {
                    var f = new FileInfo(Paths.LogFile);
                    if (f.Exists && f.Length > 1000000) f.Delete();
                    File.AppendAllText(Paths.LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
