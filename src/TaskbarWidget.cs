using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeUsageTray
{
    /// <summary>
    /// タスクバーの上に重ねる小さな表示パネル。
    /// トレイアイコンは 16x16 に固定されて読みにくいため、こちらを主表示にできるようにする。
    /// フォーカスは奪わず、Alt+Tab にも出ない。
    /// </summary>
    internal sealed class TaskbarWidget : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly Settings _settings;
        private readonly Action _onClick;
        private UsageSnapshot _snap;

        private bool _dragging;
        private Point _dragStartScreen;
        private Point _dragStartLocation;
        private bool _moved;

        public TaskbarWidget(Settings settings, Action onClick, ContextMenuStrip menu)
        {
            _settings = settings;
            _onClick = onClick;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(150, 32);
            ContextMenuStrip = menu;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            MouseDown += OnMouseDownHandler;
            MouseMove += OnMouseMoveHandler;
            MouseUp += OnMouseUpHandler;

            Location = ResolvePosition();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        /// <summary>表示しても入力フォーカスを奪わない。</summary>
        protected override bool ShowWithoutActivation { get { return true; } }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                                int x, int y, int cx, int cy, uint flags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        /// <summary>
        /// タスクバー自身も最前面なので、最前面指定を定期的に貼り直して隠れないようにする。
        /// </summary>
        public void EnsureTopMost()
        {
            if (_dragging || !IsHandleCreated || IsDisposed) return;
            try
            {
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                             SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
            }
            catch { }
        }

        // ------------------------------------------------------------------
        private string _shownValue;
        private string _shownSub;

        public void SetData(UsageSnapshot snap)
        {
            _snap = snap;
            _shownValue = null;   // 値が変わっていなくても色が変わりうるので描き直す
            Tick();
        }

        /// <summary>
        /// 残り時間や経過時間は、ファイルを読み直さなくても時間だけで変わる。
        /// 毎秒呼ばれる前提で、実際に文字が変わったときだけ再描画する。
        /// </summary>
        public void Tick()
        {
            if (IsDisposed) return;
            string value = ValueText();
            string sub = SubText();
            if (value == _shownValue && sub == _shownSub) return;
            _shownValue = value;
            _shownSub = sub;
            ResizeToContent();
            Invalidate();
        }

        /// <summary>中身に合わせて横幅を詰める。無駄に長い帯にしない。</summary>
        private void ResizeToContent()
        {
            int want;
            using (Graphics g = CreateGraphics())
            using (Font fValue = new Font(FontName, 12.5f, FontStyle.Bold))
            using (Font fSub = new Font(FontName, 8.5f))
            {
                float w = PadLeft + RingSize + GapAfterRing;
                w += g.MeasureString(ValueText(), fValue, 400, StringFormat.GenericTypographic).Width;
                string sub = SubText();
                if (sub.Length > 0)
                    w += GapBeforeSub + g.MeasureString(sub, fSub, 400, StringFormat.GenericTypographic).Width;
                want = (int)Math.Ceiling(w) + PadRight;
            }
            if (want < 96) want = 96;
            if (Width != want) Width = want;
        }

        private string ValueText()
        {
            if (_snap == null || !_snap.Ok) return "－";
            if (_snap.Stale) return "停止";
            int v = _snap.Session;
            return v < 0 ? "－" : v + "%";
        }

        private string SubText()
        {
            if (_snap == null) return "読み取り中";
            if (!_snap.Ok) return "取得できません";
            if (_snap.Stale) return DetailForm.FormatAge(_snap.AgeMinutes);
            if (_snap.SessionResetLocal.HasValue)
            {
                TimeSpan left = _snap.SessionResetLocal.Value - DateTime.Now;
                if (left.TotalSeconds < 0) left = TimeSpan.Zero;
                return "残り " + (int)left.TotalHours + ":" + left.Minutes.ToString("00");
            }
            if (_snap.Session == 0) return "未使用";
            return "リセット不明";
        }

        /// <summary>既定位置はタスクバー右寄り（通知領域の少し左）。保存位置が画面外なら既定に戻す。</summary>
        private Point ResolvePosition()
        {
            if (_settings.WidgetX >= 0 && _settings.WidgetY >= 0)
            {
                Rectangle p = new Rectangle(_settings.WidgetX, _settings.WidgetY, Width, Height);
                foreach (Screen s in Screen.AllScreens)
                    if (s.Bounds.IntersectsWith(p)) return p.Location;
            }
            return DefaultPosition();
        }

        private Point DefaultPosition()
        {
            Screen scr = Screen.PrimaryScreen;
            Rectangle b = scr.Bounds;
            Rectangle wa = scr.WorkingArea;

            int taskbarTop = wa.Bottom;                 // タスクバーが下にある通常構成
            int taskbarHeight = b.Bottom - wa.Bottom;

            if (taskbarHeight <= 0)                     // 自動的に隠す設定など
            {
                taskbarTop = b.Bottom - 48;
                taskbarHeight = 48;
            }

            int y = taskbarTop + Math.Max(0, (taskbarHeight - Height) / 2);
            int x = b.Right - 270 - Width;              // 通知領域とだいたい重ならない位置
            if (x < b.Left) x = b.Left;
            return new Point(x, y);
        }

        public void ResetPosition()
        {
            Location = DefaultPosition();
            SavePosition();
        }

        private void SavePosition()
        {
            _settings.WidgetX = Location.X;
            _settings.WidgetY = Location.Y;
            _settings.Save();
        }

        // ------------------------------------------------------------------
        private void OnMouseDownHandler(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _moved = false;
            _dragStartScreen = Control.MousePosition;
            _dragStartLocation = Location;
        }

        private void OnMouseMoveHandler(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point now = Control.MousePosition;
            int dx = now.X - _dragStartScreen.X;
            int dy = now.Y - _dragStartScreen.Y;
            if (!_moved && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
            _moved = true;
            Location = new Point(_dragStartLocation.X + dx, _dragStartLocation.Y + dy);
        }

        private void OnMouseUpHandler(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = false;
            if (_moved) SavePosition();
            else if (_onClick != null) _onClick();
        }

        // ------------------------------------------------------------------
        private const string FontName = "Segoe UI";
        private const int PadLeft = 9;
        private const int PadRight = 11;
        private const int RingSize = 20;
        private const int RingThickness = 3;
        private const int GapAfterRing = 9;
        private const int GapBeforeSub = 8;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            bool dark = Theme.IsDark();
            Color chip = dark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(255, 255, 255);
            Color chipEdge = dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(216, 216, 211);
            Color track = dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(228, 228, 224);

            // 窓の形そのものを角丸にしてあるので、塗りつぶし＋内側の縁取りだけでよい
            g.Clear(chip);
            using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
            using (Pen edge = new Pen(chipEdge))
                g.DrawPath(edge, path);

            int percent = (_snap != null && _snap.Ok && !_snap.Stale) ? _snap.Session : -1;
            Color level = percent < 0
                ? (_snap != null && (!_snap.Ok || _snap.Stale) ? Theme.LevelColor(UsageLevel.Error) : Theme.Muted)
                : Theme.LevelColor(Theme.LevelOf(percent, _settings.WarnPercent));

            // 使用率のリング
            Rectangle ring = new Rectangle(PadLeft, (Height - RingSize) / 2, RingSize, RingSize);
            using (Pen pTrack = new Pen(track, RingThickness))
                g.DrawEllipse(pTrack, ring);
            if (percent > 0)
            {
                using (Pen pArc = new Pen(level, RingThickness))
                {
                    pArc.StartCap = LineCap.Round;
                    pArc.EndCap = LineCap.Round;
                    g.DrawArc(pArc, ring, -90f, 360f * Math.Min(100, percent) / 100f);
                }
            }

            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (Font fValue = new Font(FontName, 12.5f, FontStyle.Bold))
            using (Font fSub = new Font(FontName, 8.5f))
            using (SolidBrush bValue = new SolidBrush(level))
            using (SolidBrush bSub = new SolidBrush(Theme.Muted))
            {
                string value = ValueText();
                float x = PadLeft + RingSize + GapAfterRing;
                SizeF sv = g.MeasureString(value, fValue, 400, StringFormat.GenericTypographic);
                g.DrawString(value, fValue, bValue, x, (Height - sv.Height) / 2f - 1,
                             StringFormat.GenericTypographic);
                x += sv.Width + GapBeforeSub;

                string sub = SubText();
                if (sub.Length > 0)
                {
                    SizeF ss = g.MeasureString(sub, fSub, 400, StringFormat.GenericTypographic);
                    g.DrawString(sub, fSub, bSub, x, (Height - ss.Height) / 2f,
                                 StringFormat.GenericTypographic);
                }
            }
        }

        /// <summary>窓の外形を角丸にする。透過キーを使わないので色のにじみが出ない。</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            try
            {
                using (GraphicsPath path = RoundedRect(new Rectangle(0, 0, Width, Height), Height / 2))
                {
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
            Invalidate();
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
