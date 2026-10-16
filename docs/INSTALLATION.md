# Installation und Bereitstellung

## Unterstützter und geprüfter Stand

Linux amd64, Debian 13.6, .NET SDK 10.0.402 / Runtime und ASP.NET 10.0.13. Der verwaltete Anwendungscode ist plattformübergreifend aufgebaut (Windows-Dateirechte werden gesondert behandelt); Windows/macOS wurden nicht als Laufzeit abgenommen. Das Containerfile ist bewusst auf amd64 begrenzt.

Das SDK und die NuGet-Pakete sind versionsgebunden. packages.lock.json wird beim Restore nicht neu aufgelöst. Beim Update: offizielle Quellen/Checksummen prüfen und alle Netzwerk-/Persistenztests wiederholen. Keine deaktivierten Signatur- oder TLS-Prüfungen.

## Lokal mit SDK

~~~bash
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test tests/Gateway.Tests/Gateway.Tests.csproj --no-build --no-restore
bash scripts/run-demo.sh
~~~

In der vorbereiteten Codex-Cloud zuerst source /workspace/.toolchains/activate.sh verwenden. Es setzt DOTNET_ROOT, NuGet-/CLI-Verzeichnisse und private XDG-Verzeichnisse in /workspace, weil das Home-Verzeichnis dort schreibgeschützt ist. HOME wird nicht überschrieben.

Produktiv lokal: GATEWAY_DEMO nicht setzen; getrennte GATEWAY_DATA_DIR/GATEWAY_KEY_DIR verwenden und in src/Gateway mit dotnet run beziehungsweise aus dem Publish-Verzeichnis starten. Standardmäßig wird eine leere produktive Konfiguration angelegt. Zuerst Webadministrator, API-Key, bestätigte Profile und Netzwerkeinstellungen einrichten. Ohne API-Key bleibt die Datenquellen-Health fehlerhaft; Webadministration/Prozessdiagnose funktionieren trotzdem.

Publish:

~~~bash
dotnet publish src/Gateway/Gateway.csproj -c Release --no-restore -o .artifacts/publish
~~~

Danach aus diesem Verzeichnis dotnet Gateway.dll starten. Inhalt einschließlich Models/ und wwwroot/ zusammen ausliefern; die Modelldateien müssen für den Laufzeitbenutzer lesbar sein.

## Container-Build

~~~bash
docker build --target gateway -t kitchen-gateway:0.1.0 .
docker build --target demo -t kitchen-demo-api:0.1.0 .
~~~

In der bereitgestellten Cloud mit verschachteltem Docker/Proxy alternativ:

~~~bash
source /workspace/.toolchains/activate.sh
python3 scripts/build-containers.py
~~~

Der Helper übernimmt ausschließlich die vorhandenen standardisierten Docker-Proxyargumente und deren Hostauflösung. Ein optionaler öffentlicher Build-CA wird als BuildKit-Secret eingebunden, nicht als Zugangsdaten in ein Image geschrieben. In anderen Umgebungen ohne diesen Proxy sind die normalen Docker-Befehle ausreichend.

Die Microsoft-Registry leitete in dieser Cloud auf einen nicht erlaubten Artefakthost um. Der geprüfte Default-Build verwendet deshalb:

- debian:13.6-slim mit Digest sha256:d7e12182ce18b85b93007c1dedf31f2d29e01ccf3182cc4017c709b6259bc132;
- signatur-/hashgeprüfte Debian-Runtimepakete;
- das offizielle .NET-SDK-Archiv mit festem SHA-512 aus den offiziellen Release-Metadaten;
- nur dotnet/host/shared und Lizenzdateien im Laufzeitimage; kein SDK;
- einen nicht privilegierten Benutzer app mit UID/GID 1654.

APT-Sicherheitspakete werden aus signierten Distributionsquellen aktualisiert. Damit ist der Build mit festen SDK-/NuGet-/Basisimage-Versionen wiederholbar, aber wegen aktueller signierter Debian-Patchpakete nicht als byteidentischer Rebuild garantiert. Für eine vollständig identische Artefakt-Reproduktion zusätzlich die verwendeten Debian-Paketstände/Repository-Snapshots intern archivieren.

## Compose-Demo

~~~bash
docker compose -f compose.demo.yaml up --build -d
docker compose -f compose.demo.yaml exec gateway cat /keys/gateway/bootstrap-token
docker compose -f compose.demo.yaml ps
docker compose -f compose.demo.yaml down
~~~

Nach Vorab-Build über den Cloud-Helper up -d --no-build verwenden. Der API-Schlüssel wird im privaten benannten Demo-Volume erzeugt. Das Gateway liest ihn einmal zur verschlüsselten Hinterlegung. Die API startet vor dem Gateway und besitzt einen eigenen Healthcheck.

Die Demo-Weboberfläche ist nur auf Host-Loopback-Port 5080 veröffentlicht; OPC UA nur auf Host-Loopback-Port 4840. Den Simulations-Steuerpfad nicht öffentlich exponieren. Die Volumes bleiben bei down erhalten. **Kein down --volumes auf benötigte Konfigurationen anwenden.**

## HTTPS-Bereitstellung mit compose.yaml

Die produktive Compose-Datei erwartet:

- certificates/admin.pfx: privates, passwortgeschütztes TLS-Zertifikat für den Web-DNS-Namen;
- .keys/admin-tls-password: getrennte Passwortdatei, als Docker Secret gemountet;
- Dateien/Verzeichnisse so lesbar, dass UID 1654 sie im Container verwenden kann; äußeres Key-Verzeichnis auf dem Host restriktiv halten.

Für eine rein lokale Entwicklungsprüfung kann bash scripts/create-local-web-certificate.sh ein eigenes, 30 Tage gültiges Zertifikat für localhost erzeugen. Es überschreibt keine vorhandenen Dateien. certificates/admin.crt muss im lokalen Client gezielt vertraut werden. Das ist kein öffentlich vertrauenswürdiges Produktivzertifikat.

~~~bash
bash scripts/create-local-web-certificate.sh
docker compose up --build -d
docker compose exec gateway cat /keys/gateway/bootstrap-token
~~~

Web-TLS wird nur auf Host-Loopback-Port 8443 veröffentlicht. Ein interner HTTP-Health-Endpunkt bindet ausschließlich Container-Loopback 8080. Für externe Administration die TLS-Portbindung und Firewall bewusst auf den gewünschten Verwaltungszugang beschränken. Die TLS-Zertifikatsdateien werden nicht ins Image kopiert.

OPC UA veröffentlicht standardmäßig TCP 4840. Angekündigten DNS-Namen und Zertifikate in der UI konfigurieren, dann neu starten. Eine öffentliche Cloud-REST-URL benötigt Internetzugang; ein lokales ConnectedKitchen-System kann ohne unnötige externe Dienste angebunden werden. Build-/Paketdownload und laufender Datenbetrieb sind getrennte Netzanforderungen.

## Dauerbetrieb

Linux-Systemdienst oder Container-Restartpolicy verwenden. Konfiguration/PKI im Datenvolume, Master-Key/Web-Keyring im separaten Key-Volume persistent halten. Siehe BETRIEB.md für Ports, Backup, Zertifikatswechsel und Wiederanlauf.
