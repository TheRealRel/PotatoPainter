#!/bin/bash
# Paths: set VALHEIM_MANAGED to Valheim/valheim_Data/Managed, BEPINEX to a folder with core/BepInEx.dll and
# core/0Harmony.dll, and JOTUNN to Jotunn.dll (potato-build-refs\bep).
M="${VALHEIM_MANAGED:-/mnt/user-data/uploads/Managed}"; B="${BEPINEX:-/mnt/user-data/uploads/bep}"
J="${JOTUNN:-$B/Jotunn.dll}"
CSC="${CSC:-dotnet /usr/lib/dotnet/sdk/8.0.131/Roslyn/bincore/csc.dll}"
python3 -I check_config_names.py src/*.cs || { echo "Build stopped: fix the setting names above."; exit 1; }
python3 -I check_patches.py src/*.cs || { echo "Build stopped: a patch class above is never switched on."; exit 1; }
# Every rune drawing goes into the DLL as a resource named runes.<file>.
RES=""; for f in assets/runes/*.png; do RES="$RES -resource:$f,runes.$(basename "$f")"; done
# The Rúnakefli's model, its texture and its painted word (tools/kefli.py), as runakefli.<file>.
for f in assets/runakefli/*; do RES="$RES -resource:$f,runakefli.$(basename "$f")"; done
mkdir -p out
$CSC -nologo -nostdlib -noconfig -target:library -optimize+ -deterministic -langversion:9 -out:out/PotatoPainter.dll $RES \
  -r:"$M/mscorlib.dll" -r:"$M/netstandard.dll" -r:"$M/System.dll" -r:"$M/System.Core.dll" \
  -r:"$M/assembly_valheim.dll" -r:"$M/assembly_utils.dll" -r:"$M/assembly_guiutils.dll" \
  -r:"$M/UnityEngine.dll" -r:"$M/UnityEngine.CoreModule.dll" -r:"$M/UnityEngine.PhysicsModule.dll" -r:"$M/UnityEngine.ImageConversionModule.dll" -r:"$M/UnityEngine.AssetBundleModule.dll" -r:"$M/assembly_postprocessing.dll" \
  -r:"$M/UnityEngine.UI.dll" -r:"$M/UnityEngine.UIModule.dll" -r:"$M/Unity.TextMeshPro.dll" -r:"$M/UnityEngine.TextRenderingModule.dll" \
  -r:"$B/core/BepInEx.dll" -r:"$B/core/0Harmony.dll" -r:"$J" src/*.cs
