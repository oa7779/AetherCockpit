namespace AetherOperations;

public sealed record WindowsUpdateSelection(Guid UpdateId, int Revision, string EulaHash, bool AcceptEula);
