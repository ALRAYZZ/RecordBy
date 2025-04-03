using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Forms; // For OpenFileDialog
using System.Windows.Input;
using System.Windows.Interop;
using Client.Utilities;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Client
{
	public partial class MainWindow : Window
	{
		private readonly List<string> _trackedExePaths = new();
		private readonly System.Timers.Timer _processCheckTimer;
		private HotkeyManager _hotkeyManager;
		private RollingVideoBuffer _videoBuffer;

		private GraphicsCaptureSession _captureSession;
		private Direct3D11CaptureFramePool _framePool;
		private unsafe ID3D11Device* _d3dDevice;
		private bool _isCapturing;
		private Process _currentProcess;

		public MainWindow()
		{
			InitializeComponent();
			InitializeDevice();
			_videoBuffer = new RollingVideoBuffer(TimeSpan.FromMinutes(BufferSlider.Value));
			BufferSlider.ValueChanged += (s, e) => _videoBuffer.SetDuration(TimeSpan.FromMinutes(BufferSlider.Value));
			_processCheckTimer = new System.Timers.Timer(1000) { AutoReset = true };
			_processCheckTimer.Elapsed += CheckProcessStatus;
			_processCheckTimer.Start();
			RegisterHotkey();
		}

		private unsafe void InitializeDevice()
		{
			try
			{
				var d3d11 = D3D11.GetApi(null);
				ID3D11Device* device = null;
				ID3D11DeviceContext* context = null;
				var hr = d3d11.CreateDevice(
					null, D3DDriverType.Hardware, 0, (uint)CreateDeviceFlag.BgraSupport,
					null, 0, D3D11.SdkVersion, &device, null, &context);

				if (hr < 0)
				{
					hr = d3d11.CreateDevice(
						null, D3DDriverType.Warp, 0, (uint)CreateDeviceFlag.BgraSupport,
						null, 0, D3D11.SdkVersion, &device, null, &context);
				}

				SilkMarshal.ThrowHResult(hr);
				_d3dDevice = device;
				if (context != null) context->Release();
				StatusLabel.Content = "Status: Device Ready";
			}
			catch (Exception ex)
			{
				System.Windows.MessageBox.Show($"Failed to initialize Direct3D11 device: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
				_d3dDevice = null;
				StatusLabel.Content = "Status: Device Initalization Error";
			}
		}

		private void AddExeButton_Click(object sender, RoutedEventArgs e)
		{
			var dialog = new OpenFileDialog { Filter = "Executable Files (*.exe)|*.exe", Multiselect = true };
			if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				foreach (var path in dialog.FileNames)
				{
					if (!_trackedExePaths.Contains(path)) _trackedExePaths.Add(path);
				}
				ExeListBox.ItemsSource = _trackedExePaths.Select(System.IO.Path.GetFileName);
			}
		}

		private void CheckProcessStatus(object? sender, ElapsedEventArgs e)
		{
			if (_trackedExePaths.Count == 0) return;

			Process[] processes = null;
			try
			{
				processes = Process.GetProcesses();
				var target = processes.FirstOrDefault(p =>
					_trackedExePaths.Any(path => System.IO.Path.GetFileNameWithoutExtension(path)
						.Equals(p.ProcessName, StringComparison.OrdinalIgnoreCase)) &&
					p.MainWindowHandle != IntPtr.Zero);

				Dispatcher.Invoke(() =>
				{
					if (target != null && (!_isCapturing || _currentProcess?.Id != target.Id))
					{
						StopCapture();
						StartCapture(target);
						_currentProcess = target;
						StatusLabel.Content = $"Status: Tracking {target.ProcessName}";
					}
					else if (target == null && _isCapturing)
					{
						StopCapture();
						_currentProcess = null;
						StatusLabel.Content = "Status: Idle";
					}
				});
			}
			finally
			{
				if (processes != null) foreach (var p in processes) p?.Dispose();
			}
		}

		private unsafe void StartCapture(Process target)
		{
			if (_isCapturing || _d3dDevice == null) return;

			try
			{
				var hwnd = target.MainWindowHandle;
				var item = CaptureHelper.CreateItemForWindow(hwnd);
				var winRtDevice = Direct3D11Extensions.AsDirect3DDevice(_d3dDevice);

				_framePool = Direct3D11CaptureFramePool.Create(
					winRtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
				_framePool.FrameArrived += FramePool_FrameArrived;
				_captureSession = _framePool.CreateCaptureSession(item);
				_captureSession.StartCapture();

				_isCapturing = true;
			}
			catch (Exception ex)
			{
				StatusLabel.Content = $"Status: Capture Failed - {ex.Message}";
			}
		}

		private void FramePool_FrameArrived(Direct3D11CaptureFramePool sender, object args)
		{
			using var frame = sender.TryGetNextFrame();
			if (frame != null)
			{
				_videoBuffer.AddFrame(frame);
				Dispatcher.Invoke(() => StatusLabel.Content = $"Status: Buffering {_currentProcess?.ProcessName}");
			}
		}

		private void StopCapture()
		{
			if (!_isCapturing) return;
			_captureSession?.Dispose();
			_framePool?.Dispose();
			_isCapturing = false;
		}

		private void RegisterHotkey()
		{
			this.Loaded += (s, e) =>
			{
				try
				{
					_hotkeyManager = new HotkeyManager(Key.F8, ModifierKeys.None);
					_hotkeyManager.Pressed += async (_, _) => await SaveRecording();
					StatusLabel.Content = "Status: Hotkey F8 registered for recording";
				}
				catch (Exception ex)
				{
					System.Windows.MessageBox.Show($"Failed to register hotkey: {ex.Message}", "Hotkey Error", MessageBoxButton.OK, MessageBoxImage.Warning);

					// As a fallback, we add a direct key handler to the window
					this.KeyDown += async (s, e) =>
					{
						if (e.Key == Key.F8)
						{
							await SaveRecording();
							e.Handled = true;
						}
					};
					StatusLabel.Content ="Status: Using fallback key detection (F8)";
				}
			};
			
		}

		private async Task SaveRecording()
		{
			if (!_isCapturing || _videoBuffer.IsEmpty)
			{
				StatusLabel.Content = "Status: Nothing to save";
				return;
			}

			StatusLabel.Content = "Status: Saving...";
			try
			{
				var picker = new FileSavePicker
				{
					SuggestedStartLocation = PickerLocationId.VideosLibrary,
					SuggestedFileName = $"Replay_{DateTime.Now:yyyyMMdd_HHmmss}",
					FileTypeChoices = { { "MP4 Video", new List<string> { ".mp4" } } }
				};
				var hwnd = new WindowInteropHelper(this).Handle;
				WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);


				var file = await picker.PickSaveFileAsync();
				if (file != null)
				{
					await _videoBuffer.SaveToFile(file);
					StatusLabel.Content = $"Status: Saved to {file.Path}";
				}
			}
			catch (Exception ex)
			{
				StatusLabel.Content = $"Status: Save Failed - {ex.Message}";
			}
		}

		protected override unsafe void OnClosed(EventArgs e)
		{
			_processCheckTimer?.Stop();
			_hotkeyManager?.Dispose();
			StopCapture();
			if (_d3dDevice != null) { unsafe { _d3dDevice->Release(); } }
			base.OnClosed(e);
		}
	}
}