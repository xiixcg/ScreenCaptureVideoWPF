using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using Device = SharpDX.Direct3D11.Device;

namespace ScreenCaptureVideoWPF {
    internal class Encoding {
        private IDirect3DDevice _device;
        private Device _sharpDxD3dDevice;
        private GraphicsCaptureItem _captureItem;
        private string _captureItemDisplayName;
        private Texture2D _composeTexture;
        private RenderTargetView _composeRenderTargetView;
        private Texture2D _cpuTexture;
        private int _width;
        private int _height;
        private uint _evenUWidth;
        private uint _evenUHeight;
        private MediaEncodingProfile _encodingProfile;
        private VideoStreamDescriptor _videoDescriptor;
        private MediaStreamSource _mediaStreamSource;
        private MediaTranscoder _transcoder;

        private StorageFolder _videoFilePath;
        private StorageFolder _imageFilePath;
        private string _videoFileName;
        private string _imageFileName;

        private Multithread _multithread;
        private ManualResetEvent _frameEvent;
        private ManualResetEvent _closedEvent;
        private ManualResetEvent[] _events;
        private Direct3D11CaptureFrame _currentFrame;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;

        // Flags to control video capture status
        private bool _isRecording;
        private bool _isClosed = true;  // Flag for video file open for encoding

        public Encoding(GraphicsCaptureItem item) {
            // Create the D3D device and SharpDX device
            _device = Direct3D11Helpers.CreateD3DDevice();
            _sharpDxD3dDevice = Direct3D11Helpers.CreateSharpDXDevice(_device);
            _captureItem = item;
            _captureItemDisplayName = _captureItem.DisplayName;
            //_captureItemDisplayName = "screenshot";

            // Initialize a blank texture and render target view for copying frames, using the same size as the capture item
            _composeTexture = Direct3D11Helpers.InitializeComposeTexture(_sharpDxD3dDevice, _captureItem.Size);
            _composeRenderTargetView = new SharpDX.Direct3D11.RenderTargetView(_sharpDxD3dDevice, _composeTexture);
            _cpuTexture = CreateTexture2D(_captureItem.Size.Width, _captureItem.Size.Height);

            _width = _captureItem.Size.Width;
            _height = _captureItem.Size.Height;

            // This example encodes video using the item's actual size.
            uint width = (uint)_width;
            uint height = (uint)_height;

            // Make sure the dimensions are are even. Required by some encoders.
            _evenUWidth = (width % 2 == 0) ? width : width + 1;
            _evenUHeight = (height % 2 == 0) ? height : height + 1;

            MediaEncodingProfile temp = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            uint bitrate = temp.Video.Bitrate;
            uint framerate = 30;

            _encodingProfile = new MediaEncodingProfile();
            _encodingProfile.Container.Subtype = "MPEG4";
            _encodingProfile.Video.Subtype = "H264";
            _encodingProfile.Video.Width = _evenUWidth;
            _encodingProfile.Video.Height = _evenUHeight;
            _encodingProfile.Video.Bitrate = bitrate;
            _encodingProfile.Video.FrameRate.Numerator = framerate;
            _encodingProfile.Video.FrameRate.Denominator = 1;
            _encodingProfile.Video.PixelAspectRatio.Numerator = 1;
            _encodingProfile.Video.PixelAspectRatio.Denominator = 1;

            VideoEncodingProperties videoProperties = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, _evenUWidth, _evenUHeight);
            _videoDescriptor = new VideoStreamDescriptor(videoProperties);

            // Create our MediaStreamSource
            _mediaStreamSource = new MediaStreamSource(_videoDescriptor);
            _mediaStreamSource.BufferTime = TimeSpan.FromSeconds(0);

            // Create our transcoder
            _transcoder = new MediaTranscoder();
            _transcoder.HardwareAccelerationEnabled = true;
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

        private void OnMediaStreamSourceSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args) {
            if (_isRecording && !_isClosed) {
                try {
                    using (SurfaceWithInfo frame = WaitForNewFrame()) {
                        if (frame == null) {
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
                catch (Exception e) {
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

        public SurfaceWithInfo WaitForNewFrame() {
            // Let's get a fresh one.
            _currentFrame?.Dispose();
            _frameEvent.Reset();

            ManualResetEvent signaledEvent = _events[WaitHandle.WaitAny(_events)];
            if (signaledEvent == _closedEvent) {
                Cleanup();
                return null;
            }

            SurfaceWithInfo result = new SurfaceWithInfo();
            result.SystemRelativeTime = _currentFrame.SystemRelativeTime;
            using (MultithreadLock multithreadLock = new MultithreadLock(_multithread))
            using (Texture2D sourceTexture = Direct3D11Helpers.CreateSharpDXTexture2D(_currentFrame.Surface)) {

                // This is to save current texture for saving it to screenshot
                // copy the DirectX resource into the CPU-readable texture2D
                _sharpDxD3dDevice.ImmediateContext.CopyResource(sourceTexture, _cpuTexture);

                _sharpDxD3dDevice.ImmediateContext.ClearRenderTargetView(_composeRenderTargetView, new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 1));

                int width = Utils.MathClamp(_currentFrame.ContentSize.Width, 0, _currentFrame.Surface.Description.Width);
                int height = Utils.MathClamp(_currentFrame.ContentSize.Height, 0, _currentFrame.Surface.Description.Height);
                ResourceRegion region = new SharpDX.Direct3D11.ResourceRegion(0, 0, 0, width, height, 1);
                _sharpDxD3dDevice.ImmediateContext.CopySubresourceRegion(sourceTexture, 0, region, _composeTexture, 0);

                Texture2DDescription description = sourceTexture.Description;
                description.Usage = SharpDX.Direct3D11.ResourceUsage.Default;
                description.BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource | SharpDX.Direct3D11.BindFlags.RenderTarget;
                description.CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None;
                description.OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None;

                using (Texture2D copyTexture = new SharpDX.Direct3D11.Texture2D(_sharpDxD3dDevice, description)) {

                    _sharpDxD3dDevice.ImmediateContext.CopyResource(_composeTexture, copyTexture);
                    result.Surface = Direct3D11Helpers.CreateDirect3DSurfaceFromSharpDXTexture(copyTexture);
                }
            }

            return result;
        }

        private void OnMediaStreamSourceStarting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args) {
            using (SurfaceWithInfo frame = WaitForNewFrame()) {
                args.Request.SetActualStartPosition(frame.SystemRelativeTime);
            }
        }

        public async Task<StorageFolder> SetVideoFilePath(string videoFilePath) {
            _videoFilePath = await StorageFolder.GetFolderFromPathAsync(videoFilePath);
            return _videoFilePath;
        }

        public async Task<StorageFolder> SetImageFilePath(string imageFilePath) {
            _imageFilePath = await StorageFolder.GetFolderFromPathAsync(imageFilePath);
            return _imageFilePath;
        }

        public void SetVideoFileName(string videoFilePrefix) {
            _videoFileName = $"{videoFilePrefix}_{_captureItemDisplayName}";
        }

        public void SetImageFileName(string imageFilePrefix) {
            _imageFileName = $"{imageFilePrefix}_{_captureItemDisplayName}";
        }

        public async void startVideoCapture(string videoFilePath, string videoFilePrefix) {
            await SetVideoFilePath(videoFilePath);
            SetVideoFileName(videoFilePrefix);
            StorageFile file = await _videoFilePath.CreateFileAsync($"{_videoFileName}.mp4");

            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite)) {
                _isClosed = false;
                await EncodeAsync(stream);
            }
        }

        private async Task EncodeAsync(IRandomAccessStream stream) {
            while (!_isRecording && !_isClosed) {
                _isRecording = true;

                StartCapture();

                PrepareTranscodeResult transcode = await _transcoder.PrepareMediaStreamSourceTranscodeAsync(_mediaStreamSource, stream, _encodingProfile);

                await transcode.TranscodeAsync();
            }
            // isn't really being used for anything but will keep it for now
            _isRecording = false;
        }

        private void StartCapture() {
            _multithread = _sharpDxD3dDevice.QueryInterface<SharpDX.Direct3D11.Multithread>();
            _multithread.SetMultithreadProtected(true);
            _frameEvent = new ManualResetEvent(false);
            _closedEvent = new ManualResetEvent(false);
            _events = new[] { _closedEvent, _frameEvent };

            _mediaStreamSource.Starting += OnMediaStreamSourceStarting;
            _mediaStreamSource.SampleRequested += OnMediaStreamSourceSampleRequested;

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

        public void StopVideoCapture() {
            _isClosed = true;
        }

        private void StopCapture() {
            _closedEvent.Set();
        }

        private void Cleanup() {
            _framePool?.Dispose();
            _session?.Dispose();
            if (_captureItem != null) {
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

        public bool GetIsClosed() {
            return this._isClosed;
        }

        public void SetIsClosed(bool isClosed) {
            this._isClosed = isClosed;
        }


        /// <summary>
        /// Create screenshot file from current frame texture using the current unix time in milliseconds
        /// </summary>
        /// <param name="currentSaveFrameTime"></param>
        public async Task SaveScreenshotOfCurrentFrame(string imageFilePath, string imageFilePrefix) {
            await SetImageFilePath(imageFilePath);
            SetImageFileName(imageFilePrefix);
            await Task.Run(async () => {
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
                string screenshotFileName = $@"{_imageFilePath.Path}\{_imageFileName}.png";

                using (FileStream file = new FileStream(screenshotFileName, FileMode.Create, FileAccess.Write)) {
                    // create a PNG encoder
                    BitmapEncoder encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());

                    // set the bitmap to it & flush
                    encoder.SetSoftwareBitmap(softwareBitmap);
                    await encoder.FlushAsync();
                    return;
                }
            });
        }
    }
}
