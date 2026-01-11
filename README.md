# BeamNG38MapConverter

A tool to migrate BeamNG.drive custom maps from older versions to 0.38+ by converting material texture paths to be compatible with the new central asset system. I'm not an expert in BeamNG modding - so am not sure at which version the JSON materials manifests were introduced!

## Quick Start

### Prerequisites
- BeamNG.drive installed (version 0.38 or higher)
- `migrator-config.json` file in the same directory as the executable (included with the tool)

### Configuration
Edit `migrator-config.json` to point to your BeamNG.drive installation. You can obtain this by right clicking on the game in steam, and choosing "Manage" -> "Browse Local Files", then copying the file path opened. (Make sure to use double backslashes \\\\):
```json
{
  "GameRoot": "E:\\Steam\\steamapps\\common\\BeamNG.drive"
}
```

### Basic Usage

**Convert a map folder:**
```cmd
BeamNG38MapConverter.exe --targetlevel "C:\path\to\map\folder" --targetmapname "your_map_name"
```

**Convert a map zip file:**
```cmd
BeamNG38MapConverter.exe --targetlevel "C:\path\to\map.zip" --targetmapname "your_map_name"
```

**For example:**
```cmd
BeamNG38MapConverter.exe  --targetlevel "E:\Games\BeamNGServer\Resources\Client\nb 152 reshjemheia 0.3.1 BNG0.37 compatible.zip" --targetmapname Reshjemheia
```

When processing a zip file, the tool will automatically extract it, perform the conversion, and create an `output.zip` file in the current directory.

### Optional Flags

- `--copylocal` - Copy all central assets locally to the map's zip (larger file and duplcates assets)
- `--debug` - Enable detailed debug logging

### Examples

**Convert with local copy:**
```cmd
BeamNG38MapConverter.exe --targetlevel "C:\maps\my_custom_map" --targetmapname "my_custom_map" --copylocal
```

**Convert a zip with debug output:**
```cmd
BeamNG38MapConverter.exe --targetlevel "C:\downloads\map.zip" --targetmapname "map_name" --copylocal --debug
```

---

## What Does This Tool Do?

### Overview
BeamNG.drive version 0.38 introduced a new asset system that changed how maps reference textures and materials. Custom maps created for older versions will have broken textures when loaded in 0.38+ because the material paths are no longer valid. This tool automatically attempts to fixe those paths to restore textures.

### Problem Background
Previously, custom maps could reference textures from other levels (e.g., `/levels/west_coast_usa/art/shapes/terrain/...`)

In version 0.38, the asset structure was reorganized and many of these paths no longer work, resulting in:
- Missing textures (purple/pink surfaces)
- Broken material references
- Unplayable custom maps

### Solution Process

The tool performs the following operations:

#### **Scanning**
   - Recursively searches the map's `art/` folder for all `*.materials.json` files
   - These JSON files define material properties and texture paths for every surface in the map

#### **Path Analysis**
   For each texture reference in the materials files, the tool:
   - Identifies textures with broken paths
   - Skips textures already pointing to the correct local map location
   - Searches for the actual texture file in multiple locations

#### **Texture Location Strategies**
   The tool uses multiple search strategies to find textures:

   **Strategy A: Level Zips**
   - If the texture references another level (e.g., `/levels/west_coast_usa/...`)
   - Searches in `BeamNG.drive/content/levels/[level_name].zip`
   - Extracts the texture to the target map's `art/remote_assets/` folder

   **Strategy B: Central Assets (Exact Path)**
   - Searches in `BeamNG.drive/content/assets/materials/*.zip` files
   - Looks for exact VFS path matches
   - Either copies locally (with `--copylocal`) or updates to the new central path

   **Strategy C: Filename Search**
   - If exact path fails, searches by filename across all asset zips
   - Handles extension differences (e.g., finds `.dds` when `.png` is referenced)
   - Automatically tries `.dds` extension if original extension fails - some assets have been updated from PNG to DDS in latest versions.

####  **Output**
   - Saves modified `*.materials.json` files with corrected paths
   - If input was a zip, creates `output.zip` with the converted map
   - Provides summary statistics (files processed, fixes applied, paths skipped)

### Technical Details

**File Formats:**
- Processes JSON files containing material definitions
- Handles duplicate keys gracefully (uses last value)
- Maintains JSON formatting and structure

**Supported Texture Types:**
- All texture map types (colorMap, normalMap, roughnessMap, metallicMap, etc.)

**Search Scope:**
- Central materials: `content/assets/materials/*.zip`
- Level packs: `content/levels/*.zip`
- All subdirectories within archives

### Logging
The tool provides three levels of output:

- **Information (default):** Key operations and results
- **Warning:** Non-critical issues (duplicate keys, missing config)
- **Debug (`--debug` flag):** Detailed step-by-step processing information

---

## Troubleshooting

**Error: "GameRoot directory does not exist"**
- Check that `migrator-config.json` has the correct path to your BeamNG.drive installation
- Ensure the path uses double backslashes: `E:\\Steam\\...`

**Error: "BeamNG.drive.exe not found"**
- Verify your BeamNG.drive installation is complete
- Check that the `GameRoot` path points to the game's root folder (not a subfolder)

**Warning: "No *.materials.json files found"**
- Ensure you're pointing to the correct map folder
- The folder should contain an `art/` subdirectory with materials files

**Some textures still missing after conversion:**
- Try running with `--debug` to see detailed search results
- The texture may not exist in your BeamNG.drive installation
- Some custom textures may need to be manually placed in `art/remote_assets/`

---

## Output Example

```
BeamNG38MapConverter.exe --targetlevel "E:\Games\BeamNGServer\Resources\Client\nb 152 reshjemheia 0.3.1 BNG0.37 compatible.zip" --targetmapname Reshjemheia
info: BeamAssetMigrator[0]
      Configuration loaded from migrator-config.json
info: BeamAssetMigrator[0]
      Target level is a zip file, extracting to temp folder
info: BeamAssetMigrator[0]
      Found 40 materials.json file(s) to process
info: BeamAssetMigrator[0]
      Processing: main.materials.json
info: BeamAssetMigrator[0]
      [FIXED] t_beachsand_nm.png -> /levels/Reshjemheia/art/remote_assets/t_beachsand_nm.png
info: BeamAssetMigrator[0]
      [FIXED] t_macro_grass_b.png -> /assets/materials/terrain/grass/macro_grass/t_macro_grass_b.png
info: BeamAssetMigrator[0]
      [FIXED] t_macro_buttercup_nm.png -> /levels/Reshjemheia/art/remote_assets/t_macro_buttercup_nm.png
info: BeamAssetMigrator[0]
      [FIXED] t_macro_buttercup_nm.png -> /levels/Reshjemheia/art/remote_assets/t_macro_buttercup_nm.png
info: BeamAssetMigrator[0]
      [FIXED] eca_plastic_rough_d.dds -> /assets/materials/tileable/plastic/eca_plastic_rough/eca_plastic_rough_d.dds

... etc ...

info: BeamAssetMigrator[0]
      Localized: 18 | Skipped: 3
info: BeamAssetMigrator[0]
      === OVERALL SUMMARY ===
info: BeamAssetMigrator[0]
      Total files processed: 40
info: BeamAssetMigrator[0]
      Total fixed: 734
info: BeamAssetMigrator[0]
      Total already correct (skipped): 52
info: BeamAssetMigrator[0]
      Creating output zip: E:\Games\BeamNGServer\Temp\BeamNG38MapConverter\bin\Release\net10.0\win-x64\publish\output.zip
info: BeamAssetMigrator[0]
      Output saved to: E:\Games\BeamNGServer\Temp\BeamNG38MapConverter\bin\Release\net10.0\win-x64\publish\output.zip
```

---

## License

This tool is provided as-is, without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose, and noninfringement.

In no event shall the author or copyright holder be liable for any claim, damages, or other liability, whether in an action of contract, tort, or otherwise, arising from, out of, or in connection with the software or the use or other dealings in the software.

The author accepts no liability for how this tool is used or for any consequences resulting from its use, including but not limited to data loss, corruption, or damage to game files.
