using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeUsageTray
{
    /// <summary>
    /// GDI+ でトレイアイコンを生成する。背景は完全透過、文字色だけで状態を示す。
    /// 週次を表示しているときは数字の下に短い横線を引く。
    /// </summary>
    internal sealed class RenderedIcon : IDisposable
    {
        public Icon Icon;
        private IntPtr _handle;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        internal RenderedIcon(Icon icon, IntPtr handle)
        {
            Icon = icon;
            _handle = handle;
        }

        public void Dispose()
        {
            if (Icon != null) { Icon.Dispose(); Icon = null; }
            if (_handle != IntPtr.Zero) { DestroyIcon(_handle); _handle = IntPtr.Zero; }
        }
    }

    /// <summary>
    /// 「いま何を、何色で、下線ありで出すか」の判断。トレイと詳細ウィンドウで共通に使う。
    /// </summary>
    internal static class IconState
    {
        public static void Compute(UsageSnapshot snap, Settings settings,
                                   out string text, out Color color, out bool underline)
        {
            underline = false;

            if (snap == null || !snap.Ok || snap.Stale)
            {
                text = "!";
                color = Theme.LevelColor(UsageLevel.Error);
                return;
            }

            bool showWeekly = snap.Weekly >= 0 && snap.Weekly >= settings.WeeklySwitchPercent;
            int value = showWeekly ? snap.Weekly : snap.Session;

            if (value < 0)
            {
                text = "!";
                color = Theme.LevelColor(UsageLevel.Error);
                return;
            }

            text = value.ToString();
            color = Theme.LevelColor(Theme.LevelOf(value, settings.WarnPercent));
            underline = showWeekly;
        }
    }

    internal static class TrayIconRenderer
    {
        public static RenderedIcon Render(string text, Color color, bool underline)
        {
            Size sz = SystemInformation.SmallIconSize;
            int w = sz.Width <= 0 ? 16 : sz.Width;
            int h = sz.Height <= 0 ? 16 : sz.Height;

            Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    int lineHeight = underline ? Math.Max(2, h / 8) : 0;
                    int lineGap = underline ? Math.Max(1, h / 16) : 0;
                    int textBoxH = h - lineHeight - lineGap;

                    using (GraphicsPath path = BuildTextPath(text, w, textBoxH))
                    {
                        if (path != null)
                        {
                            // 縁取り（テーマ問わずコントラストを確保する）
                            using (Pen halo = new Pen(Theme.IconHalo, Math.Max(1.6f, w / 9f)))
                            {
                                halo.LineJoin = LineJoin.Round;
                                g.DrawPath(halo, path);
                            }
                            using (SolidBrush brush = new SolidBrush(color))
                            {
                                g.FillPath(brush, path);
                            }
                        }
                    }

                    if (underline)
                    {
                        int lw = Math.Max(6, (int)(w * 0.62));
                        int lx = (w - lw) / 2;
                        int ly = h - lineHeight;
                        using (SolidBrush hb = new SolidBrush(Theme.IconHalo))
                            g.FillRectangle(hb, lx - 1, ly - 1, lw + 2, lineHeight + 2);
                        using (SolidBrush b = new SolidBrush(color))
                            g.FillRectangle(b, lx, ly, lw, lineHeight);
                    }
                }

                // GetHicon のハンドルは GDI 側の所有。Icon.FromHandle は所有しないので
                // RenderedIcon.Dispose で DestroyIcon するまで生かしておく。
                IntPtr hIcon = bmp.GetHicon();
                return new RenderedIcon(Icon.FromHandle(hIcon), hIcon);
            }
            finally
            {
                bmp.Dispose();
            }
        }

        /// <summary>
        /// 箱に収まる最大サイズの文字パスを作る。1〜3 桁を同じ枠で扱えるようにするため。
        /// </summary>
        private static GraphicsPath BuildTextPath(string text, int boxW, int boxH)
        {
            if (string.IsNullOrEmpty(text)) return null;

            FontFamily family;
            try { family = new FontFamily("Segoe UI"); }
            catch { family = FontFamily.GenericSansSerif; }

            using (family)
            {
                for (float em = boxH + 2; em >= 5f; em -= 0.5f)
                {
                    GraphicsPath path = new GraphicsPath();
                    try
                    {
                        path.AddString(text, family, (int)FontStyle.Bold, em,
                            new PointF(0, 0), StringFormat.GenericTypographic);
                        RectangleF b = path.GetBounds();
                        if (b.Width <= 0 || b.Height <= 0) { path.Dispose(); continue; }

                        float pad = Math.Max(1.2f, boxW / 12f);
                        if (b.Width <= boxW - pad && b.Height <= boxH - 1)
                        {
                            float dx = (boxW - b.Width) / 2f - b.X;
                            float dy = (boxH - b.Height) / 2f - b.Y;
                            using (Matrix m = new Matrix())
                            {
                                m.Translate(dx, dy);
                                path.Transform(m);
                            }
                            return path;
                        }
                    }
                    catch
                    {
                        path.Dispose();
                        return null;
                    }
                    path.Dispose();
                }
            }
            return null;
        }
    }
}
