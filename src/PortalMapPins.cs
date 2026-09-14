using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace PortalAtlas
{
	/// <summary>
	/// All pin create/remove goes through here. Manual player pins are never touched:
	/// we only RemovePin on references we created, or map pins whose name carries our private marker.
	/// </summary>
	internal static class PortalMapPins
	{
		/// <summary>Zero-width space prefix — invisible in the UI, identifies pins we created.</summary>
		private const string OwnedMarker = "\u200b";

		private static readonly List<object> OverlayPins = new List<object>();
		private static readonly List<object> SavedPins = new List<object>();
		private static readonly List<KnownPortalEntry> _pendingAutoPins = new List<KnownPortalEntry>();

		internal static Color OverlayTint = new Color(1f, 0.55f, 0.1f, 1f);

		internal static void ClearOverlay()
		{
			int count = OverlayPins.Count;
			RemoveTrackedPins(OverlayPins);
			if (count > 0)
				PortalAtlasPlugin.Debug($"ClearOverlay removed {count} temp pin(s)");
		}

		internal static void ShowOverlay(IEnumerable<PortalRow> rows)
		{
			ClearOverlay();
			if (rows == null)
				return;

			Minimap map = Minimap.instance;
			Player player = Player.m_localPlayer;
			if ((UnityEngine.Object)map == null || (UnityEngine.Object)player == null)
			{
				PortalAtlasPlugin.Debug("ShowOverlay skipped — minimap/player missing");
				return;
			}

			object pinType = GetPortalPinType();
			long playerId = player.GetPlayerID();
			int added = 0;
			int skipped = 0;

			foreach (PortalRow row in rows)
			{
				Vector3 pos = new Vector3(row.X, row.Y, row.Z);
				string display = row.DisplayTag;
				if (HasPermanentPinNamedNear(display, pos, 1.5f))
				{
					skipped++;
					continue;
				}

				object pin = AddMinimapPin(map, pos, pinType, ToOwnedName(display), playerId, save: false);
				if (pin == null)
					continue;
				OverlayPins.Add(pin);
				added++;
			}

			ForceUpdatePins(map);
			RetintOverlay();
			PortalAtlasPlugin.Debug($"ShowOverlay placed {added} temp pin(s), skipped {skipped} (permanent already there)");
		}

		internal static void RetintOverlay()
		{
			foreach (object pin in OverlayPins)
				TryTintPin(pin, OverlayTint);
		}

		internal static void UpsertSavedPin(KnownPortalEntry entry)
		{
			if (entry == null)
				return;

			Minimap map = Minimap.instance;
			Player player = Player.m_localPlayer;
			if ((UnityEngine.Object)map == null || (UnityEngine.Object)player == null)
			{
				PortalAtlasPlugin.Debug(
					$"Auto-pin deferred — minimap/player missing for tag='{entry.Tag}'");
				QueuePendingAutoPin(entry);
				return;
			}

			Vector3 pos = new Vector3(entry.X, entry.Y, entry.Z);
			string display = string.IsNullOrEmpty(entry.Tag) ? "(untagged)" : entry.Tag;

			// Skip if a permanent pin (manual or our saved auto-pin) already covers this portal.
			if (HasPermanentPinNamedNear(display, pos, 1.5f))
			{
				PortalAtlasPlugin.Debug(
					$"Auto-pin skipped — permanent pin '{display}' already near ({pos.x:0.#},{pos.z:0.#})");
				return;
			}

			RemoveOwnedNear(pos, 1.5f);

			object pinType = GetPortalPinType();
			object pin = AddMinimapPin(map, pos, pinType, ToOwnedName(display), player.GetPlayerID(), save: true);
			if (pin != null)
			{
				SavedPins.Add(pin);
				ForceUpdatePins(map);
				PortalAtlasPlugin.Debug(
					$"Auto-pin placed tag='{display}' prefab={entry.Prefab} at ({pos.x:0.#},{pos.z:0.#})");
			}
			else
			{
				PortalAtlasPlugin.Debug(
					$"Auto-pin failed — AddPin returned null for tag='{display}'");
				QueuePendingAutoPin(entry);
			}
		}

		/// <summary>Retry auto-pins that failed while the minimap was unavailable.</summary>
		internal static void TickPendingAutoPins()
		{
			if (_pendingAutoPins.Count == 0)
				return;
			if ((UnityEngine.Object)Minimap.instance == null || (UnityEngine.Object)Player.m_localPlayer == null)
				return;

			List<KnownPortalEntry> copy = new List<KnownPortalEntry>(_pendingAutoPins);
			_pendingAutoPins.Clear();
			foreach (KnownPortalEntry entry in copy)
				UpsertSavedPin(entry);
		}

		internal static bool HasPendingAutoPins => _pendingAutoPins.Count > 0;

		/// <summary>
		/// Explicit Pin from the panel list — creates a saved map pin for this portal.
		/// </summary>
		internal static bool TryPinPortal(PortalRow row, out string message)
		{
			message = "Could not pin.";
			if (row == null)
				return false;

			KnownPortalEntry entry = new KnownPortalEntry
			{
				Uid = row.Uid ?? string.Empty,
				Prefab = row.Prefab ?? string.Empty,
				Tag = row.Tag ?? string.Empty,
				X = row.X,
				Y = row.Y,
				Z = row.Z,
				TargetUid = row.TargetUid ?? string.Empty,
				TargetX = row.TargetX,
				TargetY = row.TargetY,
				TargetZ = row.TargetZ,
				LastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
			};

			string display = string.IsNullOrEmpty(entry.Tag) ? "(untagged)" : entry.Tag;
			Vector3 pos = new Vector3(entry.X, entry.Y, entry.Z);

			if (HasPermanentPinNamedNear(display, pos, 1.5f))
			{
				message = $"Already pinned: {display}";
				return false;
			}

			Minimap map = Minimap.instance;
			Player player = Player.m_localPlayer;
			if ((UnityEngine.Object)map == null || (UnityEngine.Object)player == null)
			{
				QueuePendingAutoPin(entry);
				message = $"Pin queued for {display} (map not ready yet).";
				return true;
			}

			RemoveOwnedNear(pos, 1.5f);
			object pinType = GetPortalPinType();
			object pin = AddMinimapPin(map, pos, pinType, ToOwnedName(display), player.GetPlayerID(), save: true);
			if (pin == null)
			{
				QueuePendingAutoPin(entry);
				message = $"Pin queued for {display} (AddPin failed).";
				return false;
			}

			SavedPins.Add(pin);
			ForceUpdatePins(map);
			message = $"Pinned {display}.";
			PortalAtlasPlugin.Debug($"TryPinPortal ok tag='{display}' at ({pos.x:0.#},{pos.z:0.#})");
			return true;
		}

		/// <summary>
		/// Removes only pins this mod created (in-memory refs and any map pins with our name marker).
		/// Never removes a pin that lacks the ownership marker.
		/// </summary>
		internal static void ClearSavedPins()
		{
			int tracked = SavedPins.Count;
			RemoveTrackedPins(SavedPins);
			RemoveAllOwnedSavedPinsFromMap();
			PortalAtlasPlugin.Debug($"ClearSavedPins trackedRefs={tracked} (manual pins untouched)");
		}

		/// <summary>
		/// Open/ensure large map, center on the point, then place the player ping.
		/// </summary>
		internal static void Ping(Vector3 position)
		{
			OpenLargeMap();
			CenterOn(position);

			try
			{
				if (Chat.instance != null)
				{
					MethodInfo send = typeof(Chat).GetMethod("SendPing",
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (send != null)
						send.Invoke(Chat.instance, new object[] { position });
				}
			}
			catch
			{
			}

			PortalAtlasPlugin.Debug($"Ping + center map at ({position.x:0.#},{position.z:0.#})");
		}

		/// <summary>Move the large map view to a world position (no ping).</summary>
		internal static void CenterOn(Vector3 position)
		{
			Minimap map = Minimap.instance;
			if ((UnityEngine.Object)map == null)
				return;

			OpenLargeMap();

			// Vanilla "focus this world point on the large map" — preferred.
			try
			{
				MethodInfo showPoint = typeof(Minimap).GetMethod("ShowPointOnMap",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null, new[] { typeof(Vector3) }, null);
				if (showPoint != null)
				{
					showPoint.Invoke(map, new object[] { position });
					return;
				}
			}
			catch
			{
			}

			try
			{
				MethodInfo center = typeof(Minimap).GetMethod("CenterMap",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null, new[] { typeof(Vector3) }, null);
				if (center != null)
					center.Invoke(map, new object[] { position });
			}
			catch
			{
			}
		}

		internal static void OpenLargeMap()
		{
			Minimap map = Minimap.instance;
			if ((UnityEngine.Object)map == null)
				return;

			try
			{
				MethodInfo setMode = typeof(Minimap).GetMethod("SetMapMode",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (setMode != null)
				{
					Type modeType = typeof(Minimap).GetNestedType("MapMode", BindingFlags.Public | BindingFlags.NonPublic);
					if (modeType != null)
					{
						object large = Enum.Parse(modeType, "Large", true);
						setMode.Invoke(map, new[] { large });
						return;
					}
				}
			}
			catch
			{
			}

			try
			{
				MethodInfo show = typeof(Minimap).GetMethod("ShowMap",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null, Type.EmptyTypes, null);
				if (show != null)
					show.Invoke(map, null);
			}
			catch
			{
			}
		}

		internal static bool IsLargeMapOpen()
		{
			Minimap map = Minimap.instance;
			if ((UnityEngine.Object)map == null)
				return false;

			try
			{
				MethodInfo method = typeof(Minimap).GetMethod("IsOpen",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null, Type.EmptyTypes, null);
				if (method != null)
					return (bool)method.Invoke(map, null);
			}
			catch
			{
			}

			try
			{
				FieldInfo large = typeof(Minimap).GetField("m_largeRoot",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (large != null)
				{
					GameObject go = large.GetValue(map) as GameObject;
					return (UnityEngine.Object)go != null && go.activeInHierarchy;
				}
			}
			catch
			{
			}

			return false;
		}

		internal static Transform GetMapPanelRoot()
		{
			Minimap map = Minimap.instance;
			if ((UnityEngine.Object)map == null)
				return null;

			// Prefer the parchment panel, not the fullscreen large-root overlay.
			foreach (string name in new[] { "m_mapLarge", "m_largeRoot" })
			{
				FieldInfo field = typeof(Minimap).GetField(name,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field == null)
					continue;
				object value = field.GetValue(map);
				if (value is GameObject go && (UnityEngine.Object)go != null)
					return go.transform;
				if (value is Transform t && (UnityEngine.Object)t != null)
					return t;
				if (value is Component c && (UnityEngine.Object)c != null)
					return c.transform;
			}

			return GetLargeMapRoot();
		}

		internal static Transform GetLargeMapRoot()
		{
			Minimap map = Minimap.instance;
			if ((UnityEngine.Object)map == null)
				return null;

			string[] names = { "m_mapLarge", "m_largeRoot", "m_mapRoot", "m_largeMap" };
			foreach (string name in names)
			{
				FieldInfo field = typeof(Minimap).GetField(name,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field == null)
					continue;
				object value = field.GetValue(map);
				if (value is GameObject go)
					return go.transform;
				if (value is Transform t)
					return t;
				if (value is Component c)
					return c.transform;
			}

			return map.transform;
		}

		internal static bool TryMapClickToWorld(out Vector3 world)
		{
			world = Vector3.zero;
			Minimap map = Minimap.instance;
			if ((UnityEngine.Object)map == null)
				return false;

			try
			{
				MethodInfo screenToWorld = typeof(Minimap).GetMethod("ScreenToWorldPoint",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (screenToWorld != null)
				{
					object result = screenToWorld.Invoke(map, new object[] { Input.mousePosition });
					if (result is Vector3 v)
					{
						world = v;
						return true;
					}
				}
			}
			catch
			{
			}

			return false;
		}

		private static string ToOwnedName(string displayTag)
		{
			return OwnedMarker + (displayTag ?? string.Empty);
		}

		private static bool IsOwnedPin(object pin)
		{
			string name = GetPinName(pin);
			return !string.IsNullOrEmpty(name) && name.StartsWith(OwnedMarker, StringComparison.Ordinal);
		}

		/// <summary>
		/// True if a permanent map pin (player manual, or this mod's saved auto-pin) already
		/// uses this portal name near the position. Temp overlay pins are ignored.
		/// </summary>
		private static bool HasPermanentPinNamedNear(string displayName, Vector3 pos, float radius)
		{
			if (string.IsNullOrEmpty(displayName))
				return false;

			foreach (object pin in SnapshotMapPins())
			{
				if (pin == null)
					continue;

				// Ignore unsaved overlay pins (ours or otherwise temporary).
				if (IsOwnedPin(pin) && IsPinSaveFlag(pin) == false)
					continue;

				string name = GetDisplayPinName(pin);
				if (string.IsNullOrEmpty(name))
					continue;
				if (!string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase))
					continue;

				Vector3? pinPos = GetPinPos(pin);
				if (!pinPos.HasValue)
					continue;
				if (Vector3.Distance(pinPos.Value, pos) <= radius)
					return true;
			}

			return false;
		}

		private static string GetDisplayPinName(object pin)
		{
			string name = GetPinName(pin);
			if (string.IsNullOrEmpty(name))
				return string.Empty;
			if (name.StartsWith(OwnedMarker, StringComparison.Ordinal))
				return name.Substring(OwnedMarker.Length);
			return name;
		}

		private static void QueuePendingAutoPin(KnownPortalEntry entry)
		{
			if (entry == null || string.IsNullOrEmpty(entry.Uid))
				return;
			for (int i = 0; i < _pendingAutoPins.Count; i++)
			{
				if (string.Equals(_pendingAutoPins[i].Uid, entry.Uid, StringComparison.OrdinalIgnoreCase))
				{
					_pendingAutoPins[i] = entry;
					return;
				}
			}
			_pendingAutoPins.Add(entry);
		}

		private static void RemoveOwnedNear(Vector3 pos, float radius)
		{
			// In-memory refs from this session.
			for (int i = SavedPins.Count - 1; i >= 0; i--)
			{
				object pin = SavedPins[i];
				if (!IsOwnedPin(pin))
				{
					// Should never happen; drop the ref without removing from the map.
					SavedPins.RemoveAt(i);
					continue;
				}

				Vector3? pinPos = GetPinPos(pin);
				if (!pinPos.HasValue)
					continue;
				if (Vector3.Distance(pinPos.Value, pos) <= radius)
				{
					RemoveOneIfOwned(pin);
					SavedPins.RemoveAt(i);
				}
			}

			// After relog, also find owned pins on the map by marker + position — never unmarked.
			foreach (object pin in SnapshotMapPins())
			{
				if (!IsOwnedPin(pin))
					continue;
				Vector3? pinPos = GetPinPos(pin);
				if (!pinPos.HasValue)
					continue;
				if (Vector3.Distance(pinPos.Value, pos) <= radius)
					RemoveOneIfOwned(pin);
			}
		}

		private static void RemoveAllOwnedSavedPinsFromMap()
		{
			foreach (object pin in SnapshotMapPins())
			{
				if (!IsOwnedPin(pin))
					continue;
				// Overlay pins are unsaved; ClearSaved targets permanent ones only when possible.
				if (IsPinSaveFlag(pin) == false)
					continue;
				RemoveOneIfOwned(pin);
			}
		}

		private static List<object> SnapshotMapPins()
		{
			List<object> result = new List<object>();
			Minimap map = Minimap.instance;
			if ((UnityEngine.Object)map == null)
				return result;

			try
			{
				FieldInfo pinsField = typeof(Minimap).GetField("m_pins",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				object list = pinsField != null ? pinsField.GetValue(map) : null;
				if (list is IEnumerable enumerable)
				{
					foreach (object pin in enumerable)
					{
						if (pin != null)
							result.Add(pin);
					}
				}
			}
			catch
			{
			}

			return result;
		}

		private static bool? IsPinSaveFlag(object pin)
		{
			if (pin == null)
				return null;
			FieldInfo save = pin.GetType().GetField("m_save", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (save == null || save.FieldType != typeof(bool))
				return null;
			return (bool)save.GetValue(pin);
		}

		private static string GetPinName(object pin)
		{
			if (pin == null)
				return null;
			FieldInfo field = pin.GetType().GetField("m_name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
				return null;
			return field.GetValue(pin) as string;
		}

		private static Vector3? GetPinPos(object pin)
		{
			if (pin == null)
				return null;
			FieldInfo field = pin.GetType().GetField("m_pos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
				return null;
			object value = field.GetValue(pin);
			return value is Vector3 v ? (Vector3?)v : null;
		}

		private static void RemoveTrackedPins(List<object> pins)
		{
			for (int i = pins.Count - 1; i >= 0; i--)
			{
				RemoveOneIfOwned(pins[i]);
				pins.RemoveAt(i);
			}
			pins.Clear();
		}

		/// <summary>Hard gate: refuse to remove anything that is not clearly ours.</summary>
		private static void RemoveOneIfOwned(object pin)
		{
			if (pin == null || !IsOwnedPin(pin))
				return;

			Minimap map = Minimap.instance;
			if ((UnityEngine.Object)map == null)
				return;

			try
			{
				map.RemovePin(pin as Minimap.PinData);
			}
			catch
			{
			}
		}

		private static void TryTintPin(object pin, Color color)
		{
			if (pin == null || !IsOwnedPin(pin))
				return;

			Type type = pin.GetType();
			try
			{
				foreach (string fieldName in new[] { "m_icon", "m_iconElement", "m_uiElement", "m_pinIcon" })
				{
					FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (field == null)
						continue;
					object value = field.GetValue(pin);
					if (TintGraphic(value, color))
						return;
				}
			}
			catch
			{
			}
		}

		private static bool TintGraphic(object value, Color color)
		{
			if (value == null)
				return false;

			if (value is UnityEngine.UI.Image image)
			{
				image.color = color;
				return true;
			}

			if (value is Component component)
			{
				UnityEngine.UI.Image[] images = component.GetComponentsInChildren<UnityEngine.UI.Image>(true);
				if (images != null && images.Length > 0)
				{
					foreach (UnityEngine.UI.Image img in images)
					{
						if ((UnityEngine.Object)img != null)
							img.color = color;
					}
					return true;
				}
			}

			if (value is GameObject go)
			{
				UnityEngine.UI.Image[] images = go.GetComponentsInChildren<UnityEngine.UI.Image>(true);
				if (images != null && images.Length > 0)
				{
					foreach (UnityEngine.UI.Image img in images)
					{
						if ((UnityEngine.Object)img != null)
							img.color = color;
					}
					return true;
				}
			}

			return false;
		}

		private static void ForceUpdatePins(Minimap map)
		{
			if ((UnityEngine.Object)map == null)
				return;

			foreach (string name in new[] { "UpdatePins", "UpdateDynamicPins" })
			{
				try
				{
					MethodInfo method = typeof(Minimap).GetMethod(name,
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (method == null)
						continue;

					ParameterInfo[] parameters = method.GetParameters();
					if (parameters.Length == 0)
					{
						method.Invoke(map, null);
						return;
					}

					if (parameters.Length == 1 && parameters[0].ParameterType == typeof(float))
					{
						method.Invoke(map, new object[] { 0f });
						return;
					}
				}
				catch
				{
				}
			}
		}

		private static object GetPortalPinType()
		{
			Type pinType = typeof(Minimap).GetNestedType("PinType", BindingFlags.Public | BindingFlags.NonPublic);
			if (pinType == null || !pinType.IsEnum)
				return 4;

			foreach (string name in new[] { "Portal", "Icon4" })
			{
				try { return Enum.Parse(pinType, name, true); }
				catch { }
			}

			return Enum.ToObject(pinType, 4);
		}

		private static object AddMinimapPin(Minimap map, Vector3 pos, object pinType, string name, long playerId, bool save)
		{
			if ((UnityEngine.Object)map == null)
				return null;

			foreach (MethodInfo method in typeof(Minimap).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (!string.Equals(method.Name, "AddPin", StringComparison.Ordinal))
					continue;

				ParameterInfo[] parameters = method.GetParameters();
				if (parameters.Length < 5)
					continue;

				try
				{
					object[] args = new object[parameters.Length];
					args[0] = pos;
					args[1] = pinType;
					args[2] = name ?? string.Empty;
					args[3] = save;
					args[4] = false;
					if (parameters.Length > 5 && (parameters[5].ParameterType == typeof(long) || parameters[5].ParameterType == typeof(int)))
						args[5] = Convert.ChangeType(playerId, parameters[5].ParameterType, CultureInfo.InvariantCulture);

					for (int i = 0; i < args.Length; i++)
					{
						if (args[i] != null)
							continue;
						Type t = parameters[i].ParameterType;
						args[i] = t.IsValueType ? Activator.CreateInstance(t) : null;
					}

					return method.Invoke(map, args);
				}
				catch
				{
				}
			}

			return null;
		}
	}
}
