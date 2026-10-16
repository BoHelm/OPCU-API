# ConnectedKitchen → HKI / OPC-UA-Gateway

Eine tatsächlich implementierte C#/.NET-10-Anwendung mit ASP.NET-Core-Konfigurationsoberfläche, zentralem REST-Poller, offiziellem OPC-UA-Stack, persistenter Konfiguration und lokaler ConnectedKitchen-Simulation.

**Stand: lesender, fachlich begrenzter MVP. Keine OPC-Zertifizierung und kein vollständiger HKI-Konformitätsnachweis.** Die API dokumentiert keinen passenden Rückkanal für laufende Sollwerte, Start/Stop oder Geräte-Alarmquittierungen. Alle Gerätevariablen sind deshalb lesend; ein vorbereiteter Dispatcher lehnt unbelegte Operationen ab.

## Schnellstart: vollständige lokale Simulation

Voraussetzungen: Linux, .NET SDK **10.0.402**, Bash und curl. In der eingerichteten Codex-Cloud vorher:

~~~bash
source /workspace/.toolchains/activate.sh
cd /workspace/OPCU-API
bash scripts/run-demo.sh
~~~

Die Weboberfläche läuft lokal auf HTTP-Port **5080**, OPC UA auf **4840**; die simulierte REST-API auf **5090**. Im Browser die lokale Adresse mit Port 5080 öffnen. Die Cloud-Onboarding-Oberfläche stellt dafür keinen Web-Preview bereit.

1. Den Einrichtungstoken aus **.keys/demo-run/bootstrap-token** lokal lesen. Er wird nicht über das Web oder in Logs ausgegeben.
2. Über „Ersteinrichtung“ einen Webadministrator mit mindestens 12 Zeichen langem Passwort anlegen.
3. Anmelden; API-Verbindung testen und Datenqualität prüfen.
4. Einen separaten OPC-UA-Benutzer anlegen.
5. Öffentliches Gateway-Zertifikat im OPC-UA-Client freigeben; öffentliches Client-Zertifikat in der Gateway-Vertrauensliste freigeben. **SignAndEncrypt / Basic256Sha256** verwenden.

Das Startskript erzeugt einen zufälligen Simulations-API-Key zur Laufzeit. Es enthält keine fest eingebauten Zugangsdaten und beendet beide gestarteten Prozesse bei Abbruch.

Alternativ mit Docker:

~~~bash
docker compose -f compose.demo.yaml up --build -d
docker compose -f compose.demo.yaml exec gateway cat /keys/gateway/bootstrap-token
docker compose -f compose.demo.yaml ps
~~~

Die Compose-Demo veröffentlicht Web und OPC UA ausschließlich auf Host-Loopback. Web-HTTP ist in dieser Datei **ausdrücklich als lokale Demo-Ausnahme** aktiviert; OPC UA bleibt verschlüsselt und authentifiziert. Die Simulation wird im UI deutlich angezeigt und hat keinen technischen Pfad zu realen Geräten.

## Verifikation

~~~bash
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test tests/Gateway.Tests/Gateway.Tests.csproj --no-restore
bash scripts/check-models.sh
python3 scripts/integration.py
~~~

Der Integrationstest startet eigene Prozesse auf Ports 15080, 15090 und 14840, richtet die Webadministration ein und verbindet einen **eigenständigen OPC-UA-Clientprozess**. Ergebnisse liegen in .artifacts/. Die Tests betreffen simulierte Geräte, keinen realen ConnectedKitchen-Mandanten. Einzelheiten: [Abnahme](docs/ABNAHME.md).

## Geräteumfang und Grenzen

| Profil | Implementiert | Produktive Aktivierung |
|---|---|---|
| Kochstellen / CookingZone | Offizieller Typ, geerbte Pflichtknoten, mehrere Zonen, Nennspannung/Phasen/Nennleistung, belegbare optionale Felder | Nach bestätigter Profilwahl und vollständigen Pflicht-Stammdaten; Feldbedeutungen/Einheiten explizit belegen |
| Brat-/Grillgeräte | Offizieller Typ, Zonen und Pflichtfeld IsWithPlaten, optionale Temperatur-Platzhalter | Nach bestätigter Profilwahl und vollständigen Pflicht-Stammdaten |
| Fritteuse | Offizieller Typ und alle Pflichtknoten; Temperatur-/Zeitpfad in der Simulation | **Gesperrt**, solange ProgramMode und SignalMode durch die API nicht belegt sind |

Auch bei den aktivierbaren Profilen bleiben aktive physische Gerätealarme und geräteseitige HACCP-Historienparameter offen. Das begrenzt die Konformitätsaussage. ErrorConditions und InformationConditions sind vorhanden; leere Container werden ausdrücklich **nicht** als Beweis fehlerfreier Geräte ausgegeben. Der eigene Kommunikationsalarm liegt im Hersteller-Namespace.

Ein lokales Gateway kann eine lokale ConnectedKitchen-Installation verwenden. Ein Gateway gegen **https://connectedkitchen-eu.cloud** bleibt von dieser Cloud und der Internetverbindung abhängig. Es gibt keine pauschale Offline-Garantie.

## Dokumentation

- [Quellenprüfung, Versionen und Technologieentscheidung](docs/QUELLEN-UND-ENTSCHEIDUNG.md)
- [Architektur und Quellcode-Verantwortung](docs/ARCHITEKTUR.md)
- [Installation, Container und lokale Bereitstellung](docs/INSTALLATION.md)
- [Konfiguration, Geräteprofile und Sicherheit](docs/KONFIGURATION.md)
- [Mapping, Pflichtumfang und Datenqualität](docs/MAPPING.md), [vollständige Deklarationsmatrix](docs/mapping.csv)
- [Betrieb, Zertifikate, Backups und Wiederanlauf](docs/BETRIEB.md)
- [Testergebnisse und Abnahmegrenzen](docs/ABNAHME.md)
- [Offene fachliche Punkte und API-Erweiterungen](docs/OFFENE-PUNKTE.md)
- [Drittanbieterhinweise](THIRD-PARTY-NOTICES.md)

Der eigene Quellcode liegt unter src/, tests/ und tools/. Unveränderte offizielle Modelle liegen separat in models/upstream/. Vom ModelCompiler erzeugter Code landet ausschließlich unter .artifacts/generated/ und wird nicht mit dem handgeschriebenen Anwendungscode vermischt.
