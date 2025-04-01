using System.Windows;
using System.Diagnostics;
using Timer = System.Timers.Timer;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;


namespace Client;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public static class NativeMethods
{
	[DllImport("user32.dll")]
	public static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);
}

[ComImport, Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IGraphicsCaptureItemInterop
{
	IntPtr CreateForWindow([In] IntPtr window);
	IntPtr CreateForMonitor([In] IntPtr monitor);
}

public partial class MainWindow : Window
{
	private readonly Timer _processTimer;
    private string _targetProcessName;

	private GraphicsCaptureSession _captureSession;
	private Direct3D11CaptureFramePool _framePool;
	private SharpDX.Direct3D11.Device _d3dDevice;
	private bool _isCapturing;

	public MainWindow()
    {
        InitializeComponent();
        LoadProcesses();

		_processTimer = new Timer(1000);
		_processTimer.Elapsed += CheckProcessStatus; // When the timer elapses, it will call the CheckProcessStatus method
		_processTimer.Start();

		this.Closed += (sender, e) => _processTimer.Stop(); // Stop the timer when the window is closed
	}

	private void CheckProcessStatus(object? sender, System.Timers.ElapsedEventArgs e)
	{
		if (string.IsNullOrEmpty(_targetProcessName)) return;
		
		bool isRunning = Process.GetProcessesByName(_targetProcessName.Replace(".exe", ""))
			.Any(p => !string.IsNullOrEmpty(p.MainWindowTitle));

		Dispatcher.Invoke(() =>
			{
				if (isRunning && !_isCapturing)
				{
					StartCapture();
				}
				else if (!isRunning && _isCapturing)
				{
					StopCapture();
				}
				StatusLabel.Content = _isCapturing ? "Status: Capturing" : "Status: Idle";
			});
	}
	private void StartCapture()
	{
		if (_isCapturing || string.IsNullOrEmpty(_targetProcessName)) return;

		// Locate the process by the process name and get the main window handle
		var process = Process.GetProcessesByName(_targetProcessName.Replace(".exe", ""))
			.FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle));
		if (process == null) return;
		IntPtr hWnd = process.MainWindowHandle;
		if (hWnd == IntPtr.Zero) return;

		try
		{
			// SharpDX: Use DXGI to enumerate the graphics hardware
			// Factory1 is SharpDx's wrapper for IDXGIFactor1, listing GPU adapters (e.g NVIDIA/AMD cards)
			// Adapters1 is a collection of adapters, we're interested in the first one
			using (var dxgiFactory = new Factory1())
			{
				var adapter = dxgiFactory.Adapters1[0];

				// SharpDX: Create a Direct3D11 device, the core object for GPU interaction.
				// This wraps ID3D11Device, tying it to the adapter. BgraSupport flag ensures compatibility with BGRA pixel format
				// Store in _d3dDevice for frame pool setup and later cleanup
				_d3dDevice = new SharpDX.Direct3D11.Device(adapter, DeviceCreationFlags.BgraSupport);
			}

			// Interop: Convert SharpDX's Device to WinRT's IDirect3DDevice
			// Windows.Graphics.Capture requires a WinRT device for frame pool creation, not a SharpDX device
			// CreateDirect3DDevice extracts the COM pointer (ID3D11Device) and re-wraps it in a WinRT object
			var winRtDevice = CreateDirect3DDevice(_d3dDevice);


			// Interop: Instantiate the COM interface for creating a GraphicsCaptureItem
			// IGrpahicsCaptureItemInterop is a WinRT COM object
			// This bridges the Windows API to our app, defining what we are capturing
			var interop = (IGraphicsCaptureItemInterop)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB1C503-51F5-4FB7-BC78-2B80C27B6B7F")));

			// Interop: Create the capture item from the window handle.
			// CreateForWindow returns a COM pointer to a GrpahicsCaptureItem, representing the window graphics output
			// Marshal.GetObjectForIUnkown converts this unmanaged pointer to a managed WinRT object
			var itemPtr = interop.CreateForWindow(hWnd);
			var item = Marshal.GetObjectForIUnknown(itemPtr) as GraphicsCaptureItem;
			if (item == null) throw new Exception("Failed to create capture item");

			// Set up the frame pool for receiving captured frames
			_framePool = Direct3D11CaptureFramePool.Create(
				winRtDevice,
				DirectXPixelFormat.B8G8R8A8UIntNormalized,
				2,
				new Windows.Graphics.SizeInt32 { Width = 1920, Height = 1080 });

			_framePool.FrameArrived += FramePool_FrameArrived;

			// Links the frame pool to the capture item, allowing it to receive frames
			_captureSession = _framePool.CreateCaptureSession(item);
			_captureSession.StartCapture();

			_isCapturing = true;
			StatusLabel.Content = "Status: Capturing";
		}
		catch (Exception ex)
		{
			StatusLabel.Content = $"Status: Capture Failed - {ex.Message}";
		}
	}

	private void FramePool_FrameArrived(Direct3D11CaptureFramePool sender, object args)
	{
		using (var frame = sender.TryGetNextFrame())
		{
			if (frame != null)
			{
				// Log frame details for debugging
				Dispatcher.Invoke(() =>
				{
					StatusLabel.Content = $"Frame Captured: {frame.SystemRelativeTime}";
				});

			}
			// Process the frame here
		}
	}

	private void StopCapture()
	{
		if (!_isCapturing) return;

		_captureSession?.Dispose();
		_framePool?.Dispose();
		_d3dDevice?.Dispose();

		_isCapturing = false;
		StatusLabel.Content = "Status: Idle";
	}

	private void LoadProcesses()
	{
        var processes = Process.GetProcesses()
            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle)) // Filter out processes without a window by checking the WindowTitle, if not title, it's a background process
			.OrderBy(p => p.ProcessName)
            .Select(p => p.ProcessName + ".exe") // Converting the process name to the executable name ".exe"
			.Distinct(); // Avoid duplicates

        ProcessComboBox.ItemsSource = processes;
        ProcessComboBox.SelectedIndex = 0; // Select the first item
	}
	private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		_targetProcessName = ProcessComboBox.SelectedItem?.ToString();
	}
	private IDirect3DDevice CreateDirect3DDevice(SharpDX.Direct3D11.Device sharpDxDevice)
	{
		var d3dDevicePtr = sharpDxDevice.NativePointer;

		var d3dDevice = (IDirect3DDevice)Activator.CreateInstance(
			Type.GetTypeFromCLSID(new Guid("A066E67E-5B34-4D88-BD71-5DAE2E516D85")),
			null,
			d3dDevicePtr);

		return d3dDevice;
	}
}