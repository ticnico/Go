using RuriLib.Functions.Http;
using RuriLib.Functions.Http.Options;
using RuriLib.Models.Blocks.Parameters;
using RuriLib.Models.Blocks.Settings;
using System.Collections.Generic;
using RuriLib.Http.Curl;

namespace RuriLib.Models.Blocks.Custom
{
    public class HttpRequestBlockDescriptor : BlockDescriptor
    {
        public HttpRequestBlockDescriptor()
        {
            Id = "HttpRequest";
            Name = "Http Request";
            Description = "Performs an Http request and reads the response";
            Category = new BlockCategory
            {
                Name = "Http",
                BackgroundColor = "#32cd32",
                ForegroundColor = "#000",
                Path = "RuriLib.Blocks.Requests.Http",
                Namespace = "RuriLib.Blocks.Requests.Http.Methods",
                Description = "Blocks for performing Http requests"
            };
            Parameters = new Dictionary<string, BlockParameter>
            {
                // --- NEW PROXY MODE SETTING ---
                { "proxyMode", new StringParameter("proxyMode", "Default") },

                { "url", new StringParameter("url", "https://google.com") },
                { "method", new EnumParameter("method", typeof(HttpMethod), HttpMethod.GET.ToString()) },
                { "autoRedirect", new BoolParameter("autoRedirect", true) },
                { "maxNumberOfRedirects", new IntParameter("maxNumberOfRedirects", 8) },
                { "readResponseContent", new BoolParameter("readResponseContent", true) },
                { "urlEncodeContent", new BoolParameter("urlEncodeContent", false) },
                { "absoluteUriInFirstLine", new BoolParameter("absoluteUriInFirstLine", false) },
                { "httpLibrary", new EnumParameter("httpLibrary", typeof(HttpLibrary), HttpLibrary.RuriLibHttp.ToString())
                {
                    Description = $"{nameof(HttpLibrary.RuriLibHttp)} is the custom library, {nameof(HttpLibrary.SystemNet)} is the built-in .NET one with HTTP/2.0 and HTTP/3.0 support, {nameof(HttpLibrary.CurlImpersonate)} uses curl-impersonate for browser fingerprint spoofing"
                } },
                { "curlImpersonateBrowserProfile", new EnumParameter("curlImpersonateBrowserProfile",
                    typeof(CurlImpersonateBrowserProfile), nameof(CurlImpersonateBrowserProfile.Chrome142))
                {
                    Description = "The browser profile to impersonate when using the CurlImpersonate HTTP library."
                } },
                { "curlUseBrowserHeaders", new BoolParameter("curlUseBrowserHeaders", true)
                {
                    Description = "If true, curl-impersonate sends browser-default headers and browser-managed custom headers are ignored to preserve header order."
                } },
                { "securityProtocol", new EnumParameter("securityProtocol", typeof(SecurityProtocol), SecurityProtocol.SystemDefault.ToString()) },
                
                // --- ADDED MISSING PARAMETER ---
                { "ignoreCertificateValidation", new BoolParameter("ignoreCertificateValidation", true) },

                { "useCustomCipherSuites", new BoolParameter("useCustomCipherSuites", false) },
                { "alwaysSendContent", new BoolParameter("alwaysSendContent", false) },
                { "decodeHtml", new BoolParameter("decodeHtml", false) },
                { "codePagesEncoding", new StringParameter("codePagesEncoding", string.Empty) },
                { "customCipherSuites", new ListOfStringsParameter("customCipherSuites",
                    new List<string>
                    {
                        "TLS_AES_128_GCM_SHA256",
                        "TLS_CHACHA20_POLY1305_SHA256",
                        "TLS_AES_256_GCM_SHA384",
                        "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256",
                        "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256",
                        "TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256",
                        "TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256",
                        "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384",
                        "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
                        "TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA",
                        "TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA",
                        "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA",
                        "TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA",
                        "TLS_RSA_WITH_AES_128_GCM_SHA256",
                        "TLS_RSA_WITH_AES_256_GCM_SHA384",
                        "TLS_RSA_WITH_AES_128_CBC_SHA",
                        "TLS_RSA_WITH_AES_256_CBC_SHA",
                        "TLS_RSA_WITH_3DES_EDE_CBC_SHA"
                    },
                    SettingInputMode.Fixed) },
                { "customCookies", new DictionaryOfStringsParameter("customCookies", null, SettingInputMode.Interpolated) },
                { "customHeaders", new DictionaryOfStringsParameter("customHeaders",
                    new Dictionary<string, string>
                    {
                        { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/83.0.4103.116 Safari/537.36" },
                        { "Pragma", "no-cache" },
                        { "Accept", "*/*" },
                        { "Accept-Language", "en-US,en;q=0.8" }
                    },
                    SettingInputMode.Interpolated) },
                { "timeoutMilliseconds", new IntParameter("timeoutMilliseconds", 15000) },
                { "httpVersion", new StringParameter("httpVersion", "1.1") }
            };
        }
    }
}
