using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoldenBullet.Views.Pages
{
    public partial class SiteScannerPage : Page
    {
        private HttpClient _httpClient;

        public SiteScannerPage()
        {
            InitializeComponent();
            InitializeHttpClient();
            InitializeSecurityHeaders();
        }

        private void InitializeHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            handler.ServerCertificateCustomValidationCallback = 
                (message, cert, chain, errors) => { return true; };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", 
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        private void InitializeSecurityHeaders()
        {
            var headers = new[]
            {
                "Content-Security-Policy",
                "X-Frame-Options",
                "X-Content-Type-Options",
                "Referrer-Policy",
                "Permissions-Policy",
                "X-XSS-Protection",
                "Strict-Transport-Security"
            };

            foreach (var headerName in headers)
            {
                AddSecurityHeaderRow(headerName);
            }
        }

        private void AddSecurityHeaderRow(string headerName)
        {
            var grid = new Grid();
            grid.Margin = new Thickness(0, 8, 0, 8);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerText = new TextBlock
            {
                Text = headerName,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE0E0E0")),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            };
            Grid.SetColumn(headerText, 0);

            var statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var statusIcon = new TextBlock
            {
                Text = "✗",
                Foreground = Brushes.Red,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 16,
                FontWeight = FontWeights.Bold
            };

            var headerValue = new TextBlock
            {
                Text = "Not present",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFAAAAAA")),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };

            statusPanel.Children.Add(statusIcon);
            statusPanel.Children.Add(headerValue);
            Grid.SetColumn(statusPanel, 1);

            grid.Children.Add(headerText);
            grid.Children.Add(statusPanel);

            grid.Tag = headerName;

            SecurityHeadersPanel.Children.Add(grid);
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            string targetUrl = txtTargetURL.Text.Trim();

            if (string.IsNullOrEmpty(targetUrl))
            {
                MessageBox.Show("Please enter a target URL", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!targetUrl.StartsWith("http://") && !targetUrl.StartsWith("https://"))
            {
                targetUrl = "https://" + targetUrl;
            }

            if (!Uri.IsWellFormedUriString(targetUrl, UriKind.Absolute))
            {
                MessageBox.Show($"Invalid URL format: {targetUrl}\n\nPlease enter a valid URL (e.g., https://example.com)", 
                    "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ScanBtn.IsEnabled = false;
                ScanBtn.Content = "⏳ Scanning...";
                txtRawHeaders.Text = "Scanning... Please wait...";

                Debug.WriteLine($" Starting scan for: {targetUrl}");

                var stopwatch = Stopwatch.StartNew();
                
                using (var request = new HttpRequestMessage(HttpMethod.Get, targetUrl))
                {
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                    
                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    var htmlContent = await response.Content.ReadAsStringAsync();
                    
                    Debug.WriteLine($"📄 HTML Content length: {htmlContent.Length} bytes");
                    
                    // Extract and analyze JavaScript files
                    var jsContents = await ExtractAndFetchJavaScriptFiles(targetUrl, htmlContent);
                    
                    // Combine HTML and JS content for analysis
                    var fullContent = htmlContent + "\n\n" + string.Join("\n\n", jsContents);
                    
                    Debug.WriteLine($"📦 Total content (HTML + JS): {fullContent.Length} bytes");
                    Debug.WriteLine($"📦 JavaScript files analyzed: {jsContents.Count}");
                    
                    stopwatch.Stop();

                    Debug.WriteLine($"✅ Response received: {(int)response.StatusCode} in {stopwatch.ElapsedMilliseconds}ms");

                    StatusBar.Visibility = Visibility.Visible;
                    txtStatus.Text = ((int)response.StatusCode).ToString();
                    txtStatus.Foreground = response.IsSuccessStatusCode
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F5738"))
                        : Brushes.Red;

                    txtTime.Text = $"{stopwatch.ElapsedMilliseconds} ms";
                    txtProtocol.Text = response.RequestMessage.RequestUri.Scheme.ToUpper();
                    txtFinalURL.Text = response.RequestMessage.RequestUri.ToString();

                    AnalyzeSecurityHeaders(response);
                    DetectBotProtection(fullContent, response.Headers);
                    UpdateRawHeaders(response);
                    CalculateSecurityGrade();
                }
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"❌ Timeout error: {ex.Message}");
                MessageBox.Show($"Request timed out after 30 seconds.\n\nThe site may be:\n- Too slow to respond\n- Blocking automated requests\n- Unreachable\n\nTry again or check your internet connection.", 
                    "Timeout Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtRawHeaders.Text = "Error: Request timed out";
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"❌ HTTP Request error: {ex.Message}");
                Debug.WriteLine($"❌ Inner exception: {ex.InnerException?.Message}");
                
                string errorMsg = $"Error scanning URL: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMsg += $"\n\nDetails: {ex.InnerException.Message}";
                }
                
                errorMsg += "\n\nPossible causes:\n- No internet connection\n- Site is blocking requests\n- Invalid URL\n- Firewall/Proxy blocking";
                
                MessageBox.Show(errorMsg, "Network Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtRawHeaders.Text = $"Error: {ex.Message}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Unexpected error: {ex}");
                MessageBox.Show($"Unexpected error: {ex.Message}\n\nSee debug output for details.", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                txtRawHeaders.Text = $"Error: {ex.Message}";
            }
            finally
            {
                ScanBtn.IsEnabled = true;
                ScanBtn.Content = "🔍 Scan";
            }
        }

        private async Task<List<string>> ExtractAndFetchJavaScriptFiles(string baseUrl, string htmlContent)
        {
            var jsContents = new List<string>();
            var jsUrls = new List<string>();

            try
            {
                // Find all script tags with src attribute
                var scriptSrcPattern = Regex.Matches(
                    htmlContent,
                    @"<script[^>]+src=[""']([^""']+)[""'][^>]*>",
                    RegexOptions.IgnoreCase);

                foreach (Match match in scriptSrcPattern)
                {
                    if (match.Groups.Count > 1)
                    {
                        string src = match.Groups[1].Value;

                        // Convert relative URL to absolute
                        if (src.StartsWith("//"))
                            src = "https:" + src;
                        else if (src.StartsWith("/"))
                        {
                            try
                            {
                                var baseUri = new Uri(baseUrl);
                                src = baseUri.Scheme + "://" + baseUri.Host + src;
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        if (src.StartsWith("http"))
                            jsUrls.Add(src);
                    }
                }

                // Also extract inline script content (scripts without src)
                var inlineScriptPattern = Regex.Matches(
                    htmlContent,
                    @"<script[^>]*>([\s\S]*?)</script>",
                    RegexOptions.IgnoreCase);

                foreach (Match match in inlineScriptPattern)
                {
                    if (match.Groups.Count > 1)
                    {
                        string inlineContent = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(inlineContent) && !inlineContent.Contains("src="))
                        {
                            jsContents.Add(inlineContent);
                            Debug.WriteLine($"📝 Found inline script ({inlineContent.Length} chars)");
                        }
                    }
                }

                Debug.WriteLine($" Found {jsUrls.Count} external JavaScript files to analyze");

                // Fetch JavaScript files (increased to 20 to catch more)
                int count = 0;
                foreach (var jsUrl in jsUrls.Take(20))
                {
                    try
                    {
                        using (var jsRequest = new HttpRequestMessage(HttpMethod.Get, jsUrl))
                        {
                            jsRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                            jsRequest.Headers.Add("Accept", "*/*");
                            jsRequest.Headers.Add("Referer", baseUrl);

                            var jsResponse = await _httpClient.SendAsync(jsRequest);
                            if (jsResponse.IsSuccessStatusCode)
                            {
                                var jsContent = await jsResponse.Content.ReadAsStringAsync();
                                jsContents.Add(jsContent);
                                count++;
                                Debug.WriteLine($"✅ Fetched JS: {jsUrl} ({jsContent.Length} bytes)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ Failed to fetch JS {jsUrl}: {ex.Message}");
                    }
                }

                Debug.WriteLine($"📚 Successfully fetched {count} JavaScript files");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error extracting JS files: {ex.Message}");
            }

            return jsContents;
        }

        private void AnalyzeSecurityHeaders(HttpResponseMessage response)
        {
            foreach (var item in SecurityHeadersPanel.Children)
            {
                if (item is Grid grid && grid.Tag is string headerName)
                {
                    string value = null;

                    if (response.Headers.TryGetValues(headerName, out var values))
                    {
                        value = string.Join("; ", values);
                    }
                    else if (response.Content.Headers.TryGetValues(headerName, out var contentValues))
                    {
                        value = string.Join("; ", contentValues);
                    }

                    var statusPanel = grid.Children[1] as StackPanel;
                    var statusIcon = statusPanel.Children[0] as TextBlock;
                    var valueText = statusPanel.Children[1] as TextBlock;

                    if (!string.IsNullOrEmpty(value))
                    {
                        statusIcon.Text = "✓";
                        statusIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F5738"));
                        valueText.Text = value.Length > 80 ? value.Substring(0, 80) + "..." : value;
                        valueText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F5738"));
                    }
                    else
                    {
                        statusIcon.Text = "✗";
                        statusIcon.Foreground = Brushes.Red;
                        valueText.Text = "Not present";
                        valueText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFAAAAAA"));
                    }
                }
            }
        }

        private void DetectBotProtection(string fullContent, System.Net.Http.Headers.HttpResponseHeaders headers)
        {
            ResetAllCheckboxes();

            string lowerContent = fullContent.ToLower();

            List<string> detectedTools = new List<string>();

            Debug.WriteLine($"\n🔍 ========== STARTING SECURITY DETECTION ==========");
            Debug.WriteLine($"📊 Analyzing {lowerContent.Length} total characters (HTML + JS + Inline)");

            // ========== ENHANCED reCAPTCHA DETECTION ==========
            Debug.WriteLine($"\n🔍 Checking for reCAPTCHA...");

            bool hasReCaptcha = false;
            var recaptchaPatterns = new[]
            {
        "recaptcha", "google.com/recaptcha", "www.google.com/recaptcha",
        "g-recaptcha", "recaptcha/api.js", "recaptcha-enterprise",
        "recaptchav3", "data-recaptcha", "data-sitekey",
        " grecaptcha.", "grecaptcha.execute", "grecaptcha.render",
        "recaptcha-badge", "recaptcha-frame", "recaptcha/api2/anchor",
        "recaptcha/api2/bframe", "recaptcha/releases",
        "recaptcha.net", "google-recaptcha",
        "recaptcha__en", "recaptcha__it",  // Language-specific
        "captcha/recaptcha", "recaptcha-captcha",
        "ng-recaptcha", "ngx-recaptcha",  // Angular reCAPTCHA
        "angular-recaptcha", "recaptcha-component",
        "recaptcha-loader", "load-recaptcha",
        "init-recaptcha", "setup-recaptcha"
    };

            foreach (var pattern in recaptchaPatterns)
            {
                if (lowerContent.Contains(pattern))
                {
                    Debug.WriteLine($"✅ Found reCAPTCHA pattern: '{pattern}'");
                    hasReCaptcha = true;
                    break;
                }
            }

            // Also check for reCAPTCHA in specific Angular patterns
            if (!hasReCaptcha)
            {
                // Check for Angular reCAPTCHA component usage
                var angularRecaptchaPattern = Regex.Match(lowerContent, @"recaptcha[^""]*component", RegexOptions.IgnoreCase);
                if (angularRecaptchaPattern.Success)
                {
                    hasReCaptcha = true;
                    Debug.WriteLine($"✅ Found Angular reCAPTCHA component");
                }
            }

            if (hasReCaptcha)
            {
                chkReCAPTCHAv2.IsChecked = true;
                detectedTools.Add("reCAPTCHA");
                Debug.WriteLine("✅ reCAPTCHA v2/v3 detected");

                if (lowerContent.Contains("recaptchav3") ||
                    lowerContent.Contains("/v3") ||
                    lowerContent.Contains("recaptcha-enterprise") ||
                    lowerContent.Contains("v3_beta") ||
                    lowerContent.Contains("grecaptcha.execute"))
                {
                    chkReCAPTCHAv3.IsChecked = true;
                    Debug.WriteLine("✅ reCAPTCHA v3 confirmed");
                }

                if (lowerContent.Contains("enterprise"))
                {
                    chkReCAPTCHAEnt.IsChecked = true;
                    Debug.WriteLine("✅ reCAPTCHA Enterprise confirmed");
                }
            }
            else
            {
                Debug.WriteLine("⚠️ reCAPTCHA NOT found in content");
                Debug.WriteLine($" Content sample (first 2000 chars):\n{lowerContent.Substring(0, Math.Min(2000, lowerContent.Length))}");
            }

            // ========== hCaptcha Detection ==========
            if (lowerContent.Contains("hcaptcha") ||
                lowerContent.Contains("hcaptcha.com") ||
                lowerContent.Contains("js.hcaptcha.com") ||
                lowerContent.Contains("h-captcha"))
            {
                chkHCaptcha.IsChecked = true;
                detectedTools.Add("hCaptcha");
                Debug.WriteLine("✅ hCaptcha detected");
            }

            // ========== Cloudflare Turnstile Detection ==========
            if (lowerContent.Contains("turnstile") ||
                lowerContent.Contains("challenges.cloudflare.com") ||
                lowerContent.Contains("cf-turnstile") ||
                lowerContent.Contains("turnstile.js"))
            {
                chkCloudflare.IsChecked = true;
                detectedTools.Add("Cloudflare Turnstile");
                Debug.WriteLine("✅ Cloudflare Turnstile detected");
            }

            // ========== FunCaptcha / Arkose Detection ==========
            if (lowerContent.Contains("funcaptcha") ||
                lowerContent.Contains("arkoselabs.com") ||
                lowerContent.Contains("arkose"))
            {
                chkFunCaptcha.IsChecked = true;
                detectedTools.Add("FunCaptcha");
                Debug.WriteLine("✅ FunCaptcha detected");
            }

            // ========== GeeTest Detection ==========
            if (lowerContent.Contains("geetest") ||
                lowerContent.Contains("gt.js") ||
                lowerContent.Contains("gcaptcha"))
            {
                chkGeeTest.IsChecked = true;
                detectedTools.Add("GeeTest");
                Debug.WriteLine("✅ GeeTest detected");
            }

            // ========== Text/Image CAPTCHA ==========
            if ((lowerContent.Contains("captcha") && lowerContent.Contains("image")) ||
                lowerContent.Contains("enter the code") ||
                (lowerContent.Contains("security code") && lowerContent.Contains("image")))
            {
                chkTextImage.IsChecked = true;
                detectedTools.Add("Text/Image CAPTCHA");
            }

            // ========== Jscrambler Detection ==========
            if (lowerContent.Contains("jscrambler") ||
                lowerContent.Contains("jscrambler.com") ||
                lowerContent.Contains("jstraps") ||
                lowerContent.Contains("__jscrambler__") ||
                lowerContent.Contains("jssha"))
            {
                chkJscrambler.IsChecked = true;
                detectedTools.Add("Jscrambler");
                Debug.WriteLine("✅ Jscrambler detected");
            }

            // ========== Arxan Detection ==========
            if (lowerContent.Contains("arxan") ||
                lowerContent.Contains("arxantechnologies") ||
                lowerContent.Contains("arxanjs"))
            {
                chkArxan.IsChecked = true;
                detectedTools.Add("Arxan");
            }

            // ========== Approov Detection ==========
            if (lowerContent.Contains("approov") ||
                headers.Contains("x-approov"))
            {
                chkApproov.IsChecked = true;
                detectedTools.Add("Approov");
            }

            // ========== GuardSquare Detection ==========
            if (lowerContent.Contains("guardsquare") ||
                lowerContent.Contains("dexguard") ||
                lowerContent.Contains("proguard"))
            {
                chkGuardSquare.IsChecked = true;
                detectedTools.Add("GuardSquare");
            }

            // ========== DataDome Detection ==========
            if (lowerContent.Contains("datadome") ||
                lowerContent.Contains("datadome-loader") ||
                headers.Contains("x-datadome"))
            {
                chkDataDome.IsChecked = true;
                detectedTools.Add("DataDome");
                Debug.WriteLine("✅ DataDome detected");
            }

            // ========== PerimeterX Detection ==========
            if (lowerContent.Contains("perimeterx") ||
                (lowerContent.Contains("px") && lowerContent.Contains("captcha")) ||
                lowerContent.Contains("__px") ||
                headers.Contains("x-px") ||
                headers.Contains("x-perimeterx"))
            {
                chkPerimeterX.IsChecked = true;
                detectedTools.Add("PerimeterX");
            }

            // ========== Shape Security Detection ==========
            if ((lowerContent.Contains("shape") && lowerContent.Contains("security")) ||
                lowerContent.Contains("shapesecurity") ||
                headers.Contains("x-shape"))
            {
                chkShapeSecurity.IsChecked = true;
                detectedTools.Add("Shape Security");
            }

            // ========== Kasada Detection ==========
            if (lowerContent.Contains("kasada") ||
                lowerContent.Contains("__kp") ||
                headers.Contains("x-kasada"))
            {
                chkKasada.IsChecked = true;
                detectedTools.Add("Kasada");
            }

            // ========== Distil Networks Detection ==========
            if (lowerContent.Contains("distil") ||
                lowerContent.Contains("__distil") ||
                headers.Contains("x-distil"))
            {
                chkDistil.IsChecked = true;
                detectedTools.Add("Distil Networks");
            }

            // ========== Radware Detection ==========
            if (lowerContent.Contains("radware") ||
                lowerContent.Contains("botmanager") ||
                headers.Contains("x-radware"))
            {
                chkRadware.IsChecked = true;
                detectedTools.Add("Radware");
            }

            // ========== Cloudflare WAF Detection ==========
            if (headers.Contains("cf-ray") ||
                headers.Contains("cf-cache-status") ||
                headers.Contains("cf-request-id"))
            {
                if (!detectedTools.Contains("Cloudflare WAF"))
                    detectedTools.Add("Cloudflare WAF");
                Debug.WriteLine("✅ Cloudflare WAF detected via headers");
            }

            // ========== Akamai Detection ==========
            if (lowerContent.Contains("akamai") ||
                headers.Contains("x-akamai-transformed") ||
                headers.Contains("akamai-grn") ||
                headers.Contains("x-akamai-requestid") ||
                lowerContent.Contains("akamaized.net"))
            {
                chkAkamai.IsChecked = true;
                detectedTools.Add("Akamai");
                Debug.WriteLine("✅ Akamai detected");
            }

            // ========== Imperva Detection ==========
            if (lowerContent.Contains("incapsula") ||
                headers.Contains("x-iinfo"))
            {
                chkImperva.IsChecked = true;
                detectedTools.Add("Imperva");
            }

            // ========== F5 Detection ==========
            if (headers.Contains("x-f5-auth") ||
                headers.Contains("f5-waf"))
            {
                chkF5.IsChecked = true;
                detectedTools.Add("F5 WAF");
            }

            // ========== Barracuda Detection ==========
            if (headers.Contains("x-barracuda"))
            {
                chkBarracuda.IsChecked = true;
                detectedTools.Add("Barracuda");
            }

            // ========== Citrix Detection ==========
            if (headers.Contains("x-citrix") ||
                headers.Contains("ns-cache"))
            {
                chkCitrix.IsChecked = true;
                detectedTools.Add("Citrix");
            }

            // ========== FortiWeb Detection ==========
            if (headers.Contains("x-fortiwaf") ||
                lowerContent.Contains("fortiweb"))
            {
                chkFortiWeb.IsChecked = true;
                detectedTools.Add("FortiWeb");
            }

            // ========== ModSecurity Detection ==========
            if (headers.Contains("x-modsecurity"))
            {
                chkModSecurity.IsChecked = true;
                detectedTools.Add("ModSecurity");
            }

            // ========== Sucuri Detection ==========
            if (lowerContent.Contains("sucuri") ||
                headers.Contains("x-sucuri"))
            {
                chkSucuri.IsChecked = true;
                detectedTools.Add("Sucuri");
            }

            // ========== Wordfence Detection ==========
            if (lowerContent.Contains("wordfence"))
            {
                chkWordfence.IsChecked = true;
                detectedTools.Add("Wordfence");
            }

            // Update WAF field
            string wafText = detectedTools.Count > 0
                ? string.Join(", ", detectedTools.Take(3))
                : "None detected";

            if (detectedTools.Count > 3)
                wafText += $" (+{detectedTools.Count - 3} more)";

            txtWAF.Text = wafText;

            // Update Challenge field
            var challengeTools = new List<string>();

            if (chkCloudflare.IsChecked == true) challengeTools.Add("Cloudflare");
            if (chkReCAPTCHAv2.IsChecked == true || chkReCAPTCHAv3.IsChecked == true) challengeTools.Add("reCAPTCHA");
            if (chkHCaptcha.IsChecked == true) challengeTools.Add("hCaptcha");
            if (chkDataDome.IsChecked == true) challengeTools.Add("DataDome");
            if (chkPerimeterX.IsChecked == true) challengeTools.Add("PerimeterX");
            if (chkKasada.IsChecked == true) challengeTools.Add("Kasada");
            if (chkDistil.IsChecked == true) challengeTools.Add("Distil");

            if (challengeTools.Count > 0)
            {
                txtChallenge.Text = string.Join(", ", challengeTools);
                ChallengeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            }
            else
            {
                txtChallenge.Text = "None";
                ChallengeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F5738"));
            }

            Debug.WriteLine($"\n🎯 Total tools detected: {detectedTools.Count}");
            if (detectedTools.Count > 0)
                Debug.WriteLine($"📋 Tools: {string.Join(", ", detectedTools)}");
            Debug.WriteLine($" ========== DETECTION COMPLETE ==========\n");
        }

        private void ResetAllCheckboxes()
        {
            chkJscrambler.IsChecked = false;
            chkArxan.IsChecked = false;
            chkApproov.IsChecked = false;
            chkGuardSquare.IsChecked = false;
            chkDataDome.IsChecked = false;
            chkPerimeterX.IsChecked = false;
            chkShapeSecurity.IsChecked = false;
            chkKasada.IsChecked = false;
            chkDistil.IsChecked = false;
            chkRadware.IsChecked = false;
            chkReCAPTCHAv2.IsChecked = false;
            chkReCAPTCHAv3.IsChecked = false;
            chkReCAPTCHAEnt.IsChecked = false;
            chkHCaptcha.IsChecked = false;
            chkCloudflare.IsChecked = false;
            chkFunCaptcha.IsChecked = false;
            chkGeeTest.IsChecked = false;
            chkTextImage.IsChecked = false;
            chkAkamai.IsChecked = false;
            chkImperva.IsChecked = false;
            chkF5.IsChecked = false;
            chkBarracuda.IsChecked = false;
            chkCitrix.IsChecked = false;
            chkFortiWeb.IsChecked = false;
            chkModSecurity.IsChecked = false;
            chkSucuri.IsChecked = false;
            chkWordfence.IsChecked = false;
        }

        private void UpdateRawHeaders(HttpResponseMessage response)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}");
            sb.AppendLine();

            foreach (var header in response.Headers)
            {
                sb.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
            }

            var contentHeaders = response.Content.Headers;
            foreach (var header in contentHeaders)
            {
                sb.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
            }

            txtRawHeaders.Text = sb.ToString();
        }

        private void CalculateSecurityGrade()
        {
            int score = 0;
            int total = 0;

            foreach (var item in SecurityHeadersPanel.Children)
            {
                if (item is Grid grid)
                {
                    var statusPanel = grid.Children[1] as StackPanel;
                    var statusIcon = statusPanel.Children[0] as TextBlock;
                    total++;
                    if (statusIcon.Text == "✓")
                        score++;
                }
            }

            string grade;
            Color gradeColor;
            int percentage = total > 0 ? (score * 100) / total : 0;

            if (score == total && total > 0)
            {
                grade = "A+";
                gradeColor = (Color)ColorConverter.ConvertFromString("#2F5738");
            }
            else if (percentage >= 80)
            {
                grade = "A";
                gradeColor = (Color)ColorConverter.ConvertFromString("#2F5738");
            }
            else if (percentage >= 60)
            {
                grade = "B";
                gradeColor = (Color)ColorConverter.ConvertFromString("#2F5738");
            }
            else if (percentage >= 40)
            {
                grade = "C";
                gradeColor = (Color)ColorConverter.ConvertFromString("#F59E0B");
            }
            else if (percentage >= 20)
            {
                grade = "D";
                gradeColor = (Color)ColorConverter.ConvertFromString("#D97706");
            }
            else
            {
                grade = "F";
                gradeColor = (Color)ColorConverter.ConvertFromString("#DC3545");
            }

            txtSecurityGrade.Text = grade;
            GradeBadge.Background = new SolidColorBrush(gradeColor);
            txtScore.Text = $"(score {percentage}/100)";
        }
    }
}
