using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ValheimPortalList
{
	internal static class KnownPortalCache
	{
		private const float ApproachPollNearSeconds = 1.5f;
		private const float ApproachPollFarSeconds = 3.5f;
		private const float SaveDebounceSeconds = 4f;
		private const float MoveThresholdMeters = 1.25f;

		private static float ApproachRange
		{
			get
			{
				if (ValheimPortalListPlugin.ApproachRangeMeters == null)
					return 8f;
				return Mathf.Clamp(ValheimPortalListPlugin.ApproachRangeMeters.Value, 1f, 50f);
			}
		}

		private static readonly Dictionary<string, KnownPortalEntry> Entries =
			new Dictionary<string, KnownPortalEntry>(StringComparer.OrdinalIgnoreCase);

		private static string _worldId = string.Empty;
		private static string _characterId = string.Empty;
		private static float _nextPoll;
		private static float _earliestSaveTime;
		private static bool _dirty;
		private static Vector3 _lastScanPlayerPos;
		private static bool _hasLastScanPos;
		private static int _lastNearbyCount;

		internal static IReadOnlyCollection<KnownPortalEntry> All => Entries.Values;

		internal static void Tick()
		{
			if (Player.m_localPlayer == null || ZNet.instance == null)
				return;

			float now = Time.unscaledTime;

			if (_dirty && now >= _earliestSaveTime)
				Save();

			if (now < _nextPoll)
				return;

			float interval = _lastNearbyCount > 0 ? ApproachPollNearSeconds : ApproachPollFarSeconds;
			_nextPoll = now + interval;

			Player player = Player.m_localPlayer;
			Vector3 pos = player.transform.position;
			if (_hasLastScanPos && _lastNearbyCount == 0)
			{
				float mdx = pos.x - _lastScanPlayerPos.x;
				float mdz = pos.z - _lastScanPlayerPos.z;
				if (mdx * mdx + mdz * mdz < MoveThresholdMeters * MoveThresholdMeters)
				{
					// Standing still with nothing nearby — skip GetPortalList this cycle.
					_nextPoll = now + ApproachPollFarSeconds;
					return;
				}
			}

			EnsureLoaded();
			ScanNearby();
			_lastScanPlayerPos = pos;
			_hasLastScanPos = true;

			if (_dirty && _earliestSaveTime <= 0f)
				_earliestSaveTime = now + SaveDebounceSeconds;
		}

		/// <summary>Flush pending journal writes (panel hide / unload).</summary>
		internal static void FlushIfDirty()
		{
			if (_dirty)
				Save();
		}

		internal static void EnsureLoaded()
		{
			string worldId = GetWorldId();
			string characterId = GetCharacterId();
			if (string.Equals(worldId, _worldId, StringComparison.Ordinal) &&
			    string.Equals(characterId, _characterId, StringComparison.Ordinal) &&
			    !string.IsNullOrEmpty(_worldId))
			{
				return;
			}

			_worldId = worldId;
			_characterId = characterId;
			Entries.Clear();
			_dirty = false;

			string path = PortalPaths.JournalPath(_worldId, _characterId);
			if (!File.Exists(path))
				return;

			try
			{
				string json = File.ReadAllText(path, Encoding.UTF8);
				KnownPortalFile file = SimpleJson.Deserialize(json);
				if (file?.Portals == null)
					return;

				foreach (KnownPortalEntry entry in file.Portals)
				{
					if (entry == null || string.IsNullOrEmpty(entry.Uid))
						continue;
					Entries[entry.Uid] = entry;
				}

				int removed = CompactDuplicates();
				ValheimPortalListPlugin.ModLogger.LogInfo(
					$"Loaded {Entries.Count} known portal(s) from {path}");
				ValheimPortalListPlugin.Debug(
					$"Journal load world='{_worldId}' character='{_characterId}' count={Entries.Count} " +
					$"compacted={removed} path={path}");
				if (removed > 0)
					Save();
			}
			catch (Exception ex)
			{
				ValheimPortalListPlugin.ModLogger.LogWarning($"Failed to load portal journal: {ex.Message}");
			}
		}

		internal static void Save()
		{
			if (string.IsNullOrEmpty(_worldId))
				EnsureLoaded();

			string path = PortalPaths.JournalPath(_worldId, _characterId);
			try
			{
				KnownPortalFile file = new KnownPortalFile
				{
					WorldId = _worldId,
					CharacterId = _characterId,
					Portals = new List<KnownPortalEntry>(Entries.Values)
				};
				File.WriteAllText(path, SimpleJson.Serialize(file), new UTF8Encoding(false));
				_dirty = false;
				_earliestSaveTime = 0f;
				ValheimPortalListPlugin.Debug($"Journal save count={Entries.Count} path={path}");
			}
			catch (Exception ex)
			{
				ValheimPortalListPlugin.ModLogger.LogWarning($"Failed to save portal journal: {ex.Message}");
			}
		}

		internal static void RecordZdo(ZDO zdo, string prefabHint = null)
		{
			if (zdo == null || !zdo.IsValid())
				return;

			EnsureLoaded();

			Vector3 pos = zdo.GetPosition();
			string tag = PortalScan.SafeGetString(zdo, "tag");
			ZDOID targetId = PortalScan.GetPortalConnectionId(zdo);
			float? tx = null, ty = null, tz = null;
			string targetUid = targetId.ToString();

			try
			{
				if (!targetId.IsNone())
				{
					ZDO target = ZDOMan.instance.GetZDO(targetId);
					if (target != null && target.IsValid())
					{
						Vector3 tp = target.GetPosition();
						tx = tp.x;
						ty = tp.y;
						tz = tp.z;
					}
				}
			}
			catch
			{
			}

			string uid = zdo.m_uid.ToString();
			string prefab = prefabHint ?? string.Empty;
			if (string.IsNullOrEmpty(prefab))
			{
				try
				{
					ZNetView view = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
					if ((UnityEngine.Object)view != null)
						prefab = view.gameObject.name.Replace("(Clone)", string.Empty).Trim();
				}
				catch
				{
				}
			}

			if (string.IsNullOrEmpty(prefab))
				prefab = ResolvePrefabName(zdo);

			// Same tag at nearly the same spot → one journal entry (UID can change after rebuilds).
			int merged = MergeAwayNearbyDuplicates(uid, tag, pos);

			KnownPortalEntry existing;
			bool isNew = !Entries.TryGetValue(uid, out existing);

			KnownPortalEntry entry = new KnownPortalEntry
			{
				Uid = uid,
				Prefab = prefab,
				Tag = tag,
				X = pos.x,
				Y = pos.y,
				Z = pos.z,
				TargetUid = targetUid,
				TargetX = tx,
				TargetY = ty,
				TargetZ = tz,
				LastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
			};

			bool changed = isNew || merged > 0 || !EntriesMatch(existing, entry);
			if (!changed)
				return;

			Entries[uid] = entry;
			_dirty = true;
			if (_earliestSaveTime <= 0f)
				_earliestSaveTime = Time.unscaledTime + SaveDebounceSeconds;

			bool autoPin = ValheimPortalListPlugin.AutoPin != null && ValheimPortalListPlugin.AutoPin.Value;
			ValheimPortalListPlugin.Debug(
				$"Record {(isNew ? "new" : "update")} prefab={prefab} tag='{tag}' uid={uid} " +
				$"pos=({pos.x:0.#},{pos.z:0.#}) link={(!targetId.IsNone() ? targetUid : "none")} autoPin={autoPin}");

			if (autoPin)
				PortalMapPins.UpsertSavedPin(entry);
		}

		private static bool EntriesMatch(KnownPortalEntry a, KnownPortalEntry b)
		{
			if (a == null || b == null)
				return false;

			const float eps = 0.25f;
			if (!string.Equals(a.Tag ?? string.Empty, b.Tag ?? string.Empty, StringComparison.Ordinal))
				return false;
			if (!string.Equals(a.Prefab ?? string.Empty, b.Prefab ?? string.Empty, StringComparison.OrdinalIgnoreCase))
				return false;
			if (!string.Equals(a.TargetUid ?? string.Empty, b.TargetUid ?? string.Empty, StringComparison.OrdinalIgnoreCase))
				return false;
			if (Mathf.Abs(a.X - b.X) > eps || Mathf.Abs(a.Y - b.Y) > eps || Mathf.Abs(a.Z - b.Z) > eps)
				return false;
			if (!NullableFloatClose(a.TargetX, b.TargetX, eps) ||
			    !NullableFloatClose(a.TargetY, b.TargetY, eps) ||
			    !NullableFloatClose(a.TargetZ, b.TargetZ, eps))
				return false;

			return true;
		}

		private static bool NullableFloatClose(float? a, float? b, float eps)
		{
			if (!a.HasValue && !b.HasValue)
				return true;
			if (!a.HasValue || !b.HasValue)
				return false;
			return Mathf.Abs(a.Value - b.Value) <= eps;
		}

		/// <summary>
		/// Explicit user action: copy a portal from a world Refresh into the journal.
		/// World scan/Refresh never calls this.
		/// </summary>
		internal static bool AddFromRow(PortalRow row, bool alsoAutoPin = false)
		{
			if (row == null || string.IsNullOrEmpty(row.Uid))
				return false;

			EnsureLoaded();

			KnownPortalEntry entry = new KnownPortalEntry
			{
				Uid = row.Uid,
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

			Entries[row.Uid] = entry;
			_dirty = true;
			Save();

			if (alsoAutoPin || (ValheimPortalListPlugin.AutoPin != null && ValheimPortalListPlugin.AutoPin.Value))
				PortalMapPins.UpsertSavedPin(entry);

			return true;
		}

		internal static void RecordTeleportWorld(TeleportWorld portal)
		{
			if ((UnityEngine.Object)portal == null)
				return;

			ZNetView view = portal.GetComponent<ZNetView>();
			if ((UnityEngine.Object)view == null)
				view = portal.GetComponentInParent<ZNetView>();
			if ((UnityEngine.Object)view == null || view.GetZDO() == null)
			{
				ValheimPortalListPlugin.Debug(
					$"RecordTeleportWorld skipped — no ZNetView on '{portal.gameObject.name}'");
				return;
			}

			string prefabHint = view.gameObject.name.Replace("(Clone)", string.Empty).Trim();
			RecordZdo(view.GetZDO(), prefabHint);
		}

		private static void ScanNearby()
		{
			Player player = Player.m_localPlayer;
			if ((UnityEngine.Object)player == null || ZDOMan.instance == null)
				return;

			Vector3 pos = player.transform.position;
			float range = ApproachRange;
			float rangeSq = range * range;

			List<ZDO> portalZdos = null;
			try
			{
				portalZdos = ZDOMan.instance.GetPortalList();
			}
			catch (Exception ex)
			{
				ValheimPortalListPlugin.Debug($"GetPortalList failed: {ex.Message}");
			}

			if (portalZdos != null && portalZdos.Count > 0)
			{
				int nearby = 0;
				bool wasDirty = _dirty;
				HashSet<string> seenUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (ZDO zdo in portalZdos)
				{
					if (zdo == null || !zdo.IsValid())
						continue;

					string uid = zdo.m_uid.ToString();
					if (!seenUids.Add(uid))
						continue;

					Vector3 portalPos = zdo.GetPosition();
					float dx = pos.x - portalPos.x;
					float dz = pos.z - portalPos.z;
					if (dx * dx + dz * dz > rangeSq)
						continue;

					nearby++;
					RecordZdo(zdo);
				}

				_lastNearbyCount = nearby;

				if (ValheimPortalListPlugin.DebugEnabled && nearby > 0 && _dirty && !wasDirty)
				{
					ValheimPortalListPlugin.Debug(
						$"Approach scan via GetPortalList: total={portalZdos.Count} nearby={nearby} " +
						$"range={range:0.#}m player=({pos.x:0.#},{pos.z:0.#})");
				}

				return;
			}

			TeleportWorld[] portals = Resources.FindObjectsOfTypeAll<TeleportWorld>();
			int fallbackNearby = 0;
			bool fallbackDirty = _dirty;
			foreach (TeleportWorld portal in portals)
			{
				if ((UnityEngine.Object)portal == null || !portal.gameObject.scene.IsValid())
					continue;

				Vector3 portalPos = GetPortalApproachPoint(portal);
				float dx = pos.x - portalPos.x;
				float dz = pos.z - portalPos.z;
				if (dx * dx + dz * dz > rangeSq)
					continue;

				fallbackNearby++;
				RecordTeleportWorld(portal);
			}

			_lastNearbyCount = fallbackNearby;

			if (ValheimPortalListPlugin.DebugEnabled && fallbackNearby > 0 && _dirty && !fallbackDirty)
			{
				ValheimPortalListPlugin.Debug(
					$"Approach scan fallback TeleportWorld[]: instances={portals.Length} nearby={fallbackNearby} range={range:0.#}m");
			}
		}

		private static Vector3 GetPortalApproachPoint(TeleportWorld portal)
		{
			try
			{
				var rootField = typeof(TeleportWorld).GetField("m_proximityRoot",
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
				if (rootField != null)
				{
					Transform root = rootField.GetValue(portal) as Transform;
					if ((UnityEngine.Object)root != null)
						return root.position;
				}
			}
			catch
			{
			}

			return portal.transform.position;
		}

		private static string ResolvePrefabName(ZDO zdo)
		{
			if (zdo == null || ZNetScene.instance == null)
				return string.Empty;

			try
			{
				int hash = 0;
				var field = typeof(ZDO).GetField("m_prefab",
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
				if (field != null)
					hash = Convert.ToInt32(field.GetValue(zdo), CultureInfo.InvariantCulture);

				if (hash == 0)
					return string.Empty;

				foreach (string name in new[] { "portal_wood", "portal_stone" })
				{
					GameObject prefab = ZNetScene.instance.GetPrefab(name);
					if ((UnityEngine.Object)prefab == null)
						continue;
					if (PortalScan.StableHash(name) == hash)
						return name;
				}
			}
			catch
			{
			}

			return string.Empty;
		}

		/// <summary>
		/// Drop older journal rows that share the same tag and sit on top of each other
		/// (common after portal rebuilds / UID changes).
		/// </summary>
		private static int MergeAwayNearbyDuplicates(string keepUid, string tag, Vector3 pos)
		{
			const float mergeRadius = 4f;
			float mergeSq = mergeRadius * mergeRadius;
			List<string> remove = null;

			foreach (KeyValuePair<string, KnownPortalEntry> kv in Entries)
			{
				if (string.Equals(kv.Key, keepUid, StringComparison.OrdinalIgnoreCase))
					continue;
				if (!string.Equals(kv.Value.Tag ?? string.Empty, tag ?? string.Empty, StringComparison.OrdinalIgnoreCase))
					continue;

				float dx = kv.Value.X - pos.x;
				float dz = kv.Value.Z - pos.z;
				if (dx * dx + dz * dz > mergeSq)
					continue;

				if (remove == null)
					remove = new List<string>();
				remove.Add(kv.Key);
			}

			if (remove == null)
				return 0;

			foreach (string uid in remove)
			{
				Entries.Remove(uid);
				ValheimPortalListPlugin.Debug(
					$"Journal merge: removed duplicate '{tag}' uid={uid} (keeping {keepUid})");
			}

			return remove.Count;
		}

		/// <summary>
		/// Collapse already-saved duplicates (same tag, within a few meters). Keeps newest LastSeen.
		/// Returns how many entries were removed.
		/// </summary>
		internal static int CompactDuplicates()
		{
			const float mergeRadius = 4f;
			float mergeSq = mergeRadius * mergeRadius;
			List<KnownPortalEntry> list = new List<KnownPortalEntry>(Entries.Values);
			list.Sort((a, b) => b.LastSeenUnix.CompareTo(a.LastSeenUnix));

			HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<string> remove = new List<string>();

			foreach (KnownPortalEntry entry in list)
			{
				if (entry == null || string.IsNullOrEmpty(entry.Uid))
					continue;

				bool duplicate = false;
				foreach (string keptUid in keep)
				{
					if (!Entries.TryGetValue(keptUid, out KnownPortalEntry kept) || kept == null)
						continue;
					if (!string.Equals(kept.Tag ?? string.Empty, entry.Tag ?? string.Empty, StringComparison.OrdinalIgnoreCase))
						continue;
					float dx = kept.X - entry.X;
					float dz = kept.Z - entry.Z;
					if (dx * dx + dz * dz <= mergeSq)
					{
						duplicate = true;
						break;
					}
				}

				if (duplicate)
					remove.Add(entry.Uid);
				else
					keep.Add(entry.Uid);
			}

			foreach (string uid in remove)
			{
				if (Entries.TryGetValue(uid, out KnownPortalEntry dropped))
				{
					ValheimPortalListPlugin.Debug(
						$"Journal compact: removed '{dropped.Tag}' uid={uid} at ({dropped.X:0.#},{dropped.Z:0.#})");
				}
				Entries.Remove(uid);
			}

			if (remove.Count > 0)
				_dirty = true;

			return remove.Count;
		}

		private static string GetWorldId()
		{
			try
			{
				if (ZNet.instance != null)
				{
					string name = ZNet.instance.GetWorldName();
					if (!string.IsNullOrEmpty(name))
						return name;
				}
			}
			catch
			{
			}

			return "unknown_world";
		}

		private static string GetCharacterId()
		{
			try
			{
				Player player = Player.m_localPlayer;
				if ((UnityEngine.Object)player != null)
				{
					string name = player.GetPlayerName();
					long id = player.GetPlayerID();
					return id.ToString(CultureInfo.InvariantCulture) + "_" + (name ?? "player");
				}
			}
			catch
			{
			}

			return "unknown_character";
		}

		[HarmonyPatch(typeof(TeleportWorld), "Interact")]
		private static class TeleportWorld_Interact_Patch
		{
			private static void Postfix(TeleportWorld __instance)
			{
				RecordTeleportWorld(__instance);
			}
		}

		[HarmonyPatch(typeof(TeleportWorld), "GetHoverText")]
		private static class TeleportWorld_GetHoverText_Patch
		{
			private static void Postfix(TeleportWorld __instance)
			{
				RecordTeleportWorld(__instance);
			}
		}
	}

	/// <summary>Minimal JSON for journal files — avoids System.Text.Json dependency.</summary>
	internal static class SimpleJson
	{
		internal static string Serialize(KnownPortalFile file)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("{\"WorldId\":").Append(Q(file.WorldId));
			sb.Append(",\"CharacterId\":").Append(Q(file.CharacterId));
			sb.Append(",\"Portals\":[");
			bool first = true;
			foreach (KnownPortalEntry p in file.Portals)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append('{');
				sb.Append("\"Prefab\":").Append(Q(p.Prefab)).Append(',');
				sb.Append("\"Tag\":").Append(Q(p.Tag)).Append(',');
				sb.Append("\"Uid\":").Append(Q(p.Uid)).Append(',');
				sb.Append("\"X\":").Append(F(p.X)).Append(',');
				sb.Append("\"Y\":").Append(F(p.Y)).Append(',');
				sb.Append("\"Z\":").Append(F(p.Z)).Append(',');
				sb.Append("\"TargetUid\":").Append(Q(p.TargetUid)).Append(',');
				sb.Append("\"TargetX\":").Append(p.TargetX.HasValue ? F(p.TargetX.Value) : "null").Append(',');
				sb.Append("\"TargetY\":").Append(p.TargetY.HasValue ? F(p.TargetY.Value) : "null").Append(',');
				sb.Append("\"TargetZ\":").Append(p.TargetZ.HasValue ? F(p.TargetZ.Value) : "null").Append(',');
				sb.Append("\"LastSeenUnix\":").Append(p.LastSeenUnix.ToString(CultureInfo.InvariantCulture));
				sb.Append('}');
			}
			sb.Append("]}");
			return sb.ToString();
		}

		internal static KnownPortalFile Deserialize(string json)
		{
			KnownPortalFile file = new KnownPortalFile { Portals = new List<KnownPortalEntry>() };
			if (string.IsNullOrEmpty(json))
				return file;

			file.WorldId = ExtractString(json, "WorldId");
			file.CharacterId = ExtractString(json, "CharacterId");

			int portalsIdx = json.IndexOf("\"Portals\"", StringComparison.Ordinal);
			if (portalsIdx < 0)
				return file;

			int arrayStart = json.IndexOf('[', portalsIdx);
			int arrayEnd = json.LastIndexOf(']');
			if (arrayStart < 0 || arrayEnd <= arrayStart)
				return file;

			string array = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
			foreach (string obj in SplitObjects(array))
			{
				KnownPortalEntry entry = new KnownPortalEntry
				{
					Prefab = ExtractString(obj, "Prefab"),
					Tag = ExtractString(obj, "Tag"),
					Uid = ExtractString(obj, "Uid"),
					X = ExtractFloat(obj, "X"),
					Y = ExtractFloat(obj, "Y"),
					Z = ExtractFloat(obj, "Z"),
					TargetUid = ExtractString(obj, "TargetUid"),
					TargetX = ExtractNullableFloat(obj, "TargetX"),
					TargetY = ExtractNullableFloat(obj, "TargetY"),
					TargetZ = ExtractNullableFloat(obj, "TargetZ"),
					LastSeenUnix = (long)ExtractFloat(obj, "LastSeenUnix")
				};
				if (!string.IsNullOrEmpty(entry.Uid))
					file.Portals.Add(entry);
			}

			return file;
		}

		private static IEnumerable<string> SplitObjects(string arrayBody)
		{
			List<string> objects = new List<string>();
			int depth = 0;
			int start = -1;
			for (int i = 0; i < arrayBody.Length; i++)
			{
				char c = arrayBody[i];
				if (c == '{')
				{
					if (depth == 0) start = i;
					depth++;
				}
				else if (c == '}')
				{
					depth--;
					if (depth == 0 && start >= 0)
					{
						objects.Add(arrayBody.Substring(start, i - start + 1));
						start = -1;
					}
				}
			}
			return objects;
		}

		private static string ExtractString(string json, string key)
		{
			string pattern = "\"" + key + "\"";
			int idx = json.IndexOf(pattern, StringComparison.Ordinal);
			if (idx < 0) return string.Empty;
			int colon = json.IndexOf(':', idx + pattern.Length);
			if (colon < 0) return string.Empty;
			int q1 = json.IndexOf('"', colon + 1);
			if (q1 < 0) return string.Empty;
			int q2 = q1 + 1;
			while (q2 < json.Length)
			{
				if (json[q2] == '"' && json[q2 - 1] != '\\')
					break;
				q2++;
			}
			if (q2 >= json.Length) return string.Empty;
			return json.Substring(q1 + 1, q2 - q1 - 1).Replace("\\\"", "\"");
		}

		private static float ExtractFloat(string json, string key)
		{
			float? n = ExtractNullableFloat(json, key);
			return n ?? 0f;
		}

		private static float? ExtractNullableFloat(string json, string key)
		{
			string pattern = "\"" + key + "\"";
			int idx = json.IndexOf(pattern, StringComparison.Ordinal);
			if (idx < 0) return null;
			int colon = json.IndexOf(':', idx + pattern.Length);
			if (colon < 0) return null;
			int i = colon + 1;
			while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
			if (i + 4 <= json.Length && string.Compare(json, i, "null", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
				return null;
			int end = i;
			while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-' || json[end] == '+' || json[end] == '.' || json[end] == 'e' || json[end] == 'E'))
				end++;
			if (end <= i) return null;
			if (float.TryParse(json.Substring(i, end - i), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
				return value;
			return null;
		}

		private static string Q(string value)
		{
			value = value ?? string.Empty;
			return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
		}

		private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
	}
}
