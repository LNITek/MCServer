using System.Text;

namespace MCServer.Plugins;

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

            cmd.Execute([.. Params]);
        }
        catch (Exception e)
        {
            Commands.ThrowExeption(Line, e);
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
