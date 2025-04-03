using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System.Runtime.InteropServices;

namespace Client.Utilities
{
	public static class Direct3D11Extensions
	{
		public static unsafe Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice AsDirect3DDevice(this ComPtr<ID3D11Device> d3dDevice)
		{
			using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
			var adapter = default(ComPtr<IDXGIAdapter>);

			IDXGIAdapter* adapterPtr;
			Marshal.ThrowExceptionForHR(dxgiDevice.Handle->GetAdapter(&adapterPtr));
			adapter = new ComPtr<IDXGIAdapter>(adapterPtr);

			return CreateDirect3DDeviceFromDXGIDevice(dxgiDevice);
		}

		[DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", ExactSpelling = true)]
		private static extern unsafe int CreateDirect3D11DeviceFromDXGIDevice_(
			IntPtr dxgiDevice, out IntPtr graphicsDevice);

		private static unsafe Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice CreateDirect3DDeviceFromDXGIDevice(ComPtr<IDXGIDevice> dxgiDevice)
		{
			IntPtr nativeDxgiDevice = (IntPtr)dxgiDevice.Handle;
			CreateDirect3D11DeviceFromDXGIDevice_(nativeDxgiDevice, out var deviceHandle);
			return Marshal.GetObjectForIUnknown(deviceHandle) as Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice;
		}
	}
}