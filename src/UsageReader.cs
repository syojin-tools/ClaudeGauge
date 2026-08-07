using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ClaudeUsageTray
{
    internal sealed class Sample
    {
        public long T;                          // epoch ミリ秒
        public string Org;
        public Dictionary<string, int> U;       // 短縮キー → 使用率(%)

        public int Get(string key)
        {
            int v;
            if (U != null && U.TryGetValue(key, out v)) return v;
            return -1;                          // -1 = 値なし
        }
    }

    internal sealed class UsageSnapshot
    {
        public bool Ok;
        public string ErrorMessage;             // 読み取り不能時の理由（UI にそのまま出す）
        public bool SourceMissing;              // Claude 側のファイル自体が無い＝未ログイン等

        public List<Sample> Samples;            // 最新 org のぶんだけ、時刻昇順
        public string OrgUuid;
        public Dictionary<string, int> Latest;  // 最新サンプルの全グループ

        public DateTime? LastSampleLocal;
        public double AgeMinutes;
        public bool Stale;

        public DateTime? SessionStartLocal;
        public DateTime? SessionResetLocal;
        public string SessionResetNote;

        public DateTime? WeeklyResetLocal;
        public string WeeklyResetNote;

        public int Session { get { return GetLatest(Constants.KeySession); } }
        public int Weekly { get { return GetLatest(Constants.KeyWeekly); } }

        public int GetLatest(string key)
        {
            int v;
            if (Latest != null && Latest.TryGetValue(key, out v)) return v;
            return -1;
        }

        public static UsageSnapshot Failure(string message, bool missing)
        {
            UsageSnapshot s = new UsageSnapshot();
            s.Ok = false;
            s.ErrorMessage = message;
            s.SourceMissing = missing;
            s.Samples = new List<Sample>();
            s.Latest = new Dictionary<string, int>();
            return s;
        }
    }

    /// <summary>
    /// Claude デスクトップアプリが書き出す plan-usage-history.json を読むだけ。
    /// 認証情報には触れない。共有読み取りで開き、本家アプリの書き込みを妨げない。
    /// </summary>
    internal static class UsageReader
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static UsageSnapshot Read(int staleMinutes)
        {
            string path = Constants.UsageHistoryPath;

            // 本家アプリは「一時ファイルへ書いて置き換える」方式なので、
            // 置き換えの一瞬だけ存在しないことがある。数回見直してから「無い」と判断する。
            if (!ExistsWithGrace(path))
            {
                DiagLog.Write("履歴ファイルが見つかりません: {0}", path);
                return UsageSnapshot.Failure(
                    "Claude デスクトップアプリの使用量データがまだありません。\n" +
                    "Claude デスクトップアプリを起動してログインした状態で数分お待ちください。", true);
            }

            string json;
            try
            {
                json = ReadSharedText(path);
            }
            catch (Exception ex)
            {
                DiagLog.Write("履歴ファイルの読み取りに失敗: {0} {1}", ex.GetType().Name, ex.Message);
                return UsageSnapshot.Failure("使用量データを読み取れませんでした（" + ex.GetType().Name + "）。", false);
            }

            UsageSnapshot snap = new UsageSnapshot();
            snap.Samples = new List<Sample>();
            snap.Latest = new Dictionary<string, int>(StringComparer.Ordinal);

            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                object root = ser.DeserializeObject(json);

                IDictionary rootMap = root as IDictionary;
                if (rootMap == null)
                    return UsageSnapshot.Failure("使用量データの形式が想定と異なります（最上位がオブジェクトではありません）。", false);

                int version = ToInt(rootMap[Constants.FieldVersion], -1);
                if (version != Constants.ExpectedHistoryVersion)
                    DiagLog.Write("履歴ファイルのバージョンが想定外です: {0}（想定 {1}）。読める範囲で継続します。",
                        version, Constants.ExpectedHistoryVersion);

                object samplesObj = rootMap[Constants.FieldSamples];
                IEnumerable samples = samplesObj as IEnumerable;
                if (samples == null || samplesObj is string)
                    return UsageSnapshot.Failure("使用量データに samples 配列がありません。", false);

                List<Sample> all = new List<Sample>();
                int malformed = 0;
                foreach (object item in samples)
                {
                    IDictionary m = item as IDictionary;
                    if (m == null) { malformed++; continue; }

                    long t = ToLong(m[Constants.FieldTimestamp], 0);
                    if (t <= 0) { malformed++; continue; }

                    Sample s = new Sample();
                    s.T = t;
                    object org = m[Constants.FieldOrg];
                    s.Org = org == null ? null : org.ToString();
                    s.U = new Dictionary<string, int>(StringComparer.Ordinal);

                    IDictionary u = m[Constants.FieldUsage] as IDictionary;
                    if (u != null)
                    {
                        foreach (DictionaryEntry e in u)
                        {
                            if (e.Key == null) continue;
                            int v = ToInt(e.Value, -1);
                            if (v >= 0) s.U[e.Key.ToString()] = v;
                        }
                    }
                    all.Add(s);
                }

                if (malformed > 0)
                    DiagLog.Write("読み飛ばしたサンプル: {0} 件", malformed);

                if (all.Count == 0)
                    return UsageSnapshot.Failure(
                        "使用量データがまだ記録されていません。Claude デスクトップアプリを起動したままにしてください。", false);

                all.Sort(delegate(Sample a, Sample b) { return a.T.CompareTo(b.T); });

                // 最新サンプルの組織だけを対象にする（複数アカウントの合算はしない）
                string org2 = all[all.Count - 1].Org;
                snap.OrgUuid = org2;
                List<Sample> mine = new List<Sample>();
                foreach (Sample s in all)
                {
                    if (org2 == null || s.Org == null || s.Org == org2) mine.Add(s);
                }
                snap.Samples = mine;

                Sample last = mine[mine.Count - 1];
                foreach (KeyValuePair<string, int> kv in last.U) snap.Latest[kv.Key] = kv.Value;

                DateTime lastUtc = FromMs(last.T);
                snap.LastSampleLocal = lastUtc.ToLocalTime();
                snap.AgeMinutes = (DateTime.UtcNow - lastUtc).TotalMinutes;
                if (snap.AgeMinutes < 0) snap.AgeMinutes = 0;
                snap.Stale = snap.AgeMinutes > staleMinutes;

                EstimateSessionReset(snap, mine);
                EstimateWeeklyReset(snap, mine);

                snap.Ok = true;
                return snap;
            }
            catch (Exception ex)
            {
                DiagLog.Write("履歴ファイルの解析に失敗: {0} {1}", ex.GetType().Name, ex.Message);
                return UsageSnapshot.Failure("使用量データを解釈できませんでした（" + ex.GetType().Name + "）。", false);
            }
        }

        private static bool ExistsWithGrace(string path)
        {
            for (int i = 0; i < 4; i++)
            {
                if (File.Exists(path)) return true;
                if (i < 3) System.Threading.Thread.Sleep(250);
            }
            return false;
        }

        /// <summary>
        /// 本家アプリが書き込み中でも読めるよう共有読み取りで開く。
        /// 一時的なロックは短いバックオフで再試行する。
        /// </summary>
        private static string ReadSharedText(string path)
        {
            IOException lastError = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                               FileShare.ReadWrite | FileShare.Delete, 64 * 1024))
                    using (StreamReader sr = new StreamReader(fs, new UTF8Encoding(false)))
                    {
                        return sr.ReadToEnd();
                    }
                }
                catch (IOException ex)
                {
                    lastError = ex;
                    System.Threading.Thread.Sleep(120 * (attempt + 1));
                }
            }
            throw lastError;
        }

        // ------------------------------------------------------------------
        // 5時間セッションのリセット時刻を履歴から推定する。
        // fh が 0 → 正 に変わった境界がセッション開始。そこから 5 時間後がリセット。
        // ------------------------------------------------------------------
        private static void EstimateSessionReset(UsageSnapshot snap, List<Sample> s)
        {
            int n = s.Count;
            int cur = s[n - 1].Get(Constants.KeySession);

            if (cur < 0)
            {
                snap.SessionResetNote = "取得不可（データに項目がありません）";
                return;
            }
            if (cur == 0)
            {
                snap.SessionResetNote = "セッション未使用";
                return;
            }

            // 直近の「fh > 0」が続いている区間の先頭を探す
            int i = n - 1;
            while (i - 1 >= 0 && s[i - 1].Get(Constants.KeySession) > 0) i--;

            if (i == 0)
            {
                snap.SessionResetNote = "不明（履歴の先頭より前に開始）";
                return;
            }

            // 区間内にデータ欠落があると、別セッションを繋げて見ている恐れがある
            long gapLimit = (long)Constants.DataGapMinutes * 60L * 1000L;
            for (int k = i + 1; k < n; k++)
            {
                if (s[k].T - s[k - 1].T > gapLimit)
                {
                    snap.SessionResetNote = "不明（Claude アプリ停止によるデータ欠落あり）";
                    return;
                }
            }

            // 開始時刻は「最後の 0 のサンプル」と「最初の正のサンプル」の中間とみなす
            long startMs = s[i - 1].T + (s[i].T - s[i - 1].T) / 2;
            DateTime startUtc = FromMs(startMs);
            DateTime resetUtc = startUtc.AddHours(Constants.SessionWindowHours);

            snap.SessionStartLocal = startUtc.ToLocalTime();

            if (resetUtc < DateTime.UtcNow.AddMinutes(-10))
            {
                // 5時間を過ぎているのに fh が 0 に戻っていない＝データが古い
                snap.SessionResetNote = "不明（データが更新されていません）";
                return;
            }

            snap.SessionResetLocal = resetUtc.ToLocalTime();
            snap.SessionResetNote = "推定（誤差 ±5 分）";
        }

        // ------------------------------------------------------------------
        // 週次リセットの推定。履歴内で sd がはっきり下がった時点を前回リセットとみなす。
        // 検出できなければ正直に「不明」と出す。
        // ------------------------------------------------------------------
        private static void EstimateWeeklyReset(UsageSnapshot snap, List<Sample> s)
        {
            int lastDrop = -1;
            for (int i = 1; i < s.Count; i++)
            {
                int prev = s[i - 1].Get(Constants.KeyWeekly);
                int cur = s[i].Get(Constants.KeyWeekly);
                if (prev < 0 || cur < 0) continue;
                if (cur <= prev - Constants.WeeklyResetDropPoints) lastDrop = i;
            }

            if (lastDrop < 0)
            {
                snap.WeeklyResetNote = "不明（履歴内にリセットの記録がありません）";
                return;
            }

            DateTime resetUtc = FromMs(s[lastDrop].T);
            DateTime now = DateTime.UtcNow;
            int guard = 0;
            while (resetUtc <= now && guard++ < 60) resetUtc = resetUtc.AddDays(7);

            snap.WeeklyResetLocal = resetUtc.ToLocalTime();
            snap.WeeklyResetNote = "推定（前回リセットから 7 日周期）";
        }

        // ------------------------------------------------------------------
        private static DateTime FromMs(long ms)
        {
            return Epoch.AddMilliseconds(ms);
        }

        private static int ToInt(object o, int fallback)
        {
            if (o == null) return fallback;
            try { return (int)Math.Round(Convert.ToDouble(o, CultureInfo.InvariantCulture)); }
            catch { return fallback; }
        }

        private static long ToLong(object o, long fallback)
        {
            if (o == null) return fallback;
            try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }
    }
}
