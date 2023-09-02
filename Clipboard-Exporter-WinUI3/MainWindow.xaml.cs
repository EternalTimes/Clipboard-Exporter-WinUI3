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
        private List<string> clipboardHistory = new List<string>();
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
                        // 将剪贴板内容保存在内存中的历史记录中
                        clipboardHistory.Add(clipboardText);
                        UpdateClipboardHistoryText();
                    }
                }
            }
        }

        private void UpdateClipboardHistoryText()
        {
            // 更新显示当前剪贴板历史记录的文本框
            StringBuilder sb = new StringBuilder();
            foreach (string text in clipboardHistory)
            {
                sb.AppendLine(text);
            }
            clipboardHistoryTextBlock.Text = sb.ToString();
        }

        private void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            isMonitoringEnabled = toggleSwitch.IsOn;
        }

        private async void CopyContent_Click(object sender, RoutedEventArgs e)
        {
            // 复制整个历史记录到剪贴板
            if (clipboardHistory.Count > 0)
            {
                // 复制所有剪贴板历史记录到剪贴板
                DataPackage dataPackage = new DataPackage();
                dataPackage.SetText(string.Join("\n", clipboardHistory));
                Clipboard.SetContent(dataPackage);
            }
        }

        private async void ExportToFile_Click(object sender, RoutedEventArgs e)
        {
            // 使用文件选择器保存历史记录到纯文本文件
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss-fff}.txt"
            };
            picker.FileTypeChoices.Add("Text", new List<string> { ".txt" });

            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                // 将剪贴板历史记录写入选定的文件
                await FileIO.WriteTextAsync(file, string.Join("\n", clipboardHistory));
            }
        }

        private async void ClearContent_Click(object sender, RoutedEventArgs e)
        {
            // 清空内存中的历史记录
            clipboardHistory.Clear();
            UpdateClipboardHistoryText();
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
