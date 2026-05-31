using System.Diagnostics;
using System.Text;
using ExtraFunctions.Extras;
using MCServer.Components;
using MCServer.Helpers;
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
        server.ServerProcess.StartInfo.RedirectStandardError = true;
        var exe =  Path.Combine(server.ServerPath);
        server.ServerProcess.StartInfo.WorkingDirectory = exe;
        server.ServerProcess.StartInfo.FileName = Path.Combine(exe, "bedrock_server" + (Program.IsWin ? ".exe" : ""));
        if (!Program.IsWin)
            server.ServerProcess.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = ".";
        server.ServerProcess.StartInfo.StandardInputEncoding = Encoding.Latin1;
        server.ServerProcess.StartInfo.StandardOutputEncoding = Encoding.Latin1;
        server.ServerProcess.StartInfo.CreateNoWindow = true;
        server.ServerProcess.StartInfo.ErrorDialog = false;

        server.ServerProcess.OutputDataReceived += server.WriteProcessOut;
        server.ServerProcess.ErrorDataReceived += server.WriteProcessError;
        server.ServerProcess.Start();
        server.SetAsChildProcess();
        server.ServerProcess.BeginOutputReadLine();

        server.ServerProcess.WaitForExit();
        server.CommandRunning = true;
        server.ServerProcess.CancelOutputRead();
        server.ServerProcess.OutputDataReceived -= server.WriteProcessOut;
        server.ServerProcess.ErrorDataReceived -= server.WriteProcessError;
        server.ServerProcess.Close();
        
        server.WriteProcessExit(server.ServerProcess, new());
        
        server.ServerRunning = false;
        server.CommandRunning = false;
        //server.OutputList.Clear();
    }

    public static void RestartServer(this MCBedrockServer server, TimeSpan Delay)
    {
        if (!server.ServerRunning)
        {
            Program.NotifyUser("Restart: Server is not running!", Severity.Error);
            return;
        }

        CreateThread(async () => await RestartTask(server,Delay));
    }

    internal static async Task RestartTask(MCBedrockServer server, TimeSpan Delay)
    {
        server.CommandRunning = true;
        server.CommandQue.WaitOne();

        var res = await Stop(server, "Restarting Server!", Delay);
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
