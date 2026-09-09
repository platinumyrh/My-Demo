// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Project Droids/Droid HD" 
{
	Properties 
	{
		_Color ("Primary Color (R)", Color) = (1,1,1,0.5)
		_Color1 ("Secondary Color (G)", Color) = (0.5,0.5,0.5,0.5)
		_Color2 ("Tretiary Color (B)", Color) = (0,0,0,0.5)
		_MainTex ("Fallback (RGB)", 2D) = "white" {}
		_MaskTex ("Mask", 2D) = "black" {}
		_BumpMap ("Normals", 2D) = "" {}
		_SpecMap ("Specular", 2D) = "" {}
		_AOMap ("Ambient Occlusion", 2D) = "" {}
		_AOScale ("Ambient Occlusion Intensity", Range(1.0,10.0)) = 1
		_DetailMap ("Pattern (R)", 2D) = "" {}
        _Power("Vertex Color Intensity", Range(1.0,16.0) ) = 2.0
        _GlowColor ("Self-Illumination Color", Color) = (0,0,0,1)
		_DamageColor ("Damage Color", Color) = (1,1,1,0)
		_BurnLevel("Burn Level", Range(0.0,1.0)) = 0.0
	}
	
	//Unity 5 Shading
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 300

		CGPROGRAM
		#pragma surface surf StandardSpecular
        #pragma target 3.0
		#pragma shader_feature _NORMALMAP
		#pragma shader_feature _DETAILMAP
		#pragma shader_feature _AOMAP 
		#pragma shader_feature _VERTEXCOLOR

		fixed4 _Color;
		fixed4 _Color1;
		fixed4 _Color2;
		fixed4 _GlowColor;
		fixed4 _DamageColor;
		fixed _BurnLevel;
        
		sampler2D _MaskTex;
		sampler2D _SpecMap;
#ifdef _NORMALMAP
        sampler2D _BumpMap;
#endif
#ifdef _DETAILMAP
        sampler2D _DetailMap;
#endif
#ifdef _AOMAP
	    sampler2D _AOMap;
		fixed _AOScale;	
#endif	
		struct Input {
            fixed4 color : COLOR;
			float2 uv_MainTex;
#ifdef _DETAILMAP
			float2 uv2_DetailMap;
#endif
			float3 viewDir;
		};

        fixed _Power;

		void surf (Input IN, inout SurfaceOutputStandardSpecular o) {
			fixed4 col = fixed4(0,0,0,0);
			fixed4 mask = tex2D (_MaskTex, IN.uv_MainTex);
			fixed4 spec = tex2D (_SpecMap, IN.uv_MainTex);
#ifdef _DETAILMAP
			fixed4 patt = tex2D (_DetailMap, IN.uv2_DetailMap);
			_Color *= patt;
#endif
			col += _Color * mask.r;
			col += _Color1 * mask.g;
			col += _Color2 * mask.b;

			o.Albedo = col.rgb * (1 - _BurnLevel);
			o.Smoothness = col.a * (1 - _BurnLevel);
			o.Specular = spec.g * (1 - _BurnLevel);

#ifdef _VERTEXCOLOR
			o.Albedo *= IN.color * _Power;
			o.Specular *= IN.color;
#endif			
			
#ifdef _AOMAP
			fixed4 ao = tex2D (_AOMap, IN.uv_MainTex);
			o.Albedo *= ao * _AOScale;
#endif
			
#ifdef _NORMALMAP
            o.Normal = UnpackNormal(tex2D (_BumpMap, IN.uv_MainTex));
#endif					
			o.Emission = clamp((_GlowColor.rgb * mask.a * _GlowColor.a + _DamageColor.rgb * _DamageColor.a), fixed3(0,0,0), fixed3(1,1,1)) * (1 - _BurnLevel);
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
		#pragma surface surf BlinnPhong
		#pragma target 3.0
		#pragma shader_feature _NORMALMAP
		#pragma shader_feature _DETAILMAP
		#pragma shader_feature _AOMAP 
		#pragma shader_feature _VERTEXCOLOR

		fixed4 _Color;
		fixed4 _Color1;
		fixed4 _Color2;
        fixed4 _GlowColor;
		fixed4 _DamageColor;

		sampler2D _MaskTex;
		sampler2D _SpecMap;
#ifdef _NORMALMAP
        sampler2D _BumpMap;
#endif
#ifdef _DETAILMAP
        sampler2D _DetailMap;
#endif
#ifdef _AOMAP
	    sampler2D _AOMap;
		fixed _AOScale;	
#endif

		struct Input {
            fixed4 color : COLOR;
			float2 uv_MainTex;
#ifdef _DETAILMAP
			float2 uv2_DetailMap;
#endif
			float3 viewDir;
		};

        fixed _Power;

		void surf (Input IN, inout SurfaceOutput o) {
			fixed4 col = fixed4(0,0,0,0);
			fixed4 mask = tex2D (_MaskTex, IN.uv_MainTex);
			fixed4 spec = tex2D (_SpecMap, IN.uv_MainTex);
#ifdef _DETAILMAP
			fixed4 patt = tex2D (_DetailMap, IN.uv2_DetailMap);
			_Color *= patt;
#endif
			col += _Color * mask.r;
			col += _Color1 * mask.g;
			col += _Color2 * mask.b;
					
			o.Albedo = col.rgb;
			o.Specular = col.a;
			o.Gloss = spec.a;

#ifdef _VERTEXCOLOR
			o.Albedo *= IN.color * _Power;
			o.Specular *= IN.color;
#endif			

#ifdef _AOMAP
			fixed4 ao = tex2D (_AOMap, IN.uv_MainTex);
			o.Albedo *= ao * _AOScale;
#endif

#ifdef _NORMALMAP
            o.Normal = UnpackNormal(tex2D (_BumpMap, IN.uv_MainTex));
#endif
			o.Emission = clamp((_GlowColor.rgb * mask.a * _GlowColor.a + _DamageColor.rgb * _DamageColor.a), fixed3(0,0,0), fixed3(1,1,1));
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
			#pragma shader_feature _DETAILMAP
			#include "UnityCG.cginc"

			fixed4 _Color;
			fixed4 _Color1;
			fixed4 _Color2;
			fixed4 _GlowColor;
			fixed4 _DamageColor;

			sampler2D _MaskTex;
			float4 _MaskTex_ST;

#ifdef _DETAILMAP
			sampler2D _DetailMap;
			float4 _DetailMap_ST;
#endif

			struct appdata_t
			{
				float4 vertex : POSITION;
				fixed4 color : COLOR;
				float3 normal : NORMAL;
				float2 texcoord : TEXCOORD0;
#ifdef _DETAILMAP
				float2 texcoord1 : TEXCOORD1;
#endif
			};

			struct v2f
			{
				float4 vertex : POSITION;
				fixed3 color : COLOR;
				float2 texcoord : TEXCOORD0;
#ifdef _DETAILMAP
				float2 texcoord1 : TEXCOORD1;
#endif
			};

			v2f vert (appdata_t v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.color = ShadeVertexLights(v.vertex, v.normal) * 2.0;
				o.color *= v.color;
				o.texcoord = TRANSFORM_TEX(v.texcoord, _MaskTex);
#ifdef _DETAILMAP
				o.texcoord1 = TRANSFORM_TEX(v.texcoord1, _DetailMap);
#endif
				return o;
			}

			fixed4 frag (v2f IN) : COLOR
			{
				fixed4 col = fixed4(0,0,0,0);
				fixed4 mask = tex2D (_MaskTex, IN.texcoord);
#ifdef _DETAILMAP
				fixed4 patt = tex2D (_DetailMap, IN.texcoord1);
				_Color *= patt;
#endif
				col += _Color * mask.r;
				col += _Color1 * mask.g;
				col += _Color2 * mask.b;		
				col.rgb *= IN.color;
				col.rgb += clamp((_GlowColor.rgb * mask.a * _GlowColor.a + _DamageColor.rgb * _DamageColor.a), fixed3(0,0,0), fixed3(1,1,1));;
				return col;
			}
		ENDCG
		}
	}
	
	FallBack "Diffuse"
	CustomEditor "DroidShaderEditor"
}