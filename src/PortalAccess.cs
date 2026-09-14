using System;
using System.Reflection;
using Jotunn;
using Jotunn.Managers;

namespace ValheimPortalList
{
	internal static class PortalAccess
	{
		internal static bool CanRefreshWorld()
		{
			if (ZNet.instance == null)
				return false;

			// Local / listen host owns the world ZDO list — always allow Refresh.
			if (ZNet.instance.IsServer())
				return true;

			// Dedicated client: Jötunn syncs adminlist status after login.
			return IsLocalPlayerAdmin();
		}

		internal static string DescribeRefreshAccess()
		{
			if (ZNet.instance == null)
				return "ZNet missing";
			if (ZNet.instance.IsServer())
				return "host/server (local scan allowed)";
			return $"dedicated client PlayerIsAdmin={IsLocalPlayerAdmin()}";
		}

		internal static bool IsLocalPlayerAdmin()
		{
			if (ZNet.instance == null)
				return false;

			if (ZNet.instance.IsServer())
				return true;

			try
			{
				SynchronizationManager sync = SynchronizationManager.Instance;
				if (sync != null)
					return sync.PlayerIsAdmin;
			}
			catch (Exception ex)
			{
				ValheimPortalListPlugin.Debug($"SynchronizationManager.PlayerIsAdmin failed: {ex.Message}");
			}

			// Legacy fallbacks if Jötunn sync is unavailable.
			try
			{
				MethodInfo localAdmin = typeof(ZNet).GetMethod("LocalPlayerIsAdmin",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null, Type.EmptyTypes, null);
				if (localAdmin != null)
					return (bool)localAdmin.Invoke(ZNet.instance, null);

				string host = null;
				ZNetPeer serverPeer = GetServerPeer();
				if (serverPeer != null && serverPeer.m_rpc != null && serverPeer.m_rpc.GetSocket() != null)
					host = serverPeer.m_rpc.GetSocket().GetHostName();

				if (!string.IsNullOrEmpty(host))
					return IsAdminHostName(host);
			}
			catch (Exception ex)
			{
				ValheimPortalListPlugin.Debug($"IsLocalPlayerAdmin fallback failed: {ex.Message}");
			}

			return false;
		}

		internal static bool IsPeerAdmin(long sender)
		{
			if (ZNet.instance == null)
				return false;

			if (sender == 0L)
				return ZNet.instance.IsServer();

			// Jötunn helper: checks adminlist by peer uid (server-side only).
			try
			{
				return ZNet.instance.IsAdmin(sender);
			}
			catch (Exception ex)
			{
				ValheimPortalListPlugin.Debug($"ZNet.IsAdmin({sender}) failed: {ex.Message}");
			}

			ZNetPeer peer = ZNet.instance.GetPeer(sender);
			if (peer == null)
				return false;

			string hostname = null;
			try
			{
				if (peer.m_rpc != null && peer.m_rpc.GetSocket() != null)
					hostname = peer.m_rpc.GetSocket().GetHostName();
			}
			catch
			{
				hostname = null;
			}

			return IsAdminHostName(hostname);
		}

		internal static bool IsAdminHostName(string hostname)
		{
			if (string.IsNullOrEmpty(hostname) || ZNet.instance == null)
				return false;

			MethodInfo isAdmin = typeof(ZNet).GetMethod("IsAdmin",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null, new[] { typeof(string) }, null);
			if (isAdmin != null)
				return (bool)isAdmin.Invoke(ZNet.instance, new object[] { hostname });

			FieldInfo listField = typeof(ZNet).GetField("m_adminList",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (listField == null)
				return false;

			object list = listField.GetValue(ZNet.instance);
			if (list == null)
				return false;

			MethodInfo contains = typeof(ZNet).GetMethod("ListContainsId",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (contains != null)
			{
				ParameterInfo[] parameters = contains.GetParameters();
				if (parameters.Length == 2 && parameters[1].ParameterType == typeof(string))
					return (bool)contains.Invoke(ZNet.instance, new[] { list, hostname });
			}

			MethodInfo containsId = list.GetType().GetMethod("Contains",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null, new[] { typeof(string) }, null);
			if (containsId != null)
				return (bool)containsId.Invoke(list, new object[] { hostname });

			return false;
		}

		private static ZNetPeer GetServerPeer()
		{
			MethodInfo getPeer = typeof(ZNet).GetMethod("GetServerPeer",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null, Type.EmptyTypes, null);
			if (getPeer == null)
				return null;
			return getPeer.Invoke(ZNet.instance, null) as ZNetPeer;
		}
	}
}
