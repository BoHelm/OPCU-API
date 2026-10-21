# Abgleich der Projektstände vom 21. Oktober 2026

## Arbeitsgrundlage

Die vom Projektteam bereitgestellte Datei **MKN_OPC_UA_Helm.zip** ist die neue fachliche Arbeitsgrundlage. SHA-256: `e2b80da705c409fc8b33fb1201e4b895f744ea5ad65435af993cd57200f898ee`. Der Vergleich erfolgte gegen GitHub-Commit `9173b54bd5e3cbb83cbca43ed9358e87074fc9e8`. Die ZIP enthielt 58 Dateien; sechs vorhandene Quelldateien waren geändert, zwei Dateien kamen hinzu. Es fehlte keine zuvor versionierte Datei. Offizielle NodeSets, API-Beschreibung, Paketbindungen und Geräteprofile waren unverändert.

Die ZIP selbst wird nicht zusätzlich versioniert; ihre integrierten Quelldateien sind direkt im Repository prüfbar. Vor dem Zusammenführen wurden die noch unversionierten lokalen Arbeiten separat außerhalb des Repository gesichert.

## Übernommene Änderungen des Projektteams

- DiagnosticsLog.cs: begrenztes RAM-Protokoll mit bereinigten Exception-Details.
- ConnectedKitchen.cs: REST-Aktivitäten und Polling-/Mapping-Diagnose, verständlichere Netzwerk-/TLS-Fehler.
- OpcUaServer.cs: benannte Startphasen, Ursachen/Stacktraces und Abschaltdiagnose.
- Program.cs: Registrierung des Protokolls, geschützter Endpunkt /api/logs und Konfigurationsmeldungen.
- Weboberfläche: Live-Verlauf mit Filtern, Pause und aufklappbaren Fehlerdetails.
- AGENTS.md: unverändert übernommen.

## Zusammengeführte Korrekturen

- Visual-Studio-Projektprofile und gemeinsames Solution-Startprofil für Gateway plus DemoApi; Browserstart, Loopback-Adressen und getrennte Demo-/Echtkonfiguration.
- Fehlende Simulationsschlüsseldatei beim parallelen Start beendet das Gateway nicht mehr. Der Poller verbindet sich mit der später gestarteten Demo-API, ohne einen Gateway-Neustart zu verlangen. Dieser Pfad bleibt auf den ausdrücklichen Demo-Modus beschränkt.
- Herstellerneutrale Standard-URIs und konfigurierbare Softwareanbieterangaben; offizielle HKI-/DI-URIs unverändert. Bestandskonfigurationen behalten ihre bisherigen Node-/Zertifikatsidentitäten durch Migration.
- Separater OPC-UA-Client findet den Devices-Einstieg per Browse und funktioniert auch mit einem ausdrücklich konfigurierten eigenen Namespace.
- Visual-Studio-Lokaldateien und bisherige Standard-Schlüsselverzeichnisse werden zusätzlich durch .gitignore ausgeschlossen.
- Die Compose-Demo setzt Proxy-Ausnahmen ausschließlich für Loopback und ihren internen Simulatornamen. Damit funktionieren neue Demo-Container auch bei automatisch injizierten Unternehmens-Proxys.

Es gibt weiterhin keine erfundenen Geräte-Schreibaktionen. Modellierungsumfang, Datenqualität und Konformitätsgrenzen bleiben wie in MAPPING.md und OFFENE-PUNKTE.md dokumentiert.

## Verifikation und Grenzen

Aktuelle Ergebnisse stehen in ABNAHME.md und test-results.json. Build, Tests und reale Netzwerkinteraktionen werden mit simulierten ConnectedKitchen-Geräten geprüft. Die Startprofile werden über die .NET-CLI unter Linux geladen; eine Visual-Studio-/Windows-Abnahme und ein Test an realen Geräten werden ausdrücklich nicht behauptet. Die konkrete ursprüngliche Visual-Studio-Fehlermeldung wurde nicht geliefert; die Startkorrektur behebt eine reproduzierbare Konfigurations-/Startlücke, beweist aber keine Diagnose eines unbekannten lokalen Fehlers.
