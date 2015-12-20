﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using osu_Lyrics.Properties;

namespace osu_Lyrics
{
    internal partial class Lyrics : Form
    {
        #region Lyrics()

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize, IntPtr hdcSrc, ref Point pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        public static Lyrics Constructor;

        public Lyrics()
        {
            if (Constructor == null)
            {
                Constructor = this;
            }
            InitializeComponent();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x80000;
                const int WS_EX_TRANSPARENT = 0x20;
                const int WS_EX_NOACTIVATE = 0x8000000;

                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            const byte AC_SRC_OVER = 0;
            const byte AC_SRC_ALPHA = 1;
            const int ULW_ALPHA = 2;

            var hDC = GetDC(IntPtr.Zero);
            var hMemDC = CreateCompatibleDC(hDC);
            var hBitmap = IntPtr.Zero;
            var hOldBitmap = IntPtr.Zero;

            Bitmap bmp = null;
            Graphics g = null;
            try
            {
                bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                g = Graphics.FromImage(bmp);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                DrawLyric(g);
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                hOldBitmap = SelectObject(hMemDC, hBitmap);

                var cur = Location;
                var size = bmp.Size;
                var point = Point.Empty;
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(Handle, hDC, ref cur, ref size, hMemDC, ref point, 0, ref blend, ULW_ALPHA);
            }
            catch {}
            if (g != null)
            {
                g.Dispose();
            }
            if (bmp != null)
            {
                bmp.Dispose();
            }

            ReleaseDC(IntPtr.Zero, hDC);
            if (hBitmap != IntPtr.Zero)
            {
                SelectObject(hMemDC, hOldBitmap);
                DeleteObject(hBitmap);
            }
            DeleteDC(hMemDC);
        }

        #endregion

        private void Lyrics_Load(object sender, EventArgs e)
        {
            Notice(Osu.Listen(Osu_Signal) ? Settings._MutexName : "초기화 실패");
            Osu.HookKeyboard(Osu_KeyDown);
        }

        private async void Lyrics_Shown(object sender, EventArgs e)
        {
            // 초기 설정을 위해 대화 상자 열기
            if (!File.Exists(Settings._Path))
            {
                Task.Run(() => Invoke(new MethodInvoker(menuSetting.PerformClick)));
            }
            while (!Osu.Process.HasExited)
            {
                if (!Osu.Show(true))
                {
                    var osu = Osu.WindowInfo();
                    if (!Location.Equals(osu.Location))
                    {
                        Location = osu.Location;
                    }
                    if (!ClientSize.Equals(osu.ClientSize))
                    {
                        ClientSize = osu.ClientSize;
                        Settings.DrawingOrigin = Point.Empty;
                    }
                    if (Settings == null)
                    {
                        TopMost = true;
                    }
                    Visible = true;
                }
                else if (Settings.ShowWhileOsuTop)
                {
                    Visible = false;
                }

                if (NewLyricAvailable())
                {
                    Refresh();
                }

                await Task.Delay(Settings.RefreshRate);
            }
            Close();
        }

        private void Lyrics_FormClosing(object sender, FormClosingEventArgs e)
        {
            Osu.UnhookKeyboard();
        }






        #region Notice(...)

        private string _notice;

        private void Notice(string value)
        {
            timer1.Stop();

            _notice = value;
            Invoke(new MethodInvoker(Refresh));

            timer1.Start();
        }

        private void Notice(string format, params object[] args)
        {
            Notice(string.Format(format, args));
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            _notice = null;
            Invoke(new MethodInvoker(Invalidate));
        }

        #endregion

        private static double Now()
        {
            return new TimeSpan(DateTime.Now.Ticks).TotalSeconds;
        }

        /// <summary>
        /// 알송 서버에서 가사를 가져옴.
        /// </summary>
        /// <param name="data">[HASH]: ... | [ARTIST]: ..., [TITLE]: ...</param>
        /// <returns>List&lt;string&gt;</returns>
        private static async Task<List<Lyric>> GetLyrics(IDictionary<string, string> data)
        {
            try
            {
                var act = "GetLyric5";
                if (!data.ContainsKey("[HASH]"))
                {
                    act = "GetResembleLyric2";
                }
                var content = data.Aggregate(Resources.ResourceManager.GetString(act), (o, i) => o.Replace(i.Key, i.Value));

                var wr = Request.Create(@"http://lyrics.alsong.co.kr/alsongwebservice/service1.asmx");
                wr.Method = "POST";
                wr.UserAgent = "gSOAP";
                wr.ContentType = "application/soap+xml; charset=UTF-8";
                wr.Headers.Add("SOAPAction", "ALSongWebServer/" + act);

                using (var rq = new StreamWriter(wr.GetRequestStream()))
                {
                    rq.Write(content);
                }

                using (var rp = new StreamReader(wr.GetResponse().GetResponseStream()))
                {
                    return WebUtility.HtmlDecode(rp.ReadToEnd().Split(new[] { "<strLyric>", "</strLyric>" }, StringSplitOptions.None)[1])
                        .Split(new[] { "<br>" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(i => new Lyric(i))
                        .Where(i => i.Text.Length != 0)
                        .ToList();
                }
            }
            catch
            {
                return null;
            }
        }






        private Audio curAudio = new Audio();

        private double _curTimeChanged;
        private double _curTime;
        private double _playbackRate;

        private double curTime
        {
            get
            {
                var elapsedTime = (Now() - _curTimeChanged) *_playbackRate;
                return _curTime + elapsedTime - curAudio.Sync;
            }
            set
            {
                if (value < _curTime)
                {
                    lyricsCache = lyricsCache;
                }
                _curTimeChanged = Now();
                _curTime = value;
            }
        }
        
        private readonly Queue<Lyric> lyrics = new Queue<Lyric>();
        private List<Lyric> _lyricsCache = new List<Lyric> { new Lyric() };

        private List<Lyric> lyricsCache
        {
            get
            {
                return _lyricsCache;
            }
            set
            {
                _lyricsCache = value;
                lyrics.Clear();
                value.ForEach(lyrics.Enqueue);
                curLyric = new Lyric();
            }
        }

        private async void Osu_Signal(string line)
        {
            var data = line.Split('|');
            if (data.Length != 5)
            {
                return;
            }
            // [ time, audioPath, audioCurrentTime, audioPlaybackRate, beatmapPath ]
            // 재생 중인 곡이 바꼈다!
            if (data[1] != curAudio.Path)
            {
                curAudio = new Audio(data[1], data[4]);
                lyricsCache = new List<Lyric>
                {
                    new Lyric(0, "가사 받는 중...")
                };

                // 파일 해시로 가사 검색
                var newLyrics = await GetLyrics(new Dictionary<string, string>
                {
                    { "[HASH]", curAudio.Info.Hash }
                });
                if (newLyrics == null)
                {
                    // 음악 정보로 가사 검색
                    newLyrics = await GetLyrics(new Dictionary<string, string>
                    {
                        { "[TITLE]", curAudio.Beatmap.TitleUnicode ?? curAudio.Beatmap.Title },
                        { "[ARTIST]", curAudio.Beatmap.ArtistUnicode ?? curAudio.Beatmap.Artist }
                    });
                }
                if (newLyrics != null)
                {
                    newLyrics.Insert(0, new Lyric());
                }
                else
                {
                    newLyrics = new List<Lyric>
                    {
                        new Lyric(0, "가사 없음")
                    };
                }

                lyricsCache = newLyrics;
            }
            curTime = DateTimeOffset.Now.Subtract(
                DateTimeOffset.FromFileTime(Convert.ToInt64(data[0], 16))
            ).TotalSeconds + Convert.ToDouble(data[2]);
            _playbackRate = 1 + Convert.ToDouble(data[3]) / 100;
        }




        private Lyric _curLyric = new Lyric();

        private Lyric curLyric
        {
            get { return _curLyric; }
            set
            {
                _curLyric = value;
                lyricBuffer.Clear();
            }
        }

        private readonly List<string> lyricBuffer = new List<string>
        {
            "선곡하세요"
        };

        private bool NewLyricAvailable()
        {
            var flag = false;
            while (lyrics.Count > 0)
            {
                var lyric = lyrics.Peek();

                if (lyric.Time < curLyric.Time)
                {
                    lyrics.Dequeue();
                    continue;
                }

                if (lyric.Time <= curTime)
                {
                    if (!lyric.Time.Equals(curLyric.Time) || (lyric.Time.Equals(0) && curLyric.Time.Equals(0)))
                    {
                        curLyric = lyric;
                        flag = true;
                    }
                    lyricBuffer.Add(lyric.Text);
                    lyrics.Dequeue();
                }
                else
                {
                    break;
                }
            }
            return flag;
        }





        private bool showLyric = true;

        private void DrawLyric(Graphics g)
        {
            if (_notice != null)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddString(
                        _notice, Settings.FontFamily, Settings.FontStyle, g.DpiY * 14 / 72, Point.Empty,
                        StringFormat.GenericDefault);
                    if (Settings.BorderWidth > 0)
                    {
                        g.DrawPath(Settings.Border, path);
                    }
                    g.FillPath(Settings.Brush, path);
                }
            }

            if (!showLyric)
            {
                return;
            }

            var lyricBuilder = new StringBuilder();
            var lyricCount = lyricBuffer.Count;
            if (Settings.LineCount == 0)
            {
                foreach (var i in lyricBuffer)
                {
                    lyricBuilder.AppendLine(i);
                }
            }
            else if (Settings.LineCount > 0)
            {
                for (var i = 0; i < Settings.LineCount && i < lyricCount; i++)
                {
                    lyricBuilder.AppendLine(lyricBuffer[i]);
                }
            }
            else
            {
                var i = lyricCount + Settings.LineCount;
                if (i < 0)
                {
                    i = 0;
                }
                for (; i < lyricCount; i++)
                {
                    lyricBuilder.AppendLine(lyricBuffer[i]);
                }
            }

            using (var path = new GraphicsPath())
            {
                path.AddString(
                    lyricBuilder.ToString(), Settings.FontFamily, Settings.FontStyle, g.DpiY * Settings.FontSize / 72,
                    Settings.DrawingOrigin, Settings.StringFormat);
                if (Settings.BorderWidth > 0)
                {
                    g.DrawPath(Settings.Border, path);
                }
                g.FillPath(Settings.Brush, path);
            }
        }





        private bool Osu_KeyDown(Keys key)
        {
            if (key == Settings.KeyToggle)
            {
                showLyric = !showLyric;
                Notice("가사 {0}", showLyric ? "보임" : "숨김");
                return true;
            }
            if (!Settings.BlockSyncOnHide || (Settings.BlockSyncOnHide && showLyric))
            {
                if (key == Settings.KeyBackward)
                {
                    curAudio.Sync += 0.5;
                    lyricsCache = lyricsCache;
                    Notice("싱크 느리게({0}초)", curAudio.Sync.ToString("F1"));
                    return true;
                }
                if (key == Settings.KeyForward)
                {
                    curAudio.Sync -= 0.5;
                    Notice("싱크 빠르게({0}초)", curAudio.Sync.ToString("F1"));
                    return true;
                }
            }
            return false;
        }








        private void trayIcon_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Osu.Show();
            }
        }

        public static Settings Settings;

        private void menuSetting_Click(object sender, EventArgs e)
        {
            if (Settings == null)
            {
                Settings = new Settings
                {
                    TopMost = true
                };
                Settings.ShowDialog();
                Settings = null;
            }
            else
            {
                Settings.TopMost = true;
                Settings.Focus();
            }
        }

        private void menuExit_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}