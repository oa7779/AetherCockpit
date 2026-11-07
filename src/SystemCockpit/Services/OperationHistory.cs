using System.Collections.ObjectModel;

namespace SystemCockpit.Services;

public sealed class OperationEntry(string label) : Observable
{
    public string Label { get; } = label;
    public DateTimeOffset Created { get; } = DateTimeOffset.Now;
    private string _state = "NICHT GESTARTET", _detail = "";
    public string State { get => _state; internal set => Set(ref _state, value); }
    public string Detail { get => _detail; internal set => Set(ref _detail, value); }
}

public sealed class OperationHistory
{
    public const int Capacity = 200;
    public ObservableCollection<OperationEntry> Entries { get; } = [];
    public OperationEntry Add(string label)
    {
        if (Entries.Count >= Capacity)
        {
            var finished = Entries.LastOrDefault(e => e.State is not ("LAEUFT" or "NICHT GESTARTET"))
                ?? throw new InvalidOperationException("Auftragslimit erreicht; laufende Auftraege werden nicht entfernt.");
            Entries.Remove(finished);
        }
        var entry = new OperationEntry(label);
        Entries.Insert(0, entry);
        return entry;
    }
}
