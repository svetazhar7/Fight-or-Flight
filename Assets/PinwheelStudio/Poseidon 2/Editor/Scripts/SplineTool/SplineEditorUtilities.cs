#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using System.Linq;

namespace Pinwheel.Poseidon
{
    public static class SplineEditorUtilities
    {
        public static Color color = Color.white;

        public static void DrawSplinePath(SplineHandle spline, Vector3? newAnchorCandidate = null, int? newAnchorInsertIndex = null)
        {
            List<Vector4> outPointsWS = new List<Vector4>();
            List<Vector3> inAnchorsWS = spline.anchors.Select(a => a.position).ToList();

            if (newAnchorCandidate != null && newAnchorCandidate.HasValue)
            {
                if (newAnchorInsertIndex != null && newAnchorInsertIndex.HasValue)
                {
                    inAnchorsWS.Insert(newAnchorInsertIndex.Value, newAnchorCandidate.Value);
                }
                else
                {
                    inAnchorsWS.Add(newAnchorCandidate.Value);
                }
            }

            if (spline.head != null)
            {
                inAnchorsWS.Insert(0, spline.head.position);
            }
            if (spline.tail != null)
            {
                inAnchorsWS.Add(spline.tail.position);
            }

            if (inAnchorsWS.Count < 2)
                return;

            SplineHandle.SampleInterval(1, spline.tension, inAnchorsWS, outPointsWS);
            outPointsWS.Add(inAnchorsWS[^1]);

            Handles.color = color;
            Handles.DrawPolyLine(outPointsWS.Select(v4 => (Vector3)v4).ToArray());

            Vector3[] arrowPoints = new Vector3[]
            {
                Vector3.left, Vector3.forward, Vector3.right
            };

            for (int i = 0; i < outPointsWS.Count - 1; ++i)
            {
                Vector4 v0 = outPointsWS[i];
                if (Mathf.Approximately(v0.w, 1.0f))
                {
                    Vector4 v1 = outPointsWS[i + 1];
                    Vector3 dir = v1 - v0;
                    Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
                    Handles.matrix = Matrix4x4.TRS(v0, rot, Vector3.one * 0.5f);
                    Handles.DrawPolyLine(arrowPoints);
                }
            }

            Handles.matrix = Matrix4x4.identity;
        }

        public static void DrawSplineAnchors(SplineHandle spline)
        {
            List<Vector3> anchorsPosition = spline.anchors.Select(a => a.position).ToList();

            Handles.color = color;
            for (int i = 0; i < anchorsPosition.Count; ++i)
            {
                Handles.DrawSolidDisc(anchorsPosition[i], Vector3.up, 0.35f);
            }
        }
    }
}

#endif