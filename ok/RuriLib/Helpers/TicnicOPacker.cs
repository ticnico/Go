using Newtonsoft.Json;
using RuriLib.Extensions;
using RuriLib.Helpers.Transpilers;
using RuriLib.Models.Configs;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RuriLib.Helpers
{
    /// <summary>
    /// Takes care of packing and unpacking <see cref="Config"/> objects.
    /// 🔐 Core SECURE: Magic Header + AES-256 Encryption.
    /// Files CANNOT be opened with 7zip or any archive tool.
    /// ✅ FIXED: Using exact hex-encoded keys (no size errors).
    /// </summary>
    public static class CorePacker
    {
        // 🔐 Core CUSTOM MAGIC HEADER (5 bytes)
        private static readonly byte[] CoreMagicHeader = new byte[] { 0x54, 0x49, 0x43, 0x4E, 0x99 };
        private static readonly byte[] ZipSignature = new byte[] { 0x50, 0x4B, 0x03, 0x04 };

        // 🔐 AES-256 Key (32 bytes) - YOUR EXACT KEY
        private static readonly byte[] AesKey = HexStringToBytes("85b1188f86d966cc4109175c15ab5f92034c0850667d985c01bd7c3463170e57");

        // 🔐 AES IV (16 bytes) - YOUR EXACT IV
        private static readonly byte[] AesIV = HexStringToBytes("9a35d388faf5e505a5f2983c0c5ad9aa");

        private static readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented
        };

        // ✅ Helper: Convert hex string to byte array
        private static byte[] HexStringToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0)
                throw new ArgumentException("Invalid hex string");

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// Packs the <paramref name="config"/> and returns ENCRYPTED .tic format bytes.
        /// 7zip CANNOT open this - the ZIP payload is encrypted.
        /// </summary>
        public static async Task<byte[]> PackAsync(Config config)
        {
            using var packageStream = new MemoryStream();
            using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, true))
            {
                await CreateZipEntryFromString(archive, "readme.md", config.Readme);
                await CreateZipEntryFromString(archive, "metadata.json", JsonConvert.SerializeObject(config.Metadata, jsonSettings));
                await CreateZipEntryFromString(archive, "settings.json", JsonConvert.SerializeObject(config.Settings, jsonSettings));

                switch (config.Mode)
                {
                    case ConfigMode.Stack:
                        config.LoliCodeScript = Stack2LoliTranspiler.Transpile(config.Stack);
                        await CreateZipEntryFromString(archive, "script.loli", config.LoliCodeScript);
                        await CreateZipEntryFromString(archive, "startup.loli", config.StartupLoliCodeScript);
                        break;
                    case ConfigMode.LoliCode:
                        await CreateZipEntryFromString(archive, "script.loli", config.LoliCodeScript);
                        await CreateZipEntryFromString(archive, "startup.loli", config.StartupLoliCodeScript);
                        break;
                    case ConfigMode.CSharp:
                        await CreateZipEntryFromString(archive, "script.cs", config.CSharpScript);
                        await CreateZipEntryFromString(archive, "startup.cs", config.StartupCSharpScript);
                        break;
                    case ConfigMode.DLL:
                        await CreateZipEntryFromBytes(archive, "build.dll", config.DLLBytes);
                        break;
                    case ConfigMode.Legacy:
                        await CreateZipEntryFromString(archive, "script.legacy", config.LoliScript);
                        break;
                    default:
                        throw new NotSupportedException();
                }
            }

            config.UpdateHashes();
            var packedBytes = packageStream.ToArray();

            // 🔐 SECURITY LAYER 1: AES-256 Encrypt the ZIP payload
            var encryptedBytes = EncryptBytes(packedBytes);

            // 🔐 SECURITY LAYER 2: Prepend Magic Header
            var finalBytes = new byte[CoreMagicHeader.Length + encryptedBytes.Length];
            Buffer.BlockCopy(CoreMagicHeader, 0, finalBytes, 0, CoreMagicHeader.Length);
            Buffer.BlockCopy(encryptedBytes, 0, finalBytes, CoreMagicHeader.Length, encryptedBytes.Length);

            return finalBytes;
        }

        /// <summary>
        /// Packs multiple configs into a single archive.
        /// </summary>
        public static async Task<byte[]> PackAsync(IEnumerable<Config> configs)
        {
            var fileNames = new Dictionary<string, int>();
            using var packageStream = new MemoryStream();
            using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, true))
            {
                foreach (var config in configs)
                {
                    var fileName = config.Metadata.Name.ToValidFileName();
                    if (fileNames.ContainsKey(fileName))
                    {
                        fileNames[fileName]++;
                        fileName += fileNames[fileName];
                    }
                    else
                    {
                        fileNames[fileName] = 1;
                    }
                    var packedConfig = await PackAsync(config);
                    await CreateZipEntryFromBytes(archive, fileName + ".Tic", packedConfig);
                }
            }
            return packageStream.ToArray();
        }

        /// <summary>
        /// Unpacks a stream - AUTO-DETECTS format (.tic, .opk, or standalone .loli).
        /// </summary>
        public static async Task<Config> UnpackAsync(Stream stream)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var fileBytes = ms.ToArray();

            if (fileBytes.Length == 0)
                throw new InvalidOperationException("Empty config file");

            // 🔍 Detect .tic format (encrypted)
            if (fileBytes.Length >= CoreMagicHeader.Length &&
                fileBytes.Take(CoreMagicHeader.Length).SequenceEqual(CoreMagicHeader))
            {
                var encryptedBytes = new byte[fileBytes.Length - CoreMagicHeader.Length];
                Buffer.BlockCopy(fileBytes, CoreMagicHeader.Length, encryptedBytes, 0, encryptedBytes.Length);
                var zipBytes = DecryptBytes(encryptedBytes);
                using var zipStream = new MemoryStream(zipBytes);
                return await UnpackZipPayloadAsync(zipStream);
            }

            // 🔍 Detect .opk format (raw ZIP)
            if (fileBytes.Length >= ZipSignature.Length &&
                fileBytes.Take(ZipSignature.Length).SequenceEqual(ZipSignature))
            {
                using var zipStream = new MemoryStream(fileBytes);
                return await UnpackZipPayloadAsync(zipStream);
            }

            // 🔍 Detect standalone .loli script (plain text LoliCode)
            try
            {
                var scriptContent = Encoding.UTF8.GetString(fileBytes).Trim();
                if (!string.IsNullOrWhiteSpace(scriptContent) &&
                    (scriptContent.StartsWith("BLOCK:") ||
                     scriptContent.Contains("REQUEST") ||
                     scriptContent.Contains("KEYCHECK") ||
                     scriptContent.Contains("PARSE")))
                {
                    var config = new Config
                    {
                        Id = Guid.NewGuid().ToString(),
                        Mode = ConfigMode.LoliCode,
                        LoliCodeScript = scriptContent,
                        Metadata = new ConfigMetadata { Name = "Imported Script" },
                        Settings = new ConfigSettings()
                    };
                    config.UpdateHashes(); // ✅ Call separately - it's void
                    return config;         // ✅ Return the config object
                }
            }
            catch { /* Not a valid .loli script, continue to error */ }

            throw new InvalidOperationException("Unrecognized config format: file is neither .tic, .opk, nor a valid .loli script");
        }

        /// <summary>
        /// Internal: Unpacks the ZIP payload into a Config object.
        /// </summary>
        private static async Task<Config> UnpackZipPayloadAsync(Stream stream)
        {
            var config = new Config { Id = Guid.NewGuid().ToString() };

            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                try { config.Readme = ReadStringFromZipEntry(archive, "readme.md"); }
                catch { /* ignore */ }

                var metadataJson = ReadStringFromZipEntry(archive, "metadata.json");
                config.Metadata = JsonConvert.DeserializeObject<ConfigMetadata>(metadataJson, jsonSettings)
                    ?? throw new FileNotFoundException("Failed to deserialize metadata.json");

                var settingsJson = ReadStringFromZipEntry(archive, "settings.json");
                config.Settings = JsonConvert.DeserializeObject<ConfigSettings>(settingsJson, jsonSettings)
                    ?? throw new FileNotFoundException("Failed to deserialize settings.json");

                if (archive.Entries.Any(e => e.Name.Equals("script.cs", StringComparison.OrdinalIgnoreCase)))
                {
                    config.CSharpScript = ReadStringFromZipEntry(archive, "script.cs");
                    config.Mode = ConfigMode.CSharp;
                    config.StartupCSharpScript = ReadStringFromZipEntry(archive, "startup.cs", essential: false);
                }
                else if (archive.Entries.Any(e => e.Name.Equals("build.dll", StringComparison.OrdinalIgnoreCase)))
                {
                    config.DLLBytes = ReadBytesFromZipEntry(archive, "build.dll");
                    config.Mode = ConfigMode.DLL;
                }
                else if (archive.Entries.Any(e => e.Name.Equals("script.legacy", StringComparison.OrdinalIgnoreCase)))
                {
                    config.LoliScript = ReadStringFromZipEntry(archive, "script.legacy");
                    config.Mode = ConfigMode.Legacy;
                }
                else
                {
                    config.LoliCodeScript = ReadStringFromZipEntry(archive, "script.loli");
                    config.Mode = ConfigMode.LoliCode;
                    config.StartupLoliCodeScript = ReadStringFromZipEntry(archive, "startup.loli", essential: false);
                }
            }

            config.UpdateHashes();
            return config;
        }

        /// <summary>
        /// Exports a config in STOCK .opk format (for sharing with OpenBullet2 users).
        /// </summary>
        public static async Task<byte[]> ExportAsOpkAsync(Config config)
        {
            using var packageStream = new MemoryStream();
            using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, true))
            {
                await CreateZipEntryFromString(archive, "readme.md", config.Readme);
                await CreateZipEntryFromString(archive, "metadata.json", JsonConvert.SerializeObject(config.Metadata, jsonSettings));
                await CreateZipEntryFromString(archive, "settings.json", JsonConvert.SerializeObject(config.Settings, jsonSettings));

                switch (config.Mode)
                {
                    case ConfigMode.Stack:
                        config.LoliCodeScript = Stack2LoliTranspiler.Transpile(config.Stack);
                        await CreateZipEntryFromString(archive, "script.loli", config.LoliCodeScript);
                        await CreateZipEntryFromString(archive, "startup.loli", config.StartupLoliCodeScript);
                        break;
                    case ConfigMode.LoliCode:
                        await CreateZipEntryFromString(archive, "script.loli", config.LoliCodeScript);
                        await CreateZipEntryFromString(archive, "startup.loli", config.StartupLoliCodeScript);
                        break;
                    case ConfigMode.CSharp:
                        await CreateZipEntryFromString(archive, "script.cs", config.CSharpScript);
                        await CreateZipEntryFromString(archive, "startup.cs", config.StartupCSharpScript);
                        break;
                    case ConfigMode.DLL:
                        await CreateZipEntryFromBytes(archive, "build.dll", config.DLLBytes);
                        break;
                    case ConfigMode.Legacy:
                        await CreateZipEntryFromString(archive, "script.legacy", config.LoliScript);
                        break;
                    default:
                        throw new NotSupportedException();
                }
            }
            config.UpdateHashes();
            return packageStream.ToArray();
        }

        // 🔐 AES-256 Encrypt
        private static byte[] EncryptBytes(byte[] data)
        {
            using var aes = Aes.Create();
            aes.Key = AesKey; // 32 bytes ✅
            aes.IV = AesIV;   // 16 bytes ✅
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(data, 0, data.Length);
            }
            return ms.ToArray();
        }

        // 🔐 AES-256 Decrypt
        private static byte[] DecryptBytes(byte[] data)
        {
            using var aes = Aes.Create();
            aes.Key = AesKey;
            aes.IV = AesIV;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(data);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var result = new MemoryStream();
            cs.CopyTo(result);
            return result.ToArray();
        }

        private static async Task CreateZipEntryFromString(ZipArchive archive, string path, string content)
        {
            var zipFile = archive.CreateEntry(path);
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await using var dest = zipFile.Open();
            await source.CopyToAsync(dest);
        }

        private static async Task CreateZipEntryFromBytes(ZipArchive archive, string path, byte[] content)
        {
            var zipFile = archive.CreateEntry(path);
            using var source = new MemoryStream(content);
            await using var dest = zipFile.Open();
            await source.CopyToAsync(dest);
        }

        private static string ReadStringFromZipEntry(ZipArchive archive, string path, bool essential = true)
            => Encoding.UTF8.GetString(ReadBytesFromZipEntry(archive, path, essential));

        private static byte[] ReadBytesFromZipEntry(ZipArchive archive, string path, bool essential = true)
        {
            var entry = archive.GetEntry(path);
            if (entry == null && !essential) return Array.Empty<byte>();
            if (entry == null && essential) throw new FileNotFoundException($"Entry '{path}' not found in archive");

            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
