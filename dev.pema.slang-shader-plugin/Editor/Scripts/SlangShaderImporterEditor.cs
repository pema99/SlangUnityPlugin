using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnitySlangShader
{
    [CustomEditor(typeof(SlangShaderImporter))]
    public class SlangShaderImporterEditor : ScriptedImporterEditor
    {
        private readonly string foldoutSourceID = $"{nameof(SlangShaderImporterEditor)}.foldoutSource";
        private readonly string foldoutGeneratedShaderID = $"{nameof(SlangShaderImporterEditor)}.foldoutGeneratedShader";
        private readonly string foldoutVariantsID = $"{nameof(SlangShaderImporterEditor)}.foldoutVariants";
        private readonly string foldoutIncludePathsID = $"{nameof(SlangShaderImporterEditor)}.foldoutIncludePaths";

        private VisualElement root;

        public override bool showImportedObject => false;
        public override bool HasModified() => false;
        protected override bool needsApplyRevert => false;

        private void Rebuild(SlangShaderImporter updatedImporter)
        {
            var importer = target as SlangShaderImporter;
            if (importer == null || root == null)
                return;

            if (updatedImporter.assetPath != importer.assetPath)
                return;

            root.Clear();

            var sourceCodeArea = new TextField();
            sourceCodeArea.SetValueWithoutNotify(File.ReadAllText(updatedImporter.assetPath));
            sourceCodeArea.SetEnabled(false);
            var sourceCodeFoldout = new Foldout() { text = "Source code" };
            sourceCodeFoldout.contentContainer.style.marginLeft = 0;
            sourceCodeFoldout.Add(sourceCodeArea);
            sourceCodeFoldout.SetValueWithoutNotify(SessionState.GetBool(foldoutSourceID, false));
            sourceCodeFoldout.RegisterValueChangedCallback(evt => SessionState.SetBool(foldoutSourceID, evt.newValue));
            root.Add(sourceCodeFoldout);

            var generatedSourceCodeArea = new TextField();
            generatedSourceCodeArea.SetValueWithoutNotify(updatedImporter.GeneratedSourceCode);
            generatedSourceCodeArea.SetEnabled(false);
            var generatedSourceCodeFoldout = new Foldout() { text = "Generated shader" };
            generatedSourceCodeFoldout.contentContainer.style.marginLeft = 0;
            generatedSourceCodeFoldout.Add(generatedSourceCodeArea);
            generatedSourceCodeFoldout.SetValueWithoutNotify(SessionState.GetBool(foldoutGeneratedShaderID, false));
            generatedSourceCodeFoldout.RegisterValueChangedCallback(evt => SessionState.SetBool(foldoutGeneratedShaderID, evt.newValue));
            root.Add(generatedSourceCodeFoldout);

            var variantsArea = new ListView(updatedImporter.GeneratedVariants, -1,
                () =>
                {
                    var label = new Label();
                    label.style.whiteSpace = WhiteSpace.Normal;
                    label.style.paddingBottom = 3;
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    label.AddToClassList("unity-box");
                    return label;
                },
                (elem, idx) =>
                {
                    elem.style.backgroundColor = idx % 2 == 0 ? new StyleColor(new Color(0.2f, 0.2f, 0.2f)) : new StyleColor(new Color(0.26f, 0.26f, 0.26f));
                    elem.style.height = new StyleLength(StyleKeyword.Auto);
                    var variant = updatedImporter.GeneratedVariants[idx];
                    string text = $"Graphics Backend: {variant.GraphicsBackend}\nKeywords: {string.Join(" ", variant.Keywords)}";
                    if (variant.Keywords.Count == 0) text += "<Empty variant>";
                    (elem as Label).text = text;
                });
            variantsArea.selectionType = SelectionType.None;
            variantsArea.style.maxHeight = Mathf.Min(updatedImporter.GeneratedVariants.Length * 60f + 40f, 250f);
            variantsArea.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            variantsArea.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            var variantsFoldout = new Foldout() { text = "Generated variants" };
            variantsFoldout.contentContainer.style.marginLeft = 0;
            variantsFoldout.Add(variantsArea);
            variantsFoldout.SetValueWithoutNotify(SessionState.GetBool(foldoutVariantsID, false));
            variantsFoldout.RegisterValueChangedCallback(evt => SessionState.SetBool(foldoutVariantsID, evt.newValue));
            root.Add(variantsFoldout);

            var applyButton = new Button() { text = "Apply changes" };
            applyButton.style.marginTop = 6;
            applyButton.style.minWidth = applyButton.style.width = 100;
            applyButton.style.alignSelf = Align.FlexEnd;
            applyButton.SetEnabled(false);
            applyButton.clicked += () =>
            {
                EditorUtility.SetDirty(updatedImporter);
                updatedImporter.SaveAndReimport();
                applyButton.SetEnabled(false);
            };

            var includePathsWarning = new HelpBox("Specify additional paths to search for shader include files here.\n"+
                "Some paths, such as those inside Packages, can not be automatically located, and must added here explicitly.", HelpBoxMessageType.Info);
            var includePathsArea = new ListView(updatedImporter.AdditionalIncludePaths, -1, 
                () =>
                {
                    var tf = new TextField();
                    tf.RegisterValueChangedCallback(evt =>
                    {
                        updatedImporter.AdditionalIncludePaths[(int)tf.userData] = evt.newValue;
                        applyButton.SetEnabled(true);
                    });
                    return tf;
                },
                (elem, idx) =>
                {
                    if (idx >= updatedImporter.AdditionalIncludePaths.Length)
                    {
                        System.Array.Resize(ref updatedImporter.AdditionalIncludePaths, idx + 1);
                    }

                    var tf = elem as TextField;
                    tf.userData = idx;
                    tf.SetValueWithoutNotify(updatedImporter.AdditionalIncludePaths[idx]);
                });
            includePathsArea.showAddRemoveFooter = true;
            includePathsArea.reorderable = true;
            includePathsArea.reorderMode = ListViewReorderMode.Animated;
            includePathsArea.itemsAdded += (items) =>
            {
                applyButton.SetEnabled(true);
            };
            includePathsArea.itemsRemoved += (items) =>
            {
                var newArr = new List<string>();
                for (int i = 0; i < includePathsArea.itemsSource.Count; i++)
                {
                    if (items.Contains(i))
                        continue;

                    newArr.Add(includePathsArea.itemsSource[i] as string);
                }
                updatedImporter.AdditionalIncludePaths = newArr.ToArray();
                applyButton.SetEnabled(true);
            };
            var includePathsFoldout = new Foldout() { text = "Additional include paths" };
            includePathsFoldout.contentContainer.style.marginLeft = 0;
            includePathsFoldout.Add(includePathsWarning);
            includePathsFoldout.Add(includePathsArea);
            includePathsFoldout.SetValueWithoutNotify(SessionState.GetBool(foldoutIncludePathsID, false));
            includePathsFoldout.RegisterValueChangedCallback(evt => SessionState.SetBool(foldoutIncludePathsID, evt.newValue));
            root.Add(includePathsFoldout);

            var diagsLabel = new Label("Diagnostics");
            diagsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            diagsLabel.style.marginTop = 4;
            var orderedDiags = updatedImporter.Diagnostics.OrderBy(x => x.Warning ? 1 : 0).ThenBy(x => x.File).ThenBy(x => x.Line).ToArray();
            var diagsArea = new ListView(orderedDiags, 20,
                () =>
                {
                    var icon = new Image();
                    icon.style.width = icon.style.height = icon.style.minWidth = icon.style.minHeight = 16;
                    icon.style.marginLeft = icon.style.marginTop = 2;

                    var msgLabel = new Label() { name = "msg-label" };
                    msgLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                    msgLabel.style.overflow = Overflow.Hidden;

                    var fileLabel = new Label() { name = "file-label" };
                    fileLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                    fileLabel.style.marginLeft = new StyleLength(StyleKeyword.Auto);
                    fileLabel.style.fontSize = EditorStyles.miniLabel.fontSize;
                    fileLabel.RegisterCallback<GeometryChangedEvent>(evt => msgLabel.style.borderRightWidth = fileLabel.resolvedStyle.width + 22);

                    var container = new VisualElement();
                    container.style.flexDirection = FlexDirection.Row;
                    container.Add(icon);
                    container.Add(msgLabel);
                    container.Add(fileLabel);
                    return container;
                },
                (elem, idx) =>
                {
                    elem.style.backgroundColor = idx % 2 == 0 ? new StyleColor(new Color(0.2f, 0.2f, 0.2f)) : new StyleColor(new Color(0.26f, 0.26f, 0.26f));

                    var diag = orderedDiags[idx];

                    elem.Q<Label>("msg-label").text = diag.Text;

                    elem.Q<Image>().image = EditorGUIUtility.FindTexture(diag.Warning ? "console.warnicon" : "console.erroricon");

                    var fileLabel = elem.Q<Label>("file-label");
                    fileLabel.style.display = DisplayStyle.Flex;
                    string location = string.Empty;
                    if (diag.Line == 0)
                    {
                        if (string.IsNullOrEmpty(diag.File))
                            fileLabel.style.display = DisplayStyle.None;

                        location = diag.File;
                    }
                    else if (!string.IsNullOrEmpty(diag.File))
                    {
                        location = $"{diag.File}:{diag.Line}";
                    }
                    fileLabel.text = location;
                });
            diagsArea.itemsChosen += (chosen) =>
            {
                var diag = (SlangShaderDiagnostic)chosen.First();
                Object asset = string.IsNullOrEmpty(diag.File) ? null : AssetDatabase.LoadMainAssetAtPath(diag.File);
                AssetDatabase.OpenAsset(asset, diag.Line);
            };
            diagsArea.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            diagsArea.style.paddingBottom = diagsArea.style.paddingTop = diagsArea.style.paddingLeft = diagsArea.style.paddingRight = 4;
            diagsArea.style.maxHeight = Mathf.Min(orderedDiags.Length * 20f + 40f, 150f);
            diagsArea.selectionType = SelectionType.Single;
            if (orderedDiags.Length == 0)
            {
                diagsArea.Q<Label>(className: BaseListView.emptyLabelUssClassName).style.display = DisplayStyle.None;
            }

            root.Add(diagsLabel);
            root.Add(diagsArea);

            root.Add(applyButton);
        }

        public override VisualElement CreateInspectorGUI()
        {
            var importer = target as SlangShaderImporter;
            if (importer == null) return null;

            root = new VisualElement();
            Rebuild(importer);

            SlangShaderImporter.OnWillReimport -= Rebuild;
            SlangShaderImporter.OnWillReimport += Rebuild;

            return root;
        }
    }
}