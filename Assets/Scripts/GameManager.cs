using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Cinemachine;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RedLightQwop
{
    public enum LightPhase { Green, Red }
    public enum GameState { Playing, Won, Eliminated, TimedOut }
    public enum Difficulty { Easy, Normal, Hard, Brutal }

    /// <summary>
    /// Owns the session (host or client) and, on the host, runs the Red Light, Green Light
    /// round: phases, red-light judging of every player and runner, finish, timeout, restart.
    /// Peers connect directly to the host's address; there is no dedicated server.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Prefabs (must have NetworkObject)")]
        public GameObject PlayerPrefab;
        public GameObject NpcPrefab;
        public GameObject GameStatePrefab;

        [Header("Scene")]
        public TrafficDoll Doll;
        public GameHud Hud;
        public Transform FinishLine;
        public CinemachineCamera FollowCamera;
        public SessionMenu Menu;
        public Vector3[] PlayerSpawns = { new Vector3(0f, 0.02f, 0f), new Vector3(0.7f, 0.02f, -0.7f), new Vector3(-0.7f, 0.02f, -0.7f), new Vector3(0f, 0.02f, -1.4f) };
        public Vector3[] NpcSpawns =
        {
            new Vector3(-1.4f, 0.02f, 0.9f), new Vector3(1.4f, 0.02f, 0.9f),
            new Vector3(-2.6f, 0.02f, -0.4f), new Vector3(2.6f, 0.02f, -0.4f),
            new Vector3(-3.8f, 0.02f, 0.6f), new Vector3(3.8f, 0.02f, 0.6f),
            new Vector3(-1.2f, 0.02f, -1.4f), new Vector3(1.2f, 0.02f, -1.4f),
            new Vector3(-3.2f, 0.02f, 1.8f), new Vector3(3.2f, 0.02f, 1.8f),
        };

        [Header("Rules")]
        public Vector2 GreenDuration = new Vector2(3f, 6f);
        public Vector2 RedDuration = new Vector2(2f, 4f);
        [Tooltip("Seconds after red starts before movement is judged (the doll is turning).")]
        public float RedGracePeriod = 0.8f;
        [Tooltip("Pelvis speed (m/s) that counts as moving during red.")]
        public float MoveThreshold = 0.4f;
        [Tooltip("Seconds of continuous movement during red before elimination.")]
        public float MoveTolerance = 0.15f;
        public float TimeLimit = 120f;
        [Tooltip("0 = random each run.")]
        public int RandomSeed = 0;
        [Tooltip("Physics step in seconds. Jointed ragdolls need a small step to stay stable.")]
        public float PhysicsTimestep = 0.01f;
        [Tooltip("When on, runners collide with each other and with the player.")]
        public bool CharactersCollide = true;
        public Color EliminatedTint = new Color(0.45f, 0.4f, 0.4f);

        [Header("Difficulty (host decides, applies to every doll)")]
        public Difficulty StartDifficulty = Difficulty.Normal;
        [Tooltip("Per difficulty: x = muscle strength multiplier, y = balance assist multiplier. Index order: Easy, Normal, Hard, Brutal.")]
        public Vector2[] DifficultyStrength =
        {
            new Vector2(1.3f, 1.6f),
            new Vector2(1.0f, 1.0f),
            new Vector2(0.75f, 0.55f),
            new Vector2(0.55f, 0.25f),
        };

        [Header("Network")]
        public ushort Port = 7777;
        [Tooltip("Browser builds cannot open ports, so every session there goes through the relay. Jointed ragdolls need the 10 ms step even there.")]
        public float WebPhysicsTimestep = 0.01f;
        public int WebNpcCount = 4;
        [Tooltip("Browser builds: most simulated time one frame may catch up. Caps physics steps per frame so a slow tab goes into slow motion instead of freezing.")]
        public float WebMaxDeltaTime = 0.05f;
        [Tooltip("Seconds without traffic before a connection is considered lost. Generous so a briefly hidden browser tab does not end the session.")]
        public int DisconnectTimeoutSeconds = 120;
        [Tooltip("Network ticks per second when a browser hosts; lower halves the upload compared with the desktop's 30.")]
        public int WebTickRate = 20;
        [Tooltip("Browser builds run on requestAnimationFrame, which browsers stop for hidden tabs. Setting a target frame rate switches Unity to a timer loop that keeps ticking (slowly) while hidden, so a host that alt-tabs does not drop everyone.")]
        public int WebSessionFrameRate = 60;
        [Tooltip("Players per online (relay) session, including the host.")]
        public int MaxPlayers = 4;
        [Tooltip("Honour --host, --join <ip>, --solo, --port <n>, --relay-host and --relay-join <code> on the command line.")]
        public bool UseCommandLine = true;

        /// <summary>True in a WebGL player: no listening sockets, WebSocket transport, relay only.</summary>
        public static bool IsWebBuild => Application.platform == RuntimePlatform.WebGLPlayer;

        public const string PlayerLayerName = "Player";
        public const string NpcLayerName = "NPC";

        // --- Runtime state -------------------------------------------------------------------
        public NetGameState Net { get; private set; }
        public NetworkRagdoll LocalPlayer { get; private set; }
        public List<NetworkRagdoll> Players { get; } = new List<NetworkRagdoll>();
        public List<NetworkRagdoll> NpcRagdolls { get; } = new List<NetworkRagdoll>();
        /// <summary>Server-side runner brains (empty on clients).</summary>
        public List<NpcBrain> Npcs { get; } = new List<NpcBrain>();

        public Ragdoll Player => LocalPlayer != null ? LocalPlayer.Ragdoll : null;
        public LimbController Controller => LocalPlayer != null ? LocalPlayer.Controller : null;
        public GameState State => LocalPlayer != null ? LocalPlayer.State : GameState.Playing;
        public LightPhase Phase => Net != null ? Net.LightPhase : LightPhase.Green;
        public float TimeRemaining => Net != null ? Net.TimeRemaining.Value : TimeLimit;
        public bool RoundRunning => Net != null && Net.RoundRunning.Value;
        public float PhaseElapsed { get; private set; }
        public bool IsRedJudging => Phase == LightPhase.Red && PhaseElapsed >= RedGracePeriod;
        public bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        public bool IsClientOnly => NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer;
        public bool SessionActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        public bool LocalInputAllowed => RoundRunning && (Menu == null || !Menu.IsOpen);
        public string SessionLabel { get; private set; } = "";
        /// <summary>Join code of the current online session, or null when playing direct/solo.</summary>
        public string JoinCode { get; private set; }
        public bool IsConnecting { get; private set; }
        public event System.Action<string> SessionStatusChanged;

        public Difficulty CurrentDifficulty => Net != null ? (Difficulty)Net.Difficulty.Value : StartDifficulty;
        public Vector2 CurrentStrength
        {
            get
            {
                int i = Mathf.Clamp((int)CurrentDifficulty, 0, DifficultyStrength.Length - 1);
                return DifficultyStrength.Length > 0 ? DifficultyStrength[i] : Vector2.one;
            }
        }

        public int NpcCount => NpcRagdolls.Count;
        public int NpcActiveCount
        {
            get
            {
                int n = 0;
                foreach (var npc in NpcRagdolls) if (npc.NpcState == NpcBrain.NpcState.Active) n++;
                return n;
            }
        }

        public float DistanceToFinish
        {
            get
            {
                if (FinishLine == null || Player == null) return 0f;
                return Mathf.Max(0f, FinishLine.position.z - Player.Position.z);
            }
        }

        /// <summary>Smoothed frames per second, for the HUD and for diagnosing slow hosts.</summary>
        public float Fps { get; private set; }
        /// <summary>Fraction of real time the simulation is keeping up with (1 = full speed).</summary>
        public float SimSpeed { get; private set; } = 1f;
        float m_FpsAccum, m_FpsFrames, m_FpsClock, m_SimTimeAtSample, m_RealTimeAtSample;

        System.Random m_Rng;
        float m_PhaseTimeLeft;
        bool m_PhaseLocked;
        bool m_CallbacksHooked;
        float m_LogClock;

        // --- Lifecycle -----------------------------------------------------------------------

        void Awake()
        {
            Instance = this;
            if (IsWebBuild && WebPhysicsTimestep > 0f) PhysicsTimestep = WebPhysicsTimestep;
            if (PhysicsTimestep > 0f) Time.fixedDeltaTime = PhysicsTimestep;
            if (IsWebBuild && WebMaxDeltaTime > 0f) Time.maximumDeltaTime = WebMaxDeltaTime;
            ConfigureLayerCollisions(CharactersCollide);
            m_Rng = RandomSeed == 0 ? new System.Random() : new System.Random(RandomSeed);
        }

        void Start()
        {
            if (Hud != null) Hud.Refresh(this);
            if (UseCommandLine) HandleCommandLine();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnhookCallbacks();
        }

        public static void ConfigureLayerCollisions(bool collide)
        {
            int npc = LayerMask.NameToLayer(NpcLayerName);
            int player = LayerMask.NameToLayer(PlayerLayerName);
            if (npc < 0) return;
            Physics.IgnoreLayerCollision(npc, npc, !collide);
            if (player >= 0)
            {
                Physics.IgnoreLayerCollision(npc, player, !collide);
                Physics.IgnoreLayerCollision(player, player, !collide);
            }
        }

        void HandleCommandLine()
        {
            if (IsWebBuild)
            {
                // Browser builds have no command line; honour ?join=CODE in the page URL instead.
                var url = Application.absoluteURL ?? "";
                int q = url.IndexOf("join=", System.StringComparison.OrdinalIgnoreCase);
                if (q >= 0)
                {
                    string code = url.Substring(q + 5);
                    int end = code.IndexOfAny(new[] { '&', '#' });
                    if (end >= 0) code = code.Substring(0, end);
                    if (code.Length > 0) _ = StartClientRelayAsync(code);
                }
                return;
            }

            var args = System.Environment.GetCommandLineArgs();
            string join = null, relayJoin = null;
            bool host = false, solo = false, relayHost = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--host": host = true; break;
                    case "--solo": solo = true; break;
                    case "--relay-host": relayHost = true; break;
                    case "--join": if (i + 1 < args.Length) join = args[++i]; break;
                    case "--relay-join": if (i + 1 < args.Length) relayJoin = args[++i]; break;
                    case "--port": if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out var p)) { Port = p; i++; } break;
                }
            }
            if (relayJoin != null) _ = StartClientRelayAsync(relayJoin);
            else if (relayHost) _ = StartHostRelayAsync();
            else if (join != null) StartClient(join);
            else if (host || solo) StartHost();
        }

        // --- Session -------------------------------------------------------------------------

        NetworkManager EnsureNetworkManager()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                var go = new GameObject("NetworkManager");
                nm = go.AddComponent<NetworkManager>();
                var transport = go.AddComponent<UnityTransport>();
                nm.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = transport,
                    EnableSceneManagement = false,
                    TickRate = 30,
                    ConnectionApproval = false,
                };
            }
            var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
            utp.UseWebSockets = IsWebBuild;
            // Tolerate a peer that stalls for a while (a browser host whose tab was hidden, a laptop
            // that slept briefly) instead of dropping everyone after the default 30 s.
            utp.DisconnectTimeoutMS = DisconnectTimeoutSeconds * 1000;
            utp.MaxSendQueueSize = Mathf.Max(utp.MaxSendQueueSize, 512 * 1024);
            if (IsWebBuild)
            {
                nm.NetworkConfig.TickRate = (uint)Mathf.Clamp(WebTickRate, 10, 60);
                Application.targetFrameRate = WebSessionFrameRate;
            }
            Application.runInBackground = true;
            foreach (var prefab in new[] { PlayerPrefab, NpcPrefab, GameStatePrefab })
            {
                if (prefab != null && !nm.NetworkConfig.Prefabs.Contains(prefab)) nm.AddNetworkPrefab(prefab);
            }
            HookCallbacks(nm);
            return nm;
        }

        void HookCallbacks(NetworkManager nm)
        {
            if (m_CallbacksHooked) return;
            nm.OnServerStarted += OnServerStarted;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            m_CallbacksHooked = true;
        }

        void UnhookCallbacks()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !m_CallbacksHooked) return;
            nm.OnServerStarted -= OnServerStarted;
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
            m_CallbacksHooked = false;
        }

        /// <summary>Host a session that others can join at this machine's address. Also used for solo play.
        /// Browser builds cannot listen, so they host through the relay instead.</summary>
        public bool StartHost()
        {
            if (IsWebBuild)
            {
                _ = StartHostRelayAsync();
                return true;
            }
            var nm = EnsureNetworkManager();
            if (nm.IsListening) return false;
            var transport = (UnityTransport)nm.NetworkConfig.NetworkTransport;
            transport.SetConnectionData("127.0.0.1", Port, "0.0.0.0");
            bool ok = nm.StartHost();
            SetStatus(ok ? $"hosting {LocalIPv4()}:{Port}" : "failed to host");
            if (ok && Menu != null) Menu.Close();
            return ok;
        }

        public bool StartSolo() => StartHost();

        public bool StartClient(string address)
        {
            if (IsWebBuild)
            {
                SetStatus("browser builds join with a code, not an address");
                return false;
            }
            var nm = EnsureNetworkManager();
            if (nm.IsListening) return false;
            var transport = (UnityTransport)nm.NetworkConfig.NetworkTransport;
            transport.SetConnectionData(address, Port);
            bool ok = nm.StartClient();
            SetStatus(ok ? $"joining {address}:{Port}" : "failed to connect");
            if (ok && Menu != null) Menu.Close();
            return ok;
        }

        public void EndSession()
        {
            if (m_Session != null)
            {
                var session = m_Session;
                m_Session = null;
                _ = session.LeaveAsync();
            }
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();
            SessionLabel = "";
            JoinCode = null;
            if (IsWebBuild) Application.targetFrameRate = -1;
        }

        /// <summary>Copy the join code to the clipboard (C key, or the menu). Returns false if unavailable.</summary>
        public bool CopyJoinCode()
        {
            if (string.IsNullOrEmpty(JoinCode)) return false;
            bool ok = WebBridge.CopyText(JoinCode);
            SetStatus(ok ? $"online, join code {JoinCode} (copied)" : $"online, join code {JoinCode}");
            return ok;
        }

        /// <summary>True while a browser host's tab is hidden, when the simulation runs at a crawl.</summary>
        public bool HostTabHidden => IsWebBuild && IsServer && WebBridge.IsDocumentHidden;

        // --- Online sessions through Unity Relay ---------------------------------------------
        // The Multiplayer Services SDK allocates a relay, configures the UnityTransport and starts
        // the NetworkManager as host or client itself, so the same spawn callbacks run as for a
        // direct connection. Players never need to open ports.

        ISession m_Session;

        void SetStatus(string text)
        {
            SessionLabel = text;
            Debug.Log($"[Net] {text}");
            SessionStatusChanged?.Invoke(text);
        }

        async Task EnsureServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        /// <summary>Host an online session. Returns the join code, or null on failure.</summary>
        public async Task<string> StartHostRelayAsync()
        {
            var nm = EnsureNetworkManager();
            if (nm.IsListening || IsConnecting) return null;
            IsConnecting = true;
            try
            {
                SetStatus("signing in...");
                await EnsureServicesAsync();
                SetStatus("creating online session...");
                var options = new SessionOptions { Name = "RedLightQwop", MaxPlayers = Mathf.Max(1, MaxPlayers) }.WithRelayNetwork();
                var session = await MultiplayerService.Instance.CreateSessionAsync(options);
                m_Session = session;
                JoinCode = session.Code;
                session.RemovedFromSession += OnRemovedFromSession;
                session.Deleted += OnRemovedFromSession;
                SetStatus($"online, join code {JoinCode}");
                if (Menu != null) Menu.Close();
                return JoinCode;
            }
            catch (SessionException e)
            {
                SetStatus($"online hosting failed: {e.Error} - {e.Message}");
                if (Menu != null) Menu.Open(SessionLabel);
                return null;
            }
            catch (System.Exception e)
            {
                SetStatus($"online hosting failed: {e.Message}");
                if (Menu != null) Menu.Open(SessionLabel);
                return null;
            }
            finally
            {
                IsConnecting = false;
            }
        }

        /// <summary>Join an online session by its code.</summary>
        public async Task<bool> StartClientRelayAsync(string code)
        {
            var nm = EnsureNetworkManager();
            if (nm.IsListening || IsConnecting) return false;
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) { SetStatus("enter a join code"); return false; }
            IsConnecting = true;
            try
            {
                SetStatus("signing in...");
                await EnsureServicesAsync();
                SetStatus($"joining {code}...");
                var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                m_Session = session;
                JoinCode = session.Code;
                session.RemovedFromSession += OnRemovedFromSession;
                session.Deleted += OnRemovedFromSession;
                SetStatus($"online, joined {code}");
                if (Menu != null) Menu.Close();
                return true;
            }
            catch (SessionException e)
            {
                SetStatus($"join failed: {e.Error} - {e.Message}");
                if (Menu != null) Menu.Open(SessionLabel);
                return false;
            }
            catch (System.Exception e)
            {
                SetStatus($"join failed: {e.Message}");
                if (Menu != null) Menu.Open(SessionLabel);
                return false;
            }
            finally
            {
                IsConnecting = false;
            }
        }

        void OnRemovedFromSession()
        {
            m_Session = null;
            JoinCode = null;
            SetStatus("online session ended");
            if (Menu != null) Menu.Open(SessionLabel);
        }

        public static string LocalIPv4()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork) return addr.Address.ToString();
                    }
                }
            }
            catch (System.Exception) { }
            return "127.0.0.1";
        }

        // --- Network callbacks ---------------------------------------------------------------

        void OnServerStarted()
        {
            var stateGo = Instantiate(GameStatePrefab);
            stateGo.GetComponent<NetworkObject>().Spawn();
            if (Net != null) Net.Difficulty.Value = (int)StartDifficulty;
            SpawnNpcs();
            BeginRound();
        }

        /// <summary>Host only. Changes the strength of every doll, now and for dolls spawned later.</summary>
        public void SetDifficulty(Difficulty difficulty)
        {
            if (!IsServer || Net == null) return;
            Net.Difficulty.Value = (int)difficulty;
            foreach (var p in Players) ApplyDifficultyTo(p);
            foreach (var n in NpcRagdolls) ApplyDifficultyTo(n);
            SetStatus($"difficulty {difficulty}");
        }

        public void CycleDifficulty(int step)
        {
            int count = System.Enum.GetValues(typeof(Difficulty)).Length;
            int next = ((int)CurrentDifficulty + step + count) % count;
            SetDifficulty((Difficulty)next);
        }

        void ApplyDifficultyTo(NetworkRagdoll doll)
        {
            if (doll == null || doll.Ragdoll == null) return;
            var strength = CurrentStrength;
            doll.Ragdoll.ApplyStrength(strength.x, strength.y);
        }

        void OnClientConnected(ulong clientId)
        {
            if (IsServer)
            {
                var slot = PlayerSpawns[Players.Count % PlayerSpawns.Length];
                var go = Instantiate(PlayerPrefab, slot, Quaternion.identity);
                go.name = $"Player_{clientId}";
                go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
                Debug.Log($"[Net] Spawned player for client {clientId} at {slot}");
            }
            if (NetworkManager.Singleton.LocalClientId == clientId && !IsServer)
            {
                SetStatus(JoinCode != null ? $"online, joined {JoinCode} (client {clientId})" : $"connected to host (client {clientId})");
            }
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == clientId)
            {
                SetStatus("disconnected");
                if (Menu != null) Menu.Open("Disconnected from host");
            }
        }

        // --- Registration (called by network behaviours as they spawn) -----------------------

        public void Register(NetworkRagdoll doll)
        {
            if (IsServer) ApplyDifficultyTo(doll);
            if (doll.IsNpc)
            {
                if (!NpcRagdolls.Contains(doll)) NpcRagdolls.Add(doll);
                if (IsServer && doll.Brain != null && !Npcs.Contains(doll.Brain)) { doll.Brain.Game = this; Npcs.Add(doll.Brain); }
            }
            else
            {
                if (!Players.Contains(doll)) Players.Add(doll);
                if (doll.IsOwner) BindLocalPlayer(doll);
            }
        }

        public void Unregister(NetworkRagdoll doll)
        {
            NpcRagdolls.Remove(doll);
            Players.Remove(doll);
            if (doll.Brain != null) Npcs.Remove(doll.Brain);
            if (LocalPlayer == doll) LocalPlayer = null;
        }

        void BindLocalPlayer(NetworkRagdoll doll)
        {
            LocalPlayer = doll;
            if (FollowCamera != null && doll.Ragdoll != null && doll.Ragdoll.Pelvis != null)
            {
                FollowCamera.Target.TrackingTarget = doll.Ragdoll.Pelvis.transform;
            }
        }

        public void AttachNetState(NetGameState state) => Net = state;
        public void DetachNetState(NetGameState state) { if (Net == state) Net = null; }

        // --- Round (server) ------------------------------------------------------------------

        void SpawnNpcs()
        {
            int count = IsWebBuild ? Mathf.Min(WebNpcCount, NpcSpawns.Length) : NpcSpawns.Length;
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(NpcPrefab, NpcSpawns[i], Quaternion.identity);
                go.name = $"Runner_{i + 1}";
                var brain = go.GetComponent<NpcBrain>();
                if (brain != null)
                {
                    brain.Game = this;
                    brain.StartDelay = 0.2f + 0.1f * (i % 5);
                    brain.StepTime = 0.19f + 0.01f * (i % 4);
                }
                go.GetComponent<NetworkObject>().Spawn();
            }
        }

        void BeginRound()
        {
            if (!IsServer || Net == null) return;
            Net.TimeRemaining.Value = TimeLimit;
            Net.RoundRunning.Value = true;
            Net.Round.Value += 1;
            m_PhaseLocked = false;
            SetPhase(LightPhase.Green, Range(GreenDuration));
        }

        /// <summary>Server: put every doll back on the start line and begin a new round.</summary>
        public void RestartRound()
        {
            if (!IsServer) return;
            foreach (var p in Players) p.ResetForRound();
            foreach (var npc in new List<NetworkRagdoll>(NpcRagdolls)) npc.NetworkObject.Despawn(true);
            NpcRagdolls.Clear();
            Npcs.Clear();
            SpawnNpcs();
            BeginRound();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame && SessionActive && (Menu == null || !Menu.IsOpen))
            {
                if (IsServer) RestartRound();
                else if (Net != null) Net.RequestRestartRpc();
            }

            if (keyboard != null && IsServer && Net != null && (Menu == null || !Menu.IsOpen))
            {
                if (keyboard.rightBracketKey.wasPressedThisFrame) CycleDifficulty(+1);
                if (keyboard.leftBracketKey.wasPressedThisFrame) CycleDifficulty(-1);
            }
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame && JoinCode != null && (Menu == null || !Menu.IsOpen)) CopyJoinCode();

            if (IsServer && Net != null && Net.RoundRunning.Value) ServerTick(Time.deltaTime);

            TrackPerformance();
            if (IsClientOnly && Application.isBatchMode) BatchLog();
            if (Hud != null) Hud.Refresh(this);
        }

        void TrackPerformance()
        {
            m_FpsAccum += Time.unscaledDeltaTime;
            m_FpsFrames += 1f;
            m_FpsClock += Time.unscaledDeltaTime;
            if (m_FpsClock < 1f) return;
            Fps = m_FpsFrames / Mathf.Max(0.001f, m_FpsAccum);
            float realElapsed = Time.realtimeSinceStartup - m_RealTimeAtSample;
            float simElapsed = Time.time - m_SimTimeAtSample;
            SimSpeed = realElapsed > 0f ? Mathf.Clamp01(simElapsed / realElapsed) : 1f;
            m_SimTimeAtSample = Time.time;
            m_RealTimeAtSample = Time.realtimeSinceStartup;
            m_FpsAccum = 0f; m_FpsFrames = 0f; m_FpsClock = 0f;
            if (IsWebBuild && IsServer && SessionActive && SimSpeed < 0.9f)
            {
                Debug.Log($"[Perf] host is falling behind: {Fps:0} fps, simulation at {SimSpeed * 100f:0}% speed, {Players.Count} players, {NpcCount} runners");
            }
        }

        void ServerTick(float dt)
        {
            float t = Net.TimeRemaining.Value - dt;
            if (t <= 0f)
            {
                Net.TimeRemaining.Value = 0f;
                foreach (var p in Players) if (p.State == GameState.Playing) p.SetState(GameState.TimedOut);
                Net.RoundRunning.Value = false;
                return;
            }
            Net.TimeRemaining.Value = t;

            PhaseElapsed += dt;
            if (!m_PhaseLocked)
            {
                m_PhaseTimeLeft -= dt;
                if (m_PhaseTimeLeft <= 0f)
                {
                    if (Phase == LightPhase.Green) SetPhase(LightPhase.Red, Range(RedDuration));
                    else SetPhase(LightPhase.Green, Range(GreenDuration));
                }
            }

            if (IsRedJudging)
            {
                foreach (var p in Players)
                {
                    if (p.State != GameState.Playing) continue;
                    if (p.Ragdoll.Speed > MoveThreshold)
                    {
                        p.MovingTime += dt;
                        if (p.MovingTime >= MoveTolerance) p.SetState(GameState.Eliminated);
                    }
                    else
                    {
                        p.MovingTime = 0f;
                    }
                }
            }
        }

        float Range(Vector2 minMax) => Mathf.Lerp(minMax.x, minMax.y, (float)m_Rng.NextDouble());

        void SetPhase(LightPhase phase, float duration)
        {
            if (Net != null) Net.Phase.Value = (int)phase;
            m_PhaseTimeLeft = duration;
            PhaseElapsed = 0f;
            foreach (var p in Players) p.MovingTime = 0f;
            if (Doll != null) Doll.SetRed(phase == LightPhase.Red);
        }

        /// <summary>Server: hold a phase indefinitely (tests, debugging).</summary>
        public void ForcePhase(LightPhase phase)
        {
            if (!IsServer) return;
            SetPhase(phase, float.PositiveInfinity);
            m_PhaseLocked = true;
        }

        public void ReleasePhase()
        {
            m_PhaseLocked = false;
            SetPhase(Phase, Range(Phase == LightPhase.Green ? GreenDuration : RedDuration));
        }

        /// <summary>Called by the finish trigger. Server decides who won.</summary>
        public void OnFinishReached(Rigidbody body)
        {
            if (!IsServer || body == null || !RoundRunning) return;
            foreach (var p in Players)
            {
                if (p.State != GameState.Playing || p.Ragdoll.Bodies == null) continue;
                if (System.Array.IndexOf(p.Ragdoll.Bodies, body) >= 0) { p.SetState(GameState.Won); return; }
            }
        }

        void BatchLog()
        {
            m_LogClock += Time.deltaTime;
            if (m_LogClock < 2f) return;
            m_LogClock = 0f;
            string me = Player != null ? $"me z={Player.Position.z:0.00} y={Player.Pelvis.position.y:0.00}" : "me: none";
            float npcZ = 0f;
            foreach (var n in NpcRagdolls) npcZ += n.Ragdoll.Position.z;
            if (NpcRagdolls.Count > 0) npcZ /= NpcRagdolls.Count;
            Debug.Log($"[NetClient] t={Time.time:0.0} phase={Phase} time={TimeRemaining:0} players={Players.Count} npcs={NpcRagdolls.Count} active={NpcActiveCount} npcAvgZ={npcZ:0.00} {me} state={State}");
        }

        /// <summary>Client and server: which doll does this trigger-touching rigidbody belong to, if any player.</summary>
        public NetworkRagdoll FindPlayerByBody(Rigidbody body)
        {
            foreach (var p in Players) if (p.Ragdoll.Bodies != null && System.Array.IndexOf(p.Ragdoll.Bodies, body) >= 0) return p;
            return null;
        }
    }
}
