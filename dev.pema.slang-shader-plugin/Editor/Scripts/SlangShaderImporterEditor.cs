using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnitySlangShader
{
    [CustomEditor(typeof(SlangShaderImporter))]
    public class SlangShaderImporterEditor : ScriptedImporterEditor
    {
        private readonly string foldoutSourceID = $"{nameof(SlangShaderImporterEditor)}.foldoutSource";
        private readonly string foldoutGeneratedShaderID = $"{nameof(SlangShaderImporterEditor)}.foldoutGeneratedShader";
        private readonly string foldoutVariantsID = $"{nameof(SlangShaderImporterEditor)}.foldoutVariants";

        private Vector2 diagScrollPosition = Vector2.zero;
        private GUIStyle statusInfoStyle;
        private GUIStyle evenStyle;
        private GUIContent errorIconContent;
        private GUIContent warnIconContent;

        private Vector2 variantScrollPosition = Vector2.zero;

        public override bool showImportedObject => false;

        public override void OnInspectorGUI()
        {
            var importer = target as SlangShaderImporter;
            if (importer == null) return;

            bool foldoutSource = SessionState.GetBool(foldoutSourceID, false);
            SessionState.SetBool(foldoutSourceID, EditorGUILayout.Foldout(foldoutSource, "Source code"));
            if (foldoutSource)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextArea(File.ReadAllText(importer.assetPath));
                }
            }

            bool foldoutGeneratedShader = SessionState.GetBool(foldoutGeneratedShaderID, false);
            SessionState.SetBool(foldoutGeneratedShaderID, EditorGUILayout.Foldout(foldoutGeneratedShader, "Generated shader"));
            if (foldoutGeneratedShader)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextArea(serializedObject.FindProperty("GeneratedSourceCode").stringValue);
                }
            }

            bool foldoutVariants = SessionState.GetBool(foldoutVariantsID, false);
            SessionState.SetBool(foldoutVariantsID, EditorGUILayout.Foldout(foldoutVariants, "Generated variants"));
            if (foldoutVariants)
            {
                float height = Mathf.Min(importer.GeneratedVariants.Length * 20f + 40f, 150f);
                variantScrollPosition = GUILayout.BeginScrollView(variantScrollPosition, EditorStyles.helpBox, GUILayout.MinHeight(height));
                foreach (var variant in importer.GeneratedVariants)
                {
                    string name = string.Join(" ", variant.Keywords);
                    if (name.Trim() == string.Empty) name = "<Empty variant>";
                    EditorGUILayout.LabelField(name, EditorStyles.helpBox);
                }
                GUILayout.EndScrollView();
            }

            DrawErrorList(importer.Diagnostics);

            base.ApplyRevertGUI();
        }

        private void DrawErrorList(SlangShaderDiagnostic[] diags)
        {
            if (diags == null)
                diags = new SlangShaderDiagnostic[0];

            if (statusInfoStyle == null) statusInfoStyle = new GUIStyle("CN StatusInfo");
            if (errorIconContent == null) errorIconContent = EditorGUIUtility.IconContent("console.erroricon");
            if (warnIconContent == null) warnIconContent = EditorGUIUtility.IconContent("console.warnicon");
            if (evenStyle == null) evenStyle = new GUIStyle("CN EntryBackEven");

            GUILayout.Label("Diagnostics:", EditorStyles.boldLabel);

            int n = diags.Length;
            float height = Mathf.Min(n * 20f + 40f, 150f);
            diagScrollPosition = GUILayout.BeginScrollView(diagScrollPosition, GUI.skin.box, GUILayout.MinHeight(height));

            EditorGUIUtility.SetIconSize(new Vector2(16.0f, 16.0f));
            float lineHeight = statusInfoStyle.CalcHeight(errorIconContent, 100);

            Event e = Event.current;

            for (int i = 0; i < n; ++i)
            {
                Rect r = EditorGUILayout.GetControlRect(false, lineHeight);

                string err = diags[i].Text;
                bool warn = diags[i].Warning;
                string fileName = diags[i].File;
                int line = diags[i].Line;

                // background
                if (e.type == EventType.Repaint)
                {
                    if ((i & 1) == 0)
                    {
                        GUIStyle st = evenStyle;
                        st.Draw(r, false, false, false, false);
                    }
                }

                // error location on the right side
                Rect locRect = r;
                locRect.xMin = locRect.xMax;
                if (line > 0)
                {
                    GUIContent gc;
                    if (string.IsNullOrEmpty(fileName))
                        gc = EditorGUIUtility.TrTempContent(line.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    else
                        gc = EditorGUIUtility.TrTempContent(fileName + ":" + line.ToString(System.Globalization.CultureInfo.InvariantCulture));

                    // calculate size so we can right-align it
                    Vector2 size = EditorStyles.miniLabel.CalcSize(gc);
                    locRect.xMin -= size.x;
                    GUI.Label(locRect, gc, EditorStyles.miniLabel);
                    locRect.xMin -= 2;
                    // ensure some minimum width so that platform field next will line up
                    if (locRect.width < 30)
                        locRect.xMin = locRect.xMax - 30;
                }

                // error message
                Rect msgRect = r;
                msgRect.xMax = locRect.xMin;
                GUI.Label(msgRect, new GUIContent(err, warn ? warnIconContent.image : errorIconContent.image), statusInfoStyle);
            }
            EditorGUIUtility.SetIconSize(Vector2.zero);
            GUILayout.EndScrollView();
        }
    }
}