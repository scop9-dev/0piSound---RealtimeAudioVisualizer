using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using System.IO;
using Newtonsoft.Json;

namespace AudioVisualizerApp
{
    internal static class Settings
    {
        public static bool EnableTrail { get; set; } = true;
        public static bool EnableGlow { get; set; } = true;
        public static bool ShowSpectrogram { get; set; } = false;
        public static bool UseSinusWave { get; set; } = false;
        public static bool AutoStart { get; set; } = true;

        private static readonly string settingsFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "0piSound", "settings.json");

        private class SettingsData
        {
            public bool EnableTrail { get; set; }
            public bool EnableGlow { get; set; }
            public bool ShowSpectrogram { get; set; }
            public bool UseSinusWave { get; set; }
            public bool AutoStart { get; set; }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(settingsFilePath)) return;
                string json = File.ReadAllText(settingsFilePath);
                var data = JsonConvert.DeserializeObject<SettingsData>(json);
                if (data == null) return;

                EnableTrail = data.EnableTrail;
                EnableGlow = data.EnableGlow;
                ShowSpectrogram = data.ShowSpectrogram;
                UseSinusWave = data.UseSinusWave;
                AutoStart = data.AutoStart;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Settings.Load failed: " + ex.Message);
            }
        }

        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(settingsFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var data = new SettingsData
                {
                    EnableTrail = EnableTrail,
                    EnableGlow = EnableGlow,
                    ShowSpectrogram = ShowSpectrogram,
                    UseSinusWave = UseSinusWave,
                    AutoStart = AutoStart
                };

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Settings.Save failed: " + ex.Message);
            }
        }
    }


    internal class SettingsForm : Form
    {
        private CheckBox cbTrail, cbGlow, cbSpectrogram, cbSinus, cbAutoStart;
        private Button btnOk, btnCancel;

        public SettingsForm()
        {
            this.Text = "Parameters";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.Size = new Size(320, 260);

            cbTrail = new CheckBox() { Text = "Activate trail effect", Location = new Point(20, 20), AutoSize = true };
            cbGlow = new CheckBox() { Text = "Activate glow", Location = new Point(20, 50), AutoSize = true };
            cbSpectrogram = new CheckBox() { Text = "Show Spectrogram", Location = new Point(20, 80), AutoSize = true };
            cbSinus = new CheckBox() { Text = "Use sinus wave", Location = new Point(20, 110), AutoSize = true };
            cbAutoStart = new CheckBox() { Text = "Auto start", Location = new Point(20, 140), AutoSize = true };

            btnOk = new Button() { Text = "OK", Location = new Point(60, 180), Size = new Size(80, 30) };
            btnCancel = new Button() { Text = "Cancel", Location = new Point(170, 180), Size = new Size(80, 30) };

            btnOk.Click += BtnOk_Click;
            btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            this.Controls.AddRange(new Control[] { cbTrail, cbGlow, cbSpectrogram, cbSinus, cbAutoStart, btnOk, btnCancel });

            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            cbTrail.Checked = Settings.EnableTrail;
            cbGlow.Checked = Settings.EnableGlow;
            cbSpectrogram.Checked = Settings.ShowSpectrogram;
            cbSinus.Checked = Settings.UseSinusWave;
            cbAutoStart.Checked = Settings.AutoStart;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            Settings.EnableTrail = cbTrail.Checked;
            Settings.EnableGlow = cbGlow.Checked;
            Settings.ShowSpectrogram = cbSpectrogram.Checked;
            Settings.UseSinusWave = cbSinus.Checked;
            Settings.AutoStart = cbAutoStart.Checked;

            AutoStartManager.SetAutoStart(Settings.AutoStart);

            Settings.Save();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        internal static class AutoStartManager
        {
            private const string AppName = "0piSound"; 

            public static void SetAutoStart(bool enable)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
                    {
                        if (key == null)
                        {
                            MessageBox.Show("Key cannot be created.", "Error",
                                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        if (enable)
                        {
                            string exePath = Application.ExecutablePath;
                            key.SetValue(AppName, $"\"{exePath}\""); 
                        }
                        else
                        {
                            key.DeleteValue(AppName, throwOnMissingValue: false);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show("You're not allowed to do this",
                                    "Permission Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Cannot change auto start : " + ex.Message,
                                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            public static bool IsAutoStartEnabled()
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", writable: false))
                    {
                        if (key == null) return false;
                        object value = key.GetValue(AppName);
                        return value != null;
                    }
                }
                catch
                {
                    return false; 
                }
            }
        }
    }
}
