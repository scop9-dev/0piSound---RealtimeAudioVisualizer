using AudioVisualizerApp;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AudioVisualizerApp
{
    public class VisualizerForm : Form
    {
        private TranslucentPanel canvas;
        private Timer timer;
        private Complex[] fftBuffer;
        private int fftSize = 1024;

        private WasapiLoopbackCapture loopback;
        private LoopbackTap loopbackTap;

        // --- Spectres ---
        private AudioSpectrumBase activeSpectrum;
        private BarSpectrum barSpectrum = new BarSpectrum();
        private CircleSpectrum circleSpectrum = new CircleSpectrum();
        private WaveformSpectrum waveformSpectrum = new WaveformSpectrum();
        private GlowSpectrum glowSpectrum = new GlowSpectrum();
        private WaveTrailSpectrum waveTrail = new WaveTrailSpectrum();
        private SinusWaveSpectrum sinusSpectrum = new SinusWaveSpectrum();
        private SpectrogramSpectrum spectrogramSpectrum = new SpectrogramSpectrum();
        private Button btnSwitch;
        private Button btnParams;
        private Timer renderTimer;

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            public int leftWidth;
            public int rightWidth;
            public int topHeight;
            public int bottomHeight;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMargins);

        // ---------------------------
        // SetWindowCompositionAttribute (Acrylic / Blur) support
        // ---------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
        }

        private DateTime lastAudioTime = DateTime.MinValue;

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        public VisualizerForm()
        {
            Text = "0piSound";
            Size = new Size(1000, 450);
            BackColor = Color.FromArgb(25, 25, 25);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Icon = new Icon("Waveico.ico");

            // Double buffering (ANTI-SACCADES)
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();

            // Canvas
            canvas = new TranslucentPanel();
            canvas.Location = new Point(20, 50);
            canvas.Size = new Size(940, 350);
            canvas.SetOpacity(0.40f);
            canvas.Paint += Canvas_Paint;
            Controls.Add(canvas);

            // Switch spectrum
            btnSwitch = new Button();
            btnSwitch.Text = "Switch Spectrum";
            btnSwitch.Location = new Point(20, 10);
            btnSwitch.Size = new Size(150, 30);
            btnSwitch.BackColor = Color.FromArgb(50, 50, 50);
            btnSwitch.ForeColor = Color.White;
            btnSwitch.FlatStyle = FlatStyle.System;
            btnSwitch.Click += BtnSwitch_Click;
            Controls.Add(btnSwitch);

            // Parameters
            btnParams = new Button();
            btnParams.Text = "Parameters";
            btnParams.Location = new Point(180, 10);
            btnParams.Size = new Size(120, 30);
            btnParams.BackColor = Color.FromArgb(50, 50, 50);
            btnParams.ForeColor = Color.White;
            btnParams.FlatStyle = FlatStyle.System;
            btnParams.Click += BtnParams_Click;
            Controls.Add(btnParams);

            // FFT
            fftBuffer = new Complex[fftSize];
            activeSpectrum = barSpectrum;

            // TIMER UI (60 FPS)
            renderTimer = new Timer();
            renderTimer.Interval = 16;
            renderTimer.Tick += (s, e) => canvas.Invalidate();
            renderTimer.Start();

            FormClosing += VisualizerForm_FormClosing;
            Settings.Load();

            loopback = new WasapiLoopbackCapture();
            loopback.DataAvailable += Loopback_DataAvailable;
            loopback.RecordingStopped += Loopback_RecordingStopped;

            loopbackTap = new LoopbackTap(loopback.WaveFormat.Channels, fftSize);

            loopback.StartRecording();

            timer = new Timer();
            timer.Interval = 16; 
            timer.Tick += Timer_Tick;
            timer.Start();

        }

        private void VisualizerForm_Shown(object sender, EventArgs e)
        {
            try
            {
                EnableGlassEffect(); 
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot set blur : " + ex.Message);
            }
        }

        private void EnableGlassEffect()
        {
            try
            {
                var accent = new AccentPolicy();
                accent.AccentState = (int)AccentState.ACCENT_ENABLE_BLURBEHIND;
                accent.AccentFlags = 0;
                accent.GradientColor = 0;

                int accentSize = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = 19, // WCA_ACCENT_POLICY
                        SizeOfData = accentSize,
                        Data = accentPtr
                    };
                    SetWindowCompositionAttribute(this.Handle, ref data);
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
            catch
            {
                try
                {
                    int baseTop = SystemInformation.CaptionHeight + 8;
                    int canvasBottom = (canvas != null && !canvas.IsDisposed) ? canvas.Bounds.Bottom + 8 : baseTop;
                    int top = Math.Max(baseTop, canvasBottom);
                    top = Math.Min(top, Math.Max(1, this.ClientSize.Height - 1));
                    MARGINS margins = new MARGINS() { leftWidth = 0, rightWidth = 0, topHeight = top, bottomHeight = 0 };
                    DwmExtendFrameIntoClientArea(this.Handle, ref margins);
                }
                catch { }
            }

            this.BackColor = Color.FromArgb(25, 25, 25); 
        }

        private void BtnParams_Click(object sender, EventArgs e)
        {
            using (var f = new SettingsForm())
            {
                var dr = f.ShowDialog();
                if (dr == DialogResult.OK)
                {

                }
            }
        }
        private void BtnSwitch_Click(object sender, EventArgs e)
        {
            List<AudioSpectrumBase> spectraList = new List<AudioSpectrumBase>();

            spectraList.Add(barSpectrum);
            spectraList.Add(circleSpectrum);
            spectraList.Add(waveformSpectrum);

            if (Settings.EnableGlow) spectraList.Add(glowSpectrum);
            if (Settings.EnableTrail) spectraList.Add(waveTrail);
            if (Settings.UseSinusWave) spectraList.Add(sinusSpectrum);
            if (Settings.ShowSpectrogram) spectraList.Add(spectrogramSpectrum);

            // Chercher l'index du spectre actif
            int currentIndex = spectraList.IndexOf(activeSpectrum);

            // Passer au suivant
            int nextIndex = (currentIndex + 1) % spectraList.Count;

            activeSpectrum = spectraList[nextIndex];
        }

        private void Loopback_RecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine("Loopback stopped (exception): " + e.Exception);
                MessageBox.Show("Loopback stopped: " + e.Exception.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Loopback stopped normally.");
            }
        }

        // Remplacez la méthode Loopback_DataAvailable par celle-ci
        private void Loopback_DataAvailable(object sender, WaveInEventArgs e)
        {
            try
            {
                lastAudioTime = DateTime.Now;

                loopbackTap?.WriteFromBytes(e.Buffer, 0, e.BytesRecorded, loopback.WaveFormat);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Loopback_DataAvailable exception: " + ex);
            }
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            // Réglages PERF (anti saccades)
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
            e.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;

            if (activeSpectrum == null || fftBuffer == null)
                return;

            activeSpectrum.Draw(
                e.Graphics,
                fftBuffer,
                canvas.Width,
                canvas.Height
            );
        }

        private void VisualizerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            timer.Stop();
            try
            {
                if (loopback != null)
                {
                    loopback.DataAvailable -= Loopback_DataAvailable;
                    loopback.RecordingStopped -= Loopback_RecordingStopped;
                    loopback.StopRecording();
                    loopback.Dispose();
                    loopback = null;
                }
            }
            catch { }
            loopbackTap = null;
        }

        private bool _fftBusy = false;

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (loopbackTap == null) return;
            if (_fftBusy) return;
            _fftBusy = true;

            try
            {
                float[] buffer = new float[fftSize];
                int read = 0;

                try
                {
                    read = loopbackTap.GetSnapshot(buffer);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("GetSnapshot failed: " + ex);
                    read = 0;
                }

                double secondsSinceLast = (lastAudioTime == DateTime.MinValue) ? double.PositiveInfinity : (DateTime.Now - lastAudioTime).TotalSeconds;
                System.Diagnostics.Debug.WriteLine($"Timer_Tick: read={read}, secondsSinceLastAudio={secondsSinceLast:N2}");
                this.Text = $"0piSound — read={read} lastAudio={secondsSinceLast:N2}s";

                if (read == 0)
                {
                    Bitmap bmpEmpty = new Bitmap(canvas.Width, canvas.Height);
                    using (Graphics g = Graphics.FromImage(bmpEmpty))
                    {
                        g.Clear(Color.FromArgb(30, 0, 0, 0));
                        using (var pen = new Pen(Color.DarkGray, 2))
                        {
                            // grille simple
                            for (int x = 0; x < canvas.Width; x += 20) g.DrawLine(pen, x, 0, x, canvas.Height);
                            for (int y = 0; y < canvas.Height; y += 20) g.DrawLine(pen, 0, y, canvas.Width, y);
                        }
                        g.DrawString("No audio / waiting...", new Font("Segoe UI", 10), Brushes.LightGray, new PointF(10, 10));
                    }
                    canvas.BackgroundImage?.Dispose();
                    canvas.BackgroundImage = bmpEmpty;

                    return;
                }

                // Préparer le buffer FFT
                for (int i = 0; i < fftSize; i++)
                {
                    fftBuffer[i].X = i < read ? (float)(buffer[i] * FastFourierTransform.HammingWindow(i, fftSize)) : 0;
                    fftBuffer[i].Y = 0;
                }

                double sumSq = 0;
                for (int i = 0; i < read; i++) sumSq += buffer[i] * buffer[i];
                double rms = Math.Sqrt(sumSq / Math.Max(1, read));
                System.Diagnostics.Debug.WriteLine("Audio RMS: " + rms.ToString("F4"));

                // FFT
                try
                {
                    FastFourierTransform.FFT(true, (int)Math.Log(fftSize, 2.0), fftBuffer);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("FFT failed: " + ex);
                    return;
                }

                Bitmap bmp = new Bitmap(canvas.Width, canvas.Height);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.FillRectangle(new SolidBrush(Color.FromArgb(30, 0, 0, 0)), 0, 0, canvas.Width, canvas.Height);

                    try
                    {
                        activeSpectrum.Draw(g, fftBuffer, canvas.Width, canvas.Height);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Spectrum.Draw exception: " + ex);
                        // dessiner message d'erreur sur le bitmap
                        g.DrawString("Spectrum.Draw error: " + ex.Message, new Font("Segoe UI", 10), Brushes.Red, new PointF(10, 10));
                    }
                }

                canvas.BackgroundImage?.Dispose();
                canvas.BackgroundImage = bmp;
            }
            finally
            {
                _fftBusy = false;
            }
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new VisualizerForm());
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // VisualizerForm
            // 
            this.ClientSize = new System.Drawing.Size(284, 261);
            this.Name = "VisualizerForm";
            this.Load += new System.EventHandler(this.VisualizerForm_Load);
            this.ResumeLayout(false);

        }

        private void VisualizerForm_Load(object sender, EventArgs e)
        {

        }

        // Ajouter cette surcharge dans la classe VisualizerForm pour corriger les hit-tests
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);

                if (m.Result != IntPtr.Zero && m.Result.ToInt32() == HTCAPTION)
                {
                    Point clientPoint = this.PointToClient(Cursor.Position);
                    Control child = this.GetChildAtPoint(clientPoint);
                    if (child != null)
                    {
                        m.Result = (IntPtr)HTCLIENT;
                        return;
                    }
                }
                return;
            }

            base.WndProc(ref m);
        }
    }

    // --- Extension DoubleBuffer ---
    public static class ControlExtensions
    {
        public static void DoubleBuffered(this Control c, bool enable)
        {
            System.Reflection.PropertyInfo aProp =
                  typeof(Control).GetProperty("DoubleBuffered",
                  System.Reflection.BindingFlags.NonPublic |
                  System.Reflection.BindingFlags.Instance);
            aProp.SetValue(c, enable, null);
        }
    }

    // --- LoopbackTap ---
    public class LoopbackTap
    {
        private readonly float[] buffer;
        private int writePos;
        private readonly int bufferSize;
        private readonly int channels;
        private readonly object _lock = new object();

        public LoopbackTap(int channels, int bufferFrames)
        {
            this.channels = Math.Max(1, channels);
            this.bufferSize = Math.Max(1024, bufferFrames) * this.channels;
            buffer = new float[this.bufferSize];
            writePos = 0;
        }

        public void WriteFromBytes(byte[] bytes, int offset, int count, WaveFormat format)
        {
            if (bytes == null || count == 0) return;
            int bytesPerSample = format.BitsPerSample / 8;
            int totalSamples = count / bytesPerSample;
            float[] samples = new float[totalSamples];

            if (format.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
                for (int i = 0; i < totalSamples; i++) samples[i] = BitConverter.ToSingle(bytes, offset + i * 4);
            else if (format.BitsPerSample == 16)
                for (int i = 0; i < totalSamples; i++) samples[i] = BitConverter.ToInt16(bytes, offset + i * 2) / 32768f;
            else return;

            lock (_lock)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    buffer[writePos] = samples[i];
                    writePos++;
                    if (writePos >= bufferSize) writePos = 0;
                }
            }
        }

        public int GetSnapshot(float[] dest)
        {
            if (dest == null || dest.Length == 0) return 0;
            lock (_lock)
            {
                int framesNeeded = dest.Length;
                int samplesNeeded = framesNeeded * channels;
                int start = writePos - samplesNeeded;
                if (start < 0) start += bufferSize;

                for (int f = 0; f < framesNeeded; f++)
                {
                    int sampleIndex = (start + f * channels) % bufferSize;
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                    {
                        int idx = sampleIndex + c;
                        if (idx >= bufferSize) idx -= bufferSize;
                        sum += buffer[idx];
                    }
                    dest[f] = sum / channels;
                }
                return framesNeeded;
            }
        }
    }

    // --- Spectres modulaires ---
    public abstract class AudioSpectrumBase
    {
        public abstract void Draw(Graphics g, Complex[] fftBuffer, int width, int height);
    }

public class BarSpectrum : AudioSpectrumBase
    {
        private readonly int barCount = 64;
        private readonly SolidBrush[] barBrushes;
        private readonly float[] heights; // pour le lissage

        private const float Smoothing = 0.2f; // ajustable : 0 = instantané, 1 = très lent

        public BarSpectrum()
        {
            barBrushes = new SolidBrush[barCount];
            heights = new float[barCount];

            for (int i = 0; i < barCount; i++)
            {
                Color color = ColorFromSpectrum(i, barCount);
                barBrushes[i] = new SolidBrush(color);
            }
        }

        public override void Draw(Graphics g, Complex[] fftBuffer, int width, int height)
        {
            int barWidth = Math.Max(1, width / barCount);

            for (int i = 0; i < barCount; i++)
            {
                double mag = Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);

                float target = (float)(mag * 10000);
                if (target > height) target = height;

                // Lissage bidirectionnel
                heights[i] += (target - heights[i]) * Smoothing;

                float h = heights[i];

                g.FillRectangle(barBrushes[i],
                    i * barWidth,
                    height - h,
                    barWidth - 2,
                    h);
            }
        }

        private Color ColorFromSpectrum(int index, int total)
        {
            float hue = (float)index / total * 360f;
            return HsvToRgb(hue, 1f, 1f);
        }

        private Color HsvToRgb(float h, float s, float v)
        {
            int hi = (int)Math.Floor(h / 60) % 6;
            float f = h / 60 - (float)Math.Floor(h / 60);
            int vv = (int)Math.Round(v * 255);
            int p = (int)Math.Round(vv * (1 - s));
            int q = (int)Math.Round(vv * (1 - f * s));
            int t = (int)Math.Round(vv * (1 - (1 - f) * s));

            switch (hi)
            {
                case 0: return Color.FromArgb(255, vv, t, p);
                case 1: return Color.FromArgb(255, q, vv, p);
                case 2: return Color.FromArgb(255, p, vv, t);
                case 3: return Color.FromArgb(255, p, q, vv);
                case 4: return Color.FromArgb(255, t, p, vv);
                case 5: return Color.FromArgb(255, vv, p, q);
                default: return Color.FromArgb(255, vv, vv, vv);
            }
        }
    }

    public class CircleSpectrum : AudioSpectrumBase
    {
        public override void Draw(Graphics g, Complex[] fftBuffer, int width, int height)
        {
            int cx = width / 2;
            int cy = height / 2;
            int radius = Math.Min(cx, cy) - 20;
            int bars = 64;

            for (int i = 0; i < bars; i++)
            {
                double mag = Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);
                mag = Math.Min(mag * 5000, radius);
                double angle = i * 2 * Math.PI / bars;
                int x = cx + (int)((radius - mag) * Math.Cos(angle));
                int y = cy + (int)((radius - mag) * Math.Sin(angle));
                int x2 = cx + (int)(radius * Math.Cos(angle));
                int y2 = cy + (int)(radius * Math.Sin(angle));
                g.DrawLine(new Pen(ColorFromSpectrum(i, bars), 2), x, y, x2, y2);
            }
        }

        private Color ColorFromSpectrum(int index, int total)
        {
            float hue = (float)index / total * 360;
            return HsvToRgb(hue, 1f, 1f);
        }

        private Color HsvToRgb(float h, float s, float v)
        {
            int hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
            float f = h / 60 - (float)Math.Floor(h / 60);
            int vv = (int)(v * 255);
            int p = (int)(vv * (1 - s));
            int q = (int)(vv * (1 - f * s));
            int t = (int)(vv * (1 - (1 - f) * s));
            switch (hi)
            {
                case 0: return Color.FromArgb(255, vv, t, p);
                case 1: return Color.FromArgb(255, q, vv, p);
                case 2: return Color.FromArgb(255, p, vv, t);
                case 3: return Color.FromArgb(255, p, q, vv);
                case 4: return Color.FromArgb(255, t, p, vv);
                default: return Color.FromArgb(255, vv, p, q);
            }
        }
    }

    public class WaveformSpectrum : AudioSpectrumBase
    {
        public override void Draw(Graphics g, Complex[] fftBuffer, int width, int height)
        {
            Pen pen = new Pen(Color.Cyan, 2);
            PointF[] points = new PointF[fftBuffer.Length];
            for (int i = 0; i < fftBuffer.Length; i++)
            {
                double mag = Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);
                float x = i * width / (float)fftBuffer.Length;
                float y = height - (float)Math.Min(mag * 10000, height);
                points[i] = new PointF(x, y);
            }
            if (points.Length > 1) g.DrawLines(pen, points);
        }
    }
}

public static class ColorUtils
{
    // Retourne une couleur à partir d'un index dans la plage [0,total)
    public static Color ColorFromSpectrum(int index, int total)
    {
        float h = (float)index / Math.Max(1, total) * 360f;
        return HsvToRgb(h, 1f, 1f);
    }

    // Conversion HSV -> Color (0..360, s 0..1, v 0..1)
    public static Color HsvToRgb(float h, float s, float v)
    {
        h = (h % 360 + 360) % 360;
        float hf = h / 60f;
        int hi = (int)Math.Floor(hf) % 6;
        float f = hf - (float)Math.Floor(hf);

        int vv = (int)(v * 255f);
        int p = (int)(vv * (1f - s));
        int q = (int)(vv * (1f - f * s));
        int t = (int)(vv * (1f - (1f - f) * s));

        switch (hi)
        {
            case 0: return Color.FromArgb(255, vv, t, p);
            case 1: return Color.FromArgb(255, q, vv, p);
            case 2: return Color.FromArgb(255, p, vv, t);
            case 3: return Color.FromArgb(255, p, q, vv);
            case 4: return Color.FromArgb(255, t, p, vv);
            default: return Color.FromArgb(255, vv, p, q);
        }
    }
}

public class GlowSpectrum : AudioSpectrumBase, IDisposable
{
    private const int BarCount = 64;
    private readonly SolidBrush[] _barBrushes;
    private readonly Pen[] _glowPens;
    private readonly float[] _heights;
    private readonly SolidBrush _shadowBrush;
    private bool _disposed;

    private const float MinHeight = 2f;

    // Paramètre de lissage réactif
    private const float Smoothing = 0.2f; // ajustable : 0.0 = instantané, 1.0 = très lent

    public GlowSpectrum()
    {
        _barBrushes = new SolidBrush[BarCount];
        _glowPens = new Pen[BarCount];
        _heights = new float[BarCount];
        _shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0));

        for (int i = 0; i < BarCount; i++)
        {
            Color c = ColorFromSpectrum(i, BarCount);
            _barBrushes[i] = new SolidBrush(c);

            _glowPens[i] = new Pen(Color.FromArgb(180, c), 6f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Flat,
                EndCap = LineCap.Flat
            };
        }
    }

    public override void Draw(Graphics g, Complex[] fftBuffer, int width, int height)
    {
        if (fftBuffer == null || fftBuffer.Length == 0) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        int barWidth = Math.Max(1, width / BarCount);

        for (int i = 0; i < BarCount; i++)
        {
            // Sécurisation FFT index
            int fftIndex = i * (fftBuffer.Length / 2) / BarCount;
            if (fftIndex >= fftBuffer.Length)
                fftIndex = fftBuffer.Length - 1;

            double re = fftBuffer[fftIndex].X;
            double im = fftBuffer[fftIndex].Y;
            double mag = Math.Sqrt(re * re + im * im);

            // Amplification
            float target = (float)(mag * 75000); 
            if (target < MinHeight) target = MinHeight;
            if (target > height) target = height;

            // Lissage réactif bidirectionnel
            _heights[i] += (target - _heights[i]) * Smoothing;

            float h = _heights[i];
            float x = i * barWidth;
            float y = height - h;

            // Barre principale
            g.FillRectangle(_barBrushes[i], x, y, barWidth - 2, h);

            // Ombre pseudo 3D
            g.FillRectangle(_shadowBrush, x + 3, y + 3, barWidth - 2, h);

            // Glow vertical simple
            float cx = x + barWidth * 0.5f;
            g.DrawLine(_glowPens[i], cx, height, cx, y + 6f);
        }
    }

    private Color ColorFromSpectrum(int index, int total)
    {
        float hue = (float)index / total * 360f;
        return HsvToRgb(hue, 1f, 1f);
    }

    private Color HsvToRgb(float h, float s, float v)
    {
        int hi = (int)Math.Floor(h / 60) % 6;
        float f = h / 60 - (float)Math.Floor(h / 60);
        int vv = (int)Math.Round(v * 255);
        int p = (int)Math.Round(vv * (1 - s));
        int q = (int)Math.Round(vv * (1 - f * s));
        int t = (int)Math.Round(vv * (1 - (1 - f) * s));

        switch (hi)
        {
            case 0: return Color.FromArgb(255, vv, t, p);
            case 1: return Color.FromArgb(255, q, vv, p);
            case 2: return Color.FromArgb(255, p, vv, t);
            case 3: return Color.FromArgb(255, p, q, vv);
            case 4: return Color.FromArgb(255, t, p, vv);
            case 5: return Color.FromArgb(255, vv, p, q);
            default: return Color.FromArgb(255, vv, vv, vv);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var b in _barBrushes) b?.Dispose();
        foreach (var p in _glowPens) p?.Dispose();
        _shadowBrush?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

public class WaveTrailSpectrum : AudioSpectrumBase
{
    private readonly Color[] palette;

    public WaveTrailSpectrum()
    {
        palette = new Color[256];
        for (int i = 0; i < 256; i++)
            palette[i] = ColorUtils.HsvToRgb(i * (360f / 256f), 1f, 1f);
    }

    public override void Draw(Graphics g, Complex[] fftBuffer, int width, int height)
    {
        if (fftBuffer == null || fftBuffer.Length == 0) return;

        PointF[] points = new PointF[fftBuffer.Length];
        for (int i = 0; i < fftBuffer.Length; i++)
        {
            double mag = Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);
            float x = i * width / (float)fftBuffer.Length;
            float y = height - (float)Math.Min(mag * 10000, height);
            points[i] = new PointF(x, y);
        }

        for (int i = 1; i < points.Length; i++)
        {
            using (Pen pen = new Pen(palette[i % 256], 2))
                g.DrawLine(pen, points[i - 1], points[i]);
        }
    }
}

public class SinusWaveSpectrum : AudioSpectrumBase
{
    private float phase = 0f;

    public override void Draw(Graphics g, Complex[] fftBuffer, int width, int height)
    {
        if (fftBuffer == null || fftBuffer.Length == 0) return;

        using (Pen pen = new Pen(Color.Lime, 2))
        {
            PointF[] points = new PointF[fftBuffer.Length];

            for (int i = 0; i < fftBuffer.Length; i++)
            {
                double mag = Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);
                float x = i * width / (float)fftBuffer.Length;
                float y = height / 2f - (float)(Math.Sin(i * 0.1 + phase) * mag * 5000);
                points[i] = new PointF(x, y);
            }

            if (points.Length > 1)
                g.DrawLines(pen, points);
        }

        phase += 0.05f;
    }
}

public class SpectrogramSpectrum : AudioSpectrumBase
{
    private Bitmap spectrogramBmp;

    public override void Draw(Graphics g, Complex[] fftBuffer, int width, int height)
    {
        if (width <= 0 || height <= 0 || fftBuffer == null || fftBuffer.Length == 0) return;

        if (spectrogramBmp == null || spectrogramBmp.Width != width || spectrogramBmp.Height != height)
            spectrogramBmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        // Décale la bitmap d'une colonne vers la gauche pour effet déroulant
        using (Graphics bmpG = Graphics.FromImage(spectrogramBmp))
        {
            bmpG.DrawImage(spectrogramBmp, -1, 0);
        }

        // Verrouille la bitmap en lecture/écriture et met à jour la dernière colonne (droite)
        var rect = new Rectangle(0, 0, spectrogramBmp.Width, spectrogramBmp.Height);
        var bmpData = spectrogramBmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite, spectrogramBmp.PixelFormat);
        try
        {
            int stride = Math.Abs(bmpData.Stride);
            int bytes = stride * spectrogramBmp.Height;
            byte[] data = new byte[bytes];

            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, data, 0, bytes);

            int x = spectrogramBmp.Width - 1;
            for (int i = 0; i < fftBuffer.Length && i < spectrogramBmp.Height; i++)
            {
                double mag = Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);
                mag = Math.Min(mag * 10000, 1.0);
                byte brightness = (byte)(Math.Min(1.0, mag) * 255);

                int y = spectrogramBmp.Height - i - 1;
                int idx = y * stride + x * 4;

                if (idx + 3 < data.Length && idx >= 0)
                {
                    data[idx + 0] = (byte)(255 - brightness); // B
                    data[idx + 1] = 0;                        // G
                    data[idx + 2] = brightness;               // R
                    data[idx + 3] = 255;                      // A
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(data, 0, bmpData.Scan0, bytes);
        }
        finally
        {
            spectrogramBmp.UnlockBits(bmpData);
        }

        g.DrawImage(spectrogramBmp, 0, 0, width, height);
    }
}

public class TranslucentPanel : Panel
{
    private int overlayAlpha = 150;
    private Color overlayColor = Color.Black;

    public TranslucentPanel()
    {
        this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        this.UpdateStyles();
    }

    // Valeur 0..255
    public int OverlayAlpha
    {
        get { return overlayAlpha; }
        set
        {
            overlayAlpha = Math.Max(0, Math.Min(255, value));
            this.Invalidate();
        }
    }

    // Optionnel : changer la couleur de la teinte (par défaut noir)
    public Color OverlayColor
    {
        get { return overlayColor; }
        set
        {
            overlayColor = value;
            this.Invalidate();
        }
    }

    public void SetOpacity(float percent)
    {
        percent = Math.Max(0f, Math.Min(1f, percent));
        OverlayAlpha = (int)(percent * 255f);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Dessine BackgroundImage / BackColor d'abord
        base.OnPaintBackground(e);

        // Puis superposer la teinte semi-transparente
        using (var b = new SolidBrush(Color.FromArgb(overlayAlpha, overlayColor)))
        {
            e.Graphics.FillRectangle(b, this.ClientRectangle);
        }
    }
}

// End of code file