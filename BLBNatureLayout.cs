using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility.ModSupport;   //required for modding features
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop;

public class BLBNatureLayout : MonoBehaviour
{
    public static Mod Mod {
        get;
        private set;
    }

    private ModSettings modSettings;

    public static BLBNatureLayout Instance { get; private set; }

    [Invoke(StateManager.StateTypes.Start, 0)]
    public static void Init(InitParams initParams)
    {
        Mod = initParams.Mod;  // Get mod     
        Debug.Log("Got Mod object");
        var go = new GameObject(Mod.Title);
        Debug.Log("Created GameObject");
        Instance = go.AddComponent<BLBNatureLayout>();
        Debug.Log("Set instance");
    }

    void Awake ()
    {
        Debug.Log("blb-nature-layout awakened");
        Debug.Log("BLB: Processing mod settings");

        Debug.Log("Setting mod settings property");
        modSettings = Mod.GetSettings();

        DaggerfallWorkshop.Utility.Tuple<float, float> tmpScale = modSettings.GetTupleFloat("RandomScales", "GlobalScale");
        float globalScale = tmpScale.First;

        tmpScale = modSettings.GetTupleFloat("RandomScales", "TreeScale");
        float[] treeRandomScale = new float[2];
        treeRandomScale[0] = tmpScale.First;
        treeRandomScale[0] = tmpScale.Second;

        tmpScale = modSettings.GetTupleFloat("RandomScales", "BushScale");
        float[] bushRandomScale = new float[2];
        bushRandomScale[0] = tmpScale.First;
        bushRandomScale[0] = tmpScale.Second;

        tmpScale = modSettings.GetTupleFloat("RandomScales", "RockScale");
        float[] rockRandomScale = new float[2];
        rockRandomScale[0] = tmpScale.First;
        rockRandomScale[0] = tmpScale.Second;

        Debug.Log("BLB: Adding instance of BLBNature");
        DaggerfallUnity.Instance.TerrainNature = new BLBNature(globalScale, treeRandomScale, bushRandomScale, rockRandomScale);
        Mod.IsReady = true;
    }

}