using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;

using System.Text.RegularExpressions;
using Debug = EditorDebug;

//fix_me  
//a. UIBundleInfo.txt 没用了就从svn删除

public class LayoutBundleTool : AssetBase        
{
    public override string Type { get { return "ui"; } }

    public override List<string> SrcDir { get; set; }     // 在实例化时保存项目中的场景资源及场景使用资源路径

    public override List<string> DstDir { get; set; }     // 保存资源打包后的ab包资源存储路径以及分类

    public  string IconConfigpath = Application.dataPath + "/_Resource/Textures/UI/Icon/BuildIconConfig.json";
    public string DynamicConfigpath = Application.dataPath + "/_Resource/Textures/UI/Dynamic/BuildDynamicConfig.json";
    public string DynamicLuaConfigPath = Application.dataPath + "/../3dscripts/lua_source/config/dynamic_config";
    public string DynamicLuaConfigFile = Application.dataPath + "/../3dscripts/lua_source/config/dynamic_config/dynamic_config.lua";
    public static string UILayoutResInfoConfig = Application.dataPath + "/../3dscripts/lua_source/config/ui_res_config/ui_layout_res_config.lua";
    public Dictionary<string, bool> IconList = new Dictionary<string, bool>();         // 在UI数据打包结束（包括因失败而结束）时，将数据写入项目BuildIconConfig.json文件
    public Dictionary<string, bool> DynamicList = new Dictionary<string, bool>();

    private void ResetJsonListByFile(Dictionary<string, bool> jsonList, string filePath)
    {
        Dictionary<string, bool> tmpList = new Dictionary<string, bool>();
        if (jsonList.Count > 0)
        {
            jsonList.Clear();
        }
        System.IO.StreamReader configReader = new System.IO.StreamReader(filePath);
        tmpList = JsonHelper.ToObject<Dictionary<string, bool>>(configReader);
        configReader.Close();

        foreach (string key in tmpList.Keys)
        {
            Debug.Log(key);
            if (Directory.Exists(key))
            {
                jsonList.Add(key, tmpList[key]);
            }
        }
    }

    public override void Init()
    {
        Dictionary<string, bool> tmpList = new Dictionary<string, bool>();
        SrcDir = new List<string>  
        {
            "Assets/_Resource/Prefabs/UI",           // 游戏UI面板
            "Assets/_Resource/Prefabs/Atlas",        
            "Assets/_Resource/Prefabs/Font",
            "Assets/_Resource/Textures/UI/Icon",
            "Assets/_Resource/Textures/UI/Dynamic"
        };

        DstDir = new List<string>
        {
            "gui/UI",
            "gui/Atlas",
            "gui/Font",
            "gui/Icon",
            "gui/Dynamic"
        };

        ResetJsonListByFile(IconList, IconConfigpath);
        ResetJsonListByFile(DynamicList, DynamicConfigpath);
    }

    /// <summary>
    /// 将字典类型的对象数据写入json文件中
    /// </summary>
    /// <param name="json"></param>
    /// <param name="filePath"></param>
    private void WriteJsonToFile(Dictionary<string, bool> json, string filePath)
    {
        string json_str = JsonHelper.ToJson(json);
        File.WriteAllText(filePath, json_str);
    }

    public void WriteIconConfigtoFile()
    {
        // 写入文件中
        WriteJsonToFile(IconList, IconConfigpath);   
    }

    public void WriteDynamicConfigtoFile()
    {
        WriteJsonToFile(DynamicList, DynamicConfigpath);
    }


    public override bool Build(BuildTarget platform)          // 不同的程序员对 打包Build 的编写思路不同
    {
        Debug.Log("Begin LayoutBundleTool Build");
        if (!BuildLayout(platform)                            // 只要函数体有一个为假就运行if里面的语句
            || !BuildFont(platform)                             // 对字体打包
            || !BuildUIAtlas(platform)                        // UI图集打包
            || !BuildIcon("Assets/_Resource/Textures/UI/Icon/", platform, BundleBuildConfig.LayoutConfig.BuildIconRebuild)     // 打包图标
            || !BuildDynamic(platform))
        {
            Debug.Log("LayoutBundleTool Build failure！");
            WriteIconConfigtoFile();
            WriteDynamicConfigtoFile();
            return false;
        }
        WriteIconConfigtoFile();
        WriteDynamicConfigtoFile();
        Debug.Log("End LayoutBundleTool Build");
        return true;
    }

    public bool BuildAtlasBundle(string fileName, BuildTarget platform )
    {
        Debug.Log(string.Format("Start Build Atlas[{0}]", fileName));
        string atlasPath = "Assets/_Resource/Prefabs/Atlas/";
        UIAtlas atl = UnityInterfaceAdapter.LoadAssetAtPath<UIAtlas>(atlasPath + fileName + ".prefab");
        if(atl == null)
        {
            Debug.LogError("[key path, error!] atlas: " + fileName + "坏了，找项目组对应程序修复一下");
            return false;
        }

        if (atl.spriteMaterial == null)
        {
            Debug.LogError("[key path, error!] " + fileName + " 图集丢失贴图，找项目组对应程序修复一下");
            return false;
        }

        List<UnityEngine.Object> objlist = new List<UnityEngine.Object>();

        // 其实这个mat在打包的时候可有可无
        // 但是，这里检测就是为了强制其它人将这个mat文件提交到svn，方便后面的人修改图集
        if (!File.Exists(atlasPath + fileName + ".mat"))
        {
            Debug.LogError("[key path, error!] " + fileName + "材质丢失， 找项目组对应程序修复一下");
            return false;
        }

        if (atl.beWithPng)
        {
            UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(atlasPath + fileName + ".png");
            if (obj != null)
            {
                objlist.Add(obj);
            }
            else
            {
                Debug.Log("error!can't find " + fileName + ".png");
                return false;
            }
        }
        else
        {
            UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(atlasPath + fileName + ".bytes");
            if (obj != null)
            {
                objlist.Add(obj);
            }
            else
            {
                Debug.Log("[Key path, error!] can't find " + fileName + ".bytes");
                return false;
            }
        }
        
        bool haveBuilded;
        if (!BuildOne(null
                     , AssetBuildTool.GetBranchPath("/gui/Atlas/" + fileName + ".unity3d")
                     , AssetDatabase.LoadMainAssetAtPath(atlasPath + fileName + ".prefab")
                     , objlist.ToArray()
                     , platform
                     , out haveBuilded
                     , true))
        {
            Debug.LogError("[key path, error!] " + string.Format("Build {0} Error", fileName));
            return false;
        }
        Debug.Log(string.Format("End Build Atlas[{0}]", fileName));
        if (!File.Exists(AssetBuildTool.GetBranchPath(AssetBuildTool.GetEditorBundlePath(AssetBuildTool.GetBranchPath("/gui/Atlas/" + fileName + ".unity3d")))))
        {
            Debug.Log("[Key path, error!] build  " + fileName + " faild");
            return false;
        }

        return true;
    }

   
    public bool BuildUIAtlas(BuildTarget platform)
    {
        Debug.Log("Start Build UI Atlas");
        string UIAtlasBundlePath = AssetBuildTool.GetEditorBundlePath("/gui/Atlas/");
        //if (!Directory.Exists(UIAtlasBundlePath))
        //{
        //    Directory.CreateDirectory(UIAtlasBundlePath);
        //}

        List<string> UIAtlasFileList = new List<string>();
        string UIAtlasPath = "Assets/_Resource/Prefabs/Atlas/";                                           // 项目中的图集地址
        foreach (string file in FileHelper.FindFileBySuffix(UIAtlasPath, ".prefab"))
        {
            UIAtlasFileList.Add(file.Replace(".prefab", "").Replace(UIAtlasPath, ""));
        }

        UIAtlasFileList.Sort();   //图集名字集合

//#if UNITY_IPHONE
//        foreach (string atlasName in UIAtlasFileList)
//        {
//            UnityEngine.Object prefab_obj = AssetDatabase.LoadMainAssetAtPath(UIAtlasPath + atlasName + ".prefab");
//            GameObject gameobject = prefab_obj as GameObject;
//            UIAtlas atlat = gameobject.GetComponent<UIAtlas>();
//            atlat.beWithPng = true;
//            atlat.MarkAsChanged();
//        }
//        AssetDatabase.SaveAssets();
//        AssetDatabase.Refresh();
//#endif
        List<string> atlasList = new List<string>();
        foreach (string atlasName in UIAtlasFileList)          // 根据图集名遍历打包图集文件
        {
            UIAtlas atl = UnityInterfaceAdapter.LoadAssetAtPath<UIAtlas>(UIAtlasPath + atlasName + ".prefab");
            // 统一使用 svn版本检测
            if (CheckVersion(new string[] { UIAtlasPath + atlasName + ".prefab",  UIAtlasPath + atlasName + ".png" }))
            {
                AddToBundleList("/gui/Atlas/" + atlasName + ".unity3d");
                continue;
            }
            atlasList.Add(atlasName);
            Material m = UnityInterfaceAdapter.LoadAssetAtPath<Material>(UIAtlasPath + atlasName + ".mat");
            if(m == null)
            {
                Debug.LogError(string.Format("没有找到图集[{0}]对应的材质球，请联系程序!", atlasName));
                return false;
            }
            m.shader = null;         // 没有找到图集[{ 0}]对应的材质球

            if (!atl.beWithPng)
            {
                string pngPath = Application.dataPath + "/_Resource/Prefabs/Atlas/" + atlasName + ".png";
                //int quality, alphaQuality;
                //AssetBase.GetWebpQualiltyByType("ui", out quality, out alphaQuality);
                if (!File.Exists(pngPath))
                {
                    Debug.LogError(string.Format("[key path, error!] Assets/_Resource/Prefabs/Atlas/{0}.png not exist", atlasName));
                    return false;
                }
                ConvertPngToWebp(pngPath, pngPath.Replace(".png", ".bytes"), atl.losslessWebp, atl.pngZipLevel, atl.alphaZipLevel);
            }
        }
        
        SaveAndRefreshAssets();                                // 保存刷新资源
        
        foreach (string atl in atlasList)                        // 依次创建图集
        {
            if (!BuildAtlasBundle(atl, platform))
                return false;
        }
        Debug.Log("End Build UI Atlas");
        return true;
    }

    // 以下几个数据结构纯粹是为了保持对之前版本的兼容
    // 很多字段都没用了
    public class LResourceData
    {
        public string mPath;
        public int mSize;
        public int mType;
    }

    public enum RDependsType
    {
        eDAtlas,
        eDIcon,
    }

    public class LDependsData
    {
        public string name;
        public RDependsType type;
    }


    public class LUIResourceData
    {
        public string mName;
        public LResourceData mPanelData;
        public List<LDependsData> mDependence = new List<LDependsData>();
        public List<LResourceData> mDependencePrefabs = new List<LResourceData>();
    }

    public class LUIPanelData
    {
        public Dictionary<string, LUIResourceData> mPreloadDatas = new Dictionary<string, LUIResourceData>();
        public Dictionary<string, LUIResourceData> mPanelDatas = new Dictionary<string, LUIResourceData>();
    }

    /// <summary>
    /// 打包场景的UI面板
    /// </summary>
    /// <param name="platform"></param>
    /// <returns></returns>
    public bool BuildLayout(BuildTarget platform)
    {
        Debug.Log("Start Build Layout");
        List<string> LayoutList = new List<string>();          // 读取所有UI面板
        LayoutList.Clear();
        string LayoutPath = "Assets/_Resource/Prefabs/UI/";
        foreach (string file in FileHelper.FindFileBySuffix(LayoutPath, ".prefab"))
        {
            LayoutList.Add(file.Replace(LayoutPath, "").Replace(".prefab", ""));
        }
        LayoutList.Sort();     //  ？？？？？？？？？？？

        string layoutBundlePath = AssetBuildTool.GetEditorBundlePath("/gui/UI/");     // 打包后，包的存放路径
        //if (!Directory.Exists(layoutBundlePath))
        //{
        //    Directory.CreateDirectory(layoutBundlePath);
        //}

        foreach (string layout in LayoutList)
        {
            if (CheckVersion(new string[] { LayoutPath + layout + ".prefab" }))    // 检查版本变化
            {
                AddToBundleList("/gui/UI/" + layout + ".unity3d");   // 把已经打包的UI面板加到，已打队列
                continue;
            }
                

            GameObject obj = AssetDatabase.LoadMainAssetAtPath(LayoutPath + layout + ".prefab") as GameObject;  // 读取预设UI面板资源
            if (obj == null)
            {
                Debug.Log("[key path, error!] Panel " + layout + " broken ,can't load!");
                continue;
            }

            UILabel[] labelArray = obj.GetComponentsInChildren<UILabel>(true);                      // 重置UILabel中的内容
            UIRichlabel[] richlabelArray = obj.GetComponentsInChildren<UIRichlabel>(true);
            for (int i = 0; i < labelArray.Length; ++i)
            {
                labelArray[i].text = "";
            }

            for (int i = 0; i < richlabelArray.Length; ++i)
            {
                richlabelArray[i].text = "";
            }

            //fix_me: log 要体现source
            Debug.Log(string.Format("Start build layout bundle [{0}]", layout));
            bool haveBuilded;
            if (!BuildOne(null
                , AssetBuildTool.GetBranchPath("/gui/UI/" + layout + ".unity3d")
                , obj
                , null
                , platform
                , out haveBuilded
                , true))
            {
                Debug.LogError("[key path, error!] " + string.Format("Build {0} Error", layout));
                return false;
            }

            Debug.Log(string.Format("End build layout bundle [{0}]", layout));

            if (!File.Exists(AssetBuildTool.GetBranchPath(layoutBundlePath + layout + ".unity3d")))
            {
                Debug.Log("error," + layout + " build faild");
                return false;
            }

            WritePanel(layout);    // 在单个UI面板创建成功时，将配置信息写入lua文件
        }
        //打包layout的时候导出面板资源映射文件，用于海外多语言
        ExportAtlasMapping();

        Debug.Log("End Build Layout");
        return true;
    }

    [MenuItem(MenuNameConfig.BuildSelectPanel, false, MenuNameConfig.QAssetMenuPriority)]
    public static void BuildSelectedPanels()
    {
        Debug.Log("打包选中Panels进行中");
        List<string> list = new List<string>();
        string panelName;
        //string prefabPath;
        float count = 0f;
        float totalCnt = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets).Length;
        foreach (GameObject o in Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets))
        {
            ++count;
            EditorUtility.DisplayProgressBar("打包选中Panels进度中...", "", count / totalCnt);
            panelName = o.name;
            BuildSinglePanel(panelName);
        }
        EditorUtility.ClearProgressBar();
        Debug.Log("打包选中Panels结束");
    }

    public static bool BuildSinglePanel(string panelName)
    {

        //fix_me: 现在本地不需要  去读写 PanelSetting吧  即使是本地调式
        //不需要机不要了 业务群经常反馈冲突 应该就是这个文件吧
        //换掉txt的配置格式
        // 现在配置不在有PanelSetting了，侧地去掉了这个逻辑，配置最终由后台生成到lua里了
        // 文件放在misc/panel_setting.lua里
        string panelPath = "Assets/_Resource/Prefabs/UI/";
        string layoutBundlePath = AssetBuildTool.GetEditorBundlePath("/gui/UI/");
        //if (!Directory.Exists(layoutBundlePath))
        //{
        //    Directory.CreateDirectory(layoutBundlePath);
        //}
        LUIPanelData paneldate = new LUIPanelData();
        string text = string.Empty;

        GameObject obj = AssetDatabase.LoadMainAssetAtPath(panelPath + panelName + ".prefab") as GameObject;
        if (obj == null)
        {
            UIMessageBox.Warning("Panel " + panelName + " can't find!");
            return false;
        }

        UILabel[] labelArray = obj.GetComponentsInChildren<UILabel>(true);
        UIRichlabel[] richlabelArray = obj.GetComponentsInChildren<UIRichlabel>(true);
        for (int i = 0; i < labelArray.Length; ++i)
        {
            labelArray[i].text = "";
        }

        for (int i = 0; i < richlabelArray.Length; ++i)
        {
            richlabelArray[i].text = "";
        }

        BuildPipeline.BuildAssetBundle(obj
                                        , null
                                        , AssetBuildTool.GetBranchPath(layoutBundlePath + panelName + ".unity3d")
                                        , AssetBuildTool.buildBundlOptions
                                        , AssetBuildTool.BuildPlatformTarget);


        if (!File.Exists(AssetBuildTool.GetBranchPath(layoutBundlePath + panelName + ".unity3d")))
        {
            UIMessageBox.Warning("面板打包失败：" + panelName);
            return false;
        }

        WritePanel(panelName);
        return true;
    }


    /// <summary>
    /// 将单个面板的配置信息写入文件
    /// </summary>
    private static LUIResourceData WritePanel(string layout)
    {
        LUIResourceData uiresdate = new LUIResourceData();
        LResourceData resdate     = new LResourceData();
        // 这些字段都没什么用了，直接置空
        resdate.mSize = 0;
        resdate.mType = 0;
        resdate.mPath = "";

        uiresdate.mPanelData = resdate;
        
        List<string> atlasList = new List<string>();
        List<string> iconList  = new List<string>();
        GameObject obj = AssetDatabase.LoadMainAssetAtPath("Assets/_Resource/Prefabs/UI/" + layout + ".prefab") as GameObject;

        // 收集图集和icon的依赖关系
        GameObject findObj = GameObject.Instantiate(obj) as GameObject;
        UISprite[] spList  = findObj.GetComponentsInChildren<UISprite>();
        atlasList.Clear();
        for (int i = 0; i < spList.Length; ++i)
        {
            string atlasName = spList[i].AtlasName;
            if (string.IsNullOrEmpty(atlasName))
                continue;
            if (!atlasList.Contains(atlasName))
            {
                atlasList.Add(atlasName);
            }
        }

        UITexture[] txList = findObj.GetComponentsInChildren<UITexture>();
        iconList.Clear();
        for (int i = 0; i < txList.Length; ++i)
        {
            string iconPath = txList[i].TexturePath;
            if (string.IsNullOrEmpty(iconPath))
                continue;
            if (!iconList.Contains(iconPath))
            {
                iconList.Add(iconPath);
            }
        }

        foreach (var depends in atlasList)
        {
            LDependsData dependsData = new LDependsData();
            dependsData.type = RDependsType.eDAtlas;
            dependsData.name = depends;
            uiresdate.mDependence.Add(dependsData);
        }

        foreach (var depends in iconList)
        {
            LDependsData dependsData = new LDependsData();
            dependsData.type = RDependsType.eDIcon;
            dependsData.name = depends;
            uiresdate.mDependence.Add(dependsData);
        }
        GameObject.DestroyImmediate(findObj);

        // 写入文件中
        string json_str = JsonHelper.ToJson(uiresdate);
        string writePath = AssetBuildTool.GetEditorStreamingAssetsPath("Panels/" + AssetBuildTool.OsType + "/" + layout + ".txt"); 
        File.WriteAllText(writePath, "\"" + layout + "\" : " + json_str);

        Debug.Log(string.Format("write panel info[{0}] to {1} ", layout, writePath));
        return uiresdate;
    }

    public static void Build()
    {
        Debug.Log("开始打包全部UI Resource");
        AssetBuildTool.BuildBundle(AssetBuildTool.OsType, "ui", string.Empty, true);
        AssetDatabase.Refresh();
        Debug.Log("结束打包全部UI Resource");
    }

    public static void RebuildFont()
    {
        Debug.Log("开始重建静态字体");
        GeneratedBitmap("semibold");
        GeneratedBitmap("CHei3HK-Bold");
        GeneratedBitmap("HanSansTWHK");
        Debug.Log("结束重建静态字体");
    }

    static public void BuildFontWithWindows()
    {
        Debug.Log("开始打包UI Font");
        LayoutBundleTool buildRule = new LayoutBundleTool();
        buildRule.BuildFont(AssetBuildTool.BuildPlatformTarget);
        Debug.Log("结束打包UI Font");
    }
    public bool BuildFont(BuildTarget platform = BuildTarget.StandaloneWindows64)
    {
        AssetDatabase.Refresh();

        string FontBundlePath = AssetBuildTool.GetEditorBundlePath("/gui/Font/");     //输出包的路径

        //if (!Directory.Exists(FontBundlePath))
        //{
        //    Directory.CreateDirectory(FontBundlePath);
        //}

        // 字体文件太少，懒得前置做资源检测
        string fontPath = "Assets/_Resource/Prefabs/Font/";
        foreach (string file in Directory.GetFiles(fontPath))
        {
            if (file.EndsWith(".meta") || file.EndsWith(".png") || file.EndsWith(".mat") || file.EndsWith(".txt") || file.EndsWith(".asset"))   // 
                continue;
            string fnt = file.Replace(fontPath, "");
            fnt = fnt.Replace(".prefab", "");
            fnt = fnt.Replace("Font/", "");
            fnt = fnt.Replace(".ttf", "");
            if (file.Contains("ArtFont"))
            {
                bool haveBuilded;
                if (!BuildOne(new string[] { fontPath + fnt + ".prefab" }
                              , AssetBuildTool.GetBranchPath("/gui/Font/" + fnt + ".unity3d")
                              , AssetDatabase.LoadMainAssetAtPath(fontPath + fnt + ".prefab")
                              , null
                              , platform
                              , out haveBuilded
                              , false
                              , AssetBuildTool.buildBundlOptions))
                {
                    Debug.LogError(string.Format("Build {0} Error", file));
                }
                continue;
            }
            else if (file.EndsWith(".prefab"))
            {
                if (fnt.Contains("_bitmap"))
                {
                    string oriFontName = fnt.Replace("_bitmap", "");

                    //if (platform == BuildTarget.StandaloneWindows64)
                    //{
                    //    // 检测静态字体文本内容是否有改动
                    //    if (File.Exists(Application.dataPath + "/_Resource/Prefabs/Font/" + oriFontName + ".txt"))
                    //    {
                    //        if (!CheckVersion(new string[] { "Assets/_Resource/Prefabs/Font/" + oriFontName + ".txt" }))
                    //        {
                    //            Debug.Log(string.Format("Bitmap Font {0} txt file changed, re generate font", oriFontName));
                    //            GeneratedBitmap(oriFontName);
                    //            Debug.Log("Regenerate Font end");
                    //        }
                    //    }
                    //}

                    UIFont font = UnityInterfaceAdapter.LoadAssetAtPath<UIFont>(fontPath + fnt + ".prefab");

                    if (!File.Exists(fontPath + fnt + "_m.mat") || !File.Exists(fontPath + fnt + "_t.png"))
                    {
                        Debug.LogError(fnt + " material miss, while build uiatlas ");
                        return false;
                    }


                    List<Object> objList = new List<Object>();
                    UnityEngine.Object objPng = AssetDatabase.LoadMainAssetAtPath(fontPath + fnt + "_t.png");
                    UnityEngine.Object objMat = AssetDatabase.LoadMainAssetAtPath(fontPath + fnt + "_m.mat");
                    List<string> srcList = new List<string>();
                    srcList.Add(fontPath + fnt + "_t.png");
                    srcList.Add(fontPath + fnt + "_m.mat");
                    srcList.Add(fontPath + oriFontName + ".txt");
                    objList.Add(objPng);
                    objList.Add(objMat);

                    bool haveBuilded;
                    if (!BuildOne(srcList.ToArray()
                                  , AssetBuildTool.GetBranchPath("/gui/Font/" + fnt + ".unity3d")
                                  , AssetDatabase.LoadMainAssetAtPath(fontPath + fnt + ".prefab")
                                  , objList.ToArray()
                                  , platform
                                  , out haveBuilded
                                  , false
                                  , AssetBuildTool.buildBundlOptions))
                    {
                        Debug.LogError(string.Format("Build {0} Error", file));
                    }
                }
                else if (SDFFontBuildTool.IsSDFFontFile(fnt))
                {
                    string fontFullPath = string.Empty;
                    bool needRegen = false;
                    List<string> srcList = new List<string>();
                    List<Object> objList = new List<Object>();
                    string oriFontName = SDFFontBuildTool.GetRawName( fnt );

                    bool useDefaultTXT = false;
                    if (platform == BuildTarget.StandaloneWindows64)
                    {
                        string defaultTxtFileName = "semibold";
                        // 检测静态字体文本内容是否有改动
                        if (File.Exists(Application.dataPath + "/_Resource/Prefabs/Font/" + oriFontName + ".txt"))
                        {
                            if (!CheckVersion(new string[] { "Assets/_Resource/Prefabs/Font/" + oriFontName + ".txt" }))
                            {
                                Debug.Log(string.Format("Bitmap Font {0} txt file changed, re generate font", oriFontName));
                                needRegen = true;
                                Debug.Log("Regenerate Font end");
                            }
                        }
                        else if (File.Exists(Application.dataPath + "/_Resource/Prefabs/Font/" + defaultTxtFileName + ".txt"))
                        {
                            useDefaultTXT = true;
                            if (!CheckVersion(new string[] { "Assets/_Resource/Prefabs/Font/" + defaultTxtFileName + ".txt" }))
                            {
                                Debug.Log(string.Format("Bitmap Font {0} txt file changed, re generate font", defaultTxtFileName));
                                needRegen = true;
                                Debug.Log("Regenerate Font end");
                            }
                        }
                    }
                    SDFFontBuildTool.GenSDFFontBuildInfo(oriFontName, fontPath, needRegen, useDefaultTXT, ref fontFullPath, srcList, objList);

                    bool haveBuilded;
                    if (!BuildOne(srcList.ToArray()
                                  , "/gui/Font/" + fnt + ".unity3d"
                                  , AssetDatabase.LoadMainAssetAtPath(fontFullPath)//fontPath + helper.fontPrefabName
                                  , objList.ToArray()
                                  , platform
                                  , out haveBuilded
                                  , false
                                  , AssetBuildTool.buildBundlOptions))
                    {
                        Debug.LogError(string.Format("Build {0} Error", file));
                    }
                }
            }
            else
            {
                bool haveBuilded;
                BuildAssetBundleOptions buildFontBundlOptions = BuildAssetBundleOptions.CompleteAssets
                                                            | BuildAssetBundleOptions.DeterministicAssetBundle;
                if (!BuildOne(new string[] { fontPath + fnt + ".ttf" }
                              , AssetBuildTool.GetBranchPath("/gui/Font/" + fnt + ".unity3d")
                              , AssetDatabase.LoadMainAssetAtPath(fontPath + fnt + ".ttf")
                              , null
                              , platform
                              , out haveBuilded
                              , false
                              , buildFontBundlOptions))
                {
                    Debug.LogError(string.Format("Build {0} Error", file));
                }
            }
        }
        return true;
    }

    private void InitDynamicConfigPath()
    {
        string engineConfigPath = Application.dataPath + "/../StreamingAssets/scriptspak/engineconfig_debug.lua";
        engineConfigPath = engineConfigPath.Replace("/", "\\");
        if (File.Exists(engineConfigPath))
        {
            string engineConfig = File.ReadAllText(engineConfigPath.Replace("/", "\\"));
            string regStr = string.Format("lua_script_path\\s*=\\s*\\[\\[(.*)\\]\\],");
            Regex reg = new Regex(regStr);
            Match match = reg.Match(engineConfig);
            string luaPath = match.Groups[1].Value;
            if (Directory.Exists(luaPath))
            {
                DynamicLuaConfigPath = luaPath + "/config/dynamic_config";
                DynamicLuaConfigFile = luaPath + "/config/dynamic_config/dynamic_config.lua";
                DynamicLuaConfigPath = DynamicLuaConfigPath.Replace("\\", "/");
                DynamicLuaConfigFile = DynamicLuaConfigFile.Replace("\\", "/");
            }
        }
    }

    private bool GenDynamicConfig(string parentDir)
    {
        Debug.Log("begin GenDynamicConfig");
        Debug.Log("parentDir is " + parentDir);
        InitDynamicConfigPath();
        string outConfigPath = DynamicLuaConfigPath.Replace("/", "\\");        
        if (!Directory.Exists(outConfigPath))
        {
            Directory.CreateDirectory(outConfigPath);
        }
        string outConfigFile = DynamicLuaConfigFile.Replace("/", "\\");
        string configStr = string.Empty;
        if (File.Exists(outConfigFile))
        {
            configStr = File.ReadAllText(outConfigFile);
            File.Delete(outConfigFile);
        }

        File.Create(outConfigFile).Dispose();
        
        string iconPathStr = parentDir.Replace("\\", "/").Replace("Assets/_Resource/Textures/UI/Dynamic/", "");

        //string iconPath = Application.dataPath.Replace("Assets", parentDir).Replace("/", "\\");
        Debug.Log("iconPathStr is " + iconPathStr);

        string[] pngList = FileHelper.FindFileBySuffix(parentDir, ".png");
        System.Text.StringBuilder luaBuilder = new System.Text.StringBuilder();
        string configHeader = "local Config = {\n\n";
        luaBuilder.Append(configHeader);
        foreach (string pngFile in pngList)
        {
            string pngName = pngFile.Replace(parentDir + "/", "[").Replace(".png", "]");
            luaBuilder.Append("\t" + pngName + " = \"" + iconPathStr + "\",\n");
        }
        if (string.IsNullOrEmpty(configStr))
        {
            luaBuilder.Append("\n}\n");
            luaBuilder.Append("\nreturn Config\n\n");
        }
        else
        {
            string regStr = string.Format("\\t.*\"{0}\",\\n", iconPathStr);
            Regex reg = new Regex(regStr);
            string newConfigStr = reg.Replace(configStr, "");
            newConfigStr = newConfigStr.Remove(0, configHeader.Length);
            luaBuilder.Append(newConfigStr);
        }

        try
        {
            FileStream fs = new FileStream(outConfigFile, FileMode.Open);
            StreamWriter sw = new StreamWriter(fs);
            sw.Write(luaBuilder.ToString());
            sw.Flush();
            sw.Close();
            fs.Close();
        }
        catch (System.Exception ex)
        {
            Debug.LogError(string.Format("generateLuaConfig write error file[{0}]   error[{1}]", outConfigFile, ex.Message));
            return false;
        }
        return true;
    }

    private bool BuildTextureForDirectory(string parentDir, string outputBundlePath, BuildTarget platform, Dictionary<string, bool> textureList, bool rebuild)
    {
        //fix_me 挺简单的逻辑 没必要用递归吧 
        //       添加版本号判断也麻烦

        Debug.Log("Begin BuildTextureForDirectory");
        Debug.Log("parentDir is " + parentDir);
        Debug.Log("outputBundlePath is " + outputBundlePath);
        parentDir = parentDir.Replace('\\', '/');
        string bundlePath = AssetBuildTool.GetEditorBundlePath(outputBundlePath);
        List<string> pngList = new List<string>();
        List<Object> objList = new List<Object>();
        List<string> dirList = new List<string>();
        dirList.Add(parentDir);
        dirList.AddRange(Directory.GetDirectories(parentDir, "*", SearchOption.AllDirectories));
        bool lossless = false;
        foreach (string d in dirList)
        {
            if (!GenDynamicConfig(d))
            {
                Debug.LogError(string.Format("BuildTextureForDirectory {0} Error, GenDynamicConfig error", d));
                return false;
            }
            lossless = false;
            objList.Clear();
            pngList.Clear();
            string dir = d.Replace("\\", "/");
            string outPath = dir.Replace(parentDir, "");
            string bundleName = dir.Substring(dir.LastIndexOf('/') + 1, dir.Length - dir.LastIndexOf('/') - 1);
            if (!Directory.Exists(bundlePath + outPath))
            {
                Directory.CreateDirectory(bundlePath + outPath);
            }

            bool needToBuild = true;

            foreach (string png in Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                pngList.Add(png);
            }

            if (pngList.Count == 0)
            {
                Debug.Log("Directory [" + outPath + "] had no png file");
                continue;
            }

            if (!rebuild)
            {
                //无损的全部重打
                if (textureList.ContainsKey(dir))
                {
                    lossless = true;
                    needToBuild = true;

                }
                else if (CheckVersion(new string[] { dir }))
                {
                    AddToBundleList("/" + outputBundlePath + outPath + "/" + bundleName + ".unity3d");
                    Debug.Log("Directory [" + outPath + "] don't need to build");
                    needToBuild = false;
                    continue;
                }
            }
            else
            {
                if (textureList.ContainsKey(dir))
                {
                    lossless = true;
                    needToBuild = true;
                }
            }
            if (needToBuild)
            {
                foreach (string png in pngList)
                {
                    string pngPath = Application.dataPath + png.Replace("\\", "/").Replace("Assets", "");
                    //int quality, alphaQuality;
                    //AssetBase.GetWebpQualiltyByType("ui", out quality, out alphaQuality);
                    ConvertPngToWebp(pngPath, pngPath.Replace(".png", ".bytes"), lossless, 90, 80);

                }

                AssetDatabase.Refresh();
                foreach (string webp in Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
                {
                    objList.Add(AssetDatabase.LoadMainAssetAtPath(webp.Replace(".png", ".bytes")));
                }

                if (objList.Count > 0)
                {
                    bool haveBuilded;
                    if (!BuildOne(null
                                  , "/" + outputBundlePath + outPath + "/" + bundleName + ".unity3d"
                                  , objList[0]
                                  , objList.ToArray()
                                  , platform
                                  , out haveBuilded
                                  , true))
                    {
                        Debug.LogError(string.Format("Build {0} Error", bundlePath + outPath + "/" + bundleName + ".unity3d"));
                        return false;
                    }
                }

            }
        }
        Debug.Log("End BuildTextureForDirectory");
        return true;
    }
    public bool BuildDynamic(BuildTarget platform, bool rebuild = false)
    {
        string parentDir = "Assets/_Resource/Textures/UI/Dynamic/";
        string outputPath = "gui/Dynamic/";
        bool result = BuildTextureForDirectory(parentDir, outputPath, platform, DynamicList, rebuild);
        return result;
    }

   // public static void BuildOneDynamic(string dir, Dictionary<string, bool> iconlosslessList)
   // {
   //     string outputPath = dir.Replace("Assets/_Resource/Textures/UI/Dynamic/", "gui/Dynamic/");

   //     LayoutBundleTool layout = new LayoutBundleTool();
   //     if (!layout.GenDynamicConfig(dir))
   //     {
   //         Debug.LogError(string.Format("BuildOneDynamic {0} Error, GenDynamicConfig error", dir));
   //         return;
   //     }

   //     layout.IconList = iconlosslessList;
   //     if (layout.BuildTextureForDirectory(dir, outputPath, AssetBuildTool.BuildPlatformTarget, iconlosslessList, false))
   //     {
   //         foreach (string jpg in Directory.GetFiles(dir, "*.jpg", SearchOption.AllDirectories))
   //         {
   //             File.Delete(jpg);
   //         }
   //         foreach (string bytes in Directory.GetFiles(dir, "*.bytes", SearchOption.AllDirectories))
   //         {
   //             File.Delete(bytes);
   //         }
   //         string subDir = dir.Replace("Assets/_Resource/Textures/UI/Dynamic/", "");
   //         string iconDir = Application.dataPath.Replace("/", "\\").Replace("Assets", UnityInterfaceAdapter.GetStreamingAssets()) + "\\assetbundle\\windows\\gui\\Dynamic\\" + subDir;
   //         string[] files = Directory.GetFiles(iconDir, "*.unity3d", SearchOption.AllDirectories);
			//string[] finalFiles = new string[files.Length + 1];
   //         finalFiles[0] = iconDir;
   //         int i = 1;
   //         foreach (string temp in files)
   //         {
   //             finalFiles[i] = temp;
   //             ++i;
   //         }
			//SVNUtils.AddFiles(files);
   //         SVNUtils.CommitFiles(finalFiles, "commit dynamic");

   //         files = new string[] { layout.DynamicLuaConfigFile.Replace("/", "\\") };
			//SVNUtils.AddFiles(files);
   //         SVNUtils.CommitFiles(files, "commit dynamic lua config");
   //     }

   //     AssetDatabase.Refresh();

   // }

    public bool BuildIcon(string parentDir, BuildTarget platform, bool rebuild = false)
    {
        //  挺简单的逻辑 没必要用递归吧 
        //       添加版本号判断也麻烦

        parentDir = parentDir.Replace('\\', '/');
        
        string bundlePath = AssetBuildTool.GetEditorBundlePath("/gui/Icon/");   //打包后的ab包存放路径

        List<string> pngList = new List<string>();
        List<Object> objList = new List<Object>();
        List<string> dirList = new List<string>();
        dirList.Add(parentDir);
        dirList.AddRange(Directory.GetDirectories(parentDir, "*", SearchOption.AllDirectories));
        bool lossless = false;
        foreach (string d in dirList)
        {
            lossless = false;
            objList.Clear();
            pngList.Clear();
            string dir = d.Replace("\\", "/");
            string outPath = dir.Replace("Assets/_Resource/Textures/UI/Icon/", "");
            string bundleName = dir.Substring(dir.LastIndexOf('/') + 1, dir.Length - dir.LastIndexOf('/') - 1);
            //if (!Directory.Exists(bundlePath + outPath))
            //{
            //    Directory.CreateDirectory(bundlePath + outPath);
            //}

            bool needToBuild = true;

            foreach (string png in Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
            {
                pngList.Add(png);
            }

            if (pngList.Count == 0)
            {
                Debug.Log("Icon [" + outPath + "] had no png file");
                continue;
            }

            if (!rebuild)
            {
                //无损的全部重打
                if (IconList.ContainsKey(dir))
                {
                    lossless = true;
                    needToBuild = true;

                }else if (CheckVersion(new string[] { dir }))
                {
                    AddToBundleList("/gui/Icon/" + outPath + "/" + bundleName + ".unity3d");
                    Debug.Log("Icon [" + outPath + "] don't need to build");
                    //return true;
                    needToBuild = false;
                    continue;
                }
            }else
            {
                if (IconList.ContainsKey(dir))
                {
                    lossless = true;
                    needToBuild = true;
                }
            }

            if (needToBuild)
            {
                foreach (string png in pngList)
                {
                    string pngPath = Application.dataPath + png.Replace("\\", "/").Replace("Assets", "");
                    //int quality, alphaQuality;
                    //AssetBase.GetWebpQualiltyByType("ui", out quality, out alphaQuality);
                    ConvertPngToWebp(pngPath, pngPath.Replace(".png", ".bytes"), lossless, 90, 80);
              
                }

                AssetDatabase.Refresh();
                foreach (string webp in Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly))
                {
                    objList.Add(AssetDatabase.LoadMainAssetAtPath(webp.Replace(".png", ".bytes")));
                }

                if (objList.Count > 0)
                {
                    bool haveBuilded;
                    if (!BuildOne(null
                                  , AssetBuildTool.GetBranchPath("/gui/Icon/" + outPath + "/" + bundleName + ".unity3d")
                                  , objList[0]
                                  , objList.ToArray()
                                  , platform
                                  , out haveBuilded
                                  , true))
                    {
                        Debug.LogError(string.Format("Build {0} Error", bundlePath + outPath + "/" + bundleName + ".unity3d"));
                        return false;
                    }
                }

            }
        }

        return true;
    }


    public static void BuildOneIcon(string dir, Dictionary<string, bool> iconlosslessList)
    {

        LayoutBundleTool layout = new LayoutBundleTool();
        layout.IconList = iconlosslessList;
        if (layout.BuildIcon(dir, AssetBuildTool.BuildPlatformTarget, true))
        {
            foreach (string jpg in Directory.GetFiles(dir, "*.jpg", SearchOption.AllDirectories))
            {
                File.Delete(jpg);
            }
            foreach (string bytes in Directory.GetFiles(dir, "*.bytes", SearchOption.AllDirectories))
            {
                File.Delete(bytes);
            }
            string subDir = dir.Replace("Assets/_Resource/Textures/UI/Icon/", "");
            string iconDir = Application.dataPath.Replace("/", "\\").Replace("Assets", UnityInterfaceAdapter.GetStreamingAssets()) + "\\assetbundle\\windows\\gui\\Icon\\" + subDir;
            string[] files = Directory.GetFiles(iconDir, "*.unity3d", SearchOption.AllDirectories);
			string[] finalFiles = new string[files.Length + 1];
            finalFiles[0] = iconDir;
            int i = 1;
            foreach (string temp in files)
            {
                finalFiles[i] = temp;
                ++i;
            }
			SVNUtils.AddFiles(files);
            SVNUtils.CommitFiles(finalFiles, "commit icon");
        }

        AssetDatabase.Refresh();
    }

    public static void GeneratedBitmap(string fontName)
    {
        UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath("Assets/_Resource/Prefabs/Font/" + fontName + ".ttf");
        if (obj == null)
            obj = AssetDatabase.LoadMainAssetAtPath("Assets/_Resource/Font/" + fontName + ".ttf"); 
        Font ttf = obj as Font;
        if(ttf == null)
        {
            Debug.Log("orignal TTF Font " + fontName + ".ttf is not exist or breoken! can't regenerate font");
            return;
        }
        UIFont uiFont = null; ;
        string RESPATH = "Assets/_Resource/Prefabs/";

        string prefabPath = RESPATH + "Font/" + fontName + "_bitmap.prefab";
        if (File.Exists(prefabPath))
        {
            File.Delete(prefabPath);
        }

        GameObject go = null;
        UnityEngine.Object prefab = null;
        string fontTextPath = RESPATH + "Font/" + fontName + ".txt";

        StreamReader sr = new StreamReader(fontTextPath, Encoding.UTF8);
        string fontText = sr.ReadToEnd();

        string final = "";

        for (int i = 0; i < fontText.Length; ++i)
        {
            char c = fontText[i];
            if (c < 33)
            {
                continue;
            }
                
            string s = c.ToString();
            if (!final.Contains(s)) final += s;
        }


        char[] chars = final.ToCharArray();
        System.Array.Sort(chars);
        final = new string(chars);
        fontText = final;
        sr.Close();

        // Create a new prefab for the atlas
        prefab = PrefabUtility.CreateEmptyPrefab(prefabPath);

        // Create a new game object for the font
        go = new GameObject(fontName);
        uiFont = go.AddComponent<UIFont>();


        BMFont bmFont;
        Texture2D tex;
        if(!File.Exists(NGUISettings.pathToFreeType))
        {
            EditorUtility.DisplayDialog("错误", "找不到文件:" + NGUISettings.pathToFreeType, "确定");
            return;
        }
        FreeType.LoadLibrary(NGUISettings.pathToFreeType);
        int fontSize = EditorPrefs.GetInt("BitMapFontSize", 26);
        if (FreeType.CreateFont(
            ttf,
            fontSize, 0,
            true,
            fontText, 1, out bmFont, out tex))
        {
            uiFont.bmFont = bmFont;
            tex.name = fontName;

            string texPath = prefabPath.Replace(".prefab", "_t.png");
            string matPath = prefabPath.Replace(".prefab", "_m.mat");

            byte[] png = tex.EncodeToPNG();
            FileStream fs = File.OpenWrite(texPath);
            fs.Write(png, 0, png.Length);
            fs.Close();

            // See if the material already exists
            Material mat = AssetDatabase.LoadAssetAtPath(matPath, typeof(Material)) as Material;

            // If the material doesn't exist, create it
            if (mat == null)
            {
                Shader shader = Shader.Find("Unlit/BMFont Text");
                mat = new Material(shader);

                // Save the material
                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // Load the material so it's usable
                mat = AssetDatabase.LoadAssetAtPath(matPath, typeof(Material)) as Material;
            }
            else AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Re-load the texture
            tex = AssetDatabase.LoadAssetAtPath(texPath, typeof(Texture2D)) as Texture2D;

            // Assign the texture
            mat.mainTexture = tex;

            uiFont.atlas = null;
            uiFont.material = mat;

        }

        PrefabUtility.ReplacePrefab(go, prefab);
        SaveAndRefreshAssets();
    }




    struct SpriteRes
    {
        public string Hierarchy;
        public string Atlas;
        public string Sprite;
    }

    struct TextureRes
    {
        public string Hierarchy;
        public string TexturePath;
        public string TextureName;
    }

    class LayoutSpriteResInfo
    {
        public List<SpriteRes> uiSprites = new List<SpriteRes>();
        public List<TextureRes> uiTextures = new List<TextureRes>();
    }

    static Dictionary<string, LayoutSpriteResInfo> resInfos = new Dictionary<string, LayoutSpriteResInfo>();

    private static string GetPanelResHierarchy(GameObject obj)
    {
        var hierarchy = NGUITools.GetHierarchy(obj).Replace("\\", "/");
        hierarchy = hierarchy.Substring(hierarchy.IndexOf("/") + 1);
        return hierarchy;
    }

    /// <summary>
    /// 导出图集映射文件
    /// </summary>
    public static void ExportAtlasMapping()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows
    && EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            return;


        Debug.Log("Start Export Atlas");
        resInfos.Clear();

        List<string> LayoutList = new List<string>();
        string LayoutPath = "Assets/_Resource/Prefabs/UI/";
        foreach (string file in FileHelper.FindFileBySuffix(LayoutPath, ".prefab"))
        {
            LayoutList.Add(file);
        }
        LayoutList.Sort();

        foreach (string layout in LayoutList)
        {
            string layoutName = Path.GetFileNameWithoutExtension(layout);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(layout);

            var resInfo = new LayoutSpriteResInfo();
            resInfos.Add(layoutName, resInfo);


            var spriteList = prefab.GetComponentsInChildren<UISprite>(true);
            foreach (var s in spriteList)
            {
                resInfo.uiSprites.Add(new SpriteRes()
                {
                    Hierarchy = GetPanelResHierarchy(s.gameObject),
                    Atlas = s.AtlasName,
                    Sprite = s.spriteName,
                });
            }


            var textureList = prefab.GetComponentsInChildren<UITexture>(true);
            foreach (var t in textureList)
            {
                resInfo.uiTextures.Add(new TextureRes()
                {
                    Hierarchy = GetPanelResHierarchy(t.gameObject),
                    TexturePath = t.TexturePath,
                    TextureName = t.TextureName,
                });
            }
        }

        StringBuilder luaBuilder = new StringBuilder();

        luaBuilder.Append("local Config = {\n\n");

        foreach (var res in resInfos)
        {
            // panle name
            luaBuilder.AppendFormat("\t[\"{0}\"] = {{\n", res.Key);


            // sprite list
            luaBuilder.Append("\t\t[\"Sprites\"] = {\n");
            foreach (var s in res.Value.uiSprites)
            {
                luaBuilder.AppendFormat("\t\t\t[\"{0}\"] = {{ \"{1}&&{2}\" }},\n", s.Hierarchy, s.Atlas, s.Sprite);
            }
            luaBuilder.Append("\t\t},\n");


            //texture list
            luaBuilder.Append("\t\t[\"Textures\"] = {\n");
            foreach (var t in res.Value.uiTextures)
            {
                luaBuilder.AppendFormat("\t\t\t[\"{0}\"] = {{ \n\t\t\t\tTexturePath = \"{1}\", \n\t\t\t\tTextureName = \"{2}\", \n\t\t\t}},\n", t.Hierarchy, t.TexturePath, t.TextureName);
            }
            luaBuilder.Append("\t\t},\n");

            luaBuilder.Append("\t},\n\n");
        }

        luaBuilder.Append("\n}\n return Config");

        try
        {
            FileStream fs = new FileStream(UILayoutResInfoConfig, FileMode.Create);
            StreamWriter sw = new StreamWriter(fs);
            sw.Write(luaBuilder.ToString());
            sw.Flush();
            sw.Close();
            fs.Close();
        }
        catch (System.Exception ex)
        {
            Debug.LogError(string.Format("generateLuaConfig write error file[{0}]   error[{1}]", UILayoutResInfoConfig, ex.Message));
        }
    }
}


