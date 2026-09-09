// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Project Droids/Burn Effect"
{
	Properties
	{
		_Visibility("Visibility", Range(0.0,1.0)) = 1
		_MainTex ("Burn Mask", 2D) = "white" {}
	}
	
	SubShader 
	{
		LOD 200
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha 
		Cull Off Lighting Off ZWrite Off Fog { Color (0,0,0,0) }
		Offset -1,-1

		Pass 
		{
			CGPROGRAM
			#pragma vertex vert 
			#pragma fragment frag
			#pragma fragmentoption ARB_precision_hint_fastest
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _Visibility;

			struct appdata_t
			{
				float4 vertex : POSITION;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 texcoord : TEXCOORD0;
			};

			v2f vert (appdata_t v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
				 
				return o;
			}

			fixed4 frag(v2f i) : COLOR
			{
				fixed4 c = fixed4(0,0,0,1);
				fixed4 t = tex2D(_MainTex, i.texcoord.xy);
				c.a *= floor(_Visibility + min(0.99, t.r));
				return c;
			}

			ENDCG
		}

		// Pass to render object as a shadow caster
		Pass 
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }
		
			ZWrite On ZTest LEqual Cull Off 
			
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_shadowcaster
			#include "UnityCG.cginc"

			struct v2f { 
				V2F_SHADOW_CASTER;
				float2  uv : TEXCOORD1;
			};

			uniform float4 _MainTex_ST;

			v2f vert( appdata_base v )
			{
				v2f o;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
				o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
				return o;
			}

			uniform sampler2D _MainTex;
			uniform fixed _Visibility;
			
			float4 frag( v2f i ) : SV_Target
			{
				fixed4 texcol = tex2D( _MainTex, i.uv );
				texcol.a = texcol.r;
				clip( texcol.a - (1 - _Visibility) );
			
				SHADOW_CASTER_FRAGMENT(i)
			}
			ENDCG
		} 
	}
}