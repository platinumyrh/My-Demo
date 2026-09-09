// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Project Droids/Self-Illumin/ColoredMask(RGB)" 
{
	Properties 
	{
		_Color ("Color R", Color) = (1,1,1,0.5)
		_Color1 ("Color G", Color) = (0.5,0.5,0.5,0.5)
		_Color2 ("Color B", Color) = (0,0,0,0.5)
		_MainTex ("Fallback (RGB)", 2D) = "white" {}
		_MaskTex ("Mask", 2D) = "black" {}
		_BumpMap ("Normals", 2D) = "" {}
		_Metallic ("Metallic", Range(0,1)) = 0.0
        _Power("Vertex Color Intensity", Range(1.0,16.0) ) = 2.0
        _GlowColor ("Self-Illumination Color", Color) = (0,0,0,1)
	}
	
	//Unity 5 Shading
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 300
		
		CGPROGRAM
		#pragma surface surf Standard
        #pragma target 3.0
		#pragma shader_feature _NORMALMAP 

		fixed4 _Color;
		fixed4 _Color1;
		fixed4 _Color2;
		fixed4 _GlowColor;
        
		sampler2D _MaskTex;
#ifdef _NORMALMAP
        sampler2D _BumpMap;
#endif
		
		struct Input {
            fixed4 color : COLOR;
			float2 uv_MainTex;
			float3 viewDir;
		};

		fixed _Metallic;
        fixed _Power;

		void surf (Input IN, inout SurfaceOutputStandard o) {
			fixed4 col = fixed4(0,0,0,0);
			fixed4 mask = tex2D (_MaskTex, IN.uv_MainTex);
			col += _Color * mask.r;
			col += _Color1 * mask.g;
			col += _Color2 * mask.b;
			o.Albedo = col.rgb * (IN.color * _Power);
			o.Metallic = _Metallic;
			o.Smoothness = col.a;
#ifdef _NORMALMAP
            o.Normal = UnpackNormal(tex2D (_BumpMap, IN.uv_MainTex));
#endif
			fixed rim = 1.0 - saturate(dot(IN.viewDir, o.Normal));			
			o.Emission = _GlowColor.rgb * mask.a + (rim * rim * _GlowColor.a * mask.a);
            o.Alpha = 1;
		}
		ENDCG
	} 

	//Unity 4 shading
    SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		
		CGPROGRAM
		// Unity 4 Surface lighting model, and enable shadows on all light types
		#pragma surface surf Lambert 
		#pragma target 3.0
		#pragma shader_feature _NORMALMAP

		fixed4 _Color;
		fixed4 _Color1;
		fixed4 _Color2;
        fixed4 _GlowColor;

		sampler2D _MaskTex;
#ifdef _NORMALMAP
        sampler2D _BumpMap;
#endif

		struct Input {
            fixed4 color : COLOR;
			float2 uv_MainTex;
			float3 viewDir;
		};

		fixed _Metallic;
        fixed _Power;

		void surf (Input IN, inout SurfaceOutput o) {
			fixed4 col = fixed4(0,0,0,0);
			fixed4 mask = tex2D (_MaskTex, IN.uv_MainTex);
			col += _Color * mask.r;
			col += _Color1 * mask.g;
			col += _Color2 * mask.b;		
			o.Albedo = col.rgb * (IN.color * _Power);
			o.Specular = _Metallic;
			o.Gloss = col.a;
#ifdef _NORMALMAP
            o.Normal = UnpackNormal(tex2D (_BumpMap, IN.uv_MainTex));
#endif
			fixed rim = 1.0 - saturate(dot(IN.viewDir, o.Normal));
            o.Emission = _GlowColor.rgb * mask.a + (rim * rim * _GlowColor.a * mask.a);
            o.Alpha = 1;
		}
		ENDCG
	}
	
	//If this subshader is used then something is OLD (really simple shader)
	SubShader 
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			fixed4 _Color;
			fixed4 _Color1;
			fixed4 _Color2;
			fixed4 _GlowColor;

			sampler2D _MaskTex;
			float4 _MaskTex_ST;

			struct appdata_t
			{
				float4 vertex : POSITION;
				fixed4 color : COLOR;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : POSITION;
				fixed4 color : COLOR;
				float2 texcoord : TEXCOORD0;
			};
		
			v2f vert (appdata_t v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.texcoord = TRANSFORM_TEX(v.texcoord, _MaskTex);
				return o;
			}

			fixed4 frag (v2f IN) : COLOR
			{
				fixed4 col = fixed4(0,0,0,0);
				fixed4 mask = tex2D (_MaskTex, IN.texcoord);
				col += _Color * mask.r;
				col += _Color1 * mask.g;
				col += _Color2 * mask.b;		
				col *= IN.color;
				col += _GlowColor * mask.a;
				return col;
			}
		ENDCG
		}
	}
	
	FallBack "Diffuse"
}