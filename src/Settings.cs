using System;
using System.IO;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ClaudeUsageTray
{
    /// <summary>
    /// 設定の永続化と、Windows スタートアップ登録。
    /// 保存先は %APPDATA%\ClaudeGauge\settings.json（Claude 側フォルダには一切書かない）。
    /// </summary>
    internal sealed class Settings
    {
        public int PollSeconds { get; set; }
        public int WarnPercent { get; set; }
        public int WeeklySwitchPercent { get; set; }
        public bool NotificationsEnabled { get; set; }
        public int StaleMinutes { get; set; }

        /// <summary>タスクバー上の大きい表示を使うか。</summary>
        public bool ShowTaskbarWidget { get; set; }
        /// <summary>表示位置。-1 は既定位置（タスクバー右寄り）。</summary>
        public int WidgetX { get; set; }
        public int WidgetY { get; set; }

        public Settings()
        {
            PollSeconds = Constants.DefaultPollSeconds;
            WarnPercent = Constants.DefaultWarnPercent;
            WeeklySwitchPercent = Constants.DefaultWeeklySwitchPercent;
            NotificationsEnabled = true;
            StaleMinutes = Constants.DefaultStaleMinutes;
            ShowTaskbarWidget = true;
            WidgetX = -1;
            WidgetY = -1;
        }

        private void Clamp()
        {
            if (PollSeconds < 15) PollSeconds = 15;
            if (PollSeconds > 900) PollSeconds = 900;
            if (WarnPercent < 10) WarnPercent = 10;
            if (WarnPercent > 100) WarnPercent = 100;
            if (WeeklySwitchPercent < 10) WeeklySwitchPercent = 10;
            if (WeeklySwitchPercent > 100) WeeklySwitchPercent = 100;
            if (StaleMinutes < 6) StaleMinutes = 6;
            if (StaleMinutes > 240) StaleMinutes = 240;
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                MigrateLegacySettings();

                if (File.Exists(Constants.SettingsPath))
                {
                    string json = File.ReadAllText(Constants.SettingsPath);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    Settings loaded = ser.Deserialize<Settings>(json);
                    if (loaded != null) s = loaded;
                    DiagLog.Write("設定を読み込みました");
                }
                else
                {
                    DiagLog.Write("設定ファイルがないため既定値を使用します");
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write("設定の読み込みに失敗（既定値を使用）: {0} {1}", ex.GetType().Name, ex.Message);
                s = new Settings();
            }
            s.Clamp();
            return s;
        }

        public void Save()
        {
            Clamp();
            try
            {
                Directory.CreateDirectory(Constants.AppDataDir);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                string json = ser.Serialize(this);
                string tmp = Constants.SettingsPath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(Constants.SettingsPath)) File.Delete(Constants.SettingsPath);
                File.Move(tmp, Constants.SettingsPath);
                DiagLog.Write("設定を保存しました");
            }
            catch (Exception ex)
            {
                DiagLog.Write("設定の保存に失敗: {0} {1}", ex.GetType().Name, ex.Message);
            }
        }

        /// <summary>旧名（ClaudeUsageTray）の設定が残っていれば一度だけ引き継ぐ。</summary>
        private static void MigrateLegacySettings()
        {
            try
            {
                if (File.Exists(Constants.SettingsPath)) return;
                if (!File.Exists(Constants.LegacySettingsPath)) return;

                Directory.CreateDirectory(Constants.AppDataDir);
                File.Copy(Constants.LegacySettingsPath, Constants.SettingsPath, false);
                DiagLog.Write("旧バージョンの設定を引き継ぎました");
            }
            catch (Exception ex)
            {
                DiagLog.Write("旧設定の引き継ぎに失敗（既定値で続行）: {0}", ex.Message);
            }
        }

        /// <summary>旧名の自動起動登録を消す。放置すると二重に起動してしまう。</summary>
        public static void CleanupLegacyStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Constants.RunRegistryKey, true))
                {
                    if (key == null) return;
                    if (key.GetValue(Constants.LegacyAppName) == null) return;
                    key.DeleteValue(Constants.LegacyAppName, false);
                    DiagLog.Write("旧バージョンの自動起動登録を削除しました");
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write("旧自動起動登録の削除に失敗: {0}", ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // スタートアップ登録（アプリ内で完結。ユーザーに手作業をさせない）
        // ------------------------------------------------------------------
        public static bool GetStartWithWindows()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Constants.RunRegistryKey, false))
                {
                    if (key == null) return false;
                    object v = key.GetValue(Constants.RunValueName);
                    return v != null && !string.IsNullOrEmpty(v.ToString());
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write("自動起動設定の読み取りに失敗: {0}", ex.Message);
                return false;
            }
        }

        public static bool SetStartWithWindows(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Constants.RunRegistryKey))
                {
                    if (key == null) return false;
                    if (enabled)
                    {
                        string exe = System.Windows.Forms.Application.ExecutablePath;
                        key.SetValue(Constants.RunValueName, "\"" + exe + "\"", RegistryValueKind.String);
                        DiagLog.Write("自動起動を有効化しました");
                    }
                    else
                    {
                        if (key.GetValue(Constants.RunValueName) != null)
                            key.DeleteValue(Constants.RunValueName, false);
                        DiagLog.Write("自動起動を無効化しました");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                DiagLog.Write("自動起動設定の変更に失敗: {0} {1}", ex.GetType().Name, ex.Message);
                return false;
            }
        }
    }
}
