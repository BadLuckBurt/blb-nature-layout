using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility.ModSupport;   //required for modding features
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings; //required for mod settings
using DaggerfallWorkshop;

public class BLBNatureLayout : MonoBehaviour
{
    public static Mod Mod {
        get;
        private set;
    }

    public static BLBNatureLayout Instance { get; private set; }

    [Invoke(StateManager.StateTypes.Start, 0)]
    public static void Init(InitParams initParams)
    {
        Mod = initParams.Mod;  // Get mod     
        var go = new GameObject(Mod.Title);
        Instance = go.AddComponent<BLBNatureLayout>();
        Debug.Log("BLB: Adding instance of BLBNature");
        DaggerfallUnity.Instance.TerrainNature = new BLBNature();
    }

    void Awake ()
    {
        Debug.Log("blb-nature-layout awakened");
        Mod.IsReady = true;
    }
}