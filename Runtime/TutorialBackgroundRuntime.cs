using System;
using System.Collections.Generic;
using System.Linq;
using ADOFAI;
using Kiner.ADOFAIEditorQoL.Core;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    /// <summary>
    /// Runs on scnGame instead of TutorialBackground itself. The tutorial background
    /// GameObject is disabled when a custom image background is used, so attaching
    /// the updater to that object prevents Update from running at all.
    /// </summary>
    public sealed class TutorialBackgroundRuntime : MonoBehaviour, IRuntimeEffect
    {
        private scnGame game;
        private TutorialBackground background;
        private int lastFloor = -1;
        private int commandFingerprint;
        private bool initialized;
        private bool captureBaseOnNextRefresh = true;
        private int refreshDelayFrames;

        private float tweenStart;
        private float tweenDuration;
        private TutorialBackgroundCommand active;
        private Color startTile;
        private Color startShape;
        private Color startCamera;

        private Color baseTile = Color.white;
        private Color baseShape = Color.white;
        private Color baseCamera = Color.black;
        private bool baseCaptured;

        internal void Configure(scnGame source)
        {
            game = source;
            background = source == null ? null : source.tutorialBackground;
            ResetRuntime(true, 2);
        }

        internal void RefreshAfterBackgroundSetup()
        {
            if (game == null) game = scnGame.instance;
            if (game != null) background = game.tutorialBackground;
            ResetRuntime(true, 2);
        }

        private void ResetRuntime(bool captureBase, int delayFrames)
        {
            lastFloor = -1;
            commandFingerprint = 0;
            initialized = false;
            active = null;
            tweenDuration = 0f;
            captureBaseOnNextRefresh = captureBase;
            refreshDelayFrames = Math.Max(0, delayFrames);
            if (captureBase) baseCaptured = false;
        }

        private void Update()
        {
            if (!Main.Enabled) return;
            if (game == null) game = scnGame.instance;
            if (game == null) return;
            if (background == null) background = game.tutorialBackground;
            if (background == null || !IsLevelReady()) return;

            // SetBackground runs during loading and may overwrite colors one or two
            // frames after the scene object exists. Wait until that setup settles.
            if (refreshDelayFrames > 0)
            {
                refreshDelayFrames--;
                return;
            }

            List<TutorialBackgroundCommand> commands = GetCommands();
            int fingerprint = GetFingerprint(commands);
            int floor = GetCurrentFloor();

            if (!initialized || fingerprint != commandFingerprint)
            {
                if (captureBaseOnNextRefresh || !baseCaptured) CaptureBaseColors();
                ApplyStateForFloor(floor, commands);
                commandFingerprint = fingerprint;
                captureBaseOnNextRefresh = false;
            }
            else if (floor != lastFloor)
            {
                ProcessFloorChange(floor, commands);
            }

            UpdateTween();
        }

        private static bool IsLevelReady()
        {
            return scnGame.instance != null && scnGame.instance.levelData != null &&
                   scrLevelMaker.instance != null && scrLevelMaker.instance.listFloors != null &&
                   scrLevelMaker.instance.listFloors.Count > 0;
        }

        private static int GetCurrentFloor()
        {
            if (ADOBase.controller != null) return Math.Max(0, ADOBase.controller.currentFloorID);
            if (scrController.instance != null) return Math.Max(0, scrController.instance.currentFloorID);
            return 0;
        }

        private void ApplyStateForFloor(int floor, IList<TutorialBackgroundCommand> commands)
        {
            RestoreBaseColors();
            for (int i = 0; i < commands.Count; i++)
            {
                TutorialBackgroundCommand command = commands[i];
                if (command.Floor > floor) break;
                ApplyImmediately(command);
            }

            lastFloor = floor;
            initialized = true;
            active = null;
            tweenDuration = 0f;
        }

        private void ProcessFloorChange(int floor, IList<TutorialBackgroundCommand> commands)
        {
            if (lastFloor < 0 || floor < lastFloor)
            {
                ApplyStateForFloor(floor, commands);
                return;
            }

            for (int i = 0; i < commands.Count; i++)
            {
                TutorialBackgroundCommand command = commands[i];
                if (command.Floor > lastFloor && command.Floor <= floor)
                    Begin(command);
            }
            lastFloor = floor;
        }

        private List<TutorialBackgroundCommand> GetCommands()
        {
            List<TutorialBackgroundCommand> result = new List<TutorialBackgroundCommand>();
            if (game == null || game.events == null) return result;

            foreach (LevelEvent evnt in game.events)
            {
                if (evnt == null || !evnt.active) continue;
                TutorialBackgroundCommand command;
                if (TutorialBackgroundOperations.TryDecode(evnt, out command)) result.Add(command);
            }
            result.Sort(delegate(TutorialBackgroundCommand a, TutorialBackgroundCommand b)
            {
                return a.Floor.CompareTo(b.Floor);
            });
            return result;
        }

        private static int GetFingerprint(IList<TutorialBackgroundCommand> commands)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < commands.Count; i++)
                {
                    TutorialBackgroundCommand command = commands[i];
                    hash = hash * 31 + command.Floor;
                    hash = hash * 31 + command.TileEnabled.GetHashCode();
                    hash = hash * 31 + command.ShapeEnabled.GetHashCode();
                    hash = hash * 31 + command.CameraEnabled.GetHashCode();
                    hash = hash * 31 + command.TileColor.GetHashCode();
                    hash = hash * 31 + command.ShapeColor.GetHashCode();
                    hash = hash * 31 + command.CameraColor.GetHashCode();
                    hash = hash * 31 + command.DurationBeats.GetHashCode();
                    hash = hash * 31 + (command.Ease == null ? 0 : command.Ease.GetHashCode());
                }
                return hash;
            }
        }

        private void CaptureBaseColors()
        {
            baseTile = GetTileColor();
            baseShape = GetShapeColor();
            baseCamera = GetCameraColor();
            baseCaptured = true;
        }

        private void RestoreBaseColors()
        {
            if (!baseCaptured) CaptureBaseColors();
            SetTileColor(baseTile);
            SetShapeColor(baseShape);
            SetCameraColor(baseCamera);
        }

        private void Begin(TutorialBackgroundCommand command)
        {
            active = command;
            startTile = GetTileColor();
            startShape = GetShapeColor();
            startCamera = GetCameraColor();
            tweenStart = Time.unscaledTime;
            tweenDuration = BeatsToSeconds(command.DurationBeats, command.Floor);
            if (tweenDuration <= 0f) Apply(1f);
        }

        private void ApplyImmediately(TutorialBackgroundCommand command)
        {
            if (command == null) return;
            if (command.TileEnabled) SetTileColor(command.TileColor);
            if (command.ShapeEnabled) SetShapeColor(command.ShapeColor);
            if (command.CameraEnabled) SetCameraColor(command.CameraColor);
        }

        private float BeatsToSeconds(float beats, int floor)
        {
            if (beats <= 0f || ADOBase.conductor == null) return 0f;
            float speed = 1f;
            if (scrLevelMaker.instance != null && floor >= 0 && floor < scrLevelMaker.instance.listFloors.Count)
                speed = Mathf.Max(0.0001f, scrLevelMaker.instance.listFloors[floor].speed);
            float pitch = ADOBase.conductor.song == null ? 1f : Mathf.Max(0.0001f, ADOBase.conductor.song.pitch);
            return beats * (float)ADOBase.conductor.crotchetAtStart / (speed * pitch);
        }

        private void UpdateTween()
        {
            if (active == null || tweenDuration <= 0f) return;
            float progress = Mathf.Clamp01((Time.unscaledTime - tweenStart) / tweenDuration);
            Apply(Ease(progress, active.Ease));
            if (progress >= 1f)
            {
                Apply(1f);
                active = null;
                tweenDuration = 0f;
            }
        }

        private void Apply(float t)
        {
            if (active == null) return;
            if (active.TileEnabled) SetTileColor(Color.LerpUnclamped(startTile, active.TileColor, t));
            if (active.ShapeEnabled) SetShapeColor(Color.LerpUnclamped(startShape, active.ShapeColor, t));
            if (active.CameraEnabled) SetCameraColor(Color.LerpUnclamped(startCamera, active.CameraColor, t));
        }

        public void StopAndRestore()
        {
            active = null;
            tweenDuration = 0f;
            if (baseCaptured) RestoreBaseColors();
            initialized = false;
            captureBaseOnNextRefresh = true;
        }

        private void OnDestroy()
        {
            StopAndRestore();
        }

        private Color GetTileColor()
        {
            return background == null || background.tileRenderer == null ? Color.white : background.tileRenderer.color;
        }

        private Color GetShapeColor()
        {
            if (background != null && background.elementRenderers != null)
            {
                for (int i = 0; i < background.elementRenderers.Count; i++)
                    if (background.elementRenderers[i] != null) return background.elementRenderers[i].color;
            }
            if (background != null && background.triangleRenderer != null)
                return background.triangleRenderer.material.color;
            return Color.white;
        }

        private static Color GetCameraColor()
        {
            return scrCamera.instance == null || scrCamera.instance.Bgcamstatic == null
                ? Color.black
                : scrCamera.instance.Bgcamstatic.backgroundColor;
        }

        private void SetTileColor(Color color)
        {
            if (background != null && background.tileRenderer != null)
                background.tileRenderer.color = color;
        }

        private void SetShapeColor(Color color)
        {
            if (background == null) return;
            if (background.elementRenderers != null)
            {
                for (int i = 0; i < background.elementRenderers.Count; i++)
                {
                    SpriteRenderer renderer = background.elementRenderers[i];
                    if (renderer != null) renderer.color = color.WithAlpha(renderer.color.a);
                }
            }
            if (background.triangleRenderer != null)
                background.triangleRenderer.material.color = color.WithAlpha(background.triangleRenderer.material.color.a);
        }

        private static void SetCameraColor(Color color)
        {
            if (scrCamera.instance != null && scrCamera.instance.Bgcamstatic != null)
                scrCamera.instance.Bgcamstatic.backgroundColor = color;
        }

        private static float Ease(float t, string ease)
        {
            switch (ease)
            {
                case "InSine": return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case "OutSine": return Mathf.Sin(t * Mathf.PI * 0.5f);
                case "InOutSine": return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
                case "InQuad": return t * t;
                case "OutQuad": return 1f - (1f - t) * (1f - t);
                case "InOutQuad": return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
                case "InCubic": return t * t * t;
                case "OutCubic": return 1f - Mathf.Pow(1f - t, 3f);
                case "InOutCubic": return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                default: return t;
            }
        }
    }
}
