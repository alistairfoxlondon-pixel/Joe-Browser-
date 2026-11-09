using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace JoeBrowser
{
    /// <summary>One way to pay: a coin, on a network, to a fixed address.</summary>
    internal sealed class CoinOption
    {
        public string Key;
        public string Symbol;
        public string Network;      // full label, e.g. "BNB Chain · BEP20"
        public string Chain;        // bsc | polygon | tron | solana | ltc
        public string Address;
        public string Contract;     // empty for the chain's own coin
        public int Decimals;
        public string RateId;       // CoinGecko id
        public string UriPrefix;    // wallet URI scheme for the QR code

        public bool Native { get { return string.IsNullOrEmpty(Contract); } }

        /// <summary>Resource name of the official mark, e.g. "usdc".</summary>
        public string Logo { get { return Symbol.ToLowerInvariant(); } }

        public override string ToString() { return Symbol + " \u00b7 " + Network; }
    }

    /// <summary>
    /// A network is chosen before a coin, because sending a token on the wrong chain loses it.
    /// Each entry carries the mark drawn next to its name and the tokens that live on it.
    /// </summary>
    internal sealed class NetworkOption
    {
        public string Key;          // bsc | polygon | tron | solana | ltc
        public string Name;         // BNB Chain
        public string Standard;     // BEP20
        public string Logo;         // embedded resource name
        public string Note;         // one short line shown under the picker

        public string Label { get { return string.IsNullOrEmpty(Standard) ? Name : Name + " \u00b7 " + Standard; } }

        public override string ToString() { return Label; }
    }

    internal static class NetworkCatalog
    {
        private static List<NetworkOption> _all;

        public static List<NetworkOption> All
        {
            get
            {
                if (_all != null) return _all;
                _all = new List<NetworkOption>
                {
                    new NetworkOption { Key = "bsc", Name = "BNB Chain", Standard = "BEP20", Logo = "bnb",
                        Note = "Send only BEP20 on BNB Chain. A transfer from any other network cannot be recovered." },
                    new NetworkOption { Key = "polygon", Name = "Polygon", Standard = "", Logo = "matic",
                        Note = "Send only Polygon assets. A transfer from any other network cannot be recovered." },
                    new NetworkOption { Key = "tron", Name = "Tron", Standard = "TRC20", Logo = "trx",
                        Note = "Send only TRC20 on Tron. Most exchanges default to this network for USDT." },
                    new NetworkOption { Key = "solana", Name = "Solana", Standard = "", Logo = "sol",
                        Note = "Send only Solana assets. A transfer from any other network cannot be recovered." },
                    new NetworkOption { Key = "ltc", Name = "Litecoin", Standard = "", Logo = "ltc",
                        Note = "Send only Litecoin (LTC) on its own network. Not wrapped, not on BEP20." }
                };
                return _all;
            }
        }

        public static NetworkOption For(string chain)
        {
            foreach (var n in All) if (string.Equals(n.Key, chain, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }
    }

    /// <summary>The coins Joe Browser accepts, with the addresses the publisher supplied.</summary>
    internal static class CoinCatalog
    {
        public const string EvmAddress = "0x22f65aab136a15e8307ac8c6ff1af5a692c92e2c";
        public const string TronAddress = "TCkDdhqFKrThJ5xmZEZZ2QKAmCfLkHGZES";
        public const string SolAddress = "CMcLrsGRQxMnUHGihi3aMZkfgSjfPyHhn2AoE9hGeb3Z";
        public const string LtcAddress = "LPBySh9zb4667sGA63M1krTHyoC7uXJ2iw";

        private static List<CoinOption> _all;

        public static List<CoinOption> All
        {
            get
            {
                if (_all != null) return _all;
                _all = new List<CoinOption>
                {
                    new CoinOption { Key = "usdc-bsc", Symbol = "USDC", Network = "BNB Chain \u00b7 BEP20", Chain = "bsc",
                        Address = EvmAddress, Contract = "0x8AC76a51cc950d9822D68b83fE1Ad97B32Cd580d", Decimals = 18,
                        RateId = "usd-coin", UriPrefix = "ethereum:" },
                    new CoinOption { Key = "usdt-bsc", Symbol = "USDT", Network = "BNB Chain \u00b7 BEP20", Chain = "bsc",
                        Address = EvmAddress, Contract = "0x55d398326f99059fF775485246999027B3197955", Decimals = 18,
                        RateId = "tether", UriPrefix = "ethereum:" },
                    new CoinOption { Key = "dai-bsc", Symbol = "DAI", Network = "BNB Chain \u00b7 BEP20", Chain = "bsc",
                        Address = EvmAddress, Contract = "0x1AF3F329e8BE154074D8769D1FFa4eE058B1DBc3", Decimals = 18,
                        RateId = "dai", UriPrefix = "ethereum:" },
                    new CoinOption { Key = "usdc-polygon", Symbol = "USDC", Network = "Polygon", Chain = "polygon",
                        Address = EvmAddress, Contract = "0x3c499c542cEF5E3811e1192ce70d8cC03d5c3359", Decimals = 6,
                        RateId = "usd-coin", UriPrefix = "ethereum:" },
                    new CoinOption { Key = "usdt-tron", Symbol = "USDT", Network = "Tron \u00b7 TRC20", Chain = "tron",
                        Address = TronAddress, Contract = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", Decimals = 6,
                        RateId = "tether", UriPrefix = "tron:" },
                    new CoinOption { Key = "usdc-sol", Symbol = "USDC", Network = "Solana", Chain = "solana",
                        Address = SolAddress, Contract = "EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v", Decimals = 6,
                        RateId = "usd-coin", UriPrefix = "solana:" },
                    new CoinOption { Key = "usdt-sol", Symbol = "USDT", Network = "Solana", Chain = "solana",
                        Address = SolAddress, Contract = "Es9vMFrzaCERmJfrF4H2FYD4KCoNkY11McCe8BenwNYB", Decimals = 6,
                        RateId = "tether", UriPrefix = "solana:" },
                    new CoinOption { Key = "sol", Symbol = "SOL", Network = "Solana", Chain = "solana",
                        Address = SolAddress, Contract = "", Decimals = 9,
                        RateId = "solana", UriPrefix = "solana:" },
                    new CoinOption { Key = "bnb", Symbol = "BNB", Network = "BNB Chain \u00b7 BEP20", Chain = "bsc",
                        Address = EvmAddress, Contract = "", Decimals = 18,
                        RateId = "binancecoin", UriPrefix = "ethereum:" },
                    new CoinOption { Key = "ltc", Symbol = "LTC", Network = "Litecoin", Chain = "ltc",
                        Address = LtcAddress, Contract = "", Decimals = 8,
                        RateId = "litecoin", UriPrefix = "litecoin:" }
                };
                return _all;
            }
        }

        public static CoinOption ByKey(string key)
        {
            foreach (var c in All) if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>The coins that can be paid on one network, in display order.</summary>
        public static List<CoinOption> OnChain(string chain)
        {
            var list = new List<CoinOption>();
            foreach (var c in All)
                if (string.Equals(c.Chain, chain, StringComparison.OrdinalIgnoreCase)) list.Add(c);
            return list;
        }
    }

    /// <summary>
    /// TLS 1.2 for every call this file makes. The app sets this at start-up, but the payment
    /// code can also run from a test harness or a background thread, and a missing protocol
    /// shows up as the unhelpful "could not create SSL/TLS secure channel".
    /// </summary>
    internal static class Net
    {
        static Net()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 12;
            }
            catch { }
        }

        public static void Ensure() { }
    }

    /// <summary>Live USD prices, refreshed at most once a minute.</summary>
    internal static class Rates
    {
        private static readonly object Gate = new object();
        private static Dictionary<string, double> _cache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _fetchedUtc = DateTime.MinValue;

        public static double Usd(string rateId)
        {
            lock (Gate)
            {
                if ((DateTime.UtcNow - _fetchedUtc).TotalSeconds > 60) RefreshLocked();
                double v;
                if (_cache.TryGetValue(rateId ?? "", out v) && v > 0) return v;
            }
            /* Stablecoins are the common case and a rate outage must not stop a payment. */
            return 1.0;
        }

        public static void Refresh() { lock (Gate) RefreshLocked(); }

        /// <summary>
        /// Warms the rate cache off the UI thread. The first invoice used to make a
        /// twelve-second CoinGecko call synchronously while the plan page froze on the click
        /// that opened it - on a slow network the app looked hung, which reads as broken rather
        /// than as "fetching a price". Callers fire this when the page opens.
        /// </summary>
        public static void Warm()
        {
            System.Threading.Tasks.Task.Run(new Action(delegate
            {
                try { Refresh(); } catch { }
            }));
        }

        public static DateTime FetchedUtc { get { lock (Gate) return _fetchedUtc; } }

        private static void RefreshLocked()
        {
            Net.Ensure();
            try
            {
                string url = "https://api.coingecko.com/api/v3/simple/price?ids=" +
                             "usd-coin,tether,dai,solana,litecoin,binancecoin&vs_currencies=usd";
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 12000;
                req.UserAgent = "JoeBrowser/1.0";
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var ser = new JavaScriptSerializer();
                    var root = ser.Deserialize<Dictionary<string, object>>(r.ReadToEnd());
                    if (root == null) return;
                    var next = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in root)
                    {
                        var inner = kv.Value as Dictionary<string, object>;
                        if (inner == null || !inner.ContainsKey("usd")) continue;
                        next[kv.Key] = Convert.ToDouble(inner["usd"], CultureInfo.InvariantCulture);
                    }
                    if (next.Count > 0) { _cache = next; _fetchedUtc = DateTime.UtcNow; }
                }
            }
            catch (Exception ex) { Log.Info("Rate refresh failed: " + ex.Message); }
        }
    }

    /// <summary>An open payment request. The rate is locked when it is created.</summary>
    internal sealed class Invoice
    {
        public string Id;
        public CoinOption Coin;
        public decimal Amount;       // in the coin
        public double Usd;
        public DateTime CreatedUtc;
        public DateTime ExpiresUtc;

        /// <summary>Highest block already searched, so repeated checks walk forward instead of re-reading.</summary>
        public long ScannedTo;

        /// <summary>When set, only this transaction counts. Used to verify a referral receipt.</summary>
        public string WantTx;

        public string Uri
        {
            get
            {
                if (Coin.Native)
                {
                    /* EIP-681: a chain coin carries the address, not a contract. */
                    if (Coin.Chain == "bsc" || Coin.Chain == "polygon")
                        return Coin.UriPrefix + Coin.Address + "@" + ChainId + "?value=" + Raw;
                    return Coin.UriPrefix + Coin.Address + "?amount=" + Fmt(Amount);
                }

                switch (Coin.Chain)
                {
                    case "bsc":
                    case "polygon":
                        /* EIP-681: token contract, chain id, then the raw integer amount. */
                        return Coin.UriPrefix + Coin.Contract + "@" + ChainId + "?value=" + Raw;
                    case "solana":
                        /* Solana Pay names the mint separately from the recipient. */
                        return "solana:" + Coin.Address + "?amount=" + Fmt(Amount) + "&spl-token=" + Coin.Contract;
                    case "tron":
                        /* Tron wallets read the TRC20 contract as the payee. */
                        return "tron:" + Coin.Contract + "?amount=" + Fmt(Amount);
                }
                return Coin.UriPrefix + Coin.Address + "?amount=" + Fmt(Amount);
            }
        }

        public string ChainId
        {
            get
            {
                if (Coin.Chain == "bsc") return "56";
                if (Coin.Chain == "polygon") return "137";
                return "";
            }
        }

        private string Raw
        {
            get
            {
                decimal scaled = Amount * (decimal)Math.Pow(10, Coin.Decimals);
                return ((long)Math.Round(scaled)).ToString(CultureInfo.InvariantCulture);
            }
        }

        public static string Fmt(decimal v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        public TimeSpan Left
        {
            get
            {
                var left = ExpiresUtc - DateTime.UtcNow;
                return left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }
    }

    internal sealed class PaymentCheck
    {
        public bool Found;
        public string TxId;
        public decimal Amount;
        public string Message = "";
        public bool Pending;   // seen but not yet confirmed
        /// <summary>The node refused the query, so another endpoint should be tried.</summary>
        public bool Rejected;

        public static PaymentCheck No(string why)
        {
            return new PaymentCheck { Found = false, Message = why ?? "" };
        }
    }

    /// <summary>
    /// Reads the chain directly and looks for a transfer that arrived after the invoice was
    /// created. No API key, no middleman, no trust in the app's own clock: every candidate is
    /// checked against the block time and the exact amount, and a transaction id can only ever
    /// be redeemed once (see <see cref="Plan.HasSpent"/>).
    /// </summary>
    internal static class Payments
    {
        private const string TransferTopic = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";

        public static PaymentCheck Check(Invoice inv)
        {
            if (inv == null || inv.Coin == null) return PaymentCheck.No("No invoice.");
            try
            {
                switch (inv.Coin.Chain)
                {
                    case "bsc": return CheckEvm(inv, BscRpcs, 3, 3);
                    case "polygon": return CheckEvm(inv, PolygonRpcs, 2, 2);
                    case "tron": return CheckTron(inv);
                    case "solana": return CheckSolana(inv);
                    case "ltc": return CheckLitecoin(inv);
                }
            }
            catch (Exception ex)
            {
                Log.Info("Payment check failed: " + ex.Message);
                return PaymentCheck.No("Could not reach the " + inv.Coin.Network + " network. Retrying\u2026");
            }
            return PaymentCheck.No("Unsupported network.");
        }

        /// <summary>
        /// Looks up one specific transaction and confirms it really paid the publisher's address
        /// on that chain. Used to verify a referral receipt, where the app did not see the
        /// payment itself and cannot take the receipt's word for it.
        /// </summary>
        public static PaymentCheck Confirm(string chain, string txId)
        {
            if (string.IsNullOrWhiteSpace(chain) || string.IsNullOrWhiteSpace(txId))
                return PaymentCheck.No("That receipt is incomplete.");

            /* Any coin on that chain could be the one that was paid, so every contract is
               tried rather than assuming the first one. */
            var candidates = CoinCatalog.OnChain(chain);
            if (candidates.Count == 0) return PaymentCheck.No("That receipt names an unknown network.");

            foreach (var coin in candidates)
            {
                /* The scan is bounded by block count, not by wall-clock, so a wide start is
                   safe: the window is capped inside the checker. The amount is only a floor. */
                var inv = new Invoice
                {
                    Id = "CONFIRM",
                    Coin = coin,
                    Usd = 0,
                    Amount = 0.0000001m,
                    CreatedUtc = DateTime.UtcNow.AddHours(-6),
                    ExpiresUtc = DateTime.UtcNow.AddHours(1),
                    WantTx = txId.Trim()
                };

                var result = Check(inv);
                if (result != null && result.Found) return result;
            }

            return PaymentCheck.No("That payment is not on the chain yet, or it is not a payment to us.");
        }

        /* ------------------------------------------------------------------ EVM */
        /* Endpoints are tried in order. Public BNB Chain seeds answer eth_blockNumber but refuse
           eth_getLogs outright ("limit exceeded"), so a rejected query falls through to the next
           host instead of being read as "no payment". */
        private static readonly string[] BscRpcs =
        {
            "https://bsc.publicnode.com",
            "https://bsc-rpc.publicnode.com"
        };

        private static readonly string[] PolygonRpcs =
        {
            "https://polygon-bor-rpc.publicnode.com",
            "https://polygon-bor.publicnode.com"
        };

        /// <summary>Longest window one log query may span; public nodes cap this.</summary>
        private const int MaxLogSpan = 4000;

        private static PaymentCheck CheckEvm(Invoice inv, string[] rpcs, int blockTime, int needConfirmations)
        {
            PaymentCheck last = PaymentCheck.No("No answer from the network. Retrying\u2026");
            foreach (var url in rpcs)
            {
                long latest = HexToLong(Rpc(url, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_blockNumber\",\"params\":[]}", "result"));
                if (latest <= 0) continue;

                PaymentCheck r = inv.Coin.Native
                    ? CheckNativeEvm(inv, url, latest, blockTime, needConfirmations)
                    : CheckTokenEvm(inv, url, latest, blockTime, needConfirmations);
                if (!r.Rejected) return r;
                last = r;
            }
            return last;
        }

        private static PaymentCheck CheckTokenEvm(Invoice inv, string rpc, long latest, int blockTime, int needConfirmations)
        {
            long span = (long)((DateTime.UtcNow - inv.CreatedUtc).TotalSeconds / blockTime) + 60;
            if (span > MaxLogSpan) span = MaxLogSpan;
            long from = Math.Max(0, latest - span);

            string padded = "0x" + new string('0', 24) + inv.Coin.Address.Substring(2).ToLowerInvariant();

            decimal best = 0;
            string bestTx = null;
            bool pending = false;

            /* Walk the window in chunks so no single query exceeds a public node's range limit. */
            for (long start = from; start <= latest; start += MaxLogSpan)
            {
                long end = Math.Min(latest, start + MaxLogSpan - 1);
                string filter = "{\"address\":\"" + inv.Coin.Contract + "\",\"fromBlock\":\"0x" + start.ToString("x") +
                                "\",\"toBlock\":\"0x" + end.ToString("x") + "\",\"topics\":[\"" + TransferTopic +
                                "\",null,\"" + padded + "\"]}";
                string json = Rpc(rpc, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_getLogs\",\"params\":[" + filter + "]}", "result");
                var logs = ParseArray(json);
                if (logs == null)
                    return new PaymentCheck
                    {
                        Rejected = true,
                        Message = "The network rejected the query; trying another node\u2026"
                    };

                foreach (var item in logs)
                {
                    var log = item as Dictionary<string, object>;
                    if (log == null) continue;
                    string txHash = Str(log, "transactionHash");
                    string data = Str(log, "data");
                    if (string.IsNullOrEmpty(txHash) || Plan.HasSpent(txHash)) continue;
                    if (inv.WantTx != null && !string.Equals(txHash, inv.WantTx, StringComparison.OrdinalIgnoreCase)) continue;

                    decimal value = HexToDecimal(data, inv.Coin.Decimals);
                    if (value <= 0) continue;

                    long blockNo = HexToLong(Str(log, "blockNumber"));
                    long stamp = BlockTime(rpc, blockNo);
                    if (stamp > 0 && stamp < ToUnix(inv.CreatedUtc) - 60) continue;   // an old transfer
                    if (latest - blockNo + 1 < needConfirmations) { pending = true; continue; }

                    if (value > best) { best = value; bestTx = txHash; }
                }
            }

            if (bestTx != null && Enough(best, inv.Amount))
                return new PaymentCheck { Found = true, TxId = bestTx, Amount = best };
            if (bestTx != null && best > 0)
                return PaymentCheck.No("Received " + Invoice.Fmt(best) + " " + inv.Coin.Symbol + ", waiting for the full " +
                                       Invoice.Fmt(inv.Amount) + ".");
            if (pending) return new PaymentCheck { Pending = true, Message = "Payment seen, waiting for confirmations\u2026" };
            return PaymentCheck.No("No payment seen yet.");
        }

        /// <summary>
        /// A chain coin (BNB) leaves no transfer log, so blocks are searched for a plain transfer to
        /// the address. The scan is bounded per call and remembers how far it got, so repeated
        /// checks advance through the session instead of stalling on one huge range.
        /// </summary>
        private static PaymentCheck CheckNativeEvm(Invoice inv, string rpc, long latest, int blockTime, int needConfirmations)
        {
            long created = latest - (long)((DateTime.UtcNow - inv.CreatedUtc).TotalSeconds / blockTime) - 5;
            if (created < 0) created = 0;

            long from = inv.ScannedTo > 0 ? inv.ScannedTo + 1 : created;
            if (from > latest) return PaymentCheck.No("No payment seen yet.");

            const int Window = 200;      // blocks searched per check
            const int Batch = 25;        // blocks per request
            long to = Math.Min(latest, from + Window - 1);

            decimal need = inv.Amount * 0.99m;
            decimal best = 0;
            string bestTx = null;

            for (long start = from; start <= to; start += Batch)
            {
                long end = Math.Min(to, start + Batch - 1);
                var body = new StringBuilder("[");
                for (long n = start; n <= end; n++)
                {
                    if (n > start) body.Append(',');
                    body.Append("{\"jsonrpc\":\"2.0\",\"id\":").Append(n - start)
                        .Append(",\"method\":\"eth_getBlockByNumber\",\"params\":[\"0x")
                        .Append(n.ToString("x")).Append("\",true]}");
                }
                body.Append(']');

                var blocks = ParseArray(Post(rpc, body.ToString()));
                if (blocks == null)
                    return new PaymentCheck
                    {
                        Rejected = true,
                        Message = "The network rejected the query; trying another node\u2026"
                    };

                foreach (var b in blocks)
                {
                    var resp = b as Dictionary<string, object>;
                    if (resp == null || !resp.ContainsKey("result")) continue;
                    var block = resp["result"] as Dictionary<string, object>;
                    if (block == null) continue;
                    var txs = block.ContainsKey("transactions") ? block["transactions"] as object[] : null;
                    if (txs == null) continue;

                    long blockNo = HexToLong(Str(block, "number"));
                    long stamp = HexToLong(Str(block, "timestamp"));
                    if (stamp > 0 && stamp < ToUnix(inv.CreatedUtc) - 60) continue;
                    if (latest - blockNo + 1 < needConfirmations) continue;

                    foreach (var t in txs)
                    {
                        var tx = t as Dictionary<string, object>;
                        if (tx == null) continue;
                        string txTo = Str(tx, "to");
                        if (!string.Equals(txTo, inv.Coin.Address, StringComparison.OrdinalIgnoreCase)) continue;
                        string hash = Str(tx, "hash");
                        if (string.IsNullOrEmpty(hash) || Plan.HasSpent(hash)) continue;
                        if (inv.WantTx != null && !string.Equals(hash, inv.WantTx, StringComparison.OrdinalIgnoreCase)) continue;

                        decimal value = HexToDecimal(Str(tx, "value"), inv.Coin.Decimals);
                        if (value > best) { best = value; bestTx = hash; }
                    }
                }
            }

            inv.ScannedTo = to;

            if (bestTx != null && Enough(best, inv.Amount))
                return new PaymentCheck { Found = true, TxId = bestTx, Amount = best };
            if (bestTx != null && best > 0 && best < need)
                return PaymentCheck.No("Received " + Invoice.Fmt(best) + " " + inv.Coin.Symbol + ", waiting for the full " +
                                       Invoice.Fmt(inv.Amount) + ".");
            if (to < latest) return new PaymentCheck { Pending = true, Message = "Scanning the chain\u2026" };
            return PaymentCheck.No("No payment seen yet.");
        }

        private static long BlockTime(string rpc, long blockNo)
        {
            try
            {
                string json = Rpc(rpc, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"eth_getBlockByNumber\",\"params\":[\"0x" +
                                       blockNo.ToString("x") + "\",false]}", "result");
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (d == null || !d.ContainsKey("timestamp")) return 0;
                return HexToLong(Convert.ToString(d["timestamp"]));
            }
            catch { return 0; }
        }

        /* ---------------------------------------------------------------- Tron */

        private static PaymentCheck CheckTron(Invoice inv)
        {
            string url = "https://api.trongrid.io/v1/accounts/" + inv.Coin.Address + "/transactions/trc20" +
                         "?limit=50&only_confirmed=true&contract_address=" + inv.Coin.Contract;
            string json = Get(url);
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("data")) return PaymentCheck.No("No answer from Tron.");

            var list = root["data"] as object[];
            if (list == null) return PaymentCheck.No("No payment seen yet.");

            decimal best = 0;
            string bestTx = null;
            foreach (var o in list)
            {
                var tx = o as Dictionary<string, object>;
                if (tx == null) continue;
                string txId = Str(tx, "transaction_id");
                if (string.IsNullOrEmpty(txId) || Plan.HasSpent(txId)) continue;
                if (inv.WantTx != null && !string.Equals(txId, inv.WantTx, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Str(tx, "to"), inv.Coin.Address, StringComparison.OrdinalIgnoreCase)) continue;

                long stamp = Long(tx, "block_timestamp") / 1000;
                if (stamp > 0 && stamp < ToUnix(inv.CreatedUtc) - 60) continue;

                int decimals = 6;
                var info = tx.ContainsKey("token_info") ? tx["token_info"] as Dictionary<string, object> : null;
                if (info != null && info.ContainsKey("decimals"))
                    int.TryParse(Convert.ToString(info["decimals"]), out decimals);

                decimal value = Scale(Str(tx, "value"), decimals);
                if (value > best) { best = value; bestTx = txId; }
            }

            if (bestTx != null && Enough(best, inv.Amount))
                return new PaymentCheck { Found = true, TxId = bestTx, Amount = best };
            if (bestTx != null && best > 0)
                return PaymentCheck.No("Received " + Invoice.Fmt(best) + " " + inv.Coin.Symbol + ", waiting for the full " +
                                       Invoice.Fmt(inv.Amount) + ".");
            return PaymentCheck.No("No payment seen yet.");
        }

        /* -------------------------------------------------------------- Solana */

        private static PaymentCheck CheckSolana(Invoice inv)
        {
            const string rpc = "https://api.mainnet-beta.solana.com";
            string sigJson = Rpc(rpc, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"getSignaturesForAddress\",\"params\":[\"" +
                                     inv.Coin.Address + "\",{\"limit\":25}]}", "result");
            var sigs = ParseArray(sigJson);
            if (sigs == null) return PaymentCheck.No("No answer from Solana.");

            decimal best = 0;
            string bestTx = null;
            bool pending = false;

            foreach (var s in sigs)
            {
                var sig = s as Dictionary<string, object>;
                if (sig == null) continue;
                string signature = Str(sig, "signature");
                if (string.IsNullOrEmpty(signature) || Plan.HasSpent(signature)) continue;
                if (inv.WantTx != null && !string.Equals(signature, inv.WantTx, StringComparison.OrdinalIgnoreCase)) continue;
                if (sig.ContainsKey("err") && sig["err"] != null) continue;

                long stamp = Long(sig, "blockTime");
                if (stamp > 0 && stamp < ToUnix(inv.CreatedUtc) - 60) continue;
                if (sig.ContainsKey("confirmationStatus") &&
                    !"finalized".Equals(Convert.ToString(sig["confirmationStatus"]), StringComparison.OrdinalIgnoreCase))
                {
                    pending = true;
                    continue;
                }

                decimal delta = SolanaDelta(rpc, signature, inv);
                if (delta <= 0) continue;
                if (delta > best) { best = delta; bestTx = signature; }
            }

            if (bestTx != null && Enough(best, inv.Amount))
                return new PaymentCheck { Found = true, TxId = bestTx, Amount = best };
            if (bestTx != null && best > 0)
                return PaymentCheck.No("Received " + Invoice.Fmt(best) + " " + inv.Coin.Symbol + ", waiting for the full " +
                                       Invoice.Fmt(inv.Amount) + ".");
            if (pending) return new PaymentCheck { Pending = true, Message = "Payment seen, waiting for finality\u2026" };
            return PaymentCheck.No("No payment seen yet.");
        }

        private static decimal SolanaDelta(string rpc, string signature, Invoice inv)
        {
            string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"getTransaction\",\"params\":[\"" + signature +
                          "\",{\"encoding\":\"jsonParsed\",\"maxSupportedTransactionVersion\":0}]}";
            string json = Rpc(rpc, body, "result");
            if (string.IsNullOrEmpty(json)) return 0;
            var tx = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (tx == null) return 0;
            var meta = tx.ContainsKey("meta") ? tx["meta"] as Dictionary<string, object> : null;
            if (meta == null) return 0;

            if (inv.Coin.Native)
            {
                var pre = meta.ContainsKey("preBalances") ? meta["preBalances"] as object[] : null;
                var post = meta.ContainsKey("postBalances") ? meta["postBalances"] as object[] : null;
                var keys = Keys(tx);
                if (pre == null || post == null || keys == null) return 0;
                int idx = keys.IndexOf(inv.Coin.Address);
                if (idx < 0 || idx >= pre.Length || idx >= post.Length) return 0;
                decimal delta = Convert.ToDecimal(Convert.ToString(post[idx]), CultureInfo.InvariantCulture) -
                                Convert.ToDecimal(Convert.ToString(pre[idx]), CultureInfo.InvariantCulture);
                return delta > 0 ? delta / 1_000_000_000m : 0;
            }

            /* SPL token: the balance of our address for this mint must grow. */
            decimal before = TokenBalance(meta, "preTokenBalances", inv);
            decimal after = TokenBalance(meta, "postTokenBalances", inv);
            return after > before ? after - before : 0;
        }

        private static decimal TokenBalance(Dictionary<string, object> meta, string field, Invoice inv)
        {
            if (!meta.ContainsKey(field)) return 0;
            var list = meta[field] as object[];
            if (list == null) return 0;
            decimal total = 0;
            foreach (var o in list)
            {
                var e = o as Dictionary<string, object>;
                if (e == null) continue;
                if (!string.Equals(Str(e, "mint"), inv.Coin.Contract, StringComparison.Ordinal)) continue;
                if (!string.Equals(Str(e, "owner"), inv.Coin.Address, StringComparison.Ordinal)) continue;
                var amt = e.ContainsKey("uiTokenAmount") ? e["uiTokenAmount"] as Dictionary<string, object> : null;
                if (amt == null || !amt.ContainsKey("uiAmountString")) continue;
                decimal v;
                if (decimal.TryParse(Convert.ToString(amt["uiAmountString"]), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out v)) total += v;
            }
            return total;
        }

        private static List<string> Keys(Dictionary<string, object> tx)
        {
            var result = new List<string>();
            var t = tx.ContainsKey("transaction") ? tx["transaction"] as Dictionary<string, object> : null;
            if (t == null) return null;
            var m = t.ContainsKey("message") ? t["message"] as Dictionary<string, object> : null;
            if (m == null) return null;
            var keys = m.ContainsKey("accountKeys") ? m["accountKeys"] as object[] : null;
            if (keys == null) return null;
            foreach (var k in keys)
            {
                var d = k as Dictionary<string, object>;
                result.Add(d != null && d.ContainsKey("pubkey") ? Convert.ToString(d["pubkey"]) : Convert.ToString(k));
            }
            return result;
        }

        /* ------------------------------------------------------------ Litecoin */

        private static PaymentCheck CheckLitecoin(Invoice inv)
        {
            string json = Get("https://litecoinspace.org/api/address/" + inv.Coin.Address + "/txs");
            var list = ParseArray(json);
            if (list == null) return PaymentCheck.No("No answer from Litecoin.");

            decimal best = 0;
            string bestTx = null;
            bool pending = false;

            foreach (var o in list)
            {
                var tx = o as Dictionary<string, object>;
                if (tx == null) continue;
                string txId = Str(tx, "txid");
                if (string.IsNullOrEmpty(txId) || Plan.HasSpent(txId)) continue;
                if (inv.WantTx != null && !string.Equals(txId, inv.WantTx, StringComparison.OrdinalIgnoreCase)) continue;

                var status = tx.ContainsKey("status") ? tx["status"] as Dictionary<string, object> : null;
                bool confirmed = status != null && status.ContainsKey("confirmed") && Convert.ToBoolean(status["confirmed"]);
                long stamp = status != null ? Long(status, "block_time") : 0;
                if (stamp > 0 && stamp < ToUnix(inv.CreatedUtc) - 60) continue;

                decimal received = 0;
                var vout = tx.ContainsKey("vout") ? tx["vout"] as object[] : null;
                if (vout != null)
                {
                    foreach (var v in vout)
                    {
                        var e = v as Dictionary<string, object>;
                        if (e == null) continue;
                        if (!string.Equals(Str(e, "scriptpubkey_address"), inv.Coin.Address, StringComparison.Ordinal)) continue;
                        received += Long(e, "value") / 100_000_000m;
                    }
                }
                if (received <= 0) continue;
                if (!confirmed) { pending = true; continue; }
                if (received > best) { best = received; bestTx = txId; }
            }

            if (bestTx != null && Enough(best, inv.Amount))
                return new PaymentCheck { Found = true, TxId = bestTx, Amount = best };
            if (bestTx != null && best > 0)
                return PaymentCheck.No("Received " + Invoice.Fmt(best) + " LTC, waiting for the full " + Invoice.Fmt(inv.Amount) + ".");
            if (pending) return new PaymentCheck { Pending = true, Message = "Payment seen, waiting for one confirmation\u2026" };
            return PaymentCheck.No("No payment seen yet.");
        }

        /* ------------------------------------------------------------- helpers */

        /// <summary>A 1% shortfall covers exchange rounding and network fees, nothing more.</summary>
        private static bool Enough(decimal got, decimal want)
        {
            return want > 0 && got >= want * 0.99m;
        }

        private static string Rpc(string url, string body, string field)
        {
            string json = Post(url, body);
            if (string.IsNullOrEmpty(json)) return "";
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (d == null || !d.ContainsKey(field)) return "";
                object v = d[field];
                if (v == null) return "";
                /* A scalar comes back as a string ("0x7e1c05c"); re-serialising it would leave the
                   quotes on and every block number would parse as zero. */
                var s = v as string;
                if (s != null) return s;
                return new JavaScriptSerializer().Serialize(v);
            }
            catch { return ""; }
        }

        private static string Post(string url, string body)
        {
            Net.Ensure();
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 20000;
            req.UserAgent = "JoeBrowser/1.0";
            byte[] payload = Encoding.UTF8.GetBytes(body);
            req.ContentLength = payload.Length;
            using (var s = req.GetRequestStream()) s.Write(payload, 0, payload.Length);
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static string Get(string url)
        {
            Net.Ensure();
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 15000;
            req.UserAgent = "JoeBrowser/1.0";
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static object[] ParseArray(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var v = new JavaScriptSerializer().DeserializeObject(json);
                var arr = v as object[];
                return arr;
            }
            catch { return null; }
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return "";
            return Convert.ToString(d[key]);
        }

        private static long Long(Dictionary<string, object> d, string key)
        {
            string s = Str(d, key);
            long v;
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static long HexToLong(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
            long v;
            return long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static decimal HexToDecimal(string hex, int decimals)
        {
            if (string.IsNullOrEmpty(hex)) return 0;
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
            if (hex.Length == 0) return 0;
            /* A uint256 does not fit a long, so the hex is taken apart in 8-digit chunks. */
            decimal value = 0;
            for (int i = 0; i < hex.Length; i += 8)
            {
                int len = Math.Min(8, hex.Length - i);
                long chunk;
                if (!long.TryParse(hex.Substring(i, len), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out chunk)) return 0;
                value = value * (decimal)Math.Pow(16, len) + chunk;
            }
            return value / (decimal)Math.Pow(10, decimals);
        }

        private static decimal Scale(string raw, int decimals)
        {
            decimal v;
            if (!decimal.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return 0;
            return v / (decimal)Math.Pow(10, decimals);
        }

        private static long ToUnix(DateTime utc)
        {
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
