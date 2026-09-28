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
		private const string RpcHelloName = "PortalAtlas_Hello";
		private const string RpcHelloAckName = "PortalAtlas_HelloAck";

		private const float HelloRetrySeconds = 3f;
		private const float HelloGiveUpSeconds = 45f;

		private static bool _registered;
		internal static Action<List<PortalRow>> OnWorldListReceived;
		internal static Action OnHandshakeChanged;

		private static bool _helloAcked;
		private static string _hostVersion;
		private static float _handshakeStarted = -1f;
		private static float _nextHelloTime;
		private static bool _gaveUp;
		private static string _handshakeWorldKey;

		internal static bool HostSupportsRefresh
		{
			get
			{
				if (ZNet.instance == null)
					return false;
				if (ZNet.instance.IsServer())
					return true;
				return _helloAcked;
			}
		}

		internal static string HostVersion =>
			ZNet.instance != null && ZNet.instance.IsServer()
				? PortalAtlasPlugin.PluginVersion
				: _hostVersion;

		internal static string HandshakeState
		{
			get
			{
				if (ZNet.instance == null)
					return "no-znet";
				if (ZNet.instance.IsServer())
					return "local-host";
				if (_helloAcked)
					return "acked";
				if (_gaveUp)
					return "timeout (host missing Portal Atlas?)";
				if (_handshakeStarted < 0f)
					return "idle";
				return "waiting";
			}
		}

		internal static void TickRegister()
		{
			if (ZRoutedRpc.instance == null)
			{
				_registered = false;
				ClearHandshake("rpc-gone");
				return;
			}

			if (_registered)
				return;

			try
			{
				ZRoutedRpc.instance.Register(RpcRequestName, new Action<long>(RPC_Request));
				ZRoutedRpc.instance.Register<string>(RpcResponseName, RPC_Response);
				ZRoutedRpc.instance.Register(RpcHelloName, new Action<long>(RPC_Hello));
				ZRoutedRpc.instance.Register<string>(RpcHelloAckName, RPC_HelloAck);
				_registered = true;
				PortalAtlasPlugin.ModLogger.LogInfo("Registered portal Refresh + handshake RPCs.");
			}
			catch (Exception ex)
			{
				PortalAtlasPlugin.ModLogger.LogDebug($"RPC registration deferred: {ex.Message}");
			}
		}

		/// <summary>
		/// Dedicated clients ping the host so we know Refresh RPC exists before showing the button.
		/// </summary>
		internal static void TickHandshake()
		{
			if (ZNet.instance == null || ZRoutedRpc.instance == null || !_registered)
			{
				ClearHandshake("disconnected");
				return;
			}

			if (ZNet.instance.IsServer())
			{
				// Listen host / dedicated: no client handshake needed.
				if (!_helloAcked)
				{
					_helloAcked = true;
					_hostVersion = PortalAtlasPlugin.PluginVersion;
					_gaveUp = false;
					NotifyHandshakeChanged();
				}
				return;
			}

			string worldKey = GetWorldKey();
			if (!string.Equals(worldKey, _handshakeWorldKey, StringComparison.Ordinal))
			{
				ClearHandshake("world-changed");
				_handshakeWorldKey = worldKey;
			}

			if (_helloAcked || _gaveUp)
				return;

			float now = Time.realtimeSinceStartup;
			if (_handshakeStarted < 0f)
			{
				_handshakeStarted = now;
				_nextHelloTime = now;
				PortalAtlasPlugin.Debug("Handshake: starting Hello to dedicated host");
			}

			if (now - _handshakeStarted >= HelloGiveUpSeconds)
			{
				_gaveUp = true;
				PortalAtlasPlugin.ModLogger.LogWarning(
					"Portal Atlas handshake timed out — host does not appear to have this mod. Refresh world disabled.");
				NotifyHandshakeChanged();
				return;
			}

			if (now < _nextHelloTime)
				return;

			long server = GetServerPeerId();
			if (server == 0L)
			{
				_nextHelloTime = now + 1f;
				return;
			}

			PortalAtlasPlugin.Debug($"Handshake: Hello → server peer {server}");
			ZRoutedRpc.instance.InvokeRoutedRPC(server, RpcHelloName);
			_nextHelloTime = now + HelloRetrySeconds;
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
				PortalAtlasPlugin.Debug("RequestWorldList local host scan (async)");
				PortalScan.BeginScan(result =>
				{
					if (result == null || result.Rows == null)
					{
						OnWorldListReceived?.Invoke(null);
						return;
					}

					PortalAtlasPlugin.Debug($"RequestWorldList local host scan → {result.Rows.Count} row(s)");
					OnWorldListReceived?.Invoke(result.Rows);
				});
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

		private static void RPC_Hello(long sender)
		{
			if (ZNet.instance == null || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null)
				return;

			PortalAtlasPlugin.Debug($"Handshake: Hello from {sender} → Ack {PortalAtlasPlugin.PluginVersion}");
			ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcHelloAckName, PortalAtlasPlugin.PluginVersion);
		}

		private static void RPC_HelloAck(long sender, string version)
		{
			if (ZNet.instance != null && ZNet.instance.IsServer())
				return;

			bool wasAcked = _helloAcked;
			_helloAcked = true;
			_gaveUp = false;
			_hostVersion = string.IsNullOrEmpty(version) ? "?" : version;
			PortalAtlasPlugin.Debug($"Handshake: HelloAck from host version={_hostVersion}");
			if (!wasAcked)
				NotifyHandshakeChanged();
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

			long peer = sender;
			PortalScan.BeginScan(result =>
			{
				if (ZRoutedRpc.instance == null)
					return;

				try
				{
					if (result == null || result.Rows == null)
					{
						ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcResponseName, "ERR:ScanFailed");
						return;
					}

					string payload = EncodeRows(result.Rows);
					PortalAtlasPlugin.Debug(
						$"RPC_Request scan ok rows={result.Rows.Count} payloadChars={payload.Length}");
					ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcResponseName, payload);
				}
				catch (Exception ex)
				{
					PortalAtlasPlugin.ModLogger.LogError("Portal Refresh RPC failed.");
					PortalAtlasPlugin.ModLogger.LogError(ex);
					ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcResponseName, "ERR:ScanFailed");
				}
			});
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

		private static void ClearHandshake(string reason)
		{
			bool had = _helloAcked || _gaveUp || _handshakeStarted >= 0f;
			_helloAcked = false;
			_hostVersion = null;
			_handshakeStarted = -1f;
			_nextHelloTime = 0f;
			_gaveUp = false;
			if (had)
			{
				PortalAtlasPlugin.Debug($"Handshake cleared ({reason})");
				NotifyHandshakeChanged();
			}
		}

		private static void NotifyHandshakeChanged()
		{
			try
			{
				OnHandshakeChanged?.Invoke();
			}
			catch (Exception ex)
			{
				PortalAtlasPlugin.Debug($"OnHandshakeChanged failed: {ex.Message}");
			}
		}

		private static string GetWorldKey()
		{
			try
			{
				if (ZNet.instance == null)
					return string.Empty;
				string name = ZNet.instance.GetWorldName();
				return name ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
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
