// Erosion noise based on https://www.shadertoy.com/view/MtGcWh

Shader "hickv/hvTerrainBaker"
{
	Properties
	{
	}
	SubShader
	{
		ZTest LEqual
		ZWrite on
		Cull off
		
		// albedoMap blit
		Pass
		{
			Blend one zero
			HLSLPROGRAM
			#pragma target 4.5

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_AlbedoMap); 
			SAMPLER(SamplerState_TriLinear_Clamp);
			float _Smoothness;

			struct Attributes
			{
				float3 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.uv = input.uv;

				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				float4 texSample = SAMPLE_TEXTURE2D_LOD(_AlbedoMap, SamplerState_TriLinear_Clamp, input.uv, 0);
				return float4(texSample.rgb, texSample.a * _Smoothness);
			}
			ENDHLSL
		}

		// normalMap unpack blit
		Pass
		{
			Blend one zero
			HLSLPROGRAM
			#pragma target 4.5

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_NormalMap); 
			SAMPLER(SamplerState_Linear_Clamp);
			float _NormalScale;

			struct Attributes
			{
				float3 positionOS 	: POSITION;
				float2 uv 			: TEXCOORD0;
			};

			struct Varyings
			{
				float2 uv 			: TEXCOORD0;
				float4 positionCS 	: SV_POSITION;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
				output.positionCS = vertexInput.positionCS;
				output.uv = input.uv;

				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				float4 packedNormal = SAMPLE_TEXTURE2D_LOD(_NormalMap, SamplerState_Linear_Clamp, input.uv, 0);
				packedNormal.w *= packedNormal.x;
				return float4(packedNormal.wy * _NormalScale, 0, 0);
			}
			ENDHLSL
		}
	}
}
