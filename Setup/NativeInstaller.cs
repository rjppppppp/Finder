using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Finder Setup")]
[assembly: AssemblyDescription("Finder Setup Installer by TN Dev Lab")]
[assembly: AssemblyCompany("TN Dev Lab")]
[assembly: AssemblyProduct("Finder")]
[assembly: AssemblyCopyright("Copyright © 2026 TN Dev Lab")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace FinderInstaller
{
    public class SetupForm : Form
    {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        private ProgressBar progressBar;
        private Label lblStatus;
        private Button btnInstall;
        private CheckBox chkDesktop;
        private CheckBox chkStartMenu;
        private CheckBox chkStartup;
        private CheckBox chkLaunch;
        private string targetDir;
        private string targetExe;

        public SetupForm()
        {
            targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Finder"
            );
            targetExe = Path.Combine(targetDir, "FinderApp.exe");

            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "Finder Setup";
            this.Size = new Size(480, 420);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColorTranslator.FromHtml("#161821");
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            try
            {
                using (Stream icoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("finder.ico"))
                {
                    if (icoStream != null)
                    {
                        this.Icon = new Icon(icoStream);
                    }
                }
            }
            catch { }

            // Outer border paint
            this.Paint += (s, e) =>
            {
                using (Pen p = new Pen(ColorTranslator.FromHtml("#2C3246"), 1.5f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            // Draggable header panel
            Panel topPanel = new Panel();
            topPanel.Size = new Size(this.Width, 75);
            topPanel.Location = new Point(0, 0);
            topPanel.BackColor = ColorTranslator.FromHtml("#11131A");
            topPanel.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
            };
            this.Controls.Add(topPanel);

            // Icon box
            PictureBox picIcon = new PictureBox();
            picIcon.Size = new Size(46, 46);
            picIcon.Location = new Point(20, 15);
            picIcon.SizeMode = PictureBoxSizeMode.Zoom;
            picIcon.BackColor = Color.Transparent;
            try
            {
                using (Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("finder.png"))
                {
                    if (iconStream != null)
                    {
                        picIcon.Image = Image.FromStream(iconStream);
                    }
                }
            }
            catch { }
            topPanel.Controls.Add(picIcon);

            // Title
            Label lblTitle = new Label();
            lblTitle.Text = "Finder Setup";
            lblTitle.Font = new Font("Segoe UI", 13.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.Location = new Point(76, 14);
            lblTitle.AutoSize = true;
            topPanel.Controls.Add(lblTitle);

            // Subtitle
            Label lblSub = new Label();
            lblSub.Text = "by TN Dev Lab  •  Ultra-Fast System Search (Alt + Space)";
            lblSub.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblSub.ForeColor = ColorTranslator.FromHtml("#94A3B8");
            lblSub.Location = new Point(78, 42);
            lblSub.AutoSize = true;
            topPanel.Controls.Add(lblSub);

            // Close button (X)
            Button btnClose = new Button();
            btnClose.Text = "✕";
            btnClose.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            btnClose.ForeColor = ColorTranslator.FromHtml("#94A3B8");
            btnClose.BackColor = Color.Transparent;
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Size = new Size(32, 32);
            btnClose.Location = new Point(this.Width - 42, 12);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Close();
            topPanel.Controls.Add(btnClose);

            // Destination box
            Label lblDestTitle = new Label();
            lblDestTitle.Text = "Installation Location:";
            lblDestTitle.ForeColor = ColorTranslator.FromHtml("#64748B");
            lblDestTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblDestTitle.Location = new Point(24, 92);
            lblDestTitle.AutoSize = true;
            this.Controls.Add(lblDestTitle);

            Panel destBox = new Panel();
            destBox.Size = new Size(this.Width - 48, 32);
            destBox.Location = new Point(24, 114);
            destBox.BackColor = ColorTranslator.FromHtml("#11131A");
            destBox.Paint += (s, e) =>
            {
                using (Pen p = new Pen(ColorTranslator.FromHtml("#212533"), 1f))
                    e.Graphics.DrawRectangle(p, 0, 0, destBox.Width - 1, destBox.Height - 1);
            };

            Label lblPath = new Label();
            lblPath.Text = targetDir;
            lblPath.ForeColor = ColorTranslator.FromHtml("#94A3B8");
            lblPath.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblPath.Location = new Point(8, 7);
            lblPath.AutoSize = true;
            destBox.Controls.Add(lblPath);
            this.Controls.Add(destBox);

            // Options Checkboxes
            chkDesktop = CreateCheckBox("Create Desktop Shortcut", 24, 160, true);
            chkStartMenu = CreateCheckBox("Create Start Menu Shortcut", 24, 190, true);
            chkStartup = CreateCheckBox("Launch automatically on Windows Startup", 24, 220, true);
            chkLaunch = CreateCheckBox("Launch Finder immediately after install", 24, 250, true);

            this.Controls.Add(chkDesktop);
            this.Controls.Add(chkStartMenu);
            this.Controls.Add(chkStartup);
            this.Controls.Add(chkLaunch);

            // Progress bar
            progressBar = new ProgressBar();
            progressBar.Size = new Size(this.Width - 48, 8);
            progressBar.Location = new Point(24, 296);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Visible = false;
            this.Controls.Add(progressBar);

            // Status label
            lblStatus = new Label();
            lblStatus.Text = "Ready to install Finder.";
            lblStatus.ForeColor = ColorTranslator.FromHtml("#94A3B8");
            lblStatus.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lblStatus.Location = new Point(24, 312);
            lblStatus.Size = new Size(this.Width - 48, 24);
            this.Controls.Add(lblStatus);

            // Install button
            btnInstall = new Button();
            btnInstall.Text = "Install Finder";
            btnInstall.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            btnInstall.ForeColor = Color.White;
            btnInstall.BackColor = ColorTranslator.FromHtml("#0284C7");
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Size = new Size(this.Width - 48, 44);
            btnInstall.Location = new Point(24, 345);
            btnInstall.Cursor = Cursors.Hand;
            btnInstall.Click += BtnInstall_Click;
            this.Controls.Add(btnInstall);
        }

        private CheckBox CreateCheckBox(string text, int x, int y, bool isChecked)
        {
            CheckBox chk = new CheckBox();
            chk.Text = text;
            chk.Checked = isChecked;
            chk.ForeColor = ColorTranslator.FromHtml("#E2E8F0");
            chk.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            chk.Location = new Point(x, y);
            chk.AutoSize = true;
            chk.Cursor = Cursors.Hand;
            return chk;
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            if (btnInstall.Text == "Finished" || btnInstall.Text == "Close")
            {
                this.Close();
                return;
            }

            btnInstall.Enabled = false;
            progressBar.Visible = true;
            progressBar.Value = 10;
            lblStatus.Text = "Stopping active Finder processes...";

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;

            worker.DoWork += (s, args) =>
            {
                // 1. Kill active processes
                try
                {
                    foreach (var p in Process.GetProcessesByName("FinderApp"))
                    {
                        p.Kill();
                        p.WaitForExit(1500);
                    }
                    foreach (var p in Process.GetProcessesByName("Finder"))
                    {
                        p.Kill();
                        p.WaitForExit(1500);
                    }
                }
                catch { }

                worker.ReportProgress(30, "Extracting Finder files to destination...");

                // 2. Extract payload
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream resStream = asm.GetManifestResourceStream("payload.gz"))
                {
                    if (resStream != null)
                    {
                        using (GZipStream gz = new GZipStream(resStream, CompressionMode.Decompress))
                        using (FileStream fs = new FileStream(targetExe, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[65536];
                            int read;
                            while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                fs.Write(buffer, 0, read);
                            }
                        }
                    }
                    else
                    {
                        // Fallback uncompressed
                        using (Stream plainStream = asm.GetManifestResourceStream("FinderApp.exe"))
                        {
                            if (plainStream != null)
                            {
                                using (FileStream fs = new FileStream(targetExe, FileMode.Create, FileAccess.Write, FileShare.None))
                                {
                                    plainStream.CopyTo(fs);
                                }
                            }
                            else
                            {
                                throw new FileNotFoundException("Embedded Finder payload not found.");
                            }
                        }
                    }
                }

                string iconTarget = Path.Combine(targetDir, "app.ico");
                try
                {
                    using (Stream icoStream = asm.GetManifestResourceStream("app.ico"))
                    {
                        if (icoStream != null)
                        {
                            using (FileStream fsIco = new FileStream(iconTarget, FileMode.Create, FileAccess.Write))
                            {
                                icoStream.CopyTo(fsIco);
                            }
                        }
                    }
                }
                catch { }

                worker.ReportProgress(70, "Configuring shortcuts and startup...");

                // 3. Shortcuts
                if (chkDesktop.Checked)
                {
                    string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Finder.lnk");
                    CreateShortcut(desktopPath, targetExe, targetDir, iconTarget);
                }

                if (chkStartMenu.Checked)
                {
                    string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                    if (!Directory.Exists(startMenuDir)) Directory.CreateDirectory(startMenuDir);
                    string startMenuPath = Path.Combine(startMenuDir, "Finder.lnk");
                    CreateShortcut(startMenuPath, targetExe, targetDir, iconTarget);
                }

                // 4. Windows Startup
                if (chkStartup.Checked)
                {
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (key != null) key.SetValue("Finder", "\"" + targetExe + "\"");
                        }
                    }
                    catch { }
                }

                // 5. Windows Uninstall Registry
                try
                {
                    using (RegistryKey uninst = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Finder"))
                    {
                        if (uninst != null)
                        {
                            uninst.SetValue("DisplayName", "Finder");
                            uninst.SetValue("DisplayVersion", "1.0.0");
                            uninst.SetValue("Publisher", "TN Dev Lab");
                            uninst.SetValue("InstallLocation", targetDir);
                            uninst.SetValue("DisplayIcon", File.Exists(iconTarget) ? iconTarget : targetExe);
                            string uninstCmd = "powershell -WindowStyle Hidden -Command \"Stop-Process -Name FinderApp, Finder -Force -ErrorAction SilentlyContinue; Remove-Item -Path '" + targetDir + "' -Recurse -Force -ErrorAction SilentlyContinue; Remove-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' -Name 'Finder' -ErrorAction SilentlyContinue; Remove-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Finder' -Recurse -Force -ErrorAction SilentlyContinue; Remove-Item -Path \\\"$([Environment]::GetFolderPath('Desktop'))\\Finder.lnk\\\" -Force -ErrorAction SilentlyContinue; Remove-Item -Path \\\"$([Environment]::GetFolderPath('StartMenu'))\\Programs\\Finder.lnk\\\" -Force -ErrorAction SilentlyContinue;\"";
                            uninst.SetValue("UninstallString", uninstCmd);
                        }
                    }
                }
                catch { }

                worker.ReportProgress(90, "Launching Finder...");

                // 6. Launch
                if (chkLaunch.Checked)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(targetExe);
                        psi.WorkingDirectory = targetDir;
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                    }
                    catch { }
                }

                worker.ReportProgress(100, "Installation complete!");
            };

            worker.ProgressChanged += (s, args) =>
            {
                progressBar.Value = args.ProgressPercentage;
                lblStatus.Text = args.UserState as string ?? "";
            };

            worker.RunWorkerCompleted += (s, args) =>
            {
                if (args.Error != null)
                {
                    lblStatus.ForeColor = ColorTranslator.FromHtml("#EF4444");
                    lblStatus.Text = "Error: " + args.Error.Message;
                    btnInstall.Enabled = true;
                    btnInstall.Text = "Retry";
                }
                else
                {
                    lblStatus.ForeColor = ColorTranslator.FromHtml("#10B981");
                    lblStatus.Text = "✔ Installed successfully! Press Alt + Space to search.";
                    btnInstall.Text = "Finished";
                    btnInstall.BackColor = ColorTranslator.FromHtml("#059669");
                    btnInstall.Enabled = true;
                }
            };

            worker.RunWorkerAsync();
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workDir, string iconPath = null)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    object shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                        if (sc != null)
                        {
                            Type scType = sc.GetType();
                            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { targetPath });
                            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
                            scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Finder by TN Dev Lab - Ultra-Fast System Search (Alt + Space)" });
                            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                            {
                                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { iconPath + ",0" });
                            }
                            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                        }
                    }
                }
            }
            catch { }
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}
