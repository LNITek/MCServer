using MudBlazor;

namespace MCServer.Plugins;

/// <summary>
/// UI metadata for a server command. When null, the command stays
/// console-parseable but hidden from the Console/Overview UI.
/// </summary>
public sealed class CommandDisplayInfo
{
    /// <summary>MudBlazor icon string, e.g. <c>Icons.Material.Rounded.PlayArrow</c>.</summary>
    public string Icon { get; set; } = "";

    /// <summary>
    /// Live predicate evaluated every render. Null means enabled.
    /// Use a lambda (e.g. <c>() => server.CommandRunning</c>) so the
    /// state stays fresh instead of a snapshot taken at setup time.
    /// </summary>
    public Func<bool>? IsDisabled { get; set; }

    /// <summary>
    /// Group path for the hierarchy, e.g. <c>"Server"</c> or <c>"World/Backups"</c>.
    /// Empty means top-level (ungrouped). Group visuals come from
    /// <see cref="ServerCommandList.GroupDeclarations"/>.
    /// </summary>
    public string Group { get; set; } = "";

    public int Order { get; set; }

    /// <summary>Button/menu label. Defaults to the command <see cref="ServerCommand.Name"/>.</summary>
    public string? Label { get; set; }
}

public class ServerCommand
{
    public string Name { get; }
    public string Description { get; }
    public List<ArgumentInfo> Arguments { get; } = [];
    public Action<ServerCommandList.Arguments>? OnExecution;
    internal void Execute(ServerCommandList.Arguments Arguments) => OnExecution?.Invoke(Arguments);

    /// <summary>Null = hidden from UI, still runnable from the console input.</summary>
    public CommandDisplayInfo? Display { get; set; }

    public string DisplayLabel => Display?.Label ?? Name;

    public ServerCommand(string Name, string Description, IEnumerable<ArgumentInfo> Args)
    {
        this.Name = Name;
        this.Description = Description;
        Arguments.AddRange(Args);
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder($"{Name} \t\t| {Description}");

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
