"""Source-only inventory: no desktop DLLs, sensor reads or settings writes.

Lists unresolved display literals and German-authored C#/PowerShell candidates.
Candidates are explicitly not an assertion of complete semantic coverage:
data-flow through model/catalog properties still requires manual review.
"""
import argparse
import collections
import html
import json
import pathlib
import re
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
DISPLAY_PROPERTY = re.compile(
    r'(Text|Content|Header|Title|ToolTip|AutomationProperties.Name)="([^"]*)"'
)
GERMAN = re.compile(
    r"(?i)(?:"
    r"schli[eß]|ueber|über|waeh|wähl|pruef|prüf|loesch|lösch|lesend|anzeigen|"
    r"ausgabe|verfueg|verfüg|ausfue|ausfü|eintra|einträ|neustart|speicher|"
    r"auftra|aufträ|geraet|gerät|bereinig|warnschw|messwert|rechner|belegt|"
    r"zeitraum|erstellt|bestaet|bestät|einstell|zurue|zurü|hauptfenster|hinweis|"
    r"herausgeber|fehler|verbind|quelle|such|auswahl|aktualis|installiert|"
    r"deinstallier|diagnose|zeitstempel|laufzeit|sicher|aender|änder|"
    r"vorschau|schluess|schlüss|zugriff|gefund|grenze|hersteller|verlauf|"
    r"reparatur|ordner|werte|bereich|berecht|vollstaend|vollständ|"
    r"\b(?:frei|zeit|art|aktiv|befehl|keine?|nur|nicht|bitte|wird|wurden|"
    r"wurde|abgebrochen|abgeschlossen|beendet|lesen|starten|anhalten|"
    r"sekunden|minuten|tage|alt|thema|links|rechts|vorher|nachher)\b)"
)
ORIGINAL_DETECTOR = re.compile(
    r"(?i)(?:schli[eß]|ueber|über|waeh|wähl|pruef|prüf|loesch|lösch|lesend|"
    r"anzeigen|ausgabe|verfueg|verfüg|ausfue|ausfü|eintra|einträ|neustart|"
    r"speicher|auftra|aufträ|geraet|gerät|bereinig|warnschw|messwert|rechner|"
    r"belegt|zeitraum|erstellt|bestaet|bestät|einstell|zurue|zurü|hauptfenster|"
    r"hinweis|herausgeber)"
)
INVARIANT_CODES = {
    "Cockpit", "Mainboard", "Taskmanager", "Sensoren", "Laufwerke",
    "Messauswertung", "Diagnose", "Sicherheit", "Cleaner", "Ereignisse",
    "Superadmin", "Operations", "Hilfe", "SMART", "Netzwerk", "Pakete",
    "Auftraege", "Wartung", "Autostart", "Spuren", "Logs / Gaming",
    "Einstellungen", "Schluessel", "KRITISCH", "HOCH", "MITTEL", "UNBEKANNT",
    "HINWEIS", "INFORMATION", "LAEUFT", "ABGESCHLOSSEN", "ABGEBROCHEN",
    "FEHLGESCHLAGEN", "NICHT GESTARTET", "UEBERSPRUNGEN", "KEIN UPGRADE",
    "UNVOLLSTAENDIG", "NICHT AUSGEFUEHRT", "Alle", "App", "Datei", "Ordner",
    "Nur Diagnose", "Windows reparieren", "Kurzpflege", "App-Updates",
}
CS_LITERAL = re.compile(r'@?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'')
DIRECT_SINK = re.compile(
    r"(?:\.Text\s*=|\.Footer\s*=|\.CleanupStatus\s*=|\.Status\s*=|"
    r"MessageBox\.Show|Write\(|AppendTerminal\(|RunAsync\(|Confirm\(|"
    r"throw new|new TextBlock|Content\s*=|Header\s*=|Title\s*=|"
    r"Description\s*=|ToolTip\s*=)"
)

REVIEWED_AUTHORED_FILES = set("""
App.xaml.cs ControlCenterWindow.Packages.cs ControlCenterWindow.xaml.cs
AdminWorkbench.xaml.cs EventBrowser.xaml.cs HardwareBoardView.Drawing.cs
OperationsHub.xaml.cs DashboardModel.cs DiagnosticExport.cs ExportPreviewWindow.xaml.cs
GadgetWindow.cs MainWindow.xaml.cs OverlayWindow.xaml.cs StorageAnalysisWindow.cs TaskCenterWindow.cs
ActionJournal.cs AdminCommandCatalog.cs AdminManagement.cs AudioTopology.cs BoardLiveFeed.cs
CacheCatalog.cs CertificateDiagnostics.cs CommandRunner.cs DashboardMetrics.cs DiagnosticAssistants.cs
DisplayTopology.cs EventMonitor.cs FrameTelemetry.cs HardwareSensors.cs HardwareTopology.cs HelpCatalog.cs
HistoryReader.cs MaintenancePlans.cs ManagedActionTools.cs ManagedDevices.cs ManagerReportingService.cs
NativeMetrics.cs NetworkTools.cs NetworkWatch.cs NpcapCapture.cs PackageCenter.cs PackageCenter.Transfer.cs
PerformanceIndex.cs RegistryTweaks.cs SensorChartHistory.cs SensorSemantics.cs ShellbagAnalyzer.cs
SidebarDisplays.cs SmartService.cs Sparkline.cs SpeedTest.cs StorageAnalysis.cs SystemActions.cs
TaskCatalog.cs TempCleaner.cs WindowsProbe.cs Program.cs SaverSession.cs SaverWindow.cs
""".split())
REVIEWED_NEUTRAL_DISPLAY = {
    "CPU-TEMP", "%    DOWNLOAD", "%    GPU", "%    RAM", "Mbit/s    UPLOAD",
    "2 / Windows Update", "A E T H E R", "A-01 // SYSTEM BUS", "AETHER / LIVE",
    "AETHER / xBAR", "AETHER // MAINFRAME 1986", "AETHER // SYSTEM CONTROL",
    "AETHER Overlay", "AETHER VT // PHOSPHOR TERMINAL", "AETHER xBar",
    "AETHER | System Observatory | Olaf Weins", "AVG", "Auto 15s", "Autoscroll",
    "Bluetooth", "CMOS Minimum V", "CPU", "CPU %", "CPU / PACKAGE POWER",
    "CPU / THERMAL", "CPU // MEMORY // STORAGE // LIVE", "CPU TEMP", "Cleaner",
    "Cockpit", "Copyright (c) 2026 Olaf Weins", "DETAIL",
    "EVENTMONITOR / SYSTEM + APPLICATION", "GPU TEMP", "HANDLES", "HTTPS 5986",
    "Height", "ID", "INSTALL", "Information", "LIVE", "LOCAL FIRST",
    "M365 / Copilot / Server", "MAC", "MAX", "MIN", "MS", "Monitor", "NAME",
    "NATIVE / WINDOWS / v0.7", "NEON WAVE / GLASS CONTROL",
    "OPTIONAL / MICROSOFT SYSINTERNALS + ADK", "Operations", "PID", "PIN",
    "PLAN", "PS ADMIN >", "Ping", "PresentMon Capture", "PresentMon Console EXE",
    "RAM MB", "READ MB/s", "Router-Ping", "SENSOR", "SLOT", "SMART", "SSD TEMP",
    "STATUS", "Speedtest Download", "Spiel.exe", "Standard", "Start / Enter",
    "Storage Observatory", "Superadmin", "System Observatory",
    "TASKMANAGER / LIVE PERFORMANCE", "THREADS", "TRANSPORT", "Task-Manager",
    "VERSION", "WHEA / Kernel / Bugchecks", "WRITE MB/s", "Windows Update", "X",
    "aam", "abort", "apm", "conveyance", "long", "selective", "short", "v", "⌂ COCKPIT",
}


def review_site(item):
    """Classify the inspected desktop data paths, without changing or hiding UI."""
    detection = item["classification"]
    item["detection"] = detection
    name = pathlib.PureWindowsPath(item["path"]).name
    text = item["text"]
    if detection == "non-ui-log":
        return "preserved-internal-log", "Trace-only message; no text-bearing UI property."
    if detection == "neutral-or-unreviewed-display":
        if text.strip() in REVIEWED_NEUTRAL_DISPLAY:
            return "preserved-neutral-display", "Existing English/technical caption, brand, identifier or executable example."
        return "unreviewed-display", "New literal outside the reviewed static-caption inventory."
    if detection == "untranslated-display-confirmed":
        return detection, "Authored literal assigned directly to a text-bearing XAML property."
    if name == "ManagedActionTools.cs" and detection != "untranslated-direct-ui-confirmed":
        return "preserved-localized-definition", "Private OptionalDefinitions metadata is replaced by named resources in OptionalTools."
    if name == "MaintenancePlans.cs" and re.search(
        r'new\("(?:inventory|events|startup|componentAnalysis|diskScan|repairImage|cleanup|'
        r'componentCleanup|optimize|defenderUpdate|defenderQuick|defenderFull|windowsSearch|'
        r'windowsInstall|wingetInstall)"', text
    ):
        return "preserved-localized-definition", "Private action Name/Description replaced by resources in Catalog; script text on other lines remains separately inventoried."
    if name == "DiagnosticAssistants.cs":
        return "preserved-invariant-identifier", "Agent/Severity IDs are serialized unchanged; AgentDisplay/SeverityDisplay use named resources."
    if name == "PackageCenter.cs" and "columns.FindIndex" in text:
        return "preserved-external-format", "Parser recognizes original German/English WinGet output column headings."
    if name == "AdminManagement.cs" and (
        "else script += action switch" in text or '"Ausfuehren" => $"Start-ScheduledTask' in text or
        "var verb = action switch" in text or "changes = action !=" in text
    ):
        return "preserved-invariant-identifier", "Action dispatch ID only; untranslated action display options are separately counted."
    if name == "AdminWorkbench.xaml.cs" and 'if (_previewAction == "Gesicherten Zustand wiederherstellen")' in text:
        return "preserved-invariant-identifier", "Restore dispatch ID; the corresponding visible option remains a separate UI gap."
    if name == "MainWindow.xaml.cs" and "task.Destination ==" in text:
        return "preserved-invariant-identifier", "Navigation destination ID; TaskCatalog display definitions remain separate UI gaps."
    if name == "OperationsHub.xaml.cs" and 'Save(folder, "Diagnose.json"' in text:
        return "preserved-path", "Report filename, not a translated caption."
    if detection == "invariant-code-requires-display-projection-review":
        if name not in {"AdminCommandCatalog.cs", "ShellbagAnalyzer.cs"}:
            return "preserved-invariant-identifier", "Route, category, template or state code; existing display projection is separate."
    if name in REVIEWED_AUTHORED_FILES:
        return (
            "untranslated-direct-ui-confirmed" if detection == "untranslated-direct-ui-confirmed"
            else "untranslated-authored-ui-confirmed",
            "Authored caption/model/report/help/safety text or script wrapper/error reaches a desktop text property; embedded native data/identifiers must remain unchanged.",
        )
    return "unreviewed-source", "Source surface outside the reviewed data paths."


def catalog(name):
    result = []
    for language in ("", ".en"):
        file = ROOT / "src" / "SystemCockpit" / "Resources" / f"{name}{language}.resx"
        nodes = ET.parse(file).getroot().findall("data")
        data = {node.attrib["name"]: node.findtext("value") or "" for node in nodes}
        if len(data) != len(nodes):
            raise ValueError(f"Duplicate keys: {file}")
        result.append(data)
    if result[0].keys() != result[1].keys():
        raise ValueError(f"Catalog key mismatch: {name}")
    for key, value in result[0].items():
        placeholders = lambda text: sorted(re.findall(r"\{\d+[^}]*\}", text))
        if placeholders(value) != placeholders(result[1][key]):
            raise ValueError(f"Placeholder mismatch: {name}/{key}")
    return result[0]


def inventory():
    catalogs = {name: catalog(name) for name in ("DesktopText", "DesktopUiText")}
    all_resource_keys = set().union(*(data.keys() for data in catalogs.values()))
    items = []
    missing = []
    old_count = 0
    for subtree in ("SystemCockpit", "AetherScreensaver"):
        for file in sorted((ROOT / "src" / subtree).rglob("*")):
            if any(part in ("bin", "obj") for part in file.parts) or file.suffix not in (".xaml", ".cs"):
                continue
            relative = str(file.relative_to(ROOT))
            text = file.read_text(encoding="utf-8-sig")
            for accessor, key in re.findall(
                r'(Desktop(?:Ui)?Text)\.(?:Get|Format)\("([^"]+)"\s*[,)]', text
            ):
                if key not in catalogs[accessor]:
                    missing.append({"path": relative, "key": key})
            if file.suffix == ".xaml":
                for key in re.findall(r"loc:DesktopLabel ([^,}\s]+)|ModernKey=([^}\s]+)", text):
                    name = key[0] or key[1]
                    if name not in catalogs["DesktopUiText"]:
                        missing.append({"path": relative, "key": name})
            for arguments in re.findall(r"DesktopScriptText\.Preamble\((.*?)\)", text, re.S):
                for key in re.findall(r'"([^"]+)"', arguments):
                    if key not in catalogs["DesktopUiText"]:
                        missing.append({"path": relative, "key": key})
            for number, line in enumerate(text.splitlines(), 1):
                if file.suffix == ".xaml":
                    values = list(DISPLAY_PROPERTY.findall(line))
                    values += [
                        ("inline-display", value)
                        for value in re.findall(r">([^<]+)</(?:TextBlock|Run|Button|CheckBox)>", line)
                    ]
                    if 'Property="Text"' in line or 'Property="Content"' in line:
                        values += [("Setter.Value", value) for value in re.findall(r'Value="([^"]*)"', line)]
                    for prop, value in values:
                        value = html.unescape(value)
                        if value.startswith("{") or not re.search(r"[A-Za-zÄÖÜäöüß]", value):
                            continue
                        if prop not in ("Setter.Value", "inline-display") and ORIGINAL_DETECTOR.search(value):
                            old_count += 1
                        items.append({
                            "path": relative, "line": number, "property": prop,
                            "classification": "untranslated-display-confirmed" if GERMAN.search(value)
                                else "neutral-or-unreviewed-display",
                            "text": value,
                        })
                else:
                    if line.lstrip().startswith(("//", "/*", "*")):
                        continue
                    values = [
                        m.group()[2 if m.group().startswith('@"') else 1:-1]
                        for m in CS_LITERAL.finditer(line)
                    ]
                    german = [
                        value for value in values
                        if value not in all_resource_keys and GERMAN.search(value)
                    ]
                    if not german:
                        continue
                    if "Trace." in line or "Debug." in line:
                        classification = "non-ui-log"
                    elif all(value in INVARIANT_CODES for value in german):
                        classification = "invariant-code-requires-display-projection-review"
                    elif re.search(r"(?:HKLM|HKCU|HKEY_|SOFTWARE\\|System32\\|\\\\)", line):
                        classification = "path-or-script-candidate-requires-review"
                    elif DIRECT_SINK.search(line):
                        classification = "untranslated-direct-ui-confirmed"
                    else:
                        classification = "authored-model-or-script-candidate-requires-review"
                    items.append({
                        "path": relative, "line": number, "classification": classification,
                        "text": line.strip(),
                    })
    detection_counts = collections.Counter(item["classification"] for item in items)
    for item in items:
        item["classification"], item["reason"] = review_site(item)
    counts = collections.Counter(item["classification"] for item in items)
    confirmed = sum(counts[key] for key in counts if key.startswith("untranslated-"))
    app = (ROOT / "src" / "SystemCockpit" / "App.xaml.cs").read_text(encoding="utf-8-sig")
    exit_body = app[app.index("protected override void OnExit"):]
    restart = (ROOT / "src" / "SystemCockpit" / "Services" / "DesktopRestart.cs").read_text(encoding="utf-8-sig")
    source_checks = {
        "registered_wait_completion_before_single_instance_disposal":
            exit_body.index("callbacksFinished.WaitOne()") < exit_body.index("_activation?.Dispose()"),
        "single_instance_disposal_before_replacement_launch":
            exit_body.index("_activation?.Dispose()") < exit_body.index("StartReplacementAfterExit()"),
        "restart_uses_orderly_main_window_exit": "mainWindow.RequestExit()" in restart,
        "original_120_text_detector_cleared": old_count == 0,
    }
    return {
        "scope": ["src\\SystemCockpit", "src\\AetherScreensaver"],
        "coverage": "source inventory, not a complete semantic/data-flow audit",
        "original_120_detector_remaining": old_count,
        "catalog_key_counts": {name: len(data) for name, data in catalogs.items()},
        "missing_explicit_resources": missing,
        "source_safety_checks": source_checks,
        "confirmed_untranslated_sites_detected": confirmed,
        "english_complete": False,
        "counts_by_detection": dict(detection_counts),
        "counts_by_classification": dict(counts),
        "remaining_ui_by_file": dict(collections.Counter(
            item["path"] for item in items if item["classification"].startswith("untranslated-")
        )),
        "sites": items,
        "limitations": [
            "C# candidates are line sites, not every string token or runtime-generated text.",
            "German vocabulary detection is conservative and does not prove complete English coverage.",
            "Reviewed classification separates actual displayed authored text from private localized definitions, dispatch IDs, native parser formats and internal logs.",
            "Counts are exact for inventoried line sites, not exhaustive counts of every possible UI string.",
            "External/native output and IDs/paths/hardware names must remain unchanged.",
            "Shared server-authored catalog text is outside this desktop source inventory.",
        ],
    }


def release_blockers(report):
    blockers = []
    if report["missing_explicit_resources"]:
        blockers.append("Missing explicit resources.")
    if not all(report["source_safety_checks"].values()):
        blockers.append("A source safety check failed.")
    if report["confirmed_untranslated_sites_detected"]:
        blockers.append(
            f'{report["confirmed_untranslated_sites_detected"]} untranslated source sites remain.'
        )
    if report["counts_by_classification"].get("unreviewed-display", 0):
        blockers.append("Unreviewed display literals remain.")
    if report["counts_by_classification"].get("unreviewed-source", 0):
        blockers.append("Unreviewed source sites remain.")
    if not report["english_complete"]:
        blockers.append("Full English coverage and runtime review are not confirmed.")
    return blockers


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=pathlib.Path)
    parser.add_argument("--require-complete", action="store_true",
                        help="Fail unless English is reviewed and ready for release.")
    args = parser.parse_args()
    report = inventory()
    if args.output:
        output = args.output.resolve()
        if not output.is_relative_to(ROOT):
            parser.error("Output must stay inside the project.")
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "sites"}, ensure_ascii=True, indent=2))
    if args.require_complete:
        blockers = release_blockers(report)
        if blockers:
            print("English release gate failed: " + " ".join(blockers))
            return 1
    return 1 if report["missing_explicit_resources"] or not all(report["source_safety_checks"].values()) else 0


if __name__ == "__main__":
    raise SystemExit(main())
