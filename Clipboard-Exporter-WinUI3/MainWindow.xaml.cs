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
using Microsoft.UI.Composition.SystemBackdrops;
using System.Runtime.InteropServices; // For DllImport
using WinRT; // required to support Window.As<ICompositionSupportsSystemBackdrop>()

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Clipboard_Exporter_WinUI3
{
    
    public sealed partial class MainWindow : Window
    {
        private string filePath;
        private bool isMonitoringEnabled = false;

        WindowsSystemDispatcherQueueHelper m_wsdqHelper; // See below for implementation.
        MicaController m_backdropController;
        SystemBackdropConfiguration m_configurationSource;

        public MainWindow()
        {
            this.InitializeComponent();
            InitializeClipboardExporter();
            toggleSwitch.Toggled += ToggleSwitch_Toggled;
            copyContentButton.Click += CopyContent_Click;
            exportToFileButton.Click += ExportToFile_Click;
            clearContentButton.Click += ClearContent_Click;
            TrySetSystemBackdrop();

            // 设置文件路径为用户文档目录下的 clipboard.txt
            filePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "clipboard.txt");
        }

        class WindowsSystemDispatcherQueueHelper
        {
            [StructLayout(LayoutKind.Sequential)]
            struct DispatcherQueueOptions
            {
                internal int dwSize;
                internal int threadType;
                internal int apartmentType;
            }

            [DllImport("CoreMessaging.dll")]
            private static extern int CreateDispatcherQueueController([In] DispatcherQueueOptions options, [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object dispatcherQueueController);

            object m_dispatcherQueueController = null;
            public void EnsureWindowsSystemDispatcherQueueController()
            {
                if (Windows.System.DispatcherQueue.GetForCurrentThread() != null)
                {
                    // one already exists, so we'll just use it.
                    return;
                }

                if (m_dispatcherQueueController == null)
                {
                    DispatcherQueueOptions options;
                    options.dwSize = Marshal.SizeOf(typeof(DispatcherQueueOptions));
                    options.threadType = 2;    // DQTYPE_THREAD_CURRENT
                    options.apartmentType = 2; // DQTAT_COM_STA

                    CreateDispatcherQueueController(options, ref m_dispatcherQueueController);
                }
            }
        }

        private void InitializeClipboardExporter()
        {
            Clipboard.ContentChanged += Clipboard_ContentChanged;
        }

        private void Clipboard_ContentChanged(object sender, object e)
        {
            if (isMonitoringEnabled)
            {
                DataPackageView clipboardData = Clipboard.GetContent();
                if (clipboardData.Contains(StandardDataFormats.Text))
                {
                    string clipboardText = clipboardData.GetTextAsync().GetResults(); // 同步获取文本内容
                    if (!string.IsNullOrEmpty(clipboardText))
                    {
                        // 将剪贴板内容保存在文件中
                        AppendTextToFile(filePath, clipboardText + "\n");
                    }
                }
            }
        }

        private async void AppendTextToFile(string filePath, string textToAppend)
        {
            // 检查文件是否存在，如果不存在则创建
            StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("clipboard.txt", CreationCollisionOption.OpenIfExists);

            // 向文件追加文本内容
            await FileIO.AppendTextAsync(file, textToAppend);
        }

        private async Task RefreshTextBoxAsync()
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
            if (file != null)
            {
                string fileContent = await FileIO.ReadTextAsync(file);
                textBoxContent.Text = fileContent;
            }
        }

        private void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            isMonitoringEnabled = toggleSwitch.IsOn;
        }

        private async void CopyContent_Click(object sender, RoutedEventArgs e)
        {
            // 从文件中获取最新的剪贴板内容并复制到剪贴板
            string latestClipboardText = await ReadTextFromFile(filePath);
            if (!string.IsNullOrEmpty(latestClipboardText))
            {
                DataPackage dataPackage = new DataPackage();
                dataPackage.SetText(latestClipboardText);
                Clipboard.SetContent(dataPackage);
            }
        }

        private async void ExportToFile_Click(object sender, RoutedEventArgs e)
        {
            // 导出剪贴板内容到用户文档目录下的文件
            StorageFile file = await KnownFolders.DocumentsLibrary.CreateFileAsync($"{DateTime.Now:yyyy-MM-dd-HH-mm-ss-fff}.txt");
            string clipboardText = await ReadTextFromFile(filePath);

            if (!string.IsNullOrEmpty(clipboardText))
            {
                await FileIO.WriteTextAsync(file, clipboardText);
            }
        }

        private async Task<string> ReadTextFromFile(string filePath)
        {
            try
            {
                // 从文件中读取文本内容
                StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
                return await FileIO.ReadTextAsync(file);
            }
            catch (Exception)
            {
                // 处理文件不存在或读取错误的情况
                return string.Empty;
            }
        }

        private async void ClearContent_Click(object sender, RoutedEventArgs e)
        {
            // 清空文件中的剪贴板内容
            await FileIO.WriteTextAsync(await StorageFile.GetFileFromPathAsync(filePath), string.Empty);
        }

        bool TrySetSystemBackdrop()
        {
            if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
            {
                m_wsdqHelper = new WindowsSystemDispatcherQueueHelper();
                m_wsdqHelper.EnsureWindowsSystemDispatcherQueueController();

                // Create the policy object.
                m_configurationSource = new SystemBackdropConfiguration();
                this.Activated += Window_Activated;
                this.Closed += Window_Closed;
                ((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;

                // Initial configuration state.
                m_configurationSource.IsInputActive = true;
                SetConfigurationSourceTheme();

                m_backdropController = new Microsoft.UI.Composition.SystemBackdrops.MicaController();

                // Enable the system backdrop.
                // Note: Be sure to have "using WinRT;" to support the Window.As<...>() call.
                m_backdropController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                m_backdropController.SetSystemBackdropConfiguration(m_configurationSource);
                return true; // succeeded
            }

            return false; // Mica is not supported on this system
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs args)
        {
            m_configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            // Make sure any Mica/Acrylic controller is disposed
            // so it doesn't try to use this closed window.
            if (m_backdropController != null)
            {
                m_backdropController.Dispose();
                m_backdropController = null;
            }
            this.Activated -= Window_Activated;
            m_configurationSource = null;
        }

        private void Window_ThemeChanged(FrameworkElement sender, object args)
        {
            if (m_configurationSource != null)
            {
                SetConfigurationSourceTheme();
            }
        }

        private void SetConfigurationSourceTheme()
        {
            switch (((FrameworkElement)this.Content).ActualTheme)
            {
                case ElementTheme.Dark: m_configurationSource.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark; break;
                case ElementTheme.Light: m_configurationSource.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light; break;
                case ElementTheme.Default: m_configurationSource.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Default; break;
            }
        }
    }
}
