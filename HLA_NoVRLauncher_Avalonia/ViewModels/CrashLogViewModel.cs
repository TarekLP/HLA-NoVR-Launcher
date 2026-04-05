using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HLA_NoVRLauncher_Avalonia.Models;
using HLA_NoVRLauncher_Avalonia.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
	public partial class CrashLogViewModel : ViewModelBase
	{
		private readonly SettingsService _settingsService = new();
		private readonly GameService _gameService = new();

		[ObservableProperty]
		private bool _filterEnabled = false;

		[ObservableProperty]
		private string _logStatus = "No log loaded.";

		[ObservableProperty]
		private bool _isLoaded = false;

		public ObservableCollection<LogLine> LogLines { get; } = new();

		private string GetLogPath()
		{
			var settings = _settingsService.LoadSettings();
			string steamPath = _gameService.GetSteamInstallPath() ?? "";
			string gamePath = string.IsNullOrEmpty(settings.GamePath)
				? _gameService.GetDefaultGamePath(steamPath)
				: settings.GamePath;

			return Path.Combine(gamePath, "game", "hlvr", "console.log");
		}

		public CrashLogViewModel()
		{
			LoadLog();
		}

		[RelayCommand]
		private void LoadLog()
		{
			LogLines.Clear();
			string logPath = GetLogPath();

			if (!File.Exists(logPath))
			{
				LogStatus = $"Log file not found at: {logPath}";
				IsLoaded = false;
				return;
			}

			var lines = File.ReadAllLines(logPath);
			foreach (var line in lines)
			{
				var logLine = new LogLine(line);
				LogLines.Add(logLine);
			}

			LogStatus = $"Loaded {lines.Length} lines from console.log";
			IsLoaded = true;
			ApplyFilter();
		}

		partial void OnFilterEnabledChanged(bool value)
		{
			ApplyFilter();
		}

		private void ApplyFilter()
		{
			foreach (var line in LogLines)
			{
				line.IsVisible = !FilterEnabled || line.IsErrorOrWarning;
			}
		}

		[RelayCommand]
		private void ClearLog()
		{
			string logPath = GetLogPath();
			if (!File.Exists(logPath)) return;
			File.WriteAllText(logPath, string.Empty);
			LoadLog();
		}
	}

	public partial class LogLine : ObservableObject
	{
		public string Text { get; }
		public LogLineType Type { get; }
		public bool IsErrorOrWarning =>
			Type == LogLineType.Error || Type == LogLineType.Warning;

		[ObservableProperty]
		private bool _isVisible = true;

		public LogLine(string text)
		{
			Text = text;

			string lower = text.ToLowerInvariant();
			if (lower.Contains("error") || lower.Contains("exception") ||
				lower.Contains("fatal") || lower.Contains("crash"))
				Type = LogLineType.Error;
			else if (lower.Contains("warning") || lower.Contains("warn"))
				Type = LogLineType.Warning;
			else
				Type = LogLineType.Normal;
		}
		public string TextColor => Type switch
		{
			LogLineType.Error => "#E53935",
			LogLineType.Warning => "#FB7E14",
			_ => "#FFFFFF"
		};
	}

	public enum LogLineType
	{
		Normal,
		Warning,
		Error
	}
}