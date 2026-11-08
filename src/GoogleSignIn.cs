using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    /// <summary>The identity Google answered with.</summary>
    public class GoogleIdentity
    {
        public string Id;
        public string Email;
        public string Name;
        public string Picture;
    }

    /// <summary>
    /// Outcome of a sign-in attempt. Failures carry the reason Google actually gave, because
    /// "cancelled" for every problem is what makes an OAuth setup impossible to debug.
    /// </summary>
    public class GoogleResult
    {
        public GoogleIdentity Identity;
        /// <summary>Raw OAuth error code, e.g. redirect_uri_mismatch.</summary>
        public string Code;
        /// <summary>One sentence a person can act on.</summary>
        public string Message;

        public bool Ok { get { return Identity != null; } }

        public static GoogleResult Fail(string code, string message)
        {
            return new GoogleResult { Code = code, Message = message };
        }
    }

    /// <summary>
    /// Google sign-in through the standard OAuth 2.0 loopback flow: the app starts a tiny
    /// local HTTP listener, opens Google's consent page in the user's default browser, and
    /// exchanges the returned code for an access token, then reads the profile from Google's
    /// userinfo endpoint. No embedded browser, no web view, no password ever touches this app.
    ///
    /// The OAuth client id is the one piece of configuration that belongs to the publisher,
    /// not the binary: it is created in the Google Cloud console ("Desktop app" type) and
    /// pasted once, either in Settings or in the guided setup the sign-in button opens.
    /// PKCE is used as well as the client secret, so a client created as "Web application"
    /// still works as long as a loopback redirect is allowed.
    /// </summary>
    public static class GoogleSignIn
    {
        private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

        /// <summary>The console page where the OAuth client is created.</summary>
        public const string ConsoleCredentialsUrl = "https://console.cloud.google.com/apis/credentials";

        /// <summary>How long the consent page may stay open before the flow gives up.</summary>
        public const int CallbackTimeoutMs = 180000;

        /// <summary>
        /// The publisher's OAuth client. A Desktop-app client secret is not a secret in the
        /// Google model - it ships inside every copy of an installed app by design - so it
        /// lives here as the default and Settings only has to override it for a different
        /// project. PKCE is used on top, so the secret is not the only thing protecting the
        /// exchange.
        /// </summary>
        public const string DefaultClientId = "";
        public const string DefaultClientSecret = "";

        private static string ClientIdSetting
        {
            get
            {
                try
                {
                    string v = new ProfileStore().LoadSettings().GoogleClientId;
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
                catch { }
                return DefaultClientId;
            }
        }

        /// <summary>The configured client id, cleaned of the quotes and spaces a paste carries.</summary>
        public static string ClientId() { return NormalizeClientId(ClientIdSetting); }

        /// <summary>The stored client secret, or the publisher default.</summary>
        public static string SavedClientSecret()
        {
            try
            {
                string v = new ProfileStore().LoadSettings().GoogleClientSecret;
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            catch { }
            return DefaultClientSecret;
        }

        /// <summary>
        /// Pulls the client id out of whatever the user pasted: the bare id, the id in quotes,
        /// an "id: secret" pair copied from the console, or a whole block of console text.
        /// A paste that silently kept a trailing space used to reach Google as an
        /// "invalid_client" with no explanation.
        /// </summary>
        public static string NormalizeClientId(string raw)
        {
            string s = (raw ?? "").Trim().Trim('"', '\'', '<', '>', ',', ';').Trim();
            if (s.Length == 0) return "";

            const string suffix = ".apps.googleusercontent.com";
            int end = s.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (end < 0) return s;
            end += suffix.Length;

            int start = end;
            while (start > 0)
            {
                char c = s[start - 1];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') start--;
                else break;
            }
            return s.Substring(start, end - start);
        }

        /// <summary>True when the string looks like a Google OAuth client id, not a project id.</summary>
        public static bool LooksValid(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId)) return false;
            clientId = NormalizeClientId(clientId);
            const string suffix = ".apps.googleusercontent.com";
            if (!clientId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
            /* e.g. 123456789012-abcd...-placeholder.apps.googleusercontent.com */
            string head = clientId.Substring(0, clientId.Length - suffix.Length);
            int dash = head.IndexOf('-');
            if (dash <= 0 || dash == head.Length - 1) return false;
            for (int i = 0; i < dash; i++) if (!char.IsDigit(head[i])) return false;
            return true;
        }

        /// <summary>
        /// The consent URL. <paramref name="redirectUri"/> is the loopback address the local
        /// listener actually bound to - see SignIn for why that is 127.0.0.1 and not
        /// "localhost".
        /// </summary>
        public static string AuthUrl(string clientId, string redirectUri, string state, string challenge)
        {
            string url = AuthEndpoint +
                   "?client_id=" + Uri.EscapeDataString(clientId) +
                   "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
                   "&response_type=code" +
                   "&scope=" + Uri.EscapeDataString("openid email profile") +
                   "&state=" + Uri.EscapeDataString(state) +
                   "&access_type=online" +
                   "&prompt=select_account";
            if (!string.IsNullOrEmpty(challenge))
            {
                url += "&code_challenge=" + Uri.EscapeDataString(challenge) +
                       "&code_challenge_method=S256";
            }
            return url;
        }

        /// <summary>Opens the consent page. Never throws.</summary>
        public static bool OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Cannot open " + url, ex);
                return false;
            }
        }

        /// <summary>Runs the flow. Never throws; the result explains any failure.</summary>
        public static GoogleResult SignIn(string clientId)
        {
            clientId = NormalizeClientId(clientId);
            if (clientId.Length == 0)
                return GoogleResult.Fail("no_client_id",
                    "No Google client id is configured yet. Joe Browser can walk you through creating one in two minutes.");
            if (!LooksValid(clientId))
                return GoogleResult.Fail("bad_client_id",
                    "That is not a Google OAuth client id. It ends with .apps.googleusercontent.com, so copy it again from the console.");

            TcpListener listener = null;
            try
            {
                /* Bind the IPv4 loopback explicitly and send Google the matching literal
                   address. "localhost" resolves to ::1 first on Windows, so a listener that
                   only owns 127.0.0.1 never saw the redirect: Chrome showed "connection
                   refused" and the app sat there until it timed out. That was the single
                   most common Google sign-in failure. */
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string redirect = "http://127.0.0.1:" + port;
                string state = Guid.NewGuid().ToString("N");
                string verifier = CodeVerifier();
                string challenge = CodeChallenge(verifier);
                var deadline = DateTime.UtcNow.AddMilliseconds(CallbackTimeoutMs);

                if (!OpenUrl(AuthUrl(clientId, redirect, state, challenge)))
                    return GoogleResult.Fail("no_browser",
                        "Windows has no default browser to open the Google consent page in.");

                GoogleResult result = Listen(listener, deadline, state, clientId, redirect, verifier);
                return result ?? GoogleResult.Fail("timeout",
                    "The Google page was not completed within three minutes. Try again and finish the consent screen.");
            }
            catch (SocketException ex)
            {
                return GoogleResult.Fail("socket", "Could not open the local callback port: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error("Google sign-in", ex);
                return GoogleResult.Fail("unexpected", ex.Message);
            }
            finally
            {
                try { if (listener != null) listener.Stop(); } catch { }
            }
        }

        /// <summary>
        /// Waits for Google's redirect on the local listener. Anything that is not the callback
        /// (the browser's speculative pre-connect, a favicon request, a health probe) is
        /// answered and skipped - taking the first connection at face value was why a sign-in
        /// could report "Google did not return an authorization code" while the real redirect
        /// arrived a moment later.
        /// </summary>
        private static GoogleResult Listen(TcpListener listener, DateTime deadline, string state,
            string clientId, string redirect, string verifier)
        {
            while (DateTime.UtcNow < deadline)
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(40);
                    continue;
                }

                try
                {
                    using (var client = listener.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = 5000;
                        using (NetworkStream stream = client.GetStream())
                        {
                            string request = ReadRequest(stream);
                            string target = ExtractTarget(request);
                            var query = ParseQuery(target);

                            if (!query.ContainsKey("code") && !query.ContainsKey("error"))
                            {
                                Reply(stream, 404, false, "Joe Browser",
                                    "Waiting for Google",
                                    "This tab is the local callback for the sign-in. Close it and finish in the Google tab that just opened.");
                                continue;
                            }

                            GoogleResult result;
                            if (query.ContainsKey("error"))
                            {
                                string code = query["error"];
                                string description = query.ContainsKey("error_description") ? query["error_description"] : null;
                                result = GoogleResult.Fail(code, Describe(code, description));
                            }
                            else if (!query.ContainsKey("state") || query["state"] != state)
                            {
                                result = GoogleResult.Fail("state_mismatch",
                                    "The reply did not match this sign-in attempt. Start it again from Joe Browser.");
                            }
                            else
                            {
                                result = Exchange(clientId, query["code"], redirect, verifier);
                            }

                            Reply(stream, 200, result.Ok, "Joe Browser",
                                result.Ok ? "You are signed in" : "Sign-in was not completed",
                                result.Ok ? "Close this tab and return to Joe Browser."
                                          : (result.Message ?? "Close this tab and try again in Joe Browser.") +
                                            " You can close this tab.");
                            return result;
                        }
                    }
                }
                catch (Exception ex)
                {
                    /* A connection that dies before it sends a request is not a failure of the
                       sign-in: keep waiting for the real redirect. */
                    Log.Info("Google callback connection ignored: " + ex.Message);
                }
            }
            return null;
        }

        public static string Describe(string code, string description)
        {
            switch ((code ?? "").Trim())
            {
                case "access_denied":
                    return "Sign-in was cancelled on the Google page.";
                case "redirect_uri_mismatch":
                    return "Google rejected the loopback redirect for this client. Create the OAuth client as type \"Desktop app\", " +
                           "which accepts 127.0.0.1 on any port automatically, then try again.";
                case "invalid_client":
                case "unauthorized_client":
                    return "Google does not recognise this client id. Check it was copied in full and belongs to this project.";
                case "invalid_grant":
                    return "The authorization code expired or was already used. Try again.";
                case "invalid_request":
                    return "Google rejected the token exchange (invalid_request)." +
                           (string.IsNullOrEmpty(description) ? "" : " Google said: " + description + ".");
                case "unsupported_grant_type":
                    return "This OAuth client is not enabled for the authorization-code flow.";
                case "admin_policy_enforced":
                    return "A Google Workspace policy on this account blocks third-party sign-in.";
                case "org_internal":
                    return "This OAuth client is limited to one organisation. Set the consent screen to External and add your address as a test user.";
                case "deleted_client":
                    return "This OAuth client was deleted in the Google Cloud console.";
                case "bad_client_id":
                    return "That is not a Google OAuth client id. It ends with .apps.googleusercontent.com.";
                default:
                    if (string.IsNullOrEmpty(code)) return "Google sign-in failed.";
                    if (!string.IsNullOrEmpty(description)) return "Google returned " + code + ": " + description;
                    return "Google returned: " + code + ".";
            }
        }

        /// <summary>
        /// True when the failure is something a person can fix in the guided setup rather than
        /// something that went wrong in transit. The sign-in UI uses it to offer the setup form
        /// instead of a dead end.
        /// </summary>
        public static bool IsSetupProblem(string code)
        {
            switch ((code ?? "").Trim())
            {
                case "no_client_id":
                case "bad_client_id":
                case "invalid_client":
                case "unauthorized_client":
                case "redirect_uri_mismatch":
                case "deleted_client":
                case "org_internal":
                case "invalid_request":
                    return true;
                default:
                    return false;
            }
        }

        private static GoogleResult Exchange(string clientId, string code, string redirectUri, string verifier)
        {
            string secret = SavedClientSecret();
            var form = new StringBuilder();
            form.Append("code=").Append(Uri.EscapeDataString(code));
            form.Append("&client_id=").Append(Uri.EscapeDataString(clientId));
            form.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
            form.Append("&grant_type=authorization_code");
            /* PKCE is what makes a "Web application" client work here; the secret is still
               sent when one was pasted, because Google requires it for that client type. */
            if (!string.IsNullOrEmpty(verifier)) form.Append("&code_verifier=").Append(Uri.EscapeDataString(verifier));
            if (!string.IsNullOrEmpty(secret)) form.Append("&client_secret=").Append(Uri.EscapeDataString(secret));

            string json;
            try { json = Post(TokenEndpoint, form.ToString()); }
            catch (WebException wex)
            {
                string body = ReadError(wex);
                string err = JsonField(body, "error");
                string desc = JsonField(body, "error_description");
                Log.Info("Google token exchange failed: " + err + " " + desc);
                string message = Describe(err, desc);
                if (string.IsNullOrEmpty(err) && wex.Status == WebExceptionStatus.Timeout)
                    message = "Google did not answer the token exchange in time. Check the connection and try again.";
                return GoogleResult.Fail(string.IsNullOrEmpty(err) ? "token_exchange" : err, message);
            }
            catch (Exception ex)
            {
                Log.Error("Google token exchange", ex);
                return GoogleResult.Fail("token_exchange",
                    "Could not reach Google's token endpoint: " + ex.Message +
                    ". Check the connection (and any proxy) and try again.");
            }

            var ser = new JavaScriptSerializer();
            Dictionary<string, object> token = null;
            try { token = ser.Deserialize<Dictionary<string, object>>(json); } catch { }
            if (token == null || !token.ContainsKey("access_token"))
                return GoogleResult.Fail("no_token", "Google did not return an access token for this client.");

            string info;
            try
            {
                /* userinfo is a GET endpoint: a POST with an empty body answers 405 and looked
                   like a broken token. */
                info = Get(UserInfoEndpoint, "Bearer " + Convert.ToString(token["access_token"]));
            }
            catch (WebException wex)
            {
                return GoogleResult.Fail("userinfo", "Could not read your Google profile: " + ReadError(wex));
            }
            catch (Exception ex)
            {
                Log.Error("Google userinfo", ex);
                return GoogleResult.Fail("userinfo", "Could not read your Google profile: " + ex.Message);
            }

            Dictionary<string, object> user = null;
            try { user = ser.Deserialize<Dictionary<string, object>>(info); } catch { }
            if (user == null || !user.ContainsKey("sub"))
                return GoogleResult.Fail("userinfo", "Google did not return an account id.");

            return new GoogleResult
            {
                Identity = new GoogleIdentity
                {
                    Id = Convert.ToString(user["sub"]),
                    Email = user.ContainsKey("email") ? Convert.ToString(user["email"]) : "",
                    Name = user.ContainsKey("name") ? Convert.ToString(user["name"]) : "",
                    Picture = user.ContainsKey("picture") ? Convert.ToString(user["picture"]) : ""
                }
            };
        }

        /* ---- PKCE (RFC 7636): a random verifier, and its SHA-256 sent to Google ---- */

        private static string CodeVerifier()
        {
            var bytes = new byte[32];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        private static string CodeChallenge(string verifier)
        {
            using (var sha = SHA256.Create())
                return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Post(string url, string body)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.UserAgent = "JoeBrowser/1.2 (+winforms)";
            byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
            req.ContentLength = payload.Length;
            using (var s = req.GetRequestStream()) s.Write(payload, 0, payload.Length);
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static string Get(string url, string bearer)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.UserAgent = "JoeBrowser/1.2 (+winforms)";
            req.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static string ReadError(WebException wex)
        {
            try
            {
                if (wex.Response == null) return wex.Message;
                using (var r = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch { return wex.Message; }
        }

        /// <summary>Reads one top-level string field without a full parse, for error bodies.</summary>
        private static string JsonField(string json, string name)
        {
            if (string.IsNullOrEmpty(json)) return "";
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (d != null && d.ContainsKey(name)) return Convert.ToString(d[name]);
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Reads the request head. A single Read() can return half a request on a slow or
        /// fragmented connection, which is why the target used to come back empty; this keeps
        /// reading until the blank line that ends the headers.
        /// </summary>
        private static string ReadRequest(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var sb = new StringBuilder();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (sb.Length < 16384 && DateTime.UtcNow < deadline)
            {
                int read;
                try { read = stream.Read(buffer, 0, buffer.Length); }
                catch (IOException) { break; }
                if (read <= 0) break;
                sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (sb.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) >= 0) break;
            }
            return sb.ToString();
        }

        private static string ExtractTarget(string request)
        {
            if (string.IsNullOrEmpty(request)) return "";
            int i = request.IndexOf(' ');
            if (i < 0) return "";
            int j = request.IndexOf(' ', i + 1);
            return j < 0 ? request.Substring(i + 1) : request.Substring(i + 1, j - i - 1);
        }

        private static Dictionary<string, string> ParseQuery(string target)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            int q = (target ?? "").IndexOf('?');
            if (q < 0) return result;
            foreach (string pair in target.Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                /* '+' is a space in a form-encoded query, but it is also a legal character in
                   a value; substituting before unescaping is the only order that keeps a
                   %2B (a literal plus) intact. */
                result[Uri.UnescapeDataString(pair.Substring(0, eq).Replace("+", " "))] =
                    Uri.UnescapeDataString(pair.Substring(eq + 1).Replace("+", " "));
            }
            return result;
        }

        /// <summary>The page the browser lands on, matching the app's own look.</summary>
        private static void Reply(NetworkStream stream, int status, bool ok, string title, string heading, string message)
        {
            string html =
                "<!doctype html><meta charset='utf-8'><title>" + Web(title) + "</title>" +
                "<body style='font-family:\"Segoe UI\",system-ui,-apple-system,sans-serif;background:#111111;color:#f3f3f3;" +
                "display:flex;align-items:center;justify-content:center;height:100vh;margin:0'>" +
                "<div style='text-align:center;max-width:420px;padding:0 24px'>" +
                "<div style='width:52px;height:52px;margin:0 auto 20px;border-radius:14px;" +
                "background:" + (ok ? "#2f9e44" : "#3AA6FF") + ";color:#fff;font:600 24px/52px \"Segoe UI\",sans-serif'>" +
                (ok ? "&#10003;" : "J") + "</div>" +
                "<h2 style='margin:0 0 8px;font-size:19px;font-weight:600'>" + Web(heading) + "</h2>" +
                "<p style='margin:0;color:#a0a0a0;font-size:13.5px;line-height:1.5'>" + Web(message) + "</p>" +
                "</div></body>";
            byte[] body = Encoding.UTF8.GetBytes(html);
            string head = "HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Not Found") + "\r\n" +
                          "Content-Type: text/html; charset=utf-8\r\n" +
                          "Cache-Control: no-store\r\n" +
                          "Content-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
            try
            {
                byte[] headBytes = Encoding.ASCII.GetBytes(head);
                stream.Write(headBytes, 0, headBytes.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
            catch { }
        }

        private static string Web(string s)
        {
            return (s ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
