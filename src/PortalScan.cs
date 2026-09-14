using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace ValheimPortalList
{
	internal static class PortalScan
	{
		internal static ConfigEntry<float> DumpDelaySeconds;
		internal static ConfigEntry<string> OutputFileName;
		internal static ConfigEntry<bool> WriteTextReport;
		internal static ConfigEntry<bool> WriteTagSummary;

		private static bool _dumped;
		private static float _readySince = -1f;

		internal static void BindConfig(ConfigFile config)
		{
			DumpDelaySeconds = config.Bind("General", "DumpDelaySeconds", 15f,
				"Seconds to wait after the server/world becomes available before writing portal CSV reports.");
			OutputFileName = config.Bind("General", "OutputFileName", "ValheimPortalList.csv",
				"CSV file written under BepInEx/cache/ValheimPortalList/.");
			WriteTextReport = config.Bind("General", "WriteTextReport", true,
				"Also write a human-readable ValheimPortalList.txt report.");
			WriteTagSummary = config.Bind("General", "WriteTagSummary", true,
				"Also write ValheimPortalTagSummary.csv.");
		}

		internal static void TickHostAutoDump()
		{
			if (_dumped)
				return;

			if (ZNet.instance == null || ZDOMan.instance == null || ZNetScene.instance == null)
			{
				_readySince = -1f;
				return;
			}

			if (!ZNet.instance.IsServer())
			{
				_readySince = float.MaxValue;
				return;
			}

			if (_readySince < 0f)
			{
				_readySince = Time.realtimeSinceStartup;
				ValheimPortalListPlugin.ModLogger.LogInfo(
					$"World/server detected. Waiting {DumpDelaySeconds.Value:0.#}s before portal CSV scan...");
				return;
			}

			if (Time.realtimeSinceStartup - _readySince < Math.Max(0f, DumpDelaySeconds.Value))
				return;

			try
			{
				DumpPortals();
				_dumped = true;
			}
			catch (Exception ex)
			{
				ValheimPortalListPlugin.ModLogger.LogError("Portal scan failed. Will retry in 10 seconds.");
				ValheimPortalListPlugin.ModLogger.LogError(ex);
				_readySince = Time.realtimeSinceStartup + 10f - Math.Max(0f, DumpDelaySeconds.Value);
			}
		}

		internal static void ResetDumpState()
		{
			_dumped = false;
			_readySince = -1f;
		}

		internal static PortalDumpResult ScanPortals()
		{
			List<PortalRow> rows = new List<PortalRow>();
			List<GameObject> portalPrefabs = FindPortalPrefabs();

			ValheimPortalListPlugin.ModLogger.LogInfo(
				$"Found {portalPrefabs.Count} registered prefab(s) containing TeleportWorld.");

			foreach (GameObject prefab in portalPrefabs)
			{
				if ((UnityEngine.Object)prefab == null)
					continue;

				List<ZDO> zdos = GetZDOsWithPrefab(prefab.name);
				ValheimPortalListPlugin.ModLogger.LogInfo($"{prefab.name}: {zdos.Count} portal ZDO(s)");

				foreach (ZDO zdo in zdos)
				{
					if (zdo == null || !zdo.IsValid())
						continue;

					Vector3 pos = zdo.GetPosition();
					string tag = SafeGetString(zdo, "tag");
					// Modern Valheim links portals via ZDO connections (ConnectionType.Portal),
					// not the legacy string key "target". HaveTarget/TargetFound use the same API.
					ZDOID targetId = GetPortalConnectionId(zdo);
					ZDO target = null;

					try
					{
						if (!targetId.IsNone())
							target = ZDOMan.instance.GetZDO(targetId);
					}
					catch
					{
					}

					Vector3? targetPos = target != null && target.IsValid() ? (Vector3?)target.GetPosition() : null;
					bool hasLink = !targetId.IsNone();

					rows.Add(new PortalRow
					{
						Prefab = prefab.name,
						Tag = tag,
						Uid = zdo.m_uid.ToString(),
						X = pos.x,
						Y = pos.y,
						Z = pos.z,
						TargetUid = targetId.ToString(),
						Connected = hasLink,
						TargetX = targetPos?.x,
						TargetY = targetPos?.y,
						TargetZ = targetPos?.z
					});
				}
			}

			rows.Sort((a, b) =>
			{
				int tagCompare = string.Compare(a.Tag, b.Tag, StringComparison.OrdinalIgnoreCase);
				if (tagCompare != 0) return tagCompare;
				int prefabCompare = string.Compare(a.Prefab, b.Prefab, StringComparison.OrdinalIgnoreCase);
				if (prefabCompare != 0) return prefabCompare;
				int xCompare = a.X.CompareTo(b.X);
				if (xCompare != 0) return xCompare;
				return a.Z.CompareTo(b.Z);
			});

			Dictionary<string, int> tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (PortalRow row in rows)
			{
				string key = row.Tag ?? string.Empty;
				if (!tagCounts.ContainsKey(key)) tagCounts[key] = 0;
				tagCounts[key]++;
			}

			foreach (PortalRow row in rows)
				row.SameTagCount = tagCounts[row.Tag ?? string.Empty];

			AnalyzeRelationships(rows);

			if (ValheimPortalListPlugin.DebugEnabled)
				LogScanDebug(rows, portalPrefabs.Count);

			return new PortalDumpResult
			{
				Rows = rows,
				PortalPrefabCount = portalPrefabs.Count
			};
		}

		private static void LogScanDebug(List<PortalRow> rows, int prefabCount)
		{
			int connected = 0, oneWay = 0, unconnected = 0, tagTwin = 0, wood = 0, stone = 0;
			foreach (PortalRow row in rows)
			{
				if (string.Equals(row.Prefab, "portal_stone", StringComparison.OrdinalIgnoreCase))
					stone++;
				else if (string.Equals(row.Prefab, "portal_wood", StringComparison.OrdinalIgnoreCase))
					wood++;

				string status = row.StatusLabel ?? string.Empty;
				if (status == "connected") connected++;
				else if (status == "one-way") oneWay++;
				else if (status == "tag twin") tagTwin++;
				else unconnected++;
			}

			ValheimPortalListPlugin.Debug(
				$"Scan summary: total={rows.Count} prefabs={prefabCount} wood={wood} stone={stone} " +
				$"connected={connected} one-way={oneWay} tagTwin={tagTwin} unconnected={unconnected}");

			foreach (PortalRow row in rows)
			{
				ValheimPortalListPlugin.Debug(
					$"  [{row.Prefab}] '{row.DisplayTag}' {row.StatusLabel} sameTag={row.SameTagCount} " +
					$"uid={row.Uid} link={row.TargetUid} pos=({row.X:0.#},{row.Z:0.#}) rel={row.Relationship}");
			}
		}

		internal static PortalDumpResult DumpPortals()
		{
			PortalDumpResult result = ScanPortals();
			List<PortalRow> rows = result.Rows;

			string cacheDir = PortalPaths.CacheRoot;
			string csvPath = Path.Combine(cacheDir, PortalPaths.Sanitize(OutputFileName.Value, "ValheimPortalList.csv"));
			WriteCsv(csvPath, rows);

			ValheimPortalListPlugin.ModLogger.LogInfo($"Portal scan complete: {rows.Count} portal(s) found.");
			ValheimPortalListPlugin.ModLogger.LogInfo($"CSV: {csvPath}");
			result.CsvPath = csvPath;

			if (WriteTagSummary.Value)
			{
				string summaryCsvPath = Path.Combine(cacheDir, "ValheimPortalTagSummary.csv");
				WriteTagSummaryCsv(summaryCsvPath, rows);
				ValheimPortalListPlugin.ModLogger.LogInfo($"Tag summary CSV: {summaryCsvPath}");
				result.SummaryCsvPath = summaryCsvPath;
			}

			if (WriteTextReport.Value)
			{
				string txtPath = Path.Combine(cacheDir, "ValheimPortalList.txt");
				WriteText(txtPath, rows, result.PortalPrefabCount);
				ValheimPortalListPlugin.ModLogger.LogInfo($"Text report: {txtPath}");
				result.TextPath = txtPath;
			}

			return result;
		}

		internal static void AnalyzeRelationships(List<PortalRow> rows)
		{
			Dictionary<string, PortalRow> byUid = new Dictionary<string, PortalRow>(StringComparer.OrdinalIgnoreCase);
			foreach (PortalRow row in rows)
			{
				if (!string.IsNullOrEmpty(row.Uid))
					byUid[row.Uid] = row;
			}

			foreach (PortalRow row in rows)
			{
				List<string> issues = new List<string>();
				string targetUid = row.TargetUid ?? string.Empty;
				bool hasTargetReference = !string.IsNullOrEmpty(targetUid) &&
				                          !targetUid.Equals("None", StringComparison.OrdinalIgnoreCase) &&
				                          !targetUid.Equals("0:0", StringComparison.OrdinalIgnoreCase);

				if (!hasTargetReference)
				{
					// No ZDO connection yet. Same-tag count is only a hint — the game pairs
					// asynchronously via ZDOMan.ConnectPortals; tag match alone is not "connected".
					row.Relationship = row.SameTagCount >= 2 ? "Tag twin (not linked yet)" : "Unconnected";
					issues.Add("UNPAIRED");
				}
				else if (!byUid.TryGetValue(targetUid, out PortalRow targetRow))
				{
					// Connection id exists (vanilla HaveTarget) but partner ZDO not in this scan —
					// still a live link; partner sector may be unloaded.
					row.Relationship = "Connected";
					row.Connected = true;
				}
				else if (string.Equals(targetRow.TargetUid, row.Uid, StringComparison.OrdinalIgnoreCase))
				{
					row.Relationship = "Mutual pair";
					row.Connected = true;
				}
				else
				{
					row.Relationship = "One-way link";
					row.Connected = true;
					issues.Add("ONE_WAY");
				}

				if (row.SameTagCount == 1)
				{
					if (!issues.Contains("UNPAIRED"))
						issues.Add("UNPAIRED_TAG");
				}
				else if (row.SameTagCount > 2)
				{
					issues.Add("DUPLICATE_TAG");
				}

				row.Issues = string.Join(";", issues);
			}
		}

		internal static List<PortalRow> RowsFromKnown(IEnumerable<KnownPortalEntry> entries)
		{
			List<PortalRow> rows = new List<PortalRow>();
			if (entries == null)
				return rows;

			foreach (KnownPortalEntry entry in entries)
			{
				if (entry == null)
					continue;
				rows.Add(entry.ToRow());
			}

			Dictionary<string, int> tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (PortalRow row in rows)
			{
				string key = row.Tag ?? string.Empty;
				if (!tagCounts.ContainsKey(key)) tagCounts[key] = 0;
				tagCounts[key]++;
			}

			foreach (PortalRow row in rows)
				row.SameTagCount = tagCounts[row.Tag ?? string.Empty];

			AnalyzeRelationships(rows);
			return rows;
		}

		private static List<ZDO> GetZDOsWithPrefab(string prefabName)
		{
			List<ZDO> result = new List<ZDO>();
			if (ZDOMan.instance == null || string.IsNullOrEmpty(prefabName))
				return result;

			int prefabHash = GetStableHashCode(prefabName);
			Type zdoManType = typeof(ZDOMan);

			foreach (MethodInfo method in zdoManType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (!string.Equals(method.Name, "GetAllZDOsWithPrefab", StringComparison.Ordinal))
					continue;

				ParameterInfo[] parameters = method.GetParameters();
				if (parameters.Length != 2)
					continue;

				try
				{
					object firstArg;
					if (parameters[0].ParameterType == typeof(string))
						firstArg = prefabName;
					else if (parameters[0].ParameterType == typeof(int))
						firstArg = prefabHash;
					else
						continue;

					if (!parameters[1].ParameterType.IsAssignableFrom(typeof(List<ZDO>)) &&
					    !typeof(ICollection<ZDO>).IsAssignableFrom(parameters[1].ParameterType))
						continue;

					List<ZDO> methodResult = new List<ZDO>();
					method.Invoke(ZDOMan.instance, new object[] { firstArg, methodResult });
					AddUniqueValidZDOs(result, methodResult, prefabHash);
					return result;
				}
				catch (Exception ex)
				{
					ValheimPortalListPlugin.ModLogger.LogDebug(
						$"Runtime GetAllZDOsWithPrefab invocation failed: {ex.GetType().Name}: {ex.Message}");
				}
			}

			try
			{
				MethodInfo getSaveClone = zdoManType.GetMethod("GetSaveClone",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null, Type.EmptyTypes, null);

				if (getSaveClone != null)
				{
					object clone = getSaveClone.Invoke(ZDOMan.instance, null);
					AddZDOsFromCollection(result, clone, prefabHash);
					if (result.Count > 0)
						return result;
				}
			}
			catch (Exception ex)
			{
				ValheimPortalListPlugin.ModLogger.LogDebug($"GetSaveClone fallback failed: {ex.GetType().Name}: {ex.Message}");
			}

			foreach (FieldInfo field in zdoManType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				try
				{
					object value = field.GetValue(ZDOMan.instance);
					AddZDOsFromCollection(result, value, prefabHash);
				}
				catch
				{
				}
			}

			return result;
		}

		private static void AddZDOsFromCollection(List<ZDO> destination, object collection, int prefabHash)
		{
			if (collection == null)
				return;

			if (collection is ZDO single)
			{
				AddUniqueValidZDO(destination, single, prefabHash);
				return;
			}

			if (collection is IDictionary dictionary)
			{
				foreach (DictionaryEntry entry in dictionary)
					AddZDOObject(destination, entry.Value, prefabHash);
				return;
			}

			if (collection is IEnumerable enumerable && !(collection is string))
			{
				foreach (object entry in enumerable)
					AddZDOObject(destination, entry, prefabHash);
			}
		}

		private static void AddZDOObject(List<ZDO> destination, object value, int prefabHash)
		{
			if (value == null)
				return;

			if (value is ZDO zdo)
			{
				AddUniqueValidZDO(destination, zdo, prefabHash);
				return;
			}

			PropertyInfo valueProperty = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
			if (valueProperty != null)
			{
				object nested = valueProperty.GetValue(value, null);
				if (nested is ZDO nestedZdo)
					AddUniqueValidZDO(destination, nestedZdo, prefabHash);
			}
		}

		private static void AddUniqueValidZDOs(List<ZDO> destination, IEnumerable<ZDO> source, int prefabHash)
		{
			if (source == null)
				return;

			foreach (ZDO zdo in source)
				AddUniqueValidZDO(destination, zdo, prefabHash);
		}

		private static void AddUniqueValidZDO(List<ZDO> destination, ZDO zdo, int prefabHash)
		{
			if (zdo == null || !zdo.IsValid() || GetZdoPrefabHash(zdo) != prefabHash)
				return;

			string uid = zdo.m_uid.ToString();
			foreach (ZDO existing in destination)
			{
				if (existing != null && existing.m_uid.ToString() == uid)
					return;
			}

			destination.Add(zdo);
		}

		private static int GetZdoPrefabHash(ZDO zdo)
		{
			if (zdo == null)
				return 0;

			Type type = typeof(ZDO);

			try
			{
				FieldInfo field = type.GetField("m_prefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null)
					return Convert.ToInt32(field.GetValue(zdo), CultureInfo.InvariantCulture);
			}
			catch
			{
			}

			try
			{
				MethodInfo method = type.GetMethod("GetPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null, Type.EmptyTypes, null);
				if (method != null)
					return Convert.ToInt32(method.Invoke(zdo, null), CultureInfo.InvariantCulture);
			}
			catch
			{
			}

			return 0;
		}

		private static int GetStableHashCode(string str)
		{
			unchecked
			{
				int hash1 = 5381;
				int hash2 = hash1;

				for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
				{
					hash1 = ((hash1 << 5) + hash1) ^ str[i];
					if (i == str.Length - 1 || str[i + 1] == '\0')
						break;
					hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
				}

				return hash1 + (hash2 * 1566083941);
			}
		}

		internal static int StableHash(string name) => GetStableHashCode(name ?? string.Empty);

		private static List<GameObject> FindPortalPrefabs()
		{
			Dictionary<string, GameObject> unique = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
			ZNetScene scene = ZNetScene.instance;

			FieldInfo namedPrefabsField = typeof(ZNetScene).GetField("m_namedPrefabs",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

			object collection = namedPrefabsField != null ? namedPrefabsField.GetValue(scene) : null;
			IDictionary dictionary = collection as IDictionary;

			if (dictionary != null)
			{
				foreach (DictionaryEntry entry in dictionary)
					AddIfPortal(entry.Value as GameObject, unique);
			}
			else if (collection is IEnumerable enumerable)
			{
				foreach (object entry in enumerable)
				{
					object value = GetKeyValuePairValue(entry);
					AddIfPortal(value as GameObject, unique);
				}
			}

			AddNamedPrefabIfPortal("portal_wood", unique);
			AddNamedPrefabIfPortal("portal_stone", unique);

			List<GameObject> result = new List<GameObject>(unique.Values);
			result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
			return result;
		}

		private static void AddNamedPrefabIfPortal(string prefabName, Dictionary<string, GameObject> unique)
		{
			try
			{
				GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
				AddIfPortal(prefab, unique);
			}
			catch
			{
			}
		}

		private static void AddIfPortal(GameObject prefab, Dictionary<string, GameObject> unique)
		{
			if ((UnityEngine.Object)prefab == null || string.IsNullOrEmpty(prefab.name))
				return;

			TeleportWorld portal = prefab.GetComponent<TeleportWorld>();
			if ((UnityEngine.Object)portal == null)
				portal = prefab.GetComponentInChildren<TeleportWorld>(true);

			if ((UnityEngine.Object)portal != null && !unique.ContainsKey(prefab.name))
				unique[prefab.name] = prefab;
		}

		private static object GetKeyValuePairValue(object entry)
		{
			if (entry == null)
				return null;

			PropertyInfo valueProperty = entry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
			return valueProperty != null ? valueProperty.GetValue(entry, null) : null;
		}

		internal static string SafeGetString(ZDO zdo, string key)
		{
			try { return zdo.GetString(key) ?? string.Empty; }
			catch { return string.Empty; }
		}

		/// <summary>
		/// Same link vanilla TeleportWorld.HaveTarget uses: ZDO connection type Portal.
		/// Falls back to the legacy "target" ZDOID key for worlds not yet converted.
		/// </summary>
		internal static ZDOID GetPortalConnectionId(ZDO zdo)
		{
			if (zdo == null || !zdo.IsValid())
				return ZDOID.None;

			try
			{
				ZDOID connected = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
				if (!connected.IsNone())
					return connected;
			}
			catch
			{
			}

			try
			{
				ZDOID legacy = zdo.GetZDOID("target");
				if (!legacy.IsNone())
					return legacy;
			}
			catch
			{
			}

			return ZDOID.None;
		}

		private static void WriteCsv(string path, List<PortalRow> rows)
		{
			using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
			{
				writer.WriteLine("Prefab,Tag,Connected,SameTagCount,Relationship,Issues,ZDO_UID,X,Y,Z,Target_UID,Target_X,Target_Y,Target_Z");

				foreach (PortalRow row in rows)
				{
					writer.WriteLine(string.Join(",", new[]
					{
						Csv(row.Prefab),
						Csv(row.Tag),
						row.Connected ? "true" : "false",
						row.SameTagCount.ToString(CultureInfo.InvariantCulture),
						Csv(row.Relationship),
						Csv(row.Issues),
						Csv(row.Uid),
						Num(row.X), Num(row.Y), Num(row.Z),
						Csv(row.TargetUid),
						NumNullable(row.TargetX), NumNullable(row.TargetY), NumNullable(row.TargetZ)
					}));
				}
			}
		}

		private static void WriteText(string path, List<PortalRow> rows, int prefabCount)
		{
			using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
			{
				writer.WriteLine("Valheim Portal List");
				writer.WriteLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
				writer.WriteLine("Portal prefab types: " + prefabCount);
				writer.WriteLine("Total portals: " + rows.Count);
				writer.WriteLine(new string('=', 80));
				writer.WriteLine();

				foreach (PortalRow row in rows)
				{
					writer.WriteLine($"Tag: {(string.IsNullOrEmpty(row.Tag) ? "<empty>" : row.Tag)}");
					writer.WriteLine($"Prefab: {row.Prefab}");
					writer.WriteLine($"Position: {Num(row.X)}, {Num(row.Y)}, {Num(row.Z)}");
					writer.WriteLine($"ZDO UID: {row.Uid}");
					writer.WriteLine($"Connected: {(row.Connected ? "Yes" : "No")}");
					writer.WriteLine($"Same tag count: {row.SameTagCount}");
					writer.WriteLine($"Relationship: {row.Relationship}");
					if (!string.IsNullOrEmpty(row.Issues))
						writer.WriteLine($"Issues: {row.Issues}");

					if (row.Connected)
					{
						writer.WriteLine($"Target UID: {row.TargetUid}");
						writer.WriteLine($"Target position: {NumNullable(row.TargetX)}, {NumNullable(row.TargetY)}, {NumNullable(row.TargetZ)}");
					}
					else
					{
						writer.WriteLine($"Target UID: {row.TargetUid}");
					}

					writer.WriteLine(new string('-', 80));
				}
			}
		}

		private static void WriteTagSummaryCsv(string path, List<PortalRow> rows)
		{
			Dictionary<string, List<PortalRow>> groups = new Dictionary<string, List<PortalRow>>(StringComparer.OrdinalIgnoreCase);
			foreach (PortalRow row in rows)
			{
				string key = row.Tag ?? string.Empty;
				if (!groups.TryGetValue(key, out List<PortalRow> list))
				{
					list = new List<PortalRow>();
					groups[key] = list;
				}
				list.Add(row);
			}

			List<string> keys = new List<string>(groups.Keys);
			keys.Sort(StringComparer.OrdinalIgnoreCase);

			using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true)))
			{
				writer.WriteLine("Tag,PortalCount,ConnectedCount,UnconnectedCount,MutualPairPortals,OneWayLinks,MissingTargets,Status,PortalUIDs");

				foreach (string key in keys)
				{
					List<PortalRow> group = groups[key];
					int connected = 0;
					int unconnected = 0;
					int mutual = 0;
					int oneWay = 0;
					int missing = 0;
					List<string> uids = new List<string>();

					foreach (PortalRow row in group)
					{
						if (row.Connected) connected++; else unconnected++;
						if (row.Relationship == "Mutual pair") mutual++;
						else if (row.Relationship == "One-way link") oneWay++;
						else if (row.Relationship == "Missing target") missing++;
						if (!string.IsNullOrEmpty(row.Uid)) uids.Add(row.Uid);
					}

					List<string> status = new List<string>();
					if (group.Count == 1 || unconnected > 0) status.Add("UNPAIRED");
					if (group.Count > 2) status.Add("DUPLICATE_TAG");
					if (oneWay > 0) status.Add("ONE_WAY");
					if (missing > 0) status.Add("MISSING_TARGET");
					if (status.Count == 0) status.Add("OK");

					writer.WriteLine(string.Join(",", new[]
					{
						Csv(key),
						group.Count.ToString(CultureInfo.InvariantCulture),
						connected.ToString(CultureInfo.InvariantCulture),
						unconnected.ToString(CultureInfo.InvariantCulture),
						mutual.ToString(CultureInfo.InvariantCulture),
						oneWay.ToString(CultureInfo.InvariantCulture),
						missing.ToString(CultureInfo.InvariantCulture),
						Csv(string.Join(";", status)),
						Csv(string.Join(";", uids))
					}));
				}
			}
		}

		private static string Csv(string value)
		{
			value = value ?? string.Empty;
			return "\"" + value.Replace("\"", "\"\"") + "\"";
		}

		internal static string Num(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
		private static string NumNullable(float? value) => value.HasValue ? Num(value.Value) : string.Empty;
	}
}
