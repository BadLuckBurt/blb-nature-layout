using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallConnect;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility.AssetInjection;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class BLBNature : ITerrainNature 
{
    public BLBNature(float globalScaleModifier, float[] treeScale, float[] bushScale, float[] rockScale) {
        globalScale = globalScaleModifier;
        treesRandomScale = treeScale;
        bushesRandomScale = bushScale;
        rocksRandomScale = rockScale;

        //Load natureAtlases here
        int atlasMaxSize = getAtlasMaxSize();
        int[] natureArchives = new int[]{500,501,502,503,504,505,506,507,508,509,510,511};
        Material natureAtlas;
        Rect[] rectsOut;
        RecordIndex[] indicesOut;
        for(int i = 0; i < natureArchives.Length; i++) {
            natureAtlas = GetMaterialAtlas(natureArchives[i], 0, 4, atlasMaxSize, out rectsOut, out indicesOut, 4, true, 0, false, true);       
            atlasRects.Add(natureArchives[i], rectsOut);
            atlasIndices.Add(natureArchives[i], indicesOut);
            natureAtlases.Add(natureArchives[i], natureAtlas);
        }
    }
    private float globalScale;
    protected const float maxSteepness = 45f;             // 50
    protected const float slopeSinkRatio = 70f;           // Sink flats slightly into ground as slope increases to prevent floaty trees.
    protected const float baseChanceOnDirt = 0.2f;        // 0.2
    protected const float baseChanceOnGrass = 0.9f;       // 0.4
    protected const float baseChanceOnStone = 0.05f;      // 0.05
    protected const int natureClearance = 1;    
    public bool NatureMeshUsed { get; protected set; } // Flag to signal use of meshes

    const int seed = 417028;

    private Dictionary<int, Material> natureAtlases = new Dictionary<int, Material>();
    private Dictionary<int, Rect[]> atlasRects = new Dictionary<int, Rect[]>();
    private Dictionary<int, RecordIndex[]> atlasIndices = new Dictionary<int, RecordIndex[]>();
    private float MinRandomScale = 1f;
    private float MaxRandomScale = 1f;
    private float[] treesRandomScale = new float[]{2f, 4f};
    private float[] bushesRandomScale = new float[]{1f, 2f};
    private float[] rocksRandomScale = new float[]{1f, 2f};
    private bool grassEnabled = true;
    private bool grassEnabled2 = true;
    public virtual void LayoutNature(DaggerfallTerrain dfTerrain, DaggerfallBillboardBatch dfBillboardBatch, float terrainScale, int terrainDist)
    {
        //dfBillboardBatch.MinRandomScale = 0.875f;
        //dfBillboardBatch.MaxRandomScale = 1.875f;
        //dfBillboardBatch.RandomSeed = (dfTerrain.MapPixelX * 1000) + dfTerrain.MapPixelY;
        //dfBillboardBatch.SetMaterial(504, true);
        //return;
        float averageHeight = dfTerrain.MapData.averageHeight;
        // Location Rect is expanded slightly to give extra clearance around locations
        Rect rect = dfTerrain.MapData.locationRect;
        if (rect.x > 0 && rect.y > 0)
        {
            rect.xMin -= natureClearance;
            rect.xMax += natureClearance;
            rect.yMin -= natureClearance;
            rect.yMax += natureClearance;
        }
        // Chance scaled based on map pixel height
        // This tends to produce sparser lowlands and denser highlands
        // Adjust or remove clamp range to influence nature generation
        float elevationScale = (dfTerrain.MapData.worldHeight / 128f);
        elevationScale = Mathf.Clamp(elevationScale, 0.4f, 1.0f);

        float denseProbability = 0.75f;
        float sparseProbability = 0.4f;

        int maxCount = 16000; //Max number of billboards per map pixel
        
        int denseCount = maxCount / 4; //Number of billboards per quadrant when dense
        int averageCount = denseCount / 2; //Number of billboards per quadrant when average
        int sparseCount = averageCount / 4; //Number of billboards per quadrant when sparse

        // Chance scaled by base climate type
        float chanceOnDirt = baseChanceOnDirt;
        float chanceOnGrass = baseChanceOnGrass;
        float chanceOnStone = baseChanceOnStone;
        float chanceOnWater = 0.0f;
        DFLocation.ClimateSettings climate = MapsFile.GetWorldClimateSettings(dfTerrain.MapData.worldClimate);
        //GetTextureResults natureArchive = natureAtlases[(int)climate.NatureSet];
        int natureArchive = climate.NatureArchive;

        if(DaggerfallUnity.Instance.WorldTime.Now.SeasonValue == DaggerfallDateTime.Seasons.Winter) {
            natureArchive = GetWinterArchive(natureArchive);
        }
        CachedMaterial cm = GetMaterialFromCache(natureArchive);
        dfBillboardBatch.SetMaterial(cm.material);

        GameObject grassGO;
        GameObject grassGO2;
        DaggerfallBillboardBatch grassBatch = null;
        DaggerfallBillboardBatch grassBatch2 = null;

        if(grassEnabled) {
            grassGO = GetGrassObject(dfTerrain, "GrassBillboards");
            Debug.Log("Got grass object");
            GameManager.Instance.StreamingWorld.TrackLooseObject(grassGO, false, dfTerrain.MapPixelX, dfTerrain.MapPixelY, false);
            grassBatch = grassGO.GetComponent<DaggerfallBillboardBatch>();
            grassBatch.SetMaterial(cm.material);
            grassBatch.TextureArchive = dfBillboardBatch.TextureArchive;
        }
        if(grassEnabled2) {
            grassGO2 = GetGrassObject(dfTerrain, "GrassBillboards2");
            GameManager.Instance.StreamingWorld.TrackLooseObject(grassGO2, false, dfTerrain.MapPixelX, dfTerrain.MapPixelY, false);
            grassBatch2 = grassGO2.GetComponent<DaggerfallBillboardBatch>();
            grassBatch2.SetMaterial(cm.material);
            grassBatch2.TextureArchive = dfBillboardBatch.TextureArchive;
        }

        //Rect[] rects = atlasRects[(int)climate.NatureSet];
        
        // Get terrain
        Terrain terrain = dfTerrain.gameObject.GetComponent<Terrain>();
        if (!terrain)
            return;

        // Get terrain data
        TerrainData terrainData = terrain.terrainData;
        if (!terrainData)
            return;

        // Remove exiting billboards
        dfBillboardBatch.Clear();
        MeshReplacement.ClearNatureGameObjects(terrain);

        // Seed UnityEngine.Random with terrain key
        UnityEngine.Random.InitState(TerrainHelper.MakeTerrainKey(dfTerrain.MapPixelX, dfTerrain.MapPixelY));
        
        bool inSwamp = false;

        switch (climate.ClimateType)
        {
            case DFLocation.ClimateBaseType.Swamp:
                denseProbability = UnityEngine.Random.Range(0.66f,0.99f);
                sparseProbability = UnityEngine.Random.Range(0.11f, 0.22f);
                //denseProbability = 0.75f;
                //sparseProbability = 0.25f;
                denseCount = maxCount / 4;
                averageCount = denseCount / 2;
                sparseCount = averageCount / 4;
                chanceOnDirt = 0.375f;
                chanceOnGrass = 0.75f;
                chanceOnStone = 0.125f;
                chanceOnWater = 0.25f;
                inSwamp = true;
                break;
            case DFLocation.ClimateBaseType.Desert:         // Just lower desert for now
                denseProbability = UnityEngine.Random.Range(0.97f,0.99f);
                sparseProbability = UnityEngine.Random.Range(0.55f, 0.69f);
                //denseProbability = 0.8f;
                //sparseProbability = 0.35f;
                denseCount = maxCount / 8;
                averageCount = denseCount / 4;
                sparseCount = averageCount / 2;
                chanceOnDirt = 0.25f;
                chanceOnGrass = 0.45f;
                chanceOnStone = 0.25f;
                break;
            case DFLocation.ClimateBaseType.Mountain:
                denseProbability = UnityEngine.Random.Range(0.75f,0.99f);
                sparseProbability = UnityEngine.Random.Range(0.15f, 0.55f);
                //denseProbability = 0.4f;
                //sparseProbability = 0.125f;
                denseCount = maxCount / 4;
                averageCount = denseCount / 2;
                sparseCount = averageCount / 4;
                chanceOnDirt = 0.5f;
                chanceOnGrass = 0.85f;
                chanceOnStone = 0.6f;
                break;
            case DFLocation.ClimateBaseType.Temperate:
                denseProbability = UnityEngine.Random.Range(0.75f,1.0f);
                sparseProbability = UnityEngine.Random.Range(0.15f, 0.55f);
                chanceOnDirt = 0.35f;
                chanceOnGrass = 0.85f;
                chanceOnStone = 0.35f;
                break;
        }

        int[] rocks;
        int[] plants;
        int[] trees;
        switch(climate.NatureSet) 
        {
            case DFLocation.ClimateTextureSet.Nature_Desert:
                rocks = desertRocks;
                plants = desertPlants;
                trees = desertTrees;
                break;
            case DFLocation.ClimateTextureSet.Nature_HauntedWoodlands:
            case DFLocation.ClimateTextureSet.Nature_HauntedWoodlands_Snow:
                rocks = hauntedRocks;
                plants = hauntedPlants;
                trees = hauntedTrees;
                break;
            case DFLocation.ClimateTextureSet.Nature_Mountains:
            case DFLocation.ClimateTextureSet.Nature_Mountains_Snow:
                rocks = mountainRocks;
                plants = mountainPlants;
                trees = mountainTrees;
                break;
            case DFLocation.ClimateTextureSet.Nature_RainForest:
                rocks = rainforestRocks;
                plants = rainforestPlants;
                trees = rainforestTrees;
                break;
            case DFLocation.ClimateTextureSet.Nature_SubTropical:
                rocks = subtropicalRocks;
                plants = subtropicalPlants;
                trees = subtropicalTrees;
                break;
            case DFLocation.ClimateTextureSet.Nature_Swamp:
                rocks = swampRocks;
                plants = swampPlants;
                trees = swampTrees;
                break;
            case DFLocation.ClimateTextureSet.Nature_TemperateWoodland:
            case DFLocation.ClimateTextureSet.Nature_TemperateWoodland_Snow:
                rocks = temperateRocks;
                plants = temperatePlants;
                trees = temperateTrees;
                break;
            case DFLocation.ClimateTextureSet.Nature_WoodlandHills:
            case DFLocation.ClimateTextureSet.Nature_WoodlandHills_Snow:
                rocks = woodlandRocks;
                plants = woodlandPlants;
                trees = woodlandTrees;
                break;
            default:
                rocks = temperateRocks;
                plants = temperatePlants;
                trees = temperateTrees;
                break;
        }

        int rocksIndex = 0;
        int plantsIndex = 0;
        int treesIndex = 0;
        SetStepValues(rocks.Length, plants.Length, trees.Length);

        // Just layout some random flats spread evenly across entire map pixel area
        // Flats are aligned with tiles, max 16129 billboards per batch
        Vector2 tilePos = Vector2.zero;
        int tDim = MapsFile.WorldMapTileDim;
        int hDim = DaggerfallUnity.Instance.TerrainSampler.HeightmapDimension;
        float scale = terrainData.heightmapScale.x * (float)hDim / (float)tDim;
        float maxTerrainHeight = DaggerfallUnity.Instance.TerrainSampler.MaxTerrainHeight;
        float beachLine = DaggerfallUnity.Instance.TerrainSampler.BeachElevation;
        float oceanLine = DaggerfallUnity.Instance.TerrainSampler.OceanElevation;

        int latitude;
        int longitude;
        
        float offsetY = 0.0f;
        float offsetX = 0.0f;
        int counter = 0;
        int placementAllowed = 0;

        int countPerRow = 16;
        float increment = 1.0f;

        float previousNoise = 0.0f;
        float noiseQuadrant = 0.0f;
        bool resetNoise = true;
        int quadrantCounter = 0;
        
        int sampleX = 0;
        int sampleY = 0;

        while(offsetY < 127.9f) {
            while(offsetX < 127.9f) {
                if(resetNoise) {
                    //Quadrant 1
                    if(offsetY < 64.0f && offsetX < 64.0f) {
                        sampleX = 32;
                        sampleY = 32;

                        quadrantCounter = 1;
                    //Quadrant 2
                    } else if(offsetY < 64.0f) {
                        sampleX = 96;
                        sampleY = 32;

                        quadrantCounter = 2;
                    //Quadrant 3
                    } else if(offsetY < 127.9f && offsetX < 64.0f) {
                        sampleX = 32;
                        sampleY = 96;

                        quadrantCounter = 3;
                        //Quadrant 4
                    } else {
                        sampleX = 96;
                        sampleY = 96;

                        quadrantCounter = 4;
                    }
                    sampleX = 32;
                    sampleY = 32;
                    latitude = (int)(dfTerrain.MapPixelX * MapsFile.WorldMapTileDim + sampleX);
                    longitude = (int)(MapsFile.MaxWorldTileCoordZ - dfTerrain.MapPixelY * MapsFile.WorldMapTileDim + sampleY);
                    previousNoise = noiseQuadrant;
                    noiseQuadrant = NoiseWeight(latitude, longitude);
                    if(previousNoise >= denseProbability && ((previousNoise + noiseQuadrant) / 2.25f) >= denseProbability) {
                        noiseQuadrant = previousNoise;
                    }

                    if(noiseQuadrant >= denseProbability) {
                        countPerRow = Mathf.FloorToInt(Mathf.Sqrt((float)denseCount));
                    } else if(noiseQuadrant <= sparseProbability) {
                        countPerRow = Mathf.FloorToInt(Mathf.Sqrt((float)sparseCount));
                    } else {
                        countPerRow = Mathf.FloorToInt(Mathf.Sqrt((float)averageCount));
                    }
                    //countPerRow = 4;
                    increment = 64 / countPerRow;
                    resetNoise = false;
                }
                //Added by BadLuckBurt, randomizes the flat position within the tile
                float posX = ((offsetX - UnityEngine.Random.Range(increment / 2, increment)) * scale);
                float posZ = ((offsetY - UnityEngine.Random.Range(increment / 2, increment)) * scale);
                //End added by BadLuckBurt

                // Reject if inside location rect (expanded slightly to give extra clearance around locations)
                tilePos.x = Mathf.FloorToInt(offsetX);
                tilePos.y = Mathf.FloorToInt(offsetY);
                if (rect.x > 0 && rect.y > 0 && rect.Contains(tilePos)) {
                    offsetX += increment;
                    continue;
                }

                float steepness = terrainData.GetSteepness(posX / tDim, posZ / tDim);
                if (steepness < maxSteepness) {
                    placementAllowed = 0;
                    int middleX = Mathf.Max(0, Mathf.FloorToInt(offsetX));
                    int middleY = Mathf.Max(0, Mathf.FloorToInt(offsetY));
                    
                    // Chance also determined by tile type
                    int tile = checkTiles(middleX, middleY, ref dfTerrain.MapData.tilemapSamples);
                    if (tile == 1)
                    {   // Dirt
                        if (UnityEngine.Random.Range(0f, 1f) <= chanceOnDirt) {
                            placementAllowed = 1;
                        }
                            
                    }
                    else if (tile == 2)
                    {   // Grass
                        if (UnityEngine.Random.Range(0f, 1f) <= chanceOnGrass) {
                            placementAllowed = 2;
                        }
                            
                    }
                    else if (tile == 3)
                    {   // Stone
                        if (UnityEngine.Random.Range(0f, 1f) <= chanceOnStone) {
                            placementAllowed = 3;
                        }
                            
                    } else if(tile == 0 && (climate.ClimateType == DFLocation.ClimateBaseType.Swamp && climate.WorldClimate != 223)) 
                    {   //Water - swamp only
                        if (UnityEngine.Random.Range(0f, 1f) <= chanceOnWater) {
                            placementAllowed = 4;
                        }
                    }

                    //placementAllowed = true;
                    if(placementAllowed > 0) {
                        int hx = (int)Mathf.Clamp(hDim * (offsetX / (float)tDim), 0, hDim - 1);
                        int hy = (int)Mathf.Clamp(hDim * (offsetY / (float)tDim), 0, hDim - 1);
                        float height = dfTerrain.MapData.heightmapSamples[hy, hx] * maxTerrainHeight;  // x & y swapped in heightmap for TerrainData.SetHeights()

                        // Reject if too close to water
                        if (height >= beachLine || placementAllowed == 4 || inSwamp) {        
                            // Sample height and position billboard
                            Vector3 pos = new Vector3(posX, 0, posZ);
                            float height2 = terrain.SampleHeight(pos + terrain.transform.position);
                            height2 = CapHeight(height2);
                            pos.y = height2 - (steepness / slopeSinkRatio);

                            Vector3 grassPos = new Vector3(offsetX, 0, offsetY);
                            height2 = terrain.SampleHeight(grassPos + terrain.transform.position);
                            height2 = CapHeight(height2);
                            grassPos.y = height2 - (steepness / slopeSinkRatio);

                            int record = UnityEngine.Random.Range(1, 32);
                            float placementWeight = UnityEngine.Random.Range(0f, 1f);
                            // Add to batch unless a mesh replacement is found
                            int placedObjectType = 0;
                            if (placementAllowed == 1) { //Dirt - allow all / favour plants, rocks
                                if(placementWeight < 0.25f) {
                                    record = trees[treesIndex];
                                    placedObjectType = 1;
                                    treesIndex = IncreaseIndex(treesIndex, treesStep, trees.Length);
                                } else if(placementWeight > 0.5f) {
                                    record = plants[plantsIndex];
                                    placedObjectType = 2;
                                    plantsIndex = IncreaseIndex(plantsIndex, plantsStep, plants.Length);
                                } else {
                                    record = rocks[rocksIndex];
                                    placedObjectType = 3;
                                    rocksIndex = IncreaseIndex(rocksIndex, rocksStep, rocks.Length);
                                }
                            } else if (placementAllowed == 2) { //Grass - allow all / favour trees
                                if(placementWeight < 0.125f) {
                                    record = rocks[rocksIndex];
                                    placedObjectType = 3;
                                    rocksIndex = IncreaseIndex(rocksIndex, rocksStep, rocks.Length);
                                } else if(placementWeight > 0.6f) {
                                    record = trees[treesIndex];
                                    placedObjectType = 1;
                                    treesIndex = IncreaseIndex(treesIndex, treesStep, trees.Length);
                                } else {
                                    record = plants[plantsIndex];
                                    placedObjectType = 2;
                                    plantsIndex = IncreaseIndex(plantsIndex, plantsStep, plants.Length);
                                }
                            } else if (placementAllowed == 3) { //Stone - only allow rocks and plants
                                if(placementWeight <= 0.65f) {
                                    record = rocks[rocksIndex];
                                    placedObjectType = 3;
                                    rocksIndex = IncreaseIndex(rocksIndex, rocksStep, rocks.Length);
                                } else {
                                    record = plants[plantsIndex];
                                    placedObjectType = 2;
                                    plantsIndex = IncreaseIndex(plantsIndex, plantsStep, plants.Length);
                                }
                            } else if(placementAllowed == 4) { //Swamp water - only allow plants and trees
                                if(placementWeight <= 0.65f) {
                                    record = plants[plantsIndex];
                                    placedObjectType = 2;
                                    plantsIndex = IncreaseIndex(plantsIndex, plantsStep, plants.Length);
                                } else {
                                    record = trees[treesIndex];
                                    placedObjectType = 1;
                                    treesIndex = IncreaseIndex(treesIndex, treesStep, trees.Length);
                                }
                            }
                            setRandomScale(placedObjectType);
                            if (terrainDist > 1 || !MeshReplacement.ImportNatureGameObject(dfBillboardBatch.TextureArchive, record, terrain, Mathf.FloorToInt(offsetX), Mathf.FloorToInt(offsetY))) {
                                //dfBillboardBatch.AddItem(record, pos);
                                //Debug.Log("Billboard dimensions: " + cm.recordSizes[record].x.ToString() + " " + cm.recordSizes[record].y.ToString());
                                float randomScale = UnityEngine.Random.Range(MinRandomScale, MaxRandomScale);
                                Vector2 billboardScale = new Vector2((int)cm.recordScales[record].x * randomScale, (int)cm.recordScales[record].y * randomScale);
                                dfBillboardBatch.AddItem(atlasRects[dfBillboardBatch.TextureArchive][record], new Vector2(cm.recordSizes[record].x, cm.recordSizes[record].y), billboardScale, pos);
                                
                                if(grassEnabled && grassBatch != null) {
                                    //record = 0;
                                    //billboardScale = new Vector2((int)cm.recordScales[record].x, (int)cm.recordScales[record].y);
                                    //grassBatch.AddItem(atlasRects[grassBatch.TextureArchive][record], new Vector2(cm.recordSizes[record].x, cm.recordSizes[record].y), billboardScale, pos);
                                }

                                counter++;
                            } else if (!NatureMeshUsed) {
                                NatureMeshUsed = true;  // Signal that nature mesh has been used to initiate extra terrain updates
                            }
                        }
                    }
                }
                offsetX += increment;
                //Resets noise when re-entering the loop
                if(offsetY < 64.0f && offsetX < 64.0f && quadrantCounter != 1) {
                    resetNoise = true;
                }
                //Set noise to quadrant 2
                else if(offsetY < 64.0f && offsetX >= 64.0f && quadrantCounter != 2) {
                    resetNoise = true;
                //Set noise to quadrant 3
                } else if(offsetY >= 64.0f && offsetX < 64.0f && quadrantCounter != 3) {
                    resetNoise = true;
                //Set noise to quadrant 4
                } else if(offsetY >= 64.0f && offsetX >= 64.0f && quadrantCounter != 4) {
                    resetNoise = true;
                }
            }
            offsetX = 0.0f;
            offsetY += increment;
        }

        // Apply new batch
        dfBillboardBatch.Apply();
        if(grassEnabled || grassEnabled2) {
            float grassChance1 = 0.8f;
            float grassChance2 = 0.8f;
            float chance = 1f;
            float grassSlope = 30.0f;
            for(int y = 0; y < 128; y++) {
                float posZ = (y * scale) + UnityEngine.Random.Range(0f, scale / 2);
                float posZ2 = (y * scale) + UnityEngine.Random.Range(scale / 2, scale);
                for(int x = 0; x < 128; x++) {
                    float posX = (x * scale) + UnityEngine.Random.Range(0f, scale / 2);
                    float posX2 = (x * scale) + UnityEngine.Random.Range(scale / 2, scale);

                    tilePos.x = x;
                    tilePos.y = y;

                    int tile = checkTiles(x, y, ref dfTerrain.MapData.tilemapSamples);
                    if(tile != 2) {
                        continue;
                    }

                    //if (rect.x > 0 && rect.y > 0 && rect.Contains(tilePos)) {
                        //continue;
                    //}

                    float randomScale = 0.25f;//UnityEngine.Random.Range(0.5f, 1f);
                    float steepness = terrainData.GetSteepness(posX / tDim, posZ / tDim);
                    if(steepness <= grassSlope) {
                        Vector3 pos = new Vector3(posX, 0, posZ);
                        float height = terrain.SampleHeight(pos + terrain.transform.position);
                        Vector2 billboardScale = new Vector2((int)cm.recordScales[0].x * randomScale, (int)cm.recordScales[0].y * randomScale);

                        if(grassEnabled) {
                            chance = UnityEngine.Random.Range(0.0f, 1.0f);
                            if(chance <= grassChance1) {                            
                                pos.y = CapHeight(height) - (steepness / slopeSinkRatio);
                                grassBatch.AddItem(atlasRects[grassBatch.TextureArchive][0], new Vector2(cm.recordSizes[0].x, cm.recordSizes[0].y), billboardScale, pos);
                            }
                        }

                        if(grassEnabled2) {
                            chance = UnityEngine.Random.Range(0.0f, 1.0f);
                            if(chance <= grassChance2) {
                                pos = new Vector3(posX2, 0, posZ2);
                                height = terrain.SampleHeight(pos + terrain.transform.position);
                                pos.y = CapHeight(height) - (steepness / slopeSinkRatio);
                                billboardScale = new Vector2((int)cm.recordScales[0].x * randomScale, (int)cm.recordScales[0].y * randomScale);
                                grassBatch2.AddItem(atlasRects[grassBatch2.TextureArchive][0], new Vector2(cm.recordSizes[0].x, cm.recordSizes[0].y), billboardScale, pos);
                            }
                        }
                    }
                }
            }

            //if(AddGrass(ref grassBatch, ref terrain, ref terrainData, ref cm, 0.0f)) {
                if(grassEnabled) {
                    grassBatch.Apply();
                }
                if(grassEnabled2) {
                    grassBatch2.Apply();
                }
            //}
        }
    }

    private GameObject GetGrassObject(DaggerfallTerrain dfTerrain, string name) {
        Transform t = dfTerrain.gameObject.transform;
        GameObject grassGO = null;
        for (int i = 0; i < t.childCount; i++) 
        {
            if(t.GetChild(i).gameObject.name == name)
            {
                grassGO = t.GetChild(i).gameObject;
                break;
                //t.GetChild(i).gameObject.SetActive(false);
                //GameObject.Destroy(t.GetChild(i).gameObject);
                //return t.GetChild(i).gameObject;
            }
        }
        if(grassGO == null) {
            grassGO = new GameObject();
            grassGO.name = name;
            grassGO.AddComponent<DaggerfallBillboardBatch>();
        }
        grassGO.transform.parent = dfTerrain.gameObject.transform;
        grassGO.transform.localPosition = Vector3.zero;
        return grassGO;
    }

    private bool AddGrass(ref DaggerfallBillboardBatch grassBatch, ref Terrain terrain, ref TerrainData terrainData, ref CachedMaterial cm, float offset = 0.0f) {
        int tDim = MapsFile.WorldMapTileDim;
        float posX, posZ;
        for(int y = 0; y < 127; y++) {
            posZ = (float) y + UnityEngine.Random.Range(0.0f, offset);
            for(int x = 0; x < 127; x++) {
                posX = (float) x + UnityEngine.Random.Range(0.0f, offset);
                float steepness = terrainData.GetSteepness(posX / tDim, posZ / tDim);
                Vector3 pos = new Vector3(posX, 0, posZ);
                float height = terrain.SampleHeight(pos + terrain.transform.position);
                pos.y = CapHeight(height) - (steepness / slopeSinkRatio);
                Vector2 billboardScale = new Vector2((int)cm.recordScales[0].x, (int)cm.recordScales[0].y);
                grassBatch.AddItem(atlasRects[grassBatch.TextureArchive][0], new Vector2(cm.recordSizes[0].x, cm.recordSizes[0].y), billboardScale, pos);
            }
        }
        return true;
    }

    private int GetWinterArchive(int archive) {
        switch(archive) {
            case 504:
            case 506:
            case 508:
            case 510:
                return archive + 1;
            default:
                return archive;
        }
    }
    private void setRandomScale(int objectType) {
        if(objectType == 1) {
            MinRandomScale = treesRandomScale[0];
            MaxRandomScale = treesRandomScale[1];
        } else if(objectType == 2) {
            MinRandomScale = bushesRandomScale[0];
            MaxRandomScale = bushesRandomScale[1];
        } else if(objectType == 3) {
            MinRandomScale = rocksRandomScale[0];
            MaxRandomScale = rocksRandomScale[1];
        }
    }

    private static int checkTiles(int middleX, int middleY, ref byte[,] tileMapSamples) {
        int west = Mathf.Max(0, middleX - 1);
        int east = Mathf.Min(127, middleX + 1);
        int north = Mathf.Max(0, middleY - 1);
        int south = Mathf.Min(127, middleY + 1);

        int tile = tileMapSamples[middleX, north] & 0x3F;
        if(isRoadTile(tile)) {
            return tile;
        }
        tile = tileMapSamples[east, north] & 0x3F;
        if(isRoadTile(tile)) {
            return tile;
        }
        tile = tileMapSamples[east, middleY] & 0x3F;
        if(isRoadTile(tile)) {
            return tile;
        }
        tile = tileMapSamples[east, south] & 0x3F;
        if(isRoadTile(tile)) {
            return tile;
        }
        tile = tileMapSamples[middleX, south] & 0x3F;
        if(isRoadTile(tile)) {
            return tile;
        }
        tile = tileMapSamples[west, south] & 0x3F;
        if(isRoadTile(tile)) {
            return tile;
        }
        tile = tileMapSamples[west, middleY] & 0x3F;
        if(isRoadTile(tile)) {
            return tile;
        }
        tile = tileMapSamples[west, north] & 0x3F;
        if(isRoadTile(tile)) {
            return tile;
        }
        return tileMapSamples[middleX, middleY] & 0x3F;
    }

    private static bool isRoadTile(int tile) {
        int[] roadTiles = new int[]{46, 47, 55};
        return Array.Exists( roadTiles, element => element == tile);
    }

    private static float CapHeight(float height) {
        float stepSize = 2560;
        float step = 1 / stepSize;
        float newHeight = (Mathf.Floor(height / step) * step) - (step / 2);
        return newHeight;
    }

    private float NoiseWeight(float worldX, float worldY)
    {
        float frequency = 0.05f;
        float amplitude = 0.9f;
        float persistance = 0.4f;
        int octaves = 1;
        return GetNoise(worldX, worldY, frequency, amplitude, persistance, octaves, seed);
    }

    private float GetNoise(
        float x,
        float y,
        float frequency,
        float amplitude,
        float persistance,
        int octaves,
        int seed = 0)
    {
        float finalValue = 0f;
        for (int i = 0; i < octaves; ++i)
        {
            finalValue += Mathf.PerlinNoise(seed + (x * frequency), seed + (y * frequency)) * amplitude;
            frequency *= 2.0f;
            amplitude *= persistance;
        }

        return Mathf.Clamp(finalValue, -1f, 1f);
    }
    private int rocksStep = 2;
    private int plantsStep = 2;
    private int treesStep = 2;

    private void SetStepValues( int rocks, int plants, int trees) 
    {
        if (rocks % 2 == 0) {
            rocksStep = 3;
        } else {
            rocksStep = 2;
        }

        if (plants % 2 == 0) {
            plantsStep = 3;
        } else {
            plantsStep = 2;
        }

        if (trees % 2 == 0) {
            treesStep = 3;
        } else {
            treesStep = 2;
        }
    }

    private int IncreaseIndex(int index, int stepValue, int length) 
    {
        int result = index + stepValue;
        return ClampIndex(result, length);
    }

    private int ClampIndex(int index, int length) 
    {
        if (index >= length) {
            return index - length;
        }
        return index;
    }

    Dictionary<int, CachedMaterial> materialDict = new Dictionary<int, CachedMaterial>();
    /// <summary>
    /// Gets Unity Material atlas from Daggerfall texture archive.
    /// </summary>
    /// <param name="archive">Archive index to create atlas from.</param>
    /// <param name="alphaIndex">Index to receive transparent alpha.</param>        
    /// <param name="padding">Number of pixels each sub-texture.</param>
    /// <param name="maxAtlasSize">Max size of atlas.</param>
    /// <param name="rectsOut">Array of rects, one for each record sub-texture and frame.</param>
    /// <param name="indicesOut">Array of record indices into rect array, accounting for animation frames.</param>
    /// <param name="border">Number of pixels internal border around each texture.</param>
    /// <param name="dilate">Blend texture into surrounding empty pixels.</param>
    /// <param name="shrinkUVs">Number of pixels to shrink UV rect.</param>
    /// <param name="copyToOppositeBorder">Copy texture edges to opposite border. Requires border, will overwrite dilate.</param>
    /// <param name="isBillboard">Set true when creating atlas material for simple billboards.</param>
    /// <returns>Material or null.</returns>
    public Material GetMaterialAtlas(
        int archive,
        int alphaIndex,
        int padding,
        int maxAtlasSize,
        out Rect[] rectsOut,
        out RecordIndex[] indicesOut,
        int border = 0,
        bool dilate = false,
        int shrinkUVs = 0,
        bool copyToOppositeBorder = false,
        bool isBillboard = false)
    {
        int key = archive;
        if (materialDict.ContainsKey(key))
        {
            CachedMaterial cm = GetMaterialFromCache(key);
            if (cm.filterMode == DaggerfallUnity.Instance.MaterialReader.MainFilterMode)
            {
                // Properties are the same
                rectsOut = cm.atlasRects;
                indicesOut = cm.atlasIndices;
                return cm.material;
            }
            else
            {
                // Properties don't match, remove material and reload
                materialDict.Remove(key);
            }
        }

        TextureReader tr = DaggerfallUnity.Instance.MaterialReader.TextureReader;
        // Create material
        Material material;
        if (isBillboard)
            material = MaterialReader.CreateBillboardMaterial();
        else
            material = MaterialReader.CreateDefaultMaterial();

        // Create settings
        GetTextureSettings settings = TextureReader.CreateTextureSettings(archive, 0, 0, alphaIndex, border, dilate);
        settings.createNormalMap = false;
        settings.autoEmission = false;
        settings.atlasShrinkUVs = shrinkUVs;
        settings.atlasPadding = padding;
        settings.atlasMaxSize = maxAtlasSize;
        settings.copyToOppositeBorder = copyToOppositeBorder;
        settings.stayReadable = true;

        // Setup material
        material.name = string.Format("TEXTURE.{0:000} [Atlas]", archive);
        settings.atlasMaxSize = 4096;
        MaterialReader mr = DaggerfallUnity.Instance.MaterialReader;
        GetTextureResults results = GetTexture2DAtlasReplacement(settings, mr.AlphaTextureFormat);

        material.mainTexture = results.albedoMap;
        material.mainTexture.filterMode = mr.MainFilterMode;

        // Setup normal map
        /*if (GenerateNormals && results.normalMap != null)
        {
            results.normalMap.filterMode = mr.MainFilterMode;
            material.SetTexture(Uniforms.BumpMap, results.normalMap);
            material.EnableKeyword(KeyWords.NormalMap);
        }*/

        // Setup emission map
        /*if (results.isEmissive && results.emissionMap != null)
        {
            results.emissionMap.filterMode = mr.MainFilterMode;
            material.SetTexture(Uniforms.EmissionMap, results.emissionMap);
            material.SetColor(Uniforms.EmissionColor, Color.white);
            material.EnableKeyword(KeyWords.Emission);
        }*/

        // TEMP: Bridging between legacy material out params and GetTextureResults for now
        Vector2[] sizesOut, scalesOut, offsetsOut;
        sizesOut = results.atlasSizes.ToArray();
        scalesOut = results.atlasScales.ToArray();
        offsetsOut = results.atlasOffsets.ToArray();
        rectsOut = results.atlasRects.ToArray();
        indicesOut = results.atlasIndices.ToArray();

        // Setup cached material
        CachedMaterial newcm = new CachedMaterial();
        newcm.key = key;
        newcm.keyGroup = MaterialReader.AtlasKeyGroup;
        newcm.atlasRects = rectsOut;
        newcm.atlasIndices = indicesOut;
        newcm.material = material;
        newcm.filterMode = mr.MainFilterMode;
        newcm.recordSizes = sizesOut;
        newcm.recordScales = scalesOut;
        newcm.recordOffsets = offsetsOut;
        newcm.atlasFrameCounts = results.atlasFrameCounts.ToArray();
        materialDict.Add(key, newcm);

        return material;
    }

    public GetTextureResults GetTexture2DAtlasReplacement(
        GetTextureSettings settings,
        SupportedAlphaTextureFormats alphaTextureFormat = SupportedAlphaTextureFormats.ARGB32)
    {
        GetTextureResults results = new GetTextureResults();
        int recordCount = 32;

        var textureImport = TextureImport.None;

        TextureReader tr = DaggerfallUnity.Instance.MaterialReader.TextureReader;
        // Assign texture file
        /*TextureFile textureFile;
        if (settings.textureFile == null)
        {
            textureFile = new TextureFile(Path.Combine(tr.Arena2Path, TextureFile.IndexToFileName(settings.archive)), FileUsage.UseMemory, true);
            settings.textureFile = textureFile;
        }
        else
            textureFile = settings.textureFile;
        */
        Texture2D[] textures = new Texture2D[recordCount];
        Texture2D texture = null;
        string[] guids;
        bool textureFound;
        for(int i = 0; i < recordCount; i++) {
            settings.record = i;
            textureFound = false;
            #if UNITY_EDITOR
                guids = AssetDatabase.FindAssets(settings.archive.ToString() + "_" + i.ToString() + "-0");
                if(guids.Length > 0) {
                    textureFound = true;
                    texture = (Texture2D)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), typeof(Texture2D));
                }
            #else
            if(TextureReplacement.TryImportTexture(settings.archive, i, 0, out texture)) {
                textureFound = true;
            }
            #endif

            if(textureFound == false) {
                settings.stayReadable = true;
                GetTextureResults textureResults = tr.GetTexture2D(settings, alphaTextureFormat, textureImport);
                texture = textureResults.albedoMap;
            }
            textures[i] = texture;
        }

        // Create lists
        results.atlasSizes = new List<Vector2>(recordCount);
        results.atlasScales = new List<Vector2>(recordCount);
        results.atlasOffsets = new List<Vector2>(recordCount);
        results.atlasFrameCounts = new List<int>(recordCount);

        // Read every texture in archive
        //bool hasNormalMaps = false;
        bool hasEmissionMaps = false;
        bool hasAnimation = false;

        List<Texture2D> albedoTextures = new List<Texture2D>();
        List<Texture2D> normalTextures = new List<Texture2D>();
        List<Texture2D> emissionTextures = new List<Texture2D>();
        List<RecordIndex> indices = new List<RecordIndex>();

        bool globalMipmaps = true;
        if (DaggerfallUnity.Settings.RetroRenderingMode > 0 && !DaggerfallUnity.Settings.UseMipMapsInRetroMode)
            globalMipmaps = false;
        
        for (int record = 0; record < recordCount; record++)
        {
            // Get record index and frame count
            //settings.record = record;
            int frames = 1;
            if (frames > 1)
                hasAnimation = true;

            XMLManager flatXML;
            float scaleX = 1.0f, scaleY = 1.0f;
            if(XMLManager.TryReadXml("", settings.archive.ToString() + "_" + record.ToString() + "-0", out flatXML)) {
                flatXML.TryGetFloat("scaleX", out scaleX);
                flatXML.TryGetFloat("scaleY", out scaleY);
                scaleX = Mathf.Abs(scaleX);
                scaleY = Mathf.Abs(scaleY);
                //results.atlasScales.Add(new Vector2((scaleX * globalScale), (scaleY * globalScale)));
                //Debug.Log("Found scale " + scaleX.ToString() + " - " + scaleY.ToString());
            }

            // Get record information
            DFSize size = new DFSize(Mathf.FloorToInt(textures[record].width * scaleX), Mathf.FloorToInt(textures[record].height * scaleY));
            DFPosition offset = new DFPosition(0, 0);
            RecordIndex ri = new RecordIndex()
            {
                startIndex = albedoTextures.Count,
                frameCount = frames,
                width = size.Width,
                height = size.Height,
            };
            indices.Add(ri);
            albedoTextures.Add(textures[record]);
            /*
            for (int frame = 0; frame < frames; frame++)
            {
                //settings.frame = frame;
                //GetTextureResults nextTextureResults = GetTexture2D(settings, alphaTextureFormat, textureImport);

                //albedoTextures.Add(textures[record]);
                if (nextTextureResults.normalMap != null)
                {
                    if (nextTextureResults.normalMap.width != nextTextureResults.albedoMap.width || nextTextureResults.normalMap.height != nextTextureResults.albedoMap.height)
                    {
                        Debug.LogErrorFormat("The size of atlased normal map for {0}-{1} must be equal to the size of main texture.", settings.archive, settings.record);
                        nextTextureResults.normalMap = nextTextureResults.albedoMap;
                    }

                    normalTextures.Add(nextTextureResults.normalMap);
                    hasNormalMaps = true;
                }
                if (nextTextureResults.emissionMap != null)
                {
                    if (nextTextureResults.emissionMap.width != nextTextureResults.albedoMap.width || nextTextureResults.emissionMap.height != nextTextureResults.albedoMap.height)
                    {
                        Debug.LogErrorFormat("The size of atlased emission map for {0}-{1} must be equal to the size of main texture.", settings.archive, settings.record);
                        nextTextureResults.emissionMap = nextTextureResults.albedoMap;
                    }

                    emissionTextures.Add(nextTextureResults.emissionMap);
                    hasEmissionMaps = true;
                }
            }
            */

            results.atlasSizes.Add(new Vector2(size.Width, size.Height));
            results.atlasOffsets.Add(new Vector2(offset.X, offset.Y));
            results.atlasScales.Add(new Vector2((BlocksFile.ScaleDivisor * globalScale), (BlocksFile.ScaleDivisor * globalScale)));
            results.atlasFrameCounts.Add(frames);
            //results.textureFile = textureFile;
        }

        // Pack albedo textures into atlas and get our rects
        Texture2D atlasAlbedoMap = new Texture2D(settings.atlasMaxSize, settings.atlasMaxSize, ParseTextureFormat(alphaTextureFormat), globalMipmaps);
        Rect[] rects = atlasAlbedoMap.PackTextures(albedoTextures.ToArray(), settings.atlasPadding, settings.atlasMaxSize, false);

        // Pack normal textures into atlas
        Texture2D atlasNormalMap = null;
        /*if (hasNormalMaps)
        {
            // Normals must be ARGB32
            atlasNormalMap = new Texture2D(settings.atlasMaxSize, settings.atlasMaxSize, TextureFormat.ARGB32, MipMaps);
            atlasNormalMap.PackTextures(normalTextures.ToArray(), settings.atlasPadding, settings.atlasMaxSize, !stayReadable);
        }*/

        // Pack emission textures into atlas
        // TODO: Change this as packing not consistent
        Texture2D atlasEmissionMap = null;
        /*if (hasEmissionMaps)
        {
            // Repacking to ensure correct mix of lit and unlit
            atlasEmissionMap = new Texture2D(settings.atlasMaxSize, settings.atlasMaxSize, ParseTextureFormat(alphaTextureFormat), MipMaps);
            atlasEmissionMap.PackTextures(emissionTextures.ToArray(), settings.atlasPadding, settings.atlasMaxSize, !stayReadable);
        }*/

        // Add to results
        if (results.atlasRects == null) results.atlasRects = new List<Rect>(rects.Length);
        if (results.atlasIndices == null) results.atlasIndices = new List<RecordIndex>(indices.Count);
        results.atlasRects.AddRange(rects);
        results.atlasIndices.AddRange(indices);
        // Shrink UV rect to compensate for internal border
        float ru = 1f / atlasAlbedoMap.width;
        float rv = 1f / atlasAlbedoMap.height;
        int finalBorder = settings.borderSize + settings.atlasShrinkUVs;
        for (int i = 0; i < results.atlasRects.Count; i++)
        {
            Rect rct = results.atlasRects[i];
            rct.xMin += finalBorder * ru;
            rct.xMax -= finalBorder * ru;
            rct.yMin += finalBorder * rv;
            rct.yMax -= finalBorder * rv;
            results.atlasRects[i] = rct;
        }

        // Store results
        results.albedoMap = atlasAlbedoMap;
        results.normalMap = atlasNormalMap;
        results.emissionMap = atlasEmissionMap;
        results.isAtlasAnimated = hasAnimation;
        results.isEmissive = hasEmissionMaps;

        return results;
    }

    private CachedMaterial GetMaterialFromCache(int key)
    {
        //Debug.Log("Cache key: " + key.ToString());
        CachedMaterial cachedMaterial = materialDict[key];

        // Update timestamp of last access, but only if difference is
        // significant to limit the number of reassignment to dictionary.
        float time = Time.realtimeSinceStartup;
        if (time - cachedMaterial.timeStamp > 59)
        {
            cachedMaterial.timeStamp = time;
            materialDict[key] = cachedMaterial;
        }

        return cachedMaterial;
    }
    private TextureFormat ParseTextureFormat(SupportedAlphaTextureFormats format)
    {
        switch (format)
        {
            default:
            case SupportedAlphaTextureFormats.RGBA32:
                return TextureFormat.RGBA32;
            case SupportedAlphaTextureFormats.ARGB32:
                return TextureFormat.ARGB32;
            case SupportedAlphaTextureFormats.ARGB444:
                return TextureFormat.ARGB4444;
            case SupportedAlphaTextureFormats.RGBA444:
                return TextureFormat.RGBA4444;
        }
    }
    private int getAtlasMaxSize() {
        return DaggerfallUnity.Settings.AssetInjection ? 4096 : 2048;
    }
    private GetTextureSettings createTextureSettings(int archive, int alphaIndex = 0, int padding = 4, int border = 0, bool dilate = false, bool generateNormals = false, int shrinkUVs = 0, bool copyToOppositeBorder = false, bool isBillboard = true) {
        int maxAtlasSize = getAtlasMaxSize();
        // Create settings
        GetTextureSettings settings = TextureReader.CreateTextureSettings(archive, 0, 0, alphaIndex, border, dilate, maxAtlasSize);
        settings.createNormalMap = generateNormals;
        settings.autoEmission = true;
        settings.atlasShrinkUVs = shrinkUVs;
        settings.atlasPadding = padding;
        settings.atlasMaxSize = maxAtlasSize;
        settings.copyToOppositeBorder = copyToOppositeBorder;
        return settings;
    }

    private int[] temperateRocks = new int[]{3, 4, 5, 6};
    private int[] temperatePlants = new int[]{1, 2, 7, 8, 9, 10, 19, 20, 21, 22, 23, 26, 27, 28, 29, 31};
    private int[] temperateTrees = new int[]{11, 12, 13, 14, 15, 16, 17, 18, 24, 25, 30};

    private int[] mountainRocks = new int[]{1, 3, 4, 6, 14, 17, 18, 27, 28};
    private int[] mountainPlants = new int[]{2, 7, 8, 9, 10, 16, 19, 20, 22, 23, 26, 29, 31};
    private int[] mountainTrees = new int[]{5, 11, 12, 13, 15, 21, 24, 25, 30};

    private int[] swampRocks = new int[]{2, 3, 4, 5, 6, 10};
    private int[] swampPlants = new int[]{1, 7, 8, 9, 11, 14, 19, 20, 21, 22, 23, 25, 26, 27, 28, 29, 31};
    private int[] swampTrees = new int[]{12, 13, 15, 16, 17, 18, 24, 30};

    private int[] desertRocks = new int[]{2, 3, 4, 9, 18, 19, 20, 21, 22, 24};
    private int[] desertPlants = new int[]{1, 6, 7, 8, 10, 17, 23, 25, 26, 27, 29, 31};
    private int[] desertTrees = new int[]{6, 11, 12, 13, 14, 15, 16, 28, 30};

    private int[] rainforestRocks = new int[]{4, 17, 28, 29};
    private int[] rainforestPlants = new int[]{1, 2, 5, 6, 7, 8, 9, 10, 11, 19, 20, 21, 22, 23, 24, 25, 26, 27, 31};
    private int[] rainforestTrees = new int[]{3, 12, 13, 14, 15, 16, 18, 30};

    private int[] subtropicalRocks = new int[]{3, 4, 5, 6, 10, 23};
    private int[] subtropicalPlants = new int[]{1, 2, 7, 8, 9, 14, 18, 19, 20, 21, 22, 24, 25, 26, 28, 29, 31};
    private int[] subtropicalTrees = new int[]{11, 12, 13, 15, 16, 17, 27, 30};

    private int[] woodlandRocks = new int[]{1, 3, 4, 6, 17, 18, 27, 28};
    private int[] woodlandPlants = new int[]{2, 7, 8, 9, 10, 19, 20, 21, 22, 23, 26, 29, 31};
    private int[] woodlandTrees = new int[]{5, 11, 12, 13, 14, 15, 16, 24, 25, 30};

    private int[] hauntedRocks = new int[]{1, 3, 4, 5, 6, 11, 12};
    private int[] hauntedPlants = new int[]{2, 7, 8, 9, 10, 12, 14, 17, 19, 20, 21, 22, 23, 25, 26, 27, 28, 29};
    private int[] hauntedTrees = new int[]{13, 15, 16, 24, 30};
}
