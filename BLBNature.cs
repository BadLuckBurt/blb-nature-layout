using UnityEngine;
using System;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility.AssetInjection;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;

public class BLBNature : ITerrainNature 
{
        protected const float maxSteepness = 45f;             // 50
        protected const float slopeSinkRatio = 70f;           // Sink flats slightly into ground as slope increases to prevent floaty trees.
        protected const float baseChanceOnDirt = 0.2f;        // 0.2
        protected const float baseChanceOnGrass = 0.9f;       // 0.4
        protected const float baseChanceOnStone = 0.05f;      // 0.05
        protected const int natureClearance = 1;    
        public bool NatureMeshUsed { get; protected set; } // Flag to signal use of meshes

        const int seed = 417028;

        public virtual void LayoutNature(DaggerfallTerrain dfTerrain, DaggerfallBillboardBatch dfBillboardBatch, float terrainScale, int terrainDist)
        {
            //dfBillboardBatch.MinRandomScale = 1f;
            //dfBillboardBatch.MaxRandomScale = 1f;
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

            int maxCount = 16384; //Max number of billboards per map pixel
            
            int denseCount = maxCount / 4; //Number of billboards per quadrant when dense
            int averageCount = denseCount / 2; //Number of billboards per quadrant when average
            int sparseCount = averageCount / 4; //Number of billboards per quadrant when sparse

            // Chance scaled by base climate type
            float chanceOnDirt = baseChanceOnDirt;
            float chanceOnGrass = baseChanceOnGrass;
            float chanceOnStone = baseChanceOnStone;
            float chanceOnWater = 0.0f;
            DFLocation.ClimateSettings climate = MapsFile.GetWorldClimateSettings(dfTerrain.MapData.worldClimate);
            
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
                        int west = Mathf.Max(0, middleX - 1);
                        int east = Mathf.Min(127, middleX + 1);
                        int middleY = Mathf.Max(0, Mathf.FloorToInt(offsetY));
                        int north = Mathf.Max(0, middleY - 1);
                        int south = Mathf.Min(127, middleY + 1);
                        // Chance also determined by tile type
                        int tile = checkTiles(middleX, middleY, west, east, north, south, ref dfTerrain.MapData.tilemapSamples);
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
                                int record = UnityEngine.Random.Range(1, 32);
                                float placementWeight = UnityEngine.Random.Range(0f, 1f);
                                // Add to batch unless a mesh replacement is found
                                if (placementAllowed == 1) { //Dirt - allow all / favour plants, rocks
                                    if(placementWeight < 0.25f) {
                                        record = trees[treesIndex];
                                        treesIndex = IncreaseIndex(treesIndex, treesStep, trees.Length);
                                    } else if(placementWeight > 0.5f) {
                                        record = plants[plantsIndex];
                                        plantsIndex = IncreaseIndex(plantsIndex, plantsStep, plants.Length);
                                    } else {
                                        record = rocks[rocksIndex];
                                        rocksIndex = IncreaseIndex(rocksIndex, rocksStep, rocks.Length);
                                    }
                                } else if (placementAllowed == 2) { //Grass - allow all / favour trees
                                    if(placementWeight < 0.125f) {
                                        record = rocks[rocksIndex];
                                        rocksIndex = IncreaseIndex(rocksIndex, rocksStep, rocks.Length);
                                    } else if(placementWeight > 0.6f) {
                                        record = trees[treesIndex];
                                        treesIndex = IncreaseIndex(treesIndex, treesStep, trees.Length);
                                    } else {
                                        record = plants[plantsIndex];
                                        plantsIndex = IncreaseIndex(plantsIndex, plantsStep, plants.Length);
                                    }
                                } else if (placementAllowed == 3) { //Stone - only allow rocks and plants
                                    if(placementWeight <= 0.65f) {
                                        record = rocks[rocksIndex];
                                        rocksIndex = IncreaseIndex(rocksIndex, rocksStep, rocks.Length);
                                    } else {
                                        record = plants[plantsIndex];
                                        plantsIndex = IncreaseIndex(plantsIndex, plantsStep, plants.Length);
                                    }
                                } else if(placementAllowed == 4) { //Swamp water - only allow plants and trees
                                    if(placementWeight <= 0.65f) {
                                        record = plants[plantsIndex];
                                        plantsIndex = IncreaseIndex(plantsIndex, plantsStep, plants.Length);
                                    } else {
                                        record = trees[treesIndex];
                                        treesIndex = IncreaseIndex(treesIndex, treesStep, trees.Length);
                                    }
                                }
                                
                                if (terrainDist > 1 || !MeshReplacement.ImportNatureGameObject(dfBillboardBatch.TextureArchive, record, terrain, Mathf.FloorToInt(offsetX), Mathf.FloorToInt(offsetY))) {
                                    dfBillboardBatch.AddItem(record, pos);
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
        }

        private static int checkTiles(int middleX, int middleY, int west, int east, int north, int south, ref byte[,] tileMapSamples) {
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

        private int[] temperateRocks = new int[]{3, 4, 5, 6};
        private int[] temperatePlants = new int[]{1, 2, 7, 8, 9, 21, 22, 23, 26, 27, 28, 29};
        private int[] temperateTrees = new int[]{10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 24, 25, 30, 31};

        private int[] mountainRocks = new int[]{1, 3, 4, 6, 14, 17, 18, 27, 28};
        private int[] mountainPlants = new int[]{2, 7, 8, 9, 22, 23, 26, 29};
        private int[] mountainTrees = new int[]{5, 10, 11, 12, 13, 15, 16, 19, 20, 21, 24, 25, 30, 31};

        private int[] swampRocks = new int[]{2, 3, 4, 5, 6, 10};
        private int[] swampPlants = new int[]{1, 7, 8, 9, 11, 14, 20, 21, 22, 23, 26, 27, 28, 29, 31};
        private int[] swampTrees = new int[]{12, 13, 15, 16, 17, 18, 19, 24, 25, 30};

        private int[] desertRocks = new int[]{2, 3, 4, 9, 18, 19, 20, 21, 22, 24};
        private int[] desertPlants = new int[]{1, 7, 8, 10, 17, 23, 25, 26, 27, 29, 31};
        private int[] desertTrees = new int[]{5, 6, 11, 12, 13, 14, 15, 16, 28, 30};

        private int[] rainforestRocks = new int[]{4, 17, 28, 29};
        private int[] rainforestPlants = new int[]{1, 2, 5, 6, 7, 8, 9, 10, 11, 19, 20, 21, 22, 23, 24, 26, 27, 31};
        private int[] rainforestTrees = new int[]{3, 12, 13, 14, 15, 16, 18, 25, 30};

        private int[] subtropicalRocks = new int[]{3, 4, 5, 6, 10, 23};
        private int[] subtropicalPlants = new int[]{1, 2, 7, 8, 9, 14, 18, 21, 22, 25, 26, 28, 29, 31};
        private int[] subtropicalTrees = new int[]{11, 12, 13, 15, 16, 17, 19, 20, 24, 27, 30};

        private int[] woodlandRocks = new int[]{1, 3, 4, 6, 17, 18, 28};
        private int[] woodlandPlants = new int[]{2, 7, 8, 9, 21, 22, 23, 26, 29, 31};
        private int[] woodlandTrees = new int[]{5, 10, 11, 12, 13, 14, 15, 16, 19, 20, 24, 25, 27, 30};

        private int[] hauntedRocks = new int[]{1, 3, 4, 5, 6, 11, 12, 17};
        private int[] hauntedPlants = new int[]{2, 7, 8, 9, 14, 21, 22, 23, 26, 27, 28, 29};
        private int[] hauntedTrees = new int[]{5, 10, 11, 12, 13, 14, 15, 16, 19, 20, 24, 25, 27, 30};
}