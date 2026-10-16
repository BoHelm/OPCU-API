#!/usr/bin/env python3
"""Prüft die dokumentierten nativen Startbefehle einschließlich sauberem Beenden, nur gegen die lokale Simulation."""
import json
import os
from pathlib import Path
import signal
import subprocess
import time
import urllib.error
import urllib.request

root=Path(__file__).resolve().parents[1]
artifacts=root/".artifacts"
artifacts.mkdir(exist_ok=True)
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
with (artifacts/"startup.log").open("wb") as log:
    process=subprocess.Popen(["bash","scripts/run-demo.sh"],cwd=root,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
    try:
        deadline=time.monotonic()+45
        ready=False
        while time.monotonic()<deadline:
            if process.poll() is not None:raise RuntimeError("Startskript vorzeitig beendet; siehe startup.log")
            try:
                with opener.open("http://127.0.0.1:5080/health/ready",timeout=2) as r:
                    ready=r.status==200
                with opener.open("http://127.0.0.1:5080/health/source",timeout=2) as r:
                    ready=ready and r.status==200
                if ready:break
            except (OSError,urllib.error.HTTPError):pass
            time.sleep(.2)
        assert ready,"Startskript liefert keine Bereitschaft/Quellenverbindung"
        with opener.open("http://127.0.0.1:5080/setup",timeout=3) as r:
            assert r.status==200 and b"Ersteinrichtung" in r.read()
        key=(root/".state/demo-run/api.key").read_text()
        req=urllib.request.Request("http://127.0.0.1:5090/api/devices",headers={"api-key":key})
        with opener.open(req,timeout=3) as r:assert len(json.load(r))==3
    finally:
        os.killpg(process.pid,signal.SIGTERM)
        try:process.wait(timeout=18)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid,signal.SIGKILL)
            process.wait()
            raise RuntimeError("Startskript hat seine Dienste nicht geordnet beendet.")
summary={"passed":True,"documentedStartupExecuted":True,"sourceHealth":200,"webSetup":200,"simulatedDevices":3,"gracefulShutdown":True}
(artifacts/"startup-summary.json").write_text(json.dumps(summary,indent=2))
print(json.dumps(summary))
