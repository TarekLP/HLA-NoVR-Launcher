using Avalonia.Controls;
using HLA_NoVRLauncher_Avalonia.Services;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using System;
using System.IO;

namespace HLA_NoVRLauncher_Avalonia.Views
{
	public partial class HomeView : UserControl, IDisposable
	{
		private readonly LibVLC _libVLC;
		private readonly MediaPlayer _mediaPlayer;

		public HomeView()
		{
			InitializeComponent();

			Core.Initialize();
			_libVLC = new LibVLC();
			_mediaPlayer = new MediaPlayer(_libVLC)
			{
				EnableHardwareDecoding = true
			};

			VideoBackground.MediaPlayer = _mediaPlayer;

			PlayVideo();
		}

		private void PlayVideo()
		{
			string videoPath = Path.Combine(
				AppDomain.CurrentDomain.BaseDirectory,
				"Assets",
				"background.ogv"
			);

			if (!File.Exists(videoPath))
				return;

			using var media = new Media(_libVLC, videoPath);
			media.AddOption(":loop"); // loop the video
			var settings = new SettingsService().LoadSettings();
			_mediaPlayer.Mute = settings.IsMuted;
			_mediaPlayer.Play(media);
		}

		public void Dispose()
		{
			_mediaPlayer.Stop();
			_mediaPlayer.Dispose();
			_libVLC.Dispose();
		}
	}
}