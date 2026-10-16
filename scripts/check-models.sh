#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet tool restore
mkdir -p .artifacts/generated
dotnet tool run Opc.Ua.ModelCompiler -- compile \
  -d2 models/upstream/Opc.Ua.CommercialKitchenEquipment.NodeSet2.xml,Opc.Ua.CommercialKitchenEquipment,CommercialKitchenEquipment \
  -d2 models/upstream/Opc.Ua.Di.NodeSet2.xml,Opc.Ua.DI,DI \
  -version v104 -o2 .artifacts/generated
test -s .artifacts/generated/Opc.Ua.CommercialKitchenEquipment.Classes.cs
test -s .artifacts/generated/Opc.Ua.CommercialKitchenEquipment.PredefinedNodes.uanodes
python3 - <<'PY'
import hashlib
from pathlib import Path
for name, expected in {
    'Opc.Ua.CommercialKitchenEquipment.NodeSet2.xml':'9f0b8889707b5aa975d930e1cc7fb4613c7f2d6c68c1bd4fb79eb523bc56fd35',
    'Opc.Ua.Di.NodeSet2.xml':'f089bce774262afbe16951ce11e22f13b6214a0459cc4b7c2c42b4947f5526c9'
}.items():
    actual=hashlib.sha256((Path('models/upstream')/name).read_bytes()).hexdigest()
    assert actual==expected,(name,actual)
    print(name, 'SHA-256 OK')
PY
