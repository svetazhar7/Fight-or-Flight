#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;

namespace Pinwheel.Poseidon
{
    [CustomEditor(typeof(GerstnerWaveController))]
    public class GerstnerWaveControllerInspector : Editor
    {
        private GerstnerWaveController m_instance;

        private void OnEnable()
        {
            m_instance = target as GerstnerWaveController;
        }

        public override void OnInspectorGUI()
        {
            CheckMaterialHasGerstnerWave();
            DrawWaveLayers();
        }

        private void CheckMaterialHasGerstnerWave()
        {
            if (m_instance.water != null && m_instance.water.material != null)
            {
                bool hasGerstnerWave = m_instance.water.material.HasProperty(PMat.WAVE_HEIGHTS_GERSTNER);
                if (!hasGerstnerWave)
                {
                    EditorGUILayout.HelpBox($"Current water material doesn't have Gerstner wave. This component may take no effect.\nTo suppress this message, add a \"{PMat.WAVE_HEIGHTS_GERSTNER_STR}\" property to the shader.", MessageType.Warning);
                }
            }
        }

        private void DrawWaveLayers()
        {
            if (m_instance.water == null || m_instance.water.material == null)
                return;

            Material mat = m_instance.water.material;
            Vector4 directionsX = Vector4.zero;
            if (mat.HasVector(PMat.WAVE_DIRECTIONS_X_GERSTNER))
            {
                directionsX = mat.GetVector(PMat.WAVE_DIRECTIONS_X_GERSTNER);
            }

            Vector4 directionsZ = Vector4.zero;
            if (mat.HasVector(PMat.WAVE_DIRECTIONS_Z_GERSTNER))
            {
                directionsZ = mat.GetVector(PMat.WAVE_DIRECTIONS_Z_GERSTNER);
            }

            Vector4 heights = Vector4.zero;
            if (mat.HasVector(PMat.WAVE_HEIGHTS_GERSTNER))
            {
                heights = mat.GetVector(PMat.WAVE_HEIGHTS_GERSTNER);
            }

            Vector4 lengths = Vector4.zero;
            if (mat.HasVector(PMat.WAVE_LENGTHS_GERSTNER))
            {
                lengths = mat.GetVector(PMat.WAVE_LENGTHS_GERSTNER);
            }

            Vector4 speeds = Vector4.zero;
            if (mat.HasVector(PMat.WAVE_SPEEDS_GERSTNER))
            {
                speeds = mat.GetVector(PMat.WAVE_SPEEDS_GERSTNER);
            }

            Vector4 steepnesses = Vector4.zero;
            if (mat.HasVector(PMat.WAVE_STEEPNESSES_GERSTNER))
            {
                steepnesses = mat.GetVector(PMat.WAVE_STEEPNESSES_GERSTNER);
            }

            DrawWaveProperties(0, ref directionsX, ref directionsZ, ref heights, ref lengths, ref speeds, ref steepnesses);
            EditorGUILayout.Space();
            DrawWaveProperties(1, ref directionsX, ref directionsZ, ref heights, ref lengths, ref speeds, ref steepnesses);
            EditorGUILayout.Space();
            DrawWaveProperties(2, ref directionsX, ref directionsZ, ref heights, ref lengths, ref speeds, ref steepnesses);
            EditorGUILayout.Space();
            DrawWaveProperties(3, ref directionsX, ref directionsZ, ref heights, ref lengths, ref speeds, ref steepnesses);
            EditorGUILayout.Space();

            if (mat.HasVector(PMat.WAVE_DIRECTIONS_X_GERSTNER))
            {
                mat.SetVector(PMat.WAVE_DIRECTIONS_X_GERSTNER, directionsX);
            }
            if (mat.HasVector(PMat.WAVE_DIRECTIONS_Z_GERSTNER))
            {
                mat.SetVector(PMat.WAVE_DIRECTIONS_Z_GERSTNER, directionsZ);
            }
            if (mat.HasVector(PMat.WAVE_HEIGHTS_GERSTNER))
            {
                mat.SetVector(PMat.WAVE_HEIGHTS_GERSTNER, heights);
            }
            if (mat.HasVector(PMat.WAVE_LENGTHS_GERSTNER))
            {
                mat.SetVector(PMat.WAVE_LENGTHS_GERSTNER, lengths);
            }
            if (mat.HasVector(PMat.WAVE_SPEEDS_GERSTNER))
            {
                mat.SetVector(PMat.WAVE_SPEEDS_GERSTNER, speeds);
            }
            if (mat.HasVector(PMat.WAVE_STEEPNESSES_GERSTNER))
            {
                mat.SetVector(PMat.WAVE_STEEPNESSES_GERSTNER, steepnesses);
            }
        }

        private void DrawWaveProperties(int index, ref Vector4 directionsX, ref Vector4 directionsZ, ref Vector4 heights, ref Vector4 lengths, ref Vector4 speeds, ref Vector4 steepnesses)
        {
            EditorGUILayout.LabelField($"Wave {index + 1}", EditorStyles.boldLabel);

            Vector2 dir = new Vector2(directionsX[index], directionsZ[index]).normalized;
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            EditorGUIUtility.wideMode = true;
            dir = EditorGUILayout.Vector2Field("Direction", dir);
            EditorGUIUtility.wideMode = false;
            heights[index] = EditorGUILayout.FloatField("Height", heights[index]);
            lengths[index] = EditorGUILayout.FloatField("Length", lengths[index]);
            speeds[index] = EditorGUILayout.FloatField("Speed", speeds[index]);
            steepnesses[index] = EditorGUILayout.Slider("Steepness", steepnesses[index], 0f, 1f);

            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical();
            Rect directionRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(98), GUILayout.Height(98));
            dir = WaveDirectionControl(directionRect, dir);
            directionsX[index] = dir.x;
            directionsZ[index] = dir.y;
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private const string WAVE_DIR_CONTROL_KEY = "poseidon-gerstner-wave-dir-control-active";
        private Vector2 WaveDirectionControl(Rect r, Vector2 dir)
        {
            //EditorGUI.DrawRect(r, Color.gray);
            Handles.BeginGUI();
            Handles.color = Handles.xAxisColor;
            Handles.DrawLine(new Vector3(r.min.x, r.center.y), new Vector3(r.max.x, r.center.y));
            Handles.color = Handles.zAxisColor;
            Handles.DrawLine(new Vector3(r.center.x, r.min.y), new Vector3(r.center.x, r.max.y));

            dir.y = -dir.y; //flip the y to match editor rect coords from top-left
            Vector2 handlePos = Rect.NormalizedToPoint(r, (dir + Vector2.one) * 0.5f);
            float handleSize = 10;
            Handles.color = Color.white;
            Handles.DrawLine(handlePos, r.center);
            Handles.DrawSolidDisc(handlePos, Vector3.forward, handleSize * 0.5f);
            Handles.EndGUI();

            Rect handleRect = new Rect() { size = Vector2.one * handleSize, center = handlePos };
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.MoveArrow);

            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0)
            {
                if (handleRect.Contains(Event.current.mousePosition))
                {
                    SessionState.SetString(WAVE_DIR_CONTROL_KEY, r.ToString());
                }
            }
            if (Event.current.type == EventType.MouseDrag)
            {
                string activeControl = SessionState.GetString(WAVE_DIR_CONTROL_KEY, "none");
                if (string.Equals(activeControl, r.ToString()))
                {
                    dir = Rect.PointToNormalized(r, Event.current.mousePosition);
                    dir = dir * 2 - Vector2.one;
                    GUI.changed = true;
                }
            }
            if (Event.current.type == EventType.MouseUp)
            {
                string activeControl = SessionState.GetString(WAVE_DIR_CONTROL_KEY, "none");
                if (string.Equals(activeControl, r.ToString()))
                {
                    SessionState.SetString(WAVE_DIR_CONTROL_KEY, "none");
                }
            }

            dir.y = -dir.y; //flip the y again to match scene coords from bottom left
            return dir;
        }
    }
}

#endif