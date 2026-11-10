using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JoeBrowser
{
    /// <summary>
    /// Runs the async work behind a button click without letting a failed <c>await</c> escape.
    ///
    /// Every long action in the app used to be an <c>async void</c> event handler. An
    /// <c>async void</c> method reports its exception on the UI thread through the global
    /// handler, so a timeout while detecting a locale, a failing kernel download or an
    /// unreachable proxy test threw the user out of whatever dialog they were in and showed
    /// "Joe Browser hit an unexpected error". The work now returns a Task and the failure is
    /// shown where it happened.
    /// </summary>
    internal static class Guarded
    {
        /// <summary>
        /// Starts <paramref name="work"/> and shows any exception as themed feedback on
        /// <paramref name="owner"/>. <paramref name="busy"/> is called with true/false so the
        /// caller can disable its buttons while the work runs.
        /// </summary>
        public static void Run(string action, IWin32Window owner, Func<Task> work, Action<bool> busy = null)
        {
            Task task;
            try
            {
                if (busy != null) busy(true);
                task = work();
            }
            catch (Exception ex)
            {
                if (busy != null) busy(false);
                Report(action, owner, ex);
                return;
            }
            if (task == null) { if (busy != null) busy(false); return; }

            task.ContinueWith(delegate(Task t)
            {
                Exception ex = t.Exception == null ? null : t.Exception.GetBaseException();
                try
                {
                    var form = owner as Control;
                    if (form == null || form.IsDisposed || !form.IsHandleCreated)
                    {
                        if (ex != null) Log.Error(action, ex);
                        if (busy != null) busy(false);
                        return;
                    }
                    form.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            if (busy != null) busy(false);
                            if (ex != null) Report(action, owner, ex);
                        }
                        catch (Exception inner)
                        {
                            /* A form disposed between the check and this dispatch used to let the
                               exception reach the crash dialog - the exact thing this class is
                               here to prevent. */
                            Log.Error(action + " (dispatch)", inner);
                        }
                    }));
                }
                catch
                {
                    if (ex != null) Log.Error(action, ex);
                }
            }, TaskScheduler.Default);
        }

        /// <summary>Overload for handlers that already produce a result rather than a Task.</summary>
        public static void Report(string action, IWin32Window owner, Exception ex)
        {
            Log.Error(action, ex);
            if (ex == null) return;
            string hint = "";
            if (ex is UnauthorizedAccessException)
                hint = "\n\nA file or folder is locked. Close any Joe Browser windows that are open and try again.";
            else if (ex is System.IO.IOException)
                hint = "\n\nA file could not be read or written. Another program may be holding it.";
            else if (ex is System.Net.WebException)
                hint = "\n\nThe network request did not complete. Check the connection (and the proxy, if one is set) and try again.";
            else if (ex is TaskCanceledException || ex is OperationCanceledException)
                hint = "\n\nThe step was cancelled before it finished.";

            try { ConfirmForm.Tell(owner, "That did not work", ex.Message, ("" + hint).Trim()); }
            catch { }
        }
    }
}
