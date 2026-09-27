using System;

namespace PortalAtlas
{
	internal static class PortalAccess
	{
		internal static bool CanRefreshWorld()
		{
			if (ZNet.instance == null)
				return false;

			if (ZNet.instance.IsServer())
				return true;

			return ServerDevcommandsAccess.IsAdmin();
		}

		internal static string DescribeRefreshAccess()
		{
			if (ZNet.instance == null)
				return "ZNet missing";
			if (ZNet.instance.IsServer())
				return "host/server (local scan allowed)";

			return $"dedicated client ServerDevcommands.IsAdmin={ServerDevcommandsAccess.IsAdmin()} available={ServerDevcommandsAccess.IsAvailable}";
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
