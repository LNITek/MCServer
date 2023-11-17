using ExtraFunctions.Extras;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCServer.Controllers
{
    public class BaseController
    {
        #region MK 1
        public static bool Exited = true;
        public static Process ServerProcess = new();
        public static List<string> OutputList = new();
        public static StreamWriter StreamInput;

        public static void Start()
        {
            Console.WriteLine("Starting Server".ToUpper());

            ServerProcess.StartInfo.UseShellExecute = false;
            ServerProcess.StartInfo.RedirectStandardInput = true;
            ServerProcess.StartInfo.RedirectStandardOutput = true;
            ServerProcess.StartInfo.FileName = "Bedrock Server\\bedrock_server.exe";
            ServerProcess.StartInfo.StandardInputEncoding = Encoding.UTF8;
            ServerProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;

            ServerProcess.OutputDataReceived += (s, e) =>
            {
                var Out = e.Data ?? "{NULL}";
                if(Out.Contains("ERROR"))
                    Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(Out);
                OutputList.Add(Out);
                Console.ResetColor();
            };
            ServerProcess.Start();
            ServerProcess.BeginOutputReadLine();
            Exited = false;

            ServerProcess.WaitForExit();
            ServerProcess.Close();
            Exited = true;
            if (OutputList.Any(x => x.Contains("Exiting program")))
            {
                var Dump = new StreamWriter("ERROR_DUMP.txt");
                OutputList.ForEach(x => Dump.WriteLine(x));
                Dump.Close();
                OtherController.ThrowLog("BC-S01 | Server Terminated. Internal ERROR");
                Environment.Exit(-1);
            }
        }

        public static void Stop(PowerMode Mode)
        {
            BackupController.Backup();
            Thread.Sleep(10000);
            //Var
            int I = 0;
            string Colour = "§6", sMessage;
            bool Force = false;
            TimeSpan iSleep = new(0, 10, 0);
            TimeSpan iWait = new(0, 1, 0);
            //Code
            switch (Mode)
            {
                case PowerMode.None: sMessage = "Disconecting All Players"; Force = true; iWait = new(0, 0, 10); break;

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

            string sTime = "Minutes";
            int iTime = iSleep.Minutes;
            if (Force) { iSleep = new(0, 0, 10); sTime = "Seconds"; iTime = iSleep.Seconds; };
            Console.WriteLine($"Sending Server Warning : {sMessage}");
            WriteLine($"say {Colour} {sMessage} In {iTime} {sTime}...");
            Thread.Sleep(iSleep);
            WriteLine($"say {Colour} Turning Off...");
            Thread.Sleep(iWait);
            WriteLine("stop");
            do {
                Thread.Sleep(1000);
                I++;
            } while (!Exited && I < 120);
            if(!Exited)
            {
                WriteLine($"say §c Error Could Not Shutdown Server...");
                OtherController.ThrowLog("'BC-S01' | Could Not Stop Process");
                return;
            }
            PowerController.Shutdown(Mode);
            Environment.Exit(0);
        }

        public static void Timer()
        {
            var Time = OtherController.Settings[SettingProp.ShutdownTime].Split(':');
            MyScheduler.IntervalInDays(int.Parse(Time[0]), int.Parse(Time[1]), 1,
            () => Stop((PowerMode)int.Parse(OtherController.Settings[SettingProp.ShutdownMode])));
        }

        public static void WriteLine(string Line) => ServerProcess.StandardInput.WriteLine(Line);
        #endregion
    }
}
