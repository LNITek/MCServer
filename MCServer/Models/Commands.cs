using System.Linq;
using MCServer.Views;

namespace MCServer.Models;

public interface ICommand
{
    string Name { get; }

    void Execute();
}

public class CommandArg
{
    public string Name { get; }
    public string Description { get; set; } = "";
    public Type ValueType { get; }

    public CommandArg(string Name, Type ValueType)
    {
        this.Name = Name;
        this.ValueType = ValueType;
    }
}

public class Command
{
    public static List<Command> Commands { get; set; } = [
        new("!"){ Description = "Stops the server, Exits the app and Power off (Shutdown) in 10 Seconds" },
        new("Backup"){ Description = "Backups The Active World While The Server Is Running." },
        new("Start"){ Description = "Starts The Server If It's Not Running Yet." },
        new("Restart") { Description = "Restarts The Server." },
        new("Stop", [
            new(nameof(StopCommand.Delay), typeof(int)) { Description = "The Delay Before Stoping In Seconds." }
            ]) { Description = "Stops The Server." },
        new("Exit", [
            new(nameof(ExitCommand.Delay), typeof(int)) { Description = "The Delay Before Stoping In Seconds." }
            ]) { Description = "Stops The Server And Exits The App." },
        new("Power", [
            new(nameof(PowerCommand.Delay), typeof(int)) { Description = "The Delay Before Stoping In Seconds." },
            new(nameof(PowerCommand.Mode), typeof(int)) { Description = "The Shutdown Mode To Use." }
            ]) { Description = "Stops The Server And Power On/Off OS." },
        ];

    public static (Command CMD, List<object> Params) Parse(string Line)
    {
        var Args = Line.Split(' ').Select(x => x.Trim()).ToList();
        var cmd = Commands.Find(x => x.Name.Equals(Args[0], StringComparison.CurrentCultureIgnoreCase)) ?? 
            throw new Exception($"C01- Command Does Not Exist: Could not find a command of '{Args[0]}'.");

        var I = 0;
        List<object> Params = [];
        foreach (var item in cmd.Args)
        {
            I++;
            try
            {
                Params.Add(Convert.ChangeType(Args[I], item.ValueType));
            }
            catch
            {
                throw new Exception($"C02- Invalid Argument: Could Not Parse Argumen {item.Name} for type {item.ValueType.Name} from value '{Args[I]}'.");
            }
        }

        if (cmd.Args.Count > Params.Count)
            throw new Exception($"C03- Arguments Mismach: Missing arguments!");

        return (cmd, Params);
    }

    //------------------------------------------------------------------------------------//

    public string Name { get; }
    public string Description { get; set; } = "";
    public Type MachingCommand { get; }
    public List<CommandArg> Args { get; } = [];

    public Command(string Name, IEnumerable<CommandArg> Args)
    {
        this.Name = Name;
        this.Args.AddRange(Args);
    }

    public Command(string Name)
    {
        this.Name = Name;
    }

    public override string ToString() => Name.ToLower();
}

public class BackupCommand : ICommand
{
    public string Name { get; }
    public Action Action { get; set; }

    public BackupCommand(string Name)
    {
        this.Name = Name;
    }

    public void Execute()
    {
        Action();
    }

    public override string ToString() => Name.ToLower();
}
public class StartCommand : ICommand
{
    public string Name { get; }
    public Action Action { get; set; }

    public StartCommand(string Name)
    {
        this.Name = Name;
    }

    public void Execute()
    {
        Action();
    }

    public override string ToString() => Name.ToLower();
}
public class RestartCommand : ICommand
{
    public string Name { get; }
    public Action Action { get; set; }

    public RestartCommand(string Name)
    {
        this.Name = Name;
    }

    public void Execute()
    {
        Action();
    }

    public override string ToString() => Name.ToLower();
}
public class StopCommand : ICommand
{
    public string Name { get; }
    public TimeSpan Delay { get; }
    public Action<TimeSpan> Action { get; set; }

    public StopCommand(string Name, TimeSpan Delay)
    {
        this.Name = Name;
        this.Delay = Delay;
    }

    public void Execute()
    {
        Action(Delay);
    }

    public override string ToString() => Name.ToLower();
}
public class ExitCommand : ICommand
{
    public string Name { get; }
    public TimeSpan Delay { get; }
    public Action<TimeSpan> Action { get; set; }

    public ExitCommand(string Name, TimeSpan Delay)
    {
        this.Name = Name;
        this.Delay = Delay;
    }

    public void Execute()
    {
        Action(Delay);
    }

    public override string ToString() => Name.ToLower();
}
public class PowerCommand : ICommand
{
    public string Name { get; }
    public TimeSpan Delay { get; }
    public PowerMode Mode { get; }
    public Action<TimeSpan, PowerMode> Action { get; set; }

    public PowerCommand(string Name, PowerMode Mode, TimeSpan Delay)
    {
        this.Name = Name;
        this.Delay = Delay;
        this.Mode = Mode;
    }

    public PowerCommand(string Name, int Mode, TimeSpan Delay)
    {
        this.Name = Name;
        this.Delay = Delay;
        this.Mode = (PowerMode)Mode;
    }

    public void Execute()
    {
        Action(Delay, Mode);
    }

    public override string ToString() => Name.ToLower();
}