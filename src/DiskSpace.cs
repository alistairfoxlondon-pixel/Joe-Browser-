using System;
using System.IO;

namespace JoeBrowser
{
    /// <summary>
    /// Free-space checks. A browser workspace needs real room: the bundled Chromium is a
    /// few hundred megabytes and every profile keeps its own cache. When the volume fills,
    /// Chromium fails with messages no user can act on ("Failed to create network context
    /// data directory"), so the app checks first and explains the problem in plain words.
    /// </summary>
    internal static class DiskSpace
    {
        /// <summary>Free bytes on the volume that holds <paramref name="path"/>, or -1 if unknown.</summary>
        public static long Free(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return -1;
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (!d.RootDirectory.FullName.Equals(root, StringComparison.OrdinalIgnoreCase)) continue;
                        return d.AvailableFreeSpace;
                    }
                    catch { }
                }
            }
            catch { }
            return -1;
        }

        public static string Human(long bytes)
        {
            if (bytes < 0) return "unknown";
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.0") + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }

        /// <summary>
        /// Throws a message the user can act on when the target volume cannot hold
        /// <paramref name="neededBytes"/>. Returns the free byte count otherwise.
        /// </summary>
        public static long Require(string path, long neededBytes, string what)
        {
            long free = Free(path);
            if (free >= 0 && free < neededBytes)
                throw new InvalidOperationException(
                    "Not enough free disk space to " + what + ". It needs about " +
                    Human(neededBytes) + " and only " + Human(free) + " is free on this drive. " +
                    "Free some space (or clear old profile caches) and try again.");
            return free;
        }

        /// <summary>True when a raw I/O failure is really the disk being full.</summary>
        public static bool IsOutOfSpace(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                // ERROR_DISK_FULL (0x70) and ERROR_HANDLE_DISK_FULL (0x27)
                int code = e.HResult & 0xFFFF;
                if (code == 0x70 || code == 0x27) return true;
                string m = e.Message ?? "";
                if (m.IndexOf("not enough space", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (m.IndexOf("disk is full", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>Free bytes below which profile caches are cleaned automatically.</summary>
        public const long LowFloor = 1536L * 1024 * 1024;

        /// <summary>
        /// Keeps the app working on small drives. When the volume that holds the profile
        /// store drops under <see cref="LowFloor"/>, the HTTP/code/GPU caches of every closed
        /// profile are emptied - never cookies or logins. Runs on a worker thread; failures
        /// are logged and swallowed because this is a background courtesy, not a step the
        /// user is waiting on.
        /// </summary>
        public static long AutoCleanCaches()
        {
            try
            {
                long free = Free(AppPaths.Data);
                if (free >= 0 && free < LowFloor) return ProfileStore.ClearCaches();
            }
            catch (Exception ex) { Log.Error("Automatic cache clean failed", ex); }
            return 0;
        }
    }
}
