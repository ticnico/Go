using Core.Exceptions;
using RuriLib.Helpers;
using RuriLib.Helpers.Transpilers;
using RuriLib.Legacy.Configs;
using RuriLib.Models.Configs;
using RuriLib.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Repositories;

/// <summary>
/// Stores configs on disk using Metadata.Name as filename (not GUID).
/// ✅ FIXED: Consistent filename handling for save/load.
/// </summary>
public class DiskConfigRepository : IConfigRepository
{
    private readonly RuriLibSettingsService _rlSettings;

    private string BaseFolder { get; init; }

    public DiskConfigRepository(RuriLibSettingsService rlSettings, string baseFolder)
    {
        _rlSettings = rlSettings;
        BaseFolder = baseFolder;
        Directory.CreateDirectory(baseFolder);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Config>> GetAllAsync()
    {
        // Try to convert legacy configs automatically before loading
        foreach (var file in Directory.GetFiles(BaseFolder).Where(file => file.EndsWith(".loli", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var id = Path.GetFileNameWithoutExtension(file);
                var converted = ConfigConverter.Convert(File.ReadAllText(file), id);
                await SaveAsync(converted);
                File.Delete(file);
                Console.WriteLine($"Converted legacy .loli config ({file}) to the new .Tic format");
            }
            catch
            {
                Console.WriteLine($"Could not convert legacy .loli config ({file}) to the new .Tic format");
            }
        }

        var configs = new List<Config>();
        var files = Directory.GetFiles(BaseFolder, "*.Tic");

        foreach (var file in files)
        {
            try
            {
                await using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);
                var config = await CorePacker.UnpackAsync(fileStream);

                // ✅ Set Id from filename (sanitized name) for consistency
                config.Id = Path.GetFileNameWithoutExtension(file);
                configs.Add(config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not unpack {file} properly: {ex.Message}");
                // Continue loading other configs instead of failing all
            }
        }

        return configs;
    }

    /// <inheritdoc/>
    public async Task<Config> GetAsync(string id)
    {
        // ✅ Search by iterating files and matching config.Id inside
        // Because files are named by Metadata.Name, not GUID
        var files = Directory.GetFiles(BaseFolder, "*.Tic");

        foreach (var file in files)
        {
            try
            {
                await using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);
                var config = await CorePacker.UnpackAsync(fileStream);

                if (config.Id == id)
                {
                    // ✅ Ensure Id is set correctly
                    config.Id = Path.GetFileNameWithoutExtension(file);
                    return config;
                }
            }
            catch { continue; } // Try next file
        }

        // ✅ Fallback: try direct filename lookup (in case id == sanitized name)
        var directFile = GetFileName(id);
        if (File.Exists(directFile))
        {
            await using var fileStream = new FileStream(directFile, FileMode.Open, FileAccess.Read);
            var config = await CorePacker.UnpackAsync(fileStream);
            config.Id = id;
            return config;
        }

        throw new FileNotFoundException($"Config with ID '{id}' not found");
    }

    /// <inheritdoc/>
    public async Task<byte[]> GetBytesAsync(string id)
    {
        // ✅ Search by iterating files and matching config.Id inside
        var files = Directory.GetFiles(BaseFolder, "*.Tic");

        foreach (var file in files)
        {
            try
            {
                await using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);
                var config = await CorePacker.UnpackAsync(fileStream);

                if (config.Id == id)
                {
                    return await File.ReadAllBytesAsync(file);
                }
            }
            catch { continue; }
        }

        // ✅ Fallback: direct filename lookup
        var directFile = GetFileName(id);
        if (File.Exists(directFile))
        {
            return await File.ReadAllBytesAsync(directFile);
        }

        throw new FileNotFoundException($"Config bytes with ID '{id}' not found");
    }

    /// <inheritdoc/>
    public async Task<Config> CreateAsync(string id = null)
    {
        var config = new Config { Id = id ?? Guid.NewGuid().ToString() };

        config.Settings.DataSettings.AllowedWordlistTypes = [
            _rlSettings.Environment.WordlistTypes.First().Name
        ];

        await SaveAsync(config);
        return config;
    }

    /// <inheritdoc/>
    public async Task UploadAsync(Stream stream, string fileName)
    {
        var extension = Path.GetExtension(fileName);

        if (extension.Equals(".Tic", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".opk", StringComparison.OrdinalIgnoreCase))
        {
            var config = await CorePacker.UnpackAsync(stream);

            // Preserve the original uploaded filename when importing so the
            // stored file name matches the uploaded file instead of the
            // internal config.Id (which can be a GUID).
            var uploadedBase = Path.GetFileNameWithoutExtension(fileName);
            if (!string.IsNullOrWhiteSpace(uploadedBase))
            {
                config.Id = SanitizeFileName(uploadedBase);
            }

            await File.WriteAllBytesAsync(GetFileName(config), await CorePacker.PackAsync(config));
        }
        else if (extension.Equals(".loli", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Seek(0, SeekOrigin.Begin);
            var content = Encoding.UTF8.GetString(ms.ToArray());
            var id = Path.GetFileNameWithoutExtension(fileName);
            var converted = ConfigConverter.Convert(content, id);
            await SaveAsync(converted);
        }
        else
        {
            throw new UnsupportedFileTypeException($"Unsupported file type: {extension}");
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(Config config)
    {
        // Update the last modified date
        config.Metadata.LastModified = DateTime.Now;

        // If it's possible to retrieve the block descriptors, get required plugins
        if (config.Mode is ConfigMode.Stack or ConfigMode.LoliCode)
        {
            try
            {
                var stack = config.Mode is ConfigMode.Stack
                    ? config.Stack
                    : Loli2StackTranspiler.Transpile(config.LoliCodeScript);

                config.Metadata.Plugins = stack.Select(b => b.Descriptor.AssemblyFullName)
                    .Where(n => n != null && !n.Contains("RuriLib")).ToList();
            }
            catch
            {
                // Don't do anything if we can't write metadata
            }
        }

        // Determine current on-disk filename (based on existing Id if present)
        var currentBase = string.IsNullOrWhiteSpace(config.Id)
            ? SanitizeFileName(config.Metadata.Name)
            : SanitizeFileName(config.Id);

        var currentFile = GetFileName(currentBase);

        // Determine desired target filename based on Metadata.Name
        var targetBase = SanitizeFileName(config.Metadata.Name);
        var targetFile = GetFileName(targetBase);

        // If the current file exists but its name differs from the desired target,
        // attempt to rename it (safe move) to preserve a single file instead of
        // creating a duplicate when the user renames the config.
        if (File.Exists(currentFile) && !string.Equals(currentFile, targetFile, StringComparison.OrdinalIgnoreCase))
        {
            // If the target already exists, find a unique filename (append _1, _2, ...)
            var uniqueTarget = targetFile;
            if (File.Exists(uniqueTarget))
            {
                var counter = 1;
                do
                {
                    var candidateBase = $"{targetBase}_{counter++}";
                    uniqueTarget = GetFileName(candidateBase);
                }
                while (File.Exists(uniqueTarget) && !string.Equals(uniqueTarget, currentFile, StringComparison.OrdinalIgnoreCase));
            }

            // Move the file on disk to the unique target name
            try
            {
                File.Move(currentFile, uniqueTarget);
                // Update config.Id to the new on-disk name (without extension)
                config.Id = Path.GetFileNameWithoutExtension(uniqueTarget);
            }
            catch
            {
                // If move fails for any reason, fall back to keeping current file name
                config.Id = Path.GetFileNameWithoutExtension(currentFile);
            }
        }
        else
        {
            // No existing file to rename; ensure Id matches the target base so the
            // file will be saved with the Metadata.Name filename.
            config.Id = targetBase;
        }

        var filePath = GetFileName(config);
        System.Diagnostics.Debug.WriteLine($"💾 Saving: Name={config.Metadata.Name}, File={Path.GetFileName(filePath)}, Id={config.Id}");

        await File.WriteAllBytesAsync(filePath, await CorePacker.PackAsync(config));
    }

    /// <inheritdoc/>
    public void Delete(Config config)
    {
        var file = GetFileName(config);

        if (File.Exists(file))
            File.Delete(file);
    }

    // Use config.Id (sanitized) as the filename when present. This preserves the
    // original on-disk filename even if Metadata.Name changes (prevents duplicates
    // when renaming in the UI). If Id is missing, fall back to Metadata.Name.
    private string GetFileName(Config config)
        => GetFileName(SanitizeFileName(string.IsNullOrWhiteSpace(config.Id) ? config.Metadata.Name : config.Id));

    // Kept for methods that pass filename-as-id (like GetAllAsync fallback)
    private string GetFileName(string name)
        => Path.Combine(BaseFolder, $"{name}.Tic").Replace('\\', '/');

    // ✅ Helper: Sanitize filenames to avoid invalid path characters
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Untitled";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = name;

        foreach (var c in invalidChars)
            sanitized = sanitized.Replace(c.ToString(), "_");

        while (sanitized.Contains("__"))
            sanitized = sanitized.Replace("__", "_");

        sanitized = sanitized.Trim('_', ' ');

        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }
}
