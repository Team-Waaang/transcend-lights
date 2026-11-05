using System;
using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.ARCore;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
namespace PhonePointer.Editor
{
    // Explicit opt-in. Merely importing the package changes no consumer project settings.
    public static class AndroidSetup
    {
        [MenuItem("Phone Pointer/Android/Apply ARCore build settings")]
        public static void Configure()
        {
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.Portrait;
            PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android,ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});
            PlayerSettings.Android.forceInternetPermission=true;
            ConfigureXr(); AssetDatabase.SaveAssets();
            Debug.Log("Android ARCore/ARM64/IL2CPP settings applied. Choose your own product name and build scenes.");
        }
        static void EnsureAssetFolder(string folder)
        {
            folder=folder.Replace('\\','/');
            if(AssetDatabase.IsValidFolder(folder)) return;
            var parent=Path.GetDirectoryName(folder).Replace('\\','/');
            if(parent!="Assets" && !AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
            var absolute=Path.GetFullPath(Path.Combine(Application.dataPath,"..",folder));
            if(Directory.Exists(absolute))
            {
                AssetDatabase.ImportAsset(folder,ImportAssetOptions.ForceSynchronousImport);
                if(AssetDatabase.IsValidFolder(folder)) return;
            }
            AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));
        }
        public static void ConfigureXr()
        {
            EnsureAssetFolder("Assets/PhonePointer/Settings");
            const string path="Assets/PhonePointer/Settings/XRGeneralSettings.asset";
            EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey,out var all);
            if(all==null) all=AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if(all==null) { all=ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>(); AssetDatabase.CreateAsset(all,path); }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey,all,true);
            var general=all.SettingsForBuildTarget(BuildTargetGroup.Android);
            if(general==null)
            {
                general=ScriptableObject.CreateInstance<XRGeneralSettings>(); general.name="Android XR";
                AssetDatabase.AddObjectToAsset(general,all); all.SetSettingsForBuildTarget(BuildTargetGroup.Android,general);
            }
            if(general.Manager==null)
            { var manager=ScriptableObject.CreateInstance<XRManagerSettings>(); manager.name="Android XR Manager"; AssetDatabase.AddObjectToAsset(manager,all); general.Manager=manager; }
            general.InitManagerOnStart=true;
            if(!XRPackageMetadataStore.AssignLoader(general.Manager,"UnityEngine.XR.ARCore.ARCoreLoader",BuildTargetGroup.Android))
                throw new InvalidOperationException("Could not enable Android ARCore loader.");
            var settings=ARCoreSettings.currentSettings;
            if(settings==null) { settings=ScriptableObject.CreateInstance<ARCoreSettings>(); AssetDatabase.CreateAsset(settings,"Assets/PhonePointer/Settings/ARCoreSettings.asset"); ARCoreSettings.currentSettings=settings; }
            settings.requirement=ARCoreSettings.Requirement.Required;
            settings.depth=ARCoreSettings.Requirement.Optional;
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(general); EditorUtility.SetDirty(general.Manager); EditorUtility.SetDirty(all);
        }
    }
}
