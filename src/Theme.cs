using System;
using System.Drawing;
using Microsoft.Win32;

namespace ClaudeUsageTray
{
    internal enum UsageLevel { Normal, Caution, Warning, Error }

    /// <summary>
    /// Windows のライト／ダークテーマを判定し、落ち着いた配色を返す。
    /// グラデーション・原色ベタ塗りは使わない。
    /// </summary>
    internal static class Theme
    {
        public static bool IsDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Constants.ThemeRegistryKey, false))
                {
                    if (key == null) return false;
                    object v = key.GetValue(Constants.ThemeValueName);
                    if (v == null) return false;
                    return Convert.ToInt32(v) == 0;
                }
            }
            catch { return false; }
        }

        // ---- ウィンドウ用 ----
        public static Color WindowBack { get { return IsDark() ? C(0x1E, 0x1E, 0x1E) : C(0xF7, 0xF7, 0xF5); } }
        public static Color PanelBack { get { return IsDark() ? C(0x26, 0x26, 0x26) : C(0xFF, 0xFF, 0xFF); } }
        public static Color Border { get { return IsDark() ? C(0x3A, 0x3A, 0x3A) : C(0xE2, 0xE2, 0xDF); } }
        public static Color Text { get { return IsDark() ? C(0xE8, 0xE8, 0xE6) : C(0x1F, 0x1F, 0x1E); } }
        public static Color Muted { get { return IsDark() ? C(0x9A, 0x9A, 0x96) : C(0x6B, 0x6B, 0x67); } }
        public static Color Grid { get { return IsDark() ? C(0x38, 0x38, 0x38) : C(0xE8, 0xE8, 0xE5); } }

        // ---- 状態色（トレイアイコンとウィンドウで共通） ----
        public static Color LevelColor(UsageLevel level)
        {
            bool dark = IsDark();
            switch (level)
            {
                case UsageLevel.Caution:
                    return dark ? C(0xE0, 0xA5, 0x48) : C(0xA5, 0x67, 0x08);
                case UsageLevel.Warning:
                    return dark ? C(0xF2, 0x83, 0x7B) : C(0xB3, 0x26, 0x1E);
                case UsageLevel.Error:
                    return dark ? C(0xF2, 0x83, 0x7B) : C(0xB3, 0x26, 0x1E);
                default:
                    return dark ? C(0xE8, 0xE8, 0xE6) : C(0x1F, 0x1F, 0x1E);
            }
        }

        /// <summary>トレイアイコンの文字の縁取り色。透過背景でもコントラストを確保するため。</summary>
        public static Color IconHalo
        {
            get { return IsDark() ? Color.FromArgb(190, 0, 0, 0) : Color.FromArgb(170, 255, 255, 255); }
        }

        // ---- グラフの線色（塗りつぶしなし） ----
        public static Color ChartSession { get { return IsDark() ? C(0x7A, 0xB0, 0xDF) : C(0x2F, 0x63, 0x94); } }
        public static Color ChartWeekly { get { return IsDark() ? C(0xD8, 0xA0, 0x6A) : C(0x96, 0x5A, 0x1E); } }

        public static UsageLevel LevelOf(int percent, int warnPercent)
        {
            if (percent < 0) return UsageLevel.Error;
            if (percent >= warnPercent) return UsageLevel.Warning;
            if (percent >= Constants.LevelCautionPercent) return UsageLevel.Caution;
            return UsageLevel.Normal;
        }

        private static Color C(int r, int g, int b) { return Color.FromArgb(255, r, g, b); }
    }
}
