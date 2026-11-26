# Building (developers)
Needs the .NET 8 SDK. The AutoCAD API DLLs are Autodesk property and are not in this repo.
Put them at `../refs/net48` (AutoCAD 2021 API: AcMgd, AcDbMgd, AcCoreMgd, AdWindows) and `../refs/net8` (AutoCAD 2025 API) relative to the cad-tools folder's parent, then run `./build.sh`. Output: `out/BPlusCadTools-Setup.exe`.
