## PausePanel.gd
## Attach to UI/PausePanel in main.tscn.

extends Control


# ---------------------------------------------------------------------------
# Node references
# ---------------------------------------------------------------------------

@onready var _btn_resume       : Button = $MenuContainer/Resume
@onready var _btn_settings     : Button = $MenuContainer/Settings
@onready var _btn_main_menu    : Button = $MenuContainer/MainMenu
@onready var _btn_quit_desktop : Button = $MenuContainer/QuitDesktop


# ---------------------------------------------------------------------------
# Lifecycle
# ---------------------------------------------------------------------------

func _ready() -> void:
	CommandBus.state_changed.connect(_on_state_changed)

	_btn_resume.pressed.connect(_on_resume)
	_btn_settings.pressed.connect(_on_settings)
	_btn_main_menu.pressed.connect(_on_main_menu)
	_btn_quit_desktop.pressed.connect(_on_quit_desktop)

	hide()


# ---------------------------------------------------------------------------
# Visibility
# ---------------------------------------------------------------------------

func _on_state_changed(state: String) -> void:
	visible = (state == "Paused")


# ---------------------------------------------------------------------------
# Button handlers
# TODO: wire these to SendCommandAsync on the C# side once back-channel exists
# ---------------------------------------------------------------------------

func _on_resume() -> void:
	print("[PausePanel] Resume pressed")

func _on_settings() -> void:
	print("[PausePanel] Settings pressed")

func _on_main_menu() -> void:
	print("[PausePanel] Main Menu pressed")

func _on_quit_desktop() -> void:
	print("[PausePanel] Quit to Desktop pressed")
	get_tree().quit()
