using ExtraFunctions.Extras;
using MCServer.Controllers;
using System.Text;

namespace MCServer
{
    internal class MCServer
    {
        public static bool Exit = false;
        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("MINECRAFT BEDROCK SERVER MANAGER, v1.0");
            List<string> Args = args.ToList();

            OtherController.LoadSettings();
            OtherController.Updater();
            Thread BaseThread = new(BaseController.Start) { Name = "BaseThread", };

            BaseThread.Start();
            BaseController.Timer();

            while (!Exit)
            {
                Args = Console.ReadLine().ToLower().Split(" ").ToList();

                switch (Args.First())
                {
                    case "!": BaseController.Stop(PowerMode.SoftForceShutdown); Exit = true; break;
                    case "stop" when Args.Count >= 2:
                        if (!Enum.TryParse(((PowerMode)Convert.ToInt32(Args[1])).ToString(), true, out PowerMode Mode)) Mode = PowerMode.Shutdown;
                        BaseController.Stop(Mode); Exit = true; break;
                    case "stop" when Args.Count < 2: BaseController.Stop(PowerMode.Shutdown); Exit = true; break;
                    case "exit": BaseController.Stop(PowerMode.None); Exit = true; break;
                    case "backup": BackupController.Backup(); break;
                    default: BaseController.WriteLine(Args.First()); break;//Console.WriteLine("Invalid Command : " + command);
                }
            }
        }
        #region MK 1
        #endregion
        #region MK 0
        void ArgsCommand(string[] args)
        {
            //Var
            var command = "N/A";
            //Code
            Array.Resize(ref args, 2);
            if (args.Length <= 0)
                throw new ArgumentException("No Arguments Was Provided");
            command = args[0];

            switch (command)
            {
                case "!": OtherController.Stop(PowerMode.SoftForceShutdown); break;
                case "-Stop" when args.Length >= 2:
                    if (!Enum.TryParse(((PowerMode)Convert.ToInt32(args[1])).ToString(), true, out PowerMode Mode)) Mode = PowerMode.Shutdown;
                    OtherController.Stop(Mode); break;
                case "-Stop" when args.Length < 2: OtherController.Stop(PowerMode.Shutdown); break;
                case "-Backup": BackupController.Backups(); break;
                case "-New": BackupController.NewFile(); break;
                default: throw new Exception("Invalid Command : " + command);
            }
        }
        #endregion
    }
}