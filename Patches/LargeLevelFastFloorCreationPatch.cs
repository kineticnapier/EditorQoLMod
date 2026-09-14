using HarmonyLib;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Patches
{
    // Large float-angle charts spend most of their initial RemakePath in InstantiateFloatFloors.
    // Keep the exact all-floor model, but avoid the per-floor GameObject -> parent -> GetComponent
    // sequence. Cloning the scrFloor component still clones the complete prefab GameObject while
    // returning the cloned scrFloor directly, and the parent is assigned as part of Instantiate.
    //
    // This replacement is deliberately limited to a real scnEditor.RemakePath scope and large
    // charts. Other callers keep the game's original method.
    [HarmonyPatch(typeof(scrLevelMaker), "InstantiateFloatFloors")]
    internal static class LargeLevelFastFloorCreationPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(scrLevelMaker __instance)
        {
            if (!Main.Enabled || !ADOBase.isLevelEditor ||
                LargeLevelRemakeDedupState.ScopeDepth != 1 || __instance == null ||
                __instance.floorAngles == null || __instance.listFloors == null ||
                !Application.isPlaying)
            {
                return true;
            }

            int targetCount = __instance.floorAngles.Length + 1;
            if (targetCount < LargeLevelRemakeDedupState.MinFloorCount || __instance.meshFloor == null)
            {
                return true;
            }

            scrFloor floorPrefab = __instance.meshFloor.GetComponent<scrFloor>();
            if (floorPrefab == null)
            {
                return true;
            }

            FastInstantiateFloatFloors(__instance, floorPrefab, targetCount);
            return false;
        }

        private static void FastInstantiateFloatFloors(scrLevelMaker levelMaker, scrFloor floorPrefab, int targetCount)
        {
            GameObject floorContainerObject = GameObject.Find("Floors");
            if (floorContainerObject == null)
            {
                floorContainerObject = new GameObject("Floors");
            }

            Transform floorContainer = floorContainerObject.transform;
            int existingCount = levelMaker.listFloors.Count;
            Material floorMeshDefault = RDConstants.data.floorMeshDefault;

            // Match the original type-switch behavior: a sprite-floor list cannot be reused for a
            // float-angle (mesh-floor) chart.
            bool canReuse = existingCount == 0 ||
                            levelMaker.listFloors[0].GetComponent<FloorSpriteRenderer>() == null;

            if (canReuse)
            {
                if (existingCount > targetCount)
                {
                    for (int i = targetCount; i < existingCount; i++)
                    {
                        scrFloor floor = levelMaker.listFloors[i];
                        if (floor != null)
                        {
                            Object.DestroyImmediate(floor.gameObject);
                        }
                    }
                    levelMaker.listFloors.RemoveRange(targetCount, existingCount - targetCount);
                    existingCount = targetCount;
                }
            }
            else
            {
                for (int i = 0; i < existingCount; i++)
                {
                    scrFloor floor = levelMaker.listFloors[i];
                    if (floor != null && floor.gameObject != null)
                    {
                        Object.DestroyImmediate(floor.gameObject);
                    }
                }
                levelMaker.listFloors.Clear();
                existingCount = 0;
            }

            if (levelMaker.listFloors.Count > 0)
            {
                ADOBase.conductor.onBeats.Clear();
            }

            if (levelMaker.listFloors.Capacity < targetCount)
            {
                levelMaker.listFloors.Capacity = targetCount;
            }

            scrFloor currentFloor;
            if (levelMaker.listFloors.Count == 0)
            {
                currentFloor = Object.Instantiate(floorPrefab, Vector3.zero, Quaternion.identity, floorContainer);
                currentFloor.hasLit = true;
                currentFloor.entryangle = 4.71238899230957;
                currentFloor.name = "0/Floor 0";
                levelMaker.listFloors.Add(currentFloor);
                existingCount = 1;
            }
            else
            {
                currentFloor = levelMaker.listFloors[0];
                ResetFloor(currentFloor, Vector3.zero, floorMeshDefault);
                currentFloor.hasLit = true;
                currentFloor.entryangle = 4.71238899230957;
                currentFloor.name = "0/Floor 0";
            }

            bool directionFlag = true;
            Vector3 position = Vector3.zero;
            double tileSize = scrController.instance.tileSize;

            for (int j = 0; j < levelMaker.floorAngles.Length; j++)
            {
                float floorAngle = levelMaker.floorAngles[j];
                double exitAngle;
                if (floorAngle != 999f)
                {
                    exitAngle = (-(double)floorAngle + 90.0) * 0.017453292;
                }
                else
                {
                    exitAngle = currentFloor.entryangle;
                }

                currentFloor.exitangle = exitAngle;
                position += scrMisc.getVectorFromAngle(exitAngle, tileSize);

                scrFloor nextFloor;
                if (j < existingCount - 1)
                {
                    nextFloor = levelMaker.listFloors[j + 1];
                    ResetFloor(nextFloor, position, floorMeshDefault);
                }
                else
                {
                    // Instantiating the component clones its complete GameObject, but avoids an
                    // extra GetComponent<scrFloor>() and assigns the parent during Instantiate.
                    nextFloor = Object.Instantiate(floorPrefab, position, Quaternion.identity, floorContainer);
                    levelMaker.listFloors.Add(nextFloor);
                }

                currentFloor.nextfloor = nextFloor;
                nextFloor.floatDirection = floorAngle;
                nextFloor.seqID = j + 1;
                nextFloor.entryangle = (exitAngle + 3.1415927410125732) % 6.2831854820251465;
                nextFloor.isCCW = !directionFlag;
                nextFloor.speed = 1f;

                if (floorAngle == 999f)
                {
                    currentFloor.midSpin = true;
                }

                if (j == levelMaker.floorAngles.Length - 1 && ADOBase.controller.gameworld)
                {
                    nextFloor.isportal = true;
                    nextFloor.levelnumber = Portal.EndOfLevel;
                }

                currentFloor = nextFloor;
            }

            currentFloor.exitangle = currentFloor.entryangle + 3.1415927410125732;
        }

        private static void ResetFloor(scrFloor floor, Vector3 position, Material material)
        {
            GameObject floorObject = floor.gameObject;
            Transform transform = floorObject.transform;
            transform.position = position;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            ffxPlusBase[] effects = floorObject.GetComponents<ffxPlusBase>();
            for (int i = 0; i < effects.Length; i++)
            {
                Object.DestroyImmediate(effects[i]);
            }

            floor.floorRenderer.material.CopyPropertiesFromMaterial(material);
            floor.Reset();
        }
    }
}
