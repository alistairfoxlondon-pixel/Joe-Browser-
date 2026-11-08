using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JoeBrowser
{
    /// <summary>
    /// Local authenticated-proxy bridge. Chrome talks to 127.0.0.1 without credentials;
    /// the relay talks to the real upstream proxy and keeps the credentials out of browser args.
    /// The upstream is read per connection, so a proxy can be swapped while the browser keeps running.
    /// </summary>
    internal sealed class ProxyRelay : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<ProxyConfig> _provider;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private volatile ProxyConfig _current;
        /* Every socket is pinned to the upstream that was live when it opened. Without the
           epoch a page could keep talking over a connection made through the previous proxy,
           which is exactly the leak a user changes proxy to avoid. */
        private int _epoch;
        private string _epochKey;

        public int Port { get; private set; }

        /// <summary>Raised once when an unreachable proxy is replaced by a direct connection,
        /// so the window can say so instead of the tabs simply going quiet.</summary>
        public event Action<string> Fallback;

        private int _fellBack;

        public ProxyRelay(ProxyConfig proxy)
            : this(delegate { return proxy; })
        {
        }

        public ProxyRelay(Func<ProxyConfig> provider)
        {
            _provider = provider;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(new Func<Task>(AcceptLoopAsync));
        }

        private ProxyConfig Upstream()
        {
            try
            {
                var p = _provider != null ? _provider() : null;
                if (p != null) _current = p;
            }
            catch { }
            var cfg = _current;
            string key = cfg == null || !cfg.Enabled
                ? "direct"
                : (cfg.Type ?? "") + "|" + (cfg.Host ?? "") + "|" + cfg.Port.ToString() + "|" +
                  (cfg.Username ?? "") + "|" + (cfg.Password ?? "") + "|" + (cfg.FallbackDirect ? "1" : "0");
            if (!string.Equals(key, _epochKey, StringComparison.Ordinal))
            {
                _epochKey = key;
                Interlocked.Increment(ref _epoch);
            }
            return _current;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch { break; }
                var ignored = HandleClientAsync(client);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            TcpClient upstream = null;
            try
            {
                client.NoDelay = true;
                var cs = client.GetStream();
                byte[] all = await ReadUntilHeaderEndAsync(cs, _cts.Token).ConfigureAwait(false);
                if (all == null || all.Length == 0) { client.Close(); return; }

                int headerEnd = FindHeaderEnd(all);
                if (headerEnd < 0) { client.Close(); return; }

                string head = Encoding.ASCII.GetString(all, 0, headerEnd + 4);
                int lineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
                string firstLine = lineEnd > 0 ? head.Substring(0, lineEnd) : head;
                string[] parts = firstLine.Split(' ');
                if (parts.Length < 2) { client.Close(); return; }

                bool isConnect = parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase);
                string host;
                int port;
                Uri absolute = null;

                if (isConnect)
                {
                    if (!TryParseHostPort(parts[1], out host, out port)) { client.Close(); return; }
                }
                else
                {
                    if (!Uri.TryCreate(parts[1], UriKind.Absolute, out absolute)) { client.Close(); return; }
                    host = absolute.Host;
                    port = absolute.Port;
                }

                upstream = await ConnectUpstreamAsync(host, port, _cts.Token).ConfigureAwait(false);
                if (upstream == null)
                {
                    await WriteAsync(cs, "HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n").ConfigureAwait(false);
                    client.Close();
                    return;
                }

                var us = upstream.GetStream();

                if (isConnect)
                {
                    await WriteAsync(cs, "HTTP/1.1 200 Connection Established\r\n\r\n").ConfigureAwait(false);
                }
                else
                {
                    byte[] rewritten = RewriteForOrigin(head, absolute, PlainAuthHeader());
                    await us.WriteAsync(rewritten, 0, rewritten.Length).ConfigureAwait(false);
                    if (all.Length > headerEnd + 4)
                    {
                        int extra = all.Length - (headerEnd + 4);
                        await us.WriteAsync(all, headerEnd + 4, extra).ConfigureAwait(false);
                    }
                }

                int epoch = Volatile.Read(ref _epoch);
                var up = PumpAsync(cs, us, _cts.Token, epoch);
                var down = PumpAsync(us, cs, _cts.Token, epoch);
                await Task.WhenAny(up, down).ConfigureAwait(false);
                try { client.Close(); } catch { }
                try { upstream.Close(); } catch { }
            }
            catch (Exception)
            {
                try { client.Close(); } catch { }
                if (upstream != null) { try { upstream.Close(); } catch { } }
            }
        }

        private async Task PumpAsync(NetworkStream from, NetworkStream to, CancellationToken ct, int epoch)
        {
            var buf = new byte[16384];
            try
            {
                while (true)
                {
                    /* The upstream moved: drop this socket so the next request is made through
                       the proxy the user just picked, not the one they replaced. */
                    if (Volatile.Read(ref _epoch) != epoch) break;
                    int n = await from.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
                    if (n <= 0) break;
                    await to.WriteAsync(buf, 0, n).ConfigureAwait(false);
                    await to.FlushAsync().ConfigureAwait(false);
                }
            }
            catch { }
            try { to.Close(); } catch { }
        }

        private async Task<TcpClient> ConnectUpstreamAsync(string host, int port, CancellationToken ct)
        {
            var proxy = Upstream();
            try
            {
                if (proxy == null || !proxy.Enabled)
                    return await ConnectTcpAsync(host, port, ct).ConfigureAwait(false);

                TcpClient tcp = proxy.Type.Equals("socks5", StringComparison.OrdinalIgnoreCase)
                    ? await Socks5ConnectAsync(proxy, host, port, ct).ConfigureAwait(false)
                    : await HttpConnectAsync(proxy, host, port, ct).ConfigureAwait(false);

                if (tcp != null)
                {
                    Interlocked.Exchange(ref _fellBack, 0);
                    return tcp;
                }
                if (proxy.FallbackDirect) return await FallBackAsync(proxy, host, port, ct).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Proxy connect failed " + host + ":" + port, ex);
                if (proxy != null && proxy.Enabled && proxy.FallbackDirect)
                    return await FallBackAsync(proxy, host, port, ct).ConfigureAwait(false);
                return null;
            }
        }

        /// <summary>
        /// Keeps the session usable when the proxy stops answering. The alternative - every tab
        /// hanging until the profile is closed - is what makes an expired proxy look like a
        /// broken app. The caller is told exactly once per outage, and the warning is repeated
        /// only after the proxy has worked again in between.
        /// </summary>
        private async Task<TcpClient> FallBackAsync(ProxyConfig proxy, string host, int port, CancellationToken ct)
        {
            var tcp = await ConnectTcpAsync(host, port, ct).ConfigureAwait(false);
            if (tcp == null) return null;
            if (Interlocked.Exchange(ref _fellBack, 1) == 0)
            {
                Log.Info("Proxy " + proxy.Host + ":" + proxy.Port + " unreachable; using a direct connection");
                var h = Fallback;
                if (h != null)
                {
                    try { h(proxy.Host + ":" + proxy.Port); } catch { }
                }
            }
            return tcp;
        }

        private async Task<TcpClient> HttpConnectAsync(ProxyConfig proxy, string host, int port, CancellationToken ct)
        {
            var tcp = await ConnectTcpAsync(proxy.Host, proxy.Port, ct).ConfigureAwait(false);
            if (tcp == null) return null;

            var sb = new StringBuilder();
            sb.Append("CONNECT ").Append(host).Append(':').Append(port).Append(" HTTP/1.1\r\n");
            sb.Append("Host: ").Append(host).Append(':').Append(port).Append("\r\n");
            sb.Append("Proxy-Connection: Keep-Alive\r\n");
            if (!string.IsNullOrEmpty(proxy.User))
            {
                string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(proxy.User + ":" + (proxy.Password ?? "")));
                sb.Append("Proxy-Authorization: Basic ").Append(token).Append("\r\n");
            }
            sb.Append("\r\n");

            var stream = tcp.GetStream();
            byte[] req = Encoding.ASCII.GetBytes(sb.ToString());
            await stream.WriteAsync(req, 0, req.Length).ConfigureAwait(false);

            byte[] resp = await ReadUntilHeaderEndAsync(stream, ct).ConfigureAwait(false);
            if (resp == null) { tcp.Close(); return null; }
            string text = Encoding.ASCII.GetString(resp);
            if (text.IndexOf(" 200", StringComparison.Ordinal) < 0)
            {
                Log.Info("Proxy CONNECT rejected: " + FirstLine(text));
                tcp.Close();
                return null;
            }
            return tcp;
        }

        private async Task<TcpClient> Socks5ConnectAsync(ProxyConfig proxy, string host, int port, CancellationToken ct)
        {
            var tcp = await ConnectTcpAsync(proxy.Host, proxy.Port, ct).ConfigureAwait(false);
            if (tcp == null) return null;
            var s = tcp.GetStream();

            bool useAuth = !string.IsNullOrEmpty(proxy.User);
            byte[] greet = useAuth ? new byte[] { 5, 2, 0, 2 } : new byte[] { 5, 1, 0 };
            await s.WriteAsync(greet, 0, greet.Length).ConfigureAwait(false);

            byte[] sel = await ReadExactAsync(s, 2, ct).ConfigureAwait(false);
            if (sel == null || sel[0] != 5) { tcp.Close(); return null; }

            if (sel[1] == 2)
            {
                byte[] u = Encoding.UTF8.GetBytes(proxy.User ?? "");
                byte[] p = Encoding.UTF8.GetBytes(proxy.Password ?? "");
                var auth = new byte[3 + u.Length + p.Length];
                auth[0] = 1;
                auth[1] = (byte)u.Length;
                Buffer.BlockCopy(u, 0, auth, 2, u.Length);
                auth[2 + u.Length] = (byte)p.Length;
                Buffer.BlockCopy(p, 0, auth, 3 + u.Length, p.Length);
                await s.WriteAsync(auth, 0, auth.Length).ConfigureAwait(false);
                byte[] ok = await ReadExactAsync(s, 2, ct).ConfigureAwait(false);
                if (ok == null || ok[1] != 0) { tcp.Close(); return null; }
            }
            else if (sel[1] != 0)
            {
                tcp.Close();
                return null;
            }

            byte[] h = Encoding.ASCII.GetBytes(host);
            var req = new byte[7 + h.Length];
            req[0] = 5; req[1] = 1; req[2] = 0; req[3] = 3;
            req[4] = (byte)h.Length;
            Buffer.BlockCopy(h, 0, req, 5, h.Length);
            req[5 + h.Length] = (byte)(port >> 8);
            req[6 + h.Length] = (byte)(port & 0xff);
            await s.WriteAsync(req, 0, req.Length).ConfigureAwait(false);

            byte[] head = await ReadExactAsync(s, 4, ct).ConfigureAwait(false);
            if (head == null || head[0] != 5 || head[1] != 0) { tcp.Close(); return null; }

            int addrLen;
            if (head[3] == 1) addrLen = 4;
            else if (head[3] == 4) addrLen = 16;
            else if (head[3] == 3)
            {
                byte[] l = await ReadExactAsync(s, 1, ct).ConfigureAwait(false);
                if (l == null) { tcp.Close(); return null; }
                addrLen = l[0];
            }
            else { tcp.Close(); return null; }

            byte[] rest = await ReadExactAsync(s, addrLen + 2, ct).ConfigureAwait(false);
            if (rest == null) { tcp.Close(); return null; }
            return tcp;
        }

        private static async Task<TcpClient> ConnectTcpAsync(string host, int port, CancellationToken ct)
        {
            var tcp = new TcpClient();
            tcp.NoDelay = true;
            Task connect = tcp.ConnectAsync(host, port);
            Task completed = await Task.WhenAny(connect, Task.Delay(20000, ct)).ConfigureAwait(false);
            if (completed != connect)
            {
                try { tcp.Close(); } catch { }
                return null;
            }
            try { await connect.ConfigureAwait(false); }
            catch { try { tcp.Close(); } catch { } return null; }
            return tcp;
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream s, int count, CancellationToken ct)
        {
            var buf = new byte[count];
            int off = 0;
            while (off < count)
            {
                int n = await s.ReadAsync(buf, off, count - off, ct).ConfigureAwait(false);
                if (n <= 0) return null;
                off += n;
            }
            return buf;
        }

        private static async Task<byte[]> ReadUntilHeaderEndAsync(NetworkStream s, CancellationToken ct)
        {
            var ms = new System.IO.MemoryStream();
            var buf = new byte[4096];
            while (ms.Length < 131072)
            {
                int n = await s.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
                if (n <= 0) break;
                ms.Write(buf, 0, n);
                if (FindHeaderEnd(ms.GetBuffer(), (int)ms.Length) >= 0) break;
            }
            return ms.Length == 0 ? null : ms.ToArray();
        }

        private static int FindHeaderEnd(byte[] data)
        {
            return FindHeaderEnd(data, data.Length);
        }

        private static int FindHeaderEnd(byte[] data, int length)
        {
            for (int i = 0; i + 3 < length; i++)
            {
                if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
                    return i;
            }
            return -1;
        }

        private static bool TryParseHostPort(string value, out string host, out int port)
        {
            host = null; port = 0;
            if (string.IsNullOrEmpty(value)) return false;
            int idx = value.LastIndexOf(':');
            if (idx <= 0) return false;
            host = value.Substring(0, idx).Trim('[', ']');
            return int.TryParse(value.Substring(idx + 1), out port) && port > 0;
        }

        /// <summary>
        /// CONNECT carries credentials in the tunnel handshake, but an absolute-form plain-HTTP
        /// request has to carry them itself. Without the header an authenticated upstream answers
        /// 407, which the relay would forward to the browser as a proxy login prompt.
        /// </summary>
        private string PlainAuthHeader()
        {
            var proxy = Upstream();
            if (proxy == null || !proxy.Enabled || string.IsNullOrEmpty(proxy.User)) return null;
            if ((proxy.Type ?? "").Equals("socks5", StringComparison.OrdinalIgnoreCase)) return null;
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(proxy.User + ":" + (proxy.Password ?? "")));
            return "Proxy-Authorization: Basic " + token;
        }

        private static byte[] RewriteForOrigin(string head, Uri uri, string auth)
        {
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            if (lines.Length > 0) sb.Append(lines[0].Replace(uri.Scheme + "://" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port), "")).Append("\r\n");
            bool dropClientAuth = auth == null;
            for (int i = 1; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.Length == 0) continue;
                if (l.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase)) continue;
                if (l.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase)) continue;
                if (l.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
                {
                    /* The relay owns the upstream credential; a stale one from the page must not
                       win and authenticate to the proxy as the wrong account. */
                    if (dropClientAuth) sb.Append(l).Append("\r\n");
                    continue;
                }
                sb.Append(l).Append("\r\n");
            }
            if (auth != null) sb.Append(auth).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        private static async Task WriteAsync(NetworkStream s, string text)
        {
            byte[] b = Encoding.ASCII.GetBytes(text);
            await s.WriteAsync(b, 0, b.Length).ConfigureAwait(false);
        }

        private static string FirstLine(string text)
        {
            int i = text.IndexOf("\r\n", StringComparison.Ordinal);
            return i > 0 ? text.Substring(0, i) : text;
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            try { _cts.Dispose(); } catch { }
        }
    }
}
