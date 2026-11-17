"""Flag reviewed ASCII transliterations in German desktop resource values."""
import pathlib
import re
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parent.parent
TRANSLITERATED_STEMS = (
    "abhaeng", "aelter", "aender", "ablaeuf", "aktualitaet", "aktivitaet",
    "abschliess", "ausschliess", "einschliess", "rückgaeng", "schliess",
    "aufraeum", "auftraeg", "ausdrueck", "ausfuehr", "ausgeduenn", "duenn",
    "ausgewaehl", "ausloes", "auswaehl", "beeintraecht", "benoetig",
    "beschaedig", "bestaetig", "bestueck", "bildschirmhoehe", "binaer",
    "cachegroess", "datentraeg", "domaen", "einfueg", "eingeschraenk",
    "eintraeg", "endguelt", "enthaelt", "entschluessel", "kanael",
    "erhoeht", "erklaer", "faehig", "fahrlaess", "folgeschaed", "fuehr",
    "fuer", "gebuehr", "gekuerz", "geloesch", "gemaess", "geoeffn",
    "gepruef", "geraet", "geschaetz", "geschuetz", "geschwaerz", "gewaehl",
    "gewaehr", "gewuensch", "gezaehl", "groesser", "groess", "haeng",
    "hinzufueg", "kapazitaet", "kennwoerter", "konnektivitaet", "koenn",
    "laedt", "laenge", "laeuft", "loesch", "lueck", "lueft", "moeg",
    "muess", "naechst", "netzwerkueber", "oeffn", "paketlaeng",
    "passwoerter", "persoenlich", "prioritaet", "problemgeraet",
    "prozesszaehl", "pruef", "rechteerhoeh", "renderaktivitaet", "rueck",
    "schluessel", "schwaerz", "sensorlueck", "spaet", "stabilitaets",
    "standardmaess", "stoer", "stuendlich", "systemaender", "zustaend",
    "verlaeuf", "testgroess", "ueber", "unberuehr", "ungepruef", "unguelt",
    "unterstuetz", "unveraender", "unverfueg", "unvollstaend", "verfueg",
    "vergroess", "verknuepf", "vertrauenswuerd", "vollstaend", "vorwaerts",
    "waehl", "wartungsplaen", "widerspruech", "zaehl", "zukuenft",
    "zulaess", "zurueck",
)
PATTERN = re.compile(
    "|".join(re.escape(stem) for stem in sorted(TRANSLITERATED_STEMS, key=len, reverse=True)),
    re.IGNORECASE,
)


def find_transliterations(value):
    return sorted(
        {match.group() for match in PATTERN.finditer(value)},
        key=str.casefold,
    )


def main():
    failures = []
    checked = 0
    for name in ("DesktopText", "DesktopUiText"):
        path = ROOT / "src" / "SystemCockpit" / "Resources" / f"{name}.resx"
        for node in ET.parse(path).getroot().findall("data"):
            checked += 1
            value = node.findtext("value") or ""
            matches = find_transliterations(value)
            if matches:
                failures.append(f"{path.relative_to(ROOT)}:{node.attrib['name']}: {', '.join(matches)}")
    if failures:
        print("\n".join(failures))
        return 1
    print(f"German resource audit passed for {checked} values.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
