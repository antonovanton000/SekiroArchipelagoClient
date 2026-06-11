# MapAtlasTool

Exports a first-pass interactive atlas base from Sekiro MSB files.

Default Ashina Outskirts export:

```powershell
dotnet run --project MapAtlasTool\MapAtlasTool.csproj -- --source artifacts\map_ForTests --map m11_00_00_00 --out artifacts\map_atlas\m11_00_00_00
```

Outputs:

- `*.atlas.json`: world-space bounds, layer metadata, parts, and markers.
- `*.preview.html`: local pan/zoom preview for checking projected marker positions.

Sekiro `.msb.dcx` files need `oo2core_6_win64.dll` for Oodle decompression. The tool checks these local locations automatically:

- `artifacts\oo2core_6_win64.dll`
- `artifacts\map_ForTests\oo2core_6_win64.dll`
- `artifacts\map_ForTests\oodle\oo2core_6_win64.dll`
- `SekiroAPClient\oo2core_6_win64.dll`

You can also pass it explicitly:

```powershell
dotnet run --project MapAtlasTool\MapAtlasTool.csproj -- --oodle D:\Sekiro\oo2core_6_win64.dll
```
