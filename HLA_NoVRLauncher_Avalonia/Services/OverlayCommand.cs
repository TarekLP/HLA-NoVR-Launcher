using System.Text.Json;
using System.Text.Json.Serialization;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	// ---------------------------------------------------------------------------
	// Command types
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Every command the launcher can send to the Godot overlay.
	/// Keep this as the single source of truth — GDScript reads these strings.
	/// </summary>
	public static class OverlayCommands
	{
		/// <summary>Make the overlay window visible.</summary>
		public const string Show = "show";

		/// <summary>Make the overlay window invisible.</summary>
		public const string Hide = "hide";

		/// <summary>
		/// Move and resize the overlay to match the game window.
		/// Requires data: { x, y, w, h } in physical pixels.
		/// </summary>
		public const string SetBounds = "set_bounds";

		/// <summary>
		/// Tell the overlay which state the game is in.
		/// Requires data: { state } — one of the OverlayState enum names.
		/// </summary>
		public const string SetState = "set_state";

		/// <summary>
		/// Fire an achievement notification in the overlay.
		/// Requires data: { id } — the achievement string from the game.
		/// </summary>
		public const string ShowAchievement = "show_achievement";

		/// <summary>Gracefully tells Godot to shut itself down.</summary>
		public const string Shutdown = "shutdown";
	}

	// ---------------------------------------------------------------------------
	// Message envelope
	// ---------------------------------------------------------------------------

	/// <summary>
	/// The JSON envelope that travels down the named pipe.
	///
	/// Format on the wire (one line per message, newline-terminated):
	///   {"cmd":"set_bounds","data":{"x":0,"y":0,"w":1920,"h":1080}}
	///
	/// Godot parses this with JSON.parse() and dispatches on the "cmd" field.
	/// </summary>
	public sealed class OverlayCommand
	{
		[JsonPropertyName("cmd")]
		public string Cmd { get; init; } = string.Empty;

		/// <summary>
		/// Optional payload. Null for commands that carry no extra data (show/hide/shutdown).
		/// </summary>
		[JsonPropertyName("data")]
		public object? Data { get; init; }

		// -----------------------------------------------------------------------
		// Static factory helpers — one per command type for call-site clarity
		// -----------------------------------------------------------------------

		public static OverlayCommand Show()    => new() { Cmd = OverlayCommands.Show };
		public static OverlayCommand Hide()    => new() { Cmd = OverlayCommands.Hide };
		public static OverlayCommand Shutdown() => new() { Cmd = OverlayCommands.Shutdown };

		public static OverlayCommand SetBounds(int x, int y, int w, int h) => new()
		{
			Cmd  = OverlayCommands.SetBounds,
			Data = new { x, y, w, h }
		};

		public static OverlayCommand SetState(OverlayState state) => new()
		{
			Cmd  = OverlayCommands.SetState,
			Data = new { state = state.ToString() }
		};

		public static OverlayCommand ShowAchievement(string id) => new()
		{
			Cmd  = OverlayCommands.ShowAchievement,
			Data = new { id }
		};

		// -----------------------------------------------------------------------
		// Serialisation
		// -----------------------------------------------------------------------

		private static readonly JsonSerializerOptions _jsonOpts = new()
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};

		/// <summary>Serialises to a single JSON line (no trailing newline).</summary>
		public string ToJson() => JsonSerializer.Serialize(this, _jsonOpts);
	}

	// ---------------------------------------------------------------------------
	// Overlay state
	// ---------------------------------------------------------------------------

	/// <summary>
	/// The five states the overlay can be in. Driven by console.log events.
	/// </summary>
	public enum OverlayState
	{
		/// <summary>Overlay not visible — game running normally.</summary>
		Hidden,

		/// <summary>Game just launched — showing the main menu.</summary>
		MainMenu,

		/// <summary>A chapter or save is loading — overlay hidden.</summary>
		Loading,

		/// <summary>Player is in-game — overlay hidden.</summary>
		InGame,

		/// <summary>Player pressed ESC — showing the pause menu.</summary>
		Paused
	}
}
