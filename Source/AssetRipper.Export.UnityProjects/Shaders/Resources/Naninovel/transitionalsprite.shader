Shader "Naninovel/TransitionalSprite" {
	Properties {
		_MainTex ("Main Texture", 2D) = "black" {}
		_TransitionTex ("Transition Texture", 2D) = "black" {}
		_CloudsTex ("Clouds Texture", 2D) = "black" {}
		_DissolveTex ("Dissolve Texture", 2D) = "black" {}
		_TransitionProgress ("Transition Progress", Float) = 0
		_TransitionParams ("Transition Parameters", Vector) = (1,1,1,1)
		_TintColor ("Tint Color", Vector) = (1,1,1,1)
		_RandomSeed ("Random Seed", Vector) = (0,0,0,0)
		_Flip ("Flip", Vector) = (1,1,1,1)
		_FlipMainX ("Flip Main X", Float) = 0
		_DepthAlphaCutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
	}
	SubShader {
		Tags { "RenderType"="Sprite" "Queue"="Transparent" }
		LOD 200
		Cull Off ZWrite Off ZTest LEqual
		Blend SrcAlpha OneMinusSrcAlpha

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile NANINOVEL_TRANSITION_CROSSFADE NANINOVEL_TRANSITION_BANDEDSWIRL NANINOVEL_TRANSITION_BLINDS NANINOVEL_TRANSITION_CIRCLEREVEAL NANINOVEL_TRANSITION_CIRCLESTRETCH NANINOVEL_TRANSITION_CLOUDREVEAL NANINOVEL_TRANSITION_CRUMBLE NANINOVEL_TRANSITION_DISSOLVE NANINOVEL_TRANSITION_DROPFADE NANINOVEL_TRANSITION_LINEREVEAL NANINOVEL_TRANSITION_PIXELATE NANINOVEL_TRANSITION_RADIALBLUR NANINOVEL_TRANSITION_RADIALWIGGLE NANINOVEL_TRANSITION_RANDOMCIRCLEREVEAL NANINOVEL_TRANSITION_RIPPLE NANINOVEL_TRANSITION_ROTATECRUMBLE NANINOVEL_TRANSITION_SATURATE NANINOVEL_TRANSITION_SHRINK NANINOVEL_TRANSITION_SLIDEIN NANINOVEL_TRANSITION_SWIRLGRID NANINOVEL_TRANSITION_SWIRL NANINOVEL_TRANSITION_WATER NANINOVEL_TRANSITION_WATERFALL NANINOVEL_TRANSITION_WAVE NANINOVEL_TRANSITION_CUSTOM
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			sampler2D _TransitionTex;
			sampler2D _CloudsTex;
			sampler2D _DissolveTex;
			float4 _MainTex_ST;
			float _TransitionProgress;
			float4 _TransitionParams;
			float4 _TintColor;
			float4 _RandomSeed;
			float4 _Flip;
			float _FlipMainX;
			float _DepthAlphaCutoff;

		struct appdata {
			float4 pos : POSITION;
			float2 uv : TEXCOORD0;
		};

		struct v2f {
			float4 pos : SV_POSITION;
			float2 uv : TEXCOORD0;
		};

		v2f vert(appdata v) {
			v2f o;
			o.pos = UnityObjectToClipPos(v.pos);
			o.uv = TRANSFORM_TEX(v.uv, _MainTex);
			return o;
		}

			float rand(float2 co) {
				return frac(sin(dot(co.xy, float2(12.9898, 78.233))) * 43758.5453);
			}

			float computeDissolve(float2 uv) {
				float p = _TransitionProgress;
				float4 params = _TransitionParams;
				float2 centered = uv - 0.5;
				float dist = length(centered);
				float angle = atan2(centered.y, centered.x);

				#if defined(NANINOVEL_TRANSITION_CROSSFADE)
					return p;
				#elif defined(NANINOVEL_TRANSITION_DISSOLVE)
					float d = tex2D(_DissolveTex, uv).r;
					return p * 2.0 - d;
				#elif defined(NANINOVEL_TRANSITION_CRUMBLE)
					float d = tex2D(_DissolveTex, uv).r * 0.5;
					return p * 1.5 - d;
				#elif defined(NANINOVEL_TRANSITION_ROTATECRUMBLE)
					float2 ruv = uv - 0.5;
					float r = length(ruv);
					float a = atan2(ruv.y, ruv.x) + p * 6.28;
					ruv = float2(cos(a), sin(a)) * r + 0.5;
					float d = tex2D(_DissolveTex, ruv).r * 0.5;
					return p * 1.5 - d;
				#elif defined(NANINOVEL_TRANSITION_PIXELATE)
					float pixels = lerp(1.0, 100.0, p);
					float2 puv = floor(uv * pixels) / pixels;
					return length(uv - puv) < 0.01 ? 1.0 : p;
				#elif defined(NANINOVEL_TRANSITION_CIRCLEREVEAL)
					float radius = params.x;
					float2 center = float2(params.y, params.z);
					if (params.z == 0) center = float2(0.5, 0.5);
					return p * (1.0 + radius * 2.0) - length(uv - center) + radius;
				#elif defined(NANINOVEL_TRANSITION_RANDOMCIRCLEREVEAL)
					float2 seed = _RandomSeed.xy;
					float c = rand(uv + seed);
					return p * 2.0 - c;
				#elif defined(NANINOVEL_TRANSITION_CIRCLESTRETCH)
					return p * 2.0 - dist * 2.0;
				#elif defined(NANINOVEL_TRANSITION_SHRINK)
					float scale = params.x;
					return p * 2.0 - dist * scale;
				#elif defined(NANINOVEL_TRANSITION_DROPFADE)
					return p * 2.0 - uv.y;
				#elif defined(NANINOVEL_TRANSITION_SLIDEIN)
					float dir = params.x;
					float2 off = float2(dir, params.y);
					return p * 2.0 - dot(uv - 0.5, normalize(off + 0.0001)) - 0.5;
				#elif defined(NANINOVEL_TRANSITION_LINEREVEAL)
					float thickness = params.x;
					float angle = params.y * 3.14159;
					float2 dir = float2(cos(angle), sin(angle));
					float proj = dot(uv - 0.5, dir);
					return p * 2.0 - proj - thickness;
				#elif defined(NANINOVEL_TRANSITION_RADIALBLUR)
					return p * 2.0 - dist * 2.0;
				#elif defined(NANINOVEL_TRANSITION_RIPPLE)
					float freq = params.x;
					float amp = params.y;
					float decay = params.z;
					float ripple = sin(dist * freq - p * 10.0) * amp * exp(-dist * decay);
					return p * 2.0 - dist * 2.0 + ripple;
				#elif defined(NANINOVEL_TRANSITION_WAVE)
					float freq = params.y;
					float amp = params.x;
					float wave = sin(uv.x * freq + p * 10.0) * amp;
					return p * 2.0 - uv.y + wave;
				#elif defined(NANINOVEL_TRANSITION_BANDEDSWIRL)
					float bands = params.x;
					float strength = params.y;
					float swirl = sin(angle * bands + dist * strength) * 0.5 + 0.5;
					return p * 2.0 - swirl - dist;
				#elif defined(NANINOVEL_TRANSITION_SWIRL)
					float strength = params.x;
					float2 suv = uv - 0.5;
					float r = length(suv);
					float a = atan2(suv.y, suv.x) + r * strength * (1.0 - p);
					suv = float2(cos(a), sin(a)) * r + 0.5;
					return p * 2.0 - length(uv - suv);
				#elif defined(NANINOVEL_TRANSITION_SWIRLGRID)
					float strength = params.x;
					float2 guv = uv * params.y;
					float2 g = frac(guv) - 0.5;
					float gl = length(g);
					float2 suv = uv - 0.5;
					float r = length(suv);
					float a = atan2(suv.y, suv.x) + gl * strength * (1.0 - p);
					suv = float2(cos(a), sin(a)) * r + 0.5;
					return p * 2.0 - length(uv - suv);
				#elif defined(NANINOVEL_TRANSITION_RADIALWIGGLE)
					float wig = sin(angle * 8.0 + dist * 20.0) * 0.05;
					return p * 2.0 - dist * 2.0 + wig;
				#elif defined(NANINOVEL_TRANSITION_BLINDS)
					float blinds = params.x;
					float b = frac(uv.y * blinds);
					return p * 2.0 - b;
				#elif defined(NANINOVEL_TRANSITION_CLOUDREVEAL)
					float c = tex2D(_CloudsTex, uv).r;
					return p * 2.0 - c;
				#elif defined(NANINOVEL_TRANSITION_WATER)
					float w = sin(uv.x * 20.0 + p * 10.0) * 0.02;
					return p * 2.0 - uv.y + w;
				#elif defined(NANINOVEL_TRANSITION_WATERFALL)
					float w = sin(uv.y * 20.0 + p * 10.0) * 0.02;
					return p * 2.0 - uv.x + w;
				#elif defined(NANINOVEL_TRANSITION_SATURATE)
					return p;
				#elif defined(NANINOVEL_TRANSITION_CUSTOM)
					return p;
				#else
					return p;
				#endif
			}

			float4 frag(v2f i) : SV_Target {
				float2 mainUV = i.uv;
				if (_FlipMainX > 0.5) mainUV.x = 1.0 - mainUV.x;
				float4 mainColor = tex2D(_MainTex, mainUV) * _TintColor;
				float4 transitionColor = tex2D(_TransitionTex, i.uv) * _TintColor;

				float d = computeDissolve(i.uv);
				float blend = saturate(smoothstep(d - 0.5, d + 0.5, _TransitionProgress * 2.0 - 0.5));
				float4 result = lerp(mainColor, transitionColor, blend);
				return result;
			}
			ENDCG
		}
	}
	Fallback "Transparent/VertexLit"
}