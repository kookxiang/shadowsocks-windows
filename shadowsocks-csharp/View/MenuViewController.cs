using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using Shadowsocks.Controller;
using Shadowsocks.Properties;

namespace Shadowsocks.View
{
    public class MenuViewController
    {
        private readonly NotifyIcon _notifyIcon;
        // yes this is just a menu view controller
        // when config form is closed, it moves away from RAM
        // and it should just do anything related to the config form

        private readonly ShadowsocksController controller;
        private readonly List<LogForm> logForms = new List<LogForm>();
        private bool _isFirstRun;
        private string _urlToOpen;
        private MenuItem autoCheckUpdatesToggleItem;
        private MenuItem AutoStartupItem;
        private MenuItem ConfigItem;
        private ContextMenu contextMenu1;
        private MenuItem editGFWUserRuleItem;
        private MenuItem editLocalPACItem;
        private MenuItem editOnlinePACItem;
        private MenuItem enableItem;
        private MenuItem globalModeItem;
        private MenuItem localPACItem;
        private bool logFormsVisible;
        private MenuItem modeItem;
        private MenuItem onlinePACItem;
        private MenuItem PACModeItem;
        private MenuItem SeperatorItem;
        private MenuItem ServersItem;
        private MenuItem ShareOverLANItem;
        private MenuItem updateFromGFWListItem;

        public MenuViewController(ShadowsocksController controller)
        {
            this.controller = controller;

            LoadMenu();

            controller.EnableStatusChanged += controller_EnableStatusChanged;
            controller.ConfigChanged += controller_ConfigChanged;
            controller.PACFileReadyToOpen += controller_FileReadyToOpen;
            controller.UserRuleFileReadyToOpen += controller_FileReadyToOpen;
            controller.ShareOverLANStatusChanged += controller_ShareOverLANStatusChanged;
            controller.EnableGlobalChanged += controller_EnableGlobalChanged;
            controller.Errored += controller_Errored;
            controller.UpdatePACFromGFWListCompleted += controller_UpdatePACFromGFWListCompleted;
            controller.UpdatePACFromGFWListError += controller_UpdatePACFromGFWListError;

            _notifyIcon = new NotifyIcon();
            UpdateTrayIcon();
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenu = contextMenu1;
            _notifyIcon.MouseClick += notifyIcon1_Click;

            LoadCurrentConfiguration();

            var config = controller.GetConfigurationCopy();

            if (config.isDefault)
            {
                _isFirstRun = true;
            }
        }

        private void controller_Errored(object sender, ErrorEventArgs e)
        {
            MessageBox.Show(e.GetException().ToString(),
                string.Format(I18N.GetString("Shadowsocks Error: {0}"), e.GetException().Message));
        }

        private void UpdateTrayIcon()
        {
            int dpi;
            var graphics = Graphics.FromHwnd(IntPtr.Zero);
            dpi = (int) graphics.DpiX;
            graphics.Dispose();
            Bitmap icon = null;
            if (dpi < 97)
            {
                // dpi = 96;
                icon = Resources.ss16;
            }
            else if (dpi < 121)
            {
                // dpi = 120;
                icon = Resources.ss20;
            }
            else
            {
                icon = Resources.ss24;
            }
            var config = controller.GetConfigurationCopy();
            var enabled = config.enabled;
            var global = config.global;
            icon = getTrayIconByState(icon, enabled, global);
            _notifyIcon.Icon = Icon.FromHandle(icon.GetHicon());

            string serverInfo = null;
            if (controller.GetCurrentStrategy() != null)
            {
                serverInfo = controller.GetCurrentStrategy().Name;
            }
            else
            {
                serverInfo = config.GetCurrentServer().FriendlyName();
            }
            // we want to show more details but notify icon title is limited to 63 characters
            var text = I18N.GetString("Shadowsocks") + " " + Program.Version + "\n" +
                       (enabled
                           ? I18N.GetString("System Proxy On: ") +
                             (global ? I18N.GetString("Global") : I18N.GetString("PAC"))
                           : string.Format(I18N.GetString("Running: Port {0}"), config.localPort))
                // this feedback is very important because they need to know Shadowsocks is running
                       + "\n" + serverInfo;
            _notifyIcon.Text = text.Substring(0, Math.Min(63, text.Length));
        }

        private Bitmap getTrayIconByState(Bitmap originIcon, bool enabled, bool global)
        {
            var iconCopy = new Bitmap(originIcon);
            for (var x = 0; x < iconCopy.Width; x++)
            {
                for (var y = 0; y < iconCopy.Height; y++)
                {
                    var color = originIcon.GetPixel(x, y);
                    if (color.A != 0 && color.R > 30)
                    {
                        if (!enabled)
                        {
                            iconCopy.SetPixel(x, y, Color.FromArgb((byte) (color.A/1.25), color.R, color.G, color.B));
                        }
                        else if (global)
                        {
                            var flyBlue = Color.FromArgb(25, 125, 191);
                            // Muliply with flyBlue
                            var red = color.R*flyBlue.R/255;
                            var green = color.G*flyBlue.G/255;
                            var blue = color.B*flyBlue.B/255;
                            iconCopy.SetPixel(x, y, Color.FromArgb(color.A, red, green, blue));
                        }
                    }
                    else
                    {
                        iconCopy.SetPixel(x, y, Color.FromArgb(color.A, color.R, color.G, color.B));
                    }
                }
            }
            return iconCopy;
        }

        private MenuItem CreateMenuItem(string text, EventHandler click)
        {
            return new MenuItem(I18N.GetString(text), click);
        }

        private MenuItem CreateMenuGroup(string text, MenuItem[] items)
        {
            return new MenuItem(I18N.GetString(text), items);
        }

        private void LoadMenu()
        {
            contextMenu1 = new ContextMenu(new[]
            {
                enableItem = CreateMenuItem("Enable System Proxy", EnableItem_Click),
                modeItem = CreateMenuGroup("Mode", new[]
                {
                    PACModeItem = CreateMenuItem("PAC", PACModeItem_Click),
                    globalModeItem = CreateMenuItem("Global", GlobalModeItem_Click)
                }),
                ServersItem = CreateMenuGroup("Servers", new[]
                {
                    SeperatorItem = new MenuItem("-"),
                    CreateMenuItem("Statistics Config...", StatisticsConfigItem_Click)
                }),
                CreateMenuGroup("PAC ", new[]
                {
                    localPACItem = CreateMenuItem("Local PAC", LocalPACItem_Click),
                    onlinePACItem = CreateMenuItem("Online PAC", OnlinePACItem_Click),
                    new MenuItem("-"),
                    editLocalPACItem = CreateMenuItem("Edit Local PAC File...", EditPACFileItem_Click),
                    updateFromGFWListItem =
                        CreateMenuItem("Update Local PAC from GFWList", UpdatePACFromGFWListItem_Click),
                    editGFWUserRuleItem =
                        CreateMenuItem("Edit User Rule for GFWList...", EditUserRuleFileForGFWListItem_Click),
                    editOnlinePACItem = CreateMenuItem("Edit Online PAC URL...", UpdateOnlinePACURLItem_Click)
                }),
                new MenuItem("-"),
                AutoStartupItem = CreateMenuItem("Start on Boot", AutoStartupItem_Click),
                ShareOverLANItem = CreateMenuItem("Allow Clients from LAN", ShareOverLANItem_Click),
                CreateMenuItem("Show Logs...", ShowLogItem_Click),
                new MenuItem("-"),
                CreateMenuItem("Quit", Quit_Click)
            });
        }

        private void controller_ConfigChanged(object sender, EventArgs e)
        {
            LoadCurrentConfiguration();
            UpdateTrayIcon();
        }

        private void controller_EnableStatusChanged(object sender, EventArgs e)
        {
            enableItem.Checked = controller.GetConfigurationCopy().enabled;
            modeItem.Enabled = enableItem.Checked;
        }

        private void controller_ShareOverLANStatusChanged(object sender, EventArgs e)
        {
            ShareOverLANItem.Checked = controller.GetConfigurationCopy().shareOverLan;
        }

        private void controller_EnableGlobalChanged(object sender, EventArgs e)
        {
            globalModeItem.Checked = controller.GetConfigurationCopy().global;
            PACModeItem.Checked = !globalModeItem.Checked;
        }

        private void controller_FileReadyToOpen(object sender, ShadowsocksController.PathEventArgs e)
        {
            var argument = @"/select, " + e.Path;

            Process.Start("explorer.exe", argument);
        }

        private void ShowBalloonTip(string title, string content, ToolTipIcon icon, int timeout)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = content;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(timeout);
        }

        private void controller_UpdatePACFromGFWListError(object sender, ErrorEventArgs e)
        {
            ShowBalloonTip(I18N.GetString("Failed to update PAC file"), e.GetException().Message, ToolTipIcon.Error,
                5000);
            Logging.LogUsefulException(e.GetException());
        }

        private void controller_UpdatePACFromGFWListCompleted(object sender, GFWListUpdater.ResultEventArgs e)
        {
            var result = e.Success
                ? I18N.GetString("PAC updated")
                : I18N.GetString("No updates found. Please report to GFWList if you have problems with it.");
            ShowBalloonTip(I18N.GetString("Shadowsocks"), result, ToolTipIcon.Info, 1000);
        }

        private void LoadCurrentConfiguration()
        {
            var config = controller.GetConfigurationCopy();
            UpdateServersMenu();
            enableItem.Checked = config.enabled;
            modeItem.Enabled = config.enabled;
            globalModeItem.Checked = config.global;
            PACModeItem.Checked = !config.global;
            ShareOverLANItem.Checked = config.shareOverLan;
            AutoStartupItem.Checked = AutoStartup.Check();
            onlinePACItem.Checked = onlinePACItem.Enabled && config.useOnlinePac;
            localPACItem.Checked = !onlinePACItem.Checked;
            UpdatePACItemsEnabledStatus();
        }

        private void UpdateServersMenu()
        {
            var items = ServersItem.MenuItems;
            while (items[0] != SeperatorItem)
            {
                items.RemoveAt(0);
            }
            var i = 0;
            foreach (var strategy in controller.GetStrategies())
            {
                var item = new MenuItem(strategy.Name);
                item.Tag = strategy.ID;
                item.Click += AStrategyItem_Click;
                items.Add(i, item);
                i++;
            }
            var strategyCount = i;
            var configuration = controller.GetConfigurationCopy();
            foreach (var server in configuration.configs)
            {
                var item = new MenuItem(server.FriendlyName());
                item.Tag = i - strategyCount;
                item.Click += AServerItem_Click;
                items.Add(i, item);
                i++;
            }

            foreach (MenuItem item in items)
            {
                if (item.Tag != null &&
                    (item.Tag.ToString() == configuration.index.ToString() ||
                     item.Tag.ToString() == configuration.strategy))
                {
                    item.Checked = true;
                }
            }
        }

        private void ShowLogForms()
        {
            if (logForms.Count == 0)
            {
                var f = new LogForm(controller, Logging.LogFilePath);
                f.Show();
                f.FormClosed += logForm_FormClosed;

                logForms.Add(f);
                logFormsVisible = true;
            }
            else
            {
                logFormsVisible = !logFormsVisible;
                foreach (var f in logForms)
                {
                    f.Visible = logFormsVisible;
                }
            }
        }

        private void logForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            logForms.Remove((LogForm) sender);
        }

        private void Quit_Click(object sender, EventArgs e)
        {
            controller.Stop();
            _notifyIcon.Visible = false;
            Application.Exit();
        }

        private void ShowFirstTimeBalloon()
        {
            _notifyIcon.BalloonTipTitle = I18N.GetString("Shadowsocks is here");
            _notifyIcon.BalloonTipText = I18N.GetString("You can turn on/off Shadowsocks in the context menu");
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(0);
        }

        private void notifyIcon1_Click(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // TODO: show something interesting
            }
            else if (e.Button == MouseButtons.Middle)
            {
                ShowLogForms();
            }
        }

        private void EnableItem_Click(object sender, EventArgs e)
        {
            controller.ToggleEnable(!enableItem.Checked);
        }

        private void GlobalModeItem_Click(object sender, EventArgs e)
        {
            controller.ToggleGlobal(true);
        }

        private void PACModeItem_Click(object sender, EventArgs e)
        {
            controller.ToggleGlobal(false);
        }

        private void ShareOverLANItem_Click(object sender, EventArgs e)
        {
            ShareOverLANItem.Checked = !ShareOverLANItem.Checked;
            controller.ToggleShareOverLAN(ShareOverLANItem.Checked);
        }

        private void EditPACFileItem_Click(object sender, EventArgs e)
        {
            controller.TouchPACFile();
        }

        private void UpdatePACFromGFWListItem_Click(object sender, EventArgs e)
        {
            controller.UpdatePACFromGFWList();
        }

        private void EditUserRuleFileForGFWListItem_Click(object sender, EventArgs e)
        {
            controller.TouchUserRuleFile();
        }

        private void AServerItem_Click(object sender, EventArgs e)
        {
            var item = (MenuItem) sender;
            controller.SelectServerIndex((int) item.Tag);
        }

        private void AStrategyItem_Click(object sender, EventArgs e)
        {
            var item = (MenuItem) sender;
            controller.SelectStrategy((string) item.Tag);
        }

        private void ShowLogItem_Click(object sender, EventArgs e)
        {
            var f = new LogForm(controller, Logging.LogFilePath);
            f.Show();
            f.FormClosed += logForm_FormClosed;

            logForms.Add(f);
        }

        private void StatisticsConfigItem_Click(object sender, EventArgs e)
        {
            var form = new StatisticsStrategyConfigurationForm(controller);
            form.Show();
        }

        private void AutoStartupItem_Click(object sender, EventArgs e)
        {
            AutoStartupItem.Checked = !AutoStartupItem.Checked;
            if (!AutoStartup.Set(AutoStartupItem.Checked))
            {
                MessageBox.Show(I18N.GetString("Failed to update registry"));
            }
        }

        private void LocalPACItem_Click(object sender, EventArgs e)
        {
            if (!localPACItem.Checked)
            {
                localPACItem.Checked = true;
                onlinePACItem.Checked = false;
                controller.UseOnlinePAC(false);
                UpdatePACItemsEnabledStatus();
            }
        }

        private void OnlinePACItem_Click(object sender, EventArgs e)
        {
            if (!onlinePACItem.Checked)
            {
                if (controller.GetConfigurationCopy().pacUrl.IsNullOrEmpty())
                {
                    UpdateOnlinePACURLItem_Click(sender, e);
                }
                if (!controller.GetConfigurationCopy().pacUrl.IsNullOrEmpty())
                {
                    localPACItem.Checked = false;
                    onlinePACItem.Checked = true;
                    controller.UseOnlinePAC(true);
                }
                UpdatePACItemsEnabledStatus();
            }
        }

        private void UpdateOnlinePACURLItem_Click(object sender, EventArgs e)
        {
            var origPacUrl = controller.GetConfigurationCopy().pacUrl;
            var pacUrl = Interaction.InputBox(
                I18N.GetString("Please input PAC Url"),
                I18N.GetString("Edit Online PAC URL"),
                origPacUrl, -1, -1);
            if (!pacUrl.IsNullOrEmpty() && pacUrl != origPacUrl)
            {
                controller.SavePACUrl(pacUrl);
            }
        }

        private void UpdatePACItemsEnabledStatus()
        {
            if (localPACItem.Checked)
            {
                editLocalPACItem.Enabled = true;
                updateFromGFWListItem.Enabled = true;
                editGFWUserRuleItem.Enabled = true;
                editOnlinePACItem.Enabled = false;
            }
            else
            {
                editLocalPACItem.Enabled = false;
                updateFromGFWListItem.Enabled = false;
                editGFWUserRuleItem.Enabled = false;
                editOnlinePACItem.Enabled = true;
            }
        }
    }
}