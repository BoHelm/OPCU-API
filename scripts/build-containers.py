#!/usr/bin/env python3
"""Reproduzierbarer öffentlicher Container-Build, auch hinter dem bereitgestellten Cloud-Egress-Proxy.

Proxyvariablen werden als vorgegebene Docker-Proxy-Buildargumente übergeben (keine Secret-Werte in der CLI).
Ein Hostnamen-Mapping übernimmt nur die Auflösung des bereits konfigurierten Proxys in verschachtelte
Build-Netzwerke. Das ist kein alternativer Proxy und ändert keine Egress-Berechtigungen.
"""
import os
from pathlib import Path
import socket
import subprocess
import urllib.parse

root = Path(__file__).resolve().parents[1]
environment = dict(os.environ)
environment.setdefault("DOCKER_CONFIG", str(root / ".artifacts/docker-config"))
common = ["--build-arg", "HTTP_PROXY", "--build-arg", "HTTPS_PROXY", "--build-arg", "NO_PROXY"]
for key in ["HTTPS_PROXY", "HTTP_PROXY"]:
    hostname = urllib.parse.urlsplit(os.environ.get(key, "")).hostname
    if hostname:
        try:
            common += ["--add-host", hostname + ":" + socket.gethostbyname(hostname)]
        except OSError:
            raise SystemExit("Der konfigurierte Build-Proxy ist nicht auflösbar.")
        break
ca = os.environ.get("CODEX_PROXY_CERT")
if ca and Path(ca).is_file():
    common += ["--secret", "id=extra_ca,src=" + ca]
for target, tag in [("gateway", "kitchen-gateway:0.1.0"), ("demo", "kitchen-demo-api:0.1.0")]:
    subprocess.run(["docker", "build", "--target", target, "--tag", tag, *common, "."],
        cwd=root, env=environment, check=True)
