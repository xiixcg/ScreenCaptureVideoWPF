using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Windows.Graphics.Capture;

namespace ScreenCaptureVideoWPF {
    /// <summary>
    /// 
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        // Capture API objects.
        private List<GraphicsCaptureItem> _captureItems;
        private List<Encoding> _encodingItems;

        // variables for saving screenshot
        public string _captureItemDisplayName;

        //Config variables
        private string _filePath = $@"C:\temp\";

        public MainWindow() {
            InitializeComponent();

            // Force graphicscapture.dll to load.
            GraphicsCapturePicker picker = new GraphicsCapturePicker();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            WindowInteropHelper interopWindow = new WindowInteropHelper(this);
        }

        private async Task SetupEncodingAndStartRecording() {
            if (!GraphicsCaptureSession.IsSupported()) {
                throw new Exception("Video capture is not supported");
            }

            try {
                _captureItems = GetAllMonitorItemForCapture();
                if (!_captureItems.Any()) {
                    throw new Exception("No Item to Capture");
                }

                // Create the filePath if not existing
                Directory.CreateDirectory(_filePath);
                _encodingItems = new List<Encoding>();
                string videoFilePrefix = Utils.GetEpochTimeNow();
                foreach (GraphicsCaptureItem captureItem in _captureItems) {
                    Encoding encodingItem = new Encoding(captureItem);
                    encodingItem.startVideoCapture(_filePath, videoFilePrefix);

                    _encodingItems.Add(encodingItem);
                }
            }
            catch (Exception ex) {
                throw new Exception($"{ex.Message}");
            }
        }

        private List<GraphicsCaptureItem> GetAllMonitorItemForCapture() {
            List<GraphicsCaptureItem> monitorItems = new List<GraphicsCaptureItem>();
            foreach (MonitorInfo monitor in MonitorEnumerationHelper.GetMonitors()) {
                GraphicsCaptureItem item = CaptureHelper.CreateItemForMonitor(monitor.Hmon);
                monitorItems.Add(item);
            }

            return monitorItems;
        }

        private async void StartCaptureButton_ClickAsync(object sender, RoutedEventArgs e) {
            try {
                await SetupEncodingAndStartRecording();
            }
            catch (Exception ex) {
                MessageBox.Show($@"Error Message: {ex.Message} 
								Inner Exception: {ex.InnerException}");
            }
        }

        private void StopCaptureButton_Click(object sender, RoutedEventArgs e) {
            try {
                foreach (Encoding encodingItem in _encodingItems) {
                    if (encodingItem == null || encodingItem.GetIsClosed()) {
                        // TODO: Implement multiple recordings on different windows
                        throw new Exception("Recording already off. Need to start one before stopping");
                    }
                    encodingItem.StopVideoCapture();
                }
            }
            catch (Exception ex) {
                MessageBox.Show($@"Error Message: {ex.Message} 
								Inner Exception: {ex.InnerException}");
            }
        }

        private async void TakeScreenshotButton_Click(object sender, RoutedEventArgs e) {
            try {
                string imageFilePrefix = Utils.GetEpochTimeNow();
                foreach (Encoding encodingItem in _encodingItems) {
                    if (encodingItem == null) {
                        throw new Exception("Video recording is not on. Cannot take screenshot.");
                    }
                    await encodingItem.SaveScreenshotOfCurrentFrame(_filePath, imageFilePrefix);
                }
            }
            catch (Exception ex) {
                MessageBox.Show($@"Error Message: {ex.Message} 
								Inner Exception: {ex.InnerException}");
            }
        }
    }
}
