using System.Diagnostics;
using System.Text;
using ExtraFunctions.Extras;
using MCServer.Components;
using MCServer.Server;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MCServer.Services;

public static class ProcessService
{
    public static void CreateThread(Action action) =>
        new Thread(new ThreadStart(action)).Start();

    #region Start
    public static void StartServer(this MCBedrockServer server)
    {
        if ((server.ServerThread?.ThreadState ?? System.Threading.ThreadState.Stopped) != System.Threading.ThreadState.Running)
        {
            server.ServerThread = new(() => Start(server)) { Name = "ServerThread", };
            server.ServerThread.Start();
        }
    }

    private static void Start(this MCBedrockServer server)
    {
        server.StartupDate = DateTime.Today;
        if (server.CommandRunning) return;
        if (server.ServerRunning)
        {
            Program.NotifyUser("Start: Server is already running!", Severity.Error);
            return;
        }

        server.CommandRunning = true;
        server.ServerRunning = true;

        server.WriteDisplayLine("Starting Server".ToUpper(), Color.Info);

        server.ServerProcess.StartInfo.UseShellExecute = false;
        server.ServerProcess.StartInfo.RedirectStandardInput = true;
        server.ServerProcess.StartInfo.RedirectStandardOutput = true;
        server.ServerProcess.StartInfo.FileName = Path.GetFullPath(Path.Combine(server.ServerPath, "bedrock_server" + (Program.IsWin ? ".exe" : "")));
        server.ServerProcess.StartInfo.StandardInputEncoding = Encoding.Latin1;
        server.ServerProcess.StartInfo.StandardOutputEncoding = Encoding.Latin1;
        server.ServerProcess.StartInfo.CreateNoWindow = true;
        server.ServerProcess.StartInfo.ErrorDialog = true;

        server.ServerProcess.OutputDataReceived += Write;
        server.ServerProcess.Start();
        server.SetAsChildProcess();
        server.ServerProcess.BeginOutputReadLine();

        server.ServerProcess.WaitForExit();
        server.CommandRunning = true;
        server.ServerProcess.CancelOutputRead();
        server.ServerProcess.OutputDataReceived -= Write;
        server.ServerProcess.Close();
        if (server.OutputList.Any(x => x.Line.Contains("Exiting program") && x.Type == Color.Error /*x.Contains("ERROR")*/))
        {
            /*var Dump = new StreamWriter(Path.Combine(server.ServerPath, "LOGS", $"ERROR {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt"));
            server.OutputList.ForEach((x) => Dump.WriteLine(x));
            Dump.Close();*/
            //OtherController.ThrowLog("BC-S01 | Server Terminated. Internal ERROR");
            server.WriteDisplayLine("Server Stopped With Error.", Color.Error);
        }
        else
            server.WriteDisplayLine("Server Stopped.", Color.Info);

        server.ServerRunning = false;
        server.CommandRunning = false;
        //server.OutputList.Clear();

        void Write(object sender, DataReceivedEventArgs e)
        {
            var Out = e.Data ?? "{NULL}";

            if (Out.EndsWith("Server started."))
            {
                server.WriteLine("say §eWelcome To MCServer!");
                server.CommandRunning = false;
            }

            if (Out.Contains("INFO"))
                server.WriteDisplayLine(Out, Color.Info, false);
            else if (Out.Contains("WARN"))
                server.WriteDisplayLine(Out, Color.Warning, false);
            else if (Out.Contains("ERROR"))
                server.WriteDisplayLine(Out, Color.Error, false);

            else server.WriteDisplayLine(Out, Color.Info);
        };
    }

    public static void RestartServer(this MCBedrockServer server)
    {
        if (!server.ServerRunning)
        {
            Program.NotifyUser("Restart: Server is not running!", Severity.Error);
            return;
        }

        CreateThread(async () => await RestartTask(server));
    }

    internal static async Task RestartTask(MCBedrockServer server)
    {
        server.CommandRunning = true;
        server.CommandQue.WaitOne();

        var res = await Stop(server, "Restarting Server!", new(0, 0, 10));
        if (res) StartServer(server);

        server.CommandQue.Release();
        server.CommandRunning = false;
    }
    #endregion

    #region Stop
    public static void StopServer(this MCBedrockServer server, TimeSpan Delay)
    {
        if (!server.ServerRunning)
        {
            Program.NotifyUser("Stop: Server is not running!", Severity.Error);
            return;
        }

        CreateThread(async () => await StopTask(server, Delay));
    }

    internal static async Task StopTask(MCBedrockServer server, TimeSpan Delay)
    {
        server.CommandRunning = true;
        server.CommandQue.WaitOne();

        var res = await Stop(server, "Stopping Server!", Delay);
        if (!res) Program.NotifyUser("Stop: Server could not be stopped!", Severity.Error);

        server.CommandQue.Release();
        server.CommandRunning = false;
    }

    public static async Task<bool> Stop(this MCBedrockServer server, string Message, TimeSpan Delay)
    {
        TimeSpan iWait = new(0, 1, 0);
        string Colour = "§6";
        string DelayMessage = "";

        if (Delay.Days > 0) DelayMessage += $" {Delay.Days} Days";
        if (Delay.Hours > 0) DelayMessage += $" {Delay.Hours} Hours";
        if (Delay.Minutes > 0) DelayMessage += $" {Delay.Minutes} Minutes";
        if (Delay.Seconds > 0) DelayMessage += $" {Delay.Seconds} Seconds";
        if (Delay.Milliseconds > 0) DelayMessage += $" {Delay.Milliseconds} Milliseconds";
        if (Delay.Microseconds > 0) DelayMessage += $" {Delay.Microseconds} Microseconds";
        if (string.IsNullOrWhiteSpace(DelayMessage))
            DelayMessage = "NOW!";
        else DelayMessage = "In" + DelayMessage + "...";

        server.WriteDisplayLine($"Sending Server Warning: {Message}");
        server.WriteLine($"say {Colour}{Message} {DelayMessage}");
        await Task.Delay(Delay);
        server.WriteLine($"say {Colour}Turning Off...");
        await Task.Delay(iWait);
        server.WriteLine("stop");

        int I = 0;
        do
        {
            await Task.Delay(1000);
            I++;
        } while (server.ServerRunning && I < 120);
        if (server.ServerRunning)
        {
            server.WriteLine($"say §cError Could Not Stop Server...");
            //OtherController.ThrowLog("'BC-S01' | Could Not Stop Process");
            return false;
        }

        return true;
    }
    #endregion
}
