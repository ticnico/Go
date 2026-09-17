using Jint;
using RuriLib.Extensions;
using RuriLib.Functions.Conversion;
using RuriLib.Functions.Files;
using RuriLib.Functions.Http.Options;
using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http;

internal class ReqableStyleHttpHandler : HttpRequestHandler
{
    private readonly Func<RuriLib.Models.Proxies.Proxy?, HttpOptions, HttpClient> clientFactory;

    public ReqableStyleHttpHandler()
    {
        this.clientFactory = (proxy, options) =>
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                EnableMultipleHttp2Connections = true,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = options.ConnectTimeout,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            };

            handler.SslOptions.EnabledSslProtocols = (SslProtocols)options.SecurityProtocol;
            handler.SslOptions.CertificateRevocationCheckMode = options.CertRevocationMode;

            if (options.IgnoreCertificateValidation)
            {
                handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true;
            }

            if (proxy != null)
            {
                handler.Proxy = new WebProxy(proxy.Host, proxy.Port)
                {
                    Credentials = string.IsNullOrEmpty(proxy.Username)
                        ? null
                        : new NetworkCredential(proxy.Username, proxy.Password)
                };
            }

            return new HttpClient(handler);
        };
    }

    #region HTTP Request Methods

    public override async Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
    {
        var jar = PrepareCookieJar(data, options.CustomCookies);
        MergeCustomCookieHeaders(jar, options.CustomHeaders);

        var clientOptions = GetClientOptions(data, options);
        using var client = clientFactory(data.UseProxy ? data.Proxy : null, clientOptions);

        using var request = new HttpRequestMessage
        {
            Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
            RequestUri = new Uri(options.Url)
        };

        SetHttpVersion(request, options.HttpVersion);
        AddCustomHeaders(request, options.CustomHeaders);
        request.Headers.TryAddWithoutValidation("Cookie", jar.GetCookieHeader());

        string? content = null;
        if (!string.IsNullOrEmpty(options.Content) || options.AlwaysSendContent)
        {
            content = options.Content;
            if (options.UrlEncodeContent)
            {
                content = string.Join("", content.SplitInChunks(2080)
                    .Select(Uri.EscapeDataString))
                    .Replace($"%26", "&").Replace($"%3D", "=");
            }

            request.Content = new StringContent(content.Unescape());
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);
        }

        data.Logger.LogHeader();
        LogHttpRequestData(data, request, jar, content);
        LogCurlImpersonateRequestLogNotice(data, options);

        using var response = await SendRequestWithAutoRedirectAsync(client, request, jar, data, options).ConfigureAwait(false);
        await AutoCaptureCookiesAndProcessJs(data, response, jar, options).ConfigureAwait(false);
    }

    public override async Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
    {
        var jar = PrepareCookieJar(data, options.CustomCookies);
        MergeCustomCookieHeaders(jar, options.CustomHeaders);

        var clientOptions = GetClientOptions(data, options);
        using var client = clientFactory(data.UseProxy ? data.Proxy : null, clientOptions);

        using var request = new HttpRequestMessage
        {
            Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
            RequestUri = new Uri(options.Url),
            Content = new ByteArrayContent(options.Content)
        };

        SetHttpVersion(request, options.HttpVersion);
        AddCustomHeaders(request, options.CustomHeaders);
        request.Headers.TryAddWithoutValidation("Cookie", jar.GetCookieHeader());
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);

        data.Logger.LogHeader();
        LogHttpRequestData(data, request, jar, Base64Converter.ToBase64String(options.Content));
        LogCurlImpersonateRequestLogNotice(data, options);

        using var response = await SendRequestWithAutoRedirectAsync(client, request, jar, data, options).ConfigureAwait(false);
        await AutoCaptureCookiesAndProcessJs(data, response, jar, options).ConfigureAwait(false);
    }

    public override async Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
    {
        var jar = PrepareCookieJar(data, options.CustomCookies);
        MergeCustomCookieHeaders(jar, options.CustomHeaders);

        var clientOptions = GetClientOptions(data, options);
        using var client = clientFactory(data.UseProxy ? data.Proxy : null, clientOptions);

        using var request = new HttpRequestMessage
        {
            Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
            RequestUri = new Uri(options.Url)
        };

        SetHttpVersion(request, options.HttpVersion);
        AddCustomHeaders(request, options.CustomHeaders);
        request.Headers.TryAddWithoutValidation("Cookie", jar.GetCookieHeader());

        request.Headers.TryAddWithoutValidation("Authorization", "Basic " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")));

        data.Logger.LogHeader();
        LogHttpRequestData(data, request, jar);
        LogCurlImpersonateRequestLogNotice(data, options);

        using var response = await SendRequestWithAutoRedirectAsync(client, request, jar, data, options).ConfigureAwait(false);
        await AutoCaptureCookiesAndProcessJs(data, response, jar, options).ConfigureAwait(false);
    }

    public override async Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
    {
        var jar = PrepareCookieJar(data, options.CustomCookies);
        MergeCustomCookieHeaders(jar, options.CustomHeaders);

        var clientOptions = GetClientOptions(data, options);
        using var client = clientFactory(data.UseProxy ? data.Proxy : null, clientOptions);

        if (string.IsNullOrWhiteSpace(options.Boundary))
        {
            options.Boundary = GenerateMultipartBoundary();
        }

        var multipartContent = new MultipartFormDataContent(options.Boundary);
        var boundaryParameter = multipartContent.Headers.ContentType?.Parameters
            .FirstOrDefault(o => o.Name == "boundary");

        if (boundaryParameter is not null)
        {
            boundaryParameter.Value = options.Boundary;
        }

        FileStream? fileStream = null;

        foreach (var c in options.Contents)
        {
            switch (c)
            {
                case StringHttpContent x:
                    multipartContent.Add(CreateMultipartContent(x), x.Name);
                    break;
                case RawHttpContent x:
                    multipartContent.Add(CreateMultipartContent(x), x.Name);
                    break;
                case FileHttpContent x:
                    lock (FileLocker.GetHandle(x.FileName))
                    {
                        if (data.Providers.Security.RestrictBlocksToCWD)
                        {
                            FileUtils.ThrowIfNotInCWD(x.FileName);
                        }
                        fileStream = new FileStream(x.FileName, FileMode.Open);
                        var fileContent = CreateMultipartContent(x, fileStream);
                        multipartContent.Add(fileContent, x.Name);
                    }
                    break;
            }
        }

        using var request = new HttpRequestMessage
        {
            Method = new System.Net.Http.HttpMethod(options.Method.ToString()),
            RequestUri = new Uri(options.Url),
            Content = multipartContent
        };

        SetHttpVersion(request, options.HttpVersion);
        AddCustomHeaders(request, options.CustomHeaders);
        request.Headers.TryAddWithoutValidation("Cookie", jar.GetCookieHeader());

        data.Logger.LogHeader();
        LogHttpRequestData(data, request, jar, SerializeMultipart(options.Boundary, options.Contents), options.Boundary);
        LogCurlImpersonateRequestLogNotice(data, options);

        try
        {
            using var response = await SendRequestWithAutoRedirectAsync(client, request, jar, data, options).ConfigureAwait(false);
            await AutoCaptureCookiesAndProcessJs(data, response, jar, options).ConfigureAwait(false);
        }
        finally
        {
            if (fileStream is not null)
            {
                await fileStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    #endregion

    #region Reqable-Style Core Helpers

    private SimpleCookieJar PrepareCookieJar(BotData data, Dictionary<string, string> customCookies)
    {
        var jar = new SimpleCookieJar();
        foreach (var kvp in data.COOKIES) jar.Add(kvp.Key, kvp.Value);
        foreach (var kvp in customCookies) jar.Add(kvp.Key, kvp.Value);
        return jar;
    }

    private void MergeCustomCookieHeaders(SimpleCookieJar jar, Dictionary<string, string> customHeaders)
    {
        foreach (var header in customHeaders)
        {
            if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var part in header.Value.Split(';'))
                {
                    jar.AddFromSetCookieHeader(part.Trim());
                }
            }
        }
    }

    private void AddCustomHeaders(HttpRequestMessage request, Dictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private void SetHttpVersion(HttpRequestMessage request, string versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString) || versionString.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            request.Version = HttpVersion.Version30;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            return;
        }

        request.Version = versionString switch
        {
            "1.0" => HttpVersion.Version10,
            "1.1" => HttpVersion.Version11,
            "2.0" => HttpVersion.Version20,
            "3.0" => HttpVersion.Version30,
            _ => HttpVersion.Version11
        };
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpClient client, HttpRequestMessage request, BotData data, Options.HttpRequestOptions options)
    {
        Activity.Current = null;
        using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);

        return await client.SendAsync(request, options.ReadResponseContent
            ? HttpCompletionOption.ResponseContentRead
            : HttpCompletionOption.ResponseHeadersRead,
            linkedCts.Token).ConfigureAwait(false);
    }

    // UPDATED: Manual redirect handling that properly forwards cookies
    private async Task<HttpResponseMessage> SendRequestWithAutoRedirectAsync(
        HttpClient client,
        HttpRequestMessage request,
        SimpleCookieJar jar,
        BotData data,
        Options.HttpRequestOptions options)
    {
        var currentRequest = request;
        var redirectCount = 0;
        HttpResponseMessage response;

        while (true)
        {
            response = await SendRequestAsync(client, currentRequest, data, options).ConfigureAwait(false);

            if (!options.AutoRedirect || redirectCount >= options.MaxNumberOfRedirects)
            {
                break;
            }

            var statusCode = (int)response.StatusCode;

            if (statusCode != 301 && statusCode != 302 && statusCode != 303 && statusCode != 307 && statusCode != 308)
            {
                break;
            }

            if (!response.Headers.TryGetValues("Location", out var locationValues))
            {
                break;
            }

            var location = locationValues.FirstOrDefault();
            if (string.IsNullOrEmpty(location))
            {
                break;
            }

            // Capture cookies from the redirect response BEFORE following
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var cookieHeader in setCookies)
                {
                    jar.AddFromSetCookieHeader(cookieHeader);
                }
            }

            var redirectUri = new Uri(currentRequest.RequestUri!, location);

            // FIXED: Fully qualified System.Net.Http.HttpMethod.Get to avoid collision with RuriLib's HttpMethod enum
            var method = (statusCode == 307 || statusCode == 308)
                ? currentRequest.Method
                : System.Net.Http.HttpMethod.Get;

            var newRequest = new HttpRequestMessage
            {
                Method = method,
                RequestUri = redirectUri,
                Version = currentRequest.Version,
                VersionPolicy = currentRequest.VersionPolicy
            };

            foreach (var header in currentRequest.Headers)
            {
                if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            newRequest.Headers.TryAddWithoutValidation("Cookie", jar.GetCookieHeader());

            if ((statusCode == 307 || statusCode == 308) && currentRequest.Content != null)
            {
                newRequest.Content = currentRequest.Content;
            }

            currentRequest = newRequest;
            redirectCount++;

            data.Logger.Log($"Following redirect ({statusCode}) to: {redirectUri}", LogColors.DodgerBlue);

            response.Dispose();
        }

        return response;
    }

    private async Task AutoCaptureCookiesAndProcessJs(BotData data, HttpResponseMessage response, SimpleCookieJar jar, Options.HttpRequestOptions options)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var cookieHeader in setCookies)
            {
                jar.AddFromSetCookieHeader(cookieHeader);
            }
        }

        if (response.Content != null && options.ReadResponseContent)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(data.CancellationToken).ConfigureAwait(false);
            data.RAWSOURCE = bytes;
        }
        else
        {
            data.RAWSOURCE = [];
        }

        await ProcessResponseData(data, response, options);

        if (!string.IsNullOrEmpty(data.SOURCE))
        {
            ExecuteJsChallenges(data, data.SOURCE, jar);
        }

        jar.SyncToBotData(data);

        data.Logger.Log("Auto Cookie Jar Updated:", LogColors.MikadoYellow);
        data.Logger.Log(data.COOKIES.Select(h => $"{h.Key}: {h.Value}"), LogColors.Khaki);
    }

    private void ExecuteJsChallenges(BotData data, string html, SimpleCookieJar jar)
    {
        if (string.IsNullOrEmpty(html)) return;

        var scriptMatches = Regex.Matches(html, @"<script[^>]*>(.*?)</script>", RegexOptions.Singleline);

        foreach (Match match in scriptMatches)
        {
            var scriptContent = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(scriptContent)) continue;

            try
            {
                var engine = new Engine(opts => opts.TimeoutInterval(TimeSpan.FromSeconds(2)));

                var documentMock = new Dictionary<string, object> { ["cookie"] = "" };
                engine.SetValue("window", new { location = new { href = data.ADDRESS }, document = documentMock });
                engine.SetValue("document", documentMock);
                engine.SetValue("navigator", new { userAgent = data.HEADERS.GetValueOrDefault("User-Agent", "Mozilla/5.0") });

                engine.SetValue("setTimeout", new Action<Action, int>((action, delay) => { }));
                engine.SetValue("setInterval", new Action<Action, int>((action, delay) => { }));
                engine.SetValue("clearTimeout", new Action<int>((id) => { }));
                engine.SetValue("clearInterval", new Action<int>((id) => { }));

                engine.Evaluate(scriptContent);

                var jsCookies = documentMock["cookie"]?.ToString();
                if (!string.IsNullOrEmpty(jsCookies))
                {
                    foreach (var c in jsCookies.Split(';'))
                    {
                        jar.AddFromSetCookieHeader(c);
                    }
                    data.Logger.Log("[Jint] Successfully executed JS and captured cookies!", LogColors.GreenYellow);
                }
            }
            catch
            {
                // Ignore JS errors
            }
        }
    }

    #endregion

    #region Logging

    private static void LogHttpRequestData(BotData data, HttpRequestMessage request, SimpleCookieJar jar,
        string? content = null, string? boundary = null)
    {
        using var writer = new StringWriter();

        var requestUri = request.RequestUri ?? throw new InvalidOperationException("Request URI cannot be null.");
        writer.WriteLine($"{request.Method.Method} {requestUri.PathAndQuery} HTTP/{request.Version.Major}.{request.Version.Minor}");

        writer.WriteLine($"Host: {requestUri.Host}");

        foreach (var header in request.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) continue;

            var separator = commaHeaders.Contains(header.Key) ? ", " : " ";
            writer.WriteLine($"{header.Key}: {string.Join(separator, header.Value)}");
        }

        var cookieHeader = jar.GetCookieHeader();
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            writer.WriteLine($"Cookie: {cookieHeader}");
        }

        if (request.Content != null && content != null)
        {
            switch (request.Content)
            {
                case StringContent x:
                    writer.WriteLine($"Content-Type: {x.Headers.ContentType}");
                    writer.WriteLine($"Content-Length: {x.Headers.ContentLength}");
                    writer.WriteLine();
                    writer.WriteLine(content);
                    break;
                case ByteArrayContent x:
                    writer.WriteLine($"Content-Type: {x.Headers.ContentType}");
                    writer.WriteLine($"Content-Length: {x.Headers.ContentLength}");
                    writer.WriteLine();
                    writer.WriteLine(content);
                    break;
                case MultipartFormDataContent:
                    writer.WriteLine($"Content-Type: multipart/form-data; boundary=\"{boundary}\"");
                    writer.WriteLine("Content-Length: (not calculated)");
                    writer.WriteLine();
                    writer.WriteLine(content);
                    break;
            }
        }

        data.Logger.Log(writer.ToString(), LogColors.NonPhotoBlue);
    }

    private static void LogCurlImpersonateRequestLogNotice(BotData data, Options.HttpRequestOptions options)
    {
        if (options.HttpLibrary != HttpLibrary.CurlImpersonate) return;
    }

    private static async Task ProcessResponseData(BotData data, HttpResponseMessage response, Options.HttpRequestOptions requestOptions)
    {
        var responseUri = response.RequestMessage?.RequestUri
            ?? throw new InvalidOperationException("Response request URI cannot be null.");
        data.ADDRESS = responseUri.AbsoluteUri;
        data.Logger.Log($"Address: {data.ADDRESS}", LogColors.DodgerBlue);

        data.RESPONSECODE = (int)response.StatusCode;
        data.Logger.Log($"Response code: {data.RESPONSECODE}", LogColors.Citrine);
        data.Logger.Log($"Response HTTP version: HTTP/{response.Version.Major}.{response.Version.Minor}", LogColors.Citrine);

        data.HEADERS = response.Headers.ToDictionary(h => h.Key, GetHeaderValue);

        if (response.Content != null)
        {
            foreach (var header in response.Content.Headers)
            {
                data.HEADERS[header.Key] = GetHeaderValue(header);
            }
        }

        if (!data.HEADERS.ContainsKey("Content-Length"))
        {
            data.HEADERS["Content-Length"] = data.RAWSOURCE.Length.ToString();
        }

        data.Logger.Log("Received Headers:", LogColors.MediumPurple);
        data.Logger.Log(data.HEADERS.Select(h => $"{h.Key}: {h.Value}"), LogColors.Violet);

        if (data.HEADERS.TryGetValue("Content-Encoding", out var ceValue) && ceValue.Contains("br"))
        {
            try
            {
                using var inputStream = new MemoryStream(data.RAWSOURCE);
                using var outputStream = new MemoryStream();
                await using var brotli = new BrotliStream(inputStream, CompressionMode.Decompress, false);
                await brotli.CopyToAsync(outputStream);
                data.RAWSOURCE = outputStream.ToArray();
            }
            catch
            {
                data.Logger.Log("[WARNING] Tried to decompress brotli but failed", LogColors.DarkOrange);
            }
        }

        if (data.RAWSOURCE.Length > 1 && data.RAWSOURCE[0] == 0x1F && data.RAWSOURCE[1] == 0x8B)
        {
            try
            {
                data.RAWSOURCE = GZip.Unzip(data.RAWSOURCE);
            }
            catch
            {
                data.Logger.Log("[WARNING] Tried to decompress gzip but failed", LogColors.DarkOrange);
            }
        }

        if (!string.IsNullOrWhiteSpace(requestOptions.CodePagesEncoding))
        {
            var encoding = CodePagesEncodingProvider.Instance
                .GetEncoding(requestOptions.CodePagesEncoding) ?? throw new NotSupportedException(
                    $"Encoding {requestOptions.CodePagesEncoding} is not supported");
            data.SOURCE = encoding.GetString(data.RAWSOURCE);
        }
        else
        {
            data.SOURCE = Encoding.UTF8.GetString(data.RAWSOURCE);
        }

        if (requestOptions.DecodeHtml)
        {
            data.SOURCE = WebUtility.HtmlDecode(data.SOURCE);
        }

        data.Logger.Log("Received Payload:", LogColors.ForestGreen);
        data.Logger.Log(data.SOURCE, LogColors.GreenYellow, true);
    }

    private static string GetHeaderValue(KeyValuePair<string, IEnumerable<string>> header)
    {
        var separator = commaHeaders.Contains(header.Key) ? ", " : " ";
        return string.Join(separator, header.Value);
    }

    #endregion

    #region Reqable-Style Cookie Jar Implementation

    public class SimpleCookieJar
    {
        private readonly Dictionary<string, string> _cookies = new(StringComparer.OrdinalIgnoreCase);

        public void AddFromSetCookieHeader(string setCookieHeader)
        {
            if (string.IsNullOrWhiteSpace(setCookieHeader)) return;
            var mainPart = setCookieHeader.Split(';')[0].Trim();
            var eqIndex = mainPart.IndexOf('=');
            if (eqIndex > 0)
            {
                var name = mainPart.Substring(0, eqIndex).Trim();
                var value = mainPart.Substring(eqIndex + 1).Trim();
                _cookies[name] = value;
            }
        }

        public void Add(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _cookies[name.Trim()] = value?.Trim() ?? "";
        }

        public string GetCookieHeader() => string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));

        public void SyncToBotData(BotData data)
        {
            data.COOKIES.Clear();
            foreach (var kvp in _cookies) data.COOKIES[kvp.Key] = kvp.Value;
        }
    }

    #endregion
}
