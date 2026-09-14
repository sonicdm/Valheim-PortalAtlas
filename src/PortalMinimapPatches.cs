using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PortalAtlas
{
	/// <summary>
	/// Keep large-map zoom stable while the cursor is over the portals panel and the
	/// wheel is used for the list. Blocks zoom setters (no one-frame twitch) and
	/// clears zoom inertia so the map does not keep easing after a blocked scroll.
	/// </summary>
	internal static class PortalMapZoomGuard
	{
		private static float _largeBefore;
		private static float _smallBefore;
		private static bool _guard;
		private static float _suppressUntil;

		internal static bool HasScrollInput()
		{
			if (Mathf.Abs(Input.mouseScrollDelta.y) >= 0.01f)
				return true;
			try
			{
				return Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) >= 0.0001f;
			}
			catch
			{
				return false;
			}
		}

		internal static bool ShouldSuppressZoom()
		{
			if (!PortalPanelUi.IsOpen)
				return false;

			if (HasScrollInput() && PortalPanelUi.ShouldBlockMapZoom())
			{
				_suppressUntil = Time.unscaledTime + 0.12f;
				return true;
			}

			// Kill residual zoom inertia for a couple frames after a blocked scroll.
			return Time.unscaledTime < _suppressUntil && PortalPanelUi.ShouldBlockMapZoom();
		}

		internal static void BeginFrame(Minimap map)
		{
			_guard = false;
			if ((Object)map == null || !ShouldSuppressZoom())
				return;

			_guard = true;
			_largeBefore = ReadFloat(map, "m_largeZoom", () =>
			{
				try { return map.LargeZoom; } catch { return 0f; }
			});
			_smallBefore = ReadFloat(map, "m_smallZoom", () =>
			{
				try { return map.SmallZoom; } catch { return 0f; }
			});
			ClearInertia(map);
		}

		internal static void EndFrame(Minimap map)
		{
			if (!_guard || (Object)map == null)
				return;

			WriteFloat(map, "m_largeZoom", _largeBefore, v =>
			{
				try { map.LargeZoom = v; } catch { }
			});
			WriteFloat(map, "m_smallZoom", _smallBefore, v =>
			{
				try { map.SmallZoom = v; } catch { }
			});
			ClearInertia(map);
		}

		internal static bool AllowZoomSetter()
		{
			return !ShouldSuppressZoom();
		}

		private static void ClearInertia(Minimap map)
		{
			WriteFloat(map, "m_zoomInertia", 0f, null);
			try
			{
				FieldInfo started = typeof(Minimap).GetField("m_startedZooming",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (started != null && started.FieldType == typeof(bool))
					started.SetValue(map, false);
			}
			catch
			{
			}
		}

		private static float ReadFloat(Minimap map, string fieldName, System.Func<float> fallback)
		{
			try
			{
				FieldInfo field = typeof(Minimap).GetField(fieldName,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null && field.FieldType == typeof(float))
					return (float)field.GetValue(map);
			}
			catch
			{
			}

			return fallback != null ? fallback() : 0f;
		}

		private static void WriteFloat(Minimap map, string fieldName, float value, System.Action<float> fallback)
		{
			try
			{
				FieldInfo field = typeof(Minimap).GetField(fieldName,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null && field.FieldType == typeof(float))
				{
					field.SetValue(map, value);
					return;
				}
			}
			catch
			{
			}

			fallback?.Invoke(value);
		}
	}

	[HarmonyPatch(typeof(Minimap), "Update")]
	internal static class Minimap_Update_ZoomGuard
	{
		private static void Prefix(Minimap __instance) => PortalMapZoomGuard.BeginFrame(__instance);

		private static void Postfix(Minimap __instance) => PortalMapZoomGuard.EndFrame(__instance);
	}

	[HarmonyPatch(typeof(Minimap), "set_LargeZoom")]
	internal static class Minimap_SetLargeZoom_Block
	{
		private static bool Prefix() => PortalMapZoomGuard.AllowZoomSetter();
	}

	[HarmonyPatch(typeof(Minimap), "set_SmallZoom")]
	internal static class Minimap_SetSmallZoom_Block
	{
		private static bool Prefix() => PortalMapZoomGuard.AllowZoomSetter();
	}
}
