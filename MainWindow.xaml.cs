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

namespace ScreenCaptureVideoWPF {
	/// <summary>
	/// 
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window {
		// Capture API objects.
		private List<GraphicsCaptureItem> _items;
		private Direct3D11CaptureFramePool _framePool;
		private GraphicsCaptureSession _session;

		// Non-API related members.
		private IntPtr _hwnd;
		private CompositionGraphicsDevice _compositionGraphicsDevice;
		private Compositor _compositor;
		private CompositionTarget _target;
		private CompositionDrawingSurface _surface;


		private IDirect3DDevice _device;
		private Device _sharpDxD3dDevice;
		private List<GraphicsCaptureItem> _captureItems;
		private GraphicsCaptureItem _captureItem;
		private Texture2D _composeTexture;
		private RenderTargetView _composeRenderTargetView;
		private MediaEncodingProfile _encodingProfile;
		private VideoStreamDescriptor _videoDescriptor;
		private MediaStreamSource _mediaStreamSource;
		private MediaTranscoder _transcoder;
		private bool _isRecording;
		private bool _isClosed = true;	// Flag for video file open for encoding
		private Multithread _multithread;
		private ManualResetEvent _frameEvent;
		private ManualResetEvent _closedEvent;
		private ManualResetEvent[] _events;
		private Direct3D11CaptureFrame _currentFrame;

		// variables for saving screenshot
		private Texture2D _cpuTexture;
		private bool _shouldSaveScreenshot;
		private long _currentSaveFrameTime;
		public string _captureItemDisplayName;

		//Config variables
		private string _filePath = $@"C:\temp\";

		public MainWindow() {
			InitializeComponent();
			if(_items == null) {
				_items = new List<GraphicsCaptureItem>();
			}

			// Force graphicscapture.dll to load.
			GraphicsCapturePicker picker = new GraphicsCapturePicker();
			//Task setup = SetupEncoding();
		}

		private void Window_Loaded(object sender, RoutedEventArgs e) {
			WindowInteropHelper interopWindow = new WindowInteropHelper(this);
			_hwnd = interopWindow.Handle;
		}

		private async Task SetupEncoding() {
			if(!GraphicsCaptureSession.IsSupported()) {
				// Show message to user that screen capture is unsupported
				return;
			}

			// Create the D3D device and SharpDX device
			if(_device == null) {
				_device = Direct3D11Helpers.CreateD3DDevice();
			}
			if(_sharpDxD3dDevice == null) {
				_sharpDxD3dDevice = Direct3D11Helpers.CreateSharpDXDevice(_device);
			}

			try {
				//Let the user pick an item to capture
				GraphicsCapturePicker picker = new GraphicsCapturePicker();
				picker.SetWindow(_hwnd);
				_captureItem = await picker.PickSingleItemAsync();
				if(_captureItem == null) {
					return;
				}

				// TODO: change to monior recording and use displayname
				//_captureItemDisplayName = _captureItem.DisplayName;
				_captureItemDisplayName = "screenshot";

				// Initialize a blank texture and render target view for copying frames, using the same size as the capture item
				_composeTexture = Direct3D11Helpers.InitializeComposeTexture(_sharpDxD3dDevice, _captureItem.Size);
				_composeRenderTargetView = new SharpDX.Direct3D11.RenderTargetView(_sharpDxD3dDevice, _composeTexture);


				_cpuTexture = CreateTexture2D(_captureItem.Size.Width, _captureItem.Size.Height);

				// This example encodes video using the item's actual size.
				uint width = (uint) _captureItem.Size.Width;
				uint height = (uint) _captureItem.Size.Height;

				// Make sure the dimensions are are even. Required by some encoders.
				width = (width % 2 == 0) ? width : width + 1;
				height = (height % 2 == 0) ? height : height + 1;


				MediaEncodingProfile temp = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
				uint bitrate = temp.Video.Bitrate;
				uint framerate = 30;

				_encodingProfile = new MediaEncodingProfile();
				_encodingProfile.Container.Subtype = "MPEG4";
				_encodingProfile.Video.Subtype = "H264";
				_encodingProfile.Video.Width = width;
				_encodingProfile.Video.Height = height;
				_encodingProfile.Video.Bitrate = bitrate;
				_encodingProfile.Video.FrameRate.Numerator = framerate;
				_encodingProfile.Video.FrameRate.Denominator = 1;
				_encodingProfile.Video.PixelAspectRatio.Numerator = 1;
				_encodingProfile.Video.PixelAspectRatio.Denominator = 1;

				VideoEncodingProperties videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, width, height);
				_videoDescriptor = new VideoStreamDescriptor(videoProperties);

				// Create our MediaStreamSource
				_mediaStreamSource = new MediaStreamSource(_videoDescriptor);
				_mediaStreamSource.BufferTime = TimeSpan.FromSeconds(0);
				_mediaStreamSource.Starting += OnMediaStreamSourceStarting;
				_mediaStreamSource.SampleRequested += OnMediaStreamSourceSampleRequested;

				// Create our transcoder
				_transcoder = new MediaTranscoder();
				_transcoder.HardwareAccelerationEnabled = true;


				// Create a destination file - Access to the VideosLibrary requires the "Videos Library" capability
				StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(_filePath);
				//var folder = KnownFolders.VideosLibrary;
				string name = DateTime.Now.ToString("yyyyMMddHHmmss");
				StorageFile file = await folder.CreateFileAsync($"{name}.mp4");

				using(IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite)) {
					_isClosed = false;
					await EncodeAsync(stream);
				}


			}
			catch(Exception ex) {
				MessageBox.Show($@"Error Message: {ex.Message}
									Inner Exception: {ex.InnerException}");
				return;
			}
		}

		private async Task EncodeAsync(IRandomAccessStream stream) {
			while(!_isRecording && !_isClosed) {
				_isRecording = true;

				StartCapture();

				PrepareTranscodeResult transcode = await _transcoder.PrepareMediaStreamSourceTranscodeAsync(_mediaStreamSource, stream, _encodingProfile);

				await transcode.TranscodeAsync();
			}
			// isn't really being used for anything but will keep it for now
			_isRecording = false;
		}

		private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args) {
			if(_isRecording && !_isClosed) {
				try {
					using(SurfaceWithInfo frame = WaitForNewFrame()) {
						if(frame == null) {
							args.Request.Sample = null;
							StopCapture();
							Cleanup();
							return;
						}

						TimeSpan timeStamp = frame.SystemRelativeTime;

						MediaStreamSample sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, timeStamp);
						args.Request.Sample = sample;
					}
				}
				catch(Exception e) {
					Debug.WriteLine(e.Message);
					Debug.WriteLine(e.StackTrace);
					Debug.WriteLine(e);
					args.Request.Sample = null;
					StopCapture();
					Cleanup();
				}
			}
			else {
				args.Request.Sample = null;
				StopCapture();
				Cleanup();
			}
		}

		private void OnMediaStreamSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args) {
			using(SurfaceWithInfo frame = WaitForNewFrame()) {
				args.Request.SetActualStartPosition(frame.SystemRelativeTime);
			}
		}

		public void StartCapture() {

			_multithread = _sharpDxD3dDevice.QueryInterface<SharpDX.Direct3D11.Multithread>();
			_multithread.SetMultithreadProtected(true);
			_frameEvent = new ManualResetEvent(false);
			_closedEvent = new ManualResetEvent(false);
			_events = new[] { _closedEvent, _frameEvent };

			_captureItem.Closed += OnClosed;
			_framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
				_device,
				DirectXPixelFormat.B8G8R8A8UIntNormalized,
				1,
				_captureItem.Size);
			_framePool.FrameArrived += OnFrameArrived;
			_session = _framePool.CreateCaptureSession(_captureItem);
			_session.StartCapture();
		}

		private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args) {
			_currentFrame = sender.TryGetNextFrame();
			_frameEvent.Set();
		}

		private void OnClosed(GraphicsCaptureItem sender, object args) {
			_closedEvent.Set();
		}

		public SurfaceWithInfo WaitForNewFrame() {
			// Let's get a fresh one.
			_currentFrame?.Dispose();
			_frameEvent.Reset();

			ManualResetEvent signaledEvent = _events[WaitHandle.WaitAny(_events)];
			if(signaledEvent == _closedEvent) {
				Cleanup();
				return null;
			}

			SurfaceWithInfo result = new SurfaceWithInfo();
			result.SystemRelativeTime = _currentFrame.SystemRelativeTime;
			using(MultithreadLock multithreadLock = new MultithreadLock(_multithread))
			using(Texture2D sourceTexture = Direct3D11Helpers.CreateSharpDXTexture2D(_currentFrame.Surface)) {

				// This is to save current texture for saving it to screenshot
				// copy the DirectX resource into the CPU-readable texture2D
				_sharpDxD3dDevice.ImmediateContext.CopyResource(sourceTexture, _cpuTexture);

				_sharpDxD3dDevice.ImmediateContext.ClearRenderTargetView(_composeRenderTargetView, new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 1));

				int width = MathClamp(_currentFrame.ContentSize.Width, 0, _currentFrame.Surface.Description.Width);
				int height = MathClamp(_currentFrame.ContentSize.Height, 0, _currentFrame.Surface.Description.Height);
				ResourceRegion region = new SharpDX.Direct3D11.ResourceRegion(0, 0, 0, width, height, 1);
				_sharpDxD3dDevice.ImmediateContext.CopySubresourceRegion(sourceTexture, 0, region, _composeTexture, 0);

				Texture2DDescription description = sourceTexture.Description;
				description.Usage = SharpDX.Direct3D11.ResourceUsage.Default;
				description.BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource | SharpDX.Direct3D11.BindFlags.RenderTarget;
				description.CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None;
				description.OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None;

				using(Texture2D copyTexture = new SharpDX.Direct3D11.Texture2D(_sharpDxD3dDevice, description)) {

					_sharpDxD3dDevice.ImmediateContext.CopyResource(_composeTexture, copyTexture);
					result.Surface = Direct3D11Helpers.CreateDirect3DSurfaceFromSharpDXTexture(copyTexture);
				}
			}

			return result;
		}

		private Texture2D CreateTexture2D(int width, int height) {
			// create add texture2D 2D accessible by the CPU
			Texture2DDescription desc = new Texture2DDescription() {
				Width = width,
				Height = height,
				CpuAccessFlags = CpuAccessFlags.Read,
				Usage = ResourceUsage.Staging,
				Format = Format.B8G8R8A8_UNorm,
				ArraySize = 1,
				MipLevels = 1,
				SampleDescription = new SampleDescription(1, 0),
			};
			return new Texture2D(_sharpDxD3dDevice, desc);
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

		/// <summary>
		/// Returns value clamped to the inclusive range of min and max.
		/// </summary>
		/// <param name="value"></param>
		/// <param name="min"></param>
		/// <param name="max"></param>
		/// <returns> min if value less than min. max if value more than max. Else, value </returns>
		private int MathClamp(int value, int min, int max) {
			return (value < min) ? min : ((value > max) ? max : value);
		}

		private void StopCapture() {
			_closedEvent.Set();
		}

		private void Cleanup() {
			_framePool?.Dispose();
			_session?.Dispose();
			if(_captureItem != null) {
				_captureItem.Closed -= OnClosed;
			}
			_captureItem = null;
			_device = null;
			_sharpDxD3dDevice = null;
			_composeTexture?.Dispose();
			_composeTexture = null;
			_composeRenderTargetView?.Dispose();
			_composeRenderTargetView = null;
			_currentFrame?.Dispose();
			_isRecording = false;
		}

		private async void StartCaptureButton_ClickAsync(object sender, RoutedEventArgs e) {
			if (!_isClosed) {
				// TODO: Implement multiple recordings on different windows
				MessageBox.Show("Recording already on. Need to stop one before starting one");
				return;
			}
			await SetupEncoding();
		}

		private void StopCaptureButton_Click(object sender, RoutedEventArgs e) {
			if(_isClosed) {
				// TODO: Implement multiple recordings on different windows
				MessageBox.Show("Recording already off. Need to start one before stopping");
				return;
			}
			_isClosed = true;
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
