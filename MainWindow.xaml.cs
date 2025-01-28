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
		private bool _shouldSaveScreenshot;
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

		/// <summary>
		/// Create screenshot file from current frame texture using the current unix time in milliseconds
		/// </summary>
		/// <param name="currentSaveFrameTime"></param>
		private void SaveToScreenshootFromAFrameTexture(long currentSaveFrameTime) {
			try {
				Task.Run(async () => {
					// now,  this is just an example that only saves the first frame
					// but you could also use
					// d3dDevice.ImmediateContext.MapSubresource(cpuTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out var stream); and d3dDevice.ImmediateContext.UnMapSubresource
					// to get the bytes (out from the returned stream)

					// get IDirect3DSurface from texture
					IDirect3DSurface surf = Direct3D11Helpers.CreateDirect3DSurfaceFromSharpDXTexture(_cpuTexture);

					// build a WinRT's SoftwareBitmap from this surface/texture
					SoftwareBitmap softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surf);

					// Set the directory to save file and create if directory isn't there
					// TODO: _item.DisplayName get error "The application called an interface that was marshalled for a different thread."
					//string screenshotFileName = _filePath + _currentSaveFrameTime.ToString() + "_" + _item.DisplayName + @".png";
					string screenshotFileName = _filePath + currentSaveFrameTime.ToString() + "_" + _captureItemDisplayName + ".png";

					using(FileStream file = new FileStream(screenshotFileName, FileMode.Create, FileAccess.Write)) {
						// create a PNG encoder
						BitmapEncoder encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());

						// set the bitmap to it & flush
						encoder.SetSoftwareBitmap(softwareBitmap);
						await encoder.FlushAsync();
						return;
					}
				});				
			}
			catch(Exception ex) {
				MessageBox.Show($@"Error Message: {ex.Message}
									Inner Exception: {ex.InnerException}");
				return;
			}
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

		private void TakeScreenshotButton_Click(object sender, RoutedEventArgs e) {
			if(_cpuTexture == null) {
				MessageBox.Show("Video recording is not on. Cannot take screenshot.");
				return;
			}
			_shouldSaveScreenshot = true;
			_currentSaveFrameTime = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
			SaveToScreenshootFromAFrameTexture(_currentSaveFrameTime);
		}
	}
}
