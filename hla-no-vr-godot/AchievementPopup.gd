## AchievementPopup.gd
## Attach to UI/AchievementPopup in main.tscn.

extends Control

const DISPLAY_DURATION := 4.0

@onready var _name_label : Label = $PanelContainer/HBox/VBox/NameLabel
@onready var _timer      : Timer = $Timer


# ---------------------------------------------------------------------------
# Lifecycle
# ---------------------------------------------------------------------------

func _ready() -> void:
	CommandBus.achievement_received.connect(_on_achievement_received)

	_timer.wait_time = DISPLAY_DURATION
	_timer.one_shot  = true
	_timer.timeout.connect(_hide_popup)

	hide()


# ---------------------------------------------------------------------------
# Handlers
# ---------------------------------------------------------------------------

func _on_achievement_received(id: String) -> void:
	# TODO: swap id for a human-readable name via a lookup dictionary
	_name_label.text = id
	_show_popup()


# ---------------------------------------------------------------------------
# Animation
# ---------------------------------------------------------------------------

func _show_popup() -> void:
	modulate.a = 0.0
	show()
	_timer.start()

	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 1.0, 0.2)


func _hide_popup() -> void:
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.3)
	tween.tween_callback(hide)
