//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace SreenCaptureVideoWPF {
//	internal class HelperClasses {
//	}
//}


using System.Runtime.InteropServices;
using System;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics;
//using static MonitorEnumerationHelper;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using Windows.System;

public static class CoreMessagingHelper {
	enum DISPATCHERQUEUE_THREAD_APARTMENTTYPE {
		DQTAT_COM_NONE = 0,
		DQTAT_COM_ASTA = 1,
		DQTAT_COM_STA = 2
	}

	enum DISPATCHERQUEUE_THREAD_TYPE {
		DQTYPE_THREAD_DEDICATED = 1,
		DQTYPE_THREAD_CURRENT = 2
	}

	struct DispatcherQueueOptions {
		public int dwSize;
		public DISPATCHERQUEUE_THREAD_TYPE threadType;
		public DISPATCHERQUEUE_THREAD_APARTMENTTYPE apartmentType;
	}

	[DllImport(
		"CoreMessaging.dll",
		EntryPoint = "CreateDispatcherQueueController",
		SetLastError = true,
		CharSet = CharSet.Unicode,
		ExactSpelling = true,
		CallingConvention = CallingConvention.StdCall
		)]
	static extern UInt32 CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr dispatcherQueueController);

	public static DispatcherQueueController CreateDispatcherQueueControllerForCurrentThread() {
		var options = new DispatcherQueueOptions {
			dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
			threadType = DISPATCHERQUEUE_THREAD_TYPE.DQTYPE_THREAD_CURRENT,
			apartmentType = DISPATCHERQUEUE_THREAD_APARTMENTTYPE.DQTAT_COM_NONE
		};

		DispatcherQueueController controller = null;
		uint hr = CreateDispatcherQueueController(options, out IntPtr controllerPointer);
		if(hr == 0) {
			controller = Marshal.GetObjectForIUnknown(controllerPointer) as DispatcherQueueController;
			Marshal.Release(controllerPointer);
		}

		return controller;
	}
}

public static class CaptureHelper {
	static readonly Guid GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

	[ComImport]
	[Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComVisible(true)]
	interface IInitializeWithWindow {
		void Initialize(
			IntPtr hwnd);
	}

	[ComImport]
	[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	[ComVisible(true)]
	interface IGraphicsCaptureItemInterop {
		IntPtr CreateForWindow(
			[In] IntPtr window,
			[In] ref Guid iid);

		IntPtr CreateForMonitor(
			[In] IntPtr monitor,
			[In] ref Guid iid);
	}

	public static void SetWindow(this GraphicsCapturePicker picker, IntPtr hwnd) {
		var interop = (IInitializeWithWindow) (object) picker;
		interop.Initialize(hwnd);
	}

	public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd) {
		var factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
		var interop = (IGraphicsCaptureItemInterop) factory;
		var temp = typeof(GraphicsCaptureItem);
		var itemPointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
		var item = Marshal.GetObjectForIUnknown(itemPointer) as GraphicsCaptureItem;
		Marshal.Release(itemPointer);

		return item;
	}

	public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon) {
		var factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
		var interop = (IGraphicsCaptureItemInterop) factory;
		var temp = typeof(GraphicsCaptureItem);
		var itemPointer = interop.CreateForMonitor(hmon, GraphicsCaptureItemGuid);
		var item = Marshal.GetObjectForIUnknown(itemPointer) as GraphicsCaptureItem;
		Marshal.Release(itemPointer);

		return item;
	}
}


[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
interface IDirect3DDxgiInterfaceAccess {
	IntPtr GetInterface([In] ref Guid iid);
};

public static class Direct3D11Helpers {
	internal static Guid IInspectable = new Guid("AF86E2E0-B12D-4c6a-9C5A-D7AA65101E90");
	internal static Guid ID3D11Resource = new Guid("dc8e63f3-d12b-4952-b47b-5e45026a862d");
	internal static Guid IDXGIAdapter3 = new Guid("645967A4-1392-4310-A798-8053CE3E93FD");
	internal static Guid ID3D11Device = new Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140");
	internal static Guid ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

	[DllImport(
		"d3d11.dll",
		EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
		SetLastError = true,
		CharSet = CharSet.Unicode,
		ExactSpelling = true,
		CallingConvention = CallingConvention.StdCall
		)]
	internal static extern UInt32 CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

	[DllImport(
		"d3d11.dll",
		EntryPoint = "CreateDirect3D11SurfaceFromDXGISurface",
		SetLastError = true,
		CharSet = CharSet.Unicode,
		ExactSpelling = true,
		CallingConvention = CallingConvention.StdCall
		)]
	internal static extern UInt32 CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);

	public static IDirect3DDevice CreateD3DDevice() {
		return CreateD3DDevice(false);
	}

	public static IDirect3DDevice CreateD3DDevice(bool useWARP) {
		var d3dDevice = new SharpDX.Direct3D11.Device(
			useWARP ? SharpDX.Direct3D.DriverType.Software : SharpDX.Direct3D.DriverType.Hardware,
			SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport);
		IDirect3DDevice device = null;

		// Acquire the DXGI interface for the Direct3D device.
		using(var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device3>()) {
			// Wrap the native device using a WinRT interop object.
			uint hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pUnknown);

			if(hr == 0) {
				device = Marshal.GetObjectForIUnknown(pUnknown) as IDirect3DDevice;
				Marshal.Release(pUnknown);
			}
		}

		return device;
	}


	internal static IDirect3DSurface CreateDirect3DSurfaceFromSharpDXTexture(SharpDX.Direct3D11.Texture2D texture) {
		IDirect3DSurface surface = null;

		// Acquire the DXGI interface for the Direct3D surface.
		using(var dxgiSurface = texture.QueryInterface<SharpDX.DXGI.Surface>()) {
			// Wrap the native device using a WinRT interop object.
			uint hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out IntPtr pUnknown);

			if(hr == 0) {
				surface = Marshal.GetObjectForIUnknown(pUnknown) as IDirect3DSurface;
				Marshal.Release(pUnknown);
			}
		}

		return surface;
	}



	internal static SharpDX.Direct3D11.Device CreateSharpDXDevice(IDirect3DDevice device) {
		var access = (IDirect3DDxgiInterfaceAccess) device;
		var d3dPointer = access.GetInterface(ID3D11Device);
		var d3dDevice = new SharpDX.Direct3D11.Device(d3dPointer);
		return d3dDevice;
	}

	internal static SharpDX.Direct3D11.Texture2D CreateSharpDXTexture2D(IDirect3DSurface surface) {
		var access = (IDirect3DDxgiInterfaceAccess) surface;
		var d3dPointer = access.GetInterface(ID3D11Texture2D);
		var d3dSurface = new SharpDX.Direct3D11.Texture2D(d3dPointer);
		return d3dSurface;
	}


	public static SharpDX.Direct3D11.Texture2D InitializeComposeTexture(
		SharpDX.Direct3D11.Device sharpDxD3dDevice,
		SizeInt32 size) {
		var description = new SharpDX.Direct3D11.Texture2DDescription {
			Width = size.Width,
			Height = size.Height,
			MipLevels = 1,
			ArraySize = 1,
			Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
			SampleDescription = new SharpDX.DXGI.SampleDescription() {
				Count = 1,
				Quality = 0
			},
			Usage = SharpDX.Direct3D11.ResourceUsage.Default,
			BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource | SharpDX.Direct3D11.BindFlags.RenderTarget,
			CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None,
			OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None
		};
		var composeTexture = new SharpDX.Direct3D11.Texture2D(sharpDxD3dDevice, description);


		using(var renderTargetView = new SharpDX.Direct3D11.RenderTargetView(sharpDxD3dDevice, composeTexture)) {
			sharpDxD3dDevice.ImmediateContext.ClearRenderTargetView(renderTargetView, new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 1));
		}

		return composeTexture;
	}
}

class MultithreadLock : IDisposable {
	public MultithreadLock(SharpDX.Direct3D11.Multithread multithread) {
		_multithread = multithread;
		_multithread?.Enter();
	}

	public void Dispose() {
		_multithread?.Leave();
		_multithread = null;
	}

	private SharpDX.Direct3D11.Multithread _multithread;
}

public sealed class SurfaceWithInfo : IDisposable {
	public IDirect3DSurface Surface { get; internal set; }
	public TimeSpan SystemRelativeTime { get; internal set; }

	public void Dispose() {
		Surface?.Dispose();
		Surface = null;
	}
}

public class MonitorInfo {
	public bool IsPrimary { get; set; }
	public Vector2 ScreenSize { get; set; }
	public Rect MonitorArea { get; set; }
	public Rect WorkArea { get; set; }
	public string DeviceName { get; set; }
	public IntPtr Hmon { get; set; }
}

public static class MonitorEnumerationHelper {
	delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

	[StructLayout(LayoutKind.Sequential)]
	public struct RECT {
		public int left;
		public int top;
		public int right;
		public int bottom;
	}

	private const int CCHDEVICENAME = 32;
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	internal struct MonitorInfoEx {
		public int Size;
		public RECT Monitor;
		public RECT WorkArea;
		public uint Flags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
		public string DeviceName;
	}

	[DllImport("user32.dll")]
	static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

	public static IEnumerable<MonitorInfo> GetMonitors() {
		var result = new List<MonitorInfo>();

		EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
			delegate (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) {
				MonitorInfoEx mi = new MonitorInfoEx();
				mi.Size = Marshal.SizeOf(mi);
				bool success = GetMonitorInfo(hMonitor, ref mi);
				if(success) {
					var info = new MonitorInfo {
						ScreenSize = new Vector2(mi.Monitor.right - mi.Monitor.left, mi.Monitor.bottom - mi.Monitor.top),
						MonitorArea = new Rect(mi.Monitor.left, mi.Monitor.top, mi.Monitor.right - mi.Monitor.left, mi.Monitor.bottom - mi.Monitor.top),
						WorkArea = new Rect(mi.WorkArea.left, mi.WorkArea.top, mi.WorkArea.right - mi.WorkArea.left, mi.WorkArea.bottom - mi.WorkArea.top),
						IsPrimary = mi.Flags > 0,
						Hmon = hMonitor,
						DeviceName = mi.DeviceName
					};
					result.Add(info);
				}
				return true;
			}, IntPtr.Zero);
		return result;
	}
}

// Seems like this com object require special treatment to be used in UWP project

//public static class GraphicsCaptureHelper {
//	static readonly Guid GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

//	[ComImport]
//	[System.Runtime.InteropServices.Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
//	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
//	[ComVisible(true)]
//	interface IInitializeWithWindow {
//		void Initialize(
//			IntPtr hwnd);
//	}

//	[ComImport]
//	[System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
//	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
//	[ComVisible(true)]
//	interface IGraphicsCaptureItemInterop {
//		IntPtr CreateForWindow(
//			[In] IntPtr window,
//			[In] ref Guid iid);

//		IntPtr CreateForMonitor(
//			[In] IntPtr monitor,
//			[In] ref Guid iid);
//	}

//	public static void SetWindow(this GraphicsCapturePicker picker, IntPtr hwnd) {
//		var interop = (IInitializeWithWindow) (object) picker;
//		interop.Initialize(hwnd);
//	}

//	public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd) {
//		var factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
//		var interop = (IGraphicsCaptureItemInterop) factory;
//		var temp = typeof(GraphicsCaptureItem);
//		var itemPointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
//		var item = Marshal.GetObjectForIUnknown(itemPointer) as GraphicsCaptureItem;
//		Marshal.Release(itemPointer);

//		return item;
//	}

//	public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon) {
//		var factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
//		var interop = (IGraphicsCaptureItemInterop) factory;
//		var temp = typeof(GraphicsCaptureItem);
//		var itemPointer = interop.CreateForMonitor(hmon, GraphicsCaptureItemGuid);
//		var item = Marshal.GetObjectForIUnknown(itemPointer) as GraphicsCaptureItem;
//		Marshal.Release(itemPointer);

//		return item;
//	}
//}