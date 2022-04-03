#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using System.IO;

/*
    BlendMap0 channels:
        R - texture id0
        G - texture id1
        B - weight
        A - axis

    BlendMap1 channels:
        R - normal x
        G - normal y
        B - normal z
        A - axisBlend
*/

public class hvTerrainBaker : MonoBehaviour
{
    [BurstCompile]
    struct BuildBlendMapJob : IJob
    {
        public struct SplatPixelData : System.IComparable<SplatPixelData>
        {
            public byte id;
            public byte weight;

            public SplatPixelData(byte id, byte weight)
            {
                this.id = (byte)math.clamp(id, 0, 255);
                this.weight = (byte)math.clamp(weight, 0, 255);
            }

            public int CompareTo(SplatPixelData other)
            {
                if (this.weight > other.weight)
                    return -1;
                if (this.weight < other.weight)
                    return 1;
                return 0;
            }
        }

        [ReadOnly] public NativeArray<Color32> inAlphaMaps;
        public NativeArray<Color> inoutBlendMap1;
        public NativeArray<Color32> inoutBlendMap0;
        public int resolution;
        public int splatCount;

        // alphaMap =      RGBA 8888
        // blendMap =      RGBA 8888
        public void Execute()
        {
            // calculate ids and weights
            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    // get all alpha ids that influence current pixel
                    NativeList<SplatPixelData> splats = new NativeList<SplatPixelData>(Allocator.Temp);
                    GetSplatsAtPixel(x, y, splats);

                    if (splats.Length > 0)
                    {
                        splats.Sort();

                        byte splatID0 = splats[0].id;
                        byte splatID1 = splatID0;
                        byte weight = 0;
                        byte alpha = 0;
                        if (splats.Length > 1)
                        {
                            if (splats[1].weight > 0)
                            {
                                splatID1 = splats[1].id;
                                weight = splats[1].weight;
                            }
                        }

                        int blendIndex = GetIndex(x, y, 0);

                        // calculate axis
                        Color blend1 = inoutBlendMap1[blendIndex];
                        Vector3 normalWS = new Vector3(blend1.r * 2f - 1f, blend1.g * 2f - 1f, blend1.b * 2f - 1f);
                        float facingX = math.abs(Vector3.Dot(normalWS, Vector3.right));
                        float facingY = math.abs(Vector3.Dot(normalWS, Vector3.up));
                        float facingZ = math.abs(Vector3.Dot(normalWS, Vector3.forward));

                        bool isX = facingX > facingY && facingX > facingZ;
                        bool isY = facingY > facingX && facingY > facingX;
                        bool isZ = facingZ > facingX && facingZ > facingY;

                        // bilinear is solved later
                        // (x, y, z) = (0, 1, 2)
                        // 00000[000] -> bxz (bilinear x z)
                        if (isX)
                            alpha = 1 << 1;
                        else if (isZ)
                            alpha = 1 << 0;
                        else if (isY)
                            alpha = 0;

                        inoutBlendMap0[blendIndex] = new Color32(splatID0, splatID1, weight, alpha);
                    }
                }
            }

            // calculate bilinear + axisBlend
            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    // pick neighbors
                    int indexC = GetIndex_Safe(x, y, 0);
                    int indexR = GetIndex_Safe(x + 1, y, 0);
                    int indexL = GetIndex_Safe(x - 1, y, 0);
                    int indexT = GetIndex_Safe(x, y + 1, 0);
                    int indexB = GetIndex_Safe(x, y - 1, 0);

                    int indexRT = GetIndex_Safe(x + 1, y + 1, 0);
                    int indexLT = GetIndex_Safe(x - 1, y + 1, 0);
                    int indexTR = GetIndex_Safe(x + 1, y + 1, 0);
                    int indexBL = GetIndex_Safe(x - 1, y - 1, 0);

                    // samples
                    Color blend1 = inoutBlendMap1[indexC];
                    Color32 blendC = inoutBlendMap0[indexC];
                    Color32 blendR = inoutBlendMap0[indexR];
                    Color32 blendL = inoutBlendMap0[indexL];
                    Color32 blendT = inoutBlendMap0[indexT];
                    Color32 blendB = inoutBlendMap0[indexB];

                    Color32 blendRT = inoutBlendMap0[indexRT];
                    Color32 blendLT = inoutBlendMap0[indexLT];
                    Color32 blendTR = inoutBlendMap0[indexTR];
                    Color32 blendBL = inoutBlendMap0[indexBL];

                    // 00000[000] -> bxz (bilinear x z)
                    byte alpha = blendC.a;
                    float axisBlend = 0;

                    // bilinear
                    bool bilinearR = (blendR.r != blendC.r) || (blendR.g != blendC.g) || (blendR.r != blendC.g);
                    bool bilinearL = (blendL.r != blendC.r) || (blendL.g != blendC.g) || (blendL.r != blendC.g);
                    bool bilinearT = (blendT.r != blendC.r) || (blendT.g != blendC.g) || (blendT.r != blendC.g);
                    bool bilinearB = (blendB.r != blendC.r) || (blendB.g != blendC.g) || (blendB.r != blendC.g);

                    bool bilinearRT = (blendRT.r != blendC.r) || (blendRT.g != blendC.g) || (blendRT.r != blendC.g);
                    bool bilinearLT = (blendLT.r != blendC.r) || (blendLT.g != blendC.g) || (blendLT.r != blendC.g);
                    bool bilinearTR = (blendTR.r != blendC.r) || (blendTR.g != blendC.g) || (blendTR.r != blendC.g);
                    bool bilinearBL = (blendBL.r != blendC.r) || (blendBL.g != blendC.g) || (blendBL.r != blendC.g);

                    if (bilinearR || bilinearL || bilinearT || bilinearB || bilinearRT || bilinearLT || bilinearTR || bilinearBL)
                        alpha |= 1 << 2;

                    // axisBlend
                    bool axisBlendR = (GetAxis(blendR.a) != GetAxis(blendC.a));
                    bool axisBlendL = (GetAxis(blendL.a) != GetAxis(blendC.a));
                    bool axisBlendT = (GetAxis(blendT.a) != GetAxis(blendC.a));
                    bool axisBlendB = (GetAxis(blendB.a) != GetAxis(blendC.a));

                    bool axisBlendRT = (GetAxis(blendRT.a) != GetAxis(blendC.a));
                    bool axisBlendLT = (GetAxis(blendLT.a) != GetAxis(blendC.a));
                    bool axisBlendTR = (GetAxis(blendTR.a) != GetAxis(blendC.a));
                    bool axisBlendBL = (GetAxis(blendBL.a) != GetAxis(blendC.a));

                    if (axisBlendR || axisBlendL || axisBlendT || axisBlendB || axisBlendRT || axisBlendLT || axisBlendTR || axisBlendBL)
                        axisBlend = 1.0f;

                    // set
                    inoutBlendMap0[indexC] = new Color32(blendC.r, blendC.g, blendC.b, alpha);
                    inoutBlendMap1[indexC] = new Color(blend1.r, blend1.g, blend1.b, axisBlend);
                }
            }
        }

        // (x, y, z) = (0, 1, 2)
        int GetAxis(byte alpha)
        {
            int axis;
            if ((alpha & 0x1) > 0) // z
                axis = 2;
            else if ((alpha & 0x2) > 0) // x
                axis = 0;
            else
                axis = 1; // y
            return axis;
        }

        int GetIndex(int x, int y, int depth)
        {
            return (x + y * resolution) + depth * (resolution * resolution);
        }

        int GetIndex_Safe(int x, int y, int depth)
        {
            x = math.clamp(x, 0, resolution - 1);
            y = math.clamp(y, 0, resolution - 1);
            depth = math.clamp(depth, 0, splatCount - 1);
            return GetIndex(x, y, depth);
        }

        void GetSplatsAtPixel(int x, int y, NativeList<SplatPixelData> outSplats)
        {
            for (int depth = 0; depth < splatCount; depth++)
            {
                int alphaIndex = GetIndex(x, y, depth);

                Color32 color = inAlphaMaps[alphaIndex];
                if (color.r > 0)
                    outSplats.Add(new SplatPixelData((byte)(depth * 4 + 0), color.r));
                if (color.g > 0)
                    outSplats.Add(new SplatPixelData((byte)(depth * 4 + 1), color.g));
                if (color.b > 0)
                    outSplats.Add(new SplatPixelData((byte)(depth * 4 + 2), color.b));
                if (color.a > 0)
                    outSplats.Add(new SplatPixelData((byte)(depth * 4 + 3), color.a));
            }
        }
    }

    [System.Serializable]
    enum TextureResolution : int
    {
        _128 = 128,
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096,
    }

    [Header("Baking")]
    [SerializeField] private Terrain _terrain;
    [SerializeField] private Material _unityTerrainMaterial;
    [SerializeField] private Material _hvTerrainMaterial;
    [SerializeField] private TextureResolution _albedoTextureArrayResolution = TextureResolution._1024;
    [SerializeField] private TextureResolution _normalTextureArrayResolution = TextureResolution._1024;

    [Header("Baked Data")]
    [SerializeField] private Texture2D _blendTexture0;
    [SerializeField] private Texture2D _blendTexture1;
    [SerializeField] private Texture2DArray _albedoTextureArray;
    [SerializeField] private Texture2DArray _normalTextureArray;

    [Header("Debug Baker Data")]
    [SerializeField] private Shader _terrainBakerShader;
    [SerializeField] private Material _terrainBakerMaterial;

    public void UpdateHVTerrainMaterial()
    {
        // set materials data
        _hvTerrainMaterial.SetTexture("_BlendMap0", _blendTexture0);
        _hvTerrainMaterial.SetTexture("_BlendMap1", _blendTexture1);
        _hvTerrainMaterial.SetTexture("_HolesMap", _terrain.terrainData.holesTexture);
        _hvTerrainMaterial.SetTexture("_AlbedoMaps", _albedoTextureArray);
        _hvTerrainMaterial.SetTexture("_NormalMaps", _normalTextureArray);

        // update tilling/offset stuff
        TerrainLayer[] terrainLayers = _terrain.terrainData.terrainLayers;
        Vector4[] texturesScale = new Vector4[256];
        for (int i = 0; i < terrainLayers.Length; i++)
            texturesScale[i] = new Vector4(terrainLayers[i].tileSize.x, terrainLayers[i].tileSize.y, 0, 0);
        _hvTerrainMaterial.SetVectorArray("_TexturesScale", texturesScale);
    }

#if UNITY_EDITOR
    private void CheckData()
    {
        // get baking resources
        if (_terrainBakerShader == null)
            _terrainBakerShader = Shader.Find("hickv/hvTerrainBaker");
        if (_terrainBakerMaterial == null)
            _terrainBakerMaterial = new Material(_terrainBakerShader);

        // find terrain component
        _terrain = transform.GetComponent<Terrain>();
    }

    [ContextMenu("SwitchMaterial")]
    private void SwitchMaterial()
    {
        if (_terrain.materialTemplate == _hvTerrainMaterial)
        {
            _terrain.materialTemplate = _unityTerrainMaterial;
        }
        else
        {
            UpdateHVTerrainMaterial();
            _terrain.materialTemplate = _hvTerrainMaterial;
        }
    }

    [ContextMenu("BakeHVTerrain")]
    private void BakeHVTerrain()
    {
        CheckData();

        // get terrain data
        Texture2D[] alphaMaps = _terrain.terrainData.alphamapTextures;
        TerrainLayer[] terrainLayers = _terrain.terrainData.terrainLayers;

        // res
        int mapsResolution = _terrain.terrainData.heightmapResolution - 1;

        // create blendTextures
        {
            _blendTexture0 = new Texture2D(mapsResolution, mapsResolution, TextureFormat.RGBA32, false, true);
            _blendTexture0.name = "TerrainBlendMap0";

            _blendTexture1 = new Texture2D(mapsResolution, mapsResolution, TextureFormat.RGBA64, true, true);
            _blendTexture1.name = "TerrainBlendMap1";
        }

        // create blendMap0 + blendMap1
        {
            // bake splatmaps to a heightmapResolution sized tex
            NativeList<Color32> alphaMapsPixels = new NativeList<Color32>(Allocator.TempJob);
            for (int i = 0; i < alphaMaps.Length; i++)
            {
                RenderTexture tempSplatMapRT = RenderTexture.GetTemporary(mapsResolution, mapsResolution, 0, RenderTextureFormat.ARGB32);
                tempSplatMapRT.filterMode = FilterMode.Bilinear;
                tempSplatMapRT.Create();

                // blit
                Graphics.Blit(alphaMaps[i], tempSplatMapRT);

                // build texture2D (to get Color[])
                RenderTexture.active = tempSplatMapRT;
                Texture2D splatMapTempTex = new Texture2D(mapsResolution, mapsResolution, TextureFormat.RGBA32, false, true);
                splatMapTempTex.ReadPixels(new Rect(0f, 0f, tempSplatMapRT.width, tempSplatMapRT.height), 0, 0, false);
                splatMapTempTex.Apply(false);

                NativeArray<Color32> pixels = splatMapTempTex.GetPixelData<Color32>(0);
                alphaMapsPixels.AddRange(pixels);
                pixels.Dispose();

                Object.DestroyImmediate(splatMapTempTex);
                RenderTexture.ReleaseTemporary(tempSplatMapRT);
            }

            // bake blendMap1
            RenderTexture tempBlend1RT = RenderTexture.GetTemporary(mapsResolution, mapsResolution, 0, RenderTextureFormat.ARGBFloat);
            tempBlend1RT = RenderTexture.GetTemporary(mapsResolution, mapsResolution, 0, RenderTextureFormat.ARGBFloat);
            tempBlend1RT.filterMode = FilterMode.Bilinear;
            tempBlend1RT.Create();

            // render buffers
            _terrain.drawInstanced = true; // to get normalTexture
            Graphics.Blit(_terrain.normalmapTexture, tempBlend1RT);
            _terrain.drawInstanced = false; // to get normalTexture

            // build texture2D (to get Color[])
            RenderTexture.active = tempBlend1RT;
            _blendTexture1.ReadPixels(new Rect(0f, 0f, tempBlend1RT.width, tempBlend1RT.height), 0, 0, false);
            _blendTexture1.Apply(false);

            // get blendMap1 arrays
            NativeArray<Color> blendMap1ColorArray = new NativeArray<Color>(_blendTexture1.GetPixels(0), Allocator.TempJob);

            // build blend maps using splatMap data
            BuildBlendMapJob smsJob = new BuildBlendMapJob()
            {
                inAlphaMaps = alphaMapsPixels,
                inoutBlendMap1 = blendMap1ColorArray,
                inoutBlendMap0 = new NativeArray<Color32>(mapsResolution * mapsResolution, Allocator.TempJob, NativeArrayOptions.ClearMemory),
                resolution = mapsResolution,
                splatCount = alphaMaps.Length,
            };
            smsJob.Schedule().Complete();

            // assign data
            _blendTexture0.SetPixelData<Color32>(smsJob.inoutBlendMap0.ToArray(), 0);
            _blendTexture0.Apply();

            _blendTexture1.SetPixels(smsJob.inoutBlendMap1.ToArray(), 0);
            _blendTexture1.Apply();

            alphaMapsPixels.Dispose();
            smsJob.inoutBlendMap1.Dispose();
            smsJob.inoutBlendMap0.Dispose();
            RenderTexture.ReleaseTemporary(tempBlend1RT);
        }

        // create textureArrays
        {
            const int mipCount = 10;
            _albedoTextureArray = new Texture2DArray((int)_albedoTextureArrayResolution, (int)_albedoTextureArrayResolution, terrainLayers.Length, TextureFormat.RGBA32, mipCount, false);
            _normalTextureArray = new Texture2DArray((int)_normalTextureArrayResolution, (int)_normalTextureArrayResolution, terrainLayers.Length, TextureFormat.RG16, mipCount, false);

            int slice = 0;
            for (int i = 0; i < terrainLayers.Length; i++)
            {
                TerrainLayer terrainLayer = terrainLayers[i];

                // get buffers
                RenderTexture albedoTempRT = RenderTexture.GetTemporary((int)_albedoTextureArrayResolution, (int)_albedoTextureArrayResolution, 0, RenderTextureFormat.ARGB32);
                albedoTempRT.filterMode = FilterMode.Bilinear;
                albedoTempRT.Create();

                RenderTexture normalTempRT = RenderTexture.GetTemporary((int)_normalTextureArrayResolution, (int)_normalTextureArrayResolution, 0, RenderTextureFormat.RG16);
                normalTempRT.filterMode = FilterMode.Bilinear;
                normalTempRT.Create();

                // render buffers
                Graphics.SetRenderTarget(albedoTempRT);
                _terrainBakerMaterial.SetTexture("_AlbedoMap", terrainLayer.diffuseTexture);
                _terrainBakerMaterial.SetFloat("_Smoothness", GraphicsFormatUtility.HasAlphaChannel(terrainLayer.diffuseTexture.graphicsFormat) ? 1 : terrainLayer.smoothness);
                _terrainBakerMaterial.SetPass(0);
                Graphics.Blit(null, albedoTempRT, _terrainBakerMaterial, 0);

                Graphics.SetRenderTarget(normalTempRT);
                _terrainBakerMaterial.SetTexture("_NormalMap", terrainLayer.normalMapTexture);
                _terrainBakerMaterial.SetFloat("_NormalScale", terrainLayer.normalScale);
                _terrainBakerMaterial.SetPass(1);
                Graphics.Blit(null, normalTempRT, _terrainBakerMaterial, 1);

                // build texture2Ds
                Texture2D albedoTempTexture = new Texture2D(albedoTempRT.width, albedoTempRT.height, TextureFormat.RGBA32, true);
                RenderTexture.active = albedoTempRT;
                albedoTempTexture.ReadPixels(new Rect(0f, 0f, albedoTempRT.width, albedoTempRT.height), 0, 0, true);
                albedoTempTexture.Apply();

                Texture2D normalTempTexture = new Texture2D(normalTempRT.width, normalTempRT.height, TextureFormat.RG16, true);
                RenderTexture.active = normalTempRT;
                normalTempTexture.ReadPixels(new Rect(0f, 0f, normalTempRT.width, normalTempRT.height), 0, 0, true);
                normalTempTexture.Apply();

                // set textureArray data
                for (int j = 0; j < mipCount; j++)
                {
                    Color[] pixels = albedoTempTexture.GetPixels(j);
                    _albedoTextureArray.SetPixels(pixels, slice, j);
                }

                for (int j = 0; j < mipCount; j++)
                {
                    Color[] pixels = normalTempTexture.GetPixels(j);
                    _normalTextureArray.SetPixels(pixels, slice, j);
                }

                slice++;

                RenderTexture.ReleaseTemporary(albedoTempRT);
                RenderTexture.ReleaseTemporary(normalTempRT);
                Object.DestroyImmediate(albedoTempTexture);
                Object.DestroyImmediate(normalTempTexture);
            }
            _albedoTextureArray.Apply();
            _normalTextureArray.Apply();
        }

        // create assets
        {
            // get path
            string assetPath = UnityEditor.EditorUtility.SaveFilePanelInProject("Save Terrain Textures", "TerrainTextures", "", "Select textures save location");
            string directoryPath = Path.GetDirectoryName(assetPath);

            // save textures
            {
                // blendMap0
                string blendMap0Path = WriteTexture(_blendTexture0, directoryPath);
                TextureImporter blendMapImporter = AssetImporter.GetAtPath(blendMap0Path) as TextureImporter;
                if (blendMapImporter != null)
                {
                    blendMapImporter.textureType = TextureImporterType.Default;
                    blendMapImporter.maxTextureSize = _blendTexture0.width;
                    blendMapImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                    blendMapImporter.alphaIsTransparency = false;
                    blendMapImporter.mipmapEnabled = false;
                    blendMapImporter.sRGBTexture = false;
                    blendMapImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    blendMapImporter.SaveAndReimport();
                }
                _blendTexture0 = AssetDatabase.LoadAssetAtPath<Texture2D>(blendMap0Path);

                // blendMap1
                string blendMap1Path = WriteTexture(_blendTexture1, directoryPath);
                blendMapImporter = AssetImporter.GetAtPath(blendMap1Path) as TextureImporter;
                if (blendMapImporter != null)
                {
                    blendMapImporter.textureType = TextureImporterType.Default;
                    blendMapImporter.maxTextureSize = _blendTexture0.width;
                    blendMapImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                    blendMapImporter.alphaIsTransparency = false;
                    blendMapImporter.mipmapEnabled = false;
                    blendMapImporter.sRGBTexture = false;
                    blendMapImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    blendMapImporter.SaveAndReimport();
                }
                _blendTexture1 = AssetDatabase.LoadAssetAtPath<Texture2D>(blendMap1Path);

                // textureArrays
                {
                    // albedo
                    Texture2DArray albedoTexArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(directoryPath + "/" + "TerrainAlbedoMaps" + ".asset");
                    if (albedoTexArray != null)
                    {
                        Object.DestroyImmediate(albedoTexArray, true);
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                    }
                    AssetDatabase.CreateAsset(_albedoTextureArray, directoryPath + "/" + "TerrainAlbedoMaps" + ".asset");
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    _albedoTextureArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(directoryPath + "/" + "TerrainAlbedoMaps" + ".asset");

                    // normal
                    Texture2DArray normalTexArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(directoryPath + "/" + "TerrainNormalMaps" + ".asset");
                    if (normalTexArray != null)
                    {
                        Object.DestroyImmediate(normalTexArray, true);
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                    }
                    AssetDatabase.CreateAsset(_normalTextureArray, directoryPath + "/" + "TerrainNormalMaps" + ".asset");
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    _normalTextureArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(directoryPath + "/" + "TerrainNormalMaps" + ".asset");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        UpdateHVTerrainMaterial();
    }

    string WriteTexture(Texture2D tex, string path)
    {
        byte[] bytes = tex.EncodeToPNG();

        string fullPath = path + "/" + tex.name + ".png";
        File.WriteAllBytes(fullPath, bytes);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return fullPath;
    }
#endif
}