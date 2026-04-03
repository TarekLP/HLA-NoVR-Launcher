using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HLA_NoVRLauncher_Avalonia.Services;
using LibVLCSharp.Shared;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HLA_NoVRLauncher_Avalonia.Views
{
	public partial class HomeView : UserControl, IDisposable
	{
		private readonly LibVLC _libVLC;
		private MediaPlayer _mediaPlayer;
		private WriteableBitmap? _bitmap;
		private ILockedFramebuffer? _lockedBuffer;
		private const uint VideoWidth = 1280;
		private const uint VideoHeight = 720;
		private string? _videoPath;
		private bool _isMuted;
		private bool _disposed;

		public HomeView()
		{
			InitializeComponent();

			Core.Initialize();
			_libVLC = new LibVLC("--no-video-title-show");
			_mediaPlayer = CreateMediaPlayer();

			AttachedToVisualTree += (_, _) =>
			{
				Dispatcher.UIThread.Post(PlayVideo, DispatcherPriority.Loaded);
			};

			DataContextChanged += (_, _) =>
			{
				if (DataContext is ViewModels.HomeViewModel)
				{
					var settings = new SettingsService().LoadSettings();
					SetMuted(settings.IsMuted);
				}
			};
		}

		private MediaPlayer CreateMediaPlayer()
		{
			var player = new MediaPlayer(_libVLC);
			player.SetVideoCallbacks(Lock, Unlock, Display);
			player.SetVideoFormat("BGRA", VideoWidth, VideoHeight, VideoWidth * 4);
			player.EndReached += OnEndReached;
			player.Playing += OnPlaying;
			return player;
		}

		private void DestroyMediaPlayer()
		{
			_mediaPlayer.EndReached -= OnEndReached;
			_mediaPlayer.Playing -= OnPlaying;
			_mediaPlayer.Stop();
			_mediaPlayer.Dispose();
		}

		private IntPtr Lock(IntPtr opaque, IntPtr planes)
		{
			_bitmap ??= new WriteableBitmap(
				new Avalonia.PixelSize((int)VideoWidth, (int)VideoHeight),
				new Avalonia.Vector(96, 96),
				PixelFormat.Bgra8888,
				AlphaFormat.Opaque
			);

			_lockedBuffer = _bitmap.Lock();
			Marshal.WriteIntPtr(planes, _lockedBuffer.Address);
			return _lockedBuffer.Address;
		}

		private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes)
		{
			_lockedBuffer?.Dispose();
			_lockedBuffer = null;
		}

		private void Display(IntPtr opaque, IntPtr picture)
		{
			Dispatcher.UIThread.Post(() =>
			{
				if (VideoBackground != null && _bitmap != null)
				{
					VideoBackground.Source = null;
					VideoBackground.Source = _bitmap;
				}
			}, DispatcherPriority.Render);
		}

		private void OnPlaying(object? sender, EventArgs e)
		{
			_mediaPlayer.Mute = _isMuted;
		}

		private void OnEndReached(object? sender, EventArgs e)
		{
			if (_disposed) return;

			System.Threading.ThreadPool.QueueUserWorkItem(_ =>
			{
				if (_disposed) return;

				DestroyMediaPlayer();
				_mediaPlayer = CreateMediaPlayer();

				if (_videoPath != null)
				{
					using var media = new Media(_libVLC, _videoPath);
					_mediaPlayer.Play(media);
				}
			});
		}

		private void PlayVideo()
		{
			_videoPath = Path.Combine(
				AppDomain.CurrentDomain.BaseDirectory,
				"Assets",
				"background.mp4"
			);

			if (!File.Exists(_videoPath))
				return;

			var settings = new SettingsService().LoadSettings();
			_isMuted = settings.IsMuted;

			using var media = new Media(_libVLC, _videoPath);
			_mediaPlayer.Play(media);
		}

		public void SetMuted(bool muted)
		{
			_isMuted = muted;
			_mediaPlayer.Mute = muted;
		}

		public void Dispose()
		{
			_disposed = true;
			DestroyMediaPlayer();
			_lockedBuffer?.Dispose();
			_libVLC.Dispose();
			_bitmap?.Dispose();
		}
	}
}