using GoldenBullet.ViewModels;
using RuriLib.Functions.Http.Options; // Added for HttpLibrary enum
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Custom.HttpRequest;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Blocks.Settings;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;

namespace GoldenBullet.Controls
{
    /// <summary>
    /// Interaction logic for HttpRequestBlockSettingsViewer.xaml
    /// </summary>
    public partial class HttpRequestBlockSettingsViewer : UserControl
    {
        private readonly HttpRequestBlockSettingsViewerViewModel vm;

        public HttpRequestBlockSettingsViewer(BlockViewModel blockVM)
        {
            InitializeComponent();

            if (blockVM.Block is not HttpRequestBlockInstance)
            {
                throw new Exception("Wrong block type for this UC");
            }

            vm = new HttpRequestBlockSettingsViewerViewModel(blockVM);
            vm.ModeChanged += mode =>
            {
                BindSettings();
                tabControl.SelectedIndex = (int)mode;
            };

            // FIX: Call BindSettings() BEFORE setting the DataContext.
            // This ensures that child controls (like StringSettingViewer, ByteArraySettingViewer, etc.)
            // receive their specific Setting and set their own DataContext before the parent's DataContext
            // is assigned. This prevents them from temporarily inheriting the parent's DataContext
            // and throwing "property not found" binding errors.
            tabControl.SelectedIndex = (int)vm.Mode;
            BindSettings();

            DataContext = vm;

            // Update UI visibility when the HTTP Library dropdown changes
            httpLibrarySetting.ValueChanged += (_, _) => vm.UpdateLibraryDependentSettingsVisibility();
        }

        private void BindSettings()
        {
            // Core Settings
            urlSetting.Setting = vm.HttpRequestBlock.Settings["url"];
            methodSetting.Setting = vm.HttpRequestBlock.Settings["method"];
            httpVersionSetting.Setting = vm.HttpRequestBlock.Settings["httpVersion"];
            autoRedirectSetting.Setting = vm.HttpRequestBlock.Settings["autoRedirect"];
            alwaysSendContentSetting.Setting = vm.HttpRequestBlock.Settings["alwaysSendContent"];
            decodeHtmlSetting.Setting = vm.HttpRequestBlock.Settings["decodeHtml"];

            // Proxy Mode Setting (New)
            if (vm.HttpRequestBlock.Settings.ContainsKey("proxyMode"))
            {
                proxyModeSetting.Setting = vm.HttpRequestBlock.Settings["proxyMode"];
            }

            switch (vm.Mode)
            {
                case HttpRequestMode.Standard:
                    standardContentSetting.Setting = (vm.HttpRequestBlock.RequestParams as StandardRequestParams).Content;
                    standardContentTypeSetting.Setting = (vm.HttpRequestBlock.RequestParams as StandardRequestParams).ContentType;
                    urlEncodeContentSetting.Setting = vm.HttpRequestBlock.Settings["urlEncodeContent"];
                    break;

                case HttpRequestMode.Raw:
                    rawContentSetting.Setting = (vm.HttpRequestBlock.RequestParams as RawRequestParams).Content;
                    rawContentTypeSetting.Setting = (vm.HttpRequestBlock.RequestParams as RawRequestParams).ContentType;
                    break;

                case HttpRequestMode.BasicAuth:
                    basicAuthUsernameSetting.Setting = (vm.HttpRequestBlock.RequestParams as BasicAuthRequestParams).Username;
                    basicAuthPasswordSetting.Setting = (vm.HttpRequestBlock.RequestParams as BasicAuthRequestParams).Password;
                    break;

                case HttpRequestMode.Multipart:
                    multipartBoundarySetting.Setting = (vm.HttpRequestBlock.RequestParams as MultipartRequestParams).Boundary;
                    vm.LoadMultipartParts();
                    break;
            }

            // Additional Settings
            customCookiesSetting.Setting = vm.HttpRequestBlock.Settings["customCookies"];
            customHeadersSetting.Setting = vm.HttpRequestBlock.Settings["customHeaders"];
            timeoutMillisecondsSetting.Setting = vm.HttpRequestBlock.Settings["timeoutMilliseconds"];
            maxNumberOfRedirectsSetting.Setting = vm.HttpRequestBlock.Settings["maxNumberOfRedirects"];
            absoluteUriInFirstLineSetting.Setting = vm.HttpRequestBlock.Settings["absoluteUriInFirstLine"];
            readResponseContentSetting.Setting = vm.HttpRequestBlock.Settings["readResponseContent"];
            codePagesEncodingSetting.Setting = vm.HttpRequestBlock.Settings["codePagesEncoding"];

            // Advanced Settings
            httpLibrarySetting.Setting = vm.HttpRequestBlock.Settings["httpLibrary"];
            curlImpersonateBrowserProfileSetting.Setting = vm.HttpRequestBlock.Settings["curlImpersonateBrowserProfile"];
            curlUseBrowserHeadersSetting.Setting = vm.HttpRequestBlock.Settings["curlUseBrowserHeaders"];
            securityProtocolSetting.Setting = vm.HttpRequestBlock.Settings["securityProtocol"];
            ignoreCertificateValidationSetting.Setting = vm.HttpRequestBlock.Settings["ignoreCertificateValidation"];
            useCustomCipherSuitesSetting.Setting = vm.HttpRequestBlock.Settings["useCustomCipherSuites"];
            customCipherSuitesSetting.Setting = vm.HttpRequestBlock.Settings["customCipherSuites"];
        }
    }

    public class HttpRequestBlockSettingsViewerViewModel : BlockSettingsViewerViewModel
    {
        public HttpRequestBlockInstance HttpRequestBlock => Block as HttpRequestBlockInstance;

        public bool SafeMode
        {
            get => HttpRequestBlock.Safe;
            set
            {
                HttpRequestBlock.Safe = value;
                OnPropertyChanged();
            }
        }

        public event Action<HttpRequestMode> ModeChanged;

        private StandardRequestParams cachedStandardParams = new();
        private RawRequestParams cachedRawParams = new();
        private BasicAuthRequestParams cachedBasicAuthParams = new();
        private MultipartRequestParams cachedMultipartParams = new();

        public HttpRequestMode Mode
        {
            get => HttpRequestBlock.RequestParams switch
            {
                StandardRequestParams => HttpRequestMode.Standard,
                RawRequestParams => HttpRequestMode.Raw,
                BasicAuthRequestParams => HttpRequestMode.BasicAuth,
                MultipartRequestParams => HttpRequestMode.Multipart,
                _ => throw new NotImplementedException()
            };
            set
            {
                HttpRequestBlock.RequestParams = value switch
                {
                    HttpRequestMode.Standard => cachedStandardParams,
                    HttpRequestMode.Raw => cachedRawParams,
                    HttpRequestMode.BasicAuth => cachedBasicAuthParams,
                    HttpRequestMode.Multipart => cachedMultipartParams,
                    _ => throw new NotImplementedException()
                };

                ModeChanged?.Invoke(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(MultipartParts));
            }
        }

        public bool StandardMode
        {
            get => Mode == HttpRequestMode.Standard;
            set
            {
                if (value) Mode = HttpRequestMode.Standard;
                OnPropertyChanged();
            }
        }

        public bool RawMode
        {
            get => Mode == HttpRequestMode.Raw;
            set
            {
                if (value) Mode = HttpRequestMode.Raw;
                OnPropertyChanged();
            }
        }

        public bool BasicAuthMode
        {
            get => Mode == HttpRequestMode.BasicAuth;
            set
            {
                if (value) Mode = HttpRequestMode.BasicAuth;
                OnPropertyChanged();
            }
        }

        public bool MultipartMode
        {
            get => Mode == HttpRequestMode.Multipart;
            set
            {
                if (value) Mode = HttpRequestMode.Multipart;
                OnPropertyChanged();
            }
        }

        // --- cURL Impersonate Visibility Logic ---
        public bool IsCurlImpersonate => HttpLibrarySetting.Value == nameof(HttpLibrary.CurlImpersonate);

        public bool IsNotCurlImpersonate => !IsCurlImpersonate;

        public void UpdateLibraryDependentSettingsVisibility()
        {
            OnPropertyChanged(nameof(IsCurlImpersonate));
            OnPropertyChanged(nameof(IsNotCurlImpersonate));
        }

        private EnumSetting HttpLibrarySetting
            => (EnumSetting)HttpRequestBlock.Settings["httpLibrary"].FixedSetting!;
        // -----------------------------------------

        // Multipart editing properties
        public ObservableCollection<MultipartPartViewModel> MultipartParts { get; } = new();
        public ICommand AddMultipartPartCommand { get; }
        public ICommand RemoveMultipartPartCommand { get; }

        public HttpRequestBlockSettingsViewerViewModel(BlockViewModel block) : base(block)
        {
            AddMultipartPartCommand = new RelayCommand(AddMultipartPart);
            RemoveMultipartPartCommand = new RelayCommand<MultipartPartViewModel>(RemoveMultipartPart);
        }

        public void LoadMultipartParts()
        {
            if (HttpRequestBlock.RequestParams is not MultipartRequestParams multipart)
                return;

            MultipartParts.Clear();
            foreach (var content in multipart.Contents)
            {
                MultipartParts.Add(new MultipartPartViewModel(content));
            }
            OnPropertyChanged(nameof(MultipartParts));
        }

        private void AddMultipartPart()
        {
            if (HttpRequestBlock.RequestParams is not MultipartRequestParams multipart)
                return;

            var newPart = new StringHttpContentSettingsGroup
            {
                Name = BlockSettingFactory.CreateStringSetting("field_name"),
                ContentType = BlockSettingFactory.CreateStringSetting("Content Type", "text/plain"),
                Data = BlockSettingFactory.CreateStringSetting("field_value")
            };

            multipart.Contents.Add(newPart);
            MultipartParts.Add(new MultipartPartViewModel(newPart));

            OnPropertyChanged(nameof(MultipartParts));
        }

        private void RemoveMultipartPart(MultipartPartViewModel partViewModel)
        {
            if (HttpRequestBlock.RequestParams is not MultipartRequestParams multipart)
                return;

            var realPart = multipart.Contents.FirstOrDefault(c =>
                c.Name == partViewModel.RealContent.Name &&
                c.ContentType == partViewModel.RealContent.ContentType &&
                ((c is StringHttpContentSettingsGroup s && partViewModel.RealContent is StringHttpContentSettingsGroup s2 && s.Data == s2.Data) ||
                 (c is FileHttpContentSettingsGroup f && partViewModel.RealContent is FileHttpContentSettingsGroup f2 && f.FileName == f2.FileName)));

            if (realPart != null)
            {
                multipart.Contents.Remove(realPart);
                MultipartParts.Remove(partViewModel);
                OnPropertyChanged(nameof(MultipartParts));
            }
        }
    }

    public class MultipartPartViewModel : INotifyPropertyChanged
    {
        public HttpContentSettingsGroup RealContent { get; }

        public bool IsStringType => RealContent is StringHttpContentSettingsGroup;
        public bool IsFileType => RealContent is FileHttpContentSettingsGroup;

        public string TypeIcon => IsStringType ? "Text" : "FileUpload";
        public string TypeLabel => IsStringType ? "TEXT" : "FILE";
        public string TypeIndicator => IsStringType ? "📝 Text" : "📁 File";

        public BlockSetting NameSetting => RealContent?.Name ?? BlockSettingFactory.CreateStringSetting("");
        public BlockSetting ContentTypeSetting => RealContent?.ContentType ?? BlockSettingFactory.CreateStringSetting("");
        public BlockSetting DataSetting => (RealContent as StringHttpContentSettingsGroup)?.Data ?? BlockSettingFactory.CreateStringSetting("");
        public BlockSetting FileNameSetting => (RealContent as FileHttpContentSettingsGroup)?.FileName ?? BlockSettingFactory.CreateStringSetting("");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public MultipartPartViewModel(HttpContentSettingsGroup content)
        {
            RealContent = content ?? throw new ArgumentNullException(nameof(content));
        }
    }

    public enum HttpRequestMode
    {
        Standard,
        Raw,
        BasicAuth,
        Multipart
    }

    // Simple RelayCommand implementation
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute((T)parameter);
        public void Execute(object parameter) => _execute((T)parameter);
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
