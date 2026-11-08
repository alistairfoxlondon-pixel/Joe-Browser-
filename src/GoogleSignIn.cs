using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    /// exchanges the returned code for an id token. No embedded browser, no web view, no
    /// password ever touches this app.
    ///
    /// The OAuth client id is the one piece of configuration that belongs to the publisher,
    /// not the binary: it is created in the Google Cloud console ("Desktop app" type) and
    /// pasted once into Settings. PKCE is used as well as the client secret, so a client
    /// created as "Web application" still works as long as http://localhost is an allowed
    /// redirect.
    /// </summary>
    public static class GoogleSignIn
    {
        private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

        /// <summary>Console pages, in the order the setup has to be done.</summary>
        public const string ConsoleProjectUrl = "https://console.cloud.google.com/projectcreate";
        public const string ConsoleConsentUrl = "https://console.cloud.google.com/apis/credentials/consent";
        public const string ConsoleCredentialsUrl = "https://console.cloud.google.com/apis/credentials";

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

        public static string ClientId() { return ClientIdSetting.Trim(); }

        public static bool IsConfigured()
        {
            string id = ClientId();
            return id.Length > 0 &&
                   id.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the string looks like a Google OAuth client id, not a project id.</summary>
        public static bool LooksValid(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId)) return false;
            clientId = clientId.Trim();
            if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase)) return false;
            /* e.g. 123456789012-abcd...-placeholder.apps.googleusercontent.com */
            string head = clientId.Substring(0, clientId.Length - ".apps.googleusercontent.com".Length);
            int dash = head.IndexOf('-');
            if (dash <= 0 || dash == head.Length - 1) return false;
            for (int i = 0; i < dash; i++) if (!char.IsDigit(head[i])) return false;
            return true;
        }

        public static string AuthUrl(string clientId, int port, string state, string challenge)
        {
            string url = AuthEndpoint +
                   "?client_id=" + Uri.EscapeDataString(clientId) +
                   "&redirect_uri=" + Uri.EscapeDataString("http://localhost:" + port) +
                   "&response_type=code" +
                   "&scope=" + Uri.EscapeDataString("openid email profile") +
                   "&state=" + Uri.EscapeDataString(state) +
                   "&prompt=select_account";
            if (!string.IsNullOrEmpty(challenge))
            {
                url += "&code_challenge=" + Uri.EscapeDataString(challenge) +
                       "&code_challenge_method=S256";
            }
            return url;
        }

        /// <summary>Runs the flow. Never throws; the result explains any failure.</summary>
        public static GoogleResult SignIn(string clientId)
        {
            clientId = (clientId ?? "").Trim();
            if (clientId.Length == 0)
                return GoogleResult.Fail("no_client_id",
                    "No Google client id is configured. Paste one in Settings \u203a Account.");

            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string state = Guid.NewGuid().ToString("N");
                string verifier = CodeVerifier();
                string challenge = CodeChallenge(verifier);

                using (var done = new ManualResetEventSlim(false))
                {
                    GoogleResult result = null;

                    var accept = Task.Run(delegate
                    {
                        try
                        {
                            using (TcpClient client = listener.AcceptTcpClient())
                            using (var stream = client.GetStream())
                            {
                                string request = ReadRequest(stream);
                                string url = ExtractTarget(request);
                                var query = ParseQuery(url);

                                bool failed = query.ContainsKey("error");
                                string html =
                                    "<!doctype html><meta charset='utf-8'><title>Joe Browser</title>" +
                                    "<body style='font-family:\"Segoe UI\",system-ui,sans-serif;background:#1b1b1b;color:#f3f3f3;" +
                                    "display:flex;align-items:center;justify-content:center;height:100vh;margin:0'>" +
                                    "<div style='text-align:center'>" +
                                    "<div style='width:44px;height:44px;margin:0 auto 18px;border-radius:12px;" +
                                    "background:#3b82f6;color:#fff;font:600 22px/44px \"Segoe UI\",sans-serif'>J</div>" +
                                    "<h2 style='margin:0 0 6px;font-size:18px'>" +
                                    (failed ? "Sign-in was not completed" : "You are signed in") +
                                    "</h2><p style='margin:0;color:#a0a0a0;font-size:13px'>" +
                                    (failed ? "Close this tab and try again in Joe Browser." : "Close this tab and return to Joe Browser.") +
                                    "</p></div></body>";
                                byte[] body = Encoding.UTF8.GetBytes(html);
                                string head = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                                              "Content-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
                                byte[] headBytes = Encoding.ASCII.GetBytes(head);
                                stream.Write(headBytes, 0, headBytes.Length);
                                stream.Write(body, 0, body.Length);
                                stream.Flush();

                                if (query.ContainsKey("error"))
                                    result = GoogleResult.Fail(query["error"], Describe(query["error"]));
                                else if (!query.ContainsKey("code"))
                                    result = GoogleResult.Fail("invalid_response",
                                        "Google did not return an authorization code.");
                                else if (!query.ContainsKey("state") || query["state"] != state)
                                    result = GoogleResult.Fail("state_mismatch",
                                        "The reply did not match this sign-in attempt. Start it again from Joe Browser.");
                                else
                                    result = Exchange(clientId, query["code"], "http://localhost:" + port, verifier);
                            }
                        }
                        catch (Exception ex) { result = GoogleResult.Fail("listener", ex.Message); }
                        finally { try { done.Set(); } catch { } }
                    });

                    try { Process.Start(AuthUrl(clientId, port, state, challenge)); }
                    catch
                    {
                        return GoogleResult.Fail("no_browser",
                            "Windows has no default browser to open the Google consent page in.");
                    }

                    if (!done.Wait(180000))
                        return GoogleResult.Fail("timeout",
                            "The Google sign-in page was not completed within three minutes.");

                    return result ?? GoogleResult.Fail("cancelled", "Sign-in was cancelled.");
                }
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

        /// <summary>Turns an OAuth error code into the sentence a user needs.</summary>
        public static string Describe(string code)
        {
            switch ((code ?? "").Trim())
            {
                case "access_denied":
                    return "Sign-in was cancelled on the Google page.";
                case "redirect_uri_mismatch":
                    return "Google rejected http://localhost for this client. Create the OAuth client as type \"Desktop app\", " +
                           "which accepts any localhost port automatically.";
                case "invalid_client":
                case "unauthorized_client":
                    return "Google does not recognise this client id. Check it was copied in full and belongs to this project.";
                case "invalid_grant":
                    return "The authorization code expired or was already used. Try again.";
                case "admin_policy_enforced":
                    return "A Google Workspace policy on this account blocks third-party sign-in.";
                case "org_internal":
                    return "This OAuth client is limited to one organisation. Set the consent screen to External and add your address as a test user.";
                case "deleted_client":
                    return "This OAuth client was deleted in the Google Cloud console.";
                default:
                    return string.IsNullOrEmpty(code) ? "Google sign-in failed." : "Google returned: " + code + ".";
            }
        }

        private static GoogleResult Exchange(string clientId, string code, string redirectUri, string verifier)
        {
            string secret = ClientSecret();
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
                string message = Describe(err);
                if (message.StartsWith("Google returned:", StringComparison.Ordinal) && !string.IsNullOrEmpty(desc))
                    message = "Google: " + desc;
                return GoogleResult.Fail(err, message);
            }

            var ser = new JavaScriptSerializer();
            var token = ser.Deserialize<Dictionary<string, object>>(json);
            if (token == null || !token.ContainsKey("id_token"))
                return GoogleResult.Fail("no_id_token", "Google did not return an id token.");

            string info;
            try
            {
                info = Post(UserInfoEndpoint, "",
                    new Dictionary<string, string> { { "Authorization", "Bearer " + Convert.ToString(token["access_token"]) } });
            }
            catch (WebException wex)
            {
                return GoogleResult.Fail("userinfo", "Could not read your Google profile: " + ReadError(wex));
            }

            var user = ser.Deserialize<Dictionary<string, object>>(info);
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

        private static string ClientSecret()
        {
            try
            {
                string v = new ProfileStore().LoadSettings().GoogleClientSecret;
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            catch { }
            return DefaultClientSecret;
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

        private static string Post(string url, string body, Dictionary<string, string> headers = null)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.Timeout = 20000;
            byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
            req.ContentLength = payload.Length;
            using (var s = req.GetRequestStream()) s.Write(payload, 0, payload.Length);
            if (headers != null)
                foreach (var kv in headers) req.Headers[kv.Key] = kv.Value;
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

        private static string ReadRequest(NetworkStream stream)
        {
            var buffer = new byte[8192];
            int read = stream.Read(buffer, 0, buffer.Length);
            return Encoding.ASCII.GetString(buffer, 0, read);
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
                result[Uri.UnescapeDataString(pair.Substring(0, eq)).Replace("+", " ")] =
                    Uri.UnescapeDataString(pair.Substring(eq + 1)).Replace("+", " ");
            }
            return result;
        }
    }
}
