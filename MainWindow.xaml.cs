using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Composition;
using Windows.Graphics.DirectX;
using SharpDX.Direct3D11;
using Windows.Media.MediaProperties;
using Windows.Media.Core;
using Windows.Media.Transcoding;
using System.Threading;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Windows.Interop;
using System.Security.Policy;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using System.IO;
using Windows.Graphics.Imaging;
using System.Text;
using System.Linq;

namespace ScreenCaptureVideoWPF {
	/// <summary>
	/// 
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window {
		// Capture API objects.

		// Non-API related members.
		private IntPtr _hwnd;
		private CompositionGraphicsDevice _compositionGraphicsDevice;
		private Compositor _compositor;
		private CompositionTarget _target;
		private CompositionDrawingSurface _surface;

		private GraphicsCaptureItem _captureItem;
		private Encoding _encodingItem;
		
		// variables for saving screenshot
		private Texture2D _cpuTexture;
		private long _currentSaveFrameTime;
		public string _captureItemDisplayName;

		//Config variables
		private string _filePath = $@"C:\temp\";

		public MainWindow() {
			InitializeComponent();

			// Force graphicscapture.dll to load.
			GraphicsCapturePicker picker = new GraphicsCapturePicker();
			//Task setup = SetupEncoding();
		}

		private void Window_Loaded(object sender, RoutedEventArgs e) {
			WindowInteropHelper interopWindow = new WindowInteropHelper(this);
			_hwnd = interopWindow.Handle;
		}

		private async Task SetupEncodingAndStartRecording() {
			if(!GraphicsCaptureSession.IsSupported()) {
				throw new Exception("Video capture is not supported");
			}

			try {
				//_captureItems = GetAllMonitorItemForCapture();
				//if(!_captureItems.Any()) {
				//	throw new Exception("No Item to Capture");
				//}

				//Let the user pick an item to capture
				GraphicsCapturePicker picker = new GraphicsCapturePicker();
				picker.SetWindow(_hwnd);
				_captureItem = await picker.PickSingleItemAsync();
				if(_captureItem == null) {
					throw new Exception("No Item to Capture");
				}

				_encodingItem = new Encoding(_captureItem); 
				_encodingItem.startVideoCapture(_filePath);

			}
			catch (Exception ex) {
				throw new Exception($"{ex.Message}");
			}
		}

		private List<GraphicsCaptureItem> GetAllMonitorItemForCapture() {
			List<GraphicsCaptureItem> monitorItems = new List<GraphicsCaptureItem>();
			foreach(MonitorInfo monitor in MonitorEnumerationHelper.GetMonitors()) {
				GraphicsCaptureItem item = CaptureHelper.CreateItemForMonitor(monitor.Hmon);
				monitorItems.Add(item);
			}

			return monitorItems;
		}

		private async void StartCaptureButton_ClickAsync(object sender, RoutedEventArgs e) {
			try {
				await SetupEncodingAndStartRecording();
			}
			catch(Exception ex) {
				MessageBox.Show($@"Error Message: {ex.Message} 
								Inner Exception: {ex.InnerException}");
			}
		}

		private void StopCaptureButton_Click(object sender, RoutedEventArgs e) {
			try {
				if(_encodingItem == null || _encodingItem.GetIsClosed()) {
					// TODO: Implement multiple recordings on different windows
					throw new Exception("Recording already off. Need to start one before stopping");
				}
				_encodingItem.StopVideoCapture();
			}
			catch(Exception ex) {
				MessageBox.Show($@"Error Message: {ex.Message} 
								Inner Exception: {ex.InnerException}");
			}
		}

		private async void TakeScreenshotButton_Click(object sender, RoutedEventArgs e) {
			try {
				if(_encodingItem == null) {
					throw new Exception("Video recording is not on. Cannot take screenshot.");
				}
				await _encodingItem.SaveScreenshotOfCurrentFrame(_filePath);
			}
			catch(Exception ex) {
				MessageBox.Show($@"Error Message: {ex.Message} 
								Inner Exception: {ex.InnerException}");
			}
		}
	}
}
