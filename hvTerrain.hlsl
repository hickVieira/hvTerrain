#ifndef HVTERRAIN
    #define HVTERRAIN

    half2 _TexturesScale[256];
    SAMPLER(SamplerState_Linear_Repeat);
    SAMPLER(SamplerState_Point_Clamp);
    half4 _AlbedoMaps_TexelSize;

    // https://www.gamedev.net/tutorials/programming/graphics/advanced-terrain-texture-splatting-r3287/
    // inline half4 BlendTexture(half4 texA, half4 texB, half t, half depth)
    // {
        //     half lumiA = Luminance(texA.rgb) / _TextureBlend0;
        //     half lumiB = Luminance(texB.rgb) / _TextureBlend0;

        //     half tA = 1.0 - t;
        //     half tB = t;

        //     half ma = max(lumiA + tA, lumiB + tB) - depth;
        //     half b1 = max(lumiA + tA - ma, 0);
        //     half b2 = max(lumiB + tB - ma, 0);
        //     return (texA * b1 + texB * b2) / (b1 + b2);
    // }

    half Sample_Terrain_HolesMap(float2 uv)
    {
        float hole = SAMPLE_TEXTURE2D(_HolesMap, sampler_HolesMap, uv).r;
        return hole == 0.0f ? -1 : 1;
    }

    inline half4 Sample_Terrain_Texture2DArray_ID01(Texture2DArray tex, SamplerState ss, half2 idUV, half2 texUV, half4 ddxy)
    {
        half3 blendData = SAMPLE_TEXTURE2D_LOD(_BlendMap0, SamplerState_Point_Clamp, idUV, 0).rgb;
        int id0 = blendData.r * 255;
        int id1 = blendData.g * 255;
        half weight = blendData.b;

        half2 tilling0 = _TexturesScale[id0];
        half2 tilling1 = _TexturesScale[id1];

        half4 ddxy0 = half4(ddxy / ((tilling0.x + tilling0.y) / 2));
        half4 ddxy1 = half4(ddxy / ((tilling1.x + tilling1.y) / 2));

        half2 uv0 = texUV / tilling0.xy;
        half2 uv1 = texUV / tilling1.xy;

        half4 texSample0 = PLATFORM_SAMPLE_TEXTURE2D_ARRAY_GRAD(tex, ss, uv0, id0, ddxy0.xy, ddxy0.zw);
        half4 texSample1 = PLATFORM_SAMPLE_TEXTURE2D_ARRAY_GRAD(tex, ss, uv1, id1, ddxy1.xy, ddxy1.zw);

        // return BlendTexture(texSample0, texSample1, weight, _TextureBlend1);
        return lerp(texSample0, texSample1, weight);
    }

    inline half4 Sample_Terrain_Texture2DArray_ID0(Texture2DArray tex, SamplerState ss, half2 idUV, half2 texUV, half4 ddxy)
    {
        half blendData = SAMPLE_TEXTURE2D_LOD(_BlendMap0, SamplerState_Point_Clamp, idUV, 0).r;
        int id0 = blendData * 255;

        half2 tilling0 = _TexturesScale[id0];

        half4 ddxy0 = half4(ddxy / ((tilling0.x + tilling0.y) / 2));

        half2 uv0 = texUV / tilling0.xy;

        half4 texSample0 = PLATFORM_SAMPLE_TEXTURE2D_ARRAY_GRAD(tex, ss, uv0, id0, ddxy0.xy, ddxy0.zw);

        return texSample0;
    }

    // https://community.khronos.org/t/manual-bilinear-filter/58504/8
    // https://community.khronos.org/t/bilinear-interpolation-texture-rendering/52613
    inline half4 Sample_Terrain_Texture2DArray_ID01_Bilinear(Texture2DArray tex, SamplerState ss, half2 idUV, half2 texUV, half4 ddxy)
    {
        half2 f = frac(idUV * _BlendMap0_TexelSize.zw);
        // offset to fix seam
        idUV += (0.5 - f) * _BlendMap0_TexelSize.xy;

        half4 bottomL  = Sample_Terrain_Texture2DArray_ID01(tex, ss, idUV, texUV, ddxy);
        half4 bottomR  = Sample_Terrain_Texture2DArray_ID01(tex, ss, idUV + half2(_BlendMap0_TexelSize.x, 0), texUV, ddxy);
        half4 topL     = Sample_Terrain_Texture2DArray_ID01(tex, ss, idUV + half2(0, _BlendMap0_TexelSize.y), texUV, ddxy);
        half4 topR     = Sample_Terrain_Texture2DArray_ID01(tex, ss, idUV + half2(_BlendMap0_TexelSize.x, _BlendMap0_TexelSize.y), texUV, ddxy);
        
        half4 colA = lerp(bottomL, bottomR, f.x);
        half4 colB = lerp(topL, topR, f.x);
        return lerp(colA, colB, f.y);
    }

    inline half4 Sample_Terrain_Texture2DArray_ID0_Bilinear(Texture2DArray tex, SamplerState ss, half2 idUV, half2 texUV, half4 ddxy)
    {
        half2 f = frac(idUV * _BlendMap0_TexelSize.zw);
        // offset to fix seam
        idUV += (0.5 - f) * _BlendMap0_TexelSize.xy;

        half4 bottomL  = Sample_Terrain_Texture2DArray_ID0(tex, ss, idUV, texUV, ddxy);
        half4 bottomR  = Sample_Terrain_Texture2DArray_ID0(tex, ss, idUV + half2(_BlendMap0_TexelSize.x, 0), texUV, ddxy);
        half4 topL     = Sample_Terrain_Texture2DArray_ID0(tex, ss, idUV + half2(0, _BlendMap0_TexelSize.y), texUV, ddxy);
        half4 topR     = Sample_Terrain_Texture2DArray_ID0(tex, ss, idUV + half2(_BlendMap0_TexelSize.x, _BlendMap0_TexelSize.y), texUV, ddxy);
        
        half4 colA = lerp(bottomL, bottomR, f.x);
        half4 colB = lerp(topL, topR, f.x);
        return lerp(colA, colB, f.y);
    }

    inline half3 UnpackNormal_TS(half2 normalTSSample)
    {
        // more vecs
        half3 normalTS;
        normalTS.xy = normalTSSample.rg * 2.0 - 1.0;
        normalTS.z = max(1.0e-16, sqrt(1.0 - saturate(dot(normalTS.xy, normalTS.xy))));
        // normalTS.xy *= 2;
        return normalTS;
    }

    // using triplanar code from: https://bgolus.medium.com/normal-mapping-for-a-triplanar-shader-10bf39dca05a
    void Fragment_half(in half2 inUV, in half3 inPositionWS, out half3 outColor, out half3 outNormal, out half outSmoothness, out half outAlpha)
    {
        // derivs
        half4 ddxy = half4(ddx(inUV * _BlendMap0_TexelSize.z), ddy(inUV * _BlendMap0_TexelSize.w)) / _MipMapFactor;

        // offset to fix seam + proper fit (magic number i dunno)
        half2 blendMapUV = inUV - inUV * _BlendMap0_TexelSize.xy;

        // samples
        half4 blendSample1 = SAMPLE_TEXTURE2D(_BlendMap1, SamplerState_Linear_Repeat, blendMapUV);
        half3 normalWS = blendSample1.xyz * 2.0 - 1.0;
        #if TRIPLANAR
            half axisBlend = blendSample1.a;
        #endif

        // alpha0 is coded as: 00000[000] -> [bilinear x z] (x z are uv axis)
        // hopefully GPUs support bitwise ops ?
        int alpha0 = SAMPLE_TEXTURE2D_LOD(_BlendMap0, SamplerState_Point_Clamp, blendMapUV, 0).a * 255;
        #if TRIPLANAR
            bool isX = alpha0 & 0x2;
            bool isZ = alpha0 & 0x1;
        #endif
        #if BILINEAR
            bool useBilinear = alpha0 & 0x4;
        #endif

        #if NORMALMAP
            // Get the sign (-1 or 1) of the surface normal
            half3 axisSign = sign(normalWS);

            // Construct tangent to world matrices for each axis
            half3 tangentY = normalize(cross(normalWS, half3(0, 0, axisSign.y)));
            half3 bitangentY = normalize(cross(tangentY, normalWS)) * axisSign.y;
            half3x3 tbnY = half3x3(tangentY, bitangentY, normalWS);
        #endif

        // get some vecs
        half2 yUV = inPositionWS.xz; // y facing plane
        #if TRIPLANAR
            half2 xUV = inPositionWS.zy; // x facing plane
            half2 zUV = inPositionWS.xy; // z facing plane
            half2 texUV = lerp(lerp(yUV, xUV, isX), zUV, isZ);

            #if NORMALMAP
                // define tbns for all axis
                half3 tangentX = normalize(cross(normalWS, half3(0, axisSign.x, 0)));
                half3 bitangentX = normalize(cross(tangentX, normalWS)) * axisSign.x;
                half3x3 tbnX = half3x3(tangentX, bitangentX, normalWS);

                half3 tangentZ = normalize(cross(normalWS, half3(0, -axisSign.z, 0)));
                half3 bitangentZ = normalize(-cross(tangentZ, normalWS)) * axisSign.z;
                half3x3 tbnZ = half3x3(tangentZ, bitangentZ, normalWS);
                
                half3x3 tbn = lerp(lerp(tbnY, tbnX, isX), tbnZ, isZ);
            #endif
        #else
            half2 texUV = yUV;
            #if NORMALMAP
                half3x3 tbn = tbnY;
            #endif
        #endif

        half4 albedoSmooth;
        half3 normalWSblended = normalWS;
        #if NORMALMAP
            half3 normalTS;
        #endif
        half holes;

        #if HOLES
            holes = Sample_Terrain_HolesMap(inUV);
        #else
            holes = 1;
        #endif

        #if BILINEAR
            #if USEBRANCHES
                UNITY_BRANCH
            #endif
            if (useBilinear)
            {
                albedoSmooth = Sample_Terrain_Texture2DArray_ID01_Bilinear(_AlbedoMaps, SamplerState_Linear_Repeat, blendMapUV, texUV, ddxy);
                #if NORMALMAP
                    normalTS = UnpackNormal_TS(Sample_Terrain_Texture2DArray_ID0_Bilinear(_NormalMaps, SamplerState_Linear_Repeat, blendMapUV, texUV, ddxy).rg);
                    normalWSblended = normalize(clamp(mul(normalTS, tbn), -1, 1));
                #endif
                
                #if TRIPLANAR
                    // this branch blurs the 'triplanar' at bad angles
                    #if USEBRANCHES
                        UNITY_BRANCH
                    #endif
                    if (axisBlend > 0)
                    {
                        // blends
                        half3 blend = pow(normalWS, 4);
                        blend /= dot(blend, half3(1,1,1));

                        // albedo
                        half4 xAlbedoSample = Sample_Terrain_Texture2DArray_ID01_Bilinear(_AlbedoMaps, SamplerState_Linear_Repeat, blendMapUV, xUV, ddxy);
                        half4 yAlbedoSample = Sample_Terrain_Texture2DArray_ID01_Bilinear(_AlbedoMaps, SamplerState_Linear_Repeat, blendMapUV, yUV, ddxy);
                        half4 zAlbedoSample = Sample_Terrain_Texture2DArray_ID01_Bilinear(_AlbedoMaps, SamplerState_Linear_Repeat, blendMapUV, zUV, ddxy);

                        albedoSmooth = lerp(albedoSmooth, xAlbedoSample * blend.x + yAlbedoSample * blend.y + zAlbedoSample * blend.z, axisBlend);

                        // normals
                        #if NORMALMAP
                            half3 xNormalTSSample = UnpackNormal_TS(Sample_Terrain_Texture2DArray_ID01_Bilinear(_NormalMaps, SamplerState_Linear_Repeat, blendMapUV, xUV, ddxy).rg);
                            half3 yNormalTSSample = UnpackNormal_TS(Sample_Terrain_Texture2DArray_ID01_Bilinear(_NormalMaps, SamplerState_Linear_Repeat, blendMapUV, yUV, ddxy).rg);
                            half3 zNormalTSSample = UnpackNormal_TS(Sample_Terrain_Texture2DArray_ID01_Bilinear(_NormalMaps, SamplerState_Linear_Repeat, blendMapUV, zUV, ddxy).rg);

                            // Apply tangent to world matrix and triblend
                            // Using clamp() because the cross products may be NANs
                            normalWSblended = lerp(normalWSblended, normalize(
                            clamp(mul(xNormalTSSample, tbnX), -1, 1) * blend.x +
                            clamp(mul(yNormalTSSample, tbnY), -1, 1) * blend.y +
                            clamp(mul(zNormalTSSample, tbnZ), -1, 1) * blend.z),
                            axisBlend);
                        #endif
                    }
                #endif
            }
            else
            {
            #endif
            albedoSmooth = Sample_Terrain_Texture2DArray_ID01(_AlbedoMaps, SamplerState_Linear_Repeat, blendMapUV, texUV, ddxy);
            #if NORMALMAP
                normalTS = UnpackNormal_TS(Sample_Terrain_Texture2DArray_ID0(_NormalMaps, SamplerState_Linear_Repeat, blendMapUV, texUV, ddxy).rg);
                normalWSblended = normalize(clamp(mul(normalTS, tbn), -1, 1));
            #endif
            
            #if TRIPLANAR
                // this branch blurs the 'triplanar' at bad angles
                #if USEBRANCHES
                    UNITY_BRANCH
                #endif
                if (axisBlend > 0)
                {
                    // blends
                    half3 blend = pow(normalWS, 4);
                    blend /= dot(blend, half3(1,1,1));

                    // albedo
                    half4 xAlbedoSample = Sample_Terrain_Texture2DArray_ID01(_AlbedoMaps, SamplerState_Linear_Repeat, blendMapUV, xUV, ddxy);
                    half4 yAlbedoSample = Sample_Terrain_Texture2DArray_ID01(_AlbedoMaps, SamplerState_Linear_Repeat, blendMapUV, yUV, ddxy);
                    half4 zAlbedoSample = Sample_Terrain_Texture2DArray_ID01(_AlbedoMaps, SamplerState_Linear_Repeat, blendMapUV, zUV, ddxy);

                    albedoSmooth = lerp(albedoSmooth, xAlbedoSample * blend.x + yAlbedoSample * blend.y + zAlbedoSample * blend.z, axisBlend);

                    // normals
                    #if NORMALMAP
                        half3 xNormalTSSample = UnpackNormal_TS(Sample_Terrain_Texture2DArray_ID01(_NormalMaps, SamplerState_Linear_Repeat, blendMapUV, xUV, ddxy).rg);
                        half3 yNormalTSSample = UnpackNormal_TS(Sample_Terrain_Texture2DArray_ID01(_NormalMaps, SamplerState_Linear_Repeat, blendMapUV, yUV, ddxy).rg);
                        half3 zNormalTSSample = UnpackNormal_TS(Sample_Terrain_Texture2DArray_ID01(_NormalMaps, SamplerState_Linear_Repeat, blendMapUV, zUV, ddxy).rg);

                        // Apply tangent to world matrix and triblend
                        // Using clamp() because the cross products may be NANs
                        normalWSblended = lerp(normalWSblended, normalize(
                        clamp(mul(xNormalTSSample, tbnX), -1, 1) * blend.x +
                        clamp(mul(yNormalTSSample, tbnY), -1, 1) * blend.y +
                        clamp(mul(zNormalTSSample, tbnZ), -1, 1) * blend.z),
                        axisBlend);
                    #endif
                }
            #endif
            #if BILINEAR
            }
        #endif

        outColor = albedoSmooth.rgb;
        outNormal = normalWSblended;
        outSmoothness = albedoSmooth.a;
        outAlpha = holes;
    }
#endif