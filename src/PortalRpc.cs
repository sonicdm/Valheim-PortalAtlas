using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace PortalAtlas
{
	internal static class PortalRpc
	{
		private const string RpcRequestName = "PortalAtlas_Refresh";
		private const string RpcResponseName = "PortalAtlas_RefreshOut";

		private static bool _registered;
		internal static Action<List<PortalRow>> OnWorldListReceived;

		internal static void TickRegister()
		{
			if (ZRoutedRpc.instance == null)
			{
				_registered = false;
				return;
			}

			if (_registered)
				return;

			try
			{
				ZRoutedRpc.instance.Register(RpcRequestName, new Action<long>(RPC_Request));
				ZRoutedRpc.instance.Register<string>(RpcResponseName, RPC_Response);
				_registered = true;
				PortalAtlasPlugin.ModLogger.LogInfo("Registered portal Refresh RPCs.");
			}
			catch (Exception ex)
			{
				PortalAtlasPlugin.ModLogger.LogDebug($"RPC registration deferred: {ex.Message}");
			}
		}

		internal static bool RequestWorldList()
		{
			bool can = PortalAccess.CanRefreshWorld();
			PortalAtlasPlugin.Debug(
				$"RequestWorldList canRefresh={can} ({PortalAccess.DescribeRefreshAccess()}) " +
				$"isServer={ZNet.instance != null && ZNet.instance.IsServer()}");

			if (!can)
				return false;

			if (ZNet.instance != null && ZNet.instance.IsServer())
			{
				PortalDumpResult result = PortalScan.ScanPortals();
				PortalAtlasPlugin.Debug($"RequestWorldList local host scan → {result.Rows.Count} row(s)");
				OnWorldListReceived?.Invoke(result.Rows);
				return true;
			}

			if (ZRoutedRpc.instance == null)
			{
				PortalAtlasPlugin.Debug("RequestWorldList failed — ZRoutedRpc missing");
				return false;
			}

			long server = GetServerPeerId();
			if (server == 0L)
			{
				PortalAtlasPlugin.Debug("RequestWorldList failed — server peer id is 0");
				return false;
			}

			PortalAtlasPlugin.Debug($"RequestWorldList invoking RPC → server peer {server}");
			ZRoutedRpc.instance.InvokeRoutedRPC(server, RpcRequestName);
			return true;
		}

		private static void RPC_Request(long sender)
		{
			if (ZNet.instance == null || !ZNet.instance.IsServer())
				return;

			bool admin = PortalAccess.IsPeerAdmin(sender);
			PortalAtlasPlugin.Debug($"RPC_Request from {sender} admin={admin}");

			if (!admin)
			{
				PortalAtlasPlugin.ModLogger.LogWarning($"Rejected portal Refresh from non-admin {sender}");
				ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcResponseName, "ERR:Unauthorized");
				return;
			}

			try
			{
				PortalDumpResult result = PortalScan.ScanPortals();
				string payload = EncodeRows(result.Rows);
				PortalAtlasPlugin.Debug(
					$"RPC_Request scan ok rows={result.Rows.Count} payloadChars={payload.Length}");
				ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcResponseName, payload);
			}
			catch (Exception ex)
			{
				PortalAtlasPlugin.ModLogger.LogError("Portal Refresh RPC failed.");
				PortalAtlasPlugin.ModLogger.LogError(ex);
				ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcResponseName, "ERR:ScanFailed");
			}
		}

		private static void RPC_Response(long sender, string payload)
		{
			PortalAtlasPlugin.Debug(
				$"RPC_Response from {sender} chars={(payload == null ? 0 : payload.Length)}");

			if (string.IsNullOrEmpty(payload))
				return;

			if (payload.StartsWith("ERR:", StringComparison.Ordinal))
			{
				PortalAtlasPlugin.ModLogger.LogWarning("Portal Refresh failed: " + payload.Substring(4));
				OnWorldListReceived?.Invoke(null);
				return;
			}

			List<PortalRow> rows = DecodeRows(payload);
			PortalScan.AnalyzeRelationships(rows);
			PortalAtlasPlugin.Debug($"RPC_Response decoded {rows.Count} row(s)");
			OnWorldListReceived?.Invoke(rows);
		}

		internal static string EncodeRows(List<PortalRow> rows)
		{
			StringBuilder sb = new StringBuilder();
			foreach (PortalRow row in rows)
			{
				sb.Append(Esc(row.Prefab)).Append('\t')
					.Append(Esc(row.Tag)).Append('\t')
					.Append(Esc(row.Uid)).Append('\t')
					.Append(F(row.X)).Append('\t')
					.Append(F(row.Y)).Append('\t')
					.Append(F(row.Z)).Append('\t')
					.Append(Esc(row.TargetUid)).Append('\t')
					.Append(row.TargetX.HasValue ? F(row.TargetX.Value) : string.Empty).Append('\t')
					.Append(row.TargetY.HasValue ? F(row.TargetY.Value) : string.Empty).Append('\t')
					.Append(row.TargetZ.HasValue ? F(row.TargetZ.Value) : string.Empty)
					.Append('\n');
			}
			return sb.ToString();
		}

		internal static List<PortalRow> DecodeRows(string payload)
		{
			List<PortalRow> rows = new List<PortalRow>();
			foreach (string line in payload.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string[] p = line.Split('\t');
				if (p.Length < 7)
					continue;

				float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
				float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
				float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float z);

				float? tx = null, ty = null, tz = null;
				if (p.Length > 7 && float.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float txv))
					tx = txv;
				if (p.Length > 8 && float.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float tyv))
					ty = tyv;
				if (p.Length > 9 && float.TryParse(p[9], NumberStyles.Float, CultureInfo.InvariantCulture, out float tzv))
					tz = tzv;

				rows.Add(new PortalRow
				{
					Prefab = Unesc(p[0]),
					Tag = Unesc(p[1]),
					Uid = Unesc(p[2]),
					X = x,
					Y = y,
					Z = z,
					TargetUid = Unesc(p[6]),
					Connected = tx.HasValue,
					TargetX = tx,
					TargetY = ty,
					TargetZ = tz
				});
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

			return rows;
		}

		private static long GetServerPeerId()
		{
			try
			{
				var method = typeof(ZRoutedRpc).GetMethod("GetServerPeerID",
					System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
					null, Type.EmptyTypes, null);
				if (method != null)
					return Convert.ToInt64(method.Invoke(ZRoutedRpc.instance, null), CultureInfo.InvariantCulture);
			}
			catch
			{
			}

			return 0L;
		}

		private static string Esc(string value) => (value ?? string.Empty).Replace('\t', ' ').Replace('\n', ' ');
		private static string Unesc(string value) => value ?? string.Empty;
		private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
	}
}
