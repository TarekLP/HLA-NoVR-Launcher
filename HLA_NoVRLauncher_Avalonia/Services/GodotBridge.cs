using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HLA_NoVRLauncher_Avalonia.Services
{
	// ---------------------------------------------------------------------------
	// Godot bridge (TCP)
	// ---------------------------------------------------------------------------

	/// <summary>
	/// Owns a TCP server that the Godot overlay process connects to.
	/// Swapped from NamedPipeServerStream to TcpListener because GDScript's
	/// StreamPeerTCP is first-class, while named pipe client support in GDScript
	/// is non-existent.
	///
	/// Everything else about the contract is identical to the pipe version:
	///   - One JSON line per message, newline-terminated.
	///   - Launcher is the SERVER; Godot is the CLIENT.
	///   - Godot connects once on startup.
	///   - Commands are fire-and-forget — no response expected.
	///
	/// Usage:
	///   1. Construct (optionally supply a port; default is 47832).
	///   2. Call StartAsync() — spins up the accept loop.
	///   3. Call SendAsync() to push commands.
	///   4. DisposeAsync() to shut down cleanly.
	/// </summary>
	public sealed class GodotBridge : IAsyncDisposable
	{
		// -----------------------------------------------------------------------
		// Constants
		// -----------------------------------------------------------------------

		/// <summary>
		/// Default TCP port. Passed to Godot as --overlay-port 47832.
		/// Pick anything above 1024 that isn't likely to be in use.
		/// </summary>
		public const int DefaultPort = 47832;

		// -----------------------------------------------------------------------
		// State
		// -----------------------------------------------------------------------

		private readonly int _port;

		private TcpListener?  _listener;
		private TcpClient?    _client;
		private StreamWriter? _writer;

		private readonly SemaphoreSlim _writeLock = new(1, 1);

		private CancellationTokenSource? _cts;
		private Task? _acceptLoop;

		private bool _clientConnected = false;

		// -----------------------------------------------------------------------
		// Public events
		// -----------------------------------------------------------------------

		/// <summary>Fired when the Godot process successfully connects.</summary>
		public event Action? ClientConnected;

		/// <summary>Fired when the TCP connection is lost.</summary>
		public event Action? ClientDisconnected;

		// -----------------------------------------------------------------------
		// Constructor
		// -----------------------------------------------------------------------

		public GodotBridge(int port = DefaultPort)
		{
			_port = port;
		}

		// -----------------------------------------------------------------------
		// Lifecycle
		// -----------------------------------------------------------------------

		/// <summary>
		/// Starts the TCP server and begins waiting for Godot to connect.
		/// Returns immediately — the accept loop runs in the background.
		/// </summary>
		public Task StartAsync(CancellationToken ct = default)
		{
			_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			_listener = new TcpListener(IPAddress.Loopback, _port);
			_listener.Start();

			_acceptLoop = RunAcceptLoopAsync(_cts.Token);

			Console.WriteLine($"[GodotBridge] TCP server listening on 127.0.0.1:{_port}");
			return Task.CompletedTask;
		}

		/// <summary>
		/// Serialises the command to JSON and writes it down the TCP stream.
		/// Silently drops the message if Godot is not yet connected.
		/// Thread-safe.
		/// </summary>
		public async Task SendAsync(OverlayCommand command, CancellationToken ct = default)
		{
			if (!_clientConnected || _writer == null) return;

			await _writeLock.WaitAsync(ct);
			try
			{
				string line = command.ToJson();
				await _writer.WriteLineAsync(line.AsMemory(), ct);
				await _writer.FlushAsync(ct);
				Console.WriteLine($"[GodotBridge] Sent: {line}");
			}
			catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
			{
				Console.WriteLine($"[GodotBridge] Write failed (connection lost): {ex.Message}");
				HandleDisconnect();
			}
			finally
			{
				_writeLock.Release();
			}
		}

		// -----------------------------------------------------------------------
		// Accept loop
		// -----------------------------------------------------------------------

		private async Task RunAcceptLoopAsync(CancellationToken ct)
		{
			while (!ct.IsCancellationRequested)
			{
				try
				{
					Console.WriteLine("[GodotBridge] Waiting for Godot to connect...");
					_client = await _listener!.AcceptTcpClientAsync(ct);
					_client.NoDelay = true; // Disable Nagle — we're sending small frequent messages

					var stream    = _client.GetStream();
					_writer       = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
					_clientConnected = true;

					Console.WriteLine("[GodotBridge] Godot connected.");
					ClientConnected?.Invoke();

					await MonitorConnectionAsync(ct);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[GodotBridge] Accept loop error: {ex.Message}");
					HandleDisconnect();
					await Task.Delay(500, ct);
				}
			}
		}

		private async Task MonitorConnectionAsync(CancellationToken ct)
		{
			while (!ct.IsCancellationRequested)
			{
				await Task.Delay(100, ct);

				if (_client == null || !_client.Connected)
				{
					Console.WriteLine("[GodotBridge] Connection lost.");
					HandleDisconnect();
					return;
				}
			}
		}

		// -----------------------------------------------------------------------
		// Helpers
		// -----------------------------------------------------------------------

		private void HandleDisconnect()
		{
			_clientConnected = false;
			_writer?.Dispose();
			_writer = null;
			_client?.Dispose();
			_client = null;
			ClientDisconnected?.Invoke();
		}

		// -----------------------------------------------------------------------
		// Dispose
		// -----------------------------------------------------------------------

		public async ValueTask DisposeAsync()
		{
			_cts?.Cancel();

			if (_acceptLoop != null)
			{
				try { await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2)); }
				catch { }
			}

			HandleDisconnect();
			_listener?.Stop();
			_writeLock.Dispose();
			_cts?.Dispose();
		}
	}
}
