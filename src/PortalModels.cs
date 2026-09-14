using System;
using System.Collections.Generic;

namespace PortalAtlas
{
	internal sealed class PortalRow
	{
		public string Prefab;
		public string Tag;
		public string Uid;
		public float X;
		public float Y;
		public float Z;
		public string TargetUid;
		public bool Connected;
		public float? TargetX;
		public float? TargetY;
		public float? TargetZ;
		public int SameTagCount;
		public string Relationship;
		public string Issues;

		public string DisplayTag => string.IsNullOrEmpty(Tag) ? "(untagged)" : Tag;

		public string StatusLabel
		{
			get
			{
				if (string.Equals(Relationship, "Mutual pair", StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(Relationship, "Connected", StringComparison.OrdinalIgnoreCase))
					return "connected";
				if (string.Equals(Relationship, "One-way link", StringComparison.OrdinalIgnoreCase))
					return "one-way";
				if (string.Equals(Relationship, "Tag twin (not linked yet)", StringComparison.OrdinalIgnoreCase))
					return "tag twin";
				if (Connected)
					return "connected";
				return "unconnected";
			}
		}

		public bool HasPartnerPosition =>
			TargetX.HasValue && TargetY.HasValue && TargetZ.HasValue &&
			!string.IsNullOrEmpty(TargetUid) &&
			!TargetUid.Equals("None", StringComparison.OrdinalIgnoreCase) &&
			!TargetUid.Equals("0:0", StringComparison.OrdinalIgnoreCase);
	}

	internal sealed class PortalDumpResult
	{
		public List<PortalRow> Rows = new List<PortalRow>();
		public int PortalPrefabCount;
		public string CsvPath;
		public string SummaryCsvPath;
		public string TextPath;
	}

	internal sealed class KnownPortalEntry
	{
		public string Prefab;
		public string Tag;
		public string Uid;
		public float X;
		public float Y;
		public float Z;
		public string TargetUid;
		public float? TargetX;
		public float? TargetY;
		public float? TargetZ;
		public long LastSeenUnix;

		public PortalRow ToRow()
		{
			return new PortalRow
			{
				Prefab = Prefab ?? string.Empty,
				Tag = Tag ?? string.Empty,
				Uid = Uid ?? string.Empty,
				X = X,
				Y = Y,
				Z = Z,
				TargetUid = TargetUid ?? string.Empty,
				Connected = !string.IsNullOrEmpty(TargetUid) &&
				            !TargetUid.Equals("None", StringComparison.OrdinalIgnoreCase) &&
				            !TargetUid.Equals("0:0", StringComparison.OrdinalIgnoreCase) &&
				            TargetX.HasValue,
				TargetX = TargetX,
				TargetY = TargetY,
				TargetZ = TargetZ
			};
		}
	}

	internal sealed class KnownPortalFile
	{
		public string WorldId;
		public string CharacterId;
		public List<KnownPortalEntry> Portals = new List<KnownPortalEntry>();
	}
}
