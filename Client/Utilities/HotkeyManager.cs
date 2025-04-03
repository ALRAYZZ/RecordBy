using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Client.Utilities
{
	public class HotkeyManager : IDisposable
	{
		private readonly int _hotkeyId;
		private HwndSource _source;
		private const int WM_HOTKEY = 0x0312;

		public event EventHandler? Pressed;

		[DllImport("user32.dll")]
		private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

		[DllImport("user32.dll")]
		private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		public HotkeyManager(Key key, ModifierKeys modififers)
		{
			_hotkeyId = GetHashCode();
			var window = Application.Current.MainWindow;
			_source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
			_source.AddHook(HwndHook);

			RegisterHotKey(_source.Handle, _hotkeyId, (uint)modififers, (uint)KeyInterop.VirtualKeyFromKey(key));
		}

		private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
			{
				Pressed?.Invoke(this, EventArgs.Empty);
				handled = true;
			}
			return IntPtr.Zero;
		}

		public void Dispose()
		{
			UnregisterHotKey(_source.Handle, _hotkeyId);
			_source.RemoveHook(HwndHook);
		}
	}
}
