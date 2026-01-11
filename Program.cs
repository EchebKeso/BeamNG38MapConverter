using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

public class MigratorConfig
{
    public string GameRoot { get; set; } = @"E:\Steam\steamapps\common\BeamNG.drive";
}

public class BeamAssetMigrator
{
    // --- CONFIGURATION ---
    private const string CentralAssetsDir = @"content\assets\materials";
    private const string LevelsDir = @"content\levels";
    private const string LocalArtPath = "art/remote_assets";
    private const string ConfigFileName = "migrator-config.json";
    // ---------------------

    private static ILogger? _logger;
    private static MigratorConfig _config = new MigratorConfig();

    public static void Main(string[] args)
    {
        // Parse command line arguments
        bool debugMode = args.Contains("--debug", StringComparer.OrdinalIgnoreCase);
        bool copyLocal = args.Contains("--copylocal", StringComparer.OrdinalIgnoreCase);
        string? targetLevel = GetArgValue(args, "--targetlevel");
        string? targetMapName = GetArgValue(args, "--targetmapname");

        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(debugMode ? LogLevel.Debug : LogLevel.Information);
        });
        _logger = loggerFactory.CreateLogger<BeamAssetMigrator>();

        if (debugMode)
        {
            _logger.LogDebug("Debug mode enabled");
        }

        // Load configuration
        LoadConfig();

        // Validate GameRoot exists and contains BeamNG.drive.exe
        if (!Directory.Exists(_config.GameRoot))
        {
            _logger.LogError("GameRoot directory does not exist: {GameRoot}", _config.GameRoot);
            _logger.LogError("Please ensure the GameRoot path in {ConfigFile} points to a valid BeamNG.drive installation", ConfigFileName);
            return;
        }

        string beamNGExePath = Path.Combine(_config.GameRoot, "BeamNG.drive.exe");
        if (!File.Exists(beamNGExePath))
        {
            _logger.LogError("BeamNG.drive.exe not found in GameRoot: {GameRoot}", _config.GameRoot);
            _logger.LogError("Please ensure the GameRoot path in {ConfigFile} points to a valid BeamNG.drive installation", ConfigFileName);
            return;
        }

        _logger.LogDebug("GameRoot validated: {GameRoot}", _config.GameRoot);

        // Validate required parameters
        if (string.IsNullOrEmpty(targetLevel))
        {
            _logger.LogError("--targetlevel parameter is required");
            _logger.LogInformation("Usage: BeamNG38MapConverter.exe --targetlevel <path_to_level_zip_or_folder> --targetmapname <map_name>");
            return;
        }

        if (string.IsNullOrEmpty(targetMapName))
        {
            _logger.LogError("--targetmapname parameter is required");
            _logger.LogInformation("Usage: BeamNG38MapConverter.exe --targetlevel <path_to_level_zip_or_folder> --targetmapname <map_name>");
            return;
        }

        // Handle target level parameter
        string levelRootOnDisk;
        string? tempExtractPath = null;
        bool isZipInput = false;

        if (File.Exists(targetLevel) && Path.GetExtension(targetLevel).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Target level is a zip file, extracting to temp folder");
            tempExtractPath = Path.Combine(Path.GetTempPath(), $"BeamMigrator_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempExtractPath);
            
            _logger.LogDebug("Extracting {ZipFile} to {TempPath}", targetLevel, tempExtractPath);
            ZipFile.ExtractToDirectory(targetLevel, tempExtractPath);
            
            // Find the specific level folder with targetMapName
            string expectedLevelPath = Path.Combine(tempExtractPath, "levels", targetMapName);
            if (Directory.Exists(expectedLevelPath))
            {
                levelRootOnDisk = expectedLevelPath;
                isZipInput = true;
            }
            else
            {
                _logger.LogError("Could not find level directory '/levels/{TargetMapName}' in extracted zip", targetMapName);
                // Cleanup temp folder
                try { Directory.Delete(tempExtractPath, true); } catch { }
                return;
            }
        }
        else if (Directory.Exists(targetLevel))
        {
            levelRootOnDisk = targetLevel;
        }
        else
        {
            _logger.LogError("Target level path not found: {TargetLevel}", targetLevel);
            return;
        }

        // Validate that the level folder structure is correct for both zip and directory inputs
        // Check if the path ends with the expected map name or contains levels/{targetMapName}
        if (!levelRootOnDisk.EndsWith(targetMapName, StringComparison.OrdinalIgnoreCase))
        {
            // Try to find the correct folder
            if (Directory.Exists(Path.Combine(levelRootOnDisk, "levels", targetMapName)))
            {
                levelRootOnDisk = Path.Combine(levelRootOnDisk, "levels", targetMapName);
            }
            else
            {
                _logger.LogError("Target level path does not contain '/levels/{TargetMapName}' folder structure", targetMapName);
                
                // Cleanup temp folder if it was a zip input
                if (isZipInput && tempExtractPath != null)
                {
                    try { Directory.Delete(tempExtractPath, true); } catch { }
                }
                return;
            }
        }

        _logger.LogDebug("Level Root on Disk: {LevelRootOnDisk}", levelRootOnDisk);

        string artFolder = Path.Combine(levelRootOnDisk, "art");
        string destinationDir = Path.Combine(levelRootOnDisk, "art", "remote_assets");
        string mapRootVfs = $"/levels/{targetMapName}";

        _logger.LogDebug("Configuration loaded - Target Map: {TargetMapName}", targetMapName);
        _logger.LogDebug("Game Root: {GameRoot}", _config.GameRoot);
        _logger.LogDebug("Level Root on Disk: {LevelRootOnDisk}", levelRootOnDisk);
        _logger.LogDebug("Art Folder: {ArtFolder}", artFolder);
        _logger.LogDebug("Destination Directory: {DestinationDir}", destinationDir);

        if (!Directory.Exists(artFolder)) 
        { 
            _logger.LogError("Art folder not found: {ArtFolder}", artFolder); 
            return; 
        }

        // Scan for all *.materials.json files in art folder and subfolders
        _logger.LogDebug("Scanning for *.materials.json files in art folder");
        var materialsJsonFiles = Directory.GetFiles(artFolder, "*.materials.json", SearchOption.AllDirectories);
        _logger.LogDebug("Found {Count} materials.json files", materialsJsonFiles.Length);

        if (materialsJsonFiles.Length == 0)
        {
            _logger.LogWarning("No *.materials.json files found in art folder");
            return;
        }

        _logger.LogInformation("Found {Count} materials.json file(s) to process", materialsJsonFiles.Length);

        if (!Directory.Exists(destinationDir)) 
        {
            _logger.LogDebug("Creating destination directory: {DestinationDir}", destinationDir);
            Directory.CreateDirectory(destinationDir);
        }

        // Prepare Source Zips
        string fullAssetsPath = Path.Combine(_config.GameRoot, CentralAssetsDir);
        _logger.LogDebug("Looking for central asset zips in: {FullAssetsPath}", fullAssetsPath);
        var centralZips = Directory.Exists(fullAssetsPath) 
            ? Directory.GetFiles(fullAssetsPath, "*.zip") 
            : Array.Empty<string>();
        _logger.LogDebug("Found {Count} central asset zip files", centralZips.Length);

        int totalFixedCount = 0;
        int totalSkippedCount = 0;

        // Process each materials.json file
        foreach (var jsonPath in materialsJsonFiles)
        {
            _logger.LogInformation("Processing: {RelativePath}", Path.GetRelativePath(artFolder, jsonPath));
            _logger.LogDebug("Processing file: {JsonPath}", jsonPath);

            var (fixedCount, skippedCount) = ProcessMaterialsJson(
                jsonPath, 
                destinationDir, 
                mapRootVfs, 
                centralZips,
                copyLocal
            );

            totalFixedCount += fixedCount;
            totalSkippedCount += skippedCount;
        }
        
        _logger.LogInformation("=== OVERALL SUMMARY ===");
        _logger.LogInformation("Total files processed: {Count}", materialsJsonFiles.Length);
        _logger.LogInformation("Total fixed: {Count}", totalFixedCount);
        _logger.LogInformation("Total already correct (skipped): {Count}", totalSkippedCount);
        _logger.LogDebug("All processing complete");

        // If input was a zip, create output.zip
        if (isZipInput && tempExtractPath != null)
        {
            string outputZipPath = Path.Combine(Directory.GetCurrentDirectory(), "output.zip");
            _logger.LogInformation("Creating output zip: {OutputZipPath}", outputZipPath);
            
            if (File.Exists(outputZipPath))
            {
                File.Delete(outputZipPath);
            }
            
            ZipFile.CreateFromDirectory(tempExtractPath, outputZipPath, CompressionLevel.SmallestSize, false);
            _logger.LogInformation("Output saved to: {OutputZipPath}", outputZipPath);
            
            // Cleanup temp folder
            _logger.LogDebug("Cleaning up temp folder: {TempPath}", tempExtractPath);
            try
            {
                Directory.Delete(tempExtractPath, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to delete temp folder: {Message}", ex.Message);
            }
        }
    }

    private static string? GetArgValue(string[] args, string argName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(argName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void LoadConfig()
    {
        string configPath = Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
        
        if (File.Exists(configPath))
        {
            _logger?.LogDebug("Loading config from: {ConfigPath}", configPath);
            try
            {
                string json = File.ReadAllText(configPath);
                var loadedConfig = JsonSerializer.Deserialize<MigratorConfig>(json);
                if (loadedConfig != null)
                {
                    _config = loadedConfig;
                    _logger?.LogInformation("Configuration loaded from {ConfigFileName}", ConfigFileName);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Failed to load config file, using defaults: {Message}", ex.Message);
            }
        }
        else
        {
            _logger?.LogDebug("Config file not found, using default configuration");
            // Create default config file
            try
            {
                string json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
                _logger?.LogInformation("Created default config file: {ConfigPath}", configPath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Failed to create default config file: {Message}", ex.Message);
            }
        }
    }

    private static (int fixedCount, int skippedCount) ProcessMaterialsJson(
        string jsonPath, 
        string destinationDir, 
        string mapRootVfs, 
        string[] centralZips,
        bool copyLocal)
    {
        // Load and Parse
        _logger?.LogDebug("Loading and parsing JSON file");
        JsonNode? root;
        try
        {
            // Use DuplicatePropertyNameHandling to allow duplicate keys (last one wins)
            var options = new JsonDocumentOptions { AllowTrailingCommas = true };
            root = JsonNode.Parse(File.ReadAllText(jsonPath), null, options)?.AsObject();
        }
        catch (ArgumentException ex) when (ex.Message.Contains("same key"))
        {
            _logger?.LogError("JSON file contains duplicate keys. {Message}", ex.Message);
            _logger?.LogError("Please manually fix the JSON file and try again");
            return (0, 0);
        }
        catch (JsonException ex)
        {
            _logger?.LogError("Failed to parse JSON. {Message}", ex.Message);
            return (0, 0);
        }
        
        if (root == null) 
        {
            _logger?.LogError("Failed to parse JSON");
            return (0, 0);
        }

        var rootObject = root.AsObject();
        if (rootObject == null)
        {
            _logger?.LogError("JSON root is not an object");
            return (0, 0);
        }

        int fixedCount = 0;
        int skippedCount = 0;

        _logger?.LogDebug("Starting material processing");

        foreach (var material in rootObject.ToList())
        {
            try
            {
                var matObj = material.Value?.AsObject();
                if (matObj == null) continue;

                _logger?.LogDebug("Processing material: {MaterialName}", material.Key);

                if (matObj.TryGetPropertyValue("Stages", out var stagesNode) && stagesNode is JsonArray stages)
                {
                    foreach (var stageNode in stages)
                    {
                        if (stageNode is JsonObject stage)
                        {
                            var textureKeys = stage.Where(k => k.Key.EndsWith("Map")).Select(k => k.Key).ToList();

                            foreach (var key in textureKeys)
                            {
                                string? vfsPath = stage[key]?.ToString();
                                if (string.IsNullOrEmpty(vfsPath) || !vfsPath.StartsWith("/")) continue;

                                _logger?.LogDebug("  Processing texture {Key}: {VfsPath}", key, vfsPath);

                                // CHECK: If the path already begins with our Target Map VFS root, skip it
                                if (vfsPath.StartsWith(mapRootVfs, StringComparison.OrdinalIgnoreCase))
                                {
                                    _logger?.LogDebug("    Skipping - already points to target map");
                                    skippedCount++;
                                    continue; 
                                }

                                string fileName = Path.GetFileName(vfsPath);
                                string localFileDiskPath = Path.Combine(destinationDir, fileName);
                                bool found = false;
                                string? newVfsPath = null;

                                // Strategy A: Level Zip (always copy)
                                if (vfsPath.StartsWith("/levels/"))
                                {
                                    string sourceLevel = vfsPath.Split('/', StringSplitOptions.RemoveEmptyEntries)[1];
                                    string levelZip = Path.Combine(_config.GameRoot, LevelsDir, $"{sourceLevel}.zip");
                                    _logger?.LogDebug("    Trying level zip: {LevelZip}", levelZip);
                                    found = TryExtract(levelZip, vfsPath, localFileDiskPath);
                                    if (found)
                                    {
                                        newVfsPath = $"{mapRootVfs}/{LocalArtPath}/{fileName}";
                                    }
                                }

                                // Strategy B: Central Assets
                                if (!found)
                                {
                                    _logger?.LogDebug("    Searching in {Count} central asset zips", centralZips.Length);
                                    foreach (var zipPath in centralZips)
                                    {
                                        if (copyLocal)
                                        {
                                            if (TryExtract(zipPath, vfsPath, localFileDiskPath))
                                            {
                                                found = true;
                                                newVfsPath = $"{mapRootVfs}/{LocalArtPath}/{fileName}";
                                                break;
                                            }
                                        }
                                        else
                                        {
                                            var foundPath = TryFindInZip(zipPath, vfsPath);
                                            if (foundPath != null)
                                            {
                                                found = true;
                                                newVfsPath = foundPath;
                                                break;
                                            }
                                        }
                                    }
                                }

                                // Strategy C: Search by filename in all subfolders of each central asset zip
                                if (!found)
                                {
                                    _logger?.LogDebug("    Strategy C: Searching for filename '{FileName}' in all subfolders of {Count} central asset zips", fileName, centralZips.Length);
                                    foreach (var zipPath in centralZips)
                                    {
                                        if (copyLocal)
                                        {
                                            if (TryExtractByFilename(zipPath, fileName, localFileDiskPath))
                                            {
                                                found = true;
                                                newVfsPath = $"{mapRootVfs}/{LocalArtPath}/{fileName}";
                                                break;
                                            }
                                        }
                                        else
                                        {
                                            var foundPath = TryFindByFilenameInZip(zipPath, fileName);
                                            if (foundPath != null)
                                            {
                                                found = true;
                                                newVfsPath = foundPath;
                                                break;
                                            }
                                        }
                                    }
                                    
                                    // If not found and extension is not .dds, try again with .dds extension
                                    if (!found && !fileName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string ddsFileName = Path.ChangeExtension(fileName, ".dds");
                                        string ddsLocalFileDiskPath = Path.Combine(destinationDir, ddsFileName);
                                        _logger?.LogDebug("    Strategy C fallback: Searching for '{DdsFileName}' with .dds extension", ddsFileName);
                                        
                                        foreach (var zipPath in centralZips)
                                        {
                                            if (copyLocal)
                                            {
                                                if (TryExtractByFilename(zipPath, ddsFileName, ddsLocalFileDiskPath)) 
                                                { 
                                                    found = true;
                                                    fileName = ddsFileName;
                                                    localFileDiskPath = ddsLocalFileDiskPath;
                                                    newVfsPath = $"{mapRootVfs}/{LocalArtPath}/{fileName}";
                                                    break; 
                                                }
                                            }
                                            else
                                            {
                                                var foundPath = TryFindByFilenameInZip(zipPath, ddsFileName);
                                                if (foundPath != null)
                                                {
                                                    found = true;
                                                    fileName = ddsFileName;
                                                    newVfsPath = foundPath;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }

                                if (found && newVfsPath != null)
                                {
                                    stage[key] = newVfsPath;
                                    _logger?.LogInformation("[FIXED] {FileName} -> {NewPath}", fileName, newVfsPath);
                                    fixedCount++;
                                }
                                else
                                {
                                    _logger?.LogDebug("    File not found in any zip: {FileName}", fileName);
                                }
                            }
                        }
                    }
                }
            }
            catch (ArgumentException ex) when (ex.Message.Contains("same key"))
            {
                _logger?.LogWarning("Material '{MaterialName}' has duplicate keys and was skipped. Please fix manually", material.Key);
                _logger?.LogDebug("Duplicate key details: {Message}", ex.Message);
                continue;
            }
        }

        // Save Changes
        _logger?.LogDebug("Saving modified JSON to: {JsonPath}", jsonPath);
        File.WriteAllText(jsonPath, rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        
        _logger?.LogInformation("Localized: {FixedCount} | Skipped: {SkippedCount}", fixedCount, skippedCount);
        _logger?.LogDebug("File processing complete");

        return (fixedCount, skippedCount);
    }

    private static bool TryExtract(string zipPath, string vfsPath, string outPath)
    {
        if (!File.Exists(zipPath)) 
        {
            _logger?.LogDebug("      Zip file not found: {ZipPath}", zipPath);
            return false;
        }
        
        _logger?.LogDebug("      Opening zip: {ZipPath}", Path.GetFileName(zipPath));
        
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            string fileName = Path.GetFileName(vfsPath);
            
            _logger?.LogDebug("      Searching for entry: {FileName}", fileName);
            
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (entry != null) 
            { 
                _logger?.LogDebug("      Found entry, extracting to: {OutPath}", outPath);
                entry.ExtractToFile(outPath, true); 
                _logger?.LogDebug("      Successfully extracted");
                return true; 
            }
            else
            {
                _logger?.LogDebug("      Entry not found in this zip");
            }
        }
        catch (Exception ex) 
        { 
            _logger?.LogError("Error reading {ZipFile}: {Message}", Path.GetFileName(zipPath), ex.Message);
        }
        return false;
    }

    private static string? TryFindInZip(string zipPath, string vfsPath)
    {
        if (!File.Exists(zipPath)) 
        {
            _logger?.LogDebug("      Zip file not found: {ZipPath}", zipPath);
            return null;
        }
        
        _logger?.LogDebug("      Opening zip: {ZipPath}", Path.GetFileName(zipPath));
        
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            string fileName = Path.GetFileName(vfsPath);
            
            _logger?.LogDebug("      Searching for entry: {FileName}", fileName);
            
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (entry != null) 
            { 
                // Convert zip entry path to VFS path
                string entryPath = entry.FullName.Replace('\\', '/');
                string vfsResult = $"/{entryPath}";
                _logger?.LogDebug("      Found entry at: {VfsPath}", vfsResult);
                return vfsResult;
            }
            else
            {
                _logger?.LogDebug("      Entry not found in this zip");
            }
        }
        catch (Exception ex) 
        { 
            _logger?.LogError("Error reading {ZipFile}: {Message}", Path.GetFileName(zipPath), ex.Message);
        }
        return null;
    }

    private static bool TryExtractByFilename(string zipPath, string fileName, string outPath)
    {
        if (!File.Exists(zipPath)) 
        {
            _logger?.LogDebug("      Zip file not found: {ZipPath}", zipPath);
            return false;
        }
        
        _logger?.LogDebug("      Opening zip: {ZipPath}", Path.GetFileName(zipPath));
        
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            
            _logger?.LogDebug("      Searching for filename '{FileName}' in all subfolders", fileName);
            
            // Search for the file by name in any folder within the zip
            var entry = archive.Entries.FirstOrDefault(e => 
                !string.IsNullOrEmpty(e.Name) && 
                e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            
            if (entry != null) 
            { 
                _logger?.LogDebug("      Found entry at: {FullName}, extracting to: {OutPath}", entry.FullName, outPath);
                entry.ExtractToFile(outPath, true); 
                _logger?.LogDebug("      Successfully extracted");
                return true; 
            }
            else
            {
                _logger?.LogDebug("      Filename not found in this zip");
            }
        }
        catch (Exception ex) 
        { 
            _logger?.LogError("Error reading {ZipFile}: {Message}", Path.GetFileName(zipPath), ex.Message);
        }
        return false;
    }

    private static string? TryFindByFilenameInZip(string zipPath, string fileName)
    {
        if (!File.Exists(zipPath)) 
        {
            _logger?.LogDebug("      Zip file not found: {ZipPath}", zipPath);
            return null;
        }
        
        _logger?.LogDebug("      Opening zip: {ZipPath}", Path.GetFileName(zipPath));
        
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            
            _logger?.LogDebug("      Searching for filename '{FileName}' in all subfolders", fileName);
            
            // Search for the file by name in any folder within the zip
            var entry = archive.Entries.FirstOrDefault(e => 
                !string.IsNullOrEmpty(e.Name) && 
                e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            
            if (entry != null) 
            { 
                // Convert zip entry path to VFS path
                string entryPath = entry.FullName.Replace('\\', '/');
                string vfsResult = $"/{entryPath}";
                _logger?.LogDebug("      Found entry at: {VfsPath}", vfsResult);
                return vfsResult;
            }
            else
            {
                _logger?.LogDebug("      Filename not found in this zip");
            }
        }
        catch (Exception ex) 
        { 
            _logger?.LogError("Error reading {ZipFile}: {Message}", Path.GetFileName(zipPath), ex.Message);
        }
        return null;
    }
}