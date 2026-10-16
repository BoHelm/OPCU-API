# Offene fachliche Punkte und Produktivfreigabe

Die folgenden Punkte sind sichtbar gehaltene Quellenlücken, keine erfundenen Implementierungsannahmen:

| Benötigte Information | Gegenwärtige Behandlung | Freigabevoraussetzung |
|---|---|---|
| Numerische API-Gerätetypen und Optima-Unterklassen | Keine automatische Klassifizierung | Herstellerbestätigte Tabelle; bis dahin explizite administrative Profilwahl |
| API-Zonennummern und Sensorpositionen | Explizite Zone/Binding mit Beleg | Gerätespezifischer Becken-/Zonenvertrag |
| Temperatureinheiten und genaue Bedeutung der ActualValues-Felder | Reale Bindungen nicht automatisch aktiviert | Freigegebene Feld-/Sensor-/Einheitentabelle |
| Einheit der Int64-Zeitwerte | Nur Simulator bestätigt Sekunden | Belegte s/ms-Semantik und Wertebereiche |
| API-Energie-/Lift-Codes | Kein numerisches Enum-Casting | Offizielle Codebelegung oder bestätigte Stammdaten |
| Fritteusen-ProgramMode und SignalMode | Pflichtknoten vorhanden, produktive Aktivierung gesperrt | Aktueller, zeitgestempelter Zustandsvertrag mit eindeutigen HKI-Zuordnungen |
| Kochstellen-/Grillzustände | Optionale Knoten ausgelassen | Eindeutige aktuelle Zustandscodes, unbekannte Werte müssen Fehlerstatus erhalten |
| Aktive Fehler-/Hinweisbedingungen | Keine Umdeutung der historischen error-history | Stabile Ereignis-ID, Beginn, Ende, Aktivzustand, Severity und Aktualisierungs-/Refresh-Vertrag |
| DeviceHealth | Nicht aus error-Boolean abgeleitet | Eindeutige fünfwertige HKI-/DI-Zuordnung |
| HACCP-Aufzeichnungsdauer/-Sampling/-Aggregationsparameter | Struktur vorhanden, unbekannte Metadaten BadNoData | Verlässliche geräteseitige Historienkonfiguration und Entscheidung über gegebenenfalls erforderliches HistoryRead |
| Einzelne Namespace-/PDF-Metadatenabweichungen und Kühlgeräte-Ergänzung | Gewählter XML-Stand explizit dokumentiert | Verbindliche freigegebene NodeSet-/DIN-Kombination durch Modellverantwortliche |
| Rate Limits / tatsächliche Live-Datenlatenz | Konfigurierbares Polling, Retry-After und Staleness | Produktionsvertrag und Messung mit realer Installation |

## Was einen echten Rückkanal ermöglichen würde

Die API müsste für eine tatsächlich im HKI-Profil erlaubte Funktion einen eindeutigen Befehl spezifizieren, etwa mit:

- dokumentiertem Endpoint, Berechtigung und Gerätefähigkeit;
- verifizierbarem Parameter-/Einheiten-/Wertebereichsvertrag;
- Idempotenzschlüssel oder anderem belastbaren Schutz gegen Doppelausführung;
- separater Auftragsannahme und Bestätigung durch das physische Gerät;
- Befehls-ID, Ausführungsstatus, Fehler- und Timeoutsemantik;
- bei Alarmquittierung stabiler Bedingungs-/Branch-/Ereignisidentität und Rückmeldung des tatsächlichen AckedState.

Diese Liste definiert einen benötigten Vertrag; sie erfindet keine heute existierenden API-Funktionen. Erst danach WriteDispatcher, Rollen, HKI-Zugriffsrechte, Persistenz offener Befehle und positive/negative Integrationstests zusammen implementieren. Ein PUT auf Programme/Regionen oder GET setValues ersetzt diesen Vertrag nicht.

## Vor Produktivbetrieb

Reale Gerätequelle und deren Zertifikatsvertrauen einrichten, Pflichtstammdaten und Profil-/Zonenbelege bestätigen, SourceTimestamp-/Latenzverhalten messen, notwendige Mapping-Lücken schließen und die resultierende Geräteklasse erneut fachlich abnehmen. Die lokale Simulation reicht als Nachweis für Softwarepfade, nicht für reale Sensor-/Gerätebedeutungen.

Die aktuelle Anwendung ist weder OPC-zertifiziert noch vollständig nach HKI abgenommen. Kochstellen-/Grill-Grundstrukturen sind aktivierbar; aktuelle physische Alarme und HACCP-Metadaten begrenzen auch dort die Konformitätsaussage. Fritteusen bleiben produktiv gesperrt. Kochbuch-/Firmwareverwaltung, vollständiger Historian und weitere Geräteprofile gehören nicht automatisch zu diesem MVP.
