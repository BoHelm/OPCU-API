# Konfiguration und Sicherheit

## Ersteinrichtung

Ohne vorhandenen Webadministrator erzeugt das Gateway im getrennten Key-Verzeichnis einen zufälligen bootstrap-token. Er ist nur lokal/über den autorisierten Containerbetreiber lesbar und wird nach erfolgreicher Einrichtung gelöscht. Die Weboberfläche selbst zeigt ihn nicht an. Ersteinrichtung und Anmeldung sind rate-begrenzt (zehn Versuche pro Minute für diese Endpunkte), benötigen CSRF-Token und erlauben Passwörter mit 12–256 Zeichen.

Passwörter werden mit PBKDF2-SHA256, 210.000 Iterationen und individuellem 128-Bit-Salt gespeichert. Der Vergleich ist zeitkonstant. Die Websession ist HttpOnly, SameSite=Strict und auf acht Stunden begrenzt; unter HTTPS ist das Cookie Secure. Ein persistierter separater Data-Protection-Keyring erhält Sessions über Neustarts. Seine Dateien sind geheimes Schlüsselmaterial und gehören ins Key-Backup.

HTTP-Administration ist standardmäßig nur aus Loopback zulässig. Für externe Aufrufe HTTPS konfigurieren. GATEWAY_ALLOW_HTTP=true ist eine ausdrückliche Ausnahme für isolierte lokale Tests; keine allgemeine Produktivvorgabe. Ungeprüfte Forwarded-Header werden nicht als HTTPS-Nachweis vertraut.

## Aktivierung und Speicherung

gateway.json enthält ausschließlich nicht geheime Einstellungen, Geräte-GUIDs, Zuordnungen und bestätigte Stammdaten. Änderungen werden validiert und atomar ersetzt. Der laufende Poller/OPC-UA-Adressraum nutzt einen unveränderlichen Snapshot:

| Änderung | Wirksamkeit |
|---|---|
| API-URL, lokale CA-Datei, Intervalle, Veraltungsgrenze, Historienfenster | Neustart |
| Profil, Geräte, Zonen, Stammdaten, Bindungen, EURanges | Neustart |
| OPC-UA-Bind-Adresse, Port, angekündigte URL | Neustart; bei DNS-/Zertifikatsänderung Zertifikate mitprüfen |
| API-Key ersetzen | Nächster REST-Abruf; laufender Backoff bleibt bis zum geplanten Versuch bestehen |
| OPC-UA-Benutzer/Passwort/Rolle | Nächste Anmeldung; vorhandene Sitzungen bleiben bestehen |
| Vertrauensliste | Neue Verbindungen nach Reload; Sicherheitsvorfälle erfordern zusätzlich das Beenden bestehender Sitzungen |
| Zertifikatsdateien manuell ersetzen | Geordneter Neustart und erneute Zertifikatsprüfung |

Ein neuer Schlüssel oder eine gespeicherte URL führt nicht zu einem verdeckten sofortigen Befehlsaufruf an Geräte. Der Verbindungstest ist GET /api/devices gegen die **aktive** Konfiguration. Eine nur gespeicherte neue Basis-URL muss zuerst per Neustart aktiviert werden.

## Verbindung

- Standard-Cloud-URL: https://connectedkitchen-eu.cloud. Der api-key-Header wird nur an den konfigurierten Ursprung gesendet.
- Lokale Installation: alternative HTTPS-Basis-URL eintragen und DNS/Erreichbarkeit vom Gateway prüfen. Loopback-HTTP ist nur für eine tatsächlich lokale Installation vorgesehen.
- Lokale CA: Pfad zu einem öffentlichen Zertifikat im Gateway-Dateisystem. Es wird eine eigene Chain mit diesem Trust Anchor geprüft; Hostname/SAN und Zertifikatsgültigkeit bleiben relevant. In diesem lokalen CA-Profil wird kein externer CRL-Dienst verlangt; die Verantwortung für Sperrung/Rotation liegt beim Betreiber. Niemals TLS-Verifikation deaktivieren.
- Redirects sind abgeschaltet, damit der API-Key nicht zu einem fremden Host weitergegeben wird.
- Default: Inventar 300 s, Istwerte 10 s, Sollwerte 30 s, Veraltung 120 s, Historienfenster 3600 s, Timeout 15 s, maximal vier parallele Requests.
- Das Historienfenster muss mindestens so groß wie die Veraltungsgrenze sein und ist auf sieben Tage begrenzt. Höchstens 100 konfigurierte Geräte und 32 Zonen je Gerät; keine Lastgarantie für jedes Maximum.

Die API dokumentiert kein verbindliches Rate-Limit. Werte konservativ wählen. 429/Retry-After steuert eine gemeinsame Abrufsperre; Fehler anderer Art verursachen begrenzten exponentiellen Backoff je Job. Nach API-Key-Korrektur kann ein Neustart einen berechtigten neuen Versuch früher erlauben; ein fehlender Variablenname allein beweist keine fehlende Authentifizierung.

## Geräteauswahl und HKI-Profil

„Geräte aus aktivem API-Inventar anzeigen“ holt GET /api/devices. Ein neues Gerät wird als **deaktiviertes** Profil übernommen. Nur Model und SerialNumber können als überprüfbarer Inventarsnapshot vorausgefüllt werden. Numerische type-/energySource-/isWithLift-Werte werden nicht dekodiert.

1. Physisches Gerät und Geräteklasse anhand Herstellerunterlagen bestätigen.
2. Profil CookingZone oder Grill wählen und den Beleg mitführen.
3. Alle geerbten DI-Pflichtstammdaten und die Klassenpflichtfelder ausfüllen.
4. Tatsächliche API-Zonen nummerieren. Die Zahl ist eine explizite Quellzuordnung, keine erratene Beckenposition.
5. Nennleistung/Nennspannung/Fähigkeiten und gerätespezifische EURanges bestätigen.
6. Nur belegte optionale dynamische Felder aktivieren; ungesicherte Felder fehlen im Adressraum.
7. Validieren, speichern, geordnet neu starten und mit einem separaten OPC-UA-Client prüfen.

Fryer ist produktiv gesperrt. Es fehlen verbindliche Quellen für ProgramMode und SignalMode. Der Demo-Modus zeigt diese Pflichtknoten mit Fehlerstatus, ohne vollständige Konformität zu behaupten.

DI-Pflichtfelder: Manufacturer, Model, SerialNumber, HardwareRevision, SoftwareRevision, DeviceRevision, DeviceManual, RevisionCounter. Dazu kommen DeviceClass (aus dem Profil), ErrorConditions und InformationConditions. Statische Nullwerte sind keine unbekannten Stammdaten; fehlende Pflichtdaten verhindern produktive Aktivierung.

## Feldbindungen

Die Weboberfläche stellt die seltenen fachlichen Bindungen als prüfbares JSON dar. Beispielstruktur, **kein Beleg für ein reales Gerät**:

~~~json
{
  "target": "ActualTemperature",
  "group": "Actual",
  "field": "actualTemperature",
  "unit": "°C",
  "evidence": "Hier eine konkrete, freigegebene Herstellerquelle mit Geräteklasse und Sensor-/Zonenbedeutung nennen."
}
~~~

group ist Actual oder Set und bestimmt ausschließlich die beiden dokumentierten GET-Zeitbereichs-Endpunkte. field muss in deren dokumentiertem Schema stehen. target muss eine Variable oder ein konkreter numerischer Platzhalter des gewählten HKI-Zonentyps sein. Einheit und EURange müssen zum Ziel passen; Zustandsquellen sind in dieser API-Fassung nicht freigegeben. Eine statische Konstante für ProgramMode, SignalMode, Temperaturen, Zeiten oder andere dynamische Werte wird abgewiesen.

Eine Bindung darf nicht zugleich als StaticValues-Eintrag oder geräteseitig nicht unterstützt markiert sein. DeviceUnsupported ist ausschließlich für nachweislich fehlende Gerätefähigkeiten gedacht, nicht als Abkürzung für eine fehlende API-Zuordnung.

## OPC-UA-Netzwerk

UaBindAddress begrenzt den Listener (z. B. 127.0.0.1 oder 0.0.0.0), UaPort ist der TCP-Port. UaEndpointUrl ist die für Clients erreichbare angekündigte Adresse mit DNS/NAT-Hostname und demselben Port. Der Pfad des Listeners ist /kitchen. Gateway, Client und Zertifikats-SAN müssen diesen Namen konsistent verwenden.

Das Gateway verwendet SignAndEncrypt/Basic256Sha256. Kein automatisches Vertrauen unbekannter Clientzertifikate. Die eigene Server-Anwendungs-URI enthält eine persistente Gateway-GUID. Zertifikate werden bei Ersteinrichtung erzeugt und danach wiederverwendet/geprüft.

Die Rollen Observer, Operator und Administrator werden getrennt gespeichert. Im aktuellen lesenden Geräteumfang erhalten alle erfolgreichen UA-Anmeldungen nur Observer-Leserechte. Operator-/Administrator-Bezeichnungen sind vorbereitete Rollen für zukünftige geprüfte Befehle, keine heutigen Schreibrechte. Webadministrator ist keine automatisch gültige OPC-UA-Identität.

## Secret-Verwaltung und Import/Export

API-Key: AES-256-GCM mit zufälligem Nonce und Authentifizierungstag in api-key.enc. master.key liegt **außerhalb** des Datenverzeichnisses im Key-Verzeichnis. Auf Unix sind Daten-/Key-Verzeichnisse 0700 und eigene Secret-Dateien 0600. Die Anwendung gibt nur apiKeyConfigured zurück. Sie sendet keinen gespeicherten Schlüssel an den Browser zurück und schreibt ihn nicht ins Log.

Die getrennte Schlüsseldatei ist ein konkreter lokaler Schlüsselverwaltungsmechanismus, kein externer Vault. Für Produktion: getrennte Zugriffs-/Backupregeln, verschlüsseltes Key-Volume beziehungsweise Secret-Manager-Mount und Kontrolle der Laufzeitidentität. Wer Zugriff auf laufenden Prozess und Master-Key hat, kann entschlüsseln. Ein verschwundener Master-Key wird zu bestehenden verschlüsselten Daten nicht still durch einen neuen ersetzt.

Der JSON-Export enthält weder API-Key noch Passwort-Hashes, Data-Protection-Keys oder private Zertifikate. Import nutzt denselben Validator. GatewayId muss zur Installation passen; bestehende Geräteidentitäten dürfen nicht beiläufig umnummeriert werden. Ein Vollbackup ist deshalb etwas anderes als ein Konfigurationsexport.
