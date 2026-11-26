#!/bin/bash
# Builds plugin (net48 + net8), bundle, zip and the single installer exe.
set -e
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$(dirname "$0")"
dotnet test src/Core.Tests -c Release 2>&1 | tail -3
sed -i "s#@@SERVER_URL@@#${PROXY_URL:-@@SERVER_URL@@}#" src/Plugin/Defaults.cs
dotnet build src/Plugin -c Release 2>&1 | grep -E "error|Build succeeded"
rm -rf out && mkdir -p out/BPlusCadTools.bundle/Contents/net48 out/BPlusCadTools.bundle/Contents/net8
cp bundle/PackageContents.xml out/BPlusCadTools.bundle/
cp src/Plugin/bin/Release/net48/BPlus.CadTools.dll out/BPlusCadTools.bundle/Contents/net48/
cp src/Plugin/bin/Release/net8.0-windows/BPlus.CadTools.dll out/BPlusCadTools.bundle/Contents/net8/
cp src/Plugin/bin/Release/net8.0-windows/System.Security.Cryptography.ProtectedData.dll out/BPlusCadTools.bundle/Contents/net8/ 2>/dev/null || true
(cd out/BPlusCadTools.bundle && python3 -c "
import zipfile,os
z=zipfile.ZipFile('../bundle.zip','w',zipfile.ZIP_DEFLATED)
for r,d,f in os.walk('.'):
    for n in sorted(f):
        p=os.path.join(r,n); z.write(p,os.path.relpath(p,'.').replace(os.sep,'/'))
z.close()")
dotnet build src/Installer -c Release -p:BundleZip="$PWD/out/bundle.zip" 2>&1 | grep -E "error|Build succeeded"
cp src/Installer/bin/Release/net48/BPlusCadTools-Setup.exe out/
git checkout -- src/Plugin/Defaults.cs 2>/dev/null || sed -i "s#${PROXY_URL:-@@SERVER_URL@@}#@@SERVER_URL@@#" src/Plugin/Defaults.cs
(cd out && sha256sum BPlusCadTools-Setup.exe > SHA256SUMS.txt && cat SHA256SUMS.txt)
