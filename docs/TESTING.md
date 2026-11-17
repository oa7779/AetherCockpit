# Tests

`tests\SystemCockpit.Tests` ist ein STA-Konsolenrunner mit Assertions und
kein `dotnet test`-Projekt. Die Prüfungen sind für Windows vorgesehen.
UI-Modi benötigen einen grafischen Desktop.

## Build und gezielte Regressionen

Vom Repository-Stamm:

```powershell
dotnet build .\tests\SystemCockpit.Tests\SystemCockpit.Tests.csproj -c Release
dotnet run --project .\tests\SystemCockpit.Tests\SystemCockpit.Tests.csproj -c Release --no-build -- --intelligence-fabric-tests
dotnet run --project .\tests\SystemCockpit.Tests\SystemCockpit.Tests.csproj -c Release --no-build -- --admin-platform-tests
dotnet run --project .\tests\SystemCockpit.Tests\SystemCockpit.Tests.csproj -c Release --no-build -- --branding-ui
dotnet run --project .\tests\SystemCockpit.Tests\SystemCockpit.Tests.csproj -c Release --no-build -- --interface-ui
```

Die Intelligence-Fabric-Suite nutzt synthetische Responses für
Claim-/Evidence-, Verfügbarkeits- und Messwertverträge. Sie ist kein echter
Modelllauf und beweist keine allgemeine semantische Korrektheit.

Die Branding- und Interface-Modi instanziieren WPF-Oberflächen und benötigen
einen Desktop. Sie prüfen Ressourcenauflösung und ausgewählte Layout- und
Darstellungsverträge. Sie ersetzen keinen manuellen Sichttest eines
veröffentlichten Portable-Pakets.

## Erweiterte lokale Prüfungen

Der Runner stellt zusätzlich den Guardian-Prüfmodus bereit:

```powershell
dotnet run --project .\tests\SystemCockpit.Tests\SystemCockpit.Tests.csproj -c Release --no-build -- --guardian-tests
```

Diese Regression ist kein Produktfreigabenachweis für Guardian. Die
Funktion befindet sich in Entwicklung und ist keine freigegebene
Produktionsfunktion.

## Echter Modellpfad

Ein echter Modelltest ist keine Routineprüfung. Er setzt eine zuvor
abgeschlossene, ausdrücklich genehmigte lokale Einrichtung mit passender
Runtime und Modell voraus und kann Modellinferenz ausführen. Er darf nicht
mit synthetischen Testantworten oder einem WPF-UI-Test gleichgesetzt werden.
Modellgewichte werden nicht als Teil der Testsuite heruntergeladen.

## Testergebnisse

Assertionzahlen und lokale Laufdaten gehören in den jeweiligen
Entwicklungs- oder CI-Bericht, nicht in eine dauerhaft gepflegte
Produktversionsgeschichte. Vor einer Weitergabe Diagnosen und Testartefakte
auf lokale Pfade und Systeminformationen prüfen.
