using System.Collections.ObjectModel;

namespace MCServer.Helpers;

public class StackList<T> : ObservableCollection<T>
{
    public int MaxCount;

    public StackList(int MaxCount) : base()
    {
        this.MaxCount = MaxCount;
    }

    public StackList(int MaxCount, IEnumerable<T> list) : base(list)
    {
        this.MaxCount = MaxCount;
    }

    public new void Add(T item)
    {
        if (Count == MaxCount)
            RemoveAt(0);
        if (Count > MaxCount)
            RemoveRange(0, Count - MaxCount);

        base.Add(item);
    }

    public void AddRange(IEnumerable<T> collection)
    {
        foreach (var item in collection)
            Add(item);
    }

    public void RemoveRange(int index, int count)
    {
        for (int I = index; I < count; I++)
            RemoveAt(I);
    }

    public int FindLastIndex(Func<T, bool> predicate)
    {
        for (int I = Count - 1; I >= 0; I--)
            if (predicate(Items[I]))
                return I;
        return -1;
    }

    public new void Clear()
    {
        while (Count > 0)
            RemoveAt(0);
    }
}
