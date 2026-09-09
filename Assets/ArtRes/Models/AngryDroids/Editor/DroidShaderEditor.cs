using UnityEngine;
using System.Collections;

namespace UnityEditor
{
    public class DroidShaderEditor : ShaderGUI
    {
        MaterialProperty colorR;
        MaterialProperty colorG;
        MaterialProperty colorB;
        MaterialProperty colorGlow;
        MaterialProperty maskTexture;
        MaterialProperty normalsTexture;
        MaterialProperty specularTexture;
        MaterialProperty aoTexture;
        MaterialProperty detailTexture;
        MaterialProperty vertexColor;
        float detailScale;
        float aoScale;

        MaterialEditor m_MaterialEditor;

        public void FindProperties(MaterialProperty[] props)
        {
            colorR = FindProperty("_Color", props);
            colorG = FindProperty("_Color1", props);
            colorB = FindProperty("_Color2", props);
            colorGlow = FindProperty("_GlowColor", props);
            maskTexture = FindProperty("_MaskTex", props);
            normalsTexture = FindProperty("_BumpMap", props);
            specularTexture = FindProperty("_SpecMap", props);
            aoTexture = FindProperty("_AOMap", props);
            detailTexture = FindProperty("_DetailMap", props);
            vertexColor = FindProperty("_Power", props);
            detailScale = detailTexture.textureScaleAndOffset.x;
            aoScale = FindProperty("_AOScale", props).floatValue;
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            FindProperties(properties);
            m_MaterialEditor = materialEditor;
            Material material = m_MaterialEditor.target as Material;

            ShaderPropertiesGUI(material);
        }

        public void ShaderPropertiesGUI(Material material)
        {
            EditorGUI.BeginChangeCheck();
            {
                m_MaterialEditor.ColorProperty(colorR, "Color (R)");
                m_MaterialEditor.ColorProperty(colorG, "Color (G)");
                m_MaterialEditor.ColorProperty(colorB, "Color (B)");
                m_MaterialEditor.ColorProperty(colorGlow, "Glow Color (A)");
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("RGB Mask"), maskTexture);
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("Normals"), normalsTexture);
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("Specular"), specularTexture);
                GUILayout.BeginHorizontal();
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("Ambient-Occlusion"), aoTexture);
                aoScale = GUILayout.HorizontalSlider(aoScale, 1, 10, GUILayout.Width(100));
                aoScale = EditorGUILayout.FloatField(aoScale, GUILayout.Width(50));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                m_MaterialEditor.TexturePropertySingleLine(new GUIContent("Pattern"), detailTexture);
                detailScale = GUILayout.HorizontalSlider(detailScale, 1, 50, GUILayout.Width(100));
                detailScale = EditorGUILayout.FloatField(detailScale, GUILayout.Width(50));
                GUILayout.EndHorizontal();

                m_MaterialEditor.FloatProperty(vertexColor, "Vertex Color Intensity");
            }
            if (EditorGUI.EndChangeCheck())
            {
                MaterialChanged(material);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Low"))
                material.shader.maximumLOD = 100;
            if (GUILayout.Button("Medium"))
                material.shader.maximumLOD = 200;
            if (GUILayout.Button("High"))
                material.shader.maximumLOD = 300;
            GUILayout.EndHorizontal();
        }

        void MaterialChanged(Material material)
        {
            Vector4 scale = detailTexture.textureScaleAndOffset;
            detailTexture.textureScaleAndOffset = new Vector4(detailScale, detailScale, scale.z, scale.w);
            material.SetFloat("_AOScale", aoScale);

            SetKeyword(material, "_NORMALMAP", material.GetTexture("_BumpMap"));
            SetKeyword(material, "_DETAILMAP", material.GetTexture("_DetailMap"));
            SetKeyword(material, "_AOMAP", material.GetTexture("_AOMap"));
            SetKeyword(material, "_VERTEXCOLOR", material.GetFloat("_Power") > 0);
        }

        void SetKeyword(Material m, string keyword, bool state)
        {
            if (state)
                m.EnableKeyword(keyword);
            else
                m.DisableKeyword(keyword);
        }
    }
}