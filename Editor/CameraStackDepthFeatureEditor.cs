using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[CustomEditor(typeof(CameraStackDepthFeature))]
public class CameraStackDepthFeatureEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject, "m_SelectedCameras");

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField(
            new GUIContent(
                "Selected Cameras",
                "Specifies which cameras in the stack are included in depth capture and merge processing.\n\n" +
                "The order follows the camera stack: Base camera first, followed by Overlay cameras."
            ),
            EditorStyles.boldLabel
        );

        var feature = (CameraStackDepthFeature)target;
        var selectedProp = serializedObject.FindProperty("m_SelectedCameras");

        var mainCamera = Camera.main;
        if (!mainCamera)
        {
            EditorGUILayout.HelpBox("No Main Camera found.", MessageType.Warning);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        var additionalData = mainCamera.GetUniversalAdditionalCameraData();

        List<Camera> stack = new List<Camera>();
        stack.Add(mainCamera);
        stack.AddRange(additionalData.cameraStack);

        // Ensure size matches stack
        while (selectedProp.arraySize < stack.Count)
            selectedProp.InsertArrayElementAtIndex(selectedProp.arraySize);

        while (selectedProp.arraySize > stack.Count)
            selectedProp.DeleteArrayElementAtIndex(selectedProp.arraySize - 1);

        EditorGUI.indentLevel++;

        for (int i = 0; i < stack.Count; i++)
        {
            var element = selectedProp.GetArrayElementAtIndex(i);
            var cam = stack[i];

            string label = cam ? cam.name : "Missing Camera";
            element.boolValue = EditorGUILayout.ToggleLeft(label, element.boolValue);
        }

        EditorGUI.indentLevel--;

        serializedObject.ApplyModifiedProperties();
    }
}