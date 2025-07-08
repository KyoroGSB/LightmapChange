using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

public class LightmapDataController : ControllerBase
{
    private static string dayTime = "_Light";
    private static string night = "_OffLight";

    private Dictionary<string, LightMapAssetsScriptable> _allLightmapsData = new Dictionary<string, LightMapAssetsScriptable>();
    public Dictionary<string, LightMapAssetsScriptable> LightmapsData { get { return _allLightmapsData; } }

    public  Dictionary<string, LightMapAssetsScriptable> currentSceneLightmaps = new Dictionary<string, LightMapAssetsScriptable>();

    protected override void Initialize()
    {
        //Debug.LogError("讀取LightMap...");

        //List<LightMapAssetsScriptable> tmp = new List<LightMapAssetsScriptable>();
        //Task loadAssets = LoadLightMapAssets();

        //loadAssets.ContinueWith(t =>
        //{
        //    if (t.IsFaulted)
        //    {
        //        Debug.LogException(t.Exception);
        //    }
        //    Debug.LogError("完成讀取所有LightMap");
        //});
    }
    //讀取所有的LightMapAssets (頭盔負荷不了)
    private async Task LoadLightMapAssets()
    {
        List<LightMapAssetsScriptable> tmp = new List<LightMapAssetsScriptable>();

        AsyncOperationHandle<IList<LightMapAssetsScriptable>> handle =
            Addressables.LoadAssetsAsync<LightMapAssetsScriptable>("LightmapDataAsset", null);

        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            foreach (var lightAsset in handle.Result)
            {
                _allLightmapsData.Add(lightAsset.name, lightAsset);
                Debug.LogError($"完成讀取{lightAsset.name}");
            }
        }
    }
    /// <summary>
    /// 讀取特定場景的開關燈LightMap(2個)
    /// </summary>
    /// <param name="SceneName"></param>
    public void LoadSceneLightMap(string sceneName)
    {
        if (currentSceneLightmaps.TryGetValue(sceneName + dayTime, out LightMapAssetsScriptable lightmapData))
        {
            Debug.Log($"已有{sceneName + dayTime}");
        }
        else 
        {
            Addressables.LoadAssetAsync<LightMapAssetsScriptable>(sceneName + dayTime).Completed += AddToData;
        }
        if (currentSceneLightmaps.TryGetValue(sceneName + night, out LightMapAssetsScriptable lightmapData_Off))
        {
            Debug.Log($"已有{sceneName + night}");
        }
        else 
        {
            Addressables.LoadAssetAsync<LightMapAssetsScriptable>(sceneName + night).Completed += AddToData;
        }
    }
    private void AddToData(AsyncOperationHandle <LightMapAssetsScriptable> handle)
    {
        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            currentSceneLightmaps.Add(handle.Result.name, handle.Result);
        }
        //Debug.LogError($"完成讀取{handle.Result.name}");
    }
    public void ReleaseCurrrentSceneLightMap()
    {
        currentSceneLightmaps.Clear();
    }
    /// <summary>
    /// 依據場景跟開關燈來切換Lightmap
    /// </summary>
    /// <param name="scene">場景名稱</param>
    /// <param name="lighton">是否開燈</param>
    public void ChangeLightMap(bool lighton, string sceneName)
    {
        string time = lighton ? dayTime : night;
        if (currentSceneLightmaps.TryGetValue(sceneName + time, out LightMapAssetsScriptable lightmapData))
        {
            LoadLightingData(lightmapData);
        }
        else 
        {
            Debug.LogError($"讀取不到{sceneName}光照資訊");
        }
    }

    private void LoadLightingData(LightMapAssetsScriptable data)
    {
        LightmapSettings.lightmaps = null;

        //取得儲存之LightMap資訊
        var newLightmaps = LoadLightmaps(data);
        //替換所有renderObject的光照資訊
        ApplyRendererInfo(data.rendererInfos);

        LightmapSettings.lightmaps = newLightmaps;

        //更換ReflectionProbe的材質
        ReflectionProbe[] Rp = (ReflectionProbe[])FindObjectsOfType<ReflectionProbe>();
        for (int i = 0; i < data.reflectionTexture.Length; i++)
        {
            if (Rp[i].mode == ReflectionProbeMode.Realtime) continue;
            //Rp[i].customBakedTexture = data.reflectionTexture[i];
            Rp[i].bakedTexture = data.reflectionTexture[i];
            Rp[i].RenderProbe();
        }
        //更換LightProbe的光照資訊
        LoadLightProbes(data);
    }
    LightmapData[] LoadLightmaps(LightMapAssetsScriptable data) //Load & setting lightmaps then return LightmapData[]
    {
        if (data.lightmapColor == null || data.lightmapColor.Length == 0)
            return null;

        var newLightmaps = new LightmapData[data.lightmapColor.Length];

        for (int i = 0; i < newLightmaps.Length; i++)
        {
            newLightmaps[i] = new LightmapData();
            newLightmaps[i].lightmapColor = data.lightmapColor[i];

            if (data.lightmapsMode != LightmapsMode.NonDirectional)
            {
                newLightmaps[i].lightmapDir = data.lightmapDir[i];
            }
            if (data.shadowMask.Length > 0)
            {
                newLightmaps[i].shadowMask = data.shadowMask[i];
            }
        }

        return newLightmaps;
    }
    public void LoadLightProbes(LightMapAssetsScriptable data) //Change LightProbes' data
    {

        SphericalHarmonicsL2[] newSH;        
        try
        {
            // try-catch to handle bug in older unity versions
            if (data.lightProbes.Length > 0)
            {
                newSH = new SphericalHarmonicsL2[data.lightProbes.Length];
                newSH = data.lightProbes;
                LightProbes.Tetrahedralize();
                LightmapSettings.lightProbes.bakedProbes = newSH;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }

    }

    public void ApplyRendererInfo(RendererInfo[] infos)//Update all rendering objects for the new Lighting setting
    {
        try
        {
            var hashRendererPairs = new Dictionary<int, RendererInfo>();

            //Fill with lighting scenario to load renderer infos
            foreach (var info in infos)
            {
                hashRendererPairs.Add(info.transformHash, info);
            }

            //Find all renderers
            var renderers = (MeshRenderer[])FindObjectsOfType<MeshRenderer>();
            //Apply stored scale and offset if transform and mesh hashes match
            foreach (var render in renderers)
            {
                var infoToApply = new RendererInfo();

                if (hashRendererPairs.TryGetValue(GetStableHash(render.gameObject.transform), out infoToApply))
                {
                    if (render.gameObject.name == infoToApply.name)
                    {
                        //Change light map for the rendering object
                        render.lightmapIndex = infoToApply.lightmapIndex;
                        //Change the Offset of the lightmap if in need
                        if (!render.isPartOfStaticBatch)
                        {
                            render.lightmapScaleOffset = infoToApply.lightmapOffsetScale;
                        }
                    }
                }
            }
        //TODO: Fin better solution for terrain.This is not compatible with several terrains.
        }
        catch (Exception e)
        {
            Debug.LogError("Error in ApplyRendererInfo:" + e.GetType().ToString());
        }
    }

    public static int GetStableHash(Transform transform)
    {
        int nameHash = transform.gameObject.name.GetHashCode();
        Vector3 stablePos = new Vector3(LimitDecimals(transform.position.x, 2), LimitDecimals(transform.position.y, 2), LimitDecimals(transform.position.z, 2));
        Vector3 stableRot = new Vector3(LimitDecimals(transform.rotation.x, 1), LimitDecimals(transform.rotation.y, 1), LimitDecimals(transform.rotation.z, 1));
        return nameHash + stablePos.GetHashCode() + stableRot.GetHashCode();
    }
    static float LimitDecimals(float input, int decimalcount)
    {
        var multiplier = Mathf.Pow(10, decimalcount);
        return Mathf.Floor(input * multiplier) / multiplier;
    }
}
