using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Client.Utilities
{
	public class RollingVideoBuffer
	{
		private readonly LinkedList<(Direct3D11CaptureFrame Frame, DateTime Timestamp)> _frames = new();
		private TimeSpan _maxDuration;

		public RollingVideoBuffer(TimeSpan maxDuration)
		{
			_maxDuration = maxDuration;
		}

		public void SetDuration(TimeSpan duration)
		{
			lock (_frames)
			{
				_maxDuration = duration;
				TrimBuffer();
			}
		}
		public void AddFrame(Direct3D11CaptureFrame frame)
		{
			lock (_frames)
			{
				_frames.AddLast((frame, DateTime.UtcNow));
				TrimBuffer();
			}
		}
		private void TrimBuffer()
		{
			var now = DateTime.UtcNow;
			while (_frames.Count > 0 && (now - _frames.First.Value.Timestamp) > _maxDuration)
			{
				_frames.First.Value.Frame.Dispose();
				_frames.RemoveFirst();
			}
		}

		public async Task SaveToFile(StorageFile outputFile)
		{
			if (_frames.Count == 0) return;

			try
			{
				var tempDir = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("ReplayFrames", CreationCollisionOption.ReplaceExisting);
				int frameIndex = 0;

				// Capture all frames to PNG first
				foreach (var (frame, _) in _frames)
				{
					var imageFile = await tempDir.CreateFileAsync($"frame_{frameIndex:D4}.png", CreationCollisionOption.ReplaceExisting);
					using (var stream = await imageFile.OpenAsync(FileAccessMode.ReadWrite))
					{
						var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
						var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
						encoder.SetSoftwareBitmap(bitmap);
						await encoder.FlushAsync();
					}
					frameIndex++;
				}

				string ffmpegPath = @"D:\.CODING\C#\Prep Projects\RecordBy\Client\ffmpeg\ffmpeg.exe";
				var ffmpegCmd = $"-framerate 60 -i \"{tempDir.Path}\\frame_%04d.png\" -c:v libx264 -pix_fmt yuv420p \"{outputFile.Path}\"";

				var process = new Process()
				{
					StartInfo = new ProcessStartInfo()
					{
						FileName = ffmpegPath,
						Arguments = ffmpegCmd,
						UseShellExecute = false,
						CreateNoWindow = true,
						RedirectStandardError = true
					}
				};

				process.Start();
				await process.WaitForExitAsync();
				if (process.ExitCode != 0)
				{
					throw new Exception($"FFmpeg failed: {await process.StandardError.ReadToEndAsync()}");
				}

				await tempDir.DeleteAsync();
			}
			catch (Exception ex)
			{
				throw new Exception($"Error saving video: {ex.Message}", ex);
			}
		}

		public bool IsEmpty => _frames.Count == 0;
	}
}