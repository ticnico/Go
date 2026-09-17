using Core.Helpers;
using GoldenBullet.Helpers;
using GoldenBullet.Utils;
using GoldenBullet.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GoldenBullet.Controls
{
    /// <summary>
    /// Interaction logic for ImagePicker.xaml
    /// </summary>
    public partial class ImagePicker : UserControl
    {
        public ImagePicker()
        {
            InitializeComponent();
            UpdateUI();
        }

        // DependencyProperty for Image binding
        public static readonly DependencyProperty ImageProperty =
            DependencyProperty.Register(nameof(Image), typeof(ImageSource), typeof(ImagePicker),
                new PropertyMetadata(null, OnImageChanged));

        public ImageSource Image
        {
            get => (ImageSource)GetValue(ImageProperty);
            set => SetValue(ImageProperty, value);
        }

        // DependencyProperty for ImagePath (optional)
        public static readonly DependencyProperty ImagePathProperty =
            DependencyProperty.Register(nameof(ImagePath), typeof(string), typeof(ImagePicker),
                new PropertyMetadata(string.Empty));

        public string ImagePath
        {
            get => (string)GetValue(ImagePathProperty);
            set => SetValue(ImagePathProperty, value);
        }

        private static void OnImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImagePicker picker)
            {
                picker.previewImage.Source = e.NewValue as ImageSource;
                picker.UpdateUI();
            }
        }

        private void UpdateUI()
        {
            bool hasImage = Image != null;

            // Toggle placeholder vs image
            placeholderPanel.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
            previewImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;

            // Toggle clear button
            clearImageButton.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;

            // Toggle file path display
            filePathText.Visibility = !string.IsNullOrEmpty(ImagePath) && hasImage
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ImagePreview_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                hoverOverlay.Visibility = Visibility.Visible;
            }
        }

        private void ImagePreview_Drop(object sender, DragEventArgs e)
        {
            hoverOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0)
                {
                    // Raise event or call callback to handle file load
                    OpenImageFromFile(files[0]);
                }
            }
        }

        private void ImagePreviewBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            if (Image != null)
                hoverOverlay.Visibility = Visibility.Visible;
        }

        private void ImagePreviewBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            hoverOverlay.Visibility = Visibility.Collapsed;
        }

        private void ClearImage(object sender, RoutedEventArgs e)
        {
            Image = null;
            ImagePath = string.Empty;
            // Raise ImageCleared event if needed
        }

        private void OpenImageFromFile(string path)
        {
            // Load image from file and update bindings
            try
            {
                var bitmap = new BitmapImage(new Uri(path));
                bitmap.Freeze();
                Image = bitmap;
                ImagePath = path;
            }
            catch
            {
                // Handle error (show message, etc.)
            }
        }

        private ImagePickerViewModel vm;
        public event EventHandler<byte[]> ImageChanged;

        public ImagePicker(byte[] imageBytes)
        {
            InitializeComponent();
            vm = new ImagePickerViewModel
            {
                ImageBytes = imageBytes
            };
            DataContext = vm;
        }

        private void OpenImage(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Images | *.ico;*.jpg;*.jpeg;*.png;*.bmp",
                FilterIndex = 1
            };

            ofd.ShowDialog();

            if (!string.IsNullOrEmpty(ofd.FileName))
            {
                try
                {
                    vm.SetImageFromFile(ofd.FileName);
                    ImageChanged?.Invoke(this, vm.ImageBytes);
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }
    }

    public class ImagePickerViewModel : ViewModelBase
    {
        private byte[] imageBytes;
        public byte[] ImageBytes
        {
            get => imageBytes;
            set
            {
                imageBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Image));
            }
        }

        public BitmapImage Image => ImageBytes is null ? null : Images.BytesToBitmapImage(ImageBytes);

        public void SetImageFromFile(string fileName)
            => ImageBytes = ImageEditor.ToCompatibleFormat(File.ReadAllBytes(fileName));
    }
}
