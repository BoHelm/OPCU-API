# Betrieb und Diagnose

## Health und Datenqualität

| Pfad | Aussage | HTTP |
|---|---|---|
| /health/live | ASP.NET-Prozess läuft | 200 |
| /health/ready | OPC-UA-Server wurde initialisiert | 200 oder 503 |
| /health/source | Bisherige REST-Jobs erreichen ihre Datenquelle | 200 oder 503 |

Die öffentlichen Health-Endpunkte geben keine Zugangsdaten und keine Gerätelisten preis. Ein 200 bei ready bedeutet nicht, dass die Quelle erreichbar ist oder jeder Messwert Good hat. Authentifizierte Diagnosen zeigen pro Gruppe die letzte erfolgreiche Abfrage und pro Knoten Messzeit, Status und Grund. Leere/alte Daten können bei erreichbarer API weiterhin BadNoData/Uncertain sein.

## Netzwerk und DNS

- OPC UA: konfigurierbarer TCP-Port, Default 4840. Listeneradresse und angekündigte Endpoint-URL sind getrennt.
- Web lokal: 5080; Container intern 8080/8443 je Betriebsart. Produktive Administration über HTTPS.
- Demo REST: lokal 5090; in Compose nur internes Netz.
- Ausgehend: nur die konfigurierte ConnectedKitchen-Installation und gegebenenfalls ihr Trust-/PKI-Betriebsumfeld. Ohne API-Erweiterungen keine Geräte-POSTs.
- Build: Docker-Hub/Debian-Paketquellen, builds.dotnet.microsoft.com, api.nuget.org und Paket-CDNs. Diese Ziele sind keine Laufzeitpflicht im lokalen Kundensystem.

NAT-Portweiterleitung, UaEndpointUrl und Zertifikats-SAN müssen dieselbe für den Client erreichbare Adresse beschreiben. Ein Bind auf 0.0.0.0 macht diesen Platzhalter nicht zu einem sinnvollen angekündigten DNS-Namen. Das Gateway schreibt Discovery-Endpoints auf die konfigurierte Ankündigungsadresse um.

## Fehlerverhalten

401/403: API-Key/Zuordnung in der richtigen Installation prüfen; mindestens 60 s Backoff. Keine Zugangsdaten aus Browser, URLs oder Logtext extrahieren. Nach sicherem Ersetzen wirkt der neue Key beim nächsten Versuch.

429: Retry-After beachten; gemeinsame Sperre aller neuen REST-Anfragen. Ein laufender SDK-Read löst keinen zusätzlichen REST-Request aus.

5xx/Verbindungsfehler: begrenzter Backoff je Gerät/Zone/Gruppe, höchstens fünf Minuten aus eigener Exponentialberechnung; ein längerer Retry-After bis maximal einem Tag hat Vorrang. Andere Geräte bleiben unabhängig.

Leere Reihe/fehlendes Feld: BadNoData. Ungültiges JSON/Format: BadDecodingError. Fehlendes einzelnes Inventargerät: BadNotConnected für dessen dynamische Werte. Alte letzte Werte: Quellzeit bleibt erhalten, Status wird semantisch Uncertain. Die vollständige Zuordnung steht in MAPPING.md.

Logs enthalten keine API-Header oder Response-Bodies. Produktive Logging-Stufen so wählen, dass unnötige Geräte-/Standortmetadaten nicht in zentrale Systeme gelangen. Browser-Passwörter und Schlüssel werden nie für Diagnose-Screenshots benötigt.

## Zertifikate

Im Datenverzeichnis unter pki/: own, trusted, issuers, rejected. own enthält private Schlüssel und gehört zum geschützten Vollbackup. Das öffentliche Serverzertifikat kann über die Administration heruntergeladen werden; private Zertifikate werden nie über die Weboberfläche ausgegeben.

Client-Zertifikate vor der Freigabe anhand Fingerprint, Herausgeber, SAN und Laufzeit prüfen. Im UI eigene DER-Datei hochladen; unbekannte Zertifikate werden nicht automatisch akzeptiert. Die Rejected-Liste dient der Diagnose. Entfernen aus der Trust-Liste betrifft neue Verbindungen; bei kompromittierten Clients bestehende Sitzungen zusätzlich durch geordneten Gateway-Neustart beenden.

DNS-Wechsel kann einen kontrollierten Anwendungszertifikatswechsel verlangen. Bestehendes Zertifikat zuerst sichern, Endpoint/SAN/Anwendungs-URI abstimmen, im Wartungsfenster ersetzen und Clientvertrauen neu verteilen. Keine beliebige Trust-Autoaccept-Option in Produktion einschalten.

## Persistenz und Backup

Separat sichern:

1. **Datenvolume**: gateway.json, api-key.enc, Web-/UA-Account-Hashes, pki einschließlich privater UA-Schlüssel.
2. **Key-Volume**: master.key und web/-Data-Protection-Keyring. Zugriff streng getrennt von gewöhnlichen Konfigurationsexporten.
3. **Web-TLS-Material** und dessen getrennte Passwortdatei/Secret-Manager-Bindung.

Für konsistente Backups Konfigurationsänderungen sperren beziehungsweise im Wartungsfenster sauber stoppen und beide Volumes samt Dateirechten sichern. Backups verschlüsseln und Wiederherstellung praktisch prüfen. Ein JSON-Export ist **kein** Secret-/Zertifikatsbackup.

Bei Restore dieselbe GatewayId, Geräte-GUIDs, Schlüssel und Zertifikate wiederherstellen. Kein paralleler Produktivklon mit derselben Anwendungsidentität. Ein fehlender Master-Key zu bestehendem api-key.enc führt bewusst zu einem klaren Startfehler statt stillem Datenverlust.

SIGTERM/Host-Shutdown beendet Poller und SDK. Es werden keine offenen Transaktionen zu Geräten behauptet; der MVP sendet keine Befehle. Beide Prozess- und Container-Neustarts sind in der Abnahme enthalten.

## Ressourcen

Zur Wiederherstellung eines vergessenen Webzugangs ist lokaler Betreiberzugriff nötig: Gateway stoppen, web-admin.json geschützt sichern und aus dem aktiven Datenverzeichnis entfernen. Beim nächsten Start entsteht ein neuer einmaliger Bootstrap-Token im Key-Verzeichnis. Dies ist keine ungeschützte Web-Resetfunktion; vorhandene Schlüssel/PKI werden dabei nicht gelöscht.

Compose setzt zunächst 1 CPU und 512 MiB RAM für das Gateway. Der Poller begrenzt HTTP-Parallelität und Antwortgrößen (16 MiB je Antwort). Web-Request-Bodies sind auf 2 MiB begrenzt. Diese Einstellungen sind ein Startpunkt für die drei Simulationsgeräte, keine gemessene Garantie für 100 Geräte/200 Subscriptions unter beliebiger Clientlast.

Vor größerer Flotte: erwartete Geräte-/Zonenzahl, API-Rate-Limits, Historienfenster, Client-/MonitoredItem-Zahl, Zertifikatsspeicher und Lastverhalten messen. Ein langer Quellenausfall beendet nicht den SDK-Server; Speicher wächst nicht proportional zur Ausfalldauer, da je gebundener Variable nur der letzte Sample gehalten wird.
