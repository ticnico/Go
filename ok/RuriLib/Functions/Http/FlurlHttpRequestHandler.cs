using RuriLib.Functions.Files;
using RuriLib.Functions.Http.Options;
using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart; // Your custom classes
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
// Fix ambiguity
using HttpRequestOptions = RuriLib.Functions.Http.Options.HttpRequestOptions;

namespace RuriLib.Functions.Http
{
    internal class FlurlHttpRequestHandler : HttpRequestHandler
    {
        // ─── Standard Request ──────────────────────────────────────────────
        public override async Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            ValidateBotData(data);
            ValidateOptions(options);

            await ExecuteFlurlRequest(data, options, async (client, request) =>
            {
                string body = null;
                HttpContent content = null;

                // Determine if we should send content
                bool hasContent = !string.IsNullOrEmpty(options.Content) || options.AlwaysSendContent;

                if (hasContent)
                {
                    body = options.Content ?? string.Empty;

                    // Only URL Encode if explicitly requested
                    if (options.UrlEncodeContent)
                    {
                        try
                        {
                            body = Uri.EscapeDataString(body);
                            body = body.Replace("%26", "&").Replace("%3D", "=");
                        }
                        catch (UriFormatException)
                        {
                            data.Logger.Log("[WARNING] URL Encoding failed, sending raw content.", LogColors.DarkOrange);
                        }
                    }

                    // Determine Content Type from the Input Field
                    string contentType = options.ContentType;

                    // Default to application/x-www-form-urlencoded if empty, but usually JSON APIs need explicit type
                    if (string.IsNullOrWhiteSpace(contentType))
                    {
                        contentType = "application/x-www-form-urlencoded";
                    }

                    // Create StringContent with UTF8 encoding initially
                    var stringContent = new StringContent(body, Encoding.UTF8);

                    // CLEAR the automatically added Content-Type header (which usually includes charset)
                    // This ensures we have full control over the header string to match your Signature
                    stringContent.Headers.ContentType = null;

                    // ADD the exact Content-Type header as specified in the Input Field
                    // This handles "application/json" AND "application/json; charset=utf-8" correctly
                    stringContent.Headers.TryAddWithoutValidation("Content-Type", contentType);

                    content = stringContent;
                }

                // Assign content to the request object
                request.Content = content;

                LogHttpRequestData(data, request, body);

                // SendAsync only takes HttpRequestMessage and CancellationToken
                return await client.SendAsync(request, data.CancellationToken).ConfigureAwait(false);
            });
        }

        // ─── Raw Request (Not Supported) ───────────────────────────────────
        public override Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            data.Logger.Log("[Flurl] Raw HTTP requests are not supported. Use RuriLibHttp or SystemNet instead.", LogColors.DarkOrange);
            throw new NotSupportedException("FlurlHttpRequestHandler does not support raw HTTP requests.");
        }

        // ─── Basic Auth Request ────────────────────────────────────────────
        public override async Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            ValidateBotData(data);
            ValidateOptions(options);

            await ExecuteFlurlRequest(data, options, async (client, request) =>
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

                LogHttpRequestData(data, request);
                return await client.SendAsync(request, data.CancellationToken).ConfigureAwait(false);
            });
        }

        // ─── Multipart Request ─────────────────────────────────────────────
        public override async Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
        {
            ValidateBotData(data);
            ValidateOptions(options);

            await ExecuteFlurlRequest(data, options, async (client, request) =>
            {
                string boundary = string.IsNullOrWhiteSpace(options.Boundary) ? GenerateMultipartBoundary() : options.Boundary;

                var multipartContent = new MultipartFormDataContent(boundary);

                if (options.Contents != null)
                {
                    foreach (var c in options.Contents)
                    {
                        if (c == null) continue;

                        switch (c)
                        {
                            case StringHttpContent x:
                                var sContent = new StringContent(x.Data ?? string.Empty, Encoding.UTF8, x.ContentType ?? "text/plain");
                                multipartContent.Add(sContent, x.Name ?? "undefined");
                                break;
                            case RawHttpContent x:
                                var bContent = new ByteArrayContent(x.Data ?? Array.Empty<byte>());
                                bContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(x.ContentType ?? "application/octet-stream");
                                multipartContent.Add(bContent, x.Name ?? "undefined");
                                break;
                            case FileHttpContent x:
                                var fileName = x.FileName ?? string.Empty;
                                lock (FileLocker.GetHandle(fileName))
                                {
                                    if (data.Providers?.Security?.RestrictBlocksToCWD == true)
                                        FileUtils.ThrowIfNotInCWD(fileName);

                                    if (File.Exists(fileName))
                                    {
                                        var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                                        var fileContent = new StreamContent(stream);
                                        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(x.ContentType ?? "application/octet-stream");
                                        multipartContent.Add(fileContent, x.Name ?? "file", Path.GetFileName(fileName));
                                    }
                                    else
                                    {
                                        data.Logger.Log($"[WARNING] File not found: {fileName}", LogColors.DarkOrange);
                                    }
                                }
                                break;
                        }
                    }
                }

                request.Content = multipartContent;

                LogHttpRequestData(data, request, SerializeMultipart(boundary, options.Contents), boundary);
                return await client.SendAsync(request, data.CancellationToken).ConfigureAwait(false);
            });
        }

        // ─── Core Execution Helper ─────────────────────────────────────────
        private async Task ExecuteFlurlRequest<T>(BotData data, T options, Func<HttpClient, HttpRequestMessage, Task<HttpResponseMessage>> execute)
            where T : HttpRequestOptions
        {
            try
            {
                // 1. Merge cookies
                foreach (var cookie in options.CustomCookies)
                {
                    if (!string.IsNullOrEmpty(cookie.Key))
                        data.COOKIES[cookie.Key] = cookie.Value ?? string.Empty;
                }

                // 2. Create HttpClient with Proxy Support
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                };

                if (data.UseProxy && data.Proxy != null && !string.IsNullOrEmpty(data.Proxy.Host))
                {
                    var proxyUri = BuildProxyUri(data.Proxy);
                    var webProxy = new WebProxy(proxyUri)
                    {
                        Credentials = !string.IsNullOrEmpty(data.Proxy.Username)
                            ? new NetworkCredential(data.Proxy.Username, data.Proxy.Password)
                            : null
                    };
                    handler.Proxy = webProxy;
                    handler.UseProxy = true;
                }

                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromMilliseconds(options.TimeoutMilliseconds);

                // Add Default Headers if missing (to avoid basic bot blocks)
                if (!client.DefaultRequestHeaders.UserAgent.Any())
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                if (!client.DefaultRequestHeaders.Accept.Any())
                    client.DefaultRequestHeaders.Accept.ParseAdd("*/*");

                // 3. Build HttpRequestMessage
                using var request = new HttpRequestMessage
                {
                    Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
                    RequestUri = new Uri(options.Url)
                };

                // 4. Add Headers
                foreach (var header in options.CustomHeaders ?? new Dictionary<string, string>())
                {
                    if (string.IsNullOrEmpty(header.Key)) continue;

                    string key = header.Key.Trim();
                    string value = header.Value?.Trim() ?? string.Empty;

                    // Skip headers that HttpClient manages automatically to prevent conflicts
                    if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                    if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    if (key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                    // Note: We do NOT skip Content-Type here because we cleared it in the content object 
                    // and want to allow manual override if necessary, though the Content object sets it primarily.

                    request.Headers.TryAddWithoutValidation(key, value);
                }

                // 5. Add Cookies Header
                if (data.COOKIES?.Any() == true)
                {
                    var cookieHeader = string.Join("; ", data.COOKIES
                        .Where(c => !string.IsNullOrEmpty(c.Key))
                        .Select(c => $"{c.Key}={c.Value}"));
                    if (!string.IsNullOrEmpty(cookieHeader))
                        request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                }

                // 6. Execute
                var response = await execute(client, request).ConfigureAwait(false);

                // 7. Map Response
                await MapFlurlResponseToBotDataAsync(data, response, options).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                data.Logger.Log($"[Flurl] Execute failed: {ex.Message}", LogColors.Red);
                throw;
            }
        }

        // ─── Response Mapping ──────────────────────────────────────────────
        private static async Task MapFlurlResponseToBotDataAsync(BotData data, HttpResponseMessage response, HttpRequestOptions options)
        {
            try
            {
                data.ADDRESS = response.RequestMessage?.RequestUri?.AbsoluteUri ?? options.Url;
                data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);

                data.RESPONSECODE = (int)response.StatusCode;
                data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);

                data.HEADERS = response.Headers.ToDictionary(
                    h => h.Key,
                    h => string.Join(commaHeaders.Contains(h.Key) ? ", " : " ", h.Value));

                if (response.Content?.Headers != null)
                {
                    foreach (var header in response.Content.Headers)
                        data.HEADERS[header.Key] = string.Join(commaHeaders.Contains(header.Key) ? ", " : " ", header.Value);
                }

                // Read RAW source first (Compressed)
                data.RAWSOURCE = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                if (!data.HEADERS.ContainsKey("Content-Length"))
                    data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();

                data.Logger.Log("Received Headers:", LogColors.MediumPurple);
                data.Logger.Log(data.HEADERS.Select(h => $"{h.Key}: {h.Value}"), LogColors.Violet);

                // Handle Cookies from Set-Cookie header
                if (response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
                {
                    foreach (var cookieVal in setCookieHeaders)
                    {
                        if (TryParseCookie(cookieVal, out var cName, out var cValue))
                            data.COOKIES[cName] = cValue;
                    }
                }

                data.Logger.Log("Received Cookies:", LogColors.MikadoYellow);
                data.Logger.Log(data.COOKIES?.Select(c => $"{c.Key}: {c.Value}") ?? Enumerable.Empty<string>(), LogColors.Khaki);

                // Decompress Brotli/GZip manually since we disabled AutomaticDecompression
                string contentEncoding = "";
                if (data.HEADERS.TryGetValue("Content-Encoding", out var enc))
                {
                    contentEncoding = enc.ToLowerInvariant();
                }

                if (contentEncoding.Contains("br"))
                {
                    try
                    {
                        using var input = new MemoryStream(data.RAWSOURCE);
                        using var output = new MemoryStream();
                        await using var brotli = new BrotliStream(input, CompressionMode.Decompress, false);
                        await brotli.CopyToAsync(output);
                        data.RAWSOURCE = output.ToArray();
                    }
                    catch (Exception ex)
                    {
                        data.Logger.Log($"[WARNING] Brotli decompress failed: {ex.Message}", LogColors.DarkOrange);
                    }
                }
                else if (contentEncoding.Contains("gzip") || (data.RAWSOURCE.Length > 2 && data.RAWSOURCE[0] == 0x1F && data.RAWSOURCE[1] == 0x8B))
                {
                    try
                    {
                        data.RAWSOURCE = GZip.Unzip(data.RAWSOURCE);
                    }
                    catch (Exception ex)
                    {
                        data.Logger.Log($"[WARNING] GZip decompress failed: {ex.Message}", LogColors.DarkOrange);
                    }
                }

                // ─── Check if content is binary ────────────────────────────────
                data.HEADERS.TryGetValue("Content-Type", out var contentType);
                bool isBinary = IsBinaryContent(data.RAWSOURCE, contentType);

                if (isBinary)
                {
                    // Display as hex dump
                    string hexDump = FormatHexDump(data.RAWSOURCE);
                    data.SOURCE = $"[Binary Content - {data.RAWSOURCE.Length} bytes]\r\n\r\n" + hexDump;
                    data.Logger.Log($"Received Payload (Binary - {data.RAWSOURCE.Length} bytes, Hex View):", LogColors.ForestGreen);
                    data.Logger.Log(hexDump, LogColors.GreenYellow, true);
                }
                else
                {
                    // Decode Source (text content)
                    if (!string.IsNullOrWhiteSpace(options.CodePagesEncoding))
                    {
                        data.SOURCE = CodePagesEncodingProvider.Instance
                            .GetEncoding(options.CodePagesEncoding)?.GetString(data.RAWSOURCE) ?? Encoding.UTF8.GetString(data.RAWSOURCE);
                    }
                    else
                    {
                        data.SOURCE = Encoding.UTF8.GetString(data.RAWSOURCE);
                    }

                    if (options.DecodeHtml)
                        data.SOURCE = WebUtility.HtmlDecode(data.SOURCE);

                    data.Logger.Log("Received Payload:", LogColors.ForestGreen);
                    data.Logger.Log(data.SOURCE, LogColors.GreenYellow, true);
                }
            }
            catch (Exception ex)
            {
                data.Logger.Log($"[Flurl] Response mapping error: {ex.Message}", LogColors.DarkOrange);
            }
        }

        // ─── Binary Detection Helper ───────────────────────────────────────
        private static bool IsBinaryContent(byte[] data, string contentType)
        {
            if (data == null || data.Length == 0) return false;

            // Check content-type header
            if (!string.IsNullOrEmpty(contentType))
            {
                contentType = contentType.ToLowerInvariant();
                if (contentType.Contains("image/") ||
                    contentType.Contains("application/octet-stream") ||
                    contentType.Contains("application/zip") ||
                    contentType.Contains("application/x-zip") ||
                    contentType.Contains("application/pdf") ||
                    contentType.Contains("application/x-rar") ||
                    contentType.Contains("application/x-7z") ||
                    contentType.Contains("video/") ||
                    contentType.Contains("audio/") ||
                    contentType.Contains("application/x-executable") ||
                    contentType.Contains("application/vnd.") ||
                    contentType.Contains("application/x-dosexec"))
                    return true;
            }

            // Check for binary file signatures (magic numbers)
            if (data.Length >= 4)
            {
                // PNG: 89 50 4E 47
                if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
                // JPEG: FF D8 FF
                if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
                // GIF: 47 49 46
                if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return true;
                // ZIP/JAR/DOCX: 50 4B 03 04
                if (data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04) return true;
                // PDF: 25 50 44 46
                if (data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46) return true;
                // RAR: 52 61 72 21
                if (data[0] == 0x52 && data[1] == 0x61 && data[2] == 0x72 && data[3] == 0x21) return true;
                // 7Z: 37 7A BC AF
                if (data[0] == 0x37 && data[1] == 0x7A && data[2] == 0xBC && data[3] == 0xAF) return true;
                // GZIP: 1F 8B
                if (data[0] == 0x1F && data[1] == 0x8B) return true;
                // BMP: 42 4D
                if (data[0] == 0x42 && data[1] == 0x4D) return true;
                // TIFF: 49 49 2A 00 or 4D 4D 00 2A
                if (data[0] == 0x49 && data[1] == 0x49 && data[2] == 0x2A && data[3] == 0x00) return true;
                if (data[0] == 0x4D && data[1] == 0x4D && data[2] == 0x00 && data[3] == 0x2A) return true;
                // WEBP: 52 49 46 46 ... 57 45 42 50
                if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
                    && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return true;
                // ICO: 00 00 01 00
                if (data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01 && data[3] == 0x00) return true;
                // EXE/DLL: 4D 5A
                if (data[0] == 0x4D && data[1] == 0x5A) return true;
                // ELF: 7F 45 4C 46
                if (data[0] == 0x7F && data[1] == 0x45 && data[2] == 0x4C && data[3] == 0x46) return true;
                // MP3: FF FB or ID3 tag
                if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return true;
                if (data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33) return true;
                // MP4/MOV: 00 00 00 XX 66 74 79 70
                if (data.Length >= 8 && data[4] == 0x66 && data[5] == 0x74 && data[6] == 0x79 && data[7] == 0x70) return true;
                // WAV: 52 49 46 46 ... 57 41 56 45
                if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
                    && data[8] == 0x57 && data[9] == 0x41 && data[10] == 0x56 && data[11] == 0x45) return true;
                // OGG: 4F 67 67 53
                if (data[0] == 0x4F && data[1] == 0x67 && data[2] == 0x67 && data[3] == 0x53) return true;
                // FLAC: 66 4C 61 43
                if (data[0] == 0x66 && data[1] == 0x4C && data[2] == 0x61 && data[3] == 0x43) return true;
                // SQLite: 53 51 4C 69 74 65
                if (data.Length >= 6 && data[0] == 0x53 && data[1] == 0x51 && data[2] == 0x4C && data[3] == 0x69
                    && data[4] == 0x74 && data[5] == 0x65) return true;
                // WOFF: 77 4F 46 46 or 77 4F 46 32
                if (data[0] == 0x77 && data[1] == 0x4F && data[2] == 0x46 && (data[3] == 0x46 || data[3] == 0x32)) return true;
            }

            // Check for null bytes and non-printable characters in the first chunk
            int nullBytes = 0;
            int nonPrintable = 0;
            int checkLength = Math.Min(data.Length, 1024);

            for (int i = 0; i < checkLength; i++)
            {
                if (data[i] == 0x00) nullBytes++;
                else if (data[i] < 0x20 && data[i] != 0x09 && data[i] != 0x0A && data[i] != 0x0D)
                    nonPrintable++;
            }

            // If more than 5% null bytes or 30% non-printable, consider it binary
            return nullBytes > checkLength * 0.05 || nonPrintable > checkLength * 0.3;
        }

        // ─── Hex Dump Formatter ────────────────────────────────────────────
        private static string FormatHexDump(byte[] data, int bytesPerLine = 16)
        {
            if (data == null || data.Length == 0) return "[Empty]";

            var sb = new StringBuilder();

            // Limit display to first 64KB to avoid flooding the log
            const int maxDisplayBytes = 65536;
            int displayLength = Math.Min(data.Length, maxDisplayBytes);

            // ─── BINARY SECTION (Top) ────────────────────────────────────────────
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("                           BINARY DATA (HEX)");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"Offset    00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F");
            sb.AppendLine("────────  ─────────────────────────  ─────────────────────────");

            for (int i = 0; i < displayLength; i += bytesPerLine)
            {
                // Offset
                sb.Append($"{i:X8}  ");

                // Hex bytes - first 8
                for (int j = 0; j < 8; j++)
                {
                    if (i + j < data.Length)
                        sb.Append($"{data[i + j]:X2} ");
                    else
                        sb.Append("   ");
                }

                sb.Append(" ");

                // Hex bytes - second 8
                for (int j = 8; j < bytesPerLine; j++)
                {
                    if (i + j < data.Length)
                        sb.Append($"{data[i + j]:X2} ");
                    else
                        sb.Append("   ");
                }

                sb.AppendLine();
            }

            if (data.Length > maxDisplayBytes)
            {
                sb.AppendLine($"... ({data.Length - maxDisplayBytes:N0} more bytes not shown) ...");
            }

            sb.AppendLine();

            // ─── STRING SECTION (Bottom) ─────────────────────────────────────────
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("                        EXTRACTED STRINGS (ASCII)");
            sb.AppendLine("═══════════════════════════════════════════════════════════════════════════");
            sb.AppendLine();

            // Extract and display printable strings (minimum 4 characters)
            var strings = ExtractPrintableStrings(data, minLength: 4);

            if (strings.Any())
            {
                int stringIndex = 1;
                foreach (var str in strings)
                {
                    // Clean the string for display
                    string cleanStr = new string(str.Where(c => c >= 32 && c < 127).ToArray());
                    if (!string.IsNullOrWhiteSpace(cleanStr))
                    {
                        sb.AppendLine($"[{stringIndex++,3}] {cleanStr}");
                    }
                }
            }
            else
            {
                sb.AppendLine("[No printable strings found]");
            }

            sb.AppendLine();
            sb.AppendLine("────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine($"Total: {data.Length:N0} bytes | Strings found: {strings.Count}");

            return sb.ToString();
        }

        private static List<string> ExtractPrintableStrings(byte[] data, int minLength = 4)
        {
            var strings = new List<string>();
            var currentString = new StringBuilder();

            foreach (byte b in data)
            {
                if (b >= 32 && b < 127) // Printable ASCII range
                {
                    currentString.Append((char)b);
                }
                else
                {
                    if (currentString.Length >= minLength)
                    {
                        strings.Add(currentString.ToString());
                    }
                    currentString.Clear();
                }
            }

            // Don't forget the last string if it exists
            if (currentString.Length >= minLength)
            {
                strings.Add(currentString.ToString());
            }

            return strings;
        }

        // ─── Helpers ───────────────────────────────────────────────────────
        private static readonly HashSet<string> commaHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Accept", "Accept-Encoding", "Accept-Language", "Cache-Control", "Connection", "Cookie", "Pragma", "Upgrade", "Via", "Warning"
        };

        private static void LogHttpRequestData(BotData data, HttpRequestMessage request, string content = null, string boundary = null)
        {
            try
            {
                using var writer = new StringWriter();
                writer.WriteLine($"{request.Method} {request.RequestUri.PathAndQuery} HTTP/1.1");
                writer.WriteLine($"Host: {request.RequestUri.Host}");

                foreach (var header in request.Headers)
                {
                    var separator = commaHeaders.Contains(header.Key) ? ", " : " ";
                    writer.WriteLine($"{header.Key}: {string.Join(separator, header.Value)}");
                }

                if (request.Content != null)
                {
                    foreach (var header in request.Content.Headers)
                    {
                        var separator = commaHeaders.Contains(header.Key) ? ", " : " ";
                        writer.WriteLine($"{header.Key}: {string.Join(separator, header.Value)}");
                    }
                }

                if (data.COOKIES?.Any() == true)
                {
                    var cookieHeader = string.Join("; ", data.COOKIES.Where(c => !string.IsNullOrEmpty(c.Key)).Select(c => $"{c.Key}={c.Value}"));
                    if (!string.IsNullOrEmpty(cookieHeader))
                        writer.WriteLine($"Cookie: {cookieHeader}");
                }

                if (!string.IsNullOrEmpty(content))
                {
                    writer.WriteLine($"Content-Length: {Encoding.UTF8.GetByteCount(content)}");
                    writer.WriteLine();
                    writer.WriteLine(content);
                }

                data.Logger.Log(writer.ToString(), LogColors.NonPhotoBlue);
            }
            catch (Exception ex)
            {
                data.Logger.Log($"[Flurl] Log error: {ex.Message}", LogColors.DarkOrange);
            }
        }

        private static bool TryParseCookie(string cookieHeader, out string name, out string value)
        {
            name = null; value = null;
            if (string.IsNullOrEmpty(cookieHeader)) return false;
            var endPos = cookieHeader.IndexOf(';');
            var sepPos = cookieHeader.IndexOf('=');
            if (sepPos == -1) return false;
            name = cookieHeader[..sepPos].Trim();
            value = endPos == -1 ? cookieHeader[(sepPos + 1)..].Trim() : cookieHeader.Substring(sepPos + 1, (endPos - sepPos) - 1).Trim();
            return !string.IsNullOrEmpty(name);
        }

        private static Uri BuildProxyUri(RuriLib.Models.Proxies.Proxy proxy)
        {
            if (proxy == null || string.IsNullOrEmpty(proxy.Host)) throw new InvalidOperationException("Proxy host is empty");
            string scheme = proxy.Type switch
            {
                RuriLib.Models.Proxies.ProxyType.Socks4 => "socks4",
                RuriLib.Models.Proxies.ProxyType.Socks4a => "socks4a",
                RuriLib.Models.Proxies.ProxyType.Socks5 => "socks5",
                _ => "http"
            };
            string userInfo = "";
            if (!string.IsNullOrEmpty(proxy.Username))
            {
                string user = Uri.EscapeDataString(proxy.Username);
                string pass = string.IsNullOrEmpty(proxy.Password) ? "" : Uri.EscapeDataString(proxy.Password);
                userInfo = $"{user}:{pass}@";
            }
            return new Uri($"{scheme}://{userInfo}{proxy.Host}:{proxy.Port}");
        }

        private void ValidateBotData(BotData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Logger == null) throw new InvalidOperationException("BotData.Logger is null");
            if (data.COOKIES == null) throw new InvalidOperationException("BotData.COOKIES is null");
            if (data.CancellationToken == null) throw new InvalidOperationException("BotData.CancellationToken is null");
        }

        private void ValidateOptions(HttpRequestOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.Url)) throw new InvalidOperationException("Url is empty");
            if (options.CustomCookies == null) options.CustomCookies = new Dictionary<string, string>();
            if (options.CustomHeaders == null) options.CustomHeaders = new Dictionary<string, string>();
        }

        private static string GenerateMultipartBoundary() => "----FlurlBoundary" + Guid.NewGuid().ToString("N");

        private static string SerializeMultipart(string boundary, IEnumerable<MyHttpContent> contents)
        {
            var sb = new StringBuilder();
            if (contents == null) return sb.ToString();
            foreach (var c in contents)
            {
                if (c == null) continue;
                sb.AppendLine($"--{boundary}");
                sb.AppendLine($"Content-Disposition: form-data; name=\"{c.Name}\"");
                sb.AppendLine($"Content-Type: {c.ContentType ?? "text/plain"}");
                sb.AppendLine();
                sb.AppendLine(c is StringHttpContent s ? s.Data : "[Binary Content]");
            }
            sb.AppendLine($"--{boundary}--");
            return sb.ToString();
        }
    }
}
