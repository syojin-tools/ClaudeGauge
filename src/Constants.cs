using System;
using System.Collections.Generic;
using System.IO;

namespace ClaudeUsageTray
{
    /// <summary>
    /// 外部仕様（Claude デスクトップアプリ側のファイル形式）に依存する値をここに集約する。
    /// Anthropic 側の変更で壊れた場合、原則このファイルだけを直せば復旧できるようにしておく。
    /// </summary>
    internal static class Constants
    {
        public const string AppName = "ClaudeGauge";
        public const string AppDisplayName = "Claudeゲージ";
        public const string AppVersion = "1.0.0";

        /// <summary>Anthropic 社の公式ツールではないことを明示する。ウィンドウと README の両方で使う。</summary>
        public const string Disclaimer =
            "本ソフトは Anthropic 社とは関係のない非公式ツールです。Claude は Anthropic 社の商標です。";

        /// <summary>旧名。設定と自動起動の引き継ぎ・後始末に使う。</summary>
        public const string LegacyAppName = "ClaudeUsageTray";

        // ------------------------------------------------------------------
        // 読み取り元（読み取り専用。このフォルダには絶対に書き込まない）
        // ------------------------------------------------------------------
        public static string ClaudeDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            }
        }

        public const string UsageHistoryFileName = "plan-usage-history.json";

        public static string UsageHistoryPath
        {
            get { return Path.Combine(ClaudeDataDir, UsageHistoryFileName); }
        }

        // ------------------------------------------------------------------
        // JSON スキーマ（version 2）
        // ------------------------------------------------------------------
        public const int ExpectedHistoryVersion = 2;
        public const string FieldVersion = "version";
        public const string FieldSamples = "samples";
        public const string FieldTimestamp = "t";
        public const string FieldOrg = "org";
        public const string FieldUsage = "u";

        /// <summary>5時間セッションの使用率（five_hour）。</summary>
        public const string KeySession = "fh";
        /// <summary>週次の使用率（seven_day）。</summary>
        public const string KeyWeekly = "sd";

        /// <summary>詳細ウィンドウに並べる順序。</summary>
        public static readonly string[] GroupOrder =
            { "fh", "sd", "so", "sn", "cw", "oa", "om", "op", "xu" };

        /// <summary>短縮キー → 表示名。未知のキーはキー名のまま表示する。</summary>
        public static readonly Dictionary<string, string> GroupLabels =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "fh", "5時間セッション" },
            { "sd", "週次（全体）" },
            { "so", "週次（Opus）" },
            { "sn", "週次（Sonnet）" },
            { "cw", "週次（Cowork）" },
            { "oa", "週次（連携アプリ）" },
            { "om", "週次（Omelette）" },
            { "op", "Omelette プロモ枠" },
            { "xu", "追加利用枠" },
        };

        // ------------------------------------------------------------------
        // 本家アプリの挙動（実測値）
        // ------------------------------------------------------------------
        /// <summary>本家アプリが履歴を書き込む間隔（秒）。これより短く見に行っても新しい値は出ない。</summary>
        public const int SourceUpdateIntervalSeconds = 300;
        /// <summary>5時間セッションの長さ。</summary>
        public const int SessionWindowHours = 5;
        /// <summary>本家アプリが履歴を保持する日数。</summary>
        public const int HistoryRetentionDays = 30;

        // ------------------------------------------------------------------
        // 推定ロジックのパラメータ
        // ------------------------------------------------------------------
        /// <summary>これを超える間隔が空いていたら「アプリが止まっていた」とみなす（分）。</summary>
        public const int DataGapMinutes = 30;
        /// <summary>週次のリセットとみなす下落幅（ポイント）。</summary>
        public const int WeeklyResetDropPoints = 3;

        // ------------------------------------------------------------------
        // 自前の保存先（設定・ログ）
        // ------------------------------------------------------------------
        public static string AppDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(AppDataDir, "settings.json"); }
        }

        /// <summary>旧名で保存された設定。初回だけ引き継ぐ。</summary>
        public static string LegacySettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    LegacyAppName, "settings.json");
            }
        }

        // ------------------------------------------------------------------
        // Windows 連携
        // ------------------------------------------------------------------
        public const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValueName = "ClaudeGauge";
        public const string ThemeRegistryKey =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        public const string ThemeValueName = "SystemUsesLightTheme";

        // ------------------------------------------------------------------
        // 既定値
        // ------------------------------------------------------------------
        public const int DefaultPollSeconds = 60;
        public const int DefaultWarnPercent = 80;
        public const int DefaultWeeklySwitchPercent = 80;
        public const int DefaultStaleMinutes = 15;
        public const int LevelCautionPercent = 60;
    }
}
