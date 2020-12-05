using System.Collections.Generic;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Debug = EditorDebug;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System;

public class Scene3DBundleTool : AssetBase
{
    public class FBXBundleSetting
    {
        public bool normal;
        public bool trangent;
    }

    private enum MeshType
    {
        Common = 0,
        Color = 1,
        Normal = 2,
        Tangent = 3,
        NoCombine = 6
    }

    public override string Type { get { return "scene3d"; } }
    public override List<string> SrcDir { get; set; }                    // 实例化时保存场景资源及场景使用资源路径
    public override List<string> DstDir { get; set; }

    /// <summary>
    /// Application.dataPath + "/../abNameForScene.json"
    /// </summary>
    public static string abNameJsonPath = Application.dataPath + "/../abNameForScene.json";
    public static Dictionary<string, bool> currentSceneABNameDict;
    /// <summary>
    /// 场景资源json文件中的数据
    /// </summary>
    static Dictionary<string, List<string>> mAbNameDict;       

    public static List<string> notNeedBakeSceneList = new List<string>() { };
    private static string Scene3DRootDir = "Assets/_Resource/map/Scene";
    private static List<string> mSceneUseMaterialList = new List<string>(100);                                // 场景使用材质列表
    private static Dictionary<Material, bool> mClearedKeyWordMat = new Dictionary<Material, bool>(100);      
    private static List<string> mTempFiles = new List<string>(100);

    public override void Init()
    {
        SrcDir = new List<string> { Scene3DRootDir, "Assets/Plugins/Scene", "Assets/_Resource/Effect", "Assets/_Resource/ShaderNotPackage", "Assets/_Resource/Shader", "Assets/_Resource/map", "Assets/_Resource/Textures/Lut" };
        DstDir = new List<string> { };
    }

    public override bool Build(BuildTarget platform)
    {
        bool success = true;
        if (this.m_ForceClearOldBundle)                 // 如果需要强制清除旧的场景
        {
            success = this.Clear3DSceneBundles();       // 用来删除AB包中有但项目场景没有的资源
        }
        else
        {
            AssetBuildTool.LogTime("Build Scene Data Start");
            if (!BuildData(platform))                      // 创建场景配置数据信息bundle
            {
                Debug.LogThrowError("BuildData Error");
                return false;
            }
            AssetBuildTool.LogTime("Build Scene Data End");

            AssetBuildTool.CheckShaderPackage();              // 检查更新shader包

            if (!BuildSceneBundle(platform))                  // 创建场景bundle包
            {
                Debug.LogThrowError("BuildSceneBundle Error");
                return false;
            }
            if (!GenerateAllSceneEffectConfig())              // 生成所有场景特效配置文件
            {
                Debug.LogThrowError("GenerateAllSceneEffectConfig Error");
                return false;
            }
        }
        
        SceneCommonResAnalyze.CheckScene(true, ResCommon.GetOsType());    // 将ab包中的场景和所有依赖资源信息资源写入场景资源信息文件中
        ClearAllBundleName();                                             // 将创建过程中，临时的AssetBundle资源清除
        Debug.Log("Qdazzle_Build_Success");
        return success;
    }

    public static void RebuildSceneShader()
    {
        ShaderVariantsCollectionsTool.ClearSceneShaderPassTypeMap();
        ShaderVariantsCollectionsTool.ClearShaderKeywordMap();
        ClearClearedKeyWordMat();  
        BuildShaderVariantsCollection();
    }

    public static void PreBuildTypeShader(string types)
    {
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/_Resource/map/AllMats/" + types + "_Scene.unity");
        Debug.Log("开始清除所有AsssetBundleName");
        string[] names = AssetDatabase.GetAllAssetBundleNames();
        string abname = string.Empty;
        float count = 0f;
        float totalCnt = names.Length;
        foreach (var name in names)
        {
            ++count;
            AssetDatabase.RemoveAssetBundleName(name, true);
        }

        GameObject t = new GameObject("ShaderRoot");
        t.transform.localPosition = new Vector3(20000, 20000, 20000);

        string[] matFiles = FileHelper.FindFileBySuffix("Assets/_Resource/map/AllMats/Bake", ".mat");
        foreach (var p in matFiles)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(p);

            //mat.shader
            if (Path.GetDirectoryName(p).EndsWith(types))
            {
                mat.EnableKeyword("_QFOG");
                mat.EnableKeyword("_SHADOWMASK_INALPHA");
                ClearMatTexture(mat);
                GameObject mt = GameObject.CreatePrimitive(PrimitiveType.Quad);
                MeshCollider mc = mt.GetComponent<MeshCollider>();
                if (mc != null)
                {
                    Object.DestroyImmediate(mc);
                }
                mt.name = mat.name.Replace("(Clone)", "");
                //Debug.Log(mt.name);
                mt.transform.parent = t.transform;
                mt.transform.localPosition = Vector3.zero;
                MeshRenderer mr = mt.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;

                abname = string.Format("../shader_{0}.unity3d", types.ToLower());
                SetAssetImporterAssetsBundleName(p, abname, false);
                abname = string.Format("../shader_{0}.unity3d", types.ToLower());
                SetAssetImporterAssetsBundleName(AssetDatabase.GetAssetPath(mat.shader), abname, false);
            }
        }

        string prefabPath = string.Format("Assets/_Resource/map/AllMats/ShaderRoot{0}.prefab", types);

        if (File.Exists(prefabPath))
        {
            File.Delete(prefabPath);
        }

        PrefabUtility.CreatePrefab(prefabPath, t);
        SaveAndRefreshAssets();
        GameObject.DestroyImmediate(t);

        abname = string.Format("../shader_{0}.unity3d", types.ToLower());
        SetAssetImporterAssetsBundleName(prefabPath, abname, false);

        string outputPath = Application.dataPath + "/../" + UnityInterfaceAdapter.GetStreamingAssets() + "/assetbundle/" + AssetBuildTool.OsType + "/map/ShaderBuildTemp/";
        Debug.Log(outputPath);
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        BuildPipeline.BuildAssetBundles(outputPath, BuildAssetBundleOptions.ForceRebuildAssetBundle, EditorUserBuildSettings.activeBuildTarget);
    }

    public static void ClearAllBundleName()
    {
        TextureImportSetting.SetIgnoreImportSetting(true);
        Debug.Log("开始清除所有AsssetBundleName");
        string[] names = AssetDatabase.GetAllAssetBundleNames();
        float totalCnt = names.Length;
        foreach (var name in names)
        {
            AssetDatabase.RemoveAssetBundleName(name, true);
        }
        Debug.LogFormat("结束清除所有AsssetBundleName, bundleNameCount = {0}", totalCnt);
        TextureImportSetting.SetIgnoreImportSetting(false);
    }

    public static void Build3DScene()
    {
        Debug.Log("开始打包所有3D场景");
        if (AssetBuildTool.BuildBundle(AssetBuildTool.OsType, "scene3d", "", false))
        {
            Debug.Log("BUILD SUCCESS");
        }
        Debug.Log("结束打包所有3D场景");
    }

    [MenuItem(MenuNameConfig.PackCurrenScene, false, MenuNameConfig.QAssetMenuPriority)]
    public static void BuildCurrentOpen3DScene()
    {
        Debug.Log("开始打包当前3D场景");

        Scene3DBundleTool s = new Scene3DBundleTool();
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene == null)
            return;

        luaPath = EditorPrefs.GetString("luaPath");
        string path = scene.path;
        if (string.IsNullOrEmpty(path))
            return;
        ClearClearedKeyWordMat();   
        mTempFiles.Clear(); 
        currentSceneABNameDict = new Dictionary<string, bool>();
        s.DoScene(path, BuildAssetBundleOptions.ForceRebuildAssetBundle, false);
        Debug.Log("结束打包当前3D场景");

        ShaderVariantsCollectionsTool.ClearSceneShaderPassTypeMap();
        ShaderVariantsCollectionsTool.ClearShaderKeywordMap();
        ClearClearedKeyWordMat(); 
        BuildShaderVariantsCollection();
        DeleteNoUseFile();
    }

    /// <summary>
    /// 检测场景的反射探头模式是否为runtime，为runtime则返回false
    /// </summary>
    /// <returns>检测结果</returns>
    public static bool CheckReflectionProbesInScene(Scene s)
    {
        const int maxResolution = 256;//反射探头允许的最大分辨率(需为16~2048内，2的n次方)
        const int maxRbNum = 10;//场景里允许存在的反射探头的最大数量
        bool result = true;//返回结果
        Scene originalScene = EditorSceneManager.GetActiveScene();
        try
        {
            if (originalScene != s)
            {
                Debug.Log("要检测的场景 " + s.name + " 未打开。正在打开此场景。");
                EditorSceneManager.OpenScene(s.path);//要检测的场景不是当前打开的场景，把要检测的场景打开
            }
        }
        catch(Exception e)
        {
            Debug.LogError("场景 " + s.name + " 打开失败，无法进行反射探头检测。报错：" + e);
            return false;
        }
        //查找所有反射探头组件
        ReflectionProbe[] reflectionProbes = GameObject.FindObjectsOfType<ReflectionProbe>();
        if (reflectionProbes.Length > maxRbNum)
        {
            Debug.LogError("场景 " + s.name + " 中反射探头的数量超出限制的 " + maxRbNum + " 个。");
            return false;
        }
        if (reflectionProbes.Length == 0)
        {
            Debug.Log("场景 " + s.name + " 中没有找到反射探头。（请检查带有反射探头的GameObject的激活状态）");
        }
        foreach(ReflectionProbe rb in reflectionProbes)
        {
            if (!rb.enabled)
            {
                Debug.Log("场景 " + s.name + " 的反射探头 " + rb.name + " 被禁用，不检测。");
                continue;
            }
            if (rb.resolution > maxResolution)
            {
                rb.resolution = maxResolution;
                Debug.Log("场景 " + s.name + " 的反射探头 " + rb.name + " 分辨率过大，已更改为 " + maxResolution + " 。");
            }
            if (rb.mode == ReflectionProbeMode.Realtime)
            {
                Debug.LogError("场景 " + s.name + " 的反射探头 " + rb.name + " 的模式为Realtime，性能消耗大，请修改。");
                result = false;
            }
            else Debug.Log("场景 " + s.name + " 的反射探头 " + rb.name + " 已通过检测。");
        }

        try
        {
            if (originalScene != s) EditorSceneManager.OpenScene(originalScene.path);//把检测前的场景开回来
        }
        catch(Exception e)
        {
            Debug.LogWarning("未能恢复检测前的场景。报错：" + e);
        }

        Debug.Log("场景 " + s.name + " 的反射探头检测已完成。结果：" + result);
        return result;
    }

    public static bool GenerateAllSceneEffectConfig()
    {
        luaPath = CommandLineTool.GetUnityParameter("--lua_path=");
        foreach (var s in Directory.GetDirectories("Assets/_Resource/map/Scene"))
        {
            StringBuilder effectInfoLua = new StringBuilder();
            string sn = Path.GetFileNameWithoutExtension(s);
            if (File.Exists(s + "/Effect.prefab"))
            {
                Debug.Log("开始生成场景[" + sn + "] 的配置");
                GameObject effectRoot = GameObject.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(s + "/Effect.prefab"));          
                effectInfoLua.AppendLine("local Config = { }");
                effectInfoLua.AppendLine("Config.SceneEffect = { }");
                bool allFxExists = true;
                if (effectRoot != null)
                {
                    int index = 1;
                    effectInfoLua.AppendLine(string.Format("Config.SceneEffect[{0}] = {{ ", sn));

                    int level = 0;
                    foreach (var n in LevelNames)
                    {
                        Transform node = effectRoot.transform.Find(n);
                        if (node != null)
                        {
                            for (int j = 0; j < node.childCount; ++j)
                            {
                                Transform effectNode = node.GetChild(j);
                                if (!File.Exists(@"Assets\_Resource\Effect\SceneEffect\" + effectNode.name + ".prefab"))
                                {
                                    Debug.LogError("场景特效：" + effectNode.name + "不存在");
                                    allFxExists = false;
                                }

                                int aoiType = effectNode.gameObject.layer == LayerMask.NameToLayer("Far") ? 1 : 0;
                                effectInfoLua.AppendLine(string.Format("[{0}] = {{ ", index++));
                                effectInfoLua.AppendLine(string.Format("    name = \"{0}\",", effectNode.name));
                                effectInfoLua.AppendLine(string.Format("    hashkey = {0},", effectNode.GetHashCode()));
                                effectInfoLua.AppendLine(string.Format("    level = {0},", level));
                                effectInfoLua.AppendLine(string.Format("    aoiType = {0},", aoiType));
                                effectInfoLua.AppendLine(string.Format("    position = {{ x = {0}, y = {1}, z = {2} }},", effectNode.position.x, effectNode.position.y, effectNode.position.z));
                                effectInfoLua.AppendLine(string.Format("    rotation = {{ x = {0}, y = {1}, z = {2}, w = {3} }},", effectNode.rotation.x, effectNode.rotation.y, effectNode.rotation.z, effectNode.rotation.w));
                                effectInfoLua.AppendLine(string.Format("    scale = {{ x = {0}, y = {1}, z = {2} }},", effectNode.localScale.x, effectNode.localScale.y, effectNode.localScale.z));
                                effectInfoLua.AppendLine("},");

                            }
                        }
                        level++;
                    }
                }

                GameObject.DestroyImmediate(effectRoot);

                if (!allFxExists)
                {
                    Debug.LogErrorFormat("生成场景 {0} 特效配置失败，联系特效", sn);
                    return false;
                }

                effectInfoLua.AppendLine("} ");
                effectInfoLua.AppendLine(" ");
                effectInfoLua.AppendLine("return Config");         
            }
            else
            {
                Debug.Log("为场景加上空的配置文件" + sn);
                effectInfoLua.AppendLine("local Config = { }");
                effectInfoLua.AppendLine("Config.SceneEffect = { }");
                effectInfoLua.AppendLine(string.Format("Config.SceneEffect[{0}] = {{ ", sn));
                effectInfoLua.AppendLine("} ");
                effectInfoLua.AppendLine(" ");
                effectInfoLua.AppendLine("return Config");
            }

            // 生成特效配置文件
            string configDir = luaPath + "/config/sceneinfo/effects";

            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            string configPath = configDir + string.Format("/effect_info_{0}.lua", sn);

            try
            {
                FileStream fs = new FileStream(configPath, FileMode.Create);
                StreamWriter sw = new StreamWriter(fs);
                sw.Write(effectInfoLua.ToString());
                sw.Flush();
                sw.Close();
                fs.Close();

                Debug.Log("生成场景特效配置成功\n" + configPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError(string.Format("生成场景特效配置失败 file[{0}]   error[{1}]", configPath, ex.Message));
            }
        }

        return true;
    }

    public static void uploadBundleSize(string scene_id)
    {
        string root_path = Application.dataPath + "/../" + UnityInterfaceAdapter.GetStreamingAssets() + "/assetbundle/" + AssetBuildTool.OsType + "/map/" + scene_id + "/";
        string[] names = AssetDatabase.GetAllAssetBundleNames();
        string path = "";
        float allFile = 0;
        float common = 0;
        float self = 0;
        float allTexture = 0;
        float allMesh = 0;
        int bundleCount = 0;
        Debug.Log("Begin uploadBundleSize");
        foreach (var name in names)
        {
            path = root_path + name;
            if (File.Exists(path))
            {
                FileInfo finfo = new FileInfo(path);
                Debug.Log(name + "   size: " + finfo.Length / 1024);
                if (name.Contains("shader"))
                {
                    continue;
                }

                allFile += finfo.Length;
                if (name.Contains("/common/"))
                {
                    common += finfo.Length;
                }
                else
                {
                    self += finfo.Length;
                }

                if (name.StartsWith("texture") || name.StartsWith("mat"))
                {
                    allTexture += finfo.Length;
                }
                if (name.StartsWith("mesh"))
                {
                    allMesh += finfo.Length;
                }
                bundleCount++;
            }
            else
            {
                Debug.LogError("UploadBundleSize failed, bundle file no exist!! path =" + path);
            }
        }
        allFile = allFile / 1024 / 1024;
        common = common / 1024 / 1024;
        self = self / 1024 / 1024;
        allTexture = allTexture / 1024 / 1024;
        allMesh = allMesh / 1024 / 1024;

        Debug.Log("allFlie:" + allFile);
        Debug.Log("common:" + common);
        Debug.Log("self:" + self);
        Debug.Log("allTexture:" + allTexture);
        Debug.Log("allMesh:" + allMesh);
        Debug.Log("bundleCount:" + bundleCount);

        string timeStr = System.DateTime.Now.ToString("s");
        timeStr = timeStr.Replace("-", "");
        timeStr = timeStr.Replace(":", "");
        Debug.Log(string.Format("场景:[{0}] 系统:[{1}] 占用包体大小:[{2}] 私有:[{3}] 公共:[{4}]", scene_id, AssetBuildTool.OsType, self + common, self, common));
        WWWForm wwwfrom = new WWWForm();
        wwwfrom.AddField("scene", scene_id);
        wwwfrom.AddField("time", timeStr);
        wwwfrom.AddField("platform", AssetBuildTool.OsType);
        wwwfrom.AddField("allFile", allFile.ToString());
        wwwfrom.AddField("common", common.ToString());
        wwwfrom.AddField("self", self.ToString());
        wwwfrom.AddField("allTexture", allTexture.ToString());
        wwwfrom.AddField("allMesh", allMesh.ToString());
        wwwfrom.AddField("bundleCount", bundleCount.ToString());
        www = new WWW(BundleBuildConfig.SceneConfig.UploadProfilerUrl, wwwfrom);
        EditorApplication.update += update;
    }
    static WWW www;
    static void update()
    {
        if (www != null)
        {
            if (www.isDone)
            {
                Debug.Log("End uploadBundleSize");
                EditorApplication.update -= update;
            }
            else if (www.error != null)
            {
                Debug.LogError("uploadBundleSize failed : " + www.error);
            }
        }
    }

    public bool BuildData(BuildTarget platform)
    {
        string DataDir = "Assets/_Resource/map/Data";          // 场景数据
        string[] dataFilesPath = Directory.GetFiles(DataDir, "*.asset", SearchOption.AllDirectories);   // 获得 项目场景中所有数据
        // 打包目标路径StreamingAssets中的数据
        string DataBundleDir = Application.dataPath + "/../" + UnityInterfaceAdapter.GetStreamingAssets() + "/assetbundle/" + AssetBuildTool.OsType + "/map/Data";
        
        if (!Directory.Exists(DataBundleDir))
        {
            Directory.CreateDirectory(DataBundleDir);
        }
        string[] dataBundlesPath = Directory.GetFiles(DataBundleDir, "*.unity3d", SearchOption.AllDirectories);
        string[] dataFileNames = new string[dataFilesPath.Length];
        for (int i = 0; i < dataFilesPath.Length; i++)
        {
            dataFileNames[i] = Path.GetFileNameWithoutExtension(dataFilesPath[i]) + "Data";
        }
        foreach (var dataBundle in dataBundlesPath)
        {
            string dataBundleName = Path.GetFileNameWithoutExtension(dataBundle);
            bool isFound = false;
            for (int i = 0; i < dataFileNames.Length; i++)         
            {
                if (dataBundleName.Equals(dataFileNames[i]))
                {
                    isFound = true;
                    break;
                }
            }
            if (!isFound)
            {
                File.Delete(dataBundle);         // 删除打包路径中有且项目中没有的data资源
            }
        }

        foreach (var dataFile in dataFilesPath)     // 逐一对资源数据进行打包
        {
            Debug.Log("BuildData:" + dataFile);
            Object byteFileObj = AssetDatabase.LoadAssetAtPath(dataFile, typeof(Object));
            string outputPath = "/map/Data/" + byteFileObj.name + "Data.unity3d";
            bool haveBuilded;
            if (!BuildOne(new[] { dataFile }, outputPath, byteFileObj, null, platform, out haveBuilded))
            {
                Debug.LogError(string.Format("Build {0} error", outputPath));
                return false;
            }
        }
        return true;
    }

    public void BuildLut()
    {
        Debug.Log("Build Lut Texture begin!");   
        string[] files = FileHelper.FindFileBySuffix("Assets/_Resource/Textures/Lut", ".png");
        if (CheckVersion(files))
        {
            Debug.Log("没有版本变化，勿需重打！");
            return;
        }
        ClearAllBundleName();

        foreach (var f in files)
        {
            Debug.Log(f);
            string abname = "scene_lut.unity3d";
            SetAssetImporterAssetsBundleName(f, abname, false);
            TextureImporter ti = AssetImporter.GetAtPath(f) as TextureImporter;
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows || EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64)
            {
                TextureImporterPlatformSettings setting = ti.GetPlatformTextureSettings("Standalone");
                setting.textureCompression = TextureImporterCompression.Uncompressed;
                setting.format = TextureImporterFormat.RGB16;
                setting.overridden = true;
                ti.SetPlatformTextureSettings(setting);
            }
            else if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                TextureImporterPlatformSettings setting = ti.GetPlatformTextureSettings("Android");
                setting.textureCompression = TextureImporterCompression.Uncompressed;
                setting.format = TextureImporterFormat.RGB16;
                setting.overridden = true;
                ti.SetPlatformTextureSettings(setting);
            }
            else if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
            {
                TextureImporterPlatformSettings setting = ti.GetPlatformTextureSettings("iPhone");
                setting.textureCompression = TextureImporterCompression.Uncompressed;
                setting.format = TextureImporterFormat.RGB16;
                setting.overridden = true;
                ti.SetPlatformTextureSettings(setting);
            }

            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }

        string outputPath = Application.dataPath + "/../" + UnityInterfaceAdapter.GetStreamingAssets() + "/assetbundle/" + AssetBuildTool.OsType + "/map/";
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        BuildPipeline.BuildAssetBundles(outputPath, BuildAssetBundleOptions.ForceRebuildAssetBundle, EditorUserBuildSettings.activeBuildTarget);
        File.Delete(outputPath + "map");
        File.Delete(outputPath + "map.manifest");
        File.Delete(outputPath + "scene_lut.unity3d.manifest");
    }

    /// <summary>
    /// 打包场景文件
    /// </summary>
    /// <param name="platform"></param>
    /// <returns></returns>
    public bool BuildSceneBundle(BuildTarget platform)      // 将场景数据保存到Application.dataPath + "/../abNameForScene.json"文件中
    {
        BuildLut();  // 打包后期屏幕渲染特效
        Debug.Log("Build 3d Scene begin!");
        luaPath = CommandLineTool.GetUnityParameter("--lua_path=");
        mTempFiles.Clear();

        if (!File.Exists(abNameJsonPath))
        {
            StreamWriter writer = File.CreateText(abNameJsonPath);
            writer.WriteLine("{}");
            writer.Close();
        }

        if (mAbNameDict != null)
        {
            mAbNameDict.Clear();
        }

        try
        {
            System.IO.StreamReader abNameReader = new System.IO.StreamReader(abNameJsonPath);
            mAbNameDict = JsonHelper.ToObject<Dictionary<string, List<string>>>(abNameReader);
            abNameReader.Close();
        }
        catch (System.Exception e)
        {
            Debug.LogErrorFormat("Parse abNameForScene.json Failed, {0}", e);
            return false;
        }

        try
        {
            foreach (string scenePath in FileHelper.FindFileBySuffix(Scene3DRootDir, ".unity"))   //scene文件夹下的所有的场景文件名字
            {
                string scenePathDir = Path.GetDirectoryName(scenePath);
                string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                string shaderGCPath = scenePathDir + "/" + sceneName + "/" + "ShaderGlobalControl.prefab";
                if (!File.Exists(shaderGCPath))
                {   
                    Debug.LogErrorFormat("打包场景出错，场景{0}的 ShaderGlobalControl prefab 不存在. 把ShaderGlobalControl 放到这个路径 {1}", sceneName, shaderGCPath);
                    return false;
                }
                Debug.Log("scenePathDir:" + scenePathDir);
                Debug.Log("sceneName:" + sceneName);
                Debug.Log("shaderGCPath:" + shaderGCPath);
                ClearClearedKeyWordMat();  
                // fix me 打包要判断所有依赖文件的版本号
                string[] dps = new string[] { scenePath, shaderGCPath };
                if (!CheckVersionScene3D(dps))
                {
                    currentSceneABNameDict = new Dictionary<string, bool>();
                    string value = CommandLineTool.Command("svn revert -R " + (scenePathDir + "/../")).Output;
                    Debug.LogFormat("Build scene {0} need revert : \n{1}", sceneName, value);

                    if (!DoScene(scenePath, BuildAssetBundleOptions.ForceRebuildAssetBundle))
                    {
                        Debug.LogError(string.Format("Build scene error! url={0}", scenePath));
                        return false;
                    }
                    else
                    {
                        if (AssetBuildTool.OsType == "android" || AssetBuildTool.OsType == "ios")
                        {
                            uploadBundleSize(Path.GetFileNameWithoutExtension(scenePath));
                        }
                    }
                    if (mAbNameDict.ContainsKey(sceneName))
                    {
                        mAbNameDict.Remove(sceneName);
                    }
                    mAbNameDict.Add(sceneName, new List<string>(currentSceneABNameDict.Keys));
                }
                else
                {
                    Debug.Log(scenePath + "\t没有版本变化，勿需重打！");
                }
            }

            //RebuildSceneShader();
            ShaderVariantsCollectionsTool.ClearSceneShaderPassTypeMap();
            ShaderVariantsCollectionsTool.ClearShaderKeywordMap();
            ClearClearedKeyWordMat(); 
            BuildShaderVariantsCollection();
            DeleteNoUseFile();
            Debug.Log("Save assetbundleName to json :" + abNameJsonPath);
            StreamWriter writeJson = new StreamWriter(abNameJsonPath);
            writeJson.Write(JsonHelper.ToJson(mAbNameDict));
            writeJson.Close();

            Debug.Log("Build 3D Scene End!");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogErrorFormat("打包场景C#报错，联系引擎，error = {0}", e);
            return false;
        }
    }

    public static void DeleteNoUseFile()
    {
        Debug.Log("Begin DeleteNoUseFile");
        string prefixStr = Application.dataPath + "/../" + UnityInterfaceAdapter.GetStreamingAssets() + "/assetbundle/" + AssetBuildTool.OsType;
        string[] manifestpaths = FileHelper.FindFileBySuffix(prefixStr + "/map", ".manifest");
        foreach (var manifestpath in manifestpaths)
        {
            File.Delete(manifestpath);
        }
        for(int i=0; i<mTempFiles.Count; i++)
        {
            if (File.Exists(mTempFiles[i]))
            {
                File.Delete(mTempFiles[i]);
            }
        }
        mTempFiles.Clear();
        Debug.Log("End DeleteNoUseFile");
    }

    public string GetTransPath(Transform trans)
    {
        string ret = trans.name;

        Transform parent = trans.parent;
        while (parent != null)
        {
            ret = parent.name + "/" + ret;
            parent = parent.parent;
        }
        return ret;
    }

    private void ModifyBuildingBounds()
    {
        List<Transform> objs = new List<Transform>();
        GameObject low = GameObject.Find("SceneRoot/Low");
        GameObject mid = GameObject.Find("SceneRoot/Mid");
        GameObject high = GameObject.Find("SceneRoot/High");

        if (low)
        {
            foreach (Transform child in low.transform)
                objs.Add(child);
        };

        if (mid)
        {
            foreach (Transform child in mid.transform)
                objs.Add(child);
        }

        if (high)
        {
            foreach (Transform child in high.transform)
                objs.Add(child);
        }

        foreach (Transform rootNode in objs)
        {
            foreach (Transform building in rootNode)
            {
                var obj = building.gameObject;
                var mfs = obj.GetComponentsInChildren<MeshFilter>();
                if (mfs.Length >= 2)
                {
                    Bounds newB = mfs[0].sharedMesh.bounds;
                    for (int i = 1; i < mfs.Length; i++)
                        if (mfs[i].sharedMesh) newB.Encapsulate(mfs[i].sharedMesh.bounds);
                    foreach (var m in mfs)
                        if (m.sharedMesh) m.sharedMesh.bounds = newB;
                }
            }
        }

        AssetDatabase.SaveAssets();
    }

    private void DestroyDebugNode()
    {
        HDRHelper[] hdrHelpers = Resources.FindObjectsOfTypeAll<HDRHelper>();
        for (int i = 0; i < hdrHelpers.Length; ++i)
        {
            GameObject.DestroyImmediate(hdrHelpers[i]);
        }
        FlareLayer[] flares = Resources.FindObjectsOfTypeAll<FlareLayer>();
        for (int i = 0; i < flares.Length; ++i)
        {
            GameObject.DestroyImmediate(flares[i]);
        }
        Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
        for (int i = 0; i < cameras.Length; ++i)
        {
            GameObject.DestroyImmediate(cameras[i]);
        }
        GameObject effectObj = GameObject.Find("Effect");
        if (effectObj != null)
        {
            GameObject.DestroyImmediate(effectObj);
        }
    }

    private bool CheckUnreasonableUse(GameObject root)
    {
        bool success = true;
        Debug.Log("Begin CheckScene");
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (!CheckReflectionProbesInScene(scene))
        {
            success = false;
        }
        List<Renderer> errorRdList = new List<Renderer>();
        List<Renderer> errorShaderRenderList = new List<Renderer>();
        List<GameObject> noActiveGoList = new List<GameObject>();
        Transform[] trans = root.GetComponentsInChildren<Transform>(true);
        foreach (var g in trans)
        {
            if (!g.gameObject.activeInHierarchy)
            {
                //Debug.Log("删除未激活的物体" + g.name);
                noActiveGoList.Add(g.gameObject);
            }
        }
        foreach (var g in noActiveGoList)
        {
            if (g != null)
            {
                GameObject.DestroyImmediate(g);
            }
        }

        MeshFilter[] mfs = root.GetComponentsInChildren<MeshFilter>(true);
        foreach (var m in mfs)
        {
            if (m.sharedMesh == null)
            {
                //Debug.Log("删除mesh为空的组件[MeshFilter]" + m.name);
                Object.DestroyImmediate(m);
                continue;
            }
        }

        Collider[] cs = root.GetComponentsInChildren<Collider>(true);
        foreach (var c in cs)
        {
            if (!GetTransPath(c.transform).ToLower().Contains("sceneroot/collider")
                && !c.transform.GetComponent<Terrain>())
            {
                //Debug.Log("删除非Collider下的组件[Collider]" + c.name);
                Object.DestroyImmediate(c);
            }
        }

        Renderer[] rdList = root.GetComponentsInChildren<Renderer>(true);
        foreach (var rd in rdList)
        {
            if (!IsSceneBaked())
            {
                rd.receiveShadows = true;
                rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            else
            {
                rd.receiveShadows = false;
                rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            if (!rd.enabled)
            {
                Object.DestroyImmediate(rd);
            }
            else
            {
                Material mat = rd.sharedMaterial;
                if (mat == null || (rd.GetComponent<MeshFilter>() == null && rd.GetComponent<SkinnedMeshRenderer>() == null))
                {
                    //Debug.Log("物体[" + rd.gameObject.name + "]含有MeshRender组件却没有和MeshFilter配合使用或者材质球为空，将被删除");
                    errorRdList.Add(rd);
                }
                if (mat != null && mat.shader != null)
                {
                    if (!mat.shader.name.Contains("Q/Scene"))
                    {
                        errorShaderRenderList.Add(rd);
                    }
                    if (mat.shader.name == "Q/Scene/T4M")
                    {
                        mat.EnableKeyword("_SHADOW");
                    }
                    else if (mat.shader.name == "Q/Scene/SkyBox")
                    {
                        mat.renderQueue = 2999;
                    }
                }
            }
        }
        for (int i = errorShaderRenderList.Count - 1; i >= 0; i--)
        {
            Renderer rdr = errorShaderRenderList[i];
            string matname = "Unknown";
            if (rdr.sharedMaterial != null)
            {
                matname = rdr.sharedMaterial.name;
            }
            success = false;
            Debug.LogErrorFormat("场景{0} 的结点{1} 中使用的材质{2} 使用的shader的名字不包含 Q/Scene, 请检查使用是否合理", sceneName, rdr.gameObject.name, matname);
        }
        for (int i = errorRdList.Count - 1; i >= 0; i--)
        {
            if (errorRdList[i].gameObject != null)
            {
                Debug.Log("删除物体\t" + errorRdList[i].gameObject);
                Object.DestroyImmediate(errorRdList[i].gameObject);
            }
        }
        AssetDatabase.SaveAssets();

        Debug.Log("End CheckScene");
        return success;
    }

    private void SetFbxFormat()
    {
        Debug.Log("Begin SetFbxFormat");
        Dictionary<string, FBXBundleSetting> fbxSettingMap = new Dictionary<string, FBXBundleSetting>();
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();

        foreach (var root in roots)
        {
            // t16场景勾边，把平均法线写入切线数据
            if (ProjectSetting.IsProjectT16())
            {
                PlugTangentTools.WirteAverageNormalToTangentToos(root);
                continue;
            }
            MeshRenderer[] rds = root.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in rds)
            {
                if (r.GetComponent<MeshFilter>() == null)
                    continue;

                Material mat = r.sharedMaterial;
                Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
                if (mat != null && mesh != null)
                {
                    string mesh_path = AssetDatabase.GetAssetPath(mesh);
                    if (mat.shader.name == "Q/Scene/SpeedTree" || mat.shader.name == "Q/Scene/GrassWave")
                    {
                        if (!fbxSettingMap.ContainsKey(mesh_path))
                        {
                            FBXBundleSetting fs = new FBXBundleSetting();
                            fs.normal = true;
                            fs.trangent = false;
                            if (!IsSceneBaked())
                            {
                                fs.normal = true;
                                fs.trangent = true;
                            }
                            fbxSettingMap.Add(mesh_path, fs);
                        }
                    }
                    if (mat.shader.name == "Q/Scene/TextureNS" || mat.shader.name == "Q/Scene/T4M")
                    {
                        FBXBundleSetting fs = new FBXBundleSetting();
                        fs.normal = false;
                        fs.trangent = false;
                        if (mat.IsKeywordEnabled("_SPECULAR"))
                        {
                            fs.normal = true;
                        }
                        if (mat.IsKeywordEnabled("_NORMAL"))
                        {
                            fs.normal = true;
                            fs.trangent = true;
                        }
                        if (mat.IsKeywordEnabled("_REFLECT"))
                        {
                            fs.normal = true;
                        }

                        if (!IsSceneBaked())
                        {
                            fs.normal = true;
                            fs.trangent = true;
                        }
                        if (!fbxSettingMap.ContainsKey(mesh_path))
                        {
                            fbxSettingMap.Add(mesh_path, fs);
                        }

                        // t4m的控制贴图控制在4个像素控制一平米的地块
                        if (mat.HasProperty("_Control"))
                        {
                            Texture controlTex = mat.GetTexture("_Control");
                            if (controlTex != null)
                            {
                                mesh.RecalculateBounds();
                                Bounds bounds = mesh.bounds;
                                float width = bounds.extents.x;
                                float height = bounds.extents.y;
                                float square = width * height;

                                if (square < 5000)// 50 * 100 以内都用128×128
                                {
                                    texSize[AssetDatabase.GetAssetPath(controlTex)] = 128;
                                }
                                else if (square < 20000)// 100 * 200 以内都用256 ×256
                                {
                                    texSize[AssetDatabase.GetAssetPath(controlTex)] = 256;
                                }
                                else if (square < 80000)// 200 * 400 以内都用512 ×512
                                {
                                    texSize[AssetDatabase.GetAssetPath(controlTex)] = 512;
                                }
                            }
                        }
                    }
                    if (mat.shader.name == "Q/Scene/QWater")
                    {
                        if (!fbxSettingMap.ContainsKey(mesh_path))
                        {
                            FBXBundleSetting fs = new FBXBundleSetting();
                            fs.normal = true;
                            fs.trangent = true;
                            fbxSettingMap.Add(mesh_path, fs);
                        }
                    }
                    if (!fbxSettingMap.ContainsKey(mesh_path))
                    {
                        FBXBundleSetting fs = new FBXBundleSetting();
                        fs.normal = false;
                        fs.trangent = false;
                        fbxSettingMap.Add(mesh_path, fs);
                        continue;
                    }
                }
            }
        }

        foreach (var k_v in fbxSettingMap)
        {
            ModelImporter mi = AssetImporter.GetAtPath(k_v.Key) as ModelImporter;

            if (mi != null)
            {
                bool reimport = false;
                if (mi.importMaterials == true)
                {
                    mi.importMaterials = false;
                    reimport = true;
                    Debug.Log("Reimport fbx for importMaterials:" + k_v.Key);
                }

                // if (mi.meshCompression != ModelImporterMeshCompression.Low)
                // {
                //     if (!k_v.Key.ToLower().Contains("terrain"))
                //     {
                //         mi.meshCompression = ModelImporterMeshCompression.Low;
                //         reimport = true;
                //         Debug.Log("Reimport fbx for meshCompression:" + k_v.Key);
                //     }
                // }
                // if (!k_v.Value.normal && mi.importNormals != ModelImporterNormals.None)
                // {
                //     mi.importNormals = ModelImporterNormals.None;
                //     reimport = true;
                // }
                if (!k_v.Value.trangent && mi.importTangents != ModelImporterTangents.None)
                {
                    mi.importTangents = ModelImporterTangents.None;
                    reimport = true;
                    Debug.Log("Reimport fbx for importTangentsNone:" + k_v.Key);
                }
                else if (mi.importTangents != ModelImporterTangents.CalculateMikk)
                {
                    mi.importTangents = ModelImporterTangents.CalculateMikk;
                    reimport = true;
                    Debug.Log("Reimport fbx for importTangentsCalculateMikk:" + k_v.Key);
                }
                if (reimport)
                {
                    mi.SaveAndReimport();
                }
            }
            else
            {
                Debug.LogWarning(k_v.Key + " is no model!!!");
            }
        }

        Debug.Log("End SetFbxFormat");
    }

    private static void ResetGameobjectTreeByRoot(Transform root)
    {
        Dictionary<GameObject, MeshType> rdTypeMap = new Dictionary<GameObject, MeshType>();
        MeshRenderer[] rds = root.GetComponentsInChildren<MeshRenderer>();
        foreach (var r in rds)
        {
            Material mat = r.sharedMaterial;
            Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
            if (mat != null && mesh != null)
            {
                string mesh_path = AssetDatabase.GetAssetPath(mesh);
                if (mat.shader.name == "Q/Scene/SpeedTree" || mat.shader.name == "Q/Scene/GrassWave")
                {
                    MeshType rdType = MeshType.Color;
                    if (!IsSceneBaked())
                    {
                        rdType = MeshType.Tangent;
                    }
                    rdTypeMap.Add(r.gameObject, rdType);
                }
                if (mat.shader.name == "Q/Scene/TextureNS" || mat.shader.name == "Q/Scene/T4M" || mat.shader.name == "Q/Scene/TextureNSCut")
                {
                    MeshType rdType = MeshType.Common;

                    if (mat.IsKeywordEnabled("_SPECULAR"))
                    {
                        rdType = MeshType.Tangent;
                    }
                    if (mat.IsKeywordEnabled("_NORMAL"))
                    {
                        rdType = MeshType.Tangent;
                    }
                    if (!IsSceneBaked())
                    {
                        rdType = MeshType.Tangent;
                    }
                    rdTypeMap.Add(r.gameObject, rdType);
                }
                if (mat.shader.name == "Q/Scene/QWater")
                {
                    MeshType rdType = MeshType.Tangent;
                    rdTypeMap.Add(r.gameObject, rdType);
                }
                if (!rdTypeMap.ContainsKey(r.gameObject))
                {
                    MeshType rdType = MeshType.Common;
                    if (!IsSceneBaked())
                    {
                        rdType = MeshType.Tangent;
                    }
                    rdTypeMap.Add(r.gameObject, rdType);
                }
            }
        }

        Transform common = root.Find("common");
        if (common == null)
        {
            common = new GameObject("common").transform;
        }
        common.parent = root;

        Transform tangent = root.Find("tangent");
        if (tangent == null)
        {
            tangent = new GameObject("tangent").transform;
        }
        tangent.parent = root;

        Transform color = root.Find("color");
        if (color == null)
        {
            color = new GameObject("color").transform;
        }
        color.parent = root;

        foreach (var k_v in rdTypeMap)
        {
            if (k_v.Key.name.Contains("_LOD") || (k_v.Key.transform.parent && k_v.Key.transform.parent.GetComponent<LODGroup>()))
                continue;
            switch (k_v.Value)
            {
                case MeshType.Color:
                    k_v.Key.transform.parent = color;
                    break;
                case MeshType.Common:
                    k_v.Key.transform.parent = common;
                    break;
                case MeshType.Tangent:
                    k_v.Key.transform.parent = tangent;
                    break;
            }
        }
    }

    private static void ResetGameobjectTree(GameObject root)
    {
        Debug.Log("Begin ResetGameobjectTree");
        if (root == null)
            return;
        Transform lowNode = root.transform.Find("Low");
        if (lowNode == null)
        {
            lowNode = new GameObject("Low").transform;
        }
        lowNode.parent = root.transform;
        MeshRenderer[] rds = root.GetComponentsInChildren<MeshRenderer>();
        string go_path;
        foreach (var r in rds)
        {
            go_path = FullTreePath(r.transform);
            if (go_path.Contains("/Mid/")
                || go_path.Contains("/High/")
                || go_path.Contains("_LOD")
                || go_path.Contains("/Terrain/")
                || go_path.Contains("/SkyBox/")
                || r.enabled == false
                || (r.gameObject.transform.parent && r.gameObject.transform.parent.GetComponent<LODGroup>()))
            {
                continue;
            }

            r.transform.parent = lowNode;
        }
        ResetGameobjectTreeByRoot(lowNode);

        Transform midNode = root.transform.Find("Mid");
        if (midNode != null)
        {
            ResetGameobjectTreeByRoot(midNode);
        }
        Transform highNode = root.transform.Find("High");
        if (highNode != null)
        {
            ResetGameobjectTreeByRoot(highNode);
        }
        Transform TerrainTrans = root.transform.Find("Terrain");
        if (TerrainTrans != null)
        {
            ResetGameobjectTreeByRoot(TerrainTrans);
        }
        Transform SkyBoxTrans = root.transform.Find("SkyBox");
        if (SkyBoxTrans != null)
        {
            ResetGameobjectTreeByRoot(SkyBoxTrans);
        }

        Debug.Log("End ResetGameobjectTree");
    }

    private bool SetTextureFormat(string scenePath)
    {
        Debug.Log("Begin SetTextureFormat");
        CommonTextureFormatDefine.SetTextureFormatForSceneObject(scenePath, texSize, true);
        Debug.Log("End SetTextureFormat");
        return true;
    }

    private void CollectNormalTextures(string[] dps)
    {
        for (int i = 0; i < dps.Length; i++)
        {
            string dp = dps[i];
            string dependType = Path.GetExtension(dp).ToLower();
            switch (dependType)
            {
                case ".mat":
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(dp);
                    int propertysCnt = ShaderUtil.GetPropertyCount(mat.shader);
                    string shaderName = mat.shader.name.ToLower();
                    for (int j = 0; j < propertysCnt; ++j)
                    {
                        string propertyName = ShaderUtil.GetPropertyName(mat.shader, j);
                        ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(mat.shader, j);
                        if (string.IsNullOrEmpty(propertyName) || propertyType != ShaderUtil.ShaderPropertyType.TexEnv)
                        {
                            continue;
                        }
                        if (propertyName.Contains("_Normal"))
                        {
                            Texture tex = mat.GetTexture(propertyName);
                            if (tex != null)
                            {
                                normalTextures.Add(AssetDatabase.GetAssetPath(tex));
                            }
                        }
                    }
                    break;   
            }
        }
    }

    static void SetAssetImporterAssetsBundleName(string assetPath, string abname, bool addToCurrent = true, bool forceOverride = false)
    {
        if (!string.IsNullOrEmpty(abname))
        {
            TextureImportSetting.SetIgnoreImportSetting(true);
            AssetImporter ai = AssetImporter.GetAtPath(assetPath);
            if (ai != null)
            {
                if (string.IsNullOrEmpty(ai.assetBundleName) || forceOverride)
                {
                    ai.assetBundleName = abname;
                    if (addToCurrent)
                    {
                        AddCurrentSceneABName(abname);
                    }
                }
            }
            TextureImportSetting.SetIgnoreImportSetting(false);
        }
    }

    void SetMeshAndTextureBundleName(GameObject root)
    {
        MeshFilter[] mfs = root.GetComponentsInChildren<MeshFilter>();
        List<DependsObject.PrefabGroup> depends = new List<DependsObject.PrefabGroup>();
        string abname = string.Empty;
        foreach (var m in mfs)
        {
            if (m.sharedMesh == null || m.gameObject.name.Contains("_LOD"))
                continue;

            if (m.gameObject.transform.parent && m.gameObject.transform.parent.GetComponent<LODGroup>())
                continue;

            string meshPath = AssetDatabase.GetAssetPath(m.sharedMesh);
            abname = GetMeshABName(sceneName, meshPath, true);
            SetAssetImporterAssetsBundleName(meshPath, abname);

            Renderer mr = m.GetComponent<Renderer>();
            if (mr != null && mr.sharedMaterial != null)
            {
                Material mat = mr.sharedMaterial;
                SerializedObject obj = new SerializedObject(mat);
                SerializedProperty sp = obj.GetIterator();
                List<string> refTexList = new List<string>();
                while (sp.Next(true))
                {
                    if (sp.propertyType == SerializedPropertyType.Generic && sp.type == "pair")
                    {
                        if (sp.propertyPath.Contains("m_TexEnvs"))
                        {
                            Texture t = mat.GetTexture(sp.displayName);
                            if (t != null)
                            {
                                string path = AssetDatabase.GetAssetPath(t);
                                abname = GetTextureABName(sceneName, path, true, mat.name);
                                SetAssetImporterAssetsBundleName(path, abname);
                            }
                        }
                    }
                }
            }
        }
    }

    bool SetAssetsBundleName(string scenePath, GameObject root)
    {
        Debug.Log("Begin SetAssetsBundleName");
        string sceneName = Path.GetFileNameWithoutExtension(scenePath);
        string[] dps = AssetDatabase.GetDependencies(scenePath);
        CollectNormalTextures(dps);
        SetMeshAndTextureBundleName(root);
        string abname;
        bool success = true;
        for (int i = 0; i < dps.Length; i++)
        {
            string asset = dps[i];
            string dependType = Path.GetExtension(asset).ToLower();
            switch (dependType)
            {
                case ".shader":
                case ".prefab":
                case ".cs":
                case ".unity":
                case ".anim":
                case ".controller":
                case ".giparams":
                    break;
                case ".mat":
                    //现在几个场景一起打包时，在collect shader的时候跟场景GetDependencies这个方法拿到的材质有出入，
                    //就会出现后面的场景如果collectShader里没有设置或disable的材质的keyword，
                    //但GetDependencies又获取到这个材质的引用，又因为我们没打一次场景都会把整个map文件夹进行revert一下，
                    //所以就会导致拿旧的那个来打包，就会覆盖掉前面场景的,
                    //所以改为只会collectshader里用到的材质
                    if (mSceneUseMaterialList.Contains(asset))
                    {
                        abname = GetMatABName(sceneName, asset);
                        SetAssetImporterAssetsBundleName(asset, abname);
                    }
                    break;
                case ".asset":
                    if (asset.Contains("grassData"))
                    {
                        abname = sceneName + "_grassData.unity3d";
                        SetAssetImporterAssetsBundleName(asset, abname);
                    }
                    break;
                case ".obj":
                case ".fbx":
                    abname = GetMeshABName(sceneName, asset, false);
                    SetAssetImporterAssetsBundleName(asset, abname);
                    break;
                case ".exr":
                case ".tga":
                case ".bmp":
                case ".png":
                case ".jpg":
                case ".cubemap":
                case ".tif":
                    abname = GetTextureABName(sceneName, asset, false, string.Empty);
                    SetAssetImporterAssetsBundleName(asset, abname);
                    break;
                default:
                    success = false;
                    Debug.LogErrorFormat("设置assetbundle name出错, 遇到未知的后缀格式, 请美术修改成支持的格式 path = {0}", asset);
                    break;
            }
        }
        Debug.Log("End SetAssetsBundleName");
        return success;
    }

    class ShaderInfo
    {
        public ShaderInfo(Material mat)
        {
			mat.EnableKeyword("_QFOG");
            mat.EnableKeyword("_SHADOWMASK_INALPHA");
            
            string[] keywords = mat.shaderKeywords;
            List<string> words = new List<string>();
            foreach (var k in keywords)
            {
                if (mat.IsKeywordEnabled(k))
                {
                    words.Add(k);
                }
            }
			shader = mat.shader;
            words.Sort();
            KeyWords = words.ToArray();
            Mat = Object.Instantiate(mat);
            SerializedObject obj = new SerializedObject(mat);
            SerializedProperty sp = obj.GetIterator();
            List<string> refTexList = new List<string>();
            while (sp.Next(true))
            {
                if (sp.propertyType == SerializedPropertyType.Generic && sp.type == "pair")
                {
                    if (sp.propertyPath.Contains("m_TexEnvs"))
                    {
                        Mat.SetTexture(sp.displayName, null);
                    }
                }
            }
        }

        public Shader shader;
        public Material Mat;
        public string[] KeyWords;

        public override string ToString()
        {
            string ret = shader.name.Replace("/", ".");
            foreach (var k in KeyWords)
            {
                ret += "_" + k;
            }

            return ret;
        }
    }

    static bool IsSceneBaked()
    {
        bool ret = false;
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (notNeedBakeSceneList.Contains(scene.name))
        {
            ret = true;
        }
        else
        {
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (var r in roots)
            {
                MeshRenderer[] mrs = r.GetComponentsInChildren<MeshRenderer>();
                foreach (var m in mrs)
                {
                    if (m.lightmapIndex >= 0)
                        return true;
                }
            }
        }
        return ret;
    }

    public static void ClearNoUseKeyWord(Material mat)
    {
        if (mClearedKeyWordMat.ContainsKey(mat))
        {
            return;
        }
        mClearedKeyWordMat[mat] = true;
        Debug.LogFormat("ClearNoUseKeyWord for {0}", mat);
        // 先去掉绝对用不上的key word
        if (mat.IsKeywordEnabled("_DEBUG"))
        {
            //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, "_DEBUG"));
            mat.DisableKeyword("_DEBUG");
        }

        if (mat.IsKeywordEnabled("_METALLICGLOSSMAP"))
        {
            //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, "_METALLICGLOSSMAP"));
            mat.DisableKeyword("_METALLICGLOSSMAP");
        }

        if (mat.IsKeywordEnabled("_NORMALMAP"))
        {
            //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, "_NORMALMAP"));
            mat.DisableKeyword("_NORMALMAP");
        }

        // 然后每种着色器确定自有 key word
        if (mat.shader.name == "Q/Scene/SpeedTree")
        {
            foreach (var k in mat.shaderKeywords)
            {
                if (mat.IsKeywordEnabled(k))
                {
                    if (k != "_ALPHATEST_ON" && k != "_QFOG" && k != "_SHADOWMASK_INALPHA")
                    {
                        if (k == "_ANIMATION_ON" && mat.HasProperty("_ANIMATION") && mat.GetFloat("_ANIMATION") != 0)
                        {
                            continue;
                        }
                        //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, k));
                        mat.DisableKeyword(k);
                    }
                }
            }
        }

        if (mat.shader.name == "Q/Scene/TextureNS")
        {
            foreach (var k in mat.shaderKeywords)
            {
                if (mat.IsKeywordEnabled(k))
                {
                    if (k != "_ALPHATEST_ON"
                        && k != "_NORMAL"
                        && k != "_TERRAINPANEL"
                        && k != "_SPECULAR"
                        && k != "_REFLECT"
                        && k != "_SHADOW"
                        && k != "_FLOW_ON"
                        && k != "_FLOW_OFF"
                        && k != "_QFOG"
                        && k != "_RIM"
                        && k != "_SHADOWMASK_INALPHA"
                        && k != "_CUBEMAP") 
                    {
                        //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, k));
                        mat.DisableKeyword(k);
                    }
                }
            }
        }

        if (mat.shader.name == "Q/Scene/T4M")
        {
            foreach (var k in mat.shaderKeywords)
            {
                if (mat.IsKeywordEnabled(k))
                {
                    if (k != "_T_THREE"
                        && k != "_T_FOUR"
                        && k != "_NORMAL"
                        && k != "_SPECULAR"
                        && k != "_SHADOW"
                        && k != "_FLOW_ON"
                        && k != "_FLOW_OFF"
                        && k != "_QFOG"
                        && k != "_RIM"
                        && k != "_COLOR_MASK"
                        && k != "_SHADOWMASK_INALPHA")
                    {
                        //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, k));
                        mat.DisableKeyword(k);
                    }
                }
            }
        }

        if (mat.shader.name == "Q/Scene/SkyBox")
        {
            foreach (var k in mat.shaderKeywords)
            {
                if (mat.IsKeywordEnabled(k))
                {
                    if (k != "_QFOG"
                        && k != "_ALPHABLEND_ON"
                        && k != "_GLOBALFOG"
                        && k != "_SKYFOG")
                    {
                        //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, k));
                        mat.DisableKeyword(k);
                    }
                }
            }
        }

        if (mat.shader.name == "Q/Scene/QWater")
        {
            foreach (var k in mat.shaderKeywords)
            {
                if (mat.IsKeywordEnabled(k))
                {
                    if (k != "_QFOG"
                        && k != "_DEPTH_TEXTURE"
                        && k != "_REFLECT"
                        && k != "_SPECULAR"
                        && k != "_RIM"
                        && k != "_FOAM")
                    {
                        //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, k));
                        mat.DisableKeyword(k);
                    }
                }
            }
        }

        if (mat.shader.name == "Q/Scene/GrassWave")
        {
            foreach (var k in mat.shaderKeywords)
            {
                if (mat.IsKeywordEnabled(k))
                {
                    if (k != "_QFOG"
                        && k != "_ALPHATEST_ON")
                    {
                        //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, k));
                        mat.DisableKeyword(k);
                    }
                }
            }
        }

        //这里清掉一些shader里根本就没有的keyword,这里有点坑，不知道这些keyword是怎么出来的
        var keywordList = ShaderVariantsCollectionsTool.GetShaderKeywords(mat.shader);
        foreach (var k in mat.shaderKeywords)
        {
            if (!keywordList.Contains(k))
            {
                //Debug.Log(string.Format("[{0:-10}]Disable KeyWord:{1}", mat.name, k));
                mat.DisableKeyword(k);
            }
        }

        //这里有些是写死定义了，有些又放在multi_compile里，这里为什么不统一来写呢？？？？？
        if (!keywordList.Contains("_QFOG"))
        {
            mat.DisableKeyword("_QFOG");
        }
        else
        {
            if (!ProjectSetting.IsProjectT16())
            {
                mat.EnableKeyword("_QFOG");
            }
        }
        if (!keywordList.Contains("_SHADOWMASK_INALPHA"))
            mat.DisableKeyword("_SHADOWMASK_INALPHA");
        else
            mat.EnableKeyword("_SHADOWMASK_INALPHA");
    }

    static bool SceneBaked = false;

    static void CreateShaderMat(Material mat, int lightmapIdex)
    {
        ClearNoUseKeyWord(mat);

        ShaderInfo si = new ShaderInfo(mat);
        List<string> matKeywords = new List<string>(si.Mat.shaderKeywords);
        string matPath = string.Format("Assets/_Resource/map/AllMats/ShaderVariants/Mats/{0}.mat", si);
        string dir = Path.GetDirectoryName(matPath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }
        if (!File.Exists(matPath))
        {
            AssetDatabase.CreateAsset(si.Mat, matPath);
        }
    }

    public static bool CollectShader()
    {
        Debug.Log("Begin CollectShader");
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();

        foreach (var o in roots)
        {
            Renderer[] mrs = o.GetComponentsInChildren<Renderer>();
            foreach (var m in mrs)
            {
                if (m.sharedMaterial != null)
                {
                    if (!m.enabled || m.sharedMaterial.shader == null)
                    {
                        Object.DestroyImmediate(m);
                    }
                    else
                    {

                        if (!m.sharedMaterial.shader.name.Contains("Q/Scene"))
                        {
#if UNITY_IPHONE || UNITY_ANDROID                            
                            Debug.LogError("场景使用了无效的着色器 " + m.sharedMaterial.shader.name);
                            return false;
#endif                            
                        }
                        else
                        {
                            //CreateShaderMat(m.sharedMaterial);
                            SetMaterialShaderABName(m.sharedMaterial);
                        }
                    }
                }
            }
            Terrain[] terrains = o.GetComponentsInChildren<Terrain>();
            foreach (var terrain in terrains)
            {
                if (terrain.materialTemplate != null)
                {
                    if (terrain.materialTemplate.shader.name == "Q/Scene/T4M")
                    {
                        //CreateShaderMat(terrain.materialTemplate);
                        SetMaterialShaderABName(terrain.materialTemplate);
                    }
                    else
                    {
#if UNITY_IPHONE || UNITY_ANDROID
                            Debug.LogError("场景Terrain使用了非t4m材质");
                            return false;
#endif
                    }
                }
            }
        }
        Debug.Log("End CollectShader");
        return true;
    }

    private static void SetMaterialShaderABName(Material mat)
    {
        ClearNoUseKeyWord(mat);
        string shaderPath = AssetDatabase.GetAssetPath(mat.shader);
        string abname = "../shader_bake.unity3d";
        SetAssetImporterAssetsBundleName(shaderPath, abname, false);
        string matPath = AssetDatabase.GetAssetPath(mat);
        if (!mSceneUseMaterialList.Contains(matPath))
        {
            mSceneUseMaterialList.Add(matPath);
        }
    }

    private void ClearSceneUsedMatPath()
    {
        mSceneUseMaterialList.Clear(); 
    }

    private static void ClearClearedKeyWordMat()
    {
        mClearedKeyWordMat.Clear();
    }

    public static void ClearMatTexture(Material mat)
    {
        SerializedObject obj = new SerializedObject(mat);
        SerializedProperty sp = obj.GetIterator();
        List<string> refTexList = new List<string>();
        while (sp.Next(true))
        {
            if (sp.propertyType == SerializedPropertyType.Generic && sp.type == "pair")
            {
                if (sp.propertyPath.Contains("m_TexEnvs"))
                {
                    mat.SetTexture(sp.displayName, null);
                }
            }
        }

        UnityEditor.EditorUtility.SetDirty(mat);
    }

    public static void BuildShaderVariantsCollection(bool clear = true)
    {
        Debug.Log("Begin to build scene shadervariants time=" + Time.realtimeSinceStartup);
        TextureImportSetting.SetIgnoreImportSetting(true);
        string tempMatPath = "Assets/_Resource/map/AllMats/Temp/Mats";
        string tempShaderVariantsPath = "Assets/_Resource/map/AllMats/Temp/Svc";
        bool createDir = false;
        if (!Directory.Exists(tempMatPath))
        {
            Directory.CreateDirectory(tempMatPath);
            createDir = true;
        }

        if (!Directory.Exists(tempShaderVariantsPath))
        {
            Directory.CreateDirectory(tempShaderVariantsPath);
            createDir = true;
        }

        if (createDir)
        {
            AssetDatabase.Refresh();
        }

        //开始收集所有场景上的材质keyword,并最终得到每个keyword在那些场景有引用到的列表
        Dictionary<string, Dictionary<string, List<string>>> oneKeywordInAllSceneMaps = new Dictionary<string, Dictionary<string, List<string>>>();
        ShaderVariantsCollectionsTool.GetMaterialKeywordsInAllScene(Scene3DRootDir, tempMatPath, ref oneKeywordInAllSceneMaps);

        //将上面收集到所有场景所使用的材质keyword进行公共和单个场景的划分
        Dictionary<string, List<string>> commonKeyMap = new Dictionary<string, List<string>>();
        Dictionary<string, List<string>> sceneKeyMap = new Dictionary<string, List<string>>();
        ShaderVariantsCollectionsTool.GetCommonAndSceneKeywordMap(oneKeywordInAllSceneMaps,ref commonKeyMap,ref sceneKeyMap);

        //打开一个烘焙的场景，把上面收集到的材质添加到场景里，进行收集shader variants collection
        List<string> shaderNameList = new List<string>();
        Scene scene = EditorSceneManager.OpenScene("Assets/_Resource/map/AllMats/Bake_Scene.unity", OpenSceneMode.Single);
        GameObject root = new GameObject("TempRoot");
        
        //build common shader variants collection
        foreach (var common in commonKeyMap)
        {
            List<string> matList = common.Value;
            string[] shaderNameArr = common.Key.Split('/');
            string saveName = shaderNameArr[shaderNameArr.Length - 1];
            string svcPath = tempShaderVariantsPath + "/" + saveName + ".shadervariants";
            string bundleName = "../scene_common_svc.unity3d"; 
            ShaderVariantsCollectionsTool.CreateShaderVariantsCollection(matList, tempMatPath, root.transform, svcPath, bundleName, true);
        }

        //build single scene shader variants collection
        foreach (var single in sceneKeyMap)
        {
            List<string> matList = single.Value;
            string sceneName = single.Key;
            string svcPath = tempShaderVariantsPath + "/" + sceneName + ".shadervariants";
            string bundleName = "../" + sceneName + "/" + "svc_" + sceneName + ".unity3d";
            ShaderVariantsCollectionsTool.CreateShaderVariantsCollection(matList, tempMatPath, root.transform, svcPath, bundleName, false);
            if (mAbNameDict != null && mAbNameDict.ContainsKey(sceneName) && mAbNameDict[sceneName] != null)
            {
                string value = "svc_" + sceneName + ".unity3d";
                if (!mAbNameDict[sceneName].Contains(value))
                {
                    mAbNameDict[sceneName].Add(value);
                }
            }
        }
        SaveAndRefreshAssets();

        //对收集到的shader variants collections 和 shader 进行打包，这里是将shader 和 svc拆分开来打包的
        string outputPath = Application.dataPath + "/../" + UnityInterfaceAdapter.GetStreamingAssets() + "/assetbundle/" + AssetBuildTool.OsType + "/map/temp/";
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        Debug.Log("Begin Build ShaderVarianteCollection AssetBundles");
        BuildPipeline.BuildAssetBundles(outputPath, BuildAssetBundleOptions.ForceRebuildAssetBundle, EditorUserBuildSettings.activeBuildTarget);
        Debug.Log("End Build ShaderVarianteCollection AssetBundles");

        if (root != null)
        {
            GameObject.DestroyImmediate(root);
        }
        string tempDir = "Assets/_Resource/map/AllMats/Temp";
        if (Directory.Exists(tempDir))
        {
            DeleteTempDir(tempDir);
        }
        if (Directory.Exists(outputPath))
        {
            DeleteTempDir(outputPath);
        }
        TextureImportSetting.SetIgnoreImportSetting(false);
        Debug.Log("end to build scene shadervariants time=" + Time.realtimeSinceStartup);
    }

    public static void DeleteTempDir(string file)
    {
        try
        {
            System.IO.DirectoryInfo fileInfo = new DirectoryInfo(file);
            fileInfo.Attributes = FileAttributes.Normal & FileAttributes.Directory;
            System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
            if (Directory.Exists(file))
            {
                foreach (string f in Directory.GetFileSystemEntries(file))
                {
                    if (File.Exists(f))
                    {
                        File.Delete(f);
                    }
                    else
                    {
                        DeleteTempDir(f);
                    }
                }

                Directory.Delete(file);
            }

        }
        catch (Exception ex)
        {
            Debug.LogError(ex.Message.ToString());
        }
    }

    public static void BuildShader()
    {
        //string svcPath = "Assets/_Resource/map/AllMats/ShaderVariants/Vars/SceneShaderVariantsCollection.shadervariants";
        //ShaderVariantCollection svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(svcPath);
        //svc.Clear();

        string[] matFiles = FileHelper.FindFileBySuffix("Assets/_Resource/map/AllMats/Bake", ".mat");
        foreach (var p in matFiles)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(p);
            ClearMatTexture(mat);
            string shaderPath = AssetDatabase.GetAssetPath(mat.shader);

            //aip = AssetImporter.GetAtPath(p);
            //aip.assetBundleName = "../scene_shader.unity3d";

            string abname = "../shader_bake.unity3d";
            SetAssetImporterAssetsBundleName(shaderPath, abname, false);

            //ShaderVariantCollection.ShaderVariant sv = new ShaderVariantCollection.ShaderVariant();
            //Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            //sv.shader = shader;
            //sv.passType = PassType.ForwardBase;
            //List<string> matKeywords = new List<string>(mat.shaderKeywords);
            //matKeywords.Add("DIRECTIONAL");
            ////matKeywords.Add("SHADOWS_SHADOWMASK");
            //sv.keywords = matKeywords.ToArray();
            //svc.Add(sv);

            //sv = new ShaderVariantCollection.ShaderVariant();
            //sv.shader = shader;
            //sv.passType = PassType.ShadowCaster;
            //sv.keywords = new string[1] { "SHADOWS_DEPTH" };
            //svc.Add(sv);
        }

        //aip = AssetImporter.GetAtPath(svcPath);
        //aip.assetBundleName = "../shadervariantscollection.unity3d";
    }

    static int meshPrefabIndex = 0;
    static int prefabIndex = 0;
    static void CombineMeshNode(GameObject root, int level)
    {
        DependsObject dpo = root.AddComponent<DependsObject>();
        Dictionary<string, Transform> fbxTrans = new Dictionary<string, Transform>();
        MeshFilter[] mfs = root.GetComponentsInChildren<MeshFilter>();
        List<DependsObject.PrefabGroup> depends = new List<DependsObject.PrefabGroup>();
        foreach (var m in mfs)
        {
            if (m.sharedMesh == null || m.gameObject.name.Contains("_LOD"))
                continue;

            if (m.gameObject.transform.parent && m.gameObject.transform.parent.GetComponent<LODGroup>())
                continue;

            Transform fbxRoot;
            string meshName = m.sharedMesh.name;
            if (!fbxTrans.ContainsKey(meshName))
            {
                fbxRoot = new GameObject(sceneName + "_prefab_" + meshName + "_" + (meshPrefabIndex++).ToString()).transform;
                fbxRoot.parent = root.transform;
                fbxRoot.transform.localPosition = Vector3.zero;
                fbxRoot.transform.localScale = Vector3.one;
                fbxTrans.Add(meshName, fbxRoot);
            }
            else
            {
                fbxRoot = fbxTrans[m.sharedMesh.name];
            }
            m.transform.parent = fbxRoot;
        }

        string prefabDir = string.Format("Assets/_Resource/map/{0}/QdazzleTempPrefabs", sceneName);
        if (!Directory.Exists(prefabDir))
            Directory.CreateDirectory(prefabDir);
        int count = 0;

        Transform prefabGoupTrans = null;
        List<GameObject> destroyList = new List<GameObject>();
        foreach (var f in fbxTrans)
        {
            if (prefabGoupTrans == null)
            {
                prefabIndex++;
                prefabGoupTrans = new GameObject("prefab_" + prefabIndex).transform;
                prefabGoupTrans.parent = root.transform;
                prefabGoupTrans.transform.localPosition = Vector3.zero;
                prefabGoupTrans.transform.localScale = Vector3.one;
            }
            f.Value.parent = prefabGoupTrans;
            count++;
            if (count >= BundleBuildConfig.SceneConfig.transformGroupCount)
            {
                DependsObject.PrefabGroup pg = GetPrefabInfo(prefabGoupTrans.gameObject);
                depends.Add(pg);
                string prefabPath = string.Format("{0}/{1}.prefab", prefabDir, prefabGoupTrans.name);
                if (File.Exists(prefabPath))
                {
                    File.Delete(prefabPath);
                }
                PrefabLightmapData.GenerateLightmapInfo(prefabGoupTrans.gameObject);
                PrefabUtility.CreatePrefab(prefabPath, prefabGoupTrans.gameObject);
                destroyList.Add(prefabGoupTrans.gameObject);
                prefabGoupTrans = null;
                count = 0;
            }
        }

        if (prefabGoupTrans != null)
        {
            DependsObject.PrefabGroup pg = GetPrefabInfo(prefabGoupTrans.gameObject);
            depends.Add(pg);
            string prefabPath = string.Format("{0}/{1}.prefab", prefabDir, prefabGoupTrans.name);
            if (File.Exists(prefabPath))
            {
                File.Delete(prefabPath);
            }
            PrefabLightmapData.GenerateLightmapInfo(prefabGoupTrans.gameObject);
            PrefabUtility.CreatePrefab(prefabPath, prefabGoupTrans.gameObject);
            destroyList.Add(prefabGoupTrans.gameObject);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        foreach (var f in destroyList)
        {
            string prefabPath = string.Format("{0}/{1}.prefab", prefabDir, f.name);
            string abname = string.Format("{0}_{1}.unity3d", sceneName, f.name);
            SetAssetImporterAssetsBundleName(prefabPath, abname);
            GameObject.DestroyImmediate(f);
        }
        dpo.DependObjects = depends.ToArray();
        dpo.level = level;
    }

    public static DependsObject.PrefabGroup GetPrefabInfo(GameObject obj)
    {
        MeshFilter[] mfs = obj.GetComponentsInChildren<MeshFilter>();
        List<Bounds> bounds = new List<Bounds>();
        foreach (var m in mfs)
        {
            if (m.sharedMesh == null)
                continue;

            //m.sharedMesh.RecalculateBounds();
            Bounds b = m.sharedMesh.bounds;
            Vector3[] vertices = new Vector3[8];
            vertices[0] = m.transform.TransformPoint(b.center + new Vector3(-b.size.x, -b.size.y, -b.size.z) * 0.5f);
            vertices[1] = m.transform.TransformPoint(b.center + new Vector3(b.size.x, -b.size.y, -b.size.z) * 0.5f);
            vertices[2] = m.transform.TransformPoint(b.center + new Vector3(b.size.x, -b.size.y, b.size.z) * 0.5f);
            vertices[3] = m.transform.TransformPoint(b.center + new Vector3(-b.size.x, -b.size.y, b.size.z) * 0.5f);
            vertices[4] = m.transform.TransformPoint(b.center + new Vector3(-b.size.x, b.size.y, -b.size.z) * 0.5f);
            vertices[5] = m.transform.TransformPoint(b.center + new Vector3(b.size.x, b.size.y, -b.size.z) * 0.5f);
            vertices[6] = m.transform.TransformPoint(b.center + new Vector3(b.size.x, b.size.y, b.size.z) * 0.5f);
            vertices[7] = m.transform.TransformPoint(b.center + new Vector3(-b.size.x, b.size.y, b.size.z) * 0.5f);

            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            for (int i = 0; i < 8; ++i)
            {
                Vector3 v = vertices[i];
                max.x = v.x > max.x ? v.x : max.x;
                max.y = v.y > max.y ? v.y : max.y;
                max.z = v.z > max.z ? v.z : max.z;

                min.x = v.x < min.x ? v.x : min.x;
                min.y = v.y < min.y ? v.y : min.y;
                min.z = v.z < min.z ? v.z : min.z;
            }

            Bounds boundingBox = new Bounds();
            boundingBox.max = max;
            boundingBox.min = min;
            bounds.Add(boundingBox);
        }

        DependsObject.PrefabGroup group = new DependsObject.PrefabGroup();
        group.prefabName = sceneName + "_" + obj.name;
        group.bounds = bounds.ToArray();

        return group;
    }

    static string[] LevelNames = new string[]{
        "Low"
        , "Mid"
        , "High"
    };

    static void SpliteScene(GameObject root)
    {
        Debug.Log("Begin SpliteScene");
        meshPrefabIndex = 0;
        prefabIndex = 0;
        string prefabDir = string.Format("Assets/_Resource/map/{0}/Prefabs", sceneName);
        if (Directory.Exists(prefabDir))
        {
            FileHelper.DeleteDirectory(prefabDir, false);
        }
        if (!Directory.Exists(prefabDir))
        {
            Directory.CreateDirectory(prefabDir);
        }

        int level = 0;
        foreach (var n in LevelNames)
        {
            Transform node = root.transform.Find(n);
            if (node != null)
            {
                for (int i = 0; i < node.childCount; ++i)
                {
                    CombineMeshNode(node.GetChild(i).gameObject, level);
                }
            }
            level++;
        }
        Debug.Log("End SpliteScene");
    }

    static void DeleteSkyboxMat()
    {
        RenderSettings.skybox = null;
    }

    static void DeleteNav()
    {
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();

        foreach (var root in roots)
        {
            Transform t = root.transform.Find("Navigation");
            if (t != null)
            {
                GameObject.DestroyImmediate(t.gameObject);
            }
        }
    }

    static void DeleteColliderRender()
    {
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (var root in roots)
        {
            Transform t = root.transform.Find("Collider");
            if (t != null)
            {
                Renderer[] mrs = t.gameObject.GetComponentsInChildren<Renderer>(true);
                foreach (var m in mrs)
                {
                    Object.DestroyImmediate(m);
                }

                MeshFilter[] mfs = t.gameObject.GetComponentsInChildren<MeshFilter>(true);
                foreach (var m in mfs)
                {
                    Object.DestroyImmediate(m);
                }
            }
        }
    }

    // 删掉整个场景里预设失效的物件
    static void DeleteMissingPrefab()
    {
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();

        foreach (var root in roots)
        {
            Transform[] objects = root.GetComponentsInChildren<Transform>();
            List<Transform> transList = new List<Transform>();
            foreach (var o in objects)
            {
                if (PrefabUtility.GetPrefabType(o.gameObject) == PrefabType.MissingPrefabInstance)
                {
                    transList.Add(o);
                }
            }
            while (transList.Count > 0)
            {
                var t = transList[0];
                Transform[] ts = t.GetComponentsInChildren<Transform>();
                foreach (var s in ts)
                {
                    transList.Remove(s);
                }
                GameObject.DestroyImmediate(t.gameObject);
            }
        }
    }

    public static string luaPath = string.Empty;
    public static string sceneName = string.Empty;
    static string sScenePath = string.Empty;
    public bool DoScene(string scenePath, BuildAssetBundleOptions op, bool genConfig = true)
    {
        Object o = AssetDatabase.LoadMainAssetAtPath(scenePath);
        if (o == null)
        {
            Debug.LogErrorFormat("Not found Scene Asset, scene = {0}", scenePath);
            return false;
        }

        Debug.Log("Start build scene " + Path.GetFileNameWithoutExtension(scenePath));
        texSize.Clear();
        normalTextures.Clear();
        ClearAllBundleName();
        ClearSceneUsedMatPath();
        sScenePath = scenePath;
        sceneName = Path.GetFileNameWithoutExtension(scenePath);
        SceneAsset sceneObj = AssetDatabase.LoadAssetAtPath(scenePath, typeof(SceneAsset)) as SceneAsset;
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
        GameObject[] roots = scene.GetRootGameObjects();
        SceneBaked = IsSceneBaked();           // 在打包前判断是否需要重新烘焙场景
        CombineLightColorAndShadowmaskTex();   // 合并光照贴图和shadowmask贴图
        EditorSceneManager.SaveScene(scene);   // 保存场景

        GameObject root = null;
        GameObject GrassRoot = null;
        GameObject ShaderGlobalCtrlRoot = null;
        foreach (var g in roots)
        {
            if (g.name == "SceneRoot")
            {
                root = g;
            }
            if (g.name == "GrassRoot")
            {
                GrassRoot = HandleGrassNode(g, scenePath);
            }
            if (g.name == "ShaderGlobalControl")
            {
                ShaderGlobalCtrlRoot = g;
                ShaderGlobalCtrl sGc = g.GetComponent<ShaderGlobalCtrl>();
                sGc.AllSet();
                sGc.OpenFlow = false;
                UnityEditor.EditorUtility.SetDirty(sGc);
            }
        }
        if (root == null)
        {
            Debug.LogErrorFormat("场景{0} 找不到SceneRoot根节点! ", sceneName);
            return false;
        }
        if (ShaderGlobalCtrlRoot == null)
        {
            Debug.LogErrorFormat("场景{0} 找不到ShaderGlobalCtrl结点", sceneName);
            return false;
        }
        if (!CheckAllNeedNode(root))
        {
            return false;
        }
        DestroyDebugNode();
        DeleteNav();
        DeleteColliderRender();
        DeleteMissingPrefab();      
        HandleLight(root);
        HandleSpecialLogicForProject();           
        if (!CheckUnreasonableUse(root))
        {
            Debug.LogError("打包场景失败!" + sceneName);
            return false;
        }

        SetFbxFormat();                // 设置场景中FBX模型格式
        ResetGameobjectTree(root);     // 对游戏对象的挂载节点进行检查设置，并检测游戏对象节点是否符合当前节点质量
        if (!CollectShader())          // 场景使用了无效的着色器
        {
            Debug.LogError("打包场景失败！ 收集shader出问题。" + sceneName);
            return false;
        }
        SaveAndRefreshAssets();
        SetTextureFormat(scenePath);   // 场景引用的贴图格式
        ResizeWaterDepthTextureByPanelSize(root);  // 根据平面大小调整水的贴图大小
        ClearRedundantPrefabRef(roots, scene);        // 清理预设的冗余依赖
        if (!SetAssetsBundleName(scenePath, root))
        {
            Debug.LogErrorFormat("打包场景失败! scene = {0}", sceneName);
            return false;
        }
        PrefabLightmapData rootLight = root.GetComponent<PrefabLightmapData>();
        if (rootLight != null)
        {
            rootLight.ApplyLightMapSet();
        }
        if (GrassRoot != null)
        {
            GameObject.DestroyImmediate(GrassRoot);
            GrassRoot = null;
        }

        SpliteScene(root);
        CheckSceneLodGroups(root);
        PrefabLightmapData.GenerateLightmapInfo(root);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.Refresh();
        AssetImporter ai = AssetImporter.GetAtPath(scenePath);
        ai.SaveAndReimport();
        string abname = sceneName + ".unity3d";
        SetAssetImporterAssetsBundleName(scenePath, abname);
        //Debug.Log("BuildShader");
        //BuildShader();
        string outputPath = Application.dataPath + "/../" + UnityInterfaceAdapter.GetStreamingAssets() + "/assetbundle/" + AssetBuildTool.OsType + "/map/" + sceneName + "/";
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        Debug.Log("Begin BuildAssetBundles");
        Debug.LogFormat("Total AssetBundleNames Count = {0}", AssetDatabase.GetAllAssetBundleNames().Length);
        Debug.LogFormat("Build outputPath = {0}", outputPath);
        BuildPipeline.BuildAssetBundles(outputPath, op, EditorUserBuildSettings.activeBuildTarget);
        Debug.Log("End BuildAssetBundles");

        AssetDatabase.Refresh();
        string prefabDir = string.Format("Assets/_Resource/map/{0}/QdazzleTempPrefabs", sceneName);
        if (Directory.Exists(prefabDir))
        {
            FileHelper.DeleteDirectory(prefabDir, true);
        }
        string prefabPath = Path.GetDirectoryName(scenePath) + "/" + sceneName + "_prefab.prefab";
        if (File.Exists(prefabPath))
            File.Delete(prefabPath);

        return true;
    }

    //清理prefab带来的冗余依赖
    //解决的情况是：美术拉一个prefab A到场景里，A下面有b,c两个结点，b引用了res1资源，c引用了res2资源，
    //然后美术把场景上prefab A 实例的c结点删除了，只留下b结点使用
    //如果不加这个函数处理，对场景CollectDependencies还是会得到引用了res1和re2
    //现在暂时把prefab A的Asset删除，对场景CollectDependencies就只会得到res1资源了
    private void ClearRedundantPrefabRef(GameObject[] roots, Scene scene)
    {
        UnityEngine.Object[] result = EditorUtility.CollectDependencies(roots);
        List<string> prefabPathList = new List<string>();
        foreach (var obj in result)
        {
            UnityEngine.Object prefab = PrefabUtility.GetPrefabParent(obj);
            if (prefab)
            {
                string assetpath = AssetDatabase.GetAssetPath(prefab);
                if (assetpath.EndsWith(".prefab"))
                {
                    if (assetpath.Contains("ShaderGlobalControl") || assetpath.Contains("GpuGrassRes"))
                    {
                        continue;
                    }
                    if (!prefabPathList.Contains(assetpath))
                    {
                        prefabPathList.Add(assetpath);
                    }
                }
            }
        }
        foreach (var prefab in prefabPathList)
        {
            AssetDatabase.DeleteAsset(prefab);
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.Refresh();
    }

    public static bool CheckAllNeedNode(GameObject root)
    {
        bool success = true;
        Transform collider = root.transform.Find("Collider");
        Transform terrain = root.transform.Find("Terrain");
        Transform skybox = root.transform.Find("SkyBox");
        Transform high = root.transform.Find("High");
        Transform mid = root.transform.Find("Mid");
        Transform low =  root.transform.Find("Low");
        if (collider == null)
        {
            Debug.LogError("SceneRoot 下没有找到Collider 结点");
            success = false;
        }
        if (terrain == null)
        {
            Debug.LogError("SceneRoot 下没有找到Terrain 结点");
            success = false;
        }
        if (skybox == null)
        {
            Debug.LogError("SceneRoot 下没有找到SkyBox 结点");
            success = false;
        }
        if (high == null)
        {
            Debug.LogError("SceneRoot 下没有找到High 结点");
            success = false;
        }
        if (mid == null)
        {
            Debug.LogError("SceneRoot 下没有找到Mid 结点");
            success = false;
        }
        if (low == null)
        {
            Debug.LogError("SceneRoot 下没有找到Low 结点");
            success = false;
        }
        return success;
    }

    public static void HandleSpecialLogicForProject()
    {
        if (ProjectSetting.IsProjectT16())
        {
            HandleSpecialLogicForProjectT16();
        }
    }

    private static void HandleSpecialLogicForProjectT16()
    {
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (var o in roots)
        {
            Renderer[] mrs = o.GetComponentsInChildren<Renderer>();
            foreach (var m in mrs)
            {
                Material mat = m.sharedMaterial;
                if (mat != null)
                {
                    string shaderName = mat.shader.name;
                    // 如果描边的宽度为0, 换成没有描边pass的shader
                    if (shaderName.Equals("Q/Scene/Outline"))
                    {
                        if (mat.HasProperty("_Outline"))
                        {
                            float outLineWidth = mat.GetFloat("_Outline");
                            if (outLineWidth < 0.0000001f)
                            {
                                mat.shader = Shader.Find("Q/Scene/SimpleLit");
                                Debug.LogFormat("材质 {0} 的描边宽度为0，强制把shader换成没有描边pass的节省性能", mat.name);
                            }
                        }
                        if (mat.mainTexture == null)
                        {
                            mat.EnableKeyword("MAINTEX_OFF");
                        }
                    }
                }
            }
        }
    }

    private GameObject HandleGrassNode(GameObject g, string scenePath)
    {
        GameObject GrassRoot = null;
        GrassGPUManager comp = g.GetComponent<GrassGPUManager>();
        if (comp)
        {
            GrassData grassData = comp.mGrassData;
            if (grassData && grassData.mGrassAssetList.Count > 0)
            {
                sceneName = Path.GetFileNameWithoutExtension(scenePath);
                string tmpPath = string.Format("Assets/_Resource/map/{0}/QdazzleTempPrefabs", sceneName);
                if (!Directory.Exists(tmpPath))
                {
                    Directory.CreateDirectory(tmpPath);
                }

                List<string> prefabList = new List<string>();
                foreach (var item in grassData.mGrassAssetList)
                {
                    string path = item.prefabPath;
                    if (!prefabList.Contains(path))
                    {
                        prefabList.Add(path);
                        string name = System.IO.Path.GetFileNameWithoutExtension(path);
                        GameObject @object = GameObject.Instantiate(AssetDatabase.LoadAssetAtPath(path, typeof(GameObject))) as GameObject;
                        Material mat = @object.GetComponent<Renderer>().sharedMaterial;
                        if (mat.shader.name != "Q/Scene/GrassWave")
                        {
                            mat.shader = Shader.Find("Q/Scene/GrassWave");
                        }
                        @object.name = name;
                        @object.transform.parent = g.transform;
                    }
                }

                string tmpPrefabPath = tmpPath + "/GpuGrassRes.prefab";
                PrefabUtility.CreatePrefab(tmpPrefabPath, g);
                string abname = string.Format("{0}_{1}.unity3d", sceneName, "GrassRes");
                SetAssetImporterAssetsBundleName(tmpPrefabPath, abname);
                GrassRoot = g;
            }
        }
        return GrassRoot;
    }

    private void HandleLight(GameObject root)
    {
        if (LightmapSettings.lightmaps.Length == 0)
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android || EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
            {
                Light[] lights = Resources.FindObjectsOfTypeAll(typeof(Light)) as Light[];
                if (lights.Length > 1)
                {
                    foreach (var l in lights)
                    {
                        if (l.shadows == LightShadows.None)
                        {
                            l.gameObject.SetActive(false);
                        }
                    }
                }
            }
        }
        else
        {
            if (root != null)
            {
                Light[] lights = root.gameObject.GetComponentsInChildren<Light>();
                int realTimeLightCount = 0;
                foreach (var l in lights)
                {
                    // 关掉所有标记为baked的灯光
                    if (l.lightmapBakeType == LightmapBakeType.Baked)
                    {
                        l.gameObject.SetActive(false);
                    }
                    else
                    {
                        if (l.type == LightType.Directional && realTimeLightCount == 0)
                        {
                            realTimeLightCount++;
                            l.shadows = LightShadows.None;
                        }
                        else
                        {
                            l.gameObject.SetActive(false);
                        }
                    }
                }
            }
            DeleteSkyboxMat();
        }
    }

    public static string GetTextureABName(string dir, string path, bool objectTexture, string matName)
    {
        if (path.Contains("map/Common"))
        {
            bool find = normalTextures.Contains(path);
            path = path.Replace("Assets/_Resource/map/", "");
            path = "../" + path.Replace('.', '_') + (find ? "_tex_normal" : "") + ".unity3d";
            return path;
        }
        else if (objectTexture)
        {
            if (normalTextures.Contains(path))
            {
                return string.Format("{0}_mat_{1}_tex_normal.unity3d", sceneName, matName);
            }
            else
            {
                if (path.Contains("_covermap_") || path.ToLower().Contains(".cubemap"))
                {
                    //场景中草的贴图打包到场景的贴图bundle下, cube单独统一打，低端机可以不加载
                    return string.Empty;
                }
                else
                {
                    return string.Format("{0}_mat_{1}.unity3d", sceneName, matName);
                }
            }
        }
        else
        {
            if (normalTextures.Contains(path))
            {
                return string.Format("texture_{0}_tex_normal.unity3d", dir.ToLower());
            }
            else if (path.ToLower().Contains("_comp_dir"))
            {
                return string.Format("texture_{0}_light_low.unity3d", dir.ToLower());
            }
            else if (path.ToLower().Contains(".cubemap"))
            {
                return string.Format("texture_{0}_cubmap.unity3d", dir.ToLower());
            }
            else
            {
                return string.Format("texture_{0}.unity3d", dir.ToLower());
            }
        }
    }

    public static string GetMeshABName(string sceneName, string path, bool objectMesh)
    {
        if (path.Contains("map/Common"))
        {
            path = path.Replace("Assets/_Resource/map/", "");
            path = "../" + path.Replace('.', '_').ToLower() + ".unity3d";
            return path;
        }
        else if (objectMesh)
        {
            return string.Format("{0}_mesh_{1}.unity3d", sceneName, Path.GetFileNameWithoutExtension(path).ToLower());
        }
        else
        {
            return string.Format("mesh_{0}.unity3d", sceneName);
        }
    }

    public static string GetMatABName(string dir, string path)
    {
        if (path.Contains("map/Common"))
        {
            path = path.Replace("Assets/_Resource/map/", "");
            path = "../" + path.Replace('.', '_').ToLower() + ".unity3d";
            return path;
        }
        else
        {
            return string.Format("mat_{0}.unity3d", dir.ToLower());
        }
    }

    public static void AddCurrentSceneABName(string abName)
    {
        currentSceneABNameDict[abName] = true;
    }

    public static void GetChild(ref List<GameObject> list, Transform tsf)
    {
        for (int i = 0; i < tsf.childCount; i++)
        {
            Transform child = tsf.GetChild(i);
            list.Add(child.gameObject);
            GetChild(ref list, child);
        }
    }

    public static string FullTreePath(Transform trans)
    {
        string ret = trans.name;
        Transform parent = trans.parent;
        while (parent != null)
        {
            ret = parent.name + "/" + ret;
            parent = parent.parent;
        }

        return ret;
    }

    public bool Clear3DSceneBundles()
    {
        Debug.Log("Begin Clear3DSceneBundles");
        if (!File.Exists(abNameJsonPath))
        {
            Debug.LogError("Clear3DSceneBundles Error, abNameForScene.json Not Exist");
            return false;
        }
        Dictionary<string, List<string>> abNameDict;
        try
        {
            System.IO.StreamReader abNameReader = new System.IO.StreamReader(abNameJsonPath);
            abNameDict = JsonHelper.ToObject<Dictionary<string, List<string>>>(abNameReader);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Parse abNameForScene.json Failed" + e);
            return false;
        }

        // every one contains directory path and filename with .unity
        string[] sceneArray = FileHelper.FindFileBySuffix(Scene3DRootDir, ".unity");  // 在项目资源中所有的场景文件路径
        List<string> currentSceneList = new List<string>();       // 保存场景名字而不用路径
        for (int i = 0; i < sceneArray.Length; ++i)
        {
            string scenePath = sceneArray[i];
            string sceneName = Path.GetFileNameWithoutExtension(scenePath); // no directory path
            if (!currentSceneList.Contains(sceneName))
            {
                currentSceneList.Add(sceneName);
            }
        }

        List<string> needClearList = new List<string>();
        foreach (var key in abNameDict.Keys)
        {
            if (currentSceneList.Contains(key))   // 在项目资源中包含这个场景就不用清除
            {
                continue;
            }

            if (!needClearList.Contains(key))
            {
                needClearList.Add(key);
            }
        }

        foreach (var key in needClearList)
        {
            abNameDict.Remove(key);             // 清除场景信息文件中不在项目资源的场景名字
        }

        string osType = AssetBuildTool.OsType;
        string abMapPath = Application.dataPath + "/../StreamingAssets/assetbundle/" + osType + "/map"; //StreamingAssets路径中的场景ab
        string[] sceneDirs = Directory.GetDirectories(abMapPath); // path + directory name
        var sceneList = new List<string>();            //StreamingAssets路径中的场景ab文件
        for (int i = 0; i < sceneDirs.Length; i++)
        {
            string sceneName = Path.GetFileName(sceneDirs[i]); // directory name has no extension
            Debug.Log("sceneName:" + sceneName);
            if (sceneName.Contains("common") || sceneName.Contains("Data"))
            {
                //Debug.Log("common or Data");
                continue;
            }
            sceneList.Add(sceneName);
        }
        List<string> allBundleFiles = new List<string>(100);
        for (int i = 0; i < sceneList.Count; i++)
        {
            List<string> sceneABNameList = null;
            try
            {
                sceneABNameList = abNameDict[sceneList[i]];
            }
            catch (System.Exception e)
            {
                Debug.Log("scene assetbundle name not found in json: " + sceneList[i]);
                string bundleDir = abMapPath + "/" + sceneList[i];
                Debug.Log("Delete this bundle Directory： " + bundleDir);
                Directory.Delete(bundleDir, true);
                continue;
            }
            if (sceneABNameList == null || sceneABNameList.Count == 0)
            {
                continue;
            }

            allBundleFiles.Clear();
            FileHelper.FindFileBySuffix(abMapPath + "/" + sceneList[i], ".unity3d", ref allBundleFiles);
            FileHelper.FindFileBySuffix(abMapPath + "/" + sceneList[i], ".bundle", ref allBundleFiles);
            Debug.LogFormat("path = {0}, bundleCount = {1}", abMapPath + "/" + sceneList[i], allBundleFiles.Count);
            for (int j = 0; j < allBundleFiles.Count; j++) // for each asset bundle on current scene
            {
                string fileName = Path.GetFileName(allBundleFiles[j]).ToLower();
                bool found = false;
                for (int k = 0; k < sceneABNameList.Count; k++)
                {
                    if (sceneABNameList[k].ToLower().Equals(fileName))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Debug.Log("Delete Unused File: " + allBundleFiles[j]);
                    File.Delete(allBundleFiles[j]);
                }
            }
        }

        // for common directory
        string commonABNameJsonPath = Application.dataPath + "/../abNameForCommon.json";
        StreamWriter writeJson = new StreamWriter(commonABNameJsonPath, false);

        List<string> allCommonABName = new List<string>();
        foreach (var item in abNameDict)
        {
            var sceneABList = item.Value;
            foreach (var name in sceneABList)
            {
                if (name.Contains("../common/") || name.Contains("../Common/"))
                {
                    string lowerName = name.ToLower();
                    allCommonABName.Add(lowerName);
                    writeJson.Write(lowerName + "\n");
                }
            }
        }
        writeJson.Close();
        if (Directory.Exists(abMapPath + "/common"))
        {
            allBundleFiles.Clear();
            FileHelper.FindFileBySuffix(abMapPath + "/common", ".unity3d", ref allBundleFiles);
            FileHelper.FindFileBySuffix(abMapPath + "/common", ".bundle", ref allBundleFiles);
            for (int j = 0; j < allBundleFiles.Count; j++) // for every asset bundle on common folder
            {
                string fileName = Path.GetFileName(allBundleFiles[j]);
                bool found = false;
                for (int k = 0; k < allCommonABName.Count; k++)
                {
                    if (allCommonABName[k].Contains(fileName.ToLower()))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Debug.Log("Delete Unused File: " + allBundleFiles[j]);
                    File.Delete(allBundleFiles[j]);
                }
            }
        }
        
        Debug.Log("End Clear3DSceneBundles");
        Debug.Log("Qdazzle_Build_Success");
        return true;
    }

    /// <summary>
    /// 合并光照贴图和shadowmask贴图
    /// </summary>
    private void CombineLightColorAndShadowmaskTex()
    {
        Debug.Log("Begin CombineLightColorAndShadowmaskTex");
        TextureImportSetting.SetIgnoreImportSetting(true);
        LightmapData[] lightmapArray = LightmapSettings.lightmaps;
        for (int i = 0; i < lightmapArray.Length; ++i)
        {
            // 光图合并操作
			if(lightmapArray[i].lightmapColor != null && lightmapArray[i].shadowMask != null)
			{
			    Texture2D newMap = LightmapCombineImpl(lightmapArray[i].lightmapColor, lightmapArray[i].shadowMask);

				// 写文件
				string newMapPath = AssetDatabase.GetAssetPath(lightmapArray[i].lightmapColor).Replace("exr", "new.png");
				System.IO.File.WriteAllBytes(newMapPath, newMap.EncodeToPNG());
				AssetDatabase.Refresh();

				TextureImporter oldImporter = TextureImporter.GetAtPath(AssetDatabase.GetAssetPath(lightmapArray[i].lightmapColor)) as TextureImporter;
				// 重新保存贴图为rgba的格式
				TextureImporter ti = TextureImporter.GetAtPath(newMapPath) as TextureImporter;
				TextureImporterPlatformSettings plSetting = ti.GetPlatformTextureSettings(ActivePlatformName);
				plSetting.overridden = true;
				plSetting.format = CommonTextureFormatDefine.Platform2DefaultRGBAFormat[ActivePlatformName];
				ti.textureType = TextureImporterType.Default;
				ti.anisoLevel = oldImporter.anisoLevel;
				ti.wrapMode = oldImporter.wrapMode;
				ti.SetPlatformTextureSettings(plSetting);
				ti.isReadable = false;
				ti.SaveAndReimport();

				// 把场景的光照图引用改成新的texture
				string[] oldMetaFile = File.ReadAllLines(AssetDatabase.GetAssetPath(lightmapArray[i].lightmapColor) + ".meta");
				string[] newMetaFile = File.ReadAllLines(newMapPath + ".meta");
				newMetaFile[1] = oldMetaFile[1];
				File.WriteAllLines(newMapPath + ".meta", newMetaFile);
                mTempFiles.Add(newMapPath);
                mTempFiles.Add(newMapPath + ".meta");

				// 删除旧文件
				AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(lightmapArray[i].lightmapColor));
				AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(lightmapArray[i].shadowMask));
                RefreshAssets();
			}
        }
        TextureImportSetting.SetIgnoreImportSetting(false);
        Debug.Log("End CombineLightColorAndShadowmaskTex");
    }

    private Texture2D LightmapCombineImpl(Texture2D lightmapColor, Texture2D shadowmask)
    {
        string lightmapPath = AssetDatabase.GetAssetPath(lightmapColor);

        TextureImporter ti = TextureImporter.GetAtPath(lightmapPath) as TextureImporter;
        TextureImporter smImporter = TextureImporter.GetAtPath(AssetDatabase.GetAssetPath(shadowmask)) as TextureImporter;

        // 取两张图的最大分辨率
        int maxSize = ti.maxTextureSize >= smImporter.maxTextureSize ? ti.maxTextureSize : smImporter.maxTextureSize;
        maxSize = maxSize <= 2048 ? maxSize : 2048;
  
        // 把光图跟shadowmask 设置为同一个分辨率大小, 并设置为readable
        TextureImporterPlatformSettings plSetting = ti.GetPlatformTextureSettings(ActivePlatformName);
        plSetting.overridden = true;
        plSetting.maxTextureSize = maxSize;
        plSetting.format = TextureImporterFormat.RGBA32;
        plSetting.textureCompression = TextureImporterCompression.Uncompressed;
        ti.textureType = TextureImporterType.Lightmap;
        ti.SetPlatformTextureSettings(plSetting);
        ti.isReadable = true;
        ti.SaveAndReimport();

        plSetting = smImporter.GetPlatformTextureSettings(ActivePlatformName);
        plSetting.overridden = true;
        plSetting.maxTextureSize = maxSize;
        plSetting.format = TextureImporterFormat.RGBA32;
        plSetting.textureCompression = TextureImporterCompression.Uncompressed;
        smImporter.SetPlatformTextureSettings(plSetting);
        smImporter.isReadable = true;
        smImporter.SaveAndReimport();
        SaveAndRefreshAssets();
        
        // 把shadow mask 的信息写到光照图的alpha 通道
        ///////////////////////////////////////////////////////
        Texture2D newLightmap = new Texture2D(lightmapColor.width, lightmapColor.height, TextureFormat.RGBAFloat, true);

        Color[] lc = lightmapColor.GetPixels();
        Color[] sm = shadowmask.GetPixels();
        Color[] nc = new Color[lc.Length];
        for (int i = 0; i < nc.Length; ++i)
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows || EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64)
            {
                float converToLDRLightmapFactor = 5 * lc[i].a * 0.5f;
                nc[i] = new Color(lc[i].r * converToLDRLightmapFactor, lc[i].g * converToLDRLightmapFactor, lc[i].b * converToLDRLightmapFactor, sm[i].r);
            }
            else
            {
                nc[i] = new Color(lc[i].r, lc[i].g, lc[i].b, sm[i].r);
            }
        }

        newLightmap.SetPixels(nc);
        newLightmap.Apply();
        return newLightmap;
    }

    private void ResizeWaterDepthTextureByPanelSize(GameObject root)
    {
        MeshRenderer[] rds = root.GetComponentsInChildren<MeshRenderer>();
        foreach(var rd in rds)
        {
            Material mat = rd.sharedMaterial;
            if (mat && mat.shader.name == "Q/Scene/QWater")
            {
                Texture2D tex = mat.GetTexture("_DeepTex") as Texture2D;
                if (tex)
                {
                    int panelSize = 512;
                    if(rd.bounds.size.x > 512 || rd.bounds.size.z > 512)
                        panelSize = 1024;

                    var ti = TextureImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
                    var plSetting = ti.GetPlatformTextureSettings(ActivePlatformName);
                    plSetting.overridden = true;
                    plSetting.maxTextureSize = panelSize;
                    ti.SetPlatformTextureSettings(plSetting);
                    ti.SaveAndReimport();
                }
            }
        }
    }

    private static void CheckSceneLodGroups(GameObject root)
    {
        Debug.Log("Start check scene LODGroups!");
        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        bool hasError = false;
        string msg = "";
        foreach (var renderer in renderers)
        {
            var lodGroup = renderer.GetComponent<LODGroup>();
            if (lodGroup)
            {
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length >= 1)
                {
                    Renderer[] lod0Renders = lods[0].renderers;
                    for (int i = 1; i < lods.Length; ++i)
                    {
                        if (lods[i].renderers == null || lods[i].renderers.Length != lod0Renders.Length)
                        {
                            hasError = true;
                            msg += "LodGroup " + i + " in GameObject " + renderer.gameObject.name + " has wrong renderers!\n";
                        }
                    }
                }
            }
        }
        if (hasError)
        {
            Debug.LogThrowError(msg);
        }
        Debug.Log("End check scene LODGroups!");
    }
}