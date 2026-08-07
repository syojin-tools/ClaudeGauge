using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ClaudeUsageTray
{
    /// <summary>
    /// 5時間セッションと週次を並べて表示する。数字と説明だけ。塗りつぶし装飾はしない。
    /// </summary>
    internal sealed class SummaryPanel : Control
    {
        private UsageSnapshot _snap;
        private int _warnPercent = Constants.DefaultWarnPercent;

        public SummaryPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetData(UsageSnapshot snap, int warnPercent)
        {
            _snap = snap;
            _warnPercent = warnPercent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.PanelBack);

            int half = Width / 2;

            using (Pen sep = new Pen(Theme.Border))
                g.DrawLine(sep, half, 12, half, Height - 12);

            DrawColumn(g, new Rectangle(0, 0, half, Height),
                "5時間セッション",
                _snap == null ? -1 : _snap.Session,
                SessionSubText());

            DrawColumn(g, new Rectangle(half, 0, Width - half, Height),
                "週次（全体）",
                _snap == null ? -1 : _snap.Weekly,
                WeeklySubText());
        }

        private string SessionSubText()
        {
            if (_snap == null || !_snap.Ok) return "－";
            if (_snap.SessionResetLocal.HasValue)
            {
                TimeSpan left = _snap.SessionResetLocal.Value - DateTime.Now;
                if (left.TotalSeconds < 0) left = TimeSpan.Zero;
                return "リセットまで 約" + FormatSpan(left) + "\n"
                       + _snap.SessionResetLocal.Value.ToString("H:mm") + " ごろ（推定）";
            }
            return _snap.SessionResetNote ?? "リセット時刻は取得不可";
        }

        private string WeeklySubText()
        {
            if (_snap == null || !_snap.Ok) return "－";
            if (_snap.WeeklyResetLocal.HasValue)
            {
                TimeSpan left = _snap.WeeklyResetLocal.Value - DateTime.Now;
                if (left.TotalSeconds < 0) left = TimeSpan.Zero;
                return "リセットまで 約" + FormatSpan(left) + "\n"
                       + _snap.WeeklyResetLocal.Value.ToString("M/d H:mm") + " ごろ（推定）";
            }
            return "リセット時刻は不明\n（履歴にリセットの記録なし）";
        }

        private static string FormatSpan(TimeSpan t)
        {
            if (t.TotalDays >= 1) return (int)t.TotalDays + "日" + t.Hours + "時間";
            if (t.TotalHours >= 1) return (int)t.TotalHours + "時間" + t.Minutes + "分";
            return Math.Max(0, (int)t.TotalMinutes) + "分";
        }

        private void DrawColumn(Graphics g, Rectangle r, string title, int percent, string sub)
        {
            using (Font fTitle = new Font("Segoe UI", 9f))
            using (Font fBig = new Font("Segoe UI", 30f, FontStyle.Bold))
            using (Font fUnit = new Font("Segoe UI", 12f))
            using (Font fSub = new Font("Segoe UI", 8.5f))
            using (SolidBrush bMuted = new SolidBrush(Theme.Muted))
            using (SolidBrush bText = new SolidBrush(Theme.Text))
            {
                int pad = 18;
                g.DrawString(title, fTitle, bMuted, r.Left + pad, r.Top + 12);

                string big = percent < 0 ? "－" : percent.ToString();
                Color lv = percent < 0 ? Theme.Muted : Theme.LevelColor(Theme.LevelOf(percent, _warnPercent));
                using (SolidBrush bBig = new SolidBrush(lv))
                {
                    SizeF sBig = g.MeasureString(big, fBig, 500, StringFormat.GenericTypographic);
                    float bx = r.Left + pad;
                    float by = r.Top + 32;
                    g.DrawString(big, fBig, bBig, bx, by, StringFormat.GenericTypographic);
                    if (percent >= 0)
                        g.DrawString("%", fUnit, bBig, bx + sBig.Width + 3, by + sBig.Height - 22);
                }

                RectangleF subRect = new RectangleF(r.Left + pad, r.Top + 88, r.Width - pad * 2, r.Height - 92);
                g.DrawString(sub ?? "", fSub, bMuted, subRect);
            }
        }
    }
}
