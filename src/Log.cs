using System;
using System.IO;
using System.Text;

namespace JoeBrowser
{
    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Info(string message) { Write("INFO", message, null); }
        public static void Error(string message, Exception ex = null) { Write("ERROR", message, ex); }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.Logs);
                    string file = Path.Combine(AppPaths.Logs, "joebrowser-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(level).Append(" ").Append(message);
                    if (ex != null) sb.Append(Environment.NewLine).Append(ex);
                    sb.AppendLine();
                    File.AppendAllText(file, sb.ToString(), Encoding.UTF8);

                    var info = new FileInfo(file);
                    if (info.Exists && info.Length > 4 * 1024 * 1024) info.Delete();
                }
            }
            catch { }
        }
    }
}
