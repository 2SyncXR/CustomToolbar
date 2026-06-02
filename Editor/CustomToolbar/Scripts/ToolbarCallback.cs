using System;
using UnityEngine;
using UnityEditor;
using System.Reflection;

#if UNITY_2019_1_OR_NEWER
using UnityEngine.UIElements;
#else
using UnityEngine.Experimental.UIElements;
#endif

namespace UnityToolbarExtender
{
	public static class ToolbarCallback
	{
		/// <summary>
		/// Callback for toolbar OnGUI method.
		/// </summary>
		public static Action OnToolbarGUI;
		public static Action OnToolbarGUILeft;
		public static Action OnToolbarGUIRight;

#if UNITY_6000_3_OR_NEWER
		// Unity 6.3 (6000.3) removed the internal UnityEditor.Toolbar / GUIView / IWindowBackend
		// pipeline and actively blocks the legacy reflection-based injection. Instead we inject our
		// own dock containers into the new UnityEditor.MainToolbarWindow, on either side of the
		// central play/pause button group. See https://github.com/smkplus/CustomToolbar/issues/38
		private static readonly Type m_mainToolbarWindowType =
			typeof(Editor).Assembly.GetType("UnityEditor.MainToolbarWindow");

		private const int k_maxSetupAttempts = 200;
		private static int m_setupAttempts;

		static ToolbarCallback()
		{
			EditorApplication.update -= OnUpdate;
			EditorApplication.update += OnUpdate;
		}

		private static void OnUpdate()
		{
			m_setupAttempts++;

			if (m_mainToolbarWindowType == null)
			{
				EditorApplication.update -= OnUpdate;
				return;
			}

			var toolbars = Resources.FindObjectsOfTypeAll(m_mainToolbarWindowType);
			if (toolbars.Length == 0)
			{
				if (m_setupAttempts > k_maxSetupAttempts)
				{
					Debug.LogWarning("[CustomToolbar] Could not find MainToolbarWindow instance. Aborting.");
					EditorApplication.update -= OnUpdate;
				}
				return;
			}

			var root = ((EditorWindow) toolbars[0]).rootVisualElement;
			if (root == null)
			{
				EditorApplication.update -= OnUpdate;
				return;
			}

			// The central container holds the play/pause/step buttons; we anchor our docks around it.
			var middleContainer = root.Q(className: "unity-overlay-container__middle-container");
			if (middleContainer == null)
			{
				if (m_setupAttempts > k_maxSetupAttempts)
				{
					Debug.LogWarning("[CustomToolbar] Found MainToolbarWindow, but its middle-container is not ready. Aborting.");
					EditorApplication.update -= OnUpdate;
				}
				return;
			}

			var parentContainer = middleContainer.parent;
			if (parentContainer == null)
			{
				EditorApplication.update -= OnUpdate;
				return;
			}

			var leftDock = new VisualElement
			{
				name = "CustomToolbar_LeftDock",
				style =
				{
					flexGrow = 1,
					flexBasis = 0,
					flexDirection = FlexDirection.Row,
					justifyContent = Justify.FlexEnd,
					alignItems = Align.Center
				}
			};

			var rightDock = new VisualElement
			{
				name = "CustomToolbar_RightDock",
				style =
				{
					flexGrow = 1,
					flexBasis = 0,
					flexDirection = FlexDirection.Row,
					justifyContent = Justify.FlexStart,
					alignItems = Align.Center
				}
			};

			parentContainer.Insert(parentContainer.IndexOf(middleContainer), leftDock);
			parentContainer.Insert(parentContainer.IndexOf(middleContainer) + 1, rightDock);

			leftDock.Add(new IMGUIContainer(() => OnToolbarGUILeft?.Invoke()));
			rightDock.Add(new IMGUIContainer(() => OnToolbarGUIRight?.Invoke()));

			EditorApplication.update -= OnUpdate;
		}
#else
		public static Type m_toolbarType = typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");
		public static Type m_guiViewType = typeof(Editor).Assembly.GetType("UnityEditor.GUIView");
#if UNITY_2020_1_OR_NEWER
		public static Type m_iWindowBackendType = typeof(Editor).Assembly.GetType("UnityEditor.IWindowBackend");
		public static PropertyInfo m_windowBackend = m_guiViewType.GetProperty("windowBackend",
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		public static PropertyInfo m_viewVisualTree = m_iWindowBackendType.GetProperty("visualTree",
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
#else
		public static PropertyInfo m_viewVisualTree = m_guiViewType.GetProperty("visualTree",
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
#endif
		public static FieldInfo m_imguiContainerOnGui = typeof(IMGUIContainer).GetField("m_OnGUIHandler",
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		public static ScriptableObject m_currentToolbar;

		static ToolbarCallback()
		{
			EditorApplication.update -= OnUpdate;
			EditorApplication.update += OnUpdate;
		}

		private static void OnUpdate()
		{
			// Relying on the fact that toolbar is ScriptableObject and gets deleted when layout changes
			if (m_currentToolbar == null)
			{
				// Find toolbar
				var toolbars = Resources.FindObjectsOfTypeAll(m_toolbarType);
				m_currentToolbar = toolbars.Length > 0 ? (ScriptableObject) toolbars[0] : null;

				if (m_currentToolbar != null)
				{
#if UNITY_2021_1_OR_NEWER
					var root = m_currentToolbar.GetType().GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
					var rawRoot = root.GetValue(m_currentToolbar);
					var mRoot = rawRoot as VisualElement;
					RegisterCallback("ToolbarZoneLeftAlign", OnToolbarGUILeft);
					RegisterCallback("ToolbarZoneRightAlign", OnToolbarGUIRight);

					void RegisterCallback(string root, Action cb)
					{
						var toolbarZone = mRoot.Q(root);

						var parent = new VisualElement()
						{
							style = {
								flexGrow = 1,
								flexDirection = FlexDirection.Row,
							}
						};
						var container = new IMGUIContainer();
						container.onGUIHandler += () => {
							cb?.Invoke();
						};
						parent.Add(container);
						toolbarZone.Add(parent);
					}
#else
#if UNITY_2020_1_OR_NEWER
					var windowBackend = m_windowBackend.GetValue(m_currentToolbar);

					// Get it's visual tree
					var visualTree = (VisualElement) m_viewVisualTree.GetValue(windowBackend, null);
#else
					// Get it's visual tree
					var visualTree = (VisualElement) m_viewVisualTree.GetValue(m_currentToolbar, null);
#endif
					// Get first child which 'happens' to be toolbar IMGUIContainer
					var container = (IMGUIContainer) visualTree[0];

					// (Re)attach handler
					var handler = (Action) m_imguiContainerOnGui.GetValue(container);
					handler -= OnGUI;
					handler += OnGUI;
					m_imguiContainerOnGui.SetValue(container, handler);
#endif
				}
			}
		}

		static void OnGUI()
		{
			var handler = OnToolbarGUI;
			if (handler != null) handler();
		}
#endif
	}
}
