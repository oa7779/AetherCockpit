using System.Text.Json;
using System.IO;

namespace SystemCockpit.Services;

public sealed record PerformanceAssessment(int State, double? Base, double? Cpu, double? Memory, double? Graphics,
    double? Disk, string? StoredAt)
{
    public bool Valid => State == 1;
    public string StateText => State switch
    {
        1 => "Gueltige gespeicherte Windows-Bewertung",
        2 => "Hardware seit Messung geaendert / nicht gueltig",
        3 => "Keine Bewertung vorhanden",
        4 => "Ungueltige Bewertung",
        _ => "Bewertungszustand unbekannt"
    };
}

public static class PerformanceIndex
{
    public const string ReadScript = """
        $assessment=Get-CimInstance Win32_WinSAT -Filter "TimeTaken='MostRecentAssessment'"
        if($null -eq $assessment){throw 'WinSAT-Provider liefert keine Bewertung'}
        $path=Join-Path $env:WINDIR 'Performance\WinSAT\DataStore'
        $file=if(Test-Path -LiteralPath $path){Get-ChildItem -LiteralPath $path -Filter '*Formal.Assessment*.xml' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1}
        @{State=[int]$assessment.WinSATAssessmentState;Base=$assessment.WinSPRLevel;Cpu=$assessment.CPUScore;Memory=$assessment.MemoryScore;Graphics=$assessment.GraphicsScore;Disk=$assessment.DiskScore;StoredAt=$(if($file){$file.LastWriteTimeUtc.ToString('o')}else{$null})} | ConvertTo-Json -Compress
        """;
    public const string BenchmarkScript = """
        $ErrorActionPreference='Stop'
        $exe=Join-Path $env:WINDIR 'System32\WinSAT.exe'
        if(!(Test-Path -LiteralPath $exe)){throw 'Windows WinSAT fehlt; keine Ersatzbewertung erfunden'}
        & $exe formal
        if($LASTEXITCODE -ne 0){throw "WinSAT Exit $LASTEXITCODE"}
        """;
    public static async Task<PerformanceAssessment> ReadAsync(CancellationToken token) =>
        Parse(await CommandRunner.PowerShellAsync(ReadScript, token));
    public static PerformanceAssessment Parse(string json)
    {
        var result = JsonSerializer.Deserialize<PerformanceAssessment>(json) ?? throw new InvalidOperationException("WinSAT lieferte kein Ergebnis.");
        if (result.State is < 0 or > 4) throw new InvalidDataException("Unbekannter WinSAT-Bewertungszustand.");
        return result with { Base = Score(result.Base, result.Valid), Cpu = Score(result.Cpu, result.Valid),
            Memory = Score(result.Memory, result.Valid), Graphics = Score(result.Graphics, result.Valid), Disk = Score(result.Disk, result.Valid) };
    }
    private static double? Score(double? value, bool valid) => valid && value is >= 1 and <= 9.9 && double.IsFinite(value.Value) ? value : null;
}
