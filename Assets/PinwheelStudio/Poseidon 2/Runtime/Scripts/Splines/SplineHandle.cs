#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Pinwheel.Poseidon
{
    [ExecuteInEditMode]
    public class SplineHandle : MonoBehaviour
    {
        [SerializeField]
        protected float m_tension = 0.33f;
        public float tension
        {
            get
            {
                return m_tension;
            }
            set
            {
                m_tension = Mathf.Clamp01(value);
            }
        }

        [SerializeField]
        protected List<Transform> m_anchors = new List<Transform>();
        public List<Transform> anchors
        {
            get
            {
                return m_anchors;
            }
        }

        [SerializeField]
        protected Transform m_head;
        public Transform head
        {
            get
            {
                return m_head;
            }
            set
            {
                m_head = value;
            }
        }

        [SerializeField]
        protected Transform m_tail;
        public Transform tail
        {
            get
            {
                return m_tail;
            }
            set
            {
                m_tail = value;
            }
        }

        public void ValidateAnchors()
        {
            if (m_anchors == null)
            {
                m_anchors = new List<Transform>();
            }

            m_anchors.RemoveAll(a => a == null);
        }

        public void CenterizePivotPoint()
        {
            if (anchors.Count == 0)
                return;

            Vector3 splineCenterOS = Vector3.zero;
            for (int i = 0; i < anchors.Count; ++i)
            {
                splineCenterOS += anchors[i].localPosition;
            }

            splineCenterOS = splineCenterOS / anchors.Count;

            for (int i = 0; i < anchors.Count; ++i)
            {
                anchors[i].localPosition -= splineCenterOS;
            }

            Vector3 splineCenterWS = transform.TransformPoint(splineCenterOS);
            transform.position = splineCenterWS;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="outPositions">New position samples will be appended into this list, w component is always 0</param>
        /// <param name="outScales">New scale samples will be appended to this list, w component is always 0</param>
        /// <param name="inAnchorsPosition"></param>
        /// <param name="distanceInterval"></param>
        /// <param name="tension"></param>
        public static void SampleInterval(
            float distanceInterval, float tension,
            List<Vector3> inAnchorsPosition, List<Vector4> outPositions,
            List<Vector3> inAnchorsScale = null, List<Vector4> outScales = null)
        {
            int anchorCount = inAnchorsPosition.Count;
            if (anchorCount < 2)
            {
                Debug.LogWarning("Need at least 2 anchors to sample a spline.");
                return;
            }

            List<Vector3> tangentLeftDir = new List<Vector3>();
            CalculateTangents(tangentLeftDir, inAnchorsPosition);

            float sqrDistanceBetweenPointsApprox = distanceInterval * distanceInterval;
            int sampleCountPerSegment = 100;
            Vector4 lastAcceptedSample = Vector4.zero;
            for (int iAnchor = 0; iAnchor < anchorCount - 1; ++iAnchor)
            {
                Vector3 anchor1 = inAnchorsPosition[iAnchor];
                Vector3 anchor2 = inAnchorsPosition[iAnchor + 1];
                float segmentLength = Vector3.Distance(anchor1, anchor2);
                float tangentLength = segmentLength * tension;

                Vector3 tangent1 = anchor1 - tangentLeftDir[iAnchor] * tangentLength;
                Vector3 tangent2 = anchor2 + tangentLeftDir[iAnchor + 1] * tangentLength;

                bool isFirstSampleOfSegment = true;
                for (int iT = 0; iT <= sampleCountPerSegment; ++iT)
                {
                    bool hasNewSample = false;
                    float t = Mathf.InverseLerp(0, sampleCountPerSegment, iT);
                    Vector4 sample = SamplePosition(anchor1, tangent1, tangent2, anchor2, t);
                    if (outPositions.Count == 0)
                    {
                        if (isFirstSampleOfSegment)
                        {
                            isFirstSampleOfSegment = false;
                            sample.w = 1;
                        }
                        outPositions.Add(sample);
                        lastAcceptedSample = sample;
                        hasNewSample = true;
                    }
                    else
                    {
                        float sqrDistanceToLastSample = Vector3.SqrMagnitude(sample - lastAcceptedSample);
                        if (sqrDistanceToLastSample >= sqrDistanceBetweenPointsApprox)
                        {
                            if (isFirstSampleOfSegment)
                            {
                                isFirstSampleOfSegment = false;
                                sample.w = 1;
                            }
                            outPositions.Add(sample);
                            lastAcceptedSample = sample;
                            hasNewSample = true;
                        }
                    }

                    if (hasNewSample)
                    {
                        if (inAnchorsScale != null && outScales != null)
                        {
                            Vector3 scale1 = inAnchorsScale[iAnchor];
                            Vector3 scale2 = inAnchorsScale[iAnchor + 1];
                            Vector3 scaleSample = Vector3.Lerp(scale1, scale2, t);

                            outScales.Add(scaleSample);
                        }
                    }
                }
            }
        }

        public static Vector3 SamplePosition(Vector3 anchor1, Vector3 tangent1, Vector3 tangent2, Vector3 anchor2, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1 - t;
            Vector3 p =
                oneMinusT * oneMinusT * oneMinusT * anchor1 +
                3 * oneMinusT * oneMinusT * t * tangent1 +
                3 * oneMinusT * t * t * tangent2 +
                t * t * t * anchor2;
            return p;
        }

        public static void CalculateTangents(List<Vector3> outTangents, List<Vector3> inAnchorsPosition)
        {
            outTangents.Clear();

            if (inAnchorsPosition.Count < 2)
            {
                Debug.LogWarning("Need at least 2 anchors to calculate spline tangents.");
                return;
            }

            List<Vector3> anchorsPosition = inAnchorsPosition.ToList();
            anchorsPosition.Insert(0, 2 * anchorsPosition[0] - anchorsPosition[1]);
            anchorsPosition.Add(2 * anchorsPosition[^1] - anchorsPosition[^2]);

            for (int iAnchor = 1; iAnchor < anchorsPosition.Count - 1; ++iAnchor)
            {
                Vector3 anchor = anchorsPosition[iAnchor];
                Vector3 tangentL = (anchor - anchorsPosition[iAnchor + 1]).normalized;
                Vector3 tangentR = (anchor - anchorsPosition[iAnchor - 1]).normalized;
                Vector3 tangentAvg = (tangentL * 0.5f + tangentR * 0.5f).normalized;
                Vector3 tangentDir = Vector3.Cross(tangentAvg, Vector3.Cross(tangentL, tangentR)).normalized;

                outTangents.Add(tangentDir);
            }
        }
    }
}

#endif