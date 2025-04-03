using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace Client.Utilities
{
    public static class CaptureHelper
    {
        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
		{
			IntPtr CreateForWindow(IntPtr hwnd, in Guid iid);
		}

        public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
		{
			var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
			var itemPtr = interop.CreateForWindow(hwnd, typeof(GraphicsCaptureItem).GUID);
			return Marshal.GetObjectForIUnknown(itemPtr) as GraphicsCaptureItem;
		}
	}
}
