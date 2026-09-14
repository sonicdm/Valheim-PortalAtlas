using System.IO;
using BepInEx;

namespace PortalAtlas
{
	internal static class PortalPaths
	{
		internal const string FolderName = "PortalAtlas";
		private const string LegacyFolderName = "ValheimPortalList";

		internal static string CacheRoot
		{
			get
			{
				string root = Path.Combine(Paths.CachePath, FolderName);
				Directory.CreateDirectory(root);
				TryMigrateLegacyCache(root);
				return root;
			}
		}

		internal static string JournalPath(string worldId, string characterId)
		{
			string safeWorld = Sanitize(worldId, "world");
			string safeChar = Sanitize(characterId, "character");
			return Path.Combine(CacheRoot, $"journal_{safeWorld}_{safeChar}.json");
		}

		internal static string Sanitize(string value, string fallback)
		{
			if (string.IsNullOrWhiteSpace(value))
				return fallback;

			foreach (char c in Path.GetInvalidFileNameChars())
				value = value.Replace(c, '_');

			return string.IsNullOrWhiteSpace(value) ? fallback : value;
		}

		/// <summary>
		/// One-time copy from the pre-rename cache folder so existing journals keep working.
		/// </summary>
		private static void TryMigrateLegacyCache(string newRoot)
		{
			try
			{
				string legacy = Path.Combine(Paths.CachePath, LegacyFolderName);
				if (!Directory.Exists(legacy))
					return;

				foreach (string src in Directory.GetFiles(legacy))
				{
					string dest = Path.Combine(newRoot, Path.GetFileName(src));
					if (!File.Exists(dest))
						File.Copy(src, dest);
				}
			}
			catch
			{
				// Non-fatal — fresh journal is fine if migrate fails.
			}
		}
	}
}
