#!/usr/bin/env python3
"""Eigenständige lokale Abnahme: echte HTTP- und verschlüsselte UA-TCP-Verbindungen, ausschließlich simulierte Geräte.

Zugangsdaten werden pro Lauf zufällig erzeugt, nur in ignorierten Dateien mit restriktiven Rechten gespeichert
und weder als Prozessargument noch als Testergebnis ausgegeben. Alle gestarteten Prozesse gehören diesem Lauf.
"""
from __future__ import annotations
import base64
import datetime as dt
import html
import http.cookiejar
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
RUN = ROOT / ".artifacts" / ("integration-" + dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%S"))
RUN.mkdir(parents=True, mode=0o700)
PRIVATE = RUN / "private"
PRIVATE.mkdir(mode=0o700)
WEB_PORT, API_PORT, UA_PORT = (int(os.environ.get(k, v)) for k, v in [
    ("TEST_WEB_PORT", "15080"), ("TEST_API_PORT", "15090"), ("TEST_UA_PORT", "14840")])
WEB, API = f"http://127.0.0.1:{WEB_PORT}", f"http://127.0.0.1:{API_PORT}"
DLL = ROOT / "src/Gateway/bin/Debug/net10.0/Gateway.dll"
DEMO = ROOT / "src/DemoApi/bin/Debug/net10.0/DemoApi.dll"
PROBE = ROOT / "tools/UaProbe/bin/Debug/net10.0/UaProbe.dll"
cookie = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookie), urllib.request.ProxyHandler({}))
plain = urllib.request.build_opener(urllib.request.ProxyHandler({}))
checks = []
processes = []
csrf = None
gateway = None

def check(condition, name):
    if not condition:
        raise AssertionError(name)
    checks.append(name)
    print("PASS:", name, flush=True)

def http(path, method="GET", body=None, *, authenticated=True, token=True, api=False, form=False):
    url = (API if api else WEB) + path
    headers = {}
    if api:
        headers["api-key"] = (PRIVATE / "demo.key").read_text()
    if method != "GET" and token and not api and csrf:
        headers["X-CSRF-TOKEN"] = csrf
    if body is not None:
        body = urllib.parse.urlencode(body).encode() if form else json.dumps(body).encode()
        headers["Content-Type"] = "application/x-www-form-urlencoded" if form else "application/json"
    request = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with (opener if authenticated else plain).open(request, timeout=20) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()

def get(path):
    code, body = http(path)
    if code != 200:
        raise AssertionError(f"HTTP {code} für {path}")
    return json.loads(body)

def until(predicate, name, timeout=25):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            if predicate():
                return
        except (OSError, ValueError):
            pass
        time.sleep(.25)
    try:
        (RUN/"timeout-diagnostics.json").write_text(json.dumps(get("/api/diagnostics"),indent=2))
    except Exception:
        pass
    raise AssertionError("Timeout: " + name)

def spawn(dll, directory, environment, log):
    stream = open(log, "ab", buffering=0)
    process = subprocess.Popen(["dotnet", str(dll)], cwd=directory,
        env=dict(os.environ, **environment), stdout=stream, stderr=subprocess.STDOUT)
    processes.append((process, stream))
    return process

def start_gateway():
    global gateway
    gateway = spawn(DLL, ROOT / "src/Gateway", {
        "ASPNETCORE_URLS": WEB, "GATEWAY_DEMO": "true", "GATEWAY_DEMO_API_URL": API,
        "GATEWAY_DEMO_KEY_FILE": str(PRIVATE / "demo.key"),
        "GATEWAY_DATA_DIR": str(PRIVATE / "data"), "GATEWAY_KEY_DIR": str(PRIVATE / "keys"),
        "GATEWAY_INITIAL_UA_PORT": str(UA_PORT), "Logging__LogLevel__Default": "Warning"
    }, RUN / "gateway.log")
    until(lambda: http("/health/ready", authenticated=False)[0] == 200, "OPC-UA-Start", 50)

def stop_gateway():
    gateway.terminate()
    try:
        code = gateway.wait(timeout=18)
    except subprocess.TimeoutExpired:
        gateway.kill()
        gateway.wait()
        raise AssertionError("Gateway wurde nicht geordnet beendet")
    check(code == 0, "Geordnetes Herunterfahren")

def cli(command, **options):
    arguments = ["dotnet", str(PROBE), command, "--pki", str(PRIVATE / "probe")]
    for key, value in options.items():
        arguments += ["--" + key.replace("_", "-"), str(value)]
    return subprocess.run(arguments, cwd=ROOT, capture_output=True, timeout=90)

def probe(name, expected="Good", seconds=1):
    output = RUN / (name + ".json")
    result = cli("probe", endpoint=f"opc.tcp://localhost:{UA_PORT}/kitchen",
        server_cert=RUN / "server.der", user="integration-reader", password_file=PRIVATE / "ua-password",
        output=output, expected_status=expected, seconds=seconds)
    (RUN / (name + ".log")).write_bytes(result.stdout + result.stderr)
    if result.returncode != 0:
        raise AssertionError(f"OPC-UA-Probe {name} fehlgeschlagen; siehe {name}.log")
    return json.loads(output.read_text())

def scenario(**values):
    code, _ = http("/__demo/scenario", "POST", values, api=True)
    if code != 200:
        raise AssertionError("Simulationssteuerung fehlgeschlagen")

TARGET = "device/00000000-0000-0000-0000-000000000101/zone/0/ActualTemperature"
def quality(expected):
    return any(n["nodeId"].endswith(TARGET) and n["status"] == expected for n in get("/api/diagnostics")["nodes"])

try:
    spawn(DEMO, ROOT, {"ASPNETCORE_URLS": API, "DEMO_KEY_FILE": str(PRIVATE / "demo.key"),
        "Logging__LogLevel__Default": "Warning"}, RUN / "demo.log")
    until(lambda: (PRIVATE / "demo.key").exists(), "Simulationsschlüssel")
    start_gateway()
    check(http("/api/config", authenticated=False)[0] == 401, "Administrative Daten ohne Anmeldung geschützt")
    setup = http("/setup")[1].decode()
    token = html.unescape(re.search(r'name="__RequestVerificationToken" value="([^"]+)"', setup)[1])
    admin_password = secrets.token_urlsafe(32)
    code, _ = http("/setup", "POST", {"username": "integration-admin", "password": admin_password,
        "token": (PRIVATE / "keys/bootstrap-token").read_text()}, form=True, token=False)
    check(code == 400, "Ersteinrichtung weist fehlenden CSRF-Token ab")
    code, _ = http("/setup", "POST", {"__RequestVerificationToken": token, "username": "integration-admin",
        "password": admin_password, "token": (PRIVATE / "keys/bootstrap-token").read_text()}, form=True)
    check(code == 200, "Ersteinrichtung mit einmaligem lokalem Token")
    login = http("/login")[1].decode()
    token = html.unescape(re.search(r'name="__RequestVerificationToken" value="([^"]+)"', login)[1])
    code, _ = http("/login", "POST", {"__RequestVerificationToken": token,
        "username": "integration-admin", "password": admin_password}, form=True)
    check(code == 200, "Administrative Anmeldung")
    csrf = get("/api/session")["csrfToken"]
    check(http("/api/connection-test", "POST", {}, token=False)[0] == 400, "Administrative Aktionen benötigen CSRF-Schutz")
    check(http("/api/connection-test", "POST", {})[0] == 200, "Verbindungstest gegen lokale simulierte API")
    exported = http("/api/config/export")[1]
    key = (PRIVATE / "demo.key").read_text()
    check(key.encode() not in exported and b"passwordHash" not in exported and b"apiKey" not in exported,
        "Konfigurationsexport enthält keine Schlüssel oder Passwort-Hashes")
    check(key.encode() not in (PRIVATE / "data/api-key.enc").read_bytes(), "API-Key liegt verschlüsselt vor")
    cfg = get("/api/config")["configuration"]
    invalid = dict(cfg, uaPort=7)
    check(http("/api/config", "POST", invalid)[0] == 400, "Ungültige Konfiguration wird vor Aktivierung abgewiesen")
    result = cli("init")
    check(result.returncode == 0 and (PRIVATE / "probe/probe.der").exists(), "Eigenständiger OPC-UA-Client erzeugt eigenes Zertifikat")
    (RUN / "server.der").write_bytes(http("/api/ua/certificate")[1])
    ua_password = secrets.token_urlsafe(32)
    (PRIVATE / "ua-password").write_text(ua_password)
    (PRIVATE / "ua-password").chmod(0o600)
    check(http("/api/ua/users", "POST", {"username":"integration-reader","password":ua_password,"role":"Observer"})[0] == 200,
        "Getrenntes OPC-UA-Konto eingerichtet")
    rejected = cli("probe", endpoint=f"opc.tcp://localhost:{UA_PORT}/kitchen", server_cert=RUN/"server.der",
        user="integration-reader", password_file=PRIVATE/"ua-password", seconds=1)
    (RUN / "untrusted.log").write_bytes(rejected.stdout + rejected.stderr)
    check(rejected.returncode != 0, "Nicht freigegebenes OPC-UA-Clientzertifikat wird abgewiesen")
    check(http("/api/ua/trust", "POST", {"derBase64":base64.b64encode((PRIVATE/"probe/probe.der").read_bytes()).decode()})[0] == 200,
        "Explizite Zertifikatsfreigabe durch Webadministrator")
    until(lambda: quality("Good"), "erster aktueller Messwert")
    report = probe("initial", seconds=4)
    check(report["securityMode"] == "SignAndEncrypt", "Echte verschlüsselte UA-TCP-Verbindung")
    check(len(report["notifications"]) >= 2, "Browse, Read und Subscription mit separatem OPC-UA-Client")
    hki = report["namespaceUris"].index("http://opcfoundation.org/UA/CommercialKitchenEquipment/")
    for number, typename in [(101,1030),(102,1007),(103,1032)]:
        node = f"device/00000000-0000-0000-0000-{number:012d}"
        check(any(r["nodeId"].endswith(node) and r["typeDefinition"] == f"ns={hki};i={typename}" for r in report["references"]),
            f"Offizieller HKI-Typ für Demo-Gerät {number}")
        for required in ["Manufacturer","Model","SerialNumber","HardwareRevision","SoftwareRevision",
            "DeviceRevision","DeviceManual","RevisionCounter","DeviceClass","ErrorConditions","InformationConditions"]:
            check(any(r["nodeId"].endswith(node+"/"+required) for r in report["references"]),
                f"Geerbter Pflichtknoten {number}/{required}")
    check(any(r["name"] == "CookingZone_1" for r in report["references"]), "Mehrere Zonen einer Geräteinstanz")
    check(any(r["nodeId"].endswith("/ProgramMode") and r["status"] == "BadNotSupported" and r["value"] is None for r in report["reads"]),
        "Fehlender Pflichtzustand wird nicht als Aus/Good vorgetäuscht")
    check(any(r["nodeId"].endswith("/EngineeringUnits") and isinstance(r["value"],dict) and
        r["value"].get("displayName") == "°C" and r["value"].get("unitId") == 4408652 for r in report["reads"]),
        "HKI-Einheit °C mit UN/CEFACT-Kennung")
    check(any(r["nodeId"].endswith("/HistoryDuration") and r["status"] == "BadNoData" for r in report["reads"]),
        "Unbekannte HACCP-Historienparameter bleiben ausdrücklich unbekannt")
    check("Bad" in report["writeResult"], "Nicht unterstützter Geräte-Schreibzugriff wird über OPC UA abgewiesen")
    check(report["conditionRefresh"] == "Good", "OPC-UA-ConditionRefresh ist implementiert")
    for name, injected, expected in [
        ("missing", {"MissingField":"actualTemperature"}, "BadNoData"),
        ("invalid", {"InvalidField":"actualTemperature"}, "BadDecodingError"),
        ("empty", {"Empty":True}, "BadNoData"),
        ("stale", {"FixedTimestamp":(dt.datetime.now(dt.timezone.utc)-dt.timedelta(seconds=120)).isoformat()}, "UncertainLastUsableValue"),
        ("invalid-json", {"InvalidJson":True}, "BadDecodingError"),
        ("api-outage", {"Status":503}, "UncertainNoCommunicationLastUsableValue"),
    ]:
        scenario(**injected)
        until(lambda e=expected: quality(e), name)
        current = probe(name, expected)
        check(current["reads"], f"Echte OPC-UA-Qualität bei {name}: {expected}")
        if name == "stale":
            measured = next(r for r in current["reads"] if r["nodeId"].endswith(TARGET))["sourceTimestamp"]
            check(dt.datetime.fromisoformat(measured.replace("Z","+00:00")) ==
                dt.datetime.fromisoformat(injected["FixedTimestamp"]), "Veraltete Quellzeit wird trotz erneutem Abruf exakt erhalten")
        check(http("/health/ready", authenticated=False)[0] == 200, "OPC-UA-Prozess bleibt bei "+name+" verfügbar")
        scenario()
        until(lambda: quality("Good"), name+" Wiederherstellung", 35)
    scenario(FailedDevice=102)
    until(lambda: any(n["nodeId"].endswith("/zone/0/ActualTemperature") and "000000000102" in n["nodeId"] and n["status"]=="UncertainNoCommunicationLastUsableValue"
        for n in get("/api/diagnostics")["nodes"]), "isolierter Gerätefehler")
    check(quality("Good"), "Fehler eines Gerätes beeinträchtigt andere Geräte nicht")
    scenario()
    scenario(MissingDevice=101)
    until(lambda: quality("BadNotConnected"), "einzelnes Gerät fehlt im Inventar")
    check(probe("missing-device","BadNotConnected")["reads"],"Fehlendes Inventargerät wird separat gekennzeichnet")
    scenario()
    until(lambda: quality("Good"),"Inventargerät wieder vorhanden",35)
    scenario(Status=429, RetryAfterSeconds=3)
    until(lambda: quality("BadServerTooBusy"), "Rate Limit")
    check(http("/health/source", authenticated=False)[0] == 503, "Datenquellen-Health von Prozess-Readiness getrennt")
    scenario()
    until(lambda: quality("Good"), "Retry-After Wiederherstellung", 35)
    for status in [401,403]:
        scenario(Status=status)
        until(lambda: quality("BadUserAccessDenied"), f"HTTP {status}")
        check(True, f"HTTP {status} wird als Berechtigungsfehler dargestellt")
        scenario()
        stop_gateway()
        start_gateway()
        until(lambda: quality("Good"), "Neustart nach Berechtigungsprüfung")
    before = {r["nodeId"] for r in report["references"]}
    check(http("/api/config","POST",cfg)[0]==200, "Geheimnisfreier Konfigurationsimport mit Validierung")
    stop_gateway()
    start_gateway()
    until(lambda: quality("Good"), "Persistenz")
    after = probe("after-restart")
    check(before == {r["nodeId"] for r in after["references"]}, "NodeIds bleiben nach Neustart stabil")
    check(http("/api/config")[0] == 200, "Webanmeldung und verschlüsselte Zugangsdaten überleben Neustart")
    event_process = subprocess.Popen(["dotnet",str(PROBE),"probe","--pki",str(PRIVATE/"probe"),
        "--endpoint",f"opc.tcp://localhost:{UA_PORT}/kitchen","--server-cert",str(RUN/"server.der"),
        "--user","integration-reader","--password-file",str(PRIVATE/"ua-password"),"--seconds","9",
        "--ready-file",str(RUN/"event-client.ready"),"--output",str(RUN/"events.json")],
        cwd=ROOT,stdout=open(RUN/"events.log","wb"),stderr=subprocess.STDOUT)
    processes.append((event_process,None))
    until(lambda: (RUN/"event-client.ready").exists(),"Event-Client ist tatsächlich subscribed",35)
    scenario(Status=503)
    until(lambda: quality("UncertainNoCommunicationLastUsableValue"), "Alarmbeginn")
    time.sleep(1)
    scenario()
    until(lambda: quality("Good"), "Alarmende",35)
    check(event_process.wait(timeout=40)==0,"Event-Subscription mit eigenständigem Client")
    event_report=json.loads((RUN/"events.json").read_text())
    check(any(isinstance(e,list) and True in e for e in event_report["events"]), "Gateway-Kommunikationsalarm als aktives Condition-Event")
    check(any(isinstance(e,list) and any(isinstance(v,str) and "wieder erreichbar" in v for v in e) for e in event_report["events"]),
        "Gateway-Kommunikationsalarm wird nach Wiederkehr zurückgesetzt")
    check(all(key not in (RUN/name).read_text(errors="replace") for name in ["gateway.log","demo.log"]),
        "Keine API-Schlüssel in Prozesslogs")
    check(not get("/api/config")["restartRequired"], "Aktive und gespeicherte Konfiguration stimmen nach Neustart überein")
    stop_gateway()
    print(f"Integration erfolgreich: {len(checks)} Prüfungen. Ergebnisse: {RUN}",flush=True)
    (RUN/"summary.json").write_text(json.dumps({"passed":len(checks),"failed":0,"simulationOnly":True,"checks":checks},indent=2,ensure_ascii=False))
finally:
    for process, stream in reversed(processes):
        if process.poll() is None:
            process.terminate()
            try: process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
        if stream: stream.close()
