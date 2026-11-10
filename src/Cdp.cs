using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    /// <summary>
    /// Chrome DevTools Protocol client. Connects to the browser-level endpoint and routes
    /// commands to individual targets through their sessionId, so every tab and window a
    /// profile opens receives the same treatment as the first one.
    /// </summary>
    internal sealed class CdpConnection : IDisposable
    {
        private ClientWebSocket _ws;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> _pending =
            new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer
        {
            /* Full-HD screenshots and large Runtime.evaluate results go far past the default
               ~2 MB cap, which made every such call fail as a misleading "timed out". */
            MaxJsonLength = int.MaxValue
        };
        /* ClientWebSocket allows one SendAsync at a time. Several targets can be stamped in
           parallel when tabs or popups appear, so writes are serialised here. */
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private int _nextId;
        private int _disposed;

        public event Action Closed;

        /// <summary>Fires for CDP events (no id). Arguments are the raw "params" object.</summary>
        public event Action<string, Dictionary<string, object>> Event;

        private CdpConnection(ClientWebSocket ws)
        {
            _ws = ws;
            var ignored = Task.Run(new Func<Task>(ReceiveLoopAsync));
        }

        public static async Task<CdpConnection> ConnectAsync(string wsUrl, int timeoutMs)
        {
            var ws = new ClientWebSocket();
            ws.Options.Proxy = null;
            try
            {
                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    await ws.ConnectAsync(new Uri(wsUrl), cts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                /* A refused handshake used to leave the socket alive until finalizers ran. */
                ws.Dispose();
                throw;
            }
            return new CdpConnection(ws);
        }

        public Task<Dictionary<string, object>> SendAsync(string method, Dictionary<string, object> parameters, int timeoutMs = 15000)
        {
            return SendAsync(method, parameters, null, timeoutMs);
        }

        public async Task<Dictionary<string, object>> SendAsync(string method, Dictionary<string, object> parameters, string sessionId, int timeoutMs)
        {
            int id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<Dictionary<string, object>>();
            _pending[id] = tcs;

            var payload = new Dictionary<string, object> { { "id", id }, { "method", method } };
            if (parameters != null) payload["params"] = parameters;
            if (!string.IsNullOrEmpty(sessionId)) payload["sessionId"] = sessionId;

            byte[] data = Encoding.UTF8.GetBytes(_json.Serialize(payload));
            try
            {
                await _sendLock.WaitAsync(_cts.Token).ConfigureAwait(false);
                try
                {
                    await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
                }
                finally { _sendLock.Release(); }
            }
            catch (Exception ex)
            {
                TaskCompletionSource<Dictionary<string, object>> ignored;
                _pending.TryRemove(id, out ignored);
                throw new Exception("Browser connection lost", ex);
            }

            Task done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (done != tcs.Task)
            {
                TaskCompletionSource<Dictionary<string, object>> ignored;
                _pending.TryRemove(id, out ignored);
                throw new TimeoutException(method + " timed out");
            }
            return await tcs.Task.ConfigureAwait(false);
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[65536];
            var ms = new MemoryStream();
            try
            {
                while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult res = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buffer, 0, res.Count);
                    if (!res.EndOfMessage) continue;

                    string text = Encoding.UTF8.GetString(ms.ToArray());
                    ms.SetLength(0);
                    HandleMessage(text);
                }
            }
            catch { }
            finally { SignalClosed(); }
        }

        private void HandleMessage(string text)
        {
            Dictionary<string, object> msg;
            try { msg = _json.DeserializeObject(text) as Dictionary<string, object>; }
            catch { return; }
            if (msg == null) return;

            object idObj;
            if (!msg.TryGetValue("id", out idObj) || idObj == null)
            {
                object method;
                if (msg.TryGetValue("method", out method) && method != null)
                {
                    var handler = Event;
                    if (handler != null)
                    {
                        object raw;
                        var args = msg.TryGetValue("params", out raw) ? raw as Dictionary<string, object> : null;
                        try { handler(Convert.ToString(method), args ?? new Dictionary<string, object>()); }
                        catch (Exception ex) { Log.Error("CDP event handler failed for " + method, ex); }
                    }
                }
                return;
            }

            int id = Convert.ToInt32(idObj);
            TaskCompletionSource<Dictionary<string, object>> tcs;
            if (!_pending.TryRemove(id, out tcs)) return;

            object err;
            if (msg.TryGetValue("error", out err) && err != null)
            {
                var ed = err as Dictionary<string, object>;
                string message = ed != null && ed.ContainsKey("message") ? Convert.ToString(ed["message"]) : "CDP error";
                tcs.TrySetException(new Exception(message));
                return;
            }

            object result;
            var dict = msg.TryGetValue("result", out result) ? result as Dictionary<string, object> : null;
            tcs.TrySetResult(dict ?? new Dictionary<string, object>());
        }

        private void SignalClosed()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            foreach (var kv in _pending)
            {
                TaskCompletionSource<Dictionary<string, object>> tcs;
                if (_pending.TryRemove(kv.Key, out tcs)) tcs.TrySetException(new Exception("Browser connection closed"));
            }
            var handler = Closed;
            if (handler != null) { try { handler(); } catch { } }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(1000);
            }
            catch { }
            try { if (_ws != null) _ws.Dispose(); } catch { }
            _ws = null;
            try { _sendLock.Dispose(); } catch { }
            SignalClosed();
        }
    }
}
