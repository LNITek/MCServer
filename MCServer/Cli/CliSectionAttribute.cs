namespace MCServer.Cli;

/// <summary>
/// Groups a CLI option into a generated --help section. Add the attribute to
/// newly added CliOptions properties; ungrouped options land in "Other".
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CliSectionAttribute : Attribute
{
    public CliSectionAttribute(string section, int order = 1000)
    {
        Section = section;
        Order = order;
    }

    public string Section { get; }
    public int Order { get; }
}
