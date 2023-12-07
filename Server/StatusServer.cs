﻿using NetDefines;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Server
{
    public static class StatusServer
    {
        private static bool _running = false;
        private static bool _exit = false;
        private static readonly object _sync = new object();
        private static string gdsIP;
        private static string gdsPort;
        private static int gdsWait;

        public static void Init()
        {
            string[] keys = { "gds_ip", "gds_port", "gds_wait" };
            foreach (string key in keys)
                if (!Config.settings.ContainsKey(key))
                {
                    Log.Print("STATUSSRV Error : cant find setting " + key + "!");
                    Log.Print("STATUSSRV main loop stopped");
                    return;
                }
            gdsIP = Config.settings[keys[0]];
            gdsPort = Config.settings[keys[1]];
            gdsWait = int.Parse(Config.settings[keys[2]]);
            if (gdsWait < 10)
                gdsWait = 10;
        }

        public static void Start()
        {
            if (isRunning())
                return;            
            lock (_sync)
            {
                _exit = false;
                _running = true;
            }
            new Thread(tMain).Start();
        }

        public static bool isRunning()
        {
            lock (_sync)
            {
                return _running;
            }
        }

        public static void tMain(object obj)
        {
            Log.Print("STATUSSRV main loop running...");
            while (true)
            {
                try
                {
                    lock (_sync)
                    {
                        if (_exit)
                        {
                            Log.Print("STATUSSRV main loop is exiting...");
                            break;
                        }
                        try
                        {
                            SendStatus();
                        }
                        catch
                        {
                            Log.Print("STATUSSRV failed to send status!");
                        }
                    }
                    for (int i = 0; i < 100; i++)
                    {
                        Thread.Sleep(gdsWait * 10);
                        lock (_sync)
                        {
                            if (_exit)
                                break;
                        }
                    }
                }
                catch
                {
                    _exit = true;
                    break;
                }
            }
            Log.Print("STATUSSRV main loop stopped");
            _running = false;
        }

        private static void SendStatus()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"timestamp\":\"" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() + "\",");
            sb.Append("\"server\":{");
            sb.Append("\"name\":\"" + Config.settings["name"] + "\",");
            sb.Append("\"timeout\":\"" + Config.settings["timeout"] + "\",");
            sb.Append("\"port_tcp\":\"" + Backend.port + "\",");
            sb.Append("\"port_udp\":\"" + MainServer.port + "\",");
            sb.Append("\"map_name\":\"" + Backend.currentMap + "\",");
            sb.Append("\"backend_mode\":\"" + Backend.mode.ToString() + "\",");
            sb.Append("\"backend_mode_state\":\"" + Backend.modeState.ToString() + "\"");
            sb.Append("},");
            sb.Append("\"clients\":{");
            for (int i = 0; i < Backend.clientList.Count; i++)
            {
                ClientInfo info = Backend.clientList[i];
                if (info.profile == null)
                    break;
                sb.Append("\"" + info.ID + "\":{");
                sb.Append("\"name\":\"" + info.profile.name + "\"");
                sb.Append("}");
                if (i < Backend.clientList.Count - 1)
                    sb.Append(",");
            }
            sb.Append("}");
            sb.Append("}");
            string reply = MakeRESTRequest("POST /server_status", sb.ToString());
            XElement json = NetHelper.StringToJSON(reply);
            XElement countElement = json.XPathSelectElement("playerUpdateCount");
            uint count = uint.Parse(countElement.Value);
            if (Config.playerProfileUpdateCounter != count)
            {
                Config.playerProfileUpdateCounter = count;
                Log.Print("STATUSSRV detected player changes, reloading profiles");
                Config.ReloadPlayerProfiles();
                Log.Print("STATUSSRV loaded " + Config.profiles.Count + " profiles");
            }
        }

        public static void Stop()
        {
            lock (_sync)
            {
                _exit = true;
            }
            while (true)
            {
                lock (_sync)
                {
                    if (!_running)
                        break;
                }
                Thread.Sleep(10);
                Application.DoEvents();
            }
        }

        public static string MakeRESTRequest(string url, string contentData)
        {
            byte[] content = Encoding.ASCII.GetBytes(contentData);
            byte[] signature = NetHelper.MakeSignature(content, Config.rsaParams);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(url + " HTTP/1.1");
            sb.AppendLine("Signature: " + NetHelper.MakeHexString(signature));
            sb.AppendLine("Public-Key: " + Config.pubKey);
            sb.AppendLine();
            sb.Append(contentData);
            TcpClient client = new TcpClient(gdsIP, Convert.ToInt32(gdsPort));
            NetworkStream ns = client.GetStream();
            byte[] data = Encoding.ASCII.GetBytes(sb.ToString());
            ns.Write(data, 0, data.Length);
            while (!ns.DataAvailable)
                Thread.Sleep(100);
            sb = new StringBuilder();
            int b;
            while ((b = ns.ReadByte()) != -1)
                sb.Append((char)b);
            client.Close();
            StringReader sr = new StringReader(sb.ToString());
            while (sr.ReadLine() != "") ;
            return sr.ReadToEnd();
        }
    }
}
