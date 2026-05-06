## Main.gd
## Attach to the root Node of your Main.tscn scene.
##
## Responsibilities:
##   - Apply initial window position/size from CLI args
##   - Set transparency and click-through flags at runtime
##   - Connect global signals (shutdown, bounds, show/hide)
##
## The C# launcher is already managing the window via Win32 (SetWindowBounds,
## ShowWindow, HideWindow), so Godot's own show/hide here is a failsafe only.

extends Node


# ---------------------------------------------------------------------------
# Lifecycle
# ---------------------------------------------------------------------------

func _ready() -> void:
	_setup_window()
	_apply_cli_position()
	_connect_signals()


# ---------------------------------------------------------------------------
# Window setup
# ---------------------------------------------------------------------------

func _setup_window() -> void:
	# Make the window background fully transparent so only UI nodes are visible.
	get_viewport().transparent_bg = true

	# Prevent Godot from stealing focus from the game when the window appears.
	DisplayServer.window_set_flag(DisplayServer.WINDOW_FLAG_NO_FOCUS, true)


func _apply_cli_position() -> void:
	var args := OS.get_cmdline_args()

	var x := _get_int_arg(args, "--overlay-x", -1)
	var y := _get_int_arg(args, "--overlay-y", -1)
	var w := _get_int_arg(args, "--overlay-w", -1)
	var h := _get_int_arg(args, "--overlay-h", -1)

	# Apply size before position — some platforms require this order.
	if w > 0 and h > 0:
		DisplayServer.window_set_size(Vector2i(w, h))

	if x >= 0 and y >= 0:
		DisplayServer.window_set_position(Vector2i(x, y))

	print("[Main] Initial window: pos=(%d,%d) size=(%dx%d)" % [x, y, w, h])


# ---------------------------------------------------------------------------
# Signal connections
# ---------------------------------------------------------------------------

func _connect_signals() -> void:
	CommandBus.bounds_changed.connect(_on_bounds_changed)
	CommandBus.show_requested.connect(_on_show)
	CommandBus.hide_requested.connect(_on_hide)
	CommandBus.shutdown_requested.connect(_on_shutdown)


# ---------------------------------------------------------------------------
# Handlers
# ---------------------------------------------------------------------------

## C# already moves us via Win32, but we sync Godot's internal size too
## so CanvasLayers and anchors recalculate correctly.
func _on_bounds_changed(x: int, y: int, w: int, h: int) -> void:
	if w > 0 and h > 0:
		DisplayServer.window_set_size(Vector2i(w, h))
	DisplayServer.window_set_position(Vector2i(x, y))


## C# calls Win32 ShowWindow before sending this, but we mirror it in
## Godot as a belt-and-suspenders measure.
func _on_show() -> void:
	DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)


func _on_hide() -> void:
	# Note: C# is the authority on visibility via Win32.
	# This just keeps Godot's state consistent.
	pass


func _on_shutdown() -> void:
	print("[Main] Shutdown requested by launcher.")
	get_tree().quit()


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

func _get_int_arg(args: Array, key: String, default_val: int) -> int:
	var idx := args.find(key)
	if idx != -1 and idx + 1 < args.size():
		return int(args[idx + 1])
	return default_val
