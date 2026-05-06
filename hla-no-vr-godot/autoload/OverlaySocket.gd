## OverlaySocket.gd
## Autoload singleton — add as "OverlaySocket" in Project > Autoloads.
##
## Connects to the C# launcher over TCP, reads newline-delimited JSON,
## and forwards each message to CommandBus.dispatch().
##
## The launcher is the SERVER — this is the CLIENT.
## Port is passed via CLI: --overlay-port 47832
## (falls back to DEFAULT_PORT if not supplied)

extends Node


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

const DEFAULT_PORT := 47832
const HOST         := "127.0.0.1"

## How long to wait between reconnect attempts (ms).
const RECONNECT_DELAY_MS := 500

## How long to sleep each loop tick when no bytes are available (ms).
const POLL_INTERVAL_MS := 10


# ---------------------------------------------------------------------------
# State
# ---------------------------------------------------------------------------

var _peer    : StreamPeerTCP
var _thread  : Thread
var _running := false
var _port    : int = DEFAULT_PORT


# ---------------------------------------------------------------------------
# Lifecycle
# ---------------------------------------------------------------------------

func _ready() -> void:
	_port = _read_port_from_args()
	print("[OverlaySocket] Will connect to %s:%d" % [HOST, _port])

	_peer    = StreamPeerTCP.new()
	_thread  = Thread.new()
	_running = true

	# Run the entire connect + read loop on a background thread so the
	# main thread (and therefore the UI) never blocks.
	_thread.start(_connect_loop)


func _exit_tree() -> void:
	_running = false
	if _thread.is_started():
		_thread.wait_to_finish()
	_peer.disconnect_from_host()


# ---------------------------------------------------------------------------
# Background thread — connect loop
# ---------------------------------------------------------------------------

## Keeps trying to connect to the launcher. If the connection drops
## (launcher restarted, crash, etc.) it retries automatically.
func _connect_loop() -> void:
	while _running:
		_peer = StreamPeerTCP.new()

		var err := _peer.connect_to_host(HOST, _port)
		if err != OK:
			OS.delay_msec(RECONNECT_DELAY_MS)
			continue

		# Poll until the connection is established or fails.
		while _running and _peer.get_status() == StreamPeerTCP.STATUS_CONNECTING:
			_peer.poll()
			OS.delay_msec(POLL_INTERVAL_MS)

		if _peer.get_status() != StreamPeerTCP.STATUS_CONNECTED:
			OS.delay_msec(RECONNECT_DELAY_MS)
			continue

		print("[OverlaySocket] Connected to launcher.")
		_read_loop()

		print("[OverlaySocket] Disconnected. Retrying in %dms..." % RECONNECT_DELAY_MS)
		OS.delay_msec(RECONNECT_DELAY_MS)


# ---------------------------------------------------------------------------
# Background thread — read loop
# ---------------------------------------------------------------------------

## Reads bytes off the TCP stream, assembles them into lines, and hands
## each complete JSON line to _dispatch (called on the main thread).
func _read_loop() -> void:
	var buffer := ""

	while _running and _peer.get_status() == StreamPeerTCP.STATUS_CONNECTED:
		_peer.poll()

		var available := _peer.get_available_bytes()
		if available > 0:
			var result := _peer.get_partial_data(available)

			# result[0] is the error code, result[1] is a PackedByteArray
			if result[0] == OK:
				buffer += result[1].get_string_from_utf8()

				# Flush all complete lines from the buffer
				while "\n" in buffer:
					var nl    := buffer.find("\n")
					var line  := buffer.substr(0, nl).strip_edges()
					buffer     = buffer.substr(nl + 1)

					if line.length() > 0:
						# Always touch scene nodes on the main thread
						_dispatch.call_deferred(line)
		else:
			OS.delay_msec(POLL_INTERVAL_MS)


# ---------------------------------------------------------------------------
# Dispatch (main thread)
# ---------------------------------------------------------------------------

func _dispatch(line: String) -> void:
	var json := JSON.new()
	var err  := json.parse(line)

	if err != OK:
		push_warning("[OverlaySocket] JSON parse error on: %s" % line)
		return

	var msg : Variant = json.get_data()
	if not msg is Dictionary:
		push_warning("[OverlaySocket] Expected object, got: %s" % line)
		return

	var cmd  : String  = msg.get("cmd",  "")
	var data : Variant = msg.get("data", null)

	CommandBus.dispatch(cmd, data)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

func _read_port_from_args() -> int:
	var args := OS.get_cmdline_args()
	var idx  := args.find("--overlay-port")
	if idx != -1 and idx + 1 < args.size():
		return int(args[idx + 1])
	return DEFAULT_PORT
