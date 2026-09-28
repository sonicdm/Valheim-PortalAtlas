using System;

namespace PortalAtlas
{
	internal static class PortalAccess
	{
		internal static bool CanRefreshWorld()
		{
			if (ZNet.instance == null)
				return false;

			// Dedicated clients need the host to have Portal Atlas (handshake) plus SDC admin.
			if (!PortalRpc.HostSupportsRefresh)
				return false;

			if (ZNet.instance.IsServer())
				return true;

			return ServerDevcommandsAccess.IsAdmin();
		}

		/// <summary>Short stable key for UI change detection (handshake + admin).</summary>
		internal static string GetRefreshUxKey()
		{
			if (ZNet.instance == null)
				return "offline";
			if (ZNet.instance.IsServer())
				return "host";

			string hand = PortalRpc.HandshakeState;
			bool sdc = ServerDevcommandsAccess.IsAvailable;
			bool admin = ServerDevcommandsAccess.IsAdmin();
			return $"{hand}|sdc={sdc}|admin={admin}|ver={PortalRpc.HostVersion ?? "-"}";
		}

		/// <summary>Plain-language Refresh availability for the panel.</summary>
		internal static string GetRefreshUxMessage()
		{
			if (ZNet.instance == null)
				return "Refresh: not connected";

			if (ZNet.instance.IsServer())
				return "Refresh: ready (you are host)";

			if (!PortalRpc.HostSupportsRefresh)
			{
				string state = PortalRpc.HandshakeState;
				if (state == "waiting" || state == "idle")
					return "Refresh: checking server…";
				// timeout / no ack → host missing this mod (or old build without handshake)
				return "Refresh: server incompatible (no Portal Atlas)";
			}

			if (!ServerDevcommandsAccess.IsAvailable)
				return "Refresh: Server Devcommands missing";

			if (!ServerDevcommandsAccess.IsAdmin())
				return "Refresh: not an admin";

			string ver = PortalRpc.HostVersion;
			if (!string.IsNullOrEmpty(ver) && ver != "?")
				return $"Refresh: ready (server {ver}, admin)";
			return "Refresh: ready (admin)";
		}

		internal static string DescribeRefreshAccess()
		{
			if (ZNet.instance == null)
				return "ZNet missing";
			if (ZNet.instance.IsServer())
				return "host/server (local scan allowed)";

			return $"dedicated client ServerDevcommands.IsAdmin={ServerDevcommandsAccess.IsAdmin()} " +
			       $"available={ServerDevcommandsAccess.IsAvailable} " +
			       $"hostPortalAtlas={PortalRpc.HostSupportsRefresh} " +
			       $"hostVersion={PortalRpc.HostVersion ?? "(none)"} " +
			       $"handshake={PortalRpc.HandshakeState}";
		}

		internal static bool IsPeerAdmin(long sender)
		{
			if (ZNet.instance == null)
				return false;

			if (sender == 0L)
				return ZNet.instance.IsServer();

			ZNetPeer peer = ZNet.instance.GetPeer(sender);
			if (peer == null)
				return false;

			string hostname = GetPeerHostName(peer);
			if (string.IsNullOrEmpty(hostname))
				return false;

			try
			{
				return ZNet.instance.IsAdmin(hostname);
			}
			catch (Exception ex)
			{
				PortalAtlasPlugin.Debug($"ZNet.IsAdmin({hostname}) failed: {ex.Message}");
				return false;
			}
		}

		private static string GetPeerHostName(ZNetPeer peer)
		{
			try
			{
				if (peer.m_socket != null)
					return peer.m_socket.GetHostName();
			}
			catch
			{
			}

			return null;
		}
	}
}
