using System;
using System.IO;
using BuildForge.Runtime;
using UnityEngine;

public static class VariantProofRuntime
{
    [Serializable] sealed class Result
    {
        public bool developmentBuild;
        public bool developmentDefine;
        public bool regionChina;
        public bool unityDevelopmentDefine;
        public string productName;
        public string variant;
        public bool manifestDevelopment;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--proofOutput");
        if (index < 0 || index + 1 >= args.Length) return;
        var manifest = BuildManifestLoader.Load();
        var result = new Result { developmentBuild = Debug.isDebugBuild, productName = Application.productName,
            variant = manifest.BuildVariant, manifestDevelopment = manifest.DevelopmentBuild };
#if DEVELOPMENT
        result.developmentDefine = true;
#endif
#if REGION_CHINA
        result.regionChina = true;
#endif
#if DEVELOPMENT_BUILD
        result.unityDevelopmentDefine = true;
#endif
        File.WriteAllText(args[index + 1], JsonUtility.ToJson(result, true));
        Application.Quit(0);
    }
}
