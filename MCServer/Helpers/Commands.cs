using System.Linq;
using System.Text;
using MCServer.Services;

namespace MCServer.Helpers;

public class ServerCommandList : List<ServerCommand>
{
    internal void ThrowExeption(string commandLine, Exception ex) => OnCommandException?.Invoke(commandLine, ex);
    public event EventHandler<Exception>? OnCommandException;
    
    public  ServerCommandList() : base()
    {}
    public  ServerCommandList(IEnumerable<ServerCommand> Commands) : base(Commands)
    {}

    public override string ToString()
    {
        var sb = new StringBuilder($"--All commands-- Required Arguments contains !");
        foreach (var command in this)
        {
            sb.AppendLine();
            sb.Append(command.ToString());
        }
        return sb.ToString();
    }
    
    public class Arguments : List<ServerCommand.Argument>
    {
        public ServerCommand.Argument? this[string name] 
        {
            get => this.FirstOrDefault(a => a.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        }
    }
}

public class ServerCommand
{
    public string Name { get; }
    public string Description { get; }
    public List<ArgumentInfo> Arguments { get; } = [];
    public Action<ServerCommandList.Arguments>? OnExecution;
    internal void Execute(ServerCommandList.Arguments Arguments) => OnExecution?.Invoke(Arguments);

    public ServerCommand(string Name, string Description, IEnumerable<ArgumentInfo> Args)
    {
        this.Name = Name;
        this.Description = Description;
        Arguments.AddRange(Args);
    }
    
    public override string ToString()
    {
        var sb = new StringBuilder($"{Name} \t\t| {Description}");

        foreach (var arg in Arguments)
        {
            sb.AppendLine();
            sb.Append($"\t<{arg.Name} : {arg.ValueType?.Name ?? "flag"}> \t| {arg.Description}");
        }

        return sb.ToString();
    }

    public class Argument
    {
        public string Name { get; }
        public object Value { get; }

        public Argument(ArgumentInfo @base, object Value)
        {
            this.Name = @base.Name;
            this.Value = Value;
        }
    }

    public class ArgumentInfo
    {
        public string Name { get; }
        public string Description { get; }
        public bool Optional { get; init; } = true;
        public Type? ValueType { get; }
        public Func<string, object?> ValueConverter { get; init; }

        public ArgumentInfo(string Name, string Description, Type? ValueType = null)
        {
            this.Name = Name;
            this.Description = Description;
            this.ValueType = ValueType;
            ValueConverter = (val) =>
            {
                if (ValueType == null) return true;
                return Convert.ChangeType(val, ValueType);
            };
        }

        public override string ToString()
        {
            return $"\t<{Name}{(Optional ? "" : "!")} : {ValueType?.Name ?? "flag"}> \t| {Description}";
        }
    }
}

public static class CommandsHelper
{
    public static void Parse(this ServerCommandList Commands, string Line)
    {
        try
        {
            var parts = ParseLine(Line);
            var cmd = Commands.FirstOrDefault(x => x.Name.Equals(parts[0], StringComparison.CurrentCultureIgnoreCase)) ?? 
                      throw new Exception($"C-01- Command '{parts[0]}' does not exist.");

            List<ServerCommand.Argument> Params = [];
            var provided = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

            for (int partIdx = 1; partIdx < parts.Count; partIdx++)
            {
                var token = parts[partIdx];
                var argInfo = cmd.Arguments.FirstOrDefault(a => a.Name.Equals(token, StringComparison.CurrentCultureIgnoreCase));

                if (argInfo == null)
                    throw new Exception($"C-04- Unknown argument: '{token}'.");

                if (!provided.Add(argInfo.Name))
                    throw new Exception($"C-05- Duplicate argument: '{argInfo.Name}'.");

                if (argInfo.ValueType == null)
                {
                    Params.Add(new ServerCommand.Argument(argInfo, true));
                }
                else
                {
                    if (++partIdx >= parts.Count)
                        throw new Exception($"C-03- Arguments Mismatch: Missing value for argument '{argInfo.Name}'.");

                    try
                    {
                        var value = argInfo.ValueConverter(parts[partIdx]);
                        Params.Add(new ServerCommand.Argument(argInfo, value));
                    }
                    catch
                    {
                        throw new Exception($"C-02- Invalid Argument: Could not parse argument {argInfo.Name} for type {argInfo.ValueType.Name} from value '{parts[partIdx]}'.");
                    }
                }
            }

            foreach (var argInfo in cmd.Arguments)
            {
                if (argInfo.ValueType != null && !provided.Contains(argInfo.Name) && !argInfo.Optional)
                    throw new Exception($"C-03- Arguments Mismatch: Missing argument '{argInfo.Name}'.");
            }

            cmd.Execute([..Params]);
        }
        catch (Exception e)
        {
            Commands.ThrowExeption(Line,e);
            return;
        }
    }
    
    private static List<string> ParseLine(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                i++;
                while (i < line.Length && line[i] != '"')
                    current.Append(line[i++]);
            }
            else if (line[i] == ' ' || line[i] == '\t')
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(line[i]);
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }

    public static ServerCommand.Argument ParseArgument(this ServerCommand command, string Name, object Value)
    {
        var arg = command.Arguments.FirstOrDefault(x => x.Name.Equals(Name, StringComparison.CurrentCultureIgnoreCase));
        return new(arg, Value);
    }
}
   
/*public class Command
{
    public static  void Exicute(IEnumerable<Command> Commands, string Line)
    {
        
    }

    //------------------------------------------------------------------------------------//

    public string Name { get; }
    public string Description { get; set; } = "";
    public List<CommandArg> Args { get; } = [];
    public Action<IEnumerable<(CommandArg Args, object Value)>>? Action { get; set; }

    public Command(string Name, IEnumerable<CommandArg> Args, Action<IEnumerable<(CommandArg Args, object Value)>>? Action = null)
    {
        this.Name = Name;
        this.Args.AddRange(Args);
        this.Action = Action;
    }

    public Command(string Name, Action<IEnumerable<(CommandArg Args, object Value)>>? Action = null)
    {
        this.Name = Name;
        this.Action = Action;
    }

    public override string ToString() => Name.ToLower();
}*/

/*public class BackupCommand : ICommand
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
}*/

// public class ExitCommand : ICommand
// {
//     public string Name { get; }
//     public TimeSpan Delay { get; }
//     public Action<TimeSpan> Action { get; set; }
//
//     public ExitCommand(string Name, TimeSpan Delay)
//     {
//         this.Name = Name;
//         this.Delay = Delay;
//     }
//
//     public void Execute()
//     {
//         Action(Delay);
//     }
//
//     public override string ToString() => Name.ToLower();
// }

// public class PowerCommand : ICommand
// {
//     public string Name { get; }
//     public TimeSpan Delay { get; }
//     public PowerService.PowerMode Mode { get; }
//     public Action<TimeSpan, PowerService.PowerMode> Action { get; set; }
//
//     public PowerCommand(string Name, PowerService.PowerMode Mode, TimeSpan Delay)
//     {
//         this.Name = Name;
//         this.Delay = Delay;
//         this.Mode = Mode;
//     }
//
//     public PowerCommand(string Name, int Mode, TimeSpan Delay)
//     {
//         this.Name = Name;
//         this.Delay = Delay;
//         this.Mode = (PowerService.PowerMode)Mode;
//     }
//
//     public void Execute()
//     {
//         Action(Delay, Mode);
//     }
//
//     public override string ToString() => Name.ToLower();
// }