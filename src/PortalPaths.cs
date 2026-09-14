using System.IO;
using BepInEx;

namespace ValheimPortalList
{
	internal static class PortalPaths
	{
		internal const string FolderName = "ValheimPortalList";

		internal static string CacheRoot
		{
			get
			{
				string root = Path.Combine(Paths.CachePath, FolderName);
				Directory.CreateDirectory(root);
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
	}
}
