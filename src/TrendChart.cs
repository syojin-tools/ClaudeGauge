using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ClaudeUsageTray
{
    /// <summary>
    /// 30日ぶんの推移。塗りつぶしなし・グラデーションなしの折れ線のみ。
    /// データが欠落している区間（Claude アプリ停止中）は線を繋がない。
    /// </summary>
    internal sealed class TrendChart : Control
    {
        private List<Sample> _samples = new List<Sample>();
        private int _days = Constants.HistoryRetentionDays;

        public TrendChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetData(List<Sample> samples)
        {
            _samples = samples ?? new List<Sample>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.PanelBack);

            Rectangle full = ClientRectangle;
            int padL = 34, padR = 10, padT = 22, padB = 22;
            Rectangle plot = new Rectangle(
                full.Left + padL, full.Top + padT,
                Math.Max(10, full.Width - padL - padR),
                Math.Max(10, full.Height - padT - padB));

            using (Pen border = new Pen(Theme.Border))
                g.DrawRectangle(border, plot.Left, plot.Top, plot.Width - 1, plot.Height - 1);

            using (Font small = new Font("Segoe UI", 7.5f))
            using (SolidBrush muted = new SolidBrush(Theme.Muted))
            using (Pen grid = new Pen(Theme.Grid))
            {
                // Y 軸
                for (int p = 0; p <= 100; p += 25)
                {
                    int y = plot.Bottom - (int)(plot.Height * (p / 100.0));
                    if (p > 0 && p < 100) g.DrawLine(grid, plot.Left + 1, y, plot.Right - 2, y);
                    string lbl = p + "%";
                    SizeF s = g.MeasureString(lbl, small);
                    g.DrawString(lbl, small, muted, plot.Left - s.Width - 4, y - s.Height / 2);
                }

                if (_samples.Count < 2)
                {
                    string msg = "推移を表示できるデータがまだありません";
                    using (Font f = new Font("Segoe UI", 8.5f))
                    {
                        SizeF s = g.MeasureString(msg, f);
                        g.DrawString(msg, f, muted,
                            plot.Left + (plot.Width - s.Width) / 2,
                            plot.Top + (plot.Height - s.Height) / 2);
                    }
                    return;
                }

                DateTime nowLocal = DateTime.Now;
                DateTime startLocal = nowLocal.AddDays(-_days);
                DateTime firstLocal = ToLocal(_samples[0].T);
                if (firstLocal > startLocal) startLocal = firstLocal;
                double totalMs = (nowLocal - startLocal).TotalMilliseconds;
                if (totalMs <= 0) return;

                // X 軸ラベル（5日おき）
                for (DateTime d = startLocal.Date.AddDays(1); d < nowLocal; d = d.AddDays(5))
                {
                    double r = (d - startLocal).TotalMilliseconds / totalMs;
                    if (r < 0 || r > 1) continue;
                    int x = plot.Left + (int)(plot.Width * r);
                    g.DrawLine(grid, x, plot.Top + 1, x, plot.Bottom - 2);
                    string lbl = d.ToString("M/d");
                    SizeF s = g.MeasureString(lbl, small);
                    g.DrawString(lbl, small, muted, x - s.Width / 2, plot.Bottom + 3);
                }

                DrawSeries(g, plot, startLocal, totalMs, Constants.KeyWeekly, Theme.ChartWeekly);
                DrawSeries(g, plot, startLocal, totalMs, Constants.KeySession, Theme.ChartSession);

                // 凡例
                DrawLegend(g, plot, small);
            }
        }

        private void DrawLegend(Graphics g, Rectangle plot, Font font)
        {
            string a = "5時間セッション";
            string b = "週次";
            SizeF sa = g.MeasureString(a, font);
            SizeF sb = g.MeasureString(b, font);
            float totalW = 14 + sa.Width + 14 + 14 + sb.Width;
            float x = plot.Right - totalW;
            float y = plot.Top - 18;

            using (SolidBrush txt = new SolidBrush(Theme.Muted))
            using (Pen p1 = new Pen(Theme.ChartSession, 1.8f))
            using (Pen p2 = new Pen(Theme.ChartWeekly, 1.8f))
            {
                g.DrawLine(p1, x, y + sa.Height / 2, x + 10, y + sa.Height / 2);
                g.DrawString(a, font, txt, x + 14, y);
                float x2 = x + 14 + sa.Width + 14;
                g.DrawLine(p2, x2, y + sb.Height / 2, x2 + 10, y + sb.Height / 2);
                g.DrawString(b, font, txt, x2 + 14, y);
            }
        }

        private void DrawSeries(Graphics g, Rectangle plot, DateTime startLocal, double totalMs,
                                string key, Color color)
        {
            long gapLimit = (long)Constants.DataGapMinutes * 60L * 1000L;
            List<PointF> segment = new List<PointF>();

            using (Pen pen = new Pen(color, 1.7f))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                for (int i = 0; i < _samples.Count; i++)
                {
                    Sample s = _samples[i];
                    int v = s.Get(key);
                    DateTime t = ToLocal(s.T);

                    bool breakHere = (i > 0 && s.T - _samples[i - 1].T > gapLimit) || v < 0;
                    if (breakHere)
                    {
                        FlushSegment(g, pen, segment);
                        if (v < 0) continue;
                    }

                    double r = (t - startLocal).TotalMilliseconds / totalMs;
                    if (r < 0) continue;
                    if (r > 1) r = 1;

                    float x = plot.Left + (float)(plot.Width * r);
                    float y = plot.Bottom - (float)(plot.Height * (Math.Min(100, v) / 100.0));
                    segment.Add(new PointF(x, y));
                }
                FlushSegment(g, pen, segment);
            }
        }

        private static void FlushSegment(Graphics g, Pen pen, List<PointF> pts)
        {
            if (pts.Count >= 2)
            {
                g.DrawLines(pen, pts.ToArray());
            }
            else if (pts.Count == 1)
            {
                using (SolidBrush b = new SolidBrush(pen.Color))
                    g.FillEllipse(b, pts[0].X - 1.2f, pts[0].Y - 1.2f, 2.4f, 2.4f);
            }
            pts.Clear();
        }

        private static DateTime ToLocal(long ms)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
        }
    }
}
