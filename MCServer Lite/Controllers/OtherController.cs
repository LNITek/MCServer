using ExtraFunctions.Extras;
using MCServer.Controllers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Net.Http.Headers;
using MCServer.Models;
using System.IO.Compression;

namespace MCServer
{
    public enum SettingProp
    {
        ServerPath,
        BackupPath,
        ShutdownTime,
        ShutdownMode,
    }

    public static class OtherController
    {
        #region MK 1
        internal static readonly ExLog Loger = new("ErrorLog", AppDomain.CurrentDomain.BaseDirectory, FileType.txt);
        internal static ExSettings ExSettings = new(AppDomain.CurrentDomain.BaseDirectory, FileType.txt);

        public static Dictionary<SettingProp, string> Settings = new()
        {
            {SettingProp.ServerPath, "" },
            {SettingProp.BackupPath, "" },
            {SettingProp.ShutdownTime, "22:00" },
            {SettingProp.ShutdownMode, "1" },
        };

        public static void LoadSettings()
        {
            foreach (var set in ExSettings.GetSettings())
            {
                try
                {
                    Settings[Enum.Parse<SettingProp>(set.Key)] = set.Value;
                }
                catch {  }
                ExSettings.Remove(set.Key);
            }
            ExSettings.AddRange(Settings.Select(x => new KeyValuePair<string, string>(x.Key.ToString(), x.Value)));
            ExSettings.Save();

            if (!File.Exists(Settings[SettingProp.ServerPath] + @"\bedrock_server.exe"))
            {
                ThrowLog("'OC-P01' | Invalid Server File : " + Settings[SettingProp.ServerPath]);
                return;
            }

            if (!Directory.Exists(Settings[SettingProp.BackupPath]))
            {
                ThrowLog("'OC-P02' | Invalid Source Folder : " + Settings[SettingProp.BackupPath]);
                return;
            }
        }

        public static void Updater()
        {
            var OutList = new List<string>();
            var pathToScript = AppDomain.CurrentDomain.BaseDirectory + "\\Updater.ps1";
            var scriptArguments = "-ExecutionPolicy Bypass -File \"" + pathToScript + "\"";
            var processStartInfo = new ProcessStartInfo("powershell.exe", scriptArguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process();
            process.OutputDataReceived += (s, e) =>
            {
                var Out = e.Data ?? "";
                Console.WriteLine(Out);
                OutList.Add(Out);
            };
            process.StartInfo = processStartInfo;
            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
            if(OutList.Where(x => x.Contains("ERROR")).Any())
            {
                ThrowLog("OC-U01 | " + string.Join(", ", OutList.Where(x => x.Contains("ERROR"))));
                throw new Exception(string.Join(", ", OutList.Where(x => x.Contains("ERROR"))));
            }

            var Reader = new StreamReader(Settings[SettingProp.ServerPath] + "\\server.properties");
            var SerProp = new Properties();
            var NewProp = new Properties();

            while (!Reader.EndOfStream)
            {
                var Line = Reader.ReadLine();
                while (string.IsNullOrWhiteSpace(Line))
                    Line = Reader.ReadLine();
                var Prop = Line.Split('=');
                var Com = new List<string>();
                Line = Reader.ReadLine();
                while (!string.IsNullOrWhiteSpace(Line) && Line.StartsWith('#'))
                {
                    Com.Add(Line);
                    Line = Reader.ReadLine();
                }
                SerProp.Add(new Property(Prop[0].Trim(), Prop[1].Trim(), Com));
            }
            Reader.Close();
            var ZIP = ZipFile.OpenRead(Settings[SettingProp.ServerPath] + "\\Backup\\" +
                OutList.First(x => x.Contains("bedrock-server")).Split(": ")[1]);
            Reader = new StreamReader(ZIP.Entries.First(x => x.Name == "server.properties").Open());
            while (!Reader.EndOfStream)
            {
                var Line = Reader.ReadLine();
                while (string.IsNullOrWhiteSpace(Line))
                    Line = Reader.ReadLine();                
                var Prop = Line.Split('=');
                var Com = new List<string>();
                Line = Reader.ReadLine();
                while (!string.IsNullOrWhiteSpace(Line) && Line.StartsWith('#'))
                {
                    Com.Add(Line);
                    Line = Reader.ReadLine();
                }
                NewProp.Add(new Property(Prop[0].Trim(), Prop[1].Trim(), Com));
            }
            Reader.Close();
            ZIP.Dispose();

            //var UpdateProp = NewProp.Where(x => SerProp.Where(el => x.Name != el.Name).Any());
            var UpdateProp = NewProp.Where(el => !NewProp.Select(x => x.Name).Intersect(SerProp.Select(x => x.Name)).Contains(el.Name));
            var Writer = File.AppendText(Settings[SettingProp.ServerPath] + "\\server.properties");
            foreach(var prop in UpdateProp)
            {
                Writer.WriteLine("");
                Writer.WriteLine($"{prop.Name}={prop.Value}");
                prop.Comments.ForEach(x => Writer.WriteLine(x));
            }
            Writer.Close();
        }

        public static void ThrowLog(string Error)
        {
            Loger.Log(DateTime.Now.ToString("[yyyy/MM/dd HH:mm:ss] ") + Error);
            Console.WriteLine(DateTime.Now.ToString("[yyyy/MM/dd HH:mm:ss:fff ERROR] ") + Error);
        }
        #endregion
        #region MK 0
        [DllImport("User32.dll")]
        static extern int SetForegroundWindow(IntPtr point);

        static public bool ShowWindow()
        {
            Process p = Process.GetProcessesByName("bedrock_server").FirstOrDefault();
            if (p == null) return false;
            IntPtr h = p.MainWindowHandle;
            SetForegroundWindow(h);
            return true;
        }

        public static void Stop(PowerMode Mode)
        {
            //Var
            string Colour = "§6", sMessage = "";
            bool Force = false;
            TimeSpan iSleep = new(0, 10, 0);
            TimeSpan iWait = new(0, 1, 0);
            //Code
            var bWin = ShowWindow();
            switch (Mode)
            {
                case PowerMode.Restart: sMessage = "Reastarting"; break;
                case PowerMode.Shutdown: sMessage = "Shuting Down"; break;
                case PowerMode.PowerOff: sMessage = "Cutting Power"; break;
                case PowerMode.LogOff: sMessage = "Disconecting All Players"; break;

                case PowerMode.ForceRestart: sMessage = "Emergency Reastart"; Force = true; iWait = new(0, 0, 10); break;
                case PowerMode.ForceShutdown: sMessage = "Emergency Shutdown"; Force = true; iWait = new(0, 0, 10); break;
                case PowerMode.ForcePowerOff: sMessage = "Emergency Power Cut"; Force = true; iWait = new(0, 0, 10); break;
                case PowerMode.ForceLogOff: sMessage = "Emergency Disconect"; Force = true; iWait = new(0, 0, 10); break;

                case PowerMode.SoftForceRestart: sMessage = "Fast Reastart"; Force = true; break;
                case PowerMode.SoftForceShutdown: sMessage = "Fast Shutdown"; Force = true; break;
                case PowerMode.SoftForcePowerOff: sMessage = "Fast Power Cut"; Force = true; break;

                default: sMessage = "Emergency Shutdown"; Mode = PowerMode.SoftForceShutdown; Force = true; break;
            };

            if (bWin)
                WriteToConsole();
            else
                Loger.Log("Server Console Not Running.");
            Console.WriteLine("Fin");
            PowerController.Shutdown(Mode);
            Application.Exit();

            void WriteToConsole()
            {
                string sTime = "Minutes";
                int iTime = iSleep.Minutes;
                if (Force) { iSleep = new(0, 0, 10); sTime = "Seconds"; iTime = iSleep.Seconds; };
                Console.WriteLine($"Sending Server Warning : {sMessage}");
                SendKeys.SendWait($"say {Colour} {sMessage} In {iTime} {sTime}...");
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(iSleep);
                Console.WriteLine("Server - " + sMessage);
                SendKeys.SendWait("say §c Turning Off...");
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(iWait);
                SendKeys.SendWait("stop");
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(new TimeSpan(0,1,0));
            }
        }
        #endregion
    }
}