namespace SystemCockpit.Services;

/// <summary>
/// Browserartiger Navigationsverlauf fuer Ansichten: Besuche werden aufgezeichnet,
/// Zurueck/Vor bewegen sich darin, ein neuer Besuch nach Zurueck verwirft den
/// Vorwaerts-Zweig. Rein lokale UI-Navigation, keine Persistenz.
/// </summary>
public sealed class NavigationTrail
{
    public const int MaxEntries = 100;
    private readonly List<string> _entries = [];
    private int _index = -1;

    public string? Current => _index >= 0 && _index < _entries.Count ? _entries[_index] : null;
    public bool CanBack => _index > 0;
    public bool CanForward => _index >= 0 && _index < _entries.Count - 1;

    public void Visit(string location)
    {
        if (location.Length == 0 || Current == location) return;
        if (_index < _entries.Count - 1) _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        _entries.Add(location);
        if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        _index = _entries.Count - 1;
    }

    public string? Back() => CanBack ? _entries[--_index] : null;
    public string? Forward() => CanForward ? _entries[++_index] : null;
}
