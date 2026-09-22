namespace MCServer.Plugins;

/// <summary>
/// Declared metadata for one command group (your <c>CMDGroup</c> idea).
/// Commands join the group through <see cref="CommandDisplayInfo.Group"/>.
/// </summary>
public sealed class CommandGroupInfo
{
    /// <summary>Group path, e.g. <c>"Server"</c> or <c>"World/Backups"</c>.</summary>
    public string Path { get; set; } = "";

    /// <summary>MudBlazor icon string for the group header/menu.</summary>
    public string Icon { get; set; } = "";

    /// <summary>Header/menu label. Defaults to the last path segment.</summary>
    public string? Label { get; set; }

    public int Order { get; set; }

    /// <summary>
    /// Live predicate evaluated every render. When true the whole group
    /// (header and commands) renders disabled. Null means enabled.
    /// </summary>
    public Func<bool>? IsDisabled { get; set; }
}

/// <summary>
/// One built node of the displayable-command tree: a group with its own
/// icon/label/disabled state, child <see cref="Groups"/> and leaf <see cref="Commands"/>.
/// Rebuilt on every render so all states stay live.
/// </summary>
public sealed class CommandGroupNode
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Order { get; set; }
    public bool Disabled { get; set; }
    public List<CommandGroupNode> Groups { get; } = [];
    public List<ServerCommand> Commands { get; } = [];
}

public static class CommandHierarchy
{
    public static CommandGroupNode Build(
        IEnumerable<ServerCommand> commands,
        IEnumerable<CommandGroupInfo>? groups = null)
    {
        var declarations = (groups ?? [])
            .Where(g => !string.IsNullOrWhiteSpace(g.Path))
            .GroupBy(g => g.Path.Trim('/'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var root = new CommandGroupNode { Name = "", Label = "" };
        var paths = new Dictionary<CommandGroupNode, string> { [root] = "" };

        foreach (var command in commands.Where(c => c.Display is not null))
        {
            var node = root;
            var segments = (command.Display!.Group ?? "")
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var child = node.Groups.FirstOrDefault(g =>
                    g.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (child is null)
                {
                    child = new CommandGroupNode { Name = segment, Label = segment };
                    node.Groups.Add(child);
                    paths[child] = (paths[node] + "/" + segment).Trim('/');
                }
                node = child;
            }
            node.Commands.Add(command);
        }

        ApplyMetadata(root, paths, declarations);
        Sort(root);
        return root;
    }

    private static void ApplyMetadata(
        CommandGroupNode node,
        Dictionary<CommandGroupNode, string> paths,
        Dictionary<string, CommandGroupInfo> declarations)
    {
        if (declarations.TryGetValue(paths[node], out var info))
        {
            node.Label = info.Label ?? node.Name;
            node.Icon = info.Icon ?? "";
            node.Order = info.Order;
            node.Disabled = info.IsDisabled?.Invoke() ?? false;
        }

        foreach (var child in node.Groups)
            ApplyMetadata(child, paths, declarations);
    }

    private static void Sort(CommandGroupNode node)
    {
        node.Commands.Sort((a, b) =>
        {
            var order = a.Display!.Order.CompareTo(b.Display!.Order);
            return order != 0 ? order : string.Compare(a.DisplayLabel, b.DisplayLabel, StringComparison.OrdinalIgnoreCase);
        });
        node.Groups.Sort((a, b) =>
        {
            var order = a.Order.CompareTo(b.Order);
            return order != 0 ? order : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var child in node.Groups)
            Sort(child);
    }
}
