using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace ValheimPortalList
{
	[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
	[BepInDependency(Jotunn.Main.ModGuid)]
	[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
	public sealed class ValheimPortalListPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "sonicdm.valheimportallist";
		public const string PluginName = "Valheim Portal List";
		public const string PluginVersion = "1.2.2";

		internal static ManualLogSource ModLogger;
		internal static ConfigEntry<bool> AutoPin;
		internal static ConfigEntry<float> ApproachRangeMeters;
		internal static ConfigEntry<KeyCode> ToggleKey;
		internal static ConfigEntry<bool> DebugLogging;

		internal static bool DebugEnabled => DebugLogging != null && DebugLogging.Value;

		private Harmony _harmony;
		private bool _subscribedGui;
		private bool _commandRegistered;

		private void Awake()
		{
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
			ToggleKey = Config.Bind("UI", "ToggleKey", KeyCode.P,
				"Key to toggle the Portals panel (opens the large map when needed).");

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
				Logger.LogInfo($"{PluginName} {PluginVersion} headless: CSV dump + admin RPC only.");
				Debug("DebugLogging enabled.");
				return;
			}

			GUIManager.OnCustomGUIAvailable += OnCustomGuiAvailable;
			_subscribedGui = true;
			Logger.LogInfo($"{PluginName} {PluginVersion} loaded (journal + map panel).");
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
			PortalScan.TickHostAutoDump();

			if (GUIManager.IsHeadless())
				return;

			if (!_commandRegistered)
				TryRegisterCommands();

			KnownPortalCache.Tick();
			if (PortalMapPins.HasPendingAutoPins)
				PortalMapPins.TickPendingAutoPins();
			PortalPanelUi.Tick();

			if (ToggleKey != null && Input.GetKeyDown(ToggleKey.Value) && !IsHotkeyBlocked())
			{
				Debug($"ToggleKey {ToggleKey.Value} — panel wasOpen={PortalPanelUi.IsOpen}");
				if (!PortalPanelUi.IsOpen)
					PortalMapPins.OpenLargeMap();
				PortalPanelUi.Toggle();
			}
		}

		private void OnDestroy()
		{
			KnownPortalCache.FlushIfDirty();
			if (_subscribedGui)
			{
				GUIManager.OnCustomGUIAvailable -= OnCustomGuiAvailable;
				_subscribedGui = false;
			}

			PortalPanelUi.DestroyUi();
			PortalMapPins.ClearOverlay();
			_harmony?.UnpatchSelf();
		}

		private static void OnCustomGuiAvailable()
		{
			PortalPanelUi.OnGuiReady();
		}

		private void TryRegisterCommands()
		{
			try
			{
				new Terminal.ConsoleCommand(
					"portals",
					"Toggle the Valheim Portal List panel",
					args =>
					{
						Debug("Command: portals");
						PortalMapPins.OpenLargeMap();
						PortalPanelUi.Toggle();
					},
					false, false, false, false, false, false, null, false, false, false);

				new Terminal.ConsoleCommand(
					"portallist",
					"Export every portal to CSV under BepInEx/cache/ValheimPortalList (admin / host+devcommands)",
					args =>
					{
						Debug($"Command: portallist canRefresh={PortalAccess.CanRefreshWorld()} isServer={ZNet.instance != null && ZNet.instance.IsServer()}");
						if (!PortalAccess.CanRefreshWorld() && !(ZNet.instance != null && ZNet.instance.IsServer()))
						{
							args.Context.AddString("ValheimPortalList: need adminlist or host with devcommands.");
							return;
						}

						if (ZNet.instance != null && ZNet.instance.IsServer())
						{
							PortalDumpResult result = PortalScan.DumpPortals();
							args.Context.AddString($"ValheimPortalList: wrote {result.Rows.Count} portal(s) to {result.CsvPath}");
						}
						else
						{
							args.Context.AddString("ValheimPortalList: run portallist on the host, or use Refresh world in the panel.");
						}
					},
					false, false, false, false, false, false, null, false, false, true);

				_commandRegistered = true;
				Logger.LogInfo("Registered commands: portals, portallist");
			}
			catch
			{
			}
		}

		/// <summary>
		/// Ignore the toggle key while typing in chat, console, rename prompts, or our filter box.
		/// </summary>
		private static bool IsHotkeyBlocked()
		{
			try
			{
				if (TextInput.IsVisible())
					return true;
			}
			catch
			{
			}

			try
			{
				Chat chat = Chat.instance;
				if ((UnityEngine.Object)chat != null && (chat.HasFocus() || chat.IsChatDialogWindowVisible()))
					return true;
			}
			catch
			{
			}

			try
			{
				if (Console.IsVisible())
					return true;
			}
			catch
			{
			}

			if (PortalPanelUi.IsFilterFocused)
				return true;

			return false;
		}
	}
}
