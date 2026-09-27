using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace PortalAtlas
{
	/// <summary>
	/// Optional hook into JereKuusela Server Devcommands (GUID server_devcommands).
	/// That mod already asks the dedicated server whether you are on adminlist.txt.
	/// </summary>
	internal static class ServerDevcommandsAccess
	{
		internal const string PluginGuid = "server_devcommands";

		private static Action _handler;
		private static bool _subscribed;

		internal static bool IsAvailable =>
			Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(PluginGuid);

		internal static bool IsAdmin()
		{
			if (!IsAvailable)
				return false;

			try
			{
				object manager = GetPermissionManager();
				if (manager == null)
					return false;

				PropertyInfo isAdmin = manager.GetType().GetProperty("IsAdmin", BindingFlags.Public | BindingFlags.Instance);
				return isAdmin != null && isAdmin.GetValue(manager) is bool value && value;
			}
			catch (Exception ex)
			{
				PortalAtlasPlugin.Debug($"Server Devcommands IsAdmin failed: {ex.Message}");
				return false;
			}
		}

		internal static void Subscribe(Action onChanged)
		{
			if (_subscribed || onChanged == null || !IsAvailable)
				return;

			Type api = FindType("ServerDevcommands.PermissionApi");
			if (api == null)
				return;

			MethodInfo subscribe = api.GetMethod("Subscribe", BindingFlags.Public | BindingFlags.Static);
			if (subscribe == null)
				return;

			_handler = onChanged;
			subscribe.Invoke(null, new object[] { _handler });
			_subscribed = true;
			PortalAtlasPlugin.Debug("Subscribed to Server Devcommands PermissionApi.");
		}

		internal static void Unsubscribe()
		{
			if (!_subscribed || _handler == null)
				return;

			try
			{
				Type api = FindType("ServerDevcommands.PermissionApi");
				MethodInfo unsubscribe = api != null
					? api.GetMethod("Unsubscribe", BindingFlags.Public | BindingFlags.Static)
					: null;
				unsubscribe?.Invoke(null, new object[] { _handler });
			}
			catch (Exception ex)
			{
				PortalAtlasPlugin.Debug($"Server Devcommands Unsubscribe failed: {ex.Message}");
			}

			_handler = null;
			_subscribed = false;
		}

		private static object GetPermissionManager()
		{
			Type type = FindType("ServerDevcommands.PermissionManager");
			if (type == null)
				return null;

			PropertyInfo instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
			return instance != null ? instance.GetValue(null) : null;
		}

		private static Type FindType(string fullName)
		{
			Type type = Type.GetType(fullName + ", ServerDevcommands");
			if (type != null)
				return type;

			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (Assembly assembly in assemblies)
			{
				type = assembly.GetType(fullName);
				if (type != null)
					return type;
			}

			return null;
		}
	}
}
