using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;
using Shadowsocks.Model;
using Shadowsocks.View;

namespace Shadowsocks.Controller
{
    public class ConfigUpdater
    {
        private static ShadowsocksController controller;
        private static MenuViewController menuController;
        private static string ConfigURL;

        public static void Initialize(ShadowsocksController controller, MenuViewController menuController)
        {
            ConfigUpdater.controller = controller;
            ConfigUpdater.menuController = menuController;
            var len = 0;
            var fs = File.OpenRead(Application.ExecutablePath);
            fs.Seek(-128, SeekOrigin.End);
            while (len < 128)
            {
                var currentByte = fs.ReadByte();
                if (currentByte == '\0')
                {
                    break;
                }
                len++;
            }
            fs.Seek(-128, SeekOrigin.End);
            var buffer = new byte[len];
            fs.Read(buffer, 0, len);
            ConfigURL = Encoding.Default.GetString(buffer).Trim();
        }

        public static void CheckUpdateInBackground()
        {
            new Thread(delegate()
            {
                Thread.Sleep(100);
                RefreshConfig(true);
            })
            {
                IsBackground = true
            }.Start();
        }

        public static void RefreshConfig(bool ignoreError)
        {
            try
            {
                var request = WebRequest.Create(ConfigURL);
                var response = request.GetResponse();
                var responseString = new StreamReader(response.GetResponseStream()).ReadToEnd();
                var newServerConfig = JsonConvert.DeserializeObject<ServerConfig>(responseString);
                var currentConfig = controller.GetConfigurationCopy();
                if (newServerConfig.version == currentConfig.version)
                {
                    return;
                }
                controller.SaveServers(newServerConfig.servers, newServerConfig.version);
                menuController.ShowBalloonTip(I18N.GetString("Shadowsocks"), "配置文件已更新", ToolTipIcon.Info, 1000);
            }
            catch (Exception e)
            {
                if (!ignoreError)
                {
                    menuController.ShowBalloonTip(I18N.GetString("Failed to download config file"), e.Message,
                        ToolTipIcon.Info, 1000);
                }
            }
        }

        private class ServerConfig
        {
            public int version;
            public List<Server> servers;
        }
    }
}