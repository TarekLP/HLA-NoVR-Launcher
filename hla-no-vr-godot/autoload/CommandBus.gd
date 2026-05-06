## CommandBus.gd
## Autoload singleton — add as "CommandBus" in Project > Autoloads.
##
## Every command that arrives from the launcher is dispatched through here.
## UI scenes connect to these signals rather than touching the socket directly.
## This keeps scenes completely decoupled from the transport layer.

extends Node


# ---------------------------------------------------------------------------
# Signals
# ---------------------------------------------------------------------------

## Launcher told us to become visible.
signal show_requested

## Launcher told us to become invisible.
signal hide_requested

## Game window moved or resized. Godot should re-layout if needed.
signal bounds_changed(x: int, y: int, w: int, h: int)

## Game state changed (e.g. "MainMenu", "Paused", "Loading", "InGame").
signal state_changed(state: String)

## Player unlocked an achievement — show a popup.
signal achievement_received(id: String)

## Launcher is shutting down — time to quit.
signal shutdown_requested


# ---------------------------------------------------------------------------
# Dispatch
# ---------------------------------------------------------------------------

## Called by OverlaySocket when a JSON line arrives.
## Maps the "cmd" string to the correct signal emission.
func dispatch(cmd: String, data: Variant) -> void:
	match cmd:
		"show":
			show_requested.emit()

		"hide":
			hide_requested.emit()

		"shutdown":
			shutdown_requested.emit()

		"set_bounds":
			if data is Dictionary:
				bounds_changed.emit(
					int(data.get("x", 0)),
					int(data.get("y", 0)),
					int(data.get("w", 0)),
					int(data.get("h", 0))
				)

		"set_state":
			if data is Dictionary:
				state_changed.emit(str(data.get("state", "")))

		"show_achievement":
			if data is Dictionary:
				achievement_received.emit(str(data.get("id", "")))

		_:
			push_warning("[CommandBus] Unknown command: %s" % cmd)
