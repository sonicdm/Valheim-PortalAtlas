using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;

namespace PortalAtlas
{
	/// <summary>
	/// One DLL for clients and dedicated. Jötunn is required on both (headless skips UI).
	/// Dedicated Refresh admin is Server Devcommands on the client; host runs the scan RPC.
	/// </summary>
	[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
	[BepInDependency(Jotunn.Main.ModGuid)]
	[BepInDependency(ServerDevcommandsAccess.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
	public sealed class PortalAtlasPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "sonicdm.valheimportallist";
		public const string PluginName = "Portal Atlas";
		public const string PluginVersion = "1.2.6";

		internal static ManualLogSource ModLogger;
		internal static PortalAtlasPlugin Instance;
		internal static ConfigEntry<bool> AutoPin;
		internal static ConfigEntry<float> ApproachRangeMeters;
		internal static ConfigEntry<bool> DebugLogging;

		internal static bool DebugEnabled => DebugLogging != null && DebugLogging.Value;

		private Harmony _harmony;
		private bool _commandRegistered;
		private bool _uiEnabled;
		private bool _subscribedGui;
		private bool _subscribedSdc;
		private bool _subscribedHandshake;

		private void Awake()
		{
			Instance = this;
			ModLogger = Logger;
			PortalScan.BindConfig(Config);
			DebugLogging = Config.Bind("General", "DebugLogging", false,
				"When true, write detailed [DEBUG] lines for all mod systems to BepInEx/LogOutput.log (journal, approach, pins, UI, refresh RPC, access, scans).");
			AutoPin = Config.Bind("Pins", "AutoPinOnApproach", false,
				"When enabled, approaching or using a portal also creates a saved map pin (DudeWheresMyPortal-style).");
			ApproachRangeMeters = Config.Bind("Pins", "ApproachRangeMeters", 8f,
				new ConfigDescription(
					"How close you must be (meters) for a loaded portal to be recorded in the journal (and auto-pinned if AutoPinOnApproach is on).",
					new AcceptableValueRange<float>(1f, 50f)));

			_harmony = new Harmony(PluginGuid);
			try
			{
				_harmony.PatchAll();
			}
			catch (Exception ex)
			{
				Logger.LogError("Harmony patch failed.");
				Logger.LogError(ex);
			}

			if (GUIManager.IsHeadless())
			{
				Logger.LogInfo($"{PluginName} {PluginVersion} headless: CSV dump + admin Refresh RPC.");
				Debug("DebugLogging enabled.");
				return;
			}

			_uiEnabled = true;
			GUIManager.OnCustomGUIAvailable += OnCustomGuiAvailable;
			_subscribedGui = true;
			ServerDevcommandsAccess.Subscribe(OnAdminStatusChanged);
			_subscribedSdc = true;
			PortalRpc.OnHandshakeChanged += OnHandshakeChanged;
			_subscribedHandshake = true;
			Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
			Debug("DebugLogging enabled.");
		}

		internal static void Debug(string message)
		{
			if (!DebugEnabled || ModLogger == null)
				return;
			ModLogger.LogInfo("[DEBUG] " + message);
		}

		private void Update()
		{
			PortalRpc.TickRegister();
			PortalRpc.TickHandshake();
			PortalScan.TickHostAutoDump();

			if (GUIManager.IsHeadless())
				return;

			if (!_commandRegistered)
				TryRegisterCommands();

			if (!_uiEnabled)
				return;

			KnownPortalCache.Tick();
			if (PortalMapPins.HasPendingAutoPins)
				PortalMapPins.TickPendingAutoPins();
			PortalPanelUi.Tick();
		}

		private void OnDestroy()
		{
			if (Instance == this)
				Instance = null;

			if (_subscribedGui)
			{
				GUIManager.OnCustomGUIAvailable -= OnCustomGuiAvailable;
				_subscribedGui = false;
			}

			if (_subscribedSdc)
			{
				ServerDevcommandsAccess.Unsubscribe();
				_subscribedSdc = false;
			}

			if (_subscribedHandshake)
			{
				PortalRpc.OnHandshakeChanged -= OnHandshakeChanged;
				_subscribedHandshake = false;
			}

			if (_uiEnabled)
			{
				PortalPanelUi.DestroyUi();
				PortalMapPins.ClearOverlay();
			}

			_harmony?.UnpatchSelf();
		}

		private static void OnCustomGuiAvailable()
		{
			PortalPanelUi.OnGuiReady();
		}

		private static void OnAdminStatusChanged()
		{
			Debug($"OnAdminStatusChanged ({PortalAccess.DescribeRefreshAccess()})");
			PortalPanelUi.OnAdminStatusChanged();
		}

		private static void OnHandshakeChanged()
		{
			Debug($"OnHandshakeChanged ({PortalAccess.DescribeRefreshAccess()})");
			PortalPanelUi.OnAdminStatusChanged();
		}

		private void TryRegisterCommands()
		{
			try
			{
				new Terminal.ConsoleCommand(
					"portallist",
					"Export every portal to CSV under BepInEx/cache/PortalAtlas (host, or dedicated admin via Server Devcommands)",
					args =>
					{
						Debug($"Command: portallist canRefresh={PortalAccess.CanRefreshWorld()} isServer={ZNet.instance != null && ZNet.instance.IsServer()}");
						if (!PortalAccess.CanRefreshWorld() && !(ZNet.instance != null && ZNet.instance.IsServer()))
						{
							args.Context.AddString("Portal Atlas: need Server Devcommands admin (dedicated) or be the world host.");
							return;
						}

						if (ZNet.instance != null && ZNet.instance.IsServer())
						{
							PortalDumpResult result = PortalScan.DumpPortals();
							args.Context.AddString($"Portal Atlas: wrote {result.Rows.Count} portal(s) to {result.CsvPath}");
						}
						else
						{
							args.Context.AddString("Portal Atlas: run portallist on the host, or use Refresh world in the panel.");
						}
					},
					false, false, false, false, false, false, null, false, false, true);

				new Terminal.ConsoleCommand(
					"portals",
					"Toggle the Portal Atlas panel",
					args =>
					{
						Debug("Command: portals");
						PortalMapPins.OpenLargeMap();
						PortalPanelUi.Toggle();
					},
					false, false, false, false, false, false, null, false, false, false);

				Logger.LogInfo("Registered commands: portals, portallist");
				_commandRegistered = true;
			}
			catch
			{
			}
		}
	}
}
