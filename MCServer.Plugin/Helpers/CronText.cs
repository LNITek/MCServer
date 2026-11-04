using System.Text;

namespace MCServer.Plugins;

/// <summary>
/// Turns a cron expression into a short human sentence for the UI:
/// "0 12 * * *" → "Daily at 12:00", "1 10 5 * *" → "Every 5th day of the month
/// at 10:01". Returns <c>null</c> when the expression is not understood well
/// enough to summarize; callers then keep their generic hint text.
/// </summary>
public static class CronText
{
    private static readonly string[] DayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] MonthNames = ["January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"];

    private enum FieldKind { All, Step, List, Range }

    private readonly record struct Field(FieldKind Kind, int StepSize, int[] Values, int RangeStart, int RangeEnd);

    /// <summary>Human sentence for the expression, or null if undescribable/invalid.</summary>
    public static string? Describe(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) return null;
        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 5 or > 6) return null;

        // (seconds supported by Cronos only complicate summaries; 6-field forms
        // describe the five trailing fields and note the fixed second value.)
        var seconds = 0;
        if (parts.Length == 6 && !int.TryParse(parts[0], out seconds)) return null;

        var minute = Parse(parts[^5], 0, 59);
        var hour = Parse(parts[^4], 0, 23);
        var dom = Parse(parts[^3], 1, 31);
        var month = Parse(parts[^2], 1, 12);
        var dow = Parse(parts[^1], 0, 7);
        if (!minute.HasValue || !hour.HasValue || !dom.HasValue || !month.HasValue || !dow.HasValue) return null;

        var time = DescribeTime(hour.Value, minute.Value);
        if (time is null) return null;
        var day = DescribeDay(dom.Value, dow.Value, month.Value);
        if (day is null) return null;

        var text = (day.Phrase, time.Value.Phrase) switch
        {
            ("", var t) when t.StartsWith("at") => "Daily " + t,
            ("", var t) => t,
            (var d, var t) when t.StartsWith("at") => $"{d} {t}",
            (var d, var t) => $"{t}, {d}",   // "Every 15 minutes, every Monday"
        };
        if (!day.MonthsCovered)
        {
            var months = DescribeMonth(month.Value);
            if (months is null) return null;
            if (months.Length > 0)
                text = text.StartsWith("Daily")
                    ? text.Replace("Daily", $"Every day {months}")
                    : text + " " + months;
        }
        if (parts.Length == 6) text += $" at :{seconds:00} seconds";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    // ------------------------------------------------------------ time part

    private static (string Phrase, bool)? DescribeTime(Field hour, Field minute)
    {
        if (hour.Kind == FieldKind.All)
        {
            if (minute.Kind == FieldKind.All) return ("Every minute", true);
            if (minute.Kind == FieldKind.Step) return ($"Every {minute.StepSize} minutes", true);
            if (minute.Kind == FieldKind.List && minute.Values.Length <= 6)
                return minute.Values.Length == 1
                    ? ($"Hourly at :{minute.Values[0]:00}", true)
                    : ("Hourly at " + JoinTimes(minute.Values.Select(m => $":{m:00}")), true);
            return null;
        }

        if (minute.Kind != FieldKind.List && minute.Kind != FieldKind.Step) return null;

        switch (hour.Kind)
        {
            case FieldKind.List when hour.Values.Length <= 6 && minute.Kind == FieldKind.List:
                var times = hour.Values.SelectMany(h => minute.Values.Select(m => $"{h}:{m:00}")).ToArray();
                if (times.Length > 6) return null;
                return ($"at {JoinTimes(times)}", true);
            case FieldKind.List when hour.Values.Length == 1 && minute.Kind == FieldKind.Step:
                var last = 59 - (59 % minute.StepSize);
                return ($"Every {minute.StepSize} minutes from {hour.Values[0]}:00 to {hour.Values[0]}:{last:00}", true);
            case FieldKind.Step when minute.Kind == FieldKind.List && minute.Values.Length == 1:
                return ($"Every {hour.StepSize} hours at {(minute.Values[0] == 0 ? "the top of the hour" : $":{minute.Values[0]:00}")}", true);
            case FieldKind.Range when hour.StepSize > 0 && minute.Kind == FieldKind.List && minute.Values.Length == 1:
                return ($"Every {hour.StepSize} hours from {hour.RangeStart}:{minute.Values[0]:00} to {hour.RangeEnd}:{minute.Values[0]:00}", true);
            case FieldKind.Range when hour.StepSize == 0 && minute.Kind == FieldKind.List && minute.Values.Length == 1:
                return ($"Hourly between {hour.RangeStart}:{minute.Values[0]:00} and {hour.RangeEnd}:{minute.Values[0]:00}", true);
            default: return null;
        }
    }

    // ------------------------------------------------------------ day part

    private sealed record DayPhrase(string Phrase, bool MonthsCovered);

    private static DayPhrase? DescribeDay(Field dom, Field dow, Field month)
    {
        // dom/dow/month all wildcard → no day qualifier at all.
        if (dom.Kind == FieldKind.All && dow.Kind == FieldKind.All && month.Kind == FieldKind.All)
            return new DayPhrase("", false);

        // dom * and dow * but months restricted: every day within those months.
        if (dom.Kind == FieldKind.All && dow.Kind == FieldKind.All)
            return new DayPhrase("Every day", false);

        // both dom and dow restricted is OR semantics in cron — too easy to
        // misread, keep the generic hint for those.
        if (dom.Kind != FieldKind.All && dow.Kind != FieldKind.All) return null;

        if (dom.Kind != FieldKind.All)
        {
            var (set, ok) = DescribeDaySet(dom);
            if (!ok) return null;
            var where = month.Kind == FieldKind.All ? "of the month" : "of " + JoinNames(month, MonthNames, -1);
            return new DayPhrase($"{set} {where}", month.Kind != FieldKind.All);
        }

        var days = DescribeDowSet(dow);
        if (days is null) return null;
        return new DayPhrase(days + (month.Kind == FieldKind.All ? "" : " in " + JoinNames(month, MonthNames, -1)),
            month.Kind != FieldKind.All);
    }

    /// <summary>"Every 5th day" / "Every 1st and 15th day" / "Between the 1st and the 15th".</summary>
    private static (string?, bool) DescribeDaySet(Field dom)
    {
        return dom.Kind switch
        {
            FieldKind.List when dom.Values.Length <= 8 => ($"Every {JoinOrdinals(dom.Values)} day", true),
            FieldKind.Range when dom.RangeEnd - dom.RangeStart <= 15 =>
                ($"Between the {Ordinal(dom.RangeStart)} and the {Ordinal(dom.RangeEnd)}", true),
            _ => (null, false),
        };
    }

    private static string? DescribeDowSet(Field dow)
    {
        return dow.Kind switch
        {
            FieldKind.List when dow.Values.Length <= 3 => "Every " + JoinNames(dow, DayNames, 0),
            FieldKind.List => null,
            FieldKind.Range when dow.RangeStart == 1 && dow.RangeEnd == 5 => "Every weekday",
            FieldKind.Range when dow.RangeEnd - dow.RangeStart <= 3 => "Every " + JoinNames(dow, DayNames, 0),
            _ => null,
        };
    }

    // ------------------------------------------------------------ month part

    private static string? DescribeMonth(Field month) => month.Kind switch
    {
        FieldKind.List when month.Values.Length <= 6 => "in " + JoinNames(month, MonthNames, -1),
        FieldKind.Range when month.RangeEnd - month.RangeStart <= 6 =>
            "in " + MonthNames[month.RangeStart - 1] + "–" + MonthNames[month.RangeEnd - 1],
        FieldKind.All => "",
        _ => null,
    };

    // ------------------------------------------------------------ field parsing

    private static Field? Parse(string raw, int min, int max)
    {
        raw = raw.Trim();
        if (raw is "*" or "?") return new Field(FieldKind.All, 0, [], 0, 0);

        var slash = raw.Split('/');
        int? step = null;
        var basePart = raw;
        if (slash.Length == 2)
        {
            if (!int.TryParse(slash[1], out var s) || s <= 0) return null;
            step = s;
            basePart = slash[0];
        }
        else if (slash.Length > 2) return null;

        if (basePart is "*" or "?")
            return step is { } st
                ? new Field(FieldKind.Step, st, [], min, max)
                : new Field(FieldKind.All, 0, [], 0, 0);

        var values = new List<int>();
        foreach (var token in basePart.Split(','))
        {
            var rp = token.Split('-');
            if (rp.Length == 2)
            {
                if (!TryNum(rp[0], out var a) || !TryNum(rp[1], out var b) || a > b) return null;
                for (var i = a; i <= b; i++) values.Add(i);
            }
            else if (rp.Length == 1)
            {
                if (!TryNum(token, out var v)) return null;
                values.Add(v);
            }
            else return null;
        }
        if (step is { } size) values = values.Where(v => (v - values[0]) % size == 0).ToList();
        if (values.Any(v => v < min || v > max)) return null;
        if (max == 7) values = [.. values.Select(x => x == 7 ? 0 : x).Distinct()];
        values.Sort();
        values = values.Distinct().ToList();
        if (values.Count == 1) return new Field(FieldKind.List, 0, [values[0]], 0, 0);
        // a stepped filter always yields an arithmetic sequence → range phrase
        if (step is { } stepSize) return new Field(FieldKind.Range, stepSize, [], values[0], values[^1]);
        // dense lists read better as ranges in phrases
        if (values.Count > 2 && values[^1] - values[0] == values.Count - 1)
            return new Field(FieldKind.Range, 0, [], values[0], values[^1]);
        return new Field(FieldKind.List, 0, [.. values], 0, 0);
    }

    private static bool TryNum(string text, out int value)
    {
        var t = text.Trim();
        // cron name forms: JAN, MAR, MON, SUN (at least three letters)
        if (t.Length >= 3)
        {
            for (var i = 0; i < MonthNames.Length; i++)
                if (MonthNames[i].StartsWith(t, StringComparison.OrdinalIgnoreCase))
                { value = i + 1; return true; }
            for (var i = 0; i < DayNames.Length; i++)
                if (DayNames[i].StartsWith(t, StringComparison.OrdinalIgnoreCase))
                { value = i; return true; }
        }
        return int.TryParse(t, out value);
    }

    // ------------------------------------------------------------ wording utils

    private static string JoinTimes(IEnumerable<string> times)
    {
        var parts = times.ToArray();
        return parts.Length == 2 ? $"{parts[0]} and {parts[1]}" : string.Join(", ", parts);
    }

    /// <summary>Joins field values into names. <paramref name="offset"/> shifts
    /// 1-based values (months) onto 0-based name arrays.</summary>
    private static string JoinNames(Field field, string[] names, int offset)
    {
        if (field.Kind == FieldKind.List && field.Values.Length <= 3)
        {
            var list = field.Values.Select(v => names[(v + offset) % names.Length]).Distinct().ToArray();
            return list.Length == 1 ? list[0]
                 : list.Length == 2 ? $"{list[0]} and {list[1]}"
                 : string.Join(", ", list);
        }
        if (field.Kind == FieldKind.Range)
            return $"{names[(field.RangeStart + offset) % names.Length]}–{names[(field.RangeEnd + offset) % names.Length]}";
        return "";
    }

    private static string JoinOrdinals(int[] values) => values.Length switch
    {
        1 => Ordinal(values[0]),
        2 => $"{Ordinal(values[0])} and {Ordinal(values[1])}",
        _ => string.Join(", ", values.Select(Ordinal)),
    };

    private static string Ordinal(int n) => (n % 100) is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch
    {
        1 => $"{n}st",
        2 => $"{n}nd",
        3 => $"{n}rd",
        _ => $"{n}th",
    };
}
