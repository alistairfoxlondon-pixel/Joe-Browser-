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

        /// <summary>
        /// Extra network tracing. Off by default so the log stays readable, and switched on
        /// from the command line when a machine's network behaviour has to be traced.
        /// </summary>
        public static bool IsDiagnostic
        {
            get
            {
                try { return System.Environment.GetEnvironmentVariable("JOEBROWSER_TRACE_NET") == "1"; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Where the log is, for the "open the log" affordance in the report window and for the
        /// support instructions. Null when logging has never succeeded.
        /// </summary>
        public static string CurrentFile()
        {
            try
            {
                string file = Path.Combine(AppPaths.Logs, "joebrowser-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                return File.Exists(file) ? file : (File.Exists(file + ".1") ? file + ".1" : null);
            }
            catch { return null; }
        }

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

                    /* Rotation DELETED the file with the day's whole history in it, so the one
                       moment a log matters - a user hitting the same error over and over - was
                       the moment it was thrown away. Keep the previous part as .1 instead, and
                       if another process (an antivirus, a second instance) holds the file, fall
                       back to a timestamped part file so the cap still applies. */
                    var info = new FileInfo(file);
                    if (info.Exists && info.Length > 4 * 1024 * 1024)
                    {
                        string previous = file + ".1";
                        try { if (File.Exists(previous)) File.Delete(previous); } catch { }
                        bool moved = false;
                        try { File.Move(file, previous); moved = true; } catch { }
                        if (!moved)
                            try { File.Move(file, file + "." + DateTime.Now.ToString("HHmmss") + ".part"); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
