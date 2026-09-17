using Core.Helpers;
using Core.Services;
using GoldenBullet.Utils;
using RuriLib.Models.Configs;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace GoldenBullet.ViewModels
{
    public class ConfigMetadataViewModel : ViewModelBase
    {
        private readonly ConfigService configService;

        // Keep Config private - access via properties only
        private Config Config => configService.SelectedConfig;

        public string Name
        {
            get => Config?.Metadata?.Name;
            set
            {
                if (Config?.Metadata != null)
                {
                    Config.Metadata.Name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Author
        {
            get => Config?.Metadata?.Author;
            set
            {
                if (Config?.Metadata != null)
                {
                    Config.Metadata.Author = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Category
        {
            get => Config?.Metadata?.Category;
            set
            {
                if (Config?.Metadata != null)
                {
                    Config.Metadata.Category = value;
                    OnPropertyChanged();
                }
            }
        }

        public BitmapImage Icon => Config is null ? null : Images.Base64ToBitmapImage(Config.Metadata.Base64Image);

        public ConfigMetadataViewModel()
        {
            configService = SP.GetService<ConfigService>();
        }

        public void UpdateViewModel()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Author));
            OnPropertyChanged(nameof(Category));
            OnPropertyChanged(nameof(Icon));
        }

        // ✅ Save method - minimal implementation
        public async Task SaveAsync()
        {
            if (Config is null)
                throw new InvalidOperationException("No config loaded to save.");

            // 🔹 The property setters already update Config.Metadata directly.
            // 🔹 Now we just need to persist the Config to disk.
            // 🔹 Replace the line below with your project's actual save logic:

            // Example: If your project has a method like this somewhere:
            // await configService.PersistSelectedConfigAsync();
            // OR
            // ConfigService.SaveSelectedConfig();
            // OR
            // var repo = SP.GetService<IConfigRepository>(); await repo.SaveAsync(Config);

            // Persist the selected config to the repository
            await configService.SaveAsync(Config).ConfigureAwait(false);

            // Ensure UI is updated after save:
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Author));
            OnPropertyChanged(nameof(Category));
            OnPropertyChanged(nameof(Icon));

            // TODO: Add your actual persistence call here based on your codebase
        }

        public void SetIconFromFile(string fileName)
        {
            if (Config?.Metadata == null) return;

            var bytes = ImageEditor.ToCompatibleFormat(File.ReadAllBytes(fileName));
            var base64 = Convert.ToBase64String(bytes);

            Config.Metadata.Base64Image = base64;
            OnPropertyChanged(nameof(Icon));
        }

        public async Task SetIconFromUrlAsync(string url)
        {
            if (Config?.Metadata == null) return;

            using var client = new HttpClient();
            using var response = await client.GetAsync(url);
            var bytes = ImageEditor.ToCompatibleFormat(await response.Content.ReadAsByteArrayAsync());

            var base64 = Convert.ToBase64String(bytes);
            Config.Metadata.Base64Image = base64;
            OnPropertyChanged(nameof(Icon));
        }
    }
}
