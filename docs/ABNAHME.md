# Verifikation und Abnahmegrenzen

## Durchgeführte Prüfungen

Die Ergebniszusammenfassung steht zusätzlich maschinenlesbar in test-results.json. Ausführliche lokale TRX-/JSON-Ergebnisse entstehen unter .artifacts/. Diese Laufzeitverzeichnisse enthalten auch geschützte Test-Zugangsdaten und gehören weder in Git noch in ein öffentliches Artefakt.

| Prüfung | Ergebnis und Nachweis |
|---|---|
| Locked Restore und Build | Erfolgreich, exakt gebundene NuGet-Abhängigkeiten; keine C#-Buildwarnungen |
| Unit-/Adaptertests | **17 bestanden, 0 fehlgeschlagen, 0 übersprungen** |
| Offizieller ModelCompiler 2.8.15 | Nicht leere Klassen-/uanodes-Dateien erzeugt; SHA-256-Abgleich beider Original-NodeSets erfolgreich |
| Native Prozessintegration | **87 Prüfungen bestanden**, getrennte REST-, Gateway- und OPC-UA-Clientprozesse |
| Container-Build | Gateway- und Demo-API-Images tatsächlich gebaut; SDK-SHA-512 und Debian-Paketprüfung beibehalten |
| Compose-Demo | Beide Dienste tatsächlich gestartet, Healthchecks erfolgreich; Gateway ohne Root-Rechte |
| Chromium | Einrichtung, Login, Konfiguration, API-Verbindungstest, Secret-Nichtausgabe, UA-Benutzer und Zertifikatsfreigabe erfolgreich; keine JavaScript-Ausnahmen |
| OPC UA über Docker-Port | Eigenständiger authentifizierter Client mit **SignAndEncrypt**, explizitem Vertrauen und aktiver Hostnamenprüfung |
| Container-Neustart | NodeId-Menge identisch; Konfiguration, Zertifikate, Benutzer, API-Key und Websession erhalten |
| HTTPS-Compose | Zertifikats-/Hostnamenprüfung und Secure-CSRF-Cookie geprüft; Prozess- und Datenquellen-Health getrennt |
| Lokale API-CA | Echter lokaler Kestrel-TLS-Server: konfigurierte CA akzeptiert; fehlendes Vertrauen und falscher Hostname abgewiesen |

Die dokumentierten Startbefehle werden außerdem durch scripts/startup-smoke.py geprüft. Es benötigt freie Defaultports; eigene Demo-Container vorher anhalten. Das Skript führt scripts/run-demo.sh tatsächlich aus und prüft drei Simulatorgeräte, Prozess-/Datenquellen-Bereitschaft, Ersteinrichtung und das geordnete Beenden beider Prozesse.

## Was die native Integration nachweist

- Webadministration verlangt Anmeldung; fehlender CSRF-Token wird vor Einrichtung und bei späteren Aktionen abgewiesen.
- Export enthält weder API-Key noch Passwort-Hashes; der Key liegt verschlüsselt vor; falsche Konfiguration wird vor Aktivierung abgewiesen.
- Der OPC-UA-Client erzeugt ein eigenes Zertifikat. Vor Freigabe wird er abgewiesen; anschließend baut er eine reale verschlüsselte UA-TCP-Session auf.
- Browse liefert die offiziellen TypeDefinitions der drei ausgewählten Klassen und die geerbten DI-/HKI-Pflichtknoten.
- Mehrere Kochzonen, Read-Ergebnisse, Datenänderungs-Subscriptions, EngineeringUnits mit UN/CEFACT Celsius-Code und ConditionRefresh funktionieren.
- Fehlender Pflichtzustand ist nicht Off/Good. Unbekannte HACCP-Historienparameter bleiben BadNoData.
- Ein tatsächlicher Write-Service-Aufruf liefert **BadNotWritable**.
- Fehlende Felder, falsches Format, leere Reihe, alte Messzeit, ungültiges JSON und API-Ausfall sind über echte OPC-UA-Reads getrennt erkennbar.
- Bei wiederholtem Abruf eines alten Datenpunkts bleibt SourceTimestamp exakt unverändert.
- Einzelner Gerätefehler und fehlendes Inventargerät beeinträchtigen andere Geräte nicht.
- 401, 403, 429/Retry-After und 503 werden kontrolliert behandelt. Nach 503 und Rate Limit erfolgt Wiederherstellung ohne Neuanlage der Instanzen.
- Gateway-Kommunikationsalarme werden über Event-Subscription ausgelöst und nach Wiederkehr zurückgesetzt. Historische Gerätefehler werden dafür nicht verwendet.
- Geordnete Prozessneustarts behalten NodeIds und persistierte Zugänge.

Die Eventprüfung wartet auf eine tatsächliche Subscription-Bereitschaftsmeldung des externen Clients, bevor die Störung injiziert wird. Eine feste Wartezeit wäre kein zuverlässiger Nachweis einer eingerichteten Subscription.

## Wiederholung

~~~bash
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test tests/Gateway.Tests/Gateway.Tests.csproj --no-build --no-restore
bash scripts/check-models.sh
python3 scripts/integration.py
~~~

Integration verwendet Web 15080, API 15090, UA 14840. TEST_WEB_PORT, TEST_API_PORT und TEST_UA_PORT erlauben andere Ports. Alle gestarteten Prozesse werden beendet. Der Test aktualisiert keine echten Geräte.

Für den optionalen Browser-Smoke-Test Playwright und Chromium bereitstellen und eine **frische, separate** Compose-Demo starten. Dann node scripts/browser-smoke.cjs ausführen. Das Skript verwendet zufällige Laufzeitkennwörter; es darf nicht auf eine bereits administrierte Kundeneinrichtung gerichtet werden. Bilder entstehen erst nach Anmeldung und nach Leeren der Secret-Eingabefelder und enthalten nur Simulationsdaten.

## Reale Software und Simulation

**Reale Software-Interaktion:** C#-Kompilierung, Paketauflösung, ModelCompiler, offizieller SDK-Server, UA-TCP-Verschlüsselung, Zertifikats-/Hostnamenprüfung, OPC-UA-Client, Browser/HTTP(S), Container, Persistenz und Shutdown wurden tatsächlich ausgeführt.

**Simuliert:** ConnectedKitchen-REST-Daten, Geräteidentitäten, Stammdaten, Temperaturen, Zeiten und API-Ausfälle. Demo-Feldbedeutungen sind allein im Simulator definiert, kein Nachweis für reale Optima-Geräte.

**Nicht geprüft:** reale ConnectedKitchen-Zugangsdaten, reale Großküchengeräte, echte Geräteaktionen, vollständige Last-/Dauertests, Windows/macOS, OPC-Zertifizierung und vollständige HKI-Konformität. Es wurden keine schreibenden Tests gegen reale Geräte ausgeführt.

## Verbleibende Grenzen

Fritteusen sind produktiv gesperrt, solange Pflichtzustände fehlen. Kochstellen-/Grill-Profile benötigen bestätigte Typ-/Zonen-/Einheitenbelege und vollständige Stammdaten. Aktive physische Alarme und HACCP-Historienmetadaten sind weiterhin offen. Ein Pflichtadressraum mit Fehlerstatus ist ausdrücklich kein vollständiger Konformitätsnachweis.

Die Grenzen sind in UI, Herstellerdiagnose, Mapping und OFFENE-PUNKTE.md sichtbar. Die Anwendung erzwingt keine scheinbare Bidirektionalität über ungeeignete API-Funktionen.
