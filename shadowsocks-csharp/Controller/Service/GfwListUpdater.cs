﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Shadowsocks.Model;
using Shadowsocks.Properties;
using Shadowsocks.Util;

namespace Shadowsocks.Controller
{
    public class GFWListUpdater
    {
        private const string GFWLIST_URL = "https://raw.githubusercontent.com/gfwlist/gfwlist/master/gfwlist.txt";

        private static readonly IEnumerable<char> IgnoredLineBegins = new[] {'!', '['};

        public event EventHandler<ResultEventArgs> UpdateCompleted;

        public event ErrorEventHandler Error;

        private void http_DownloadStringCompleted(object sender, DownloadStringCompletedEventArgs e)
        {
            try
            {
                File.WriteAllText(Utils.GetTempPath("gfwlist.txt"), e.Result, Encoding.UTF8);
                var lines = ParseResult(e.Result);
                if (File.Exists(PACServer.USER_RULE_FILE))
                {
                    var local = File.ReadAllText(PACServer.USER_RULE_FILE, Encoding.UTF8);
                    using (var sr = new StringReader(local))
                    {
                        foreach (var rule in sr.NonWhiteSpaceLines())
                        {
                            if (rule.BeginWithAny(IgnoredLineBegins))
                                continue;
                            lines.Add(rule);
                        }
                    }
                }
                string abpContent;
                if (File.Exists(PACServer.USER_ABP_FILE))
                {
                    abpContent = File.ReadAllText(PACServer.USER_ABP_FILE, Encoding.UTF8);
                }
                else
                {
                    abpContent = Utils.UnGzip(Resources.abp_js);
                }
                abpContent = abpContent.Replace("__RULES__", JsonConvert.SerializeObject(lines, Formatting.Indented));
                if (File.Exists(PACServer.PAC_FILE))
                {
                    var original = File.ReadAllText(PACServer.PAC_FILE, Encoding.UTF8);
                    if (original == abpContent)
                    {
                        UpdateCompleted(this, new ResultEventArgs(false));
                        return;
                    }
                }
                File.WriteAllText(PACServer.PAC_FILE, abpContent, Encoding.UTF8);
                if (UpdateCompleted != null)
                {
                    UpdateCompleted(this, new ResultEventArgs(true));
                }
            }
            catch (Exception ex)
            {
                if (Error != null)
                {
                    Error(this, new ErrorEventArgs(ex));
                }
            }
        }

        public void UpdatePACFromGFWList(Configuration config)
        {
            var http = new WebClient();
            http.Proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
            http.DownloadStringCompleted += http_DownloadStringCompleted;
            http.DownloadStringAsync(new Uri(GFWLIST_URL));
        }

        public static List<string> ParseResult(string response)
        {
            var bytes = Convert.FromBase64String(response);
            var content = Encoding.ASCII.GetString(bytes);
            var valid_lines = new List<string>();
            using (var sr = new StringReader(content))
            {
                foreach (var line in sr.NonWhiteSpaceLines())
                {
                    if (line.BeginWithAny(IgnoredLineBegins))
                        continue;
                    valid_lines.Add(line);
                }
            }
            return valid_lines;
        }

        public class ResultEventArgs : EventArgs
        {
            public bool Success;

            public ResultEventArgs(bool success)
            {
                Success = success;
            }
        }
    }
}