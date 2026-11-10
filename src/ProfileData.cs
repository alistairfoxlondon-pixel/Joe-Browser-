using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace JoeBrowser
{
    internal enum ClearScope
    {
        /// <summary>HTTP cache, code cache, GPU and shader caches. Keeps the login.</summary>
        Cache,
        /// <summary>Cookies only.</summary>
        Cookies,
        /// <summary>Cookies, local storage, IndexedDB, service workers, cache. Keeps nothing site-side.</summary>
        SiteData,
        /// <summary>The whole profile folder, as if the profile had never been opened.</summary>
        Everything
    }

    /// <summary>
    /// Clearing a profile's data, whether it is open or not. An open profile goes through the
    /// browser's own storage APIs so nothing has to be deleted under a live handle; a closed one
    /// is deleted on disk, which is both faster and complete.
    /// </summary>
    internal static class ProfileData
    {
        private static readonly string[] CacheDirs =
        {
            "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache",
            "GrShaderCache", "ShaderCache", "GraphiteDawnCache", "Media Cache", "CacheStorage",
            "Service Worker\\CacheStorage", "Service Worker\\ScriptCache", "component_crx_cache",
            "Crashpad", "optimization_guide_model_store"
        };

        private static readonly string[] SiteDirs =
        {
            "Local Storage", "Session Storage", "IndexedDB", "Service Worker", "CacheStorage",
            "Shared Dictionary", "blob_storage", "File System", "WebStorage", "Sync Data",
            "Site Characteristics Database", "Platform Notifications"
        };

        private static readonly string[] CookieFiles =
        {
            "Network\\Cookies", "Network\\Cookies-journal", "Cookies", "Cookies-journal",
            /* Chromium keeps a WAL beside the cookie database; deleting the database alone let
               cookies resurrect from it on the next start. */
            "Network\\Cookies-wal", "Cookies-wal"
        };

        public static string DirFor(Profile p)
        {
            return p == null ? null : Path.Combine(AppPaths.Data, p.Id);
        }

        /// <summary>Bytes on disk for this profile, for the "Clear cache (143 MB)" label.</summary>
        public static long Size(Profile p)
        {
            string dir = DirFor(p);
            if (dir == null || !Directory.Exists(dir)) return 0;
            long total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        public static long CacheSize(Profile p)
        {
            string dir = DirFor(p);
            if (dir == null || !Directory.Exists(dir)) return 0;
            long total = 0;
            foreach (string sub in CacheDirs)
            {
                string path = Path.Combine(dir, "Default", sub);
                if (!Directory.Exists(path)) path = Path.Combine(dir, sub);
                if (!Directory.Exists(path)) continue;
                try
                {
                    foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { total += new FileInfo(f).Length; } catch { }
                    }
                }
                catch { }
            }
            return total;
        }

        public static string Human(long bytes)
        {
            /* Invariant formatting: a German locale printed "1,5 MB" here while every parser
               in the app read the invariant decimal point. */
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1073741824.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }

        /// <summary>
        /// Clears what the caller asked for. <paramref name="live"/> is the running session for
        /// this profile, or null when it is closed. Returns the bytes reclaimed when that can be
        /// measured, and 0 for a live clear.
        /// </summary>
        public static async Task<long> ClearAsync(Profile p, ClearScope scope, BrowserSession live)
        {
            if (p == null) throw new ArgumentNullException("p");
            string dir = DirFor(p);

            if (live != null && live.IsRunning)
            {
                /* A live Firefox profile goes straight to disk: its clear must refuse the same
                   way a live Chromium profile refuses when CDP declines - deleting cookie and
                   cache files under a running browser corrupts the store and the clear
                   silently fails or throws mid-walk. */
                if (live.IsFirefox)
                    throw new InvalidOperationException("Close the profile before clearing its data.");
                if (scope == ClearScope.Everything)
                    throw new InvalidOperationException("Close the profile before erasing all of its data.");
                bool ok = await live.ClearStorageAsync(scope).ConfigureAwait(false);
                if (ok) return 0;
                throw new InvalidOperationException("The browser did not accept the clear request. Close the profile and try again.");
            }

            if (dir == null || !Directory.Exists(dir)) return 0;

            if (scope == ClearScope.Everything)
            {
                long before = Size(p);
                string guard = Path.GetFullPath(AppPaths.Data) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(dir);
                if (!full.StartsWith(guard, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing to delete a folder outside the profile store.");
                try { Directory.Delete(full, true); }
                catch (Exception ex) { throw new InvalidOperationException("Some files are still open. Close the profile and try again.", ex); }
                return before;
            }

            long freed = 0;
            string[] roots = { Path.Combine(dir, "Default"), dir };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string sub in scope == ClearScope.Cache ? CacheDirs : scope == ClearScope.Cookies ? new string[0] : SiteDirs)
                {
                    string path = Path.Combine(root, sub);
                    if (!Directory.Exists(path)) continue;
                    freed += FolderSize(path);
                    TryDeleteDir(path);
                }
                if (scope == ClearScope.Cookies || scope == ClearScope.SiteData)
                {
                    foreach (string f in CookieFiles)
                    {
                        string path = Path.Combine(root, f);
                        if (!File.Exists(path)) continue;
                        try { freed += new FileInfo(path).Length; File.Delete(path); }
                        catch { /* counted only when the delete succeeds below; a throw here
                                   leaves the file in place and the caller sees no freed bytes */ }
                    }
                }
            }
            return freed;
        }

        private static long FolderSize(string dir)
        {
            long total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    try { total += new FileInfo(f).Length; } catch { }
            }
            catch { }
            return total;
        }

        private static void TryDeleteDir(string path)
        {
            try { Directory.Delete(path, true); return; } catch { }
            // A locked file (an index journal still held by a scanner) should not stop the rest.
            try
            {
                foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    try { File.Delete(f); } catch { }
                Directory.Delete(path, true);
            }
            catch { }
        }

        /// <summary>Clears every profile's cache at once. Never touches cookies or logins.</summary>
        public static long ClearAllCaches()
        {
            return ProfileStore.ClearCaches();
        }
    }
}
