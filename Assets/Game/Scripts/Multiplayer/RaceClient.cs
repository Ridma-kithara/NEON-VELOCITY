using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NeonVelocity.Core;
using UnityEngine;

namespace NeonVelocity.Multiplayer
{
    /// <summary>
    /// Line-delimited JSON client for the authoritative NEON VELOCITY race server.
    /// Socket work happens on background threads; Unity only ever touches parsed messages on
    /// the main thread. The client automatically restores a live race after a brief network
    /// interruption using its server-issued reconnect token.
    /// </summary>
    public sealed class RaceClient : MonoBehaviour
    {
        public enum NetState
        {
            Disconnected,
            Connecting,
            Connected,
            Reconnecting,
            Failed
        }

        private sealed class NetEvent
        {
            public string Kind;
            public string Payload;
        }

        public NetState State { get; private set; } = NetState.Disconnected;
        public bool IsConnected => State == NetState.Connected;
        public string PlayerId { get; private set; } = "";
        public string Token { get; private set; } = "";
        public string PlayerName { get; private set; } = "RUNNER";
        public string Host { get; private set; } = "127.0.0.1";
        public int Port { get; private set; } = 7777;
        public string LastError { get; private set; } = "";
        public WireMessage LastWelcome { get; private set; }
        public WireMessage LastRoomState { get; private set; }
        public bool HasRoomState => LastRoomState != null;

        public void ClearRoomState() => LastRoomState = null;

        public event Action<NetState, string> StateChanged;
        public event Action<WireMessage> MessageReceived;
        public event Action Reconnected;

        private TcpClient _tcp;
        private NetworkStream _stream;
        private Thread _reader;
        private Thread _writer;
        private BlockingCollection<string> _outgoing;
        private readonly ConcurrentQueue<NetEvent> _incoming = new ConcurrentQueue<NetEvent>();
        private CancellationTokenSource _cancellation;
        private volatile bool _socketAlive;
        private bool _suppressReconnect;
        private int _generation;
        private int _requestCounter;
        private float _pingTimer;
        private float _reconnectAt;
        private int _reconnectAttempts;
        private bool _wantsSession;
        private readonly Dictionary<string, long> _pingSends = new Dictionary<string, long>();
        private double _serverOffsetMs;

        public long ServerNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)Math.Round(_serverOffsetMs);
        public long LocalNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void Update()
        {
            DrainEvents();
            if (State == NetState.Connecting || State == NetState.Reconnecting)
            {
                if (Time.unscaledTime >= _reconnectAt && _reconnectAttempts > 0)
                {
                    _reconnectAttempts--;
                    _reconnectAt = Time.unscaledTime + 2f;
                    BeginConnect(true);
                }
            }

            if (State == NetState.Connected)
            {
                _pingTimer -= Time.unscaledDeltaTime;
                if (_pingTimer <= 0f)
                {
                    _pingTimer = 3f;
                    SendPing();
                }
            }
        }

        public void SetPlayerName(string name)
        {
            PlayerName = RaceRules.SanitiseName(name);
        }

        public void Connect(string host, int port)
        {
            ConnectInternal(host, port, false);
        }

        /// <summary>Starts a new server session and discards any previous reconnect identity.</summary>
        public void ConnectFresh(string host, int port)
        {
            PlayerId = "";
            Token = "";
            LastRoomState = null;
            LastWelcome = null;
            _serverOffsetMs = 0d;
            ConnectInternal(host, port, false);
        }

        private void ConnectInternal(string host, int port, bool reconnect)
        {
            if (State == NetState.Connecting || State == NetState.Connected) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            Fail("Online races require a native build. WebGL does not support raw TCP sockets.");
            return;
#else
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            Port = Mathf.Clamp(port, 1, 65535);
            _wantsSession = true;
            _suppressReconnect = false;
            _reconnectAttempts = 0;
            BeginConnect(reconnect);
#endif
        }

        public void Disconnect(bool deliberate = true)
        {
            _suppressReconnect = deliberate;
            _wantsSession = false;
            CloseSocket();
            SetState(NetState.Disconnected, deliberate ? "" : "Connection lost");
        }

        private void BeginConnect(bool reconnect)
        {
            CloseSocket();
            SetState(reconnect ? NetState.Reconnecting : NetState.Connecting,
                reconnect ? "RECONNECTING TO CIRCUIT..." : "CONNECTING TO CIRCUIT...");
            _reconnectAt = Time.unscaledTime + 2f;
            int generation = ++_generation;
            string host = Host;
            int port = Port;

            var thread = new Thread(() =>
            {
                TcpClient client = null;
                try
                {
                    client = new TcpClient { NoDelay = true };
                    var task = client.ConnectAsync(host, port);
                    if (!task.Wait(6000))
                        throw new TimeoutException("Connection timed out.");
                    if (generation != Volatile.Read(ref _generation))
                    {
                        client.Close();
                        return;
                    }
                    var stream = client.GetStream();
                    _tcp = client;
                    _stream = stream;
                    _socketAlive = true;
                    var cts = new CancellationTokenSource();
                    _cancellation = cts;
                    _outgoing = new BlockingCollection<string>();
                    StartReader(generation, stream, cts.Token);
                    StartWriter(stream, _outgoing, cts.Token);
                    Post("connected", "");
                }
                catch (Exception e)
                {
                    if (client != null) client.Close();
                    if (generation == Volatile.Read(ref _generation))
                        Post("connect_error", e.Message);
                }
            })
            {
                IsBackground = true,
                Name = "NV-RaceConnect"
            };
            thread.Start();
        }

        private void StartReader(int generation, NetworkStream stream, CancellationToken token)
        {
            _reader = new Thread(() =>
            {
                try
                {
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true))
                    {
                        while (!token.IsCancellationRequested)
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;
                            if (line.Length == 0) continue;
                            Post("message", line);
                        }
                    }
                }
                catch (Exception)
                {
                    // Treated as a disconnect below.
                }
                finally
                {
                    if (generation == Volatile.Read(ref _generation))
                    {
                        _socketAlive = false;
                        Post("disconnected", "");
                    }
                }
            })
            {
                IsBackground = true,
                Name = "NV-RaceReader"
            };
            _reader.Start();
        }

        private void StartWriter(NetworkStream stream, BlockingCollection<string> outgoing, CancellationToken token)
        {
            _writer = new Thread(() =>
            {
                try
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                    {
                        foreach (string line in outgoing.GetConsumingEnumerable(token))
                        {
                            writer.Write(line);
                            writer.Write('\n');
                        }
                    }
                }
                catch (Exception)
                {
                    // Reader reports the disconnect.
                }
            })
            {
                IsBackground = true,
                Name = "NV-RaceWriter"
            };
            _writer.Start();
        }

        private void Post(string kind, string payload) => _incoming.Enqueue(new NetEvent { Kind = kind, Payload = payload });

        private void DrainEvents()
        {
            while (_incoming.TryDequeue(out NetEvent netEvent))
            {
                switch (netEvent.Kind)
                {
                    case "connected":
                        OnSocketConnected();
                        break;
                    case "connect_error":
                        LastError = netEvent.Payload;
                        if (_reconnectAttempts > 0 && !string.IsNullOrEmpty(Token) && _wantsSession)
                        {
                            SetState(NetState.Reconnecting, "LINK UNSTABLE - RETRYING...");
                            _reconnectAt = Time.unscaledTime + 2f;
                        }
                        else
                        {
                            Fail("Could not reach " + Host + ":" + Port + "  (" + netEvent.Payload + ")");
                        }
                        break;
                    case "disconnected":
                        OnSocketLost();
                        break;
                    case "message":
                        ParseAndDispatch(netEvent.Payload);
                        break;
                }
            }
        }

        private void OnSocketConnected()
        {
            SetState(NetState.Connected, "");
            LastError = "";
            _reconnectAttempts = 0;
            _pingTimer = 0.5f;

            if (!string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(PlayerId))
            {
                var reconnect = NewMessage("reconnect");
                reconnect.playerId = PlayerId;
                reconnect.token = Token;
                reconnect.name = PlayerName;
                Send(reconnect);
            }
            else
            {
                var hello = NewMessage("hello");
                hello.name = PlayerName;
                hello.clientVersion = Application.version;
                Send(hello);
            }
        }

        private void OnSocketLost()
        {
            if (!_wantsSession || _suppressReconnect)
            {
                SetState(NetState.Disconnected, "");
                return;
            }
            if (!string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(PlayerId))
            {
                _reconnectAttempts = 3;
                SetState(NetState.Reconnecting, "SIGNAL LOST - RESTORING LINK...");
                _reconnectAt = Time.unscaledTime + 1f;
            }
            else
            {
                Fail("Connection to the race server was lost.");
            }
        }

        private void Fail(string message)
        {
            LastError = message;
            _socketAlive = false;
            CloseSocket();
            SetState(NetState.Failed, message);
        }

        private void ParseAndDispatch(string line)
        {
            WireMessage message;
            try
            {
                message = JsonUtility.FromJson<WireMessage>(line);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RaceClient] Could not parse server message: " + e.Message);
                return;
            }
            if (message == null || string.IsNullOrEmpty(message.type)) return;
            if (!string.IsNullOrEmpty(message.error) && message.ok == false)
            {
                LastError = message.error;
            }

            switch (message.type)
            {
                case "welcome":
                    PlayerId = message.playerId ?? PlayerId;
                    if (!string.IsNullOrEmpty(message.token)) Token = message.token;
                    LastWelcome = message;
                    ApplyServerTime(message.serverTimeMs);
                    break;
                case "pong":
                    ApplyPong(message);
                    return;
                case "room_state":
                    LastRoomState = message;
                    if (!string.IsNullOrEmpty(message.playerId) && string.IsNullOrEmpty(Token))
                    {
                        // Some servers resend identity in room state after a reconnect.
                        Token = message.token;
                    }
                    break;
                case "state_sync":
                    if (message.players != null && message.players.Count > 0)
                        LastRoomState = message;
                    break;
                case "reconnected":
                    Reconnected?.Invoke();
                    break;
                case "session_expired":
                    Token = "";
                    PlayerId = "";
                    _wantsSession = false;
                    _suppressReconnect = true;
                    Fail(string.IsNullOrEmpty(message.error)
                        ? "SESSION EXPIRED - RETURN TO MULTIPLAYER"
                        : message.error);
                    return;
                case "welcome_back":
                    ApplyServerTime(message.serverTimeMs);
                    break;
            }

            MessageReceived?.Invoke(message);
        }

        private void ApplyPong(WireMessage message)
        {
            long now = LocalNowMs;
            if (!string.IsNullOrEmpty(message.requestId) &&
                _pingSends.TryGetValue(message.requestId, out long sent))
            {
                long rtt = now - sent;
                long midpoint = sent + rtt / 2;
                if (message.serverTimeMs > 0)
                {
                    double sample = message.serverTimeMs - midpoint;
                    _serverOffsetMs = _serverOffsetMs == 0d ? sample : _serverOffsetMs * 0.8d + sample * 0.2d;
                }
                _pingSends.Remove(message.requestId);
            }
            else
            {
                ApplyServerTime(message.serverTimeMs);
            }
        }

        private void ApplyServerTime(long serverTimeMs)
        {
            if (serverTimeMs <= 0) return;
            double sample = serverTimeMs - LocalNowMs;
            _serverOffsetMs = _serverOffsetMs == 0d ? sample : _serverOffsetMs * 0.7d + sample * 0.3d;
        }

        private void SendPing()
        {
            var ping = NewMessage("ping");
            _pingSends[ping.requestId] = LocalNowMs;
            Send(ping);
            if (_pingSends.Count > 12)
            {
                var keys = new List<string>(_pingSends.Keys);
                for (int i = 0; i < keys.Count - 8; i++) _pingSends.Remove(keys[i]);
            }
        }

        public WireMessage NewMessage(string type)
        {
            return new WireMessage
            {
                type = type,
                requestId = "c" + (++_requestCounter),
                playerId = PlayerId,
                token = Token,
                name = PlayerName
            };
        }

        public void Send(WireMessage message)
        {
            if (message == null) return;
            if (!_socketAlive || _outgoing == null)
            {
                Debug.LogWarning("[RaceClient] Message dropped, socket is not connected: " + message.type);
                return;
            }
            string json = JsonUtility.ToJson(message);
            _outgoing.Add(json);
        }

        public void SendRaw(string json)
        {
            if (_socketAlive && _outgoing != null) _outgoing.Add(json);
        }

        private void CloseSocket()
        {
            _socketAlive = false;
            try { _cancellation?.Cancel(); } catch (Exception) { }
            try { _outgoing?.CompleteAdding(); } catch (Exception) { }
            try { _stream?.Close(); } catch (Exception) { }
            try { _tcp?.Close(); } catch (Exception) { }
            _stream = null;
            _tcp = null;
            _outgoing = null;
            _cancellation = null;
            _reader = null;
            _writer = null;
        }

        private void OnDestroy()
        {
            _suppressReconnect = true;
            _wantsSession = false;
            CloseSocket();
        }

        private void SetState(NetState state, string message)
        {
            if (State == state && string.IsNullOrEmpty(message)) return;
            State = state;
            StateChanged?.Invoke(state, message);
        }

        public void PrepareReconnect()
        {
            _reconnectAttempts = 3;
            _wantsSession = true;
            _suppressReconnect = false;
        }
    }
}
