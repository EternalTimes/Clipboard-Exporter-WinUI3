using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Clipboard_Exporter_WinUI3
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private string filePath = "clipboard.txt";
        private bool isMonitoringEnabled = false;

        public MainWindow()
        {
            this.InitializeComponent();
            
            toggleSwitch.Toggled += ToggleSwitch_Toggled;
            Clipboard.ContentChanged += Clipboard_ContentChanged;
        }

        private async void Clipboard_ContentChanged(object sender, object e)
        {
            if (isMonitoringEnabled)
            {
                DataPackageView clipboardData = Clipboard.GetContent();
                if (clipboardData.Contains(StandardDataFormats.Text))
                {
                    string clipboardText = await clipboardData.GetTextAsync();
                    if (!string.IsNullOrEmpty(clipboardText))
                    {
                        await FileIO.AppendTextAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath), clipboardText + "\n");
                        await RefreshTextBox();
                    }
                }
            }
        }

        private async Task RefreshTextBox()
        {
            Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
            if (file != null)
            {
                string fileContent = await Windows.Storage.FileIO.ReadTextAsync(file);
                textBoxContent.Text = fileContent;
            }
        }

        private void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            isMonitoringEnabled = toggleSwitch.IsOn;
        }

        private void CopyContent_Click(object sender, RoutedEventArgs e)
        {
            DataPackage dataPackage = new DataPackage();
            dataPackage.SetText(textBoxContent.Text);
            Clipboard.SetContent(dataPackage);
        }

        private async void ExportToFile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss-fff}.txt"
            };
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });

            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await FileIO.WriteTextAsync(file, textBoxContent.Text);
            }
        }

        private async void ClearContent_Click(object sender, RoutedEventArgs e)
        {
            textBoxContent.Text = string.Empty;
            await FileIO.WriteTextAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath), string.Empty);
        }
    }
}