using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ClaudeUsageTray
{
    /// <summary>
    /// メモリ上のリングバッファ。詳細ウィンドウの「診断ログを保存」で書き出す。
    /// 認証情報は一切読まないので、ログに混入する余地もない。
    /// </summary>
    internal static class DiagLog
    {
        private const int MaxLines = 800;
        private static readonly object Sync = new object();
        private static readonly Queue<string> Lines = new Queue<string>();

        public static void Write(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                          + "  " + message;
            lock (Sync)
            {
                Lines.Enqueue(line);
                while (Lines.Count > MaxLines) Lines.Dequeue();
            }
        }

        public static void Write(string format, params object[] args)
        {
            Write(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        public static string Dump(Settings settings, UsageSnapshot snapshot)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Claude 使用量トレイ － 診断ログ");
            sb.AppendLine("生成日時 : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("バージョン: " + Constants.AppVersion);
            sb.AppendLine("OS       : " + Environment.OSVersion.Version + " / 64bit=" + Environment.Is64BitOperatingSystem);
            sb.AppendLine("実行ファイル: " + Application_ExecutablePath());
            sb.AppendLine();

            sb.AppendLine("---- 参照先 ----");
            sb.AppendLine("履歴ファイル: " + Constants.UsageHistoryPath);
            try
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(Constants.UsageHistoryPath);
                sb.AppendLine("  存在      : " + fi.Exists);
                if (fi.Exists)
                {
                    sb.AppendLine("  サイズ    : " + fi.Length + " bytes");
                    sb.AppendLine("  最終更新  : " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  情報取得失敗: " + ex.GetType().Name + " " + ex.Message);
            }
            sb.AppendLine();

            sb.AppendLine("---- 設定 ----");
            if (settings != null)
            {
                sb.AppendLine("  更新間隔      : " + settings.PollSeconds + " 秒");
                sb.AppendLine("  通知閾値      : " + settings.WarnPercent + " %");
                sb.AppendLine("  週次切替閾値  : " + settings.WeeklySwitchPercent + " %");
                sb.AppendLine("  通知有効      : " + settings.NotificationsEnabled);
                sb.AppendLine("  データ鮮度閾値: " + settings.StaleMinutes + " 分");
                sb.AppendLine("  自動起動      : " + Settings.GetStartWithWindows());
            }
            sb.AppendLine();

            sb.AppendLine("---- 直近の読み取り結果 ----");
            if (snapshot == null)
            {
                sb.AppendLine("  （まだ読み取っていません）");
            }
            else
            {
                sb.AppendLine("  成功        : " + snapshot.Ok);
                sb.AppendLine("  エラー      : " + (snapshot.ErrorMessage ?? "なし"));
                sb.AppendLine("  サンプル件数: " + (snapshot.Samples == null ? 0 : snapshot.Samples.Count));
                sb.AppendLine("  最終サンプル: " +
                    (snapshot.LastSampleLocal.HasValue
                        ? snapshot.LastSampleLocal.Value.ToString("yyyy-MM-dd HH:mm:ss")
                        : "なし"));
                sb.AppendLine("  データ経過  : " + snapshot.AgeMinutes.ToString("0.0") + " 分");
                sb.AppendLine("  古い判定    : " + snapshot.Stale);
                if (snapshot.Latest != null)
                {
                    foreach (KeyValuePair<string, int> kv in snapshot.Latest)
                        sb.AppendLine("  値 " + kv.Key + " = " + kv.Value + " %");
                }
                sb.AppendLine("  セッション開始(推定): " +
                    (snapshot.SessionStartLocal.HasValue
                        ? snapshot.SessionStartLocal.Value.ToString("yyyy-MM-dd HH:mm")
                        : "不明"));
                sb.AppendLine("  セッションリセット(推定): " +
                    (snapshot.SessionResetLocal.HasValue
                        ? snapshot.SessionResetLocal.Value.ToString("yyyy-MM-dd HH:mm")
                        : "不明") + "  / " + (snapshot.SessionResetNote ?? ""));
                sb.AppendLine("  週次リセット(推定): " +
                    (snapshot.WeeklyResetLocal.HasValue
                        ? snapshot.WeeklyResetLocal.Value.ToString("yyyy-MM-dd HH:mm")
                        : "不明") + "  / " + (snapshot.WeeklyResetNote ?? ""));
            }
            sb.AppendLine();

            sb.AppendLine("---- 動作ログ ----");
            lock (Sync)
            {
                foreach (string l in Lines) sb.AppendLine(l);
            }
            return sb.ToString();
        }

        private static string Application_ExecutablePath()
        {
            try { return System.Windows.Forms.Application.ExecutablePath; }
            catch { return "(取得不可)"; }
        }
    }
}
