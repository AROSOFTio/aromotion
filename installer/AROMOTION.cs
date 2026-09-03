using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AROMOTION
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public sealed class MainForm : Form
    {
        readonly string appDir = AppDomain.CurrentDomain.BaseDirectory;
        readonly string ffmpeg;
        readonly string ffprobe;
        readonly TextBox outputBox = new TextBox();
        readonly ComboBox qualityBox = new ComboBox();
        readonly ComboBox fpsBox = new ComboBox();
        readonly ComboBox micBox = new ComboBox();
        readonly ComboBox systemAudioBox = new ComboBox();
        readonly ComboBox webcamBox = new ComboBox();
        readonly CheckBox micCheck = new CheckBox();
        readonly CheckBox systemAudioCheck = new CheckBox();
        readonly CheckBox webcamCheck = new CheckBox();
        readonly CheckBox haloCheck = new CheckBox();
        readonly CheckBox clickPulseCheck = new CheckBox();
        readonly CheckBox autoZoomCheck = new CheckBox();
        readonly CheckBox perspectiveCheck = new CheckBox();
        readonly NumericUpDown zoomBox = new NumericUpDown();
        readonly Label status = new Label();
        readonly Label sessionLabel = new Label();
        readonly Button recordButton = new Button();
        readonly Button stopButton = new Button();
        readonly Button renderButton = new Button();
        readonly Button refreshButton = new Button();
        readonly List<Process> captureProcesses = new List<Process>();
        MouseCapture mouseCapture;
        MouseOverlay overlay;
        string currentSession;
        string currentScreen;
        string currentEvents;
        string currentAudioMux;
        bool stopping;

        public MainForm()
        {
            ffmpeg = Path.Combine(appDir, "tools", "ffmpeg", "ffmpeg.exe");
            ffprobe = Path.Combine(appDir, "tools", "ffmpeg", "ffprobe.exe");

            Text = "AROMOTION Studio — Recorder & Motion Engine";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 720);
            Size = new Size(1120, 790);
            BackColor = Color.FromArgb(15, 17, 22);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);

            BuildUi();
            Shown += delegate { RefreshDevices(); };
            FormClosing += OnFormClosing;
        }

        void BuildUi()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Color.FromArgb(22, 25, 32) };
            Controls.Add(header);
            var title = new Label { Text = "AROMOTION", AutoSize = true, Font = new Font("Segoe UI", 24, FontStyle.Bold), Location = new Point(25, 16) };
            var sub = new Label { Text = "Capture clean. Move attention. Explain beautifully.", AutoSize = true, ForeColor = Color.FromArgb(160, 171, 190), Location = new Point(29, 54) };
            status.Text = "READY"; status.AutoSize = true; status.Font = new Font("Segoe UI", 10, FontStyle.Bold); status.ForeColor = Color.FromArgb(105, 220, 155); status.Anchor = AnchorStyles.Top | AnchorStyles.Right; status.Location = new Point(950, 32);
            header.Controls.Add(title); header.Controls.Add(sub); header.Controls.Add(status);

            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 8) };
            Controls.Add(tabs);
            tabs.BringToFront();
            var recorderTab = new TabPage("Recorder") { BackColor = BackColor, ForeColor = ForeColor };
            var motionTab = new TabPage("Mouse + Motion") { BackColor = BackColor, ForeColor = ForeColor };
            var infoTab = new TabPage("Project") { BackColor = BackColor, ForeColor = ForeColor };
            tabs.TabPages.Add(recorderTab); tabs.TabPages.Add(motionTab); tabs.TabPages.Add(infoTab);

            BuildRecorderTab(recorderTab);
            BuildMotionTab(motionTab);
            BuildProjectTab(infoTab);
        }

        Label L(string text, int x, int y, int width)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, 24), ForeColor = Color.FromArgb(220, 225, 235) };
        }

        void StyleCombo(ComboBox c)
        {
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.BackColor = Color.FromArgb(245,245,247);
            c.ForeColor = Color.Black;
        }

        void BuildRecorderTab(Control tab)
        {
            int left = 30;
            tab.Controls.Add(L("Project folder", left, 28, 180));
            outputBox.Location = new Point(left, 53); outputBox.Size = new Size(790, 28);
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (String.IsNullOrWhiteSpace(videos)) videos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            outputBox.Text = Path.Combine(videos, "AROMOTION Projects");
            tab.Controls.Add(outputBox);
            var browse = new Button { Text = "Browse…", Location = new Point(835, 51), Size = new Size(110, 31) };
            browse.Click += delegate { using (var d = new FolderBrowserDialog()) { d.SelectedPath = outputBox.Text; if (d.ShowDialog(this) == DialogResult.OK) outputBox.Text = d.SelectedPath; } };
            tab.Controls.Add(browse);

            tab.Controls.Add(L("Screen quality", left, 108, 180));
            StyleCombo(qualityBox); qualityBox.Location = new Point(left, 133); qualityBox.Size = new Size(490, 30);
            qualityBox.Items.AddRange(new object[] {
                "Compact — H.264 CRF 23 (small files)",
                "Standard — H.264 CRF 18",
                "High — H.264 4:4:4 CRF 14 (sharp UI/text)",
                "Near Lossless — H.264 4:4:4 CRF 8",
                "Lossless RGB — H.264 RGB CRF 0",
                "Archival Lossless — FFV1 Level 3"
            });
            qualityBox.SelectedIndex = 2; tab.Controls.Add(qualityBox);

            tab.Controls.Add(L("FPS", 555, 108, 90));
            StyleCombo(fpsBox); fpsBox.Location = new Point(555, 133); fpsBox.Size = new Size(110, 30); fpsBox.Items.AddRange(new object[] { "30", "60", "120" }); fpsBox.SelectedIndex = 1; tab.Controls.Add(fpsBox);

            var audioGroup = new GroupBox { Text = "Audio — separate high-quality masters", Location = new Point(left, 195), Size = new Size(915, 185), ForeColor = ForeColor };
            tab.Controls.Add(audioGroup);
            micCheck.Text = "Microphone"; micCheck.Checked = true; micCheck.Location = new Point(18, 32); micCheck.AutoSize = true; audioGroup.Controls.Add(micCheck);
            StyleCombo(micBox); micBox.Location = new Point(150, 28); micBox.Size = new Size(590, 30); audioGroup.Controls.Add(micBox);
            systemAudioCheck.Text = "System audio"; systemAudioCheck.Checked = false; systemAudioCheck.Location = new Point(18, 78); systemAudioCheck.AutoSize = true; audioGroup.Controls.Add(systemAudioCheck);
            StyleCombo(systemAudioBox); systemAudioBox.Location = new Point(150, 74); systemAudioBox.Size = new Size(590, 30); audioGroup.Controls.Add(systemAudioBox);
            var audioNote = new Label { Text = "Microphone/system masters are recorded at 48 kHz PCM 24-bit where the device supports it. For system audio choose Stereo Mix / loopback / virtual cable if Windows exposes one.", Location = new Point(18, 120), Size = new Size(850, 48), ForeColor = Color.FromArgb(155,165,184) };
            audioGroup.Controls.Add(audioNote);

            var cameraGroup = new GroupBox { Text = "Webcam — independent track", Location = new Point(left, 400), Size = new Size(915, 100), ForeColor = ForeColor };
            tab.Controls.Add(cameraGroup);
            webcamCheck.Text = "Record webcam"; webcamCheck.Location = new Point(18, 32); webcamCheck.AutoSize = true; cameraGroup.Controls.Add(webcamCheck);
            StyleCombo(webcamBox); webcamBox.Location = new Point(150, 28); webcamBox.Size = new Size(590, 30); cameraGroup.Controls.Add(webcamBox);
            refreshButton.Text = "Refresh devices"; refreshButton.Location = new Point(760, 27); refreshButton.Size = new Size(130, 32); refreshButton.Click += delegate { RefreshDevices(); }; cameraGroup.Controls.Add(refreshButton);

            recordButton.Text = "●  RECORD"; recordButton.Font = new Font("Segoe UI", 13, FontStyle.Bold); recordButton.Location = new Point(left, 535); recordButton.Size = new Size(430, 60); recordButton.BackColor = Color.FromArgb(215, 62, 76); recordButton.ForeColor = Color.White; recordButton.FlatStyle = FlatStyle.Flat; recordButton.Click += delegate { StartRecording(); }; tab.Controls.Add(recordButton);
            stopButton.Text = "■  STOP & SAVE"; stopButton.Font = new Font("Segoe UI", 13, FontStyle.Bold); stopButton.Location = new Point(515, 535); stopButton.Size = new Size(430, 60); stopButton.Enabled = false; stopButton.FlatStyle = FlatStyle.Flat; stopButton.Click += delegate { StopRecording(); }; tab.Controls.Add(stopButton);
        }

        void BuildMotionTab(Control tab)
        {
            var g = new GroupBox { Text = "Mouse effects", Location = new Point(30, 28), Size = new Size(915, 150), ForeColor = ForeColor };
            tab.Controls.Add(g);
            haloCheck.Text = "Cursor halo / spotlight while recording"; haloCheck.Checked = true; haloCheck.Location = new Point(20, 32); haloCheck.AutoSize = true; g.Controls.Add(haloCheck);
            clickPulseCheck.Text = "Click pulse ring"; clickPulseCheck.Checked = true; clickPulseCheck.Location = new Point(20, 72); clickPulseCheck.AutoSize = true; g.Controls.Add(clickPulseCheck);
            var note = new Label { Text = "Mouse position and clicks are also saved as metadata, so later AROMOTION versions can reconstruct and smooth the cursor independently of the screen master.", Location = new Point(340, 31), Size = new Size(525, 72), ForeColor = Color.FromArgb(155,165,184) };
            g.Controls.Add(note);

            var m = new GroupBox { Text = "Automatic motion generation", Location = new Point(30, 205), Size = new Size(915, 245), ForeColor = ForeColor };
            tab.Controls.Add(m);
            autoZoomCheck.Text = "Generate smooth auto-zoom from clicks"; autoZoomCheck.Checked = true; autoZoomCheck.Location = new Point(20, 35); autoZoomCheck.AutoSize = true; m.Controls.Add(autoZoomCheck);
            perspectiveCheck.Text = "Add perspective 3D motion during focus moves"; perspectiveCheck.Checked = true; perspectiveCheck.Location = new Point(20, 77); perspectiveCheck.AutoSize = true; m.Controls.Add(perspectiveCheck);
            m.Controls.Add(new Label { Text = "Zoom strength", Location = new Point(20, 122), Size = new Size(120, 24) });
            zoomBox.DecimalPlaces = 2; zoomBox.Minimum = 120; zoomBox.Maximum = 250; zoomBox.Increment = 5; zoomBox.Value = 165; zoomBox.Location = new Point(145, 118); zoomBox.Size = new Size(90, 28); m.Controls.Add(zoomBox);
            m.Controls.Add(new Label { Text = "%", Location = new Point(240, 122), Size = new Size(30, 24) });
            var expl = new Label { Text = "Auto motion is rendered after capture from timestamps and click coordinates. The untouched master remains available. 3D mode uses FFmpeg's perspective transform evaluated frame-by-frame — not a fake 2D rotation.", Location = new Point(20, 165), Size = new Size(850, 62), ForeColor = Color.FromArgb(155,165,184) };
            m.Controls.Add(expl);

            renderButton.Text = "Generate Motion Preview"; renderButton.Location = new Point(30, 485); renderButton.Size = new Size(300, 48); renderButton.Enabled = false; renderButton.Click += delegate { RenderMotion(); }; tab.Controls.Add(renderButton);
        }

        void BuildProjectTab(Control tab)
        {
            tab.Controls.Add(new Label { Text = "Current / last session", Font = new Font("Segoe UI", 14, FontStyle.Bold), Location = new Point(30, 30), Size = new Size(300, 32) });
            sessionLabel.Text = "No recording yet."; sessionLabel.Location = new Point(30, 78); sessionLabel.Size = new Size(900, 70); sessionLabel.ForeColor = Color.FromArgb(165,175,194); tab.Controls.Add(sessionLabel);
            var text = new Label {
                Text = "AROMOTION keeps screen, microphone, system audio, webcam and mouse/click metadata as independent sources. This is intentional: zoom, cursor reconstruction, 3D motion and webcam layout can be changed later without damaging the original recording.",
                Location = new Point(30, 170), Size = new Size(900, 100), ForeColor = Color.FromArgb(190,198,212)
            };
            tab.Controls.Add(text);
        }

        void RefreshDevices()
        {
            if (!File.Exists(ffmpeg)) { MessageBox.Show(this, "FFmpeg is missing. Re-run the AROMOTION installer.", "AROMOTION", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            status.Text = "SCANNING DEVICES…"; Application.DoEvents();
            try
            {
                var devices = DeviceScanner.Scan(ffmpeg);
                FillCombo(micBox, devices.Audio);
                FillCombo(systemAudioBox, devices.Audio);
                FillCombo(webcamBox, devices.Video);
                var stereo = devices.Audio.FirstOrDefault(x => x.IndexOf("stereo mix", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("loopback", StringComparison.OrdinalIgnoreCase) >= 0);
                if (stereo != null) systemAudioBox.SelectedItem = stereo;
                status.Text = "READY"; status.ForeColor = Color.FromArgb(105,220,155);
            }
            catch (Exception ex)
            {
                status.Text = "DEVICE SCAN ERROR";
                MessageBox.Show(this, ex.Message, "AROMOTION device scan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void FillCombo(ComboBox box, List<string> values)
        {
            string old = box.SelectedItem == null ? null : box.SelectedItem.ToString();
            box.Items.Clear(); foreach (var x in values) box.Items.Add(x);
            if (old != null && box.Items.Contains(old)) box.SelectedItem = old; else if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        void StartRecording()
        {
            if (captureProcesses.Count > 0) return;
            try
            {
                Directory.CreateDirectory(outputBox.Text.Trim());
                currentSession = Path.Combine(outputBox.Text.Trim(), DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
                Directory.CreateDirectory(currentSession);
                currentScreen = Path.Combine(currentSession, "screen_master.mkv");
                currentEvents = Path.Combine(currentSession, "mouse_events.csv");
                currentAudioMux = Path.Combine(currentSession, "recording_with_audio.mkv");
                File.WriteAllText(Path.Combine(currentSession, "project.txt"), ProjectSummary());

                int fps = Int32.Parse(fpsBox.SelectedItem.ToString(), CultureInfo.InvariantCulture);
                captureProcesses.Add(StartFfmpeg(BuildScreenArgs(currentScreen, fps, qualityBox.SelectedIndex)));

                if (micCheck.Checked && micBox.SelectedItem != null)
                    captureProcesses.Add(StartFfmpeg(BuildAudioArgs(micBox.SelectedItem.ToString(), Path.Combine(currentSession, "microphone.wav"))));
                if (systemAudioCheck.Checked && systemAudioBox.SelectedItem != null)
                    captureProcesses.Add(StartFfmpeg(BuildAudioArgs(systemAudioBox.SelectedItem.ToString(), Path.Combine(currentSession, "system_audio.wav"))));
                if (webcamCheck.Checked && webcamBox.SelectedItem != null)
                    captureProcesses.Add(StartFfmpeg(BuildWebcamArgs(webcamBox.SelectedItem.ToString(), Path.Combine(currentSession, "webcam.mkv"))));

                mouseCapture = new MouseCapture(currentEvents);
                mouseCapture.Start();
                if (haloCheck.Checked || clickPulseCheck.Checked)
                {
                    overlay = new MouseOverlay(mouseCapture, haloCheck.Checked, clickPulseCheck.Checked);
                    overlay.Show();
                }

                SetRecordingUi(true);
                sessionLabel.Text = currentSession;
            }
            catch (Exception ex)
            {
                StopAllProcesses();
                MessageBox.Show(this, ex.Message, "AROMOTION recording error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        string ProjectSummary()
        {
            return "AROMOTION Studio Phase 1\r\nStarted: " + DateTime.Now.ToString("O") + "\r\nQuality: " + qualityBox.SelectedItem + "\r\nFPS: " + fpsBox.SelectedItem + "\r\nMicrophone: " + (micCheck.Checked ? Convert.ToString(micBox.SelectedItem) : "off") + "\r\nSystem audio: " + (systemAudioCheck.Checked ? Convert.ToString(systemAudioBox.SelectedItem) : "off") + "\r\nWebcam: " + (webcamCheck.Checked ? Convert.ToString(webcamBox.SelectedItem) : "off") + "\r\n";
        }

        string BuildScreenArgs(string output, int fps, int profile)
        {
            string codec;
            if (profile == 0) codec = "-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p";
            else if (profile == 1) codec = "-c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p";
            else if (profile == 2) codec = "-c:v libx264 -preset veryfast -crf 14 -pix_fmt yuv444p";
            else if (profile == 3) codec = "-c:v libx264 -preset fast -crf 8 -pix_fmt yuv444p";
            else if (profile == 4) codec = "-c:v libx264rgb -preset ultrafast -crf 0";
            else codec = "-c:v ffv1 -level 3 -coder 1 -g 1";
            return "-hide_banner -y -f gdigrab -framerate " + fps + " -draw_mouse 1 -i desktop " + codec + " -f matroska " + Q(output);
        }

        string BuildAudioArgs(string device, string output)
        {
            return "-hide_banner -y -f dshow -i audio=" + Q(device) + " -ar 48000 -c:a pcm_s24le " + Q(output);
        }

        string BuildWebcamArgs(string device, string output)
        {
            return "-hide_banner -y -f dshow -i video=" + Q(device) + " -c:v libx264 -preset veryfast -crf 16 -pix_fmt yuv420p -an " + Q(output);
        }

        Process StartFfmpeg(string args)
        {
            var psi = new ProcessStartInfo(ffmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
            var p = new Process { StartInfo = psi };
            var errors = new StringBuilder();
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (!String.IsNullOrEmpty(e.Data) && errors.Length < 8000) errors.AppendLine(e.Data); };
            if (!p.Start()) throw new InvalidOperationException("FFmpeg could not start.");
            p.BeginErrorReadLine(); Thread.Sleep(350);
            if (p.HasExited) throw new InvalidOperationException("A capture source could not start. FFmpeg exit code " + p.ExitCode + ".\r\n\r\n" + errors.ToString());
            return p;
        }

        void StopRecording()
        {
            if (stopping) return; stopping = true; status.Text = "FINALIZING…"; Application.DoEvents();
            try
            {
                if (mouseCapture != null) { mouseCapture.Stop(); mouseCapture.Dispose(); mouseCapture = null; }
                if (overlay != null) { overlay.Close(); overlay.Dispose(); overlay = null; }
                StopAllProcesses();
                MuxAudio();
                renderButton.Enabled = File.Exists(currentScreen) && File.Exists(currentEvents);
                if (autoZoomCheck.Checked && renderButton.Enabled) RenderMotion();
                status.Text = "SAVED"; status.ForeColor = Color.FromArgb(105,220,155);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "AROMOTION finalize warning", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { SetRecordingUi(false); stopping = false; }
        }

        void StopAllProcesses()
        {
            foreach (var p in captureProcesses.ToArray())
            {
                try { if (!p.HasExited) { p.StandardInput.WriteLine("q"); p.StandardInput.Flush(); if (!p.WaitForExit(9000)) { p.Kill(); p.WaitForExit(); } } } catch { try { if (!p.HasExited) p.Kill(); } catch { } }
                try { p.Dispose(); } catch { }
            }
            captureProcesses.Clear();
        }

        void MuxAudio()
        {
            if (String.IsNullOrEmpty(currentScreen) || !File.Exists(currentScreen)) return;
            var mic = Path.Combine(currentSession, "microphone.wav");
            var sys = Path.Combine(currentSession, "system_audio.wav");
            var args = new StringBuilder("-hide_banner -y -i ").Append(Q(currentScreen));
            int next = 1; var maps = new StringBuilder(" -map 0:v:0 ");
            if (File.Exists(mic)) { args.Append(" -i ").Append(Q(mic)); maps.Append(" -map ").Append(next++).Append(":a:0"); }
            if (File.Exists(sys)) { args.Append(" -i ").Append(Q(sys)); maps.Append(" -map ").Append(next++).Append(":a:0"); }
            if (next == 1) return;
            args.Append(maps).Append(" -c:v copy -c:a flac -shortest ").Append(Q(currentAudioMux));
            RunBlocking(ffmpeg, args.ToString(), 120000);
        }

        void RenderMotion()
        {
            if (String.IsNullOrEmpty(currentScreen) || !File.Exists(currentScreen) || !File.Exists(currentEvents)) return;
            status.Text = "RENDERING MOTION…"; Application.DoEvents();
            try
            {
                int fps = Int32.Parse(fpsBox.SelectedItem.ToString(), CultureInfo.InvariantCulture);
                var wh = ProbeSize(currentScreen);
                var clicks = ClickPoint.Load(currentEvents).Where((x, i) => i == 0 || x.Seconds - ClickPoint.Load(currentEvents)[Math.Max(0, i - 1)].Seconds > 0.15).Take(24).ToList();
                if (clicks.Count == 0) { status.Text = "NO CLICKS TO ZOOM"; return; }
                double zoom = Decimal.ToDouble(zoomBox.Value) / 100.0;
                string z = BuildZoomExpr(clicks, zoom);
                string cx = BuildCoordExpr(clicks, wh.Item1, true);
                string cy = BuildCoordExpr(clicks, wh.Item2, false);
                string filter = "scale=w='trunc(" + wh.Item1 + "*(" + z + ")/2)*2':h='trunc(" + wh.Item2 + "*(" + z + ")/2)*2':eval=frame,crop=" + wh.Item1 + ":" + wh.Item2 + ":x='max(0,min(iw-" + wh.Item1 + ",(" + cx + ")*iw/" + wh.Item1 + "-" + (wh.Item1/2) + "))':y='max(0,min(ih-" + wh.Item2 + ",(" + cy + ")*ih/" + wh.Item2 + "-" + (wh.Item2/2) + "))'";
                if (perspectiveCheck.Checked) filter += "," + BuildPerspectiveExpr(clicks, wh.Item1, wh.Item2, fps);
                var motionVideo = Path.Combine(currentSession, "motion_preview_video.mkv");
                string args = "-hide_banner -y -i " + Q(currentScreen) + " -vf " + Q(filter) + " -c:v libx264 -preset medium -crf 12 -pix_fmt yuv444p -an " + Q(motionVideo);
                RunBlocking(ffmpeg, args, 600000);

                var final = Path.Combine(currentSession, "AROMOTION_motion_preview.mkv");
                if (File.Exists(currentAudioMux))
                    RunBlocking(ffmpeg, "-hide_banner -y -i " + Q(motionVideo) + " -i " + Q(currentAudioMux) + " -map 0:v:0 -map 1:a? -c:v copy -c:a copy -shortest " + Q(final), 180000);
                else File.Copy(motionVideo, final, true);
                status.Text = "MOTION READY"; status.ForeColor = Color.FromArgb(105,220,155);
                sessionLabel.Text = currentSession + "\r\nMotion preview: " + final;
            }
            catch (Exception ex) { status.Text = "MOTION ERROR"; MessageBox.Show(this, ex.Message, "AROMOTION motion renderer", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        Tuple<int,int> ProbeSize(string file)
        {
            var psi = new ProcessStartInfo(ffprobe, "-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 " + Q(file)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi)) { string s = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); var a = s.Split('x'); return Tuple.Create(Int32.Parse(a[0]), Int32.Parse(a[1])); }
        }

        string BuildZoomExpr(List<ClickPoint> clicks, double zoom)
        {
            string rest = "1";
            for (int i = clicks.Count - 1; i >= 0; i--)
            {
                double s = Math.Max(0, clicks[i].Seconds - 0.12), a = s + 0.28, b = s + 1.55, e = s + 1.85;
                string phase = "if(between(t,"+F(s)+","+F(a)+"),1+("+F(zoom-1)+")*(t-"+F(s)+")/"+F(a-s)+",if(between(t,"+F(a)+","+F(b)+"),"+F(zoom)+",1+("+F(zoom-1)+")*("+F(e)+"-t)/"+F(e-b)+"))";
                rest = "if(between(t,"+F(s)+","+F(e)+"),"+phase+","+rest+")";
            }
            return rest;
        }

        string BuildCoordExpr(List<ClickPoint> clicks, int fallback, bool x)
        {
            string rest = F(fallback / 2.0);
            for (int i = clicks.Count - 1; i >= 0; i--)
            {
                double s = Math.Max(0, clicks[i].Seconds - 0.12), e = s + 1.85;
                int v = x ? clicks[i].X : clicks[i].Y;
                rest = "if(between(t,"+F(s)+","+F(e)+"),"+v+","+rest+")";
            }
            return rest;
        }

        string BuildPerspectiveExpr(List<ClickPoint> clicks, int w, int h, int fps)
        {
            int amp = Math.Max(8, (int)(w * 0.018));
            string x0 = "0", x1 = "W", x2 = "0", x3 = "W";
            for (int i = clicks.Count - 1; i >= 0; i--)
            {
                int s = Math.Max(1, (int)((clicks[i].Seconds - 0.05) * fps));
                int a = s + Math.Max(1, (int)(0.25 * fps));
                int b = s + Math.Max(2, (int)(1.45 * fps));
                int e = s + Math.Max(3, (int)(1.75 * fps));
                int dir = clicks[i].X < w/2 ? -1 : 1;
                string p = "if(between(on,"+s+","+a+"),(on-"+s+")/"+(a-s)+",if(between(on,"+a+","+b+"),1,("+e+"-on)/"+(e-b)+"))";
                string active = "between(on,"+s+","+e+")";
                if (dir > 0)
                {
                    x0 = "if("+active+","+amp+"*("+p+"),"+x0+")";
                    x3 = "if("+active+",W-"+amp+"*("+p+"),"+x3+")";
                }
                else
                {
                    x1 = "if("+active+",W-"+amp+"*("+p+"),"+x1+")";
                    x2 = "if("+active+","+amp+"*("+p+"),"+x2+")";
                }
            }
            return "perspective=x0='"+x0+"':y0=0:x1='"+x1+"':y1=0:x2='"+x2+"':y2=H:x3='"+x3+"':y3=H:sense=destination:eval=frame:interpolation=cubic";
        }

        static string F(double d) { return d.ToString("0.###", CultureInfo.InvariantCulture); }
        static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

        void RunBlocking(string exe, string args, int timeout)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeout)) { try { p.Kill(); } catch { } throw new TimeoutException("The media operation took too long."); }
                if (p.ExitCode != 0) throw new InvalidOperationException("FFmpeg failed ("+p.ExitCode+").\r\n" + Tail(err, 3000));
            }
        }

        static string Tail(string s, int n) { if (String.IsNullOrEmpty(s)) return s; return s.Length <= n ? s : s.Substring(s.Length - n); }

        void SetRecordingUi(bool recording)
        {
            recordButton.Enabled = !recording; stopButton.Enabled = recording; refreshButton.Enabled = !recording; outputBox.Enabled = !recording; qualityBox.Enabled = !recording; fpsBox.Enabled = !recording;
            if (recording) { status.Text = "● RECORDING"; status.ForeColor = Color.FromArgb(255,95,105); } else if (status.Text == "● RECORDING") { status.Text = "READY"; status.ForeColor = Color.FromArgb(105,220,155); }
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (captureProcesses.Count > 0)
            {
                var r = MessageBox.Show(this, "A recording is running. Stop and save it before closing?", "AROMOTION", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) StopRecording(); else e.Cancel = true;
            }
        }
    }

    public sealed class DeviceList { public readonly List<string> Audio = new List<string>(); public readonly List<string> Video = new List<string>(); }

    public static class DeviceScanner
    {
        public static DeviceList Scan(string ffmpeg)
        {
            var result = new DeviceList();
            var psi = new ProcessStartInfo(ffmpeg, "-hide_banner -list_devices true -f dshow -i dummy") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                string text = p.StandardError.ReadToEnd(); p.WaitForExit();
                foreach (string raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var m = Regex.Match(raw, "\\\"(?<name>[^\\\"]+)\\\"\\s+\\((?<kind>audio|video)\\)", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    string name = m.Groups["name"].Value; string kind = m.Groups["kind"].Value.ToLowerInvariant();
                    if (kind == "audio" && !result.Audio.Contains(name)) result.Audio.Add(name);
                    if (kind == "video" && !result.Video.Contains(name)) result.Video.Add(name);
                }
            }
            return result;
        }
    }

    public sealed class ClickPoint
    {
        public double Seconds; public int X; public int Y;
        public static List<ClickPoint> Load(string file)
        {
            var list = new List<ClickPoint>();
            foreach (var line in File.ReadAllLines(file).Skip(1))
            {
                var a = line.Split(','); if (a.Length < 5 || a[1] != "click") continue;
                double ms; int x,y; if (Double.TryParse(a[0], NumberStyles.Any, CultureInfo.InvariantCulture, out ms) && Int32.TryParse(a[2], out x) && Int32.TryParse(a[3], out y)) list.Add(new ClickPoint { Seconds = ms / 1000.0, X = x, Y = y });
            }
            return list;
        }
    }

    public sealed class MouseCapture : IDisposable
    {
        const int WH_MOUSE_LL = 14, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;
        readonly string file; readonly Stopwatch clock = new Stopwatch(); readonly HookProc proc; StreamWriter writer; IntPtr hook = IntPtr.Zero; long lastMove;
        public Point CursorPoint { get; private set; } public long LastClickMs { get; private set; }
        public MouseCapture(string filePath) { file = filePath; proc = Callback; }
        public void Start()
        {
            writer = new StreamWriter(file, false, new UTF8Encoding(false)); writer.AutoFlush = true; writer.WriteLine("ms,type,x,y,button"); clock.Restart();
            hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0); if (hook == IntPtr.Zero) throw new InvalidOperationException("Could not install mouse capture hook.");
        }
        public void Stop() { clock.Stop(); if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } if (writer != null) { writer.Flush(); writer.Dispose(); writer = null; } }
        IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && writer != null)
            {
                var d = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT)); CursorPoint = new Point(d.pt.x, d.pt.y); int msg = wParam.ToInt32(); long ms = clock.ElapsedMilliseconds;
                if (msg == WM_MOUSEMOVE && ms - lastMove >= 12) { lastMove = ms; writer.WriteLine(ms+",move,"+d.pt.x+","+d.pt.y+","); }
                else if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN) { LastClickMs = ms; writer.WriteLine(ms+",click,"+d.pt.x+","+d.pt.y+","+(msg==WM_LBUTTONDOWN?"left":"right")); }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }
        public void Dispose() { Stop(); }
        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x,y; }
        [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public UIntPtr dwExtraInfo; }
        [DllImport("user32.dll", SetLastError=true)] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
    }

    public sealed class MouseOverlay : Form
    {
        readonly MouseCapture capture; readonly bool halo, pulse; readonly Timer timer = new Timer();
        public MouseOverlay(MouseCapture c, bool showHalo, bool showPulse)
        {
            capture = c; halo = showHalo; pulse = showPulse; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Color.Magenta; TransparencyKey = Color.Magenta; Bounds = SystemInformation.VirtualScreen;
            timer.Interval = 16; timer.Tick += delegate { Invalidate(); }; timer.Start();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x20 | 0x80000 | 0x08000000; return cp; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var p = PointToClient(capture.CursorPoint); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (halo) using (var b = new SolidBrush(Color.FromArgb(55, 255, 220, 40))) e.Graphics.FillEllipse(b, p.X-24,p.Y-24,48,48);
            if (pulse)
            {
                long age = Environment.TickCount64Compat() - capture.LastClickMs; // replaced below by safe approximation at runtime
                age = 0; // hook time and overlay clock are intentionally not mixed; pulse is drawn as a small persistent click marker in Phase 1.
                if (capture.LastClickMs > 0) using (var pen = new Pen(Color.FromArgb(220,255,95,70),3)) e.Graphics.DrawEllipse(pen,p.X-16,p.Y-16,32,32);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    static class Environment
    {
        public static long TickCount64Compat() { return unchecked((uint)System.Environment.TickCount); }
    }
}
