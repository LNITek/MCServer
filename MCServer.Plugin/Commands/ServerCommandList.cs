using System.Text;

namespace MCServer.Plugins;

public class ServerCommandList : List<ServerCommand>
{
    internal void ThrowExeption(string commandLine, Exception ex) => OnCommandException?.Invoke(commandLine, ex);
    public event EventHandler<Exception>? OnCommandException;

    public ServerCommandList() : base()
    { }
    public ServerCommandList(IEnumerable<ServerCommand> Commands) : base(Commands)
    { }

    /// <summary>Only commands with <see cref="ServerCommand.Display"/> set, ordered for UI.</summary>
    public IEnumerable<ServerCommand> Displayable =>
        this.Where(c => c.Display is not null)
            .OrderBy(c => string.IsNullOrEmpty(c.Display!.Group) ? 1 : 0)
            .ThenBy(c => c.Display!.Group)
            .ThenBy(c => c.Display!.Order)
            .ThenBy(c => c.Name);

    /// <summary>
    /// Declared group visuals (<see cref="CommandGroupInfo"/>) for the hierarchy.
    /// Commands reference them through <see cref="CommandDisplayInfo.Group"/>.
    /// </summary>
    public List<CommandGroupInfo> GroupDeclarations { get; } = [];

    public void DeclareGroup(
        string path,
        string icon = "",
        int order = 0,
        Func<bool>? isDisabled = null,
        string? label = null)
    {
        GroupDeclarations.Add(new CommandGroupInfo
        {
            Path = path,
            Icon = icon,
            Order = order,
            IsDisabled = isDisabled,
            Label = label,
        });
    }

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
