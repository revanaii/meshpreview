using UnityEngine;
using System.Reflection;
using System;
using System.Linq;
using UnityEngine.Rendering;

namespace UnityEditor
{
    [CustomEditor(typeof(Mesh))]
    [CanEditMultipleObjects]
    internal class ModelInspector : Editor
    {
        internal static class Styles
        {
            public static readonly GUIContent wireframeToggle = EditorGUIUtility.TrTextContent("Wireframe", "Show wireframe");
            public static GUIContent displayModeDropdown = EditorGUIUtility.TrTextContent("", "Change display mode");
            public static GUIContent uvChannelDropdown = EditorGUIUtility.TrTextContent("", "Change active UV channel");
            
            public static GUIStyle preSlider = "preSlider";
            public static GUIStyle preSliderThumb = "preSliderThumb";
        }

        private PreviewRenderUtility m_PreviewUtility;
        
        private Material m_ShadedPreviewMaterial;
        private static Material m_MeshMultiPreviewMaterial;
        private Material m_WireMaterial;
        private static Material m_LineMaterial;

        private Material m_activeMaterial;

        private Texture2D m_CheckeredTexture;

        public static Vector2 previewDir = new Vector2(-120, 20);        
        
        static bool drawWire = true;
        
        private static int activeUVChannel = 0;
        private static DisplayMode displayMode = DisplayMode.Shaded;
        
        private static float m_ZoomFactor = 1.0f;
        private static Vector3 m_OrthoPosition = new Vector3(0.5f,0.5f,-1);
        
        private static string[] m_DisplayModes =
        {
            "Shaded", "UV Checker", "UV Layout",
            "Vertex Color", "Normals", "Tangents"
        };

        private static string[] m_UVChannels =
        {
            "Channel 0", "Channel 1", "Channel 2", "Channel 3", "Channel 4", "Channel 5", "Channel 6", "Channel 7"
        };

        enum DisplayMode
        {
            Shaded = 0,
            UVChecker = 1,
            UVLayout = 2,
            VertexColor = 3,
            Normals = 4,
            Tangent = 5
        }

        private static bool[] m_AvailableDisplayModes;
        private static bool[] m_AvailableUVChannels;

        private int m_checkerTextureMultiplier = 10;
        
        internal static Material CreateWireframeMaterial()
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (!shader)
            {
                Debug.LogWarning("Could not find the builtin Colored shader");
                return null;
            }
            var mat = new Material(shader);
            mat.hideFlags = HideFlags.HideAndDontSave;
            mat.SetColor("_Color", new Color(0, 0, 0, 0.3f));
            mat.SetInt("_ZWrite", 0);
            mat.SetFloat("_ZBias", -1.0f);
            return mat;
        }

        static Material CreateMeshMultiPreviewMaterial()
        {
            var shader = EditorGUIUtility.LoadRequired("Previews/MeshPreviewShader.shader") as Shader;
            if (!shader)
            {
                Debug.LogWarning("Could not find the built in Mesh preview shader");
                return null;
            }
            var mat = new Material(shader);
            mat.hideFlags = HideFlags.HideAndDontSave;
            return mat;
        }
        
        static Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (!shader)
            {
                Debug.LogWarning("Could not find the builtin Colored shader");
                return null;
            }
            var mat = new Material(shader);
            mat.hideFlags = HideFlags.HideAndDontSave;
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.SetInt("_ZWrite", 0);
            return mat;
        }
        
        void Init()
        {
            if (m_PreviewUtility == null)
            {
                m_PreviewUtility = new PreviewRenderUtility();
                m_PreviewUtility.camera.fieldOfView = 30.0f;
                m_PreviewUtility.camera.transform.position = new Vector3(5,5,0);
                
                m_ShadedPreviewMaterial = new Material(Shader.Find("Standard"));
                m_WireMaterial = CreateWireframeMaterial();
                m_MeshMultiPreviewMaterial = CreateMeshMultiPreviewMaterial();
                m_LineMaterial = CreateLineMaterial();
                m_activeMaterial = m_ShadedPreviewMaterial;

                previewDir = new Vector2(0, 0);
                
                drawWire = true;
                activeUVChannel = 0;
                displayMode = 0;
                
                m_CheckeredTexture = EditorGUIUtility.LoadRequired("Previews/Textures/textureChecker.png") as Texture2D;

                m_AvailableDisplayModes = Enumerable.Repeat(true, 7).ToArray();
                m_AvailableUVChannels = Enumerable.Repeat(true, 8).ToArray();

                CheckAvailableAttributes();
            }
        }
        
        void ResetView()
        {
            m_ZoomFactor = 1.0f;
            m_OrthoPosition = new Vector3(0.5f,0.5f,-1);
            
            drawWire = true;
            activeUVChannel = 0;
            
            m_MeshMultiPreviewMaterial.SetInt("_UVChannel", activeUVChannel);
            m_MeshMultiPreviewMaterial.SetTexture("_MainTex", null);
        }

        void CheckAvailableAttributes()
        {
            Mesh mesh = target as Mesh;
            
            if(!mesh)
                return;
            
            if (!mesh.HasVertexAttribute(VertexAttribute.Color))
                m_AvailableDisplayModes[(int)DisplayMode.VertexColor] = false;
            if (!mesh.HasVertexAttribute(VertexAttribute.Normal))
                m_AvailableDisplayModes[(int)DisplayMode.Normals] = false;
            if (!mesh.HasVertexAttribute(VertexAttribute.Tangent))
                m_AvailableDisplayModes[(int)DisplayMode.Tangent] = false;

            int index = 0;
            for (int i = 4; i < 12; i++)
            {
                if (!mesh.HasVertexAttribute((VertexAttribute)i))
                    m_AvailableUVChannels[index] = false;
                index++;
            }
        }
        
        public override void OnPreviewSettings()
        {
            if (!ShaderUtil.hardwareSupportsRectRenderTexture)
                return;
            GUI.enabled = true;
            Init();

            DrawMeshPreviewToolbar();
        }

        private void DoPopup(Rect popupRect, string[] elements, int selectedIndex, GenericMenu.MenuFunction2 func, bool[] disabledItems)
        {
            GenericMenu menu = new GenericMenu();
            for (int i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                
                if(disabledItems[i])
                    menu.AddItem(new GUIContent(element), i == selectedIndex, func, i);
                else
                    menu.AddDisabledItem(new GUIContent(element));
            }
            menu.DropDown(popupRect);
        }
        
        private void SetUVChannel(object data)
        {
            int popupIndex = (int)data;
            if (popupIndex < 0 || popupIndex >= m_AvailableUVChannels.Length)
                return;

            activeUVChannel = popupIndex;
            
            if(displayMode == DisplayMode.UVLayout || displayMode == DisplayMode.UVChecker)
                m_activeMaterial.SetInt("_UVChannel", popupIndex);
        }
        
        private void SetDisplayMode(object data)
        {
            int popupIndex = (int)data;
            if (popupIndex < 0 || popupIndex >= m_DisplayModes.Length)
                return;

            displayMode = (DisplayMode)popupIndex;

            switch (displayMode)
            {
                case DisplayMode.Shaded:
                    OnDropDownAction(m_ShadedPreviewMaterial, 0, false);
                    break;
                case DisplayMode.UVChecker:
                    OnDropDownAction(m_MeshMultiPreviewMaterial, 4, false);
                    m_MeshMultiPreviewMaterial.SetTexture("_MainTex", m_CheckeredTexture);
                    m_MeshMultiPreviewMaterial.mainTextureScale = new Vector2(m_checkerTextureMultiplier, m_checkerTextureMultiplier);
                    break;
                case DisplayMode.UVLayout:
                    OnDropDownAction(m_MeshMultiPreviewMaterial, 0, true);
                    break;
                case DisplayMode.VertexColor:
                    OnDropDownAction(m_MeshMultiPreviewMaterial, 1, false);
                    break;
                case DisplayMode.Normals:
                    OnDropDownAction(m_MeshMultiPreviewMaterial, 2, false);
                    break;
                case DisplayMode.Tangent:
                    OnDropDownAction(m_MeshMultiPreviewMaterial, 3, false);
                    break;
            }
        }
        
        internal static void RenderMeshPreview(
            Mesh mesh,
            PreviewRenderUtility previewUtility,
            Material litMaterial,
            Material wireMaterial,
            Vector2 direction,
            int meshSubset)
        {
            if (mesh == null || previewUtility == null)
                return;

            Bounds bounds = mesh.bounds;
            
            Transform renderCamTransform = previewUtility.camera.GetComponent<Transform>();
            previewUtility.camera.nearClipPlane = 0.0001f;
            previewUtility.camera.farClipPlane = 1000f;

            if (displayMode == DisplayMode.UVLayout)
            {
                previewUtility.camera.orthographic = true;
                previewUtility.camera.orthographicSize = m_ZoomFactor;
                renderCamTransform.position = m_OrthoPosition;
                renderCamTransform.rotation = Quaternion.identity;
                DrawUVLayout(mesh, previewUtility);
                return;
            }

            float halfSize = bounds.extents.magnitude;
            float distance = 4.0f * halfSize;
            
            previewUtility.camera.orthographic = false;
            Quaternion camRotation = Quaternion.Euler(-previewDir.y, -previewDir.x, 0);
            Vector3 camPosition = camRotation * (Vector3.forward * -distance);
            renderCamTransform.position = camPosition;
            renderCamTransform.rotation = camRotation;

            previewUtility.lights[0].intensity = 1.4f;
            previewUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0);
            previewUtility.lights[1].intensity = 1.4f;

            previewUtility.ambientColor = new Color(.1f, .1f, .1f, 0);
            
            RenderMeshPreviewSkipCameraAndLighting(mesh, bounds, previewUtility, litMaterial, wireMaterial, null, direction, meshSubset);
        }

        static void DrawUVLayout(Mesh mesh, PreviewRenderUtility previewUtility)
        {
            GL.PushMatrix();

            // draw UV grid
            m_LineMaterial.SetPass(0);

            GL.LoadProjectionMatrix(previewUtility.camera.projectionMatrix);
            GL.MultMatrix(previewUtility.camera.worldToCameraMatrix);
            
            GL.Begin(GL.LINES);
            const float step = 0.125f;
            for (var g = -2.0f; g <= 3.0f; g += step)
            {
                var majorLine = Mathf.Abs(g - Mathf.Round(g)) < 0.01f;
                if (majorLine)
                {
                    // major grid lines: larger area than [0..1] range, more opaque
                    GL.Color(new Color(0.6f, 0.6f, 0.7f, 1.0f));
                    GL.Vertex3(-2, g, 0);
                    GL.Vertex3(+3, g, 0);
                    GL.Vertex3(g, -2, 0);
                    GL.Vertex3(g, +3, 0);
                }
                else if (g >= 0 && g <= 1)
                {
                    // minor grid lines: only within [0..1] area, more transparent
                    GL.Color(new Color(0.6f, 0.6f, 0.7f, 0.5f));
                    GL.Vertex3(0, g, 0);
                    GL.Vertex3(1, g, 0);
                    GL.Vertex3(g, 0, 0);
                    GL.Vertex3(g, 1, 0);
                }
            }
            GL.End();
            
            // draw the mesh
            GL.LoadIdentity();
            m_MeshMultiPreviewMaterial.SetPass(0);
            GL.wireframe = true;
            Graphics.DrawMeshNow(mesh, previewUtility.camera.worldToCameraMatrix);
            GL.wireframe = false;
            
            GL.PopMatrix();
        }

        static Color GetSubMeshTint(int index)
        {
            // color palette generator based on "golden ratio" idea, like in
            // https://martin.ankerl.com/2009/12/09/how-to-create-random-colors-programmatically/
            var hue = Mathf.Repeat(index * 0.618f, 1);
            var sat = index == 0 ? 0f : 0.3f;
            var val = 1f;
            return Color.HSVToRGB(hue, sat, val);
        }

        internal static void RenderMeshPreviewSkipCameraAndLighting(
            Mesh mesh,
            Bounds bounds,
            PreviewRenderUtility previewUtility,
            Material litMaterial,
            Material wireMaterial,
            MaterialPropertyBlock customProperties,
            Vector2 direction,
            int meshSubset) // -1 for whole mesh
        {
            if (mesh == null || previewUtility == null)
                return;

            Quaternion rot = Quaternion.Euler(direction.y, 0, 0) * Quaternion.Euler(0, direction.x, 0);
            Vector3 pos = rot * (-bounds.center);

            bool oldFog = RenderSettings.fog;
            Unsupported.SetRenderSettingsUseFogNoDirty(false);

            int submeshes = mesh.subMeshCount;
            var tintSubmeshes = false;
            var colorPropID = 0;
            if (submeshes > 1 && displayMode == DisplayMode.Shaded && customProperties == null & meshSubset == -1)
            {
                tintSubmeshes = true;
                customProperties = new MaterialPropertyBlock();
                colorPropID = Shader.PropertyToID("_Color");
            }

            if (litMaterial != null)
            {
                previewUtility.camera.clearFlags = CameraClearFlags.Nothing;
                if (meshSubset < 0 || meshSubset >= submeshes)
                {
                    for (int i = 0; i < submeshes; ++i)
                    {
                        if (tintSubmeshes)
                            customProperties.SetColor(colorPropID, GetSubMeshTint(i));
                        previewUtility.DrawMesh(mesh, pos, rot, litMaterial, i, customProperties);
                    }
                }
                else
                    previewUtility.DrawMesh(mesh, pos, rot, litMaterial, meshSubset, customProperties);
                previewUtility.Render();
            }

            if (wireMaterial != null && drawWire)
            {
                previewUtility.camera.clearFlags = CameraClearFlags.Nothing;
                GL.wireframe = true;
                if (tintSubmeshes)
                    customProperties.SetColor(colorPropID, wireMaterial.color);
                if (meshSubset < 0 || meshSubset >= submeshes)
                {
                    for (int i = 0; i < submeshes; ++i)
                    {
                        // lines/points already are wire-like; it does not make sense to overdraw
                        // them again with dark wireframe color
                        var topology = mesh.GetTopology(i);
                        if (topology == MeshTopology.Lines || topology == MeshTopology.LineStrip || topology == MeshTopology.Points)
                            continue;
                        previewUtility.DrawMesh(mesh, pos, rot, wireMaterial, i, customProperties);
                    }
                }
                else
                    previewUtility.DrawMesh(mesh, pos, rot, wireMaterial, meshSubset, customProperties);
                previewUtility.Render();
                GL.wireframe = false;
            }

            Unsupported.SetRenderSettingsUseFogNoDirty(oldFog);
        }

        private void DoRenderPreview()
        {
            RenderMeshPreview(target as Mesh, m_PreviewUtility, m_activeMaterial, m_WireMaterial, previewDir, -1);
        }

        public override Texture2D RenderStaticPreview(string assetPath, UnityEngine.Object[] subAssets, int width, int height)
        {
            if (!ShaderUtil.hardwareSupportsRectRenderTexture)
            {
                //Debug.Log("Could not generate static preview. Render texture not supported by hardware.");
                return null;
            }

            Init();

            m_PreviewUtility.BeginStaticPreview(new Rect(0, 0, width, height));

            DoRenderPreview();

            return m_PreviewUtility.EndStaticPreview();
        }

        public override bool HasPreviewGUI()
        {
            return (target != null);
        }

        void DrawMeshPreviewToolbar()
        {
            if (displayMode == DisplayMode.UVChecker)
            {
                int oldVal = m_checkerTextureMultiplier;
                
                float sliderWidth = EditorStyles.label.CalcSize(new GUIContent("--------")).x;
                Rect sliderRect = EditorGUILayout.GetControlRect(GUILayout.Width(sliderWidth));
                sliderRect.x += 3;
                
                m_checkerTextureMultiplier = (int)GUI.HorizontalSlider(sliderRect, m_checkerTextureMultiplier, 1, 30, Styles.preSlider, Styles.preSliderThumb);
                if(oldVal != m_checkerTextureMultiplier)
                    m_activeMaterial.mainTextureScale = new Vector2(m_checkerTextureMultiplier, m_checkerTextureMultiplier);
            }
            
            if (displayMode == DisplayMode.UVLayout || displayMode == DisplayMode.UVChecker)
            {
                float channelDropDownWidth = EditorStyles.toolbarDropDown.CalcSize(new GUIContent("Channel 6")).x;
                Rect channelDropdownRect = EditorGUILayout.GetControlRect(GUILayout.Width(channelDropDownWidth));
                channelDropdownRect.y -= 1;
                channelDropdownRect.x += 5;
                GUIContent channel = new GUIContent("Channel " + activeUVChannel, Styles.uvChannelDropdown.tooltip);
                
                if (EditorGUI.DropdownButton(channelDropdownRect, channel, FocusType.Passive, EditorStyles.toolbarDropDown))
                    DoPopup(channelDropdownRect, m_UVChannels,
                        activeUVChannel, SetUVChannel, m_AvailableUVChannels);
            }

            // calculate width based on the longest value in display modes
            float displayModeDropDownWidth = EditorStyles.toolbarDropDown.CalcSize(new GUIContent(m_DisplayModes[(int)DisplayMode.VertexColor])).x;
            Rect displayModeDropdownRect = EditorGUILayout.GetControlRect(GUILayout.Width(displayModeDropDownWidth));
            displayModeDropdownRect.y -= 1;
            displayModeDropdownRect.x += 2;
            GUIContent displayModeDropdownContent = new GUIContent(m_DisplayModes[(int)displayMode], Styles.displayModeDropdown.tooltip);

            if(EditorGUI.DropdownButton(displayModeDropdownRect, displayModeDropdownContent, FocusType.Passive, EditorStyles.toolbarDropDown))
                DoPopup(displayModeDropdownRect, m_DisplayModes, (int)displayMode, SetDisplayMode, m_AvailableDisplayModes);

            using (new EditorGUI.DisabledScope(displayMode == DisplayMode.UVLayout))
            {            
                drawWire = GUILayout.Toggle(drawWire, Styles.wireframeToggle, EditorStyles.toolbarButton);
            }
        }

        void OnDropDownAction(Material mat, int mode, bool flatUVs)
        {
            ResetView();

            m_activeMaterial = mat;

            m_activeMaterial.SetInt("_Mode", mode);
            m_activeMaterial.SetInt("_UVChannel", 0);
        }
        
        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            if (!ShaderUtil.hardwareSupportsRectRenderTexture)
            {
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DropShadowLabel(new Rect(r.x, r.y, r.width, 40),
                        "Mesh preview requires\nrender texture support");
                return;
            }

            Init();

            Assembly editorAssembly = Assembly.GetAssembly(typeof(EditorGUI));
            Type guiPreview = editorAssembly.GetType("PreviewGUI");
            MethodInfo drag2D = guiPreview.GetMethod("Drag2D");

            if(displayMode != DisplayMode.UVLayout)
                previewDir = (Vector2)drag2D?.Invoke(null, new object[] {previewDir, r});
            //previewDir = PreviewGUI.Drag2D(previewDir, r);
            
            if (Event.current.type == EventType.ScrollWheel && displayMode == DisplayMode.UVLayout)
                MeshPreviewZoom(r, Event.current);

            if (Event.current.type == EventType.MouseDrag && displayMode == DisplayMode.UVLayout)
                MeshPreviewPan(r, Event.current);

            if (Event.current.type != EventType.Repaint)
                return;

            m_PreviewUtility.BeginPreview(r, background);
            
            DoRenderPreview();
            
            m_PreviewUtility.EndAndDrawPreview(r);
        }

        void MeshPreviewZoom(Rect rect, Event evt)
        {
            if (!rect.Contains(evt.mousePosition))
            {
                evt.Use();
                return;
            }
            float zoomDelta = (HandleUtility.niceMouseDeltaZoom * 0.5f) * 0.05f;
            var newZoom = m_ZoomFactor + m_ZoomFactor * zoomDelta;
            newZoom = Mathf.Clamp(newZoom, 0.1f, 10.0f);

            // we want to zoom around current mouse position
            var mouseViewPos = new Vector2(
                evt.mousePosition.x / rect.width,
                1 - evt.mousePosition.y / rect.height);
            var mouseWorldPos = m_PreviewUtility.camera.ViewportToWorldPoint(mouseViewPos);
            var mouseToCamPos = m_OrthoPosition - mouseWorldPos;
            var newCamPos = mouseWorldPos + mouseToCamPos * (newZoom / m_ZoomFactor);
            m_OrthoPosition.x = newCamPos.x;
            m_OrthoPosition.y = newCamPos.y;

            m_ZoomFactor = newZoom;
            evt.Use(); 
        }
        
        void MeshPreviewPan(Rect rect, Event evt)
        {
            if (!rect.Contains(evt.mousePosition))
            {
                evt.Use();
                return;
            }
            var cam = m_PreviewUtility.camera;
            var screenPos = cam.WorldToScreenPoint(m_OrthoPosition);
            // event delta is in "screen" units of the preview rect, but the
            // preview camera is rendering into a render target that could
            // be different size; have to adjust drag position to match
            var delta = new Vector3(
                -evt.delta.x * cam.pixelWidth / rect.width,
                evt.delta.y * cam.pixelHeight / rect.height,
                0);
            screenPos += delta;
            var worldPos = cam.ScreenToWorldPoint(screenPos);
            m_OrthoPosition.x = worldPos.x;
            m_OrthoPosition.y = worldPos.y;
            evt.Use();
        }

        static int ConvertFormatToSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32:
                    return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16:
                    return 2;
                case VertexAttributeFormat.UNorm8:
                case VertexAttributeFormat.SNorm8:
                case VertexAttributeFormat.UInt8:
                case VertexAttributeFormat.SInt8:
                    return 1;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, $"Unknown vertex format {format}");
            }
        }

        static string GetAttributeString(VertexAttributeDescriptor attr)
        {
            var format = attr.format;
            var dimension = attr.dimension;
            return $"{format} x {dimension} ({ConvertFormatToSize(format) * dimension} bytes)";
        }

        static int CalcTotalIndices(Mesh mesh)
        {
            var totalCount = 0;
            for (var i = 0; i < mesh.subMeshCount; i++)
                totalCount += (int)mesh.GetIndexCount(i);
            return totalCount;
        }

        static void DrawColorRect(Rect rect, Color color)
        {
            EditorGUI.DrawRect(rect, color);
            var dimmed = color * new Color(0.2f, 0.2f, 0.2f, 0.5f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), dimmed);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - 1, rect.y, 1, rect.height), dimmed);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y, rect.width - 2, 1), dimmed);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + rect.height - 1, rect.width - 2, 1), dimmed);
        }

        public override void OnInspectorGUI()
        {
            GUI.enabled = true;

            // Multi-selection, just display total # of verts/indices and bail out
            if (targets.Length > 1)
            {
                var totalVertices = 0;
                var totalIndices = 0;
                
                foreach (Mesh m in targets)
                {
                    totalVertices += m.vertexCount;
                    totalIndices += CalcTotalIndices(m);
                }
                EditorGUILayout.LabelField($"{targets.Length} meshes selected, {totalVertices} total vertices, {totalIndices} total indices");
                return;
            }
            
            Mesh mesh = target as Mesh;
            if (mesh == null)
                return;
            var attributes = mesh.GetVertexAttributes();
            
            ShowVertexInfo(mesh, attributes);
            ShowIndexInfo(mesh);
            ShowSkinInfo(mesh, attributes);
            ShowBlendShapeInfo(mesh);
            ShowOtherInfo(mesh);

            GUI.enabled = false;
        }

        static void ShowOtherInfo(Mesh mesh)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Other", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Bounds Center", mesh.bounds.center.ToString("g4"));
            EditorGUILayout.LabelField("Bounds Size", mesh.bounds.size.ToString("g4"));
            EditorGUILayout.LabelField("Read/Write Enabled", mesh.isReadable.ToString());
            EditorGUI.indentLevel--;
        }

        static void ShowBlendShapeInfo(Mesh mesh)
        {
            var blendShapeCount = mesh.blendShapeCount;
            if (blendShapeCount <= 0)
                return;
                
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Blend Shapes: {blendShapeCount}", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            for (int i = 0; i < blendShapeCount; ++i)
            {
                EditorGUILayout.LabelField($"#{i}: {mesh.GetBlendShapeName(i)} ({mesh.GetBlendShapeFrameCount(i)} frames)");
            }
            EditorGUI.indentLevel--;
        }

        static void ShowSkinInfo(Mesh mesh, VertexAttributeDescriptor[] attributes)
        {
            var boneCount = mesh.bindposes.Length;
            if (boneCount <= 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Skin: {boneCount} bones", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (var a in attributes)
            {
                // only list skin related attributes
                if (a.attribute == VertexAttribute.BlendIndices || a.attribute == VertexAttribute.BlendWeight)
                    EditorGUILayout.LabelField(a.attribute.ToString(), GetAttributeString(a));
            }
            EditorGUI.indentLevel--;
        }

        static void ShowIndexInfo(Mesh mesh)
        {
            var indexCount = CalcTotalIndices(mesh);
            var indexSize = mesh.indexFormat == IndexFormat.UInt16 ? 2 : 4;
            var bufferSizeStr = EditorUtility.FormatBytes(indexCount * indexSize);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Indices: {indexCount}, {mesh.indexFormat} format ({bufferSizeStr})", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var subMeshCount = mesh.subMeshCount;
            string subMeshText = subMeshCount == 1 ? "submesh" : "submeshes";
            EditorGUILayout.LabelField($"{mesh.subMeshCount} {subMeshText}:");

            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var subMesh = mesh.GetSubMesh(i);
                string topology = subMesh.topology.ToString().ToLowerInvariant();
                string baseVertex = subMesh.baseVertex == 0 ? "" : ", base vertex " + subMesh.baseVertex;

                var divisor = 3;
                switch (subMesh.topology)
                {
                    case MeshTopology.Points: divisor = 1; break;
                    case MeshTopology.Lines: divisor = 2; break;
                    case MeshTopology.Triangles: divisor = 3; break;
                    case MeshTopology.Quads: divisor = 4; break;
                    case MeshTopology.LineStrip: divisor = 2; break; // technically not correct, but eh
                }

                var primCount = subMesh.indexCount / divisor;
                if (subMeshCount > 1)
                {
                    GUILayout.BeginHorizontal();
                    var rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.label, GUILayout.Width(7));
                    rect.x += EditorGUI.indentLevel * 15; //@TODO: use EditorGUI.indent;
                    var tint = GetSubMeshTint(i);
                    DrawColorRect(rect, tint);
                }

                EditorGUILayout.LabelField($"#{i}: {primCount} {topology} ({subMesh.indexCount} indices starting from {subMesh.indexStart}){baseVertex}");
                if (subMeshCount > 1)
                {
                    GUILayout.EndHorizontal();
                }
            }
            EditorGUI.indentLevel--;
        }

        static void ShowVertexInfo(Mesh mesh, VertexAttributeDescriptor[] attributes)
        {
            var vertexSize = attributes.Sum(attr => ConvertFormatToSize(attr.format) * attr.dimension);
            var bufferSizeStr = EditorUtility.FormatBytes(mesh.vertexCount * vertexSize);
            EditorGUILayout.LabelField($"Vertices: {mesh.vertexCount} ({bufferSizeStr})", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            foreach (var a in attributes)
            {
                // skin related attributes listed separately
                if (a.attribute == VertexAttribute.BlendIndices || a.attribute == VertexAttribute.BlendWeight)
                    continue;
                var title = a.attribute.ToString();
                if (title.Contains("TexCoord"))
                    title = title.Replace("TexCoord", "UV");
                EditorGUILayout.LabelField(title, GetAttributeString(a));
            }
            EditorGUI.indentLevel--;
        }

        public void OnDisable()
        {
            if (m_PreviewUtility != null)
            {
                m_PreviewUtility.Cleanup();
                m_PreviewUtility = null;
            }
            DestroyImmediate(m_ShadedPreviewMaterial);
            DestroyImmediate(m_WireMaterial);
            DestroyImmediate(m_MeshMultiPreviewMaterial);
            DestroyImmediate(m_LineMaterial);
        }
    }
}
