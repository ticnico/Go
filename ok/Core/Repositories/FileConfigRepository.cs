using RuriLib.Helpers;
using RuriLib.Models.Configs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Core.Repositories;

/// <summary>
/// SECURE repository: Files are XOR+AES encrypted and INCOMPATIBLE with OpenBullet2.
/// Even if renamed to .opk, CorePacker.UnpackAsync() will FAIL.
/// </summary>
public class FileConfigRepository : IConfigRepository
{
    private readonly string _configsFolder;

    // 🔐 Magic Header: Must be FIRST bytes. CorePacker doesn't expect this.
    private static readonly byte[] MagicHeader = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };

    // 🔐 XOR Key (simple but effective when combined with header check)
    private static readonly byte XorKey = 0xA7;

    // 🔐 AES Key/IV for extra security (optional but recommended)
    private static readonly byte[] AesKey = Encoding.UTF8.GetBytes("85b1188f86d966cc4109175c15ab5f92034c0850667d985c01bd7c3463170e57");
    private static readonly byte[] AesIV = Encoding.UTF8.GetBytes("9a35d388faf5e505a5f2983c0c5ad9aa");

    public FileConfigRepository(string configsFolder)
    {
        _configsFolder = configsFolder;
        Directory.CreateDirectory(_configsFolder);
    }

    public async Task<Config> CreateAsync(string id = null)
    {
        var config = new Config
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Metadata = new ConfigMetadata { Name = "New Config" },
            IsRemote = false
        };
        await SaveAsync(config);
        return config;
    }

    public void Delete(Config config)
    {
        var safeName = SanitizeFileName(config.Metadata.Name);
        var filePath = Path.Combine(_configsFolder, $"{safeName}.Tic");
        if (File.Exists(filePath)) File.Delete(filePath);
    }

    public async Task<Config> GetAsync(string id)
    {
        var files = Directory.GetFiles(_configsFolder, "*.Tic");
        foreach (var file in files)
        {
            try
            {
                var config = await ReadSecureFileAsync(file);
                if (config?.Id == id) return config;
            }
            catch { continue; }
        }
        throw new FileNotFoundException($"Config with ID '{id}' not found");
    }

    public async Task<IEnumerable<Config>> GetAllAsync()
    {
        var configs = new List<Config>();
        var files = Directory.GetFiles(_configsFolder, "*.Tic");
        foreach (var file in files)
        {
            try
            {
                var config = await ReadSecureFileAsync(file);
                if (config != null) configs.Add(config);
            }
            catch { continue; }
        }
        return configs;
    }

    public async Task<byte[]> GetBytesAsync(string id)
    {
        var files = Directory.GetFiles(_configsFolder, "*.Tic");
        foreach (var file in files)
        {
            try
            {
                var config = await ReadSecureFileAsync(file);
                if (config?.Id == id) return await File.ReadAllBytesAsync(file);
            }
            catch { continue; }
        }
        throw new FileNotFoundException($"Config bytes with ID '{id}' not found");
    }

    public async Task SaveAsync(Config config)
    {
        var safeName = SanitizeFileName(config.Metadata.Name);
        var filePath = Path.Combine(_configsFolder, $"{safeName}.Tic");

        // 1️⃣ Pack with RuriLib
        var packed = await CorePacker.PackAsync(config);

        // 2️⃣ XOR scramble (breaks CorePacker compatibility)
        var xored = XorBytes(packed);

        // 3️⃣ AES encrypt (optional but recommended)
        var encrypted = EncryptBytes(xored);

        // 4️⃣ Prepend magic header
        var final = new byte[MagicHeader.Length + encrypted.Length];
        Buffer.BlockCopy(MagicHeader, 0, final, 0, MagicHeader.Length);
        Buffer.BlockCopy(encrypted, 0, final, MagicHeader.Length, encrypted.Length);

        await File.WriteAllBytesAsync(filePath, final);
    }

    public async Task UploadAsync(Stream stream, string fileName)
    {
        var safeName = SanitizeFileName(Path.GetFileNameWithoutExtension(fileName));
        var filePath = Path.Combine(_configsFolder, $"{safeName}.Tic");

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var raw = ms.ToArray();

        // Apply same transformation as SaveAsync
        var xored = XorBytes(raw);
        var encrypted = EncryptBytes(xored);
        var final = new byte[MagicHeader.Length + encrypted.Length];
        Buffer.BlockCopy(MagicHeader, 0, final, 0, MagicHeader.Length);
        Buffer.BlockCopy(encrypted, 0, final, MagicHeader.Length, encrypted.Length);

        await File.WriteAllBytesAsync(filePath, final);
    }

    // 🔐 Read + Verify + Decrypt + UnXOR + Unpack
    private async Task<Config> ReadSecureFileAsync(string filePath)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);

        // 1️⃣ Verify header (CorePacker will fail here if header missing)
        if (fileBytes.Length < MagicHeader.Length)
            throw new InvalidOperationException("Invalid file format");
        for (int i = 0; i < MagicHeader.Length; i++)
            if (fileBytes[i] != MagicHeader[i])
                throw new InvalidOperationException("Invalid file format");

        // 2️⃣ Extract encrypted payload
        var encrypted = new byte[fileBytes.Length - MagicHeader.Length];
        Buffer.BlockCopy(fileBytes, MagicHeader.Length, encrypted, 0, encrypted.Length);

        // 3️⃣ Decrypt
        var xored = DecryptBytes(encrypted);

        // 4️⃣ UnXOR
        var packed = XorBytes(xored); // XOR is reversible

        // 5️⃣ Unpack with RuriLib
        using var ms = new MemoryStream(packed);
        return await CorePacker.UnpackAsync(ms);
    }

    // 🔀 Simple XOR scramble (breaks CorePacker)
    private byte[] XorBytes(byte[] data)
    {
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] ^ XorKey);
        return result;
    }

    // 🔐 AES Encrypt
    private byte[] EncryptBytes(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = AesKey;
        aes.IV = AesIV;
        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            cs.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    // 🔐 AES Decrypt
    private byte[] DecryptBytes(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = AesKey;
        aes.IV = AesIV;
        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(data);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var result = new MemoryStream();
        cs.CopyTo(result);
        return result.ToArray();
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Untitled";
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = name;
        foreach (var c in invalidChars) sanitized = sanitized.Replace(c.ToString(), "_");
        while (sanitized.Contains("__")) sanitized = sanitized.Replace("__", "_");
        sanitized = sanitized.Trim('_', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized;
    }
}
