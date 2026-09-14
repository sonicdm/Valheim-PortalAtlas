using System;
using System.Collections.Generic;
using System.Globalization;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PortalAtlas
{
	internal static class PortalPanelUi
	{
		private const string PanelRootName = "PortalAtlas_Panel";
		private const string MapButtonName = "PortalAtlas_MapButton";
		private const float PanelWidth = 440f;
		private const float PanelHeight = 580f;
		private const float HeaderHeight = 220f;
		private const float FooterHeight = 150f;
		private const int UiFontSize = 14;
		private const int UiLayoutVersion = 5;

		private static GameObject _panelRoot;
		private static GameObject _mapButtonRoot;
		private static Text _statusText;
		private static InputField _filterInput;
		private static Transform _listContent;
		private static ScrollRect _listScroll;
		private static RectTransform _listScrollRect;
		private static Text _detailText;
		private static Button _refreshButton;
		private static Button _showKnownButton;
		private static Button _pinButton;
		private static Button _pingExitButton;
		private static Button _addJournalButton;
		private static Toggle _autoPinToggle;
		private static readonly List<GameObject> _rowObjects = new List<GameObject>();

		private static List<PortalRow> _journalRows = new List<PortalRow>();
		private static List<PortalRow> _worldRows;
		private static List<PortalRow> _visible = new List<PortalRow>();
		private static PortalRow _selected;
		private static bool _guiReady;
		private static bool _panelOpen;
		private static bool _showingWorld;
		private static bool _sortByName;
		private static bool _sortByMapClick;
		private static Vector3? _mapClickOrigin;
		private static bool _inputBlocked;
		private static bool _awaitingRefresh;
		private static bool _awaitingMapClick;
		private static Text _sortHintText;
		private static int _builtLayoutVersion;

		private static bool _mapWasOpen;
		private static float _nextRetintTime;
		private static bool _lastCanRefresh;

		internal static bool IsOpen => _panelOpen;

		internal static void OnGuiReady()
		{
			_guiReady = GUIManager.Instance != null && GUIManager.CustomGUIFront != null;
			EnsureMapButton();
		}

		internal static void Tick()
		{
			if (!_guiReady && GUIManager.Instance != null)
				_guiReady = true;

			if (!_guiReady || GUIManager.IsHeadless())
				return;

			bool mapOpen = PortalMapPins.IsLargeMapOpen();
			if (mapOpen != _mapWasOpen)
			{
				_mapWasOpen = mapOpen;
				EnsureMapButton();
				if (_panelOpen && !mapOpen)
					Hide();
			}
			else if (mapOpen)
			{
				// Keep button alive if map UI rebuilt, but not every frame.
				if ((Object)_mapButtonRoot == null || !_mapButtonRoot.activeSelf)
					EnsureMapButton();
			}

			SyncBlockInput();

			if (_panelOpen)
			{
				TryScrollListWithWheel();

				// Admin sync can arrive after login; show Refresh once PlayerIsAdmin flips true.
				bool canRefresh = PortalAccess.CanRefreshWorld();
				if (canRefresh != _lastCanRefresh)
				{
					_lastCanRefresh = canRefresh;
					RefreshChrome();
				}
			}

			if (_panelOpen && mapOpen)
			{
				if (Time.unscaledTime >= _nextRetintTime)
				{
					_nextRetintTime = Time.unscaledTime + 0.5f;
					PortalMapPins.RetintOverlay();
				}

				if (_awaitingMapClick && Input.GetMouseButtonDown(0) && !IsPointerOverOurPanel())
				{
					if (PortalMapPins.TryMapClickToWorld(out Vector3 world))
					{
						_mapClickOrigin = world;
						_sortByMapClick = true;
						_sortByName = false;
						_awaitingMapClick = false;
						PortalAtlasPlugin.Debug($"UI map-click origin=({world.x:0.#},{world.z:0.#})");
						RebuildList();
						UpdateOverlay();
						RefreshChrome();
					}
				}
			}
		}

		internal static void Toggle()
		{
			if (_panelOpen)
				Hide();
			else
				Show();
		}

		internal static void Show()
		{
			if (GUIManager.IsHeadless())
				return;

			PortalAtlasPlugin.Debug("UI Show — journal view");
			EnsurePanel();
			_panelOpen = true;
			if ((Object)_panelRoot != null)
				_panelRoot.SetActive(true);

			_showingWorld = false;
			_worldRows = null;
			ReloadJournal();
			RefreshChrome();
			RebuildList();
			UpdateOverlay();
		}

		internal static void Hide()
		{
			PortalAtlasPlugin.Debug("UI Hide");
			_panelOpen = false;
			_awaitingMapClick = false;
			if ((Object)_panelRoot != null)
				_panelRoot.SetActive(false);
			PortalMapPins.ClearOverlay();
			SetBlockInput(false);
			KnownPortalCache.FlushIfDirty();
		}

		internal static void DestroyUi()
		{
			Hide();
			if ((Object)_panelRoot != null)
			{
				Object.Destroy(_panelRoot);
				_panelRoot = null;
			}
			if ((Object)_mapButtonRoot != null)
			{
				Object.Destroy(_mapButtonRoot);
				_mapButtonRoot = null;
			}
			_rowObjects.Clear();
			PortalAtlasPlugin.Debug("UI DestroyUi");
		}

		internal static void OnWorldList(List<PortalRow> rows)
		{
			_awaitingRefresh = false;
			if (rows == null)
			{
				PortalAtlasPlugin.Debug("UI OnWorldList failed (null)");
				SetStatus("Refresh failed.");
				return;
			}

			PortalAtlasPlugin.Debug($"UI OnWorldList session rows={rows.Count} (journal untouched)");
			_worldRows = rows;
			_showingWorld = true;
			RefreshChrome();
			RebuildList();
			UpdateOverlay();
			SetStatus($"World list · {rows.Count} portals (not added to journal)");
		}

		private static void EnsureMapButton()
		{
			if (GUIManager.IsHeadless() || GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
				return;

			if (!PortalMapPins.IsLargeMapOpen())
			{
				if ((Object)_mapButtonRoot != null)
					_mapButtonRoot.SetActive(false);
				return;
			}

			// Bottom-left of the large map parchment (not the full screen / HUD).
			Transform parent = PortalMapPins.GetMapPanelRoot();
			if ((Object)parent == null)
				parent = GUIManager.CustomGUIFront.transform;

			if ((Object)_mapButtonRoot == null)
			{
				GameObject buttonGo = GUIManager.Instance.CreateButton(
					text: "Portals",
					parent: parent,
					anchorMin: new Vector2(0f, 0f),
					anchorMax: new Vector2(0f, 0f),
					position: new Vector2(36f, 36f),
					width: 110f,
					height: 34f);
				buttonGo.name = MapButtonName;
				_mapButtonRoot = buttonGo;
				Button button = buttonGo.GetComponent<Button>();
				if ((Object)button != null)
					button.onClick.AddListener(Toggle);
				GUIManager.Instance.ApplyButtonStyle(button, UiFontSize);
			}

			if (_mapButtonRoot.transform.parent != parent)
				_mapButtonRoot.transform.SetParent(parent, false);

			RectTransform rt = _mapButtonRoot.GetComponent<RectTransform>();
			if ((Object)rt != null)
			{
				rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
				rt.pivot = new Vector2(0f, 0f);
				rt.anchoredPosition = new Vector2(36f, 36f);
				rt.sizeDelta = new Vector2(110f, 34f);
			}

			_mapButtonRoot.SetActive(true);
			_mapButtonRoot.transform.SetAsLastSibling();
		}

		private static void EnsurePanel()
		{
			if ((Object)_panelRoot != null && _builtLayoutVersion != UiLayoutVersion)
			{
				Object.Destroy(_panelRoot);
				_panelRoot = null;
				_listContent = null;
				_listScroll = null;
				_listScrollRect = null;
				_statusText = null;
				_filterInput = null;
				_detailText = null;
				_sortHintText = null;
				_refreshButton = null;
				_showKnownButton = null;
				_pinButton = null;
				_pingExitButton = null;
				_addJournalButton = null;
				_autoPinToggle = null;
				_rowObjects.Clear();
			}

			if ((Object)_panelRoot != null || GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
				return;

			GameObject wood = GUIManager.Instance.CreateWoodpanel(
				parent: GUIManager.CustomGUIFront.transform,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: new Vector2(-220f, 20f),
				width: PanelWidth,
				height: PanelHeight,
				draggable: true);

			wood.name = PanelRootName;
			_panelRoot = wood;

			float y = PanelHeight * 0.5f - 28f;

			Text title = CreateLabel(wood.transform, "Portals", new Vector2(0f, y), 200f, 28f, true);
			title.alignment = TextAnchor.MiddleCenter;
			title.color = GUIManager.Instance.ValheimOrange;

			GameObject closeGo = GUIManager.Instance.CreateButton(
				text: "X",
				parent: wood.transform,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: new Vector2(PanelWidth * 0.5f - 28f, y),
				width: 32f,
				height: 28f);
			closeGo.GetComponent<Button>().onClick.AddListener(Hide);

			y -= 28f;
			_statusText = CreateLabel(wood.transform, "Known portals", new Vector2(0f, y), PanelWidth - 40f, 22f, false);

			y -= 32f;
			GameObject refreshGo = GUIManager.Instance.CreateButton(
				text: "Refresh world",
				parent: wood.transform,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: new Vector2(-70f, y),
				width: 130f,
				height: 28f);
			_refreshButton = refreshGo.GetComponent<Button>();
			_refreshButton.onClick.AddListener(OnRefreshClicked);
			GUIManager.Instance.ApplyButtonStyle(_refreshButton, UiFontSize);

			GameObject knownGo = GUIManager.Instance.CreateButton(
				text: "Show known",
				parent: wood.transform,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: new Vector2(70f, y),
				width: 120f,
				height: 28f);
			_showKnownButton = knownGo.GetComponent<Button>();
			_showKnownButton.onClick.AddListener(OnShowKnown);
			GUIManager.Instance.ApplyButtonStyle(_showKnownButton, UiFontSize);

			y -= 34f;
			GameObject filterGo = GUIManager.Instance.CreateInputField(
				parent: wood.transform,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: new Vector2(0f, y),
				contentType: InputField.ContentType.Standard,
				placeholderText: "Filter tags",
				fontSize: UiFontSize,
				width: PanelWidth - 48f,
				height: 28f);
			_filterInput = filterGo.GetComponent<InputField>();
			GUIManager.Instance.ApplyInputFieldStyle(_filterInput, UiFontSize);
			_filterInput.onValueChanged.AddListener(_ => RebuildList());

			y -= 30f;
			CreateSortButton(wood.transform, "Name", new Vector2(-120f, y), () =>
			{
				_sortByName = true;
				_sortByMapClick = false;
				_awaitingMapClick = false;
				RebuildList();
				RefreshChrome();
			});
			CreateSortButton(wood.transform, "To me", new Vector2(0f, y), () =>
			{
				_sortByName = false;
				_sortByMapClick = false;
				_mapClickOrigin = null;
				_awaitingMapClick = false;
				RebuildList();
				RefreshChrome();
			});
			CreateSortButton(wood.transform, "Map click", new Vector2(120f, y), () =>
			{
				_sortByName = false;
				_sortByMapClick = true;
				_awaitingMapClick = true;
				SetStatus("Map click — next map click sets sort origin");
				RefreshChrome();
			});

			y -= 22f;
			_sortHintText = CreateLabel(wood.transform, "Sort: to me", new Vector2(0f, y), PanelWidth - 40f, 20f, false);
			_sortHintText.fontSize = 12;
			_sortHintText.color = new Color(0.85f, 0.8f, 0.7f, 1f);

			// Scroll fills between header and footer so controls stay visible.
			float listTop = PanelHeight * 0.5f - HeaderHeight;
			float listBottom = -PanelHeight * 0.5f + FooterHeight;
			float listHeight = listTop - listBottom;
			float listCenterY = (listTop + listBottom) * 0.5f;

			ColorBlock handleColors = ColorBlock.defaultColorBlock;
			handleColors.normalColor = new Color(0.6f, 0.35f, 0.12f, 1f);
			handleColors.highlightedColor = GUIManager.Instance.ValheimOrange;
			handleColors.pressedColor = new Color(0.9f, 0.55f, 0.15f, 1f);
			handleColors.selectedColor = handleColors.highlightedColor;

			GameObject scrollGo = GUIManager.Instance.CreateScrollView(
				parent: wood.transform,
				showHorizontalScrollbar: false,
				showVerticalScrollbar: true,
				handleSize: 10f,
				handleDistanceToBorder: 3f,
				handleColors: handleColors,
				slidingAreaBackgroundColor: new Color(0f, 0f, 0f, 0.45f),
				width: PanelWidth - 40f,
				height: Mathf.Max(120f, listHeight));
			scrollGo.name = "PortalListScroll";
			RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
			scrollRt.anchorMin = scrollRt.anchorMax = new Vector2(0.5f, 0.5f);
			scrollRt.pivot = new Vector2(0.5f, 0.5f);
			scrollRt.anchoredPosition = new Vector2(0f, listCenterY);
			scrollRt.sizeDelta = new Vector2(PanelWidth - 40f, Mathf.Max(120f, listHeight));

			// Keep scroll under footer controls in draw order.
			scrollGo.transform.SetSiblingIndex(2);

			ScrollRect scroll = scrollGo.GetComponentInChildren<ScrollRect>(true);
			if ((Object)scroll == null)
				scroll = scrollGo.GetComponent<ScrollRect>();
			if ((Object)scroll != null)
			{
				scroll.horizontal = false;
				scroll.vertical = true;
				scroll.movementType = ScrollRect.MovementType.Clamped;
				scroll.scrollSensitivity = 40f;
				_listScroll = scroll;
				_listContent = scroll.content;
				_listScrollRect = scroll.GetComponent<RectTransform>();
				if ((Object)_listScrollRect == null)
					_listScrollRect = scrollRt;

				// Ensure the viewport can receive pointer hits (needed for wheel + drag).
				if ((Object)scroll.viewport != null)
				{
					Image vpImage = scroll.viewport.GetComponent<Image>();
					if ((Object)vpImage == null)
						vpImage = scroll.viewport.gameObject.AddComponent<Image>();
					vpImage.color = new Color(0f, 0f, 0f, 0.01f);
					vpImage.raycastTarget = true;
				}
			}

			if ((Object)_listContent != null)
			{
				VerticalLayoutGroup vlg = _listContent.GetComponent<VerticalLayoutGroup>();
				if ((Object)vlg == null)
					vlg = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
				vlg.childForceExpandHeight = false;
				vlg.childForceExpandWidth = true;
				vlg.childControlHeight = true;
				vlg.childControlWidth = true;
				vlg.spacing = 2f;
				vlg.padding = new RectOffset(4, 4, 4, 4);

				ContentSizeFitter fitter = _listContent.GetComponent<ContentSizeFitter>();
				if ((Object)fitter == null)
					fitter = _listContent.gameObject.AddComponent<ContentSizeFitter>();
				fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
				fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			}

			// Footer: Pin / Ping / Ping exit, then journal + clear, then auto-pin.
			float footerBase = -PanelHeight * 0.5f;
			_detailText = CreateLabel(wood.transform, string.Empty, new Vector2(0f, footerBase + 130f), PanelWidth - 40f, 34f, false);

			float actionY = footerBase + 92f;
			GameObject pinGo = CreateActionButton(wood.transform, "Pin", new Vector2(-130f, actionY), OnPinSelected);
			_pinButton = pinGo.GetComponent<Button>();
			CreateActionButton(wood.transform, "Ping", new Vector2(0f, actionY), OnPing);
			GameObject exitGo = CreateActionButton(wood.transform, "Ping exit", new Vector2(130f, actionY), OnPingExit);
			_pingExitButton = exitGo.GetComponent<Button>();

			float addY = footerBase + 58f;
			GameObject addGo = CreateActionButton(wood.transform, "Add to journal", new Vector2(-70f, addY), OnAddToJournal);
			_addJournalButton = addGo.GetComponent<Button>();
			CreateActionButton(wood.transform, "Clear saved", new Vector2(70f, addY), () =>
			{
				PortalMapPins.ClearSavedPins();
				SetStatus("Cleared this mod's saved portal pins (manual pins untouched).");
			});

			float toggleY = footerBase + 24f;
			GameObject toggleGo = GUIManager.Instance.CreateToggle(wood.transform, 28f, 28f);
			toggleGo.name = "AutoPin";
			RectTransform toggleRt = toggleGo.GetComponent<RectTransform>();
			toggleRt.anchorMin = toggleRt.anchorMax = new Vector2(0.5f, 0.5f);
			toggleRt.pivot = new Vector2(0.5f, 0.5f);
			toggleRt.anchoredPosition = new Vector2(-(PanelWidth * 0.5f - 40f), toggleY);
			toggleRt.sizeDelta = new Vector2(28f, 28f);
			_autoPinToggle = toggleGo.GetComponent<Toggle>();
			if ((Object)_autoPinToggle != null)
			{
				_autoPinToggle.isOn = PortalAtlasPlugin.AutoPin != null && PortalAtlasPlugin.AutoPin.Value;
				_autoPinToggle.onValueChanged.AddListener(v =>
				{
					if (PortalAtlasPlugin.AutoPin != null)
						PortalAtlasPlugin.AutoPin.Value = v;
					PortalAtlasPlugin.Debug($"AutoPin toggle → {v}");
				});
			}

			Text toggleLabel = CreateLabel(
				wood.transform,
				"Auto-pin when I approach a portal",
				new Vector2(18f, toggleY),
				PanelWidth - 90f,
				26f,
				false);
			toggleLabel.alignment = TextAnchor.MiddleLeft;

			_panelRoot.SetActive(false);
			_builtLayoutVersion = UiLayoutVersion;
			PortalRpc.OnWorldListReceived = OnWorldList;
		}

		private static GameObject CreateActionButton(Transform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction action)
		{
			GameObject go = GUIManager.Instance.CreateButton(
				text: text,
				parent: parent,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: pos,
				width: 110f,
				height: 28f);
			Button button = go.GetComponent<Button>();
			button.onClick.AddListener(action);
			GUIManager.Instance.ApplyButtonStyle(button, UiFontSize);
			return go;
		}

		private static void CreateSortButton(Transform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction action)
		{
			GameObject go = GUIManager.Instance.CreateButton(
				text: text,
				parent: parent,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: pos,
				width: 100f,
				height: 26f);
			go.GetComponent<Button>().onClick.AddListener(action);
			GUIManager.Instance.ApplyButtonStyle(go.GetComponent<Button>(), 12);
		}

		private static Text CreateLabel(Transform parent, string text, Vector2 pos, float width, float height, bool bold)
		{
			GameObject go = new GameObject("Label", typeof(RectTransform), typeof(Text));
			go.transform.SetParent(parent, false);
			RectTransform rt = go.GetComponent<RectTransform>();
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.sizeDelta = new Vector2(width, height);
			rt.anchoredPosition = pos;
			Text label = go.GetComponent<Text>();
			label.text = text;
			label.alignment = TextAnchor.MiddleCenter;
			label.font = GUIManager.Instance.AveriaSerifBold;
			if ((Object)label.font == null)
				label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			label.fontSize = bold ? 18 : UiFontSize;
			label.color = Color.white;
			GUIManager.Instance.ApplyTextStyle(label, bold ? 18 : UiFontSize);
			return label;
		}

		private static void ReloadJournal()
		{
			KnownPortalCache.EnsureLoaded();
			_journalRows = PortalScan.RowsFromKnown(KnownPortalCache.All);
		}

		private static void RebuildList()
		{
			foreach (GameObject row in _rowObjects)
			{
				if ((Object)row != null)
					Object.Destroy(row);
			}
			_rowObjects.Clear();

			List<PortalRow> source = _showingWorld && _worldRows != null ? _worldRows : _journalRows;
			string filter = _filterInput != null ? (_filterInput.text ?? string.Empty).Trim() : string.Empty;

			_visible = new List<PortalRow>();
			foreach (PortalRow row in source)
			{
				if (!string.IsNullOrEmpty(filter) &&
				    (row.Tag ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
				    !string.Equals(filter, "(untagged)", StringComparison.OrdinalIgnoreCase))
					continue;
				_visible.Add(row);
			}

			Vector3 origin = GetSortOrigin();
			_visible.Sort((a, b) =>
			{
				if (_sortByName)
				{
					int c = string.Compare(a.DisplayTag, b.DisplayTag, StringComparison.OrdinalIgnoreCase);
					if (c != 0) return c;
				}

				float da = DistXz(origin, a);
				float db = DistXz(origin, b);
				return da.CompareTo(db);
			});

			if (_visible.Count > 0)
			{
				if (_selected == null || !_visible.Contains(_selected))
					_selected = _visible[0];
			}
			else
			{
				_selected = null;
			}

			foreach (PortalRow row in _visible)
				CreateRow(row);

			UpdateDetail();
			RefreshChrome();
		}

		private static void CreateRow(PortalRow row)
		{
			GameObject go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
			go.transform.SetParent(_listContent, false);
			LayoutElement le = go.GetComponent<LayoutElement>();
			le.minHeight = 28f;
			le.preferredHeight = 28f;
			Image bg = go.GetComponent<Image>();
			bool selected = _selected == row;
			bg.color = selected ? new Color(0.77f, 0.36f, 0.07f, 0.85f) : new Color(0f, 0f, 0f, 0.25f);
			Button button = go.GetComponent<Button>();
			PortalRow captured = row;
			button.onClick.AddListener(() =>
			{
				_selected = captured;
				RebuildList();
				UpdateOverlay();
			});

			float dist = DistXz(GetSortOrigin(), row);
			string label = $"{row.DisplayTag}   {Mathf.RoundToInt(dist)} m   {row.StatusLabel}";
			Text text = CreateLabel(go.transform, label, Vector2.zero, PanelWidth - 60f, 26f, false);
			text.alignment = TextAnchor.MiddleLeft;
			RectTransform trt = text.GetComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.offsetMin = new Vector2(8f, 0f);
			trt.offsetMax = new Vector2(-8f, 0f);

			_rowObjects.Add(go);
		}

		private static void UpdateDetail()
		{
			if ((Object)_detailText == null)
				return;

			bool hasExit = _selected != null && TryGetExitPosition(_selected, out _);

			if (_selected == null)
			{
				_detailText.text = _visible.Count == 0
					? "Travel through a portal or stand next to one to add it here."
					: string.Empty;
				if ((Object)_pinButton != null)
					_pinButton.interactable = false;
				if ((Object)_pingExitButton != null)
					_pingExitButton.interactable = false;
				return;
			}

			string partner = hasExit
				? $"exit at ({PortalScan.Num(_selected.TargetX ?? 0f)}, {PortalScan.Num(_selected.TargetZ ?? 0f)})"
				: "no exit known";
			if (hasExit && TryGetExitPosition(_selected, out Vector3 exit))
				partner = $"exit at ({PortalScan.Num(exit.x)}, {PortalScan.Num(exit.z)})";

			_detailText.text = $"{_selected.DisplayTag} · {_selected.StatusLabel} · {partner}";
			if ((Object)_pinButton != null)
				_pinButton.interactable = true;
			if ((Object)_pingExitButton != null)
				_pingExitButton.interactable = hasExit;
		}

		private static void RefreshChrome()
		{
			bool canRefresh = PortalAccess.CanRefreshWorld();
			_lastCanRefresh = canRefresh;
			if ((Object)_refreshButton != null)
				_refreshButton.gameObject.SetActive(canRefresh);
			if ((Object)_showKnownButton != null)
				_showKnownButton.gameObject.SetActive(_showingWorld);
			if ((Object)_addJournalButton != null)
			{
				_addJournalButton.gameObject.SetActive(_showingWorld);
				_addJournalButton.interactable = _showingWorld && _selected != null;
			}
			if ((Object)_pinButton != null)
				_pinButton.interactable = _selected != null;
			if ((Object)_pingExitButton != null)
				_pingExitButton.interactable = _selected != null && TryGetExitPosition(_selected, out _);

			int count = _visible != null ? _visible.Count : 0;
			string source = _showingWorld ? "World list (session only)" : "Known portals";
			if (_awaitingRefresh)
				source = "Refreshing…";

			string sort;
			if (_sortByName)
				sort = "sort: name";
			else if (_sortByMapClick)
			{
				if (_mapClickOrigin.HasValue)
					sort = $"sort: map ({_mapClickOrigin.Value.x:0},{_mapClickOrigin.Value.z:0})";
				else
					sort = "sort: map (click the map)";
			}
			else
				sort = "sort: to me";

			SetStatus($"{source} · {count} · {sort}");

			if ((Object)_sortHintText != null)
			{
				if (_awaitingMapClick)
					_sortHintText.text = "Waiting for next map click to set distance origin…";
				else if (_sortByMapClick && _mapClickOrigin.HasValue)
					_sortHintText.text = $"Distances measured from map click ({_mapClickOrigin.Value.x:0}, {_mapClickOrigin.Value.z:0})";
				else if (_sortByMapClick)
					_sortHintText.text = "Press Map click, then click the map once";
				else if (_sortByName)
					_sortHintText.text = "Sorted alphabetically by portal tag";
				else
					_sortHintText.text = "Distances measured from your position";
			}

			if ((Object)_autoPinToggle != null && PortalAtlasPlugin.AutoPin != null)
				_autoPinToggle.isOn = PortalAtlasPlugin.AutoPin.Value;
		}

		private static void UpdateOverlay()
		{
			if (!_panelOpen)
			{
				PortalMapPins.ClearOverlay();
				return;
			}

			if (PortalMapPins.IsLargeMapOpen())
				PortalMapPins.ShowOverlay(_visible);
			else
				PortalMapPins.ClearOverlay();
		}

		private static void OnRefreshClicked()
		{
			PortalAtlasPlugin.Debug($"UI Refresh clicked ({PortalAccess.DescribeRefreshAccess()})");
			if (!PortalAccess.CanRefreshWorld())
			{
				SetStatus("Refresh requires adminlist or host+devcommands.");
				return;
			}

			_awaitingRefresh = true;
			SetStatus("Refreshing…");
			if (!PortalRpc.RequestWorldList())
			{
				_awaitingRefresh = false;
				SetStatus("Could not refresh (no server RPC).");
			}

			if (ZNet.instance != null && ZNet.instance.IsServer())
				_awaitingRefresh = false;
		}

		private static void OnShowKnown()
		{
			PortalAtlasPlugin.Debug("UI Show known (journal)");
			_showingWorld = false;
			_worldRows = null;
			ReloadJournal();
			RebuildList();
			UpdateOverlay();
		}

		private static void OnAddToJournal()
		{
			if (!_showingWorld || _selected == null)
			{
				SetStatus("Select a portal from the world list first.");
				return;
			}

			PortalAtlasPlugin.Debug(
				$"UI Add to journal tag='{_selected.DisplayTag}' uid={_selected.Uid} prefab={_selected.Prefab}");

			if (KnownPortalCache.AddFromRow(_selected))
			{
				SetStatus($"Added {_selected.DisplayTag} to journal.");
				ReloadJournal();
			}
			else
			{
				SetStatus("Could not add that portal to the journal.");
			}
		}

		private static void OnPinSelected()
		{
			if (_selected == null)
			{
				SetStatus("Select a portal first.");
				return;
			}

			string message;
			bool ok = PortalMapPins.TryPinPortal(_selected, out message);
			SetStatus(message);
			PortalAtlasPlugin.Debug($"UI Pin selected → ok={ok} {message}");
			UpdateOverlay();
		}

		private static void OnPing()
		{
			if (_selected == null)
				return;
			PortalMapPins.Ping(new Vector3(_selected.X, _selected.Y, _selected.Z));
			UpdateOverlay();
			SetStatus($"Pinged {_selected.DisplayTag}.");
		}

		private static void OnPingExit()
		{
			if (_selected == null || !TryGetExitPosition(_selected, out Vector3 exit))
			{
				SetStatus("No exit portal position known for that entry.");
				return;
			}

			PortalMapPins.Ping(exit);
			UpdateOverlay();
			SetStatus($"Pinged exit for {_selected.DisplayTag}.");
		}

		/// <summary>
		/// Resolve the paired exit: stored target coords, TargetUid match, or same-tag twin in the current lists.
		/// </summary>
		private static bool TryGetExitPosition(PortalRow row, out Vector3 exit)
		{
			exit = Vector3.zero;
			if (row == null)
				return false;

			if (row.HasPartnerPosition)
			{
				exit = new Vector3(row.TargetX.Value, row.TargetY.Value, row.TargetZ.Value);
				return true;
			}

			IEnumerable<PortalRow> pools = _visible;
			if (_showingWorld && _worldRows != null)
				pools = _worldRows;
			else if (_journalRows != null)
				pools = _journalRows;

			if (!string.IsNullOrEmpty(row.TargetUid) &&
			    !row.TargetUid.Equals("None", StringComparison.OrdinalIgnoreCase) &&
			    !row.TargetUid.Equals("0:0", StringComparison.OrdinalIgnoreCase))
			{
				foreach (PortalRow other in pools)
				{
					if (other == null || other == row)
						continue;
					if (string.Equals(other.Uid, row.TargetUid, StringComparison.OrdinalIgnoreCase))
					{
						exit = new Vector3(other.X, other.Y, other.Z);
						return true;
					}
				}
			}

			string tag = row.Tag ?? string.Empty;
			if (string.IsNullOrEmpty(tag))
				return false;

			PortalRow twin = null;
			int twins = 0;
			foreach (PortalRow other in pools)
			{
				if (other == null || other == row)
					continue;
				if (!string.Equals(other.Tag ?? string.Empty, tag, StringComparison.OrdinalIgnoreCase))
					continue;
				if (string.Equals(other.Uid, row.Uid, StringComparison.OrdinalIgnoreCase))
					continue;
				twin = other;
				twins++;
			}

			if (twins == 1 && twin != null)
			{
				exit = new Vector3(twin.X, twin.Y, twin.Z);
				return true;
			}

			return false;
		}

		private static Vector3 GetSortOrigin()
		{
			if (_sortByMapClick && _mapClickOrigin.HasValue)
				return _mapClickOrigin.Value;
			Player player = Player.m_localPlayer;
			if ((Object)player != null)
				return player.transform.position;
			return Vector3.zero;
		}

		private static float DistXz(Vector3 origin, PortalRow row)
		{
			float dx = origin.x - row.X;
			float dz = origin.z - row.Z;
			return Mathf.Sqrt(dx * dx + dz * dz);
		}

		private static void SetStatus(string text)
		{
			if ((Object)_statusText != null)
				_statusText.text = text;
		}

		internal static bool IsFilterFocused =>
			(Object)_filterInput != null && _filterInput.isFocused;

		private static bool FilterFocused => IsFilterFocused;

		private static void SyncBlockInput()
		{
			SetBlockInput(_panelOpen && FilterFocused);
		}

		private static void SetBlockInput(bool block)
		{
			if (_inputBlocked == block)
				return;
			_inputBlocked = block;
			GUIManager.BlockInput(block);
		}

		/// <summary>
		/// True when scroll should not zoom the large map (panel open + cursor over the panel).
		/// </summary>
		internal static bool ShouldBlockMapZoom()
		{
			return _panelOpen && IsPointerOverPanelArea();
		}

		private static bool IsPointerOverPanelArea()
		{
			if ((Object)_panelRoot == null || !_panelRoot.activeInHierarchy)
				return false;

			RectTransform panelRt = _panelRoot.GetComponent<RectTransform>();
			if ((Object)panelRt != null)
			{
				Canvas canvas = panelRt.GetComponentInParent<Canvas>();
				Camera cam = null;
				if ((Object)canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
					cam = canvas.worldCamera;

				if (RectTransformUtility.RectangleContainsScreenPoint(panelRt, Input.mousePosition, cam))
					return true;
			}

			return IsPointerOverOurPanel();
		}

		private static void TryScrollListWithWheel()
		{
			if (!_panelOpen || (Object)_listScroll == null)
				return;

			float wheel = Input.mouseScrollDelta.y;
			if (Mathf.Abs(wheel) < 0.01f)
				return;

			// List only scrolls when the cursor is over the list; the whole panel still blocks map zoom.
			if (!IsPointerOverScrollList())
				return;

			RectTransform content = _listScroll.content;
			RectTransform viewport = _listScroll.viewport;
			if ((Object)content == null)
				return;

			float viewH = (Object)viewport != null ? viewport.rect.height : _listScrollRect.rect.height;
			float contentH = content.rect.height;
			float scrollable = Mathf.Max(1f, contentH - viewH);
			float delta = (wheel * _listScroll.scrollSensitivity) / scrollable;
			_listScroll.velocity = Vector2.zero;
			_listScroll.verticalNormalizedPosition = Mathf.Clamp01(_listScroll.verticalNormalizedPosition + delta);
		}

		private static bool IsPointerOverScrollList()
		{
			if ((Object)_listScrollRect == null)
				return false;

			Canvas canvas = _listScrollRect.GetComponentInParent<Canvas>();
			Camera cam = null;
			if ((Object)canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
				cam = canvas.worldCamera;

			if (RectTransformUtility.RectangleContainsScreenPoint(_listScrollRect, Input.mousePosition, cam))
				return true;

			if ((Object)_listScroll != null && (Object)_listScroll.viewport != null &&
			    RectTransformUtility.RectangleContainsScreenPoint(_listScroll.viewport, Input.mousePosition, cam))
				return true;

			return false;
		}

		private static bool IsPointerOverOurPanel()
		{
			if (EventSystem.current == null)
				return false;

			PointerEventData pointer = new PointerEventData(EventSystem.current)
			{
				position = Input.mousePosition
			};
			List<RaycastResult> hits = new List<RaycastResult>();
			EventSystem.current.RaycastAll(pointer, hits);
			foreach (RaycastResult hit in hits)
			{
				if ((Object)hit.gameObject == null)
					continue;
				Transform t = hit.gameObject.transform;
				if ((Object)_panelRoot != null && (t == _panelRoot.transform || t.IsChildOf(_panelRoot.transform)))
					return true;
				if ((Object)_mapButtonRoot != null && (t == _mapButtonRoot.transform || t.IsChildOf(_mapButtonRoot.transform)))
					return true;
			}

			return false;
		}
	}
}
