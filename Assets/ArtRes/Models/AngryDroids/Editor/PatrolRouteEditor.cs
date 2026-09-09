using UnityEngine;
using UnityEditor;
using System.Collections;

[CustomEditor(typeof(PatrolRoute))]
public class PatrolRouteEditor : Editor 
{
    PatrolRoute path;
    bool editMode;

    void OnEnable() 
    {
        path = (PatrolRoute)target;
    }

    void OnDisable() 
    {

    }

    public override void OnInspectorGUI()
    {
        GUI.enabled = path.points.Count != 0;
        if (GUILayout.Button((editMode ? "Stop Editing" : "Edit"))) { editMode = !editMode; }
        
        GUI.enabled = true;

        if (GUILayout.Button("Add"))
        {
            if (path.points.Count == 0)
                path.points.Add(new Vector3(1, 0, 1));
            else
                path.points.Add(path.points[path.points.Count - 1] + new Vector3(1, 0, 1));
        }

        for (int i = 0; i < path.points.Count; i++)
        {
            GUILayout.BeginHorizontal();
            path.points[i] = EditorGUILayout.Vector3Field(("Point " + i.ToString()), path.points[i]);
            if (GUILayout.Button("x", GUILayout.Width(20)))
                path.points.RemoveAt(i);

            GUILayout.EndHorizontal();
        }

        path.pingPong = EditorGUILayout.Toggle(new GUIContent("PingPong"), path.pingPong);
    }

    void OnSceneGUI()
    {
        if (path.points.Count == 0) return;

        Color oldColor = Handles.color;
        Handles.color = Color.magenta;

        if(path.points.Count > 0)
            Handles.DrawPolyLine(path.GetPoints());
        if (!path.pingPong)
            Handles.DrawLine(path.GetPoint(0), path.GetPoint(path.points.Count - 1));

        Handles.color = Color.yellow;

        for (int i = 0; i < path.points.Count; i++)
        {
            if (editMode)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 p = Handles.PositionHandle(path.GetPoint(i), path.transform.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    path.points[i] = path.transform.InverseTransformPoint(p);
                }
                
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }
            else
               Handles.CubeHandleCap(0, path.GetPoint(i), path.transform.rotation, HandleUtility.GetHandleSize(path.GetPoint(i)) * 0.1f, EventType.Repaint);

            Handles.Label(path.GetPoint(i), i.ToString());
        }

        Handles.color = oldColor;
    }
}
