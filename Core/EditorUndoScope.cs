using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ADOFAI;
using HarmonyLib;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    /// <summary>
    /// Creates one undo step without calling scnEditor.SaveState when SmartEditor/PACL2's
    /// custom undo system is installed. The compatibility path stores a full LevelData
    /// snapshot in a small runtime LevelState subclass, so arbitrary QoL operations can
    /// still be undone reliably. Without PACL2 it falls back to the game's SaveStateScope.
    /// </summary>
    internal sealed class EditorUndoScope : IDisposable
    {
        private readonly scnEditor editor;
        private readonly IDisposable fallback;
        private readonly bool pacl2Mode;
        private readonly int stateId;
        private bool disposed;

        public EditorUndoScope(scnEditor editor)
        {
            if (editor == null) throw new ArgumentNullException("editor");
            this.editor = editor;

            if (Pacl2UndoBridge.TryBegin(editor, out stateId))
            {
                pacl2Mode = true;
                editor.changingState++;
            }
            else
            {
                fallback = new SaveStateScope(editor, false, true, false);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            if (!pacl2Mode)
            {
                if (fallback != null) fallback.Dispose();
                return;
            }

            if (editor.changingState > 0) editor.changingState--;
            Pacl2UndoBridge.Commit(editor, stateId);
        }
    }

    /// <summary>
    /// Reflection-only adapter for SmartEditor/PACL2. This project deliberately does not
    /// reference that mod at compile time.
    /// </summary>
    public static class Pacl2UndoBridge
    {
        private sealed class Snapshot
        {
            public LevelData LevelData;
            public int[] SelectedFloors;
            public int[] SelectedDecorations;
        }

        private static readonly Dictionary<int, Snapshot> snapshots = new Dictionary<int, Snapshot>();
        private static int nextId = 1;

        private static Type saveStatePatchType;
        private static FieldInfo undoStatesField;
        private static FieldInfo redoStatesField;
        private static Type dynamicStateType;
        private static FieldInfo dynamicIdField;
        private static Type dynamicBaseType;
        private static bool searched;

        internal static bool TryBegin(scnEditor editor, out int id)
        {
            id = 0;
            if (!EnsureAvailable()) return false;
            RemoveOrphanedSnapshots();

            Snapshot snapshot = Capture(editor);
            id = nextId++;
            snapshots[id] = snapshot;
            return true;
        }

        internal static void Commit(scnEditor editor, int id)
        {
            Snapshot snapshot;
            if (id <= 0 || !snapshots.TryGetValue(id, out snapshot)) return;

            try
            {
                IList undoStates = undoStatesField.GetValue(null) as IList;
                IList redoStates = redoStatesField.GetValue(null) as IList;
                if (undoStates == null || redoStates == null)
                    throw new InvalidOperationException("PACL2 undo lists were unavailable.");

                object state = Activator.CreateInstance(dynamicStateType, new object[] { id });
                while (undoStates.Count >= 100)
                {
                    object removed = undoStates[0];
                    RemoveDynamicSnapshot(removed);
                    undoStates.RemoveAt(0);
                }
                undoStates.Add(state);
                RemoveDynamicSnapshots(redoStates);
                redoStates.Clear();
                RemoveOrphanedSnapshots(undoStates, redoStates);

                FieldInfo unsaved = AccessTools.Field(typeof(scnEditor), "_unsavedChanges");
                if (unsaved != null) unsaved.SetValue(editor, true);
            }
            catch
            {
                snapshots.Remove(id);
                throw;
            }
        }

        internal static void Cleanup()
        {
            if (EnsureAvailable())
            {
                try
                {
                    IList undoStates = undoStatesField.GetValue(null) as IList;
                    IList redoStates = redoStatesField.GetValue(null) as IList;
                    RemoveDynamicStates(undoStates);
                    RemoveDynamicStates(redoStates);
                }
                catch
                {
                    // Cleanup must never make unload or scene transitions fail.
                }
            }
            snapshots.Clear();
        }

        /// <summary>Called by the runtime-generated PACL2 LevelState.</summary>
        public static void SwapFromDynamicState(int id)
        {
            Snapshot stored;
            if (!snapshots.TryGetValue(id, out stored)) return;

            scnEditor editor = scnEditor.instance;
            if (editor == null || stored.LevelData == null) return;

            Snapshot current = Capture(editor);
            Restore(editor, stored);
            snapshots[id] = current;
        }

        private static Snapshot Capture(scnEditor editor)
        {
            return new Snapshot
            {
                LevelData = editor.levelData == null ? null : editor.levelData.Copy(),
                SelectedFloors = CaptureSelectedFloors(editor),
                SelectedDecorations = CaptureSelectedDecorations(editor)
            };
        }

        private static int[] CaptureSelectedFloors(scnEditor editor)
        {
            if (editor.selectedFloors == null || editor.selectedFloors.Count == 0) return new int[0];
            return editor.selectedFloors.Select(x => x.seqID).ToArray();
        }

        private static int[] CaptureSelectedDecorations(scnEditor editor)
        {
            if (editor.selectedDecorations == null || editor.selectedDecorations.Count == 0) return new int[0];
            List<int> indices = new List<int>();
            foreach (LevelEvent decoration in editor.selectedDecorations)
            {
                int index = scrDecorationManager.GetDecorationIndex(decoration);
                if (index >= 0) indices.Add(index);
            }
            return indices.ToArray();
        }

        private static void Restore(scnEditor editor, Snapshot snapshot)
        {
            if (snapshot.LevelData == null) return;

            editor.customLevel.levelData = snapshot.LevelData;
            editor.DeselectFloors(false);
            editor.DeselectAllDecorations();
            editor.RemakePath(true, true);
            editor.UpdateDecorationObjects();
            if (editor.propertyControlDecorationsList != null)
                editor.propertyControlDecorationsList.RefreshItemsList(true);

            if (snapshot.SelectedFloors != null && snapshot.SelectedFloors.Length > 0 && editor.floors.Count > 0)
            {
                int first = Mathf.Clamp(snapshot.SelectedFloors[0], 0, editor.floors.Count - 1);
                int last = Mathf.Clamp(snapshot.SelectedFloors[snapshot.SelectedFloors.Length - 1], 0, editor.floors.Count - 1);
                if (snapshot.SelectedFloors.Length == 1)
                    editor.SelectFloor(editor.floors[first], true);
                else
                    editor.MultiSelectFloors(editor.floors[first], editor.floors[last], false);
            }

            if (snapshot.SelectedDecorations != null)
            {
                foreach (int index in snapshot.SelectedDecorations)
                {
                    if (index >= 0 && index < editor.decorations.Count)
                        editor.SelectDecoration(editor.decorations[index], false, false, true, false);
                }
            }

            editor.ApplyEventsToFloors();
        }

        private static bool EnsureAvailable()
        {
            if (searched) return saveStatePatchType != null && dynamicStateType != null;
            searched = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(x => x != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null) continue;
                    string fullName = type.FullName ?? string.Empty;
                    bool nameMatch = fullName.EndsWith(".FixLoad.CustomSaveState.SaveStatePatch", StringComparison.Ordinal) ||
                                     type.Name == "SaveStatePatch";
                    if (!nameMatch) continue;

                    FieldInfo undo = AccessTools.Field(type, "undoStates");
                    FieldInfo redo = AccessTools.Field(type, "redoStates");
                    MethodInfo save = AccessTools.Method(type, "SaveState");
                    if (undo == null || redo == null || save == null) continue;
                    if (!undo.IsStatic || !redo.IsStatic) continue;

                    saveStatePatchType = type;
                    undoStatesField = undo;
                    redoStatesField = redo;
                    break;
                }

                if (saveStatePatchType != null) break;
            }

            if (saveStatePatchType == null || undoStatesField == null || redoStatesField == null) return false;

            Type listType = undoStatesField.FieldType;
            if (!listType.IsGenericType) return false;
            Type levelStateType = listType.GetGenericArguments()[0];
            return BuildDynamicStateType(levelStateType);
        }

        private static bool BuildDynamicStateType(Type levelStateType)
        {
            if (dynamicStateType != null && dynamicBaseType == levelStateType) return true;
            try
            {
                AssemblyName name = new AssemblyName("Kiner.ADOFAIEditorQoL.Pacl2UndoRuntime");
                AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
                ModuleBuilder module = assembly.DefineDynamicModule(name.Name);
                TypeBuilder type = module.DefineType(
                    "Kiner.ADOFAIEditorQoL.Pacl2SnapshotState",
                    TypeAttributes.Public | TypeAttributes.Class,
                    levelStateType);

                FieldBuilder idField = type.DefineField("Id", typeof(int), FieldAttributes.Public);
                ConstructorInfo baseConstructor = levelStateType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (baseConstructor == null) return false;

                ConstructorBuilder ctor = type.DefineConstructor(MethodAttributes.Public,
                    CallingConventions.Standard, new[] { typeof(int) });
                ILGenerator ctorIl = ctor.GetILGenerator();
                ctorIl.Emit(OpCodes.Ldarg_0);
                ctorIl.Emit(OpCodes.Call, baseConstructor);
                ctorIl.Emit(OpCodes.Ldarg_0);
                ctorIl.Emit(OpCodes.Ldarg_1);
                ctorIl.Emit(OpCodes.Stfld, idField);
                ctorIl.Emit(OpCodes.Ret);

                MethodInfo swap = typeof(Pacl2UndoBridge).GetMethod("SwapFromDynamicState",
                    BindingFlags.Public | BindingFlags.Static);
                foreach (string methodName in new[] { "Undo", "Redo" })
                {
                    MethodInfo baseMethod = levelStateType.GetMethod(methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (baseMethod == null) return false;
                    MethodBuilder method = type.DefineMethod(methodName,
                        MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                        typeof(void), Type.EmptyTypes);
                    ILGenerator il = method.GetILGenerator();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, idField);
                    il.Emit(OpCodes.Call, swap);
                    il.Emit(OpCodes.Ret);
                    type.DefineMethodOverride(method, baseMethod);
                }

                dynamicStateType = type.CreateType();
                dynamicIdField = dynamicStateType.GetField("Id");
                dynamicBaseType = levelStateType;
                return true;
            }
            catch
            {
                dynamicStateType = null;
                dynamicIdField = null;
                return false;
            }
        }

        private static void RemoveDynamicSnapshot(object state)
        {
            if (state == null || dynamicStateType == null || !dynamicStateType.IsInstanceOfType(state) || dynamicIdField == null)
                return;
            object value = dynamicIdField.GetValue(state);
            if (value is int) snapshots.Remove((int)value);
        }

        private static void RemoveDynamicSnapshots(IList states)
        {
            if (states == null) return;
            for (int i = 0; i < states.Count; i++)
                RemoveDynamicSnapshot(states[i]);
        }

        private static void RemoveDynamicStates(IList states)
        {
            if (states == null || dynamicStateType == null) return;
            for (int i = states.Count - 1; i >= 0; i--)
            {
                object state = states[i];
                if (state == null || !dynamicStateType.IsInstanceOfType(state)) continue;
                RemoveDynamicSnapshot(state);
                states.RemoveAt(i);
            }
        }

        private static void RemoveOrphanedSnapshots()
        {
            if (!EnsureAvailable()) return;
            try
            {
                RemoveOrphanedSnapshots(undoStatesField.GetValue(null) as IList,
                    redoStatesField.GetValue(null) as IList);
            }
            catch
            {
                // Best-effort leak prevention only.
            }
        }

        private static void RemoveOrphanedSnapshots(IList undoStates, IList redoStates)
        {
            if (snapshots.Count == 0 || dynamicStateType == null || dynamicIdField == null) return;

            HashSet<int> live = new HashSet<int>();
            AddDynamicIds(undoStates, live);
            AddDynamicIds(redoStates, live);

            int[] ids = snapshots.Keys.ToArray();
            for (int i = 0; i < ids.Length; i++)
            {
                if (!live.Contains(ids[i])) snapshots.Remove(ids[i]);
            }
        }

        private static void AddDynamicIds(IList states, ISet<int> destination)
        {
            if (states == null) return;
            for (int i = 0; i < states.Count; i++)
            {
                object state = states[i];
                if (state == null || !dynamicStateType.IsInstanceOfType(state)) continue;
                object value = dynamicIdField.GetValue(state);
                if (value is int) destination.Add((int)value);
            }
        }
    }
}
