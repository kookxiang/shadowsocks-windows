﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

using Shadowsocks.Controller;
using Shadowsocks.Util;
using Shadowsocks.View;

namespace Shadowsocks {
    static class Program {
        public const string Version = "3.1.1";

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main() {
            Utils.ReleaseMemory(true);
            using (Mutex mutex = new Mutex(false, "Global\\Shadowsocks_" + Application.StartupPath.GetHashCode())) {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (!mutex.WaitOne(0, false)) {
                    Process[] oldProcesses = Process.GetProcessesByName("Shadowsocks");
                    if (oldProcesses.Length > 0) {
                        Process oldProcess = oldProcesses[0];
                    }
                    MessageBox.Show(I18N.GetString("Find Shadowsocks icon in your notify tray.") + "\n" +
                        I18N.GetString("If you want to start multiple Shadowsocks, make a copy in another directory."),
                        I18N.GetString("Shadowsocks is already running."));
                    return;
                }
                Directory.SetCurrentDirectory(Application.StartupPath);
#if DEBUG
                Logging.OpenLogFile();

                // truncate privoxy log file while debugging
                string privoxyLogFilename = Utils.GetTempPath("privoxy.log");
                if (File.Exists(privoxyLogFilename))
                    using (new FileStream(privoxyLogFilename, FileMode.Truncate)) { }
#else
                Logging.OpenLogFile();
#endif
                findFuckingSoft:
                var foundFuckingSoft = false;
                var processes = Process.GetProcesses();
                foreach (var process in processes) {
                    if (process.ProcessName.Contains("360")) {
                        foundFuckingSoft = true;
                    }
                }
                if (foundFuckingSoft) {
                    var result = MessageBox.Show("为了您的人身及水表安全，请先卸载 360 相关软件后再运行.", "温馨提示", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                    if (result == DialogResult.Retry) {
                        goto findFuckingSoft;
                    }
                    return;
                }

                ShadowsocksController controller = new ShadowsocksController();
                MenuViewController viewController = new MenuViewController(controller);
                controller.Start();
                Application.Run();
            }
        }
    }
}
