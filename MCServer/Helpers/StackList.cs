using System.Collections.ObjectModel;
using MudBlazor;
using static MudBlazor.CategoryTypes;

namespace MCServer.Helpers;

public class StackList : ObservableCollection<ConsoleLine>
{
    public readonly object Lock = new();
    public int MaxCount;

    public StackList(int MaxCount) : base()
    {
        this.MaxCount = MaxCount;
    }

    public StackList(int MaxCount, IEnumerable<ConsoleLine> list) : base(list)
    {
        this.MaxCount = MaxCount;
    }

    public new void Add(ConsoleLine item)
    {
        lock (Lock)
        {
            if (Count == MaxCount)
                RemoveAt(0);
            if (Count > MaxCount)
                RemoveRange(0, Count - MaxCount);

            base.Add(item);
        }
    }

    public void AddRange(IEnumerable<ConsoleLine> collection)
    {
        lock (Lock)
        {
            foreach (var item in collection)
                Add(item);
        }
    }

    public List<ConsoleLine> GetSnapshot()
    {
        lock (Lock)
        {
            return [..this];
        }
    }

    public void RemoveRange(int index, int count)
    {
        lock (Lock)
        {
            for (int I = index; I < count; I++)
                RemoveAt(I);
        }
    }

    public int FindLastIndex(Func<ConsoleLine, bool> predicate)
    {
        for (int I = Count - 1; I >= 0; I--)
            if (predicate(Items[I]))
                return I;
        return -1;
    }

    public new void Clear()
    {
        lock (Lock)
        {
            while (Count > 0)
                RemoveAt(0);
        }
    }
}

public class ConsoleLine(DateTime dateTime, Color type, string msg)
{
    public DateTime DateTime { get; set; } = dateTime;
    public Color Type { get; set; } = type;
    public string Line { get; set; } = msg;
    public bool IncludeInfoStamp { get; set; } = true;

    public override string ToString()
    {
        if (IncludeInfoStamp)
            return $"[{DateTime:yyyy-MM-dd HH:mm:ss:fff} {Type}] {Line}";
        return Line;
    }
}