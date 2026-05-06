## MainMenuPanel.gd
## Attach to UI/MainMenuPanel in main.tscn.

extends Control


# ---------------------------------------------------------------------------
# Node references
# ---------------------------------------------------------------------------

@onready var _btn_new_game  : Button = $MenuContainer/NewGame
@onready var _btn_load_game : Button = $MenuContainer/LoadGame
@onready var _btn_settings  : Button = $MenuContainer/Settings
@onready var _btn_quit      : Button = $MenuContainer/Quit


# ---------------------------------------------------------------------------
# Lifecycle
# ---------------------------------------------------------------------------

func _ready() -> void:
	CommandBus.state_changed.connect(_on_state_changed)

	_btn_new_game.pressed.connect(_on_new_game)
	_btn_load_game.pressed.connect(_on_load_game)
	_btn_settings.pressed.connect(_on_settings)
	_btn_quit.pressed.connect(_on_quit)

	hide()


# ---------------------------------------------------------------------------
# Visibility
# ---------------------------------------------------------------------------

func _on_state_changed(state: String) -> void:
	visible = (state == "MainMenu")


# ---------------------------------------------------------------------------
# Button handlers
# TODO: wire these to SendCommandAsync on the C# side once back-channel exists
# ---------------------------------------------------------------------------

func _on_new_game() -> void:
	print("[MainMenuPanel] New Game pressed")

func _on_load_game() -> void:
	print("[MainMenuPanel] Load Game pressed")

func _on_settings() -> void:
	print("[MainMenuPanel] Settings pressed")

func _on_quit() -> void:
	print("[MainMenuPanel] Quit pressed")
	get_tree().quit()
