#if POSEIDON_2
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pinwheel.Poseidon
{
    [ExecuteInEditMode]
    public class RiverWater : PoseidonWaterBody
    {
        public Material segmentMat;
        public Material junctionMat;

        [SerializeField]
        protected RiverMeshDesc m_meshDesc;
        public RiverMeshDesc meshDesc
        {
            get
            {
                return m_meshDesc;
            }
            set
            {
                m_meshDesc = value;
            }
        }

        [SerializeField]
        private int m_splineCounter = 0;
        [SerializeField]
        protected List<SplineHandle> m_splines = new List<SplineHandle>();
        public List<SplineHandle> splines
        {
            get
            {
                return m_splines;
            }
        }

        protected override void Reset()
        {
            base.Reset();

            m_meshDesc = new RiverMeshDesc()
            {
                width = 5,
                vertexDistance = 1,
                needNormals = false,
                needTangents = false
            };
        }

        [HideInInspector]
        public List<Vector4> positionSamplesWS = new List<Vector4>();
        //public List<Vector4> scaleSamplesWS = new List<Vector4>();
        public Dictionary<int, Transform> sampleIndexToJunctionTransform = new Dictionary<int, Transform>();
        public Dictionary<int, Transform> breakPointIndexToJunctionTransform = new Dictionary<int, Transform>();
        public Dictionary<Transform, int> anchorsRank = new Dictionary<Transform, int>();
        [HideInInspector]
        public List<Vector3> normalsWS = new List<Vector3>();
        [HideInInspector]
        public List<int> breakPointIndices = new List<int>();
        [HideInInspector]
        public List<Vector3> verticesForDelaunayDebug = new List<Vector3>();
        [HideInInspector]
        public Dictionary<Transform, List<Vector3>> debugDelaunayVerticesByAnchor = new Dictionary<Transform, List<Vector3>>();
        public const float W_SAMPLE = 0;
        public const float W_BREAK_POINT = 1;
        public const float W_START_SPLINE = 2;
        public const float W_END_SPLINE = 3;
        public const float W_JUNCTION = 4;

        public const string SEGMENT_ROOT_NAME = "~RiverRenderers";
        [SerializeField]
        protected List<RiverSegment> m_segments = new List<RiverSegment>();
        public IEnumerable<RiverSegment> segments
        {
            get
            {
                return m_segments;
            }
        }

        protected override void Update()
        {
            m_segments.ForEach(t =>
            {
                if (t == null)
                    return;

                t.meshRenderer.sharedMaterial = m_material;
                t.meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            });

            if (m_material != null)
            {
                float time = GetTimeParam();
                float sineTime = Mathf.Sin(time);
                m_material.SetFloat(PMat.TIME, time);
                m_material.SetFloat(PMat.SINE_TIME, sineTime);
            }
        }

        public SplineHandle AddSpline()
        {
            GameObject splineGO = new GameObject($"~Spline {++m_splineCounter}");
            splineGO.transform.parent = transform;
            splineGO.transform.localPosition = Vector3.zero;
            splineGO.transform.localRotation = Quaternion.identity;
            splineGO.transform.localScale = Vector3.one;

            SplineHandle spline = splineGO.AddComponent<SplineHandle>();
            splines.Add(spline);
            return spline;
        }

        public void GenerateMesh()
        {
            debugDelaunayVerticesByAnchor = new Dictionary<Transform, List<Vector3>>();

            splines.RemoveAll(s => s == null);
            IEnumerable<SplineHandle> validSplines = splines.Where(s => s.anchors.Count >= 2 && s.isActiveAndEnabled);
            ValidateSplines(validSplines);
            ComputeJunctionsRank(validSplines);
            ComputeSamples(validSplines);
            ComputeJunctionSamples();
            ComputeBreakPointsAtJunctions();
            ComputeBreakPointIndices();
            ComputeNormals();
            CreateMeshesAndRenderers();
        }

        private void ValidateSplines(IEnumerable<SplineHandle> validSplines)
        {
            foreach (SplineHandle spline in validSplines)
            {
                spline.ValidateAnchors();
            }
        }

        private void ComputeJunctionsRank(IEnumerable<SplineHandle> validSplines)
        {
            anchorsRank.Clear();

            foreach (SplineHandle spline in validSplines)
            {
                AddAnchorRank(spline.anchors[0], 1);
                AddAnchorRank(spline.anchors[^1], 1);
                for (int i = 1; i < spline.anchors.Count - 1; ++i)
                {
                    AddAnchorRank(spline.anchors[i], 2);
                }

                if (spline.head != null)
                {
                    AddAnchorRank(spline.head, 1);
                    AddAnchorRank(spline.anchors[0], 1);
                }
                if (spline.tail != null)
                {
                    AddAnchorRank(spline.tail, 1);
                    AddAnchorRank(spline.anchors[^1], 1);
                }
            }
        }

        private void AddAnchorRank(Transform anchor, int add)
        {
            if (anchorsRank.ContainsKey(anchor))
            {
                anchorsRank[anchor] = anchorsRank[anchor] + add;
            }
            else
            {
                anchorsRank[anchor] = add;
            }
        }

        private void ComputeSamples(IEnumerable<SplineHandle> validSplines)
        {
            positionSamplesWS.Clear();

            List<Vector3> inAnchorsPosition = new List<Vector3>();
            List<Vector4> tempPositionSamples = new List<Vector4>();
            foreach (SplineHandle spline in validSplines)
            {
                inAnchorsPosition.Clear();
                if (spline.head)
                {
                    inAnchorsPosition.Add(spline.head.position);
                }
                foreach (Transform a in spline.anchors)
                {
                    inAnchorsPosition.Add(a.position);
                }
                if (spline.tail)
                {
                    inAnchorsPosition.Add(spline.tail.position);
                }

                tempPositionSamples.Clear();
                SplineHandle.SampleInterval(meshDesc.vertexDistance, spline.tension, inAnchorsPosition, tempPositionSamples);

                int sampleCountPerSegment = Mathf.Max(1, Mathf.CeilToInt(meshDesc.segmentLength / meshDesc.vertexDistance));
                int tempIndex = sampleCountPerSegment;
                Vector4 breakPoint;
                breakPoint = tempPositionSamples[0];
                breakPoint.w = W_START_SPLINE;
                tempPositionSamples[0] = breakPoint;
                while (tempIndex < tempPositionSamples.Count)
                {
                    breakPoint = tempPositionSamples[tempIndex];
                    breakPoint.w = W_BREAK_POINT;
                    tempPositionSamples[tempIndex] = breakPoint;
                    tempIndex += sampleCountPerSegment;
                }

                breakPoint = tempPositionSamples[^1];
                breakPoint.w = W_END_SPLINE;
                tempPositionSamples[^1] = breakPoint;

                positionSamplesWS.AddRange(tempPositionSamples);
            }
        }

        private void ComputeJunctionSamples()
        {
            List<Transform> highRankJunctions = new List<Transform>();
            foreach (var t in anchorsRank.Keys)
            {
                if (anchorsRank[t] > 2)
                {
                    highRankJunctions.Add(t);
                }
            }

            sampleIndexToJunctionTransform.Clear();
            for (int iSample = 0; iSample < positionSamplesWS.Count; ++iSample)
            {
                Vector4 sample = positionSamplesWS[iSample];
                for (int iJunction = 0; iJunction < highRankJunctions.Count; ++iJunction)
                {
                    Transform junction = highRankJunctions[iJunction];
                    Vector4 junctionPosition = junction.position;
                    if (Vector3.SqrMagnitude(sample - junctionPosition) < meshDesc.width * meshDesc.width)
                    {
                        sample.w = W_JUNCTION;
                        sampleIndexToJunctionTransform.Add(iSample, junction);
                        positionSamplesWS[iSample] = sample;
                        break;
                    }
                }
            }
        }

        private void ComputeBreakPointsAtJunctions()
        {
            breakPointIndexToJunctionTransform.Clear();

            for (int i = 1; i < positionSamplesWS.Count - 1; ++i)
            {
                Vector4 lastSample = positionSamplesWS[i - 1];
                Vector4 nextSample = positionSamplesWS[i + 1];
                Vector4 currentSample = positionSamplesWS[i];
                if (Mathf.Approximately(lastSample.w, W_JUNCTION) ||
                    Mathf.Approximately(nextSample.w, W_JUNCTION))
                {
                    if (Mathf.Approximately(currentSample.w, W_SAMPLE) ||
                        Mathf.Approximately(currentSample.w, W_BREAK_POINT))
                    {
                        currentSample.w = W_BREAK_POINT;
                        positionSamplesWS[i] = currentSample;
                        Transform junction;
                        if (sampleIndexToJunctionTransform.TryGetValue(i - 1, out junction))
                        {
                            breakPointIndexToJunctionTransform.Add(i, junction);
                        }
                        else if (sampleIndexToJunctionTransform.TryGetValue(i + 1, out junction))
                        {
                            breakPointIndexToJunctionTransform.Add(i, junction);
                        }
                    }
                }
            }
        }

        private void ComputeBreakPointIndices()
        {
            breakPointIndices.Clear();
            for (int i = 0; i < positionSamplesWS.Count; ++i)
            {
                if (Mathf.Approximately(positionSamplesWS[i].w, W_BREAK_POINT) ||
                    Mathf.Approximately(positionSamplesWS[i].w, W_START_SPLINE) ||
                    Mathf.Approximately(positionSamplesWS[i].w, W_END_SPLINE))
                {
                    breakPointIndices.Add(i);
                }
            }
        }

        private void ComputeNormals()
        {
            normalsWS.Clear();
            normalsWS.Add(Vector3.zero);

            for (int i = 1; i < positionSamplesWS.Count - 1; ++i)
            {
                Vector4 currentPosition = positionSamplesWS[i];
                if (Mathf.Approximately(currentPosition.w, W_JUNCTION))
                {
                    normalsWS.Add(Vector3.zero);
                    continue;
                }

                Vector4 lastSample = positionSamplesWS[i - 1];
                Vector4 nextSample = positionSamplesWS[i + 1];
                Vector3 dir = nextSample - lastSample;
                Vector3 normal = Vector3.Cross(dir, Vector3.up);

                normal = normal.normalized;
                normalsWS.Add(normal);
            }

            normalsWS.Add(Vector3.zero);
        }

        private void CreateMeshesAndRenderers()
        {
            foreach (RiverSegment s in m_segments)
            {
                if (s != null)
                {
                    Object.DestroyImmediate(s.gameObject);
                }
            }
            m_segments.Clear();

            for (int i = 0; i < breakPointIndices.Count - 1; ++i)
            {
                int startIndex = breakPointIndices[i];
                int endIndex = breakPointIndices[i + 1];
                if (!Mathf.Approximately(positionSamplesWS[startIndex].w, W_END_SPLINE))
                {
                    CreateSegmentMeshAndRenderer(startIndex, endIndex);
                }
            }

            verticesForDelaunayDebug.Clear();
            foreach (var t in anchorsRank.Keys)
            {
                if (anchorsRank[t] > 2)
                {
                    CreateJunctionMeshAndRenderer(t);
                }
            }
        }

        private void CreateSegmentMeshAndRenderer(int sampleStartIndex, int sampleEndIndex)
        {
            for (int i = sampleStartIndex; i <= sampleEndIndex; ++i)
            {
                Vector4 sample = positionSamplesWS[i];
                if (Mathf.Approximately(sample.w, W_JUNCTION))
                    return;
            }

            List<Vector3> verticesOS = new List<Vector3>();
            List<Vector4> texcoord0 = new List<Vector4>();
            List<Color> vertexColor = new List<Color>();
            List<int> indices = new List<int>();
            Transform segmentRoots = GetOrAddSegmentRoot();
            GameObject segmentGO = new GameObject($"~Segment_{sampleStartIndex}_{sampleEndIndex}");
            segmentGO.transform.SetParent(segmentRoots);
            segmentGO.transform.localPosition = Vector3.zero;
            segmentGO.transform.localRotation = Quaternion.identity;
            segmentGO.transform.localScale = Vector3.one;

            RiverSegment riverSegmentComp = segmentGO.AddComponent<RiverSegment>();
            m_segments.Add(riverSegmentComp);

            int vertexCount = 0;
            float widthRounded = Mathf.Ceil(meshDesc.width / meshDesc.vertexDistance) * meshDesc.vertexDistance;
            widthRounded = Mathf.Max(widthRounded, meshDesc.vertexDistance);
            int step = Mathf.FloorToInt(widthRounded / meshDesc.vertexDistance) + 1;

            List<Vector3> tempVertices0WS = new List<Vector3>();
            List<Vector3> tempVertices1WS = new List<Vector3>();
            for (int iS = sampleStartIndex; iS < sampleEndIndex; ++iS)
            {
                Vector3 p0WS = positionSamplesWS[iS];
                Vector3 p1WS = positionSamplesWS[iS + 1];

                Vector3 n0WS = normalsWS[iS];
                Vector3 n1WS = normalsWS[iS + 1];

                tempVertices0WS.Clear();
                tempVertices1WS.Clear();

                Vector3 startVertex0WS = p0WS + n0WS * widthRounded * 0.5f;
                for (int iV = 0; iV < step; ++iV)
                {
                    Vector3 vWS = startVertex0WS - n0WS * meshDesc.vertexDistance * iV;
                    tempVertices0WS.Add(vWS);
                }

                Vector3 startVertex1WS = p1WS + n1WS * widthRounded * 0.5f;
                for (int iV = 0; iV < step; ++iV)
                {
                    Vector3 vWS = startVertex1WS - n1WS * meshDesc.vertexDistance * iV;
                    tempVertices1WS.Add(vWS);
                }

                Vector4 a, b, c, d;
                for (int iV = 0; iV < tempVertices0WS.Count - 1; ++iV)
                {
                    a = segmentGO.transform.InverseTransformPoint(tempVertices0WS[iV]);
                    b = segmentGO.transform.InverseTransformPoint(tempVertices1WS[iV]);
                    c = segmentGO.transform.InverseTransformPoint(tempVertices1WS[iV + 1]);
                    d = segmentGO.transform.InverseTransformPoint(tempVertices0WS[iV + 1]);

                    if ((iS + iV) % 2 == 0)
                    {
                        verticesOS.Add(a); texcoord0.Add(b); vertexColor.Add(c); indices.Add(vertexCount++);
                        verticesOS.Add(b); texcoord0.Add(c); vertexColor.Add(a); indices.Add(vertexCount++);
                        verticesOS.Add(c); texcoord0.Add(a); vertexColor.Add(b); indices.Add(vertexCount++);

                        verticesOS.Add(a); texcoord0.Add(c); vertexColor.Add(d); indices.Add(vertexCount++);
                        verticesOS.Add(c); texcoord0.Add(d); vertexColor.Add(a); indices.Add(vertexCount++);
                        verticesOS.Add(d); texcoord0.Add(a); vertexColor.Add(c); indices.Add(vertexCount++);
                    }
                    else
                    {
                        verticesOS.Add(a); texcoord0.Add(b); vertexColor.Add(d); indices.Add(vertexCount++);
                        verticesOS.Add(b); texcoord0.Add(d); vertexColor.Add(a); indices.Add(vertexCount++);
                        verticesOS.Add(d); texcoord0.Add(a); vertexColor.Add(b); indices.Add(vertexCount++);

                        verticesOS.Add(b); texcoord0.Add(c); vertexColor.Add(d); indices.Add(vertexCount++);
                        verticesOS.Add(c); texcoord0.Add(d); vertexColor.Add(b); indices.Add(vertexCount++);
                        verticesOS.Add(d); texcoord0.Add(b); vertexColor.Add(c); indices.Add(vertexCount++);
                    }
                }
            }

            Mesh segmentMesh = new Mesh() { name = segmentGO.name };
            segmentMesh.SetVertices(verticesOS);
            segmentMesh.SetUVs(0, texcoord0);
            segmentMesh.SetColors(vertexColor);
            segmentMesh.SetIndices(indices, 0, indices.Count, MeshTopology.Triangles, 0, true, 0);

            if (meshDesc.needNormals)
            {
                segmentMesh.RecalculateNormals();
            }
            else
            {
                segmentMesh.normals = null;
            }

            if (meshDesc.needTangents)
            {
                segmentMesh.RecalculateTangents();
            }
            else
            {
                segmentMesh.tangents = null;
            }

            riverSegmentComp.meshFilter.sharedMesh = segmentMesh;
            riverSegmentComp.meshRenderer.sharedMaterial = material;
        }

        private void CreateJunctionMeshAndRenderer(Transform anchor)
        {
            List<int> breakPointsIndices = breakPointIndexToJunctionTransform
                .Where(pair => pair.Value == anchor)
                .Select(pair => pair.Key)
                .ToList();

            List<Vector4> breakPoints = breakPointsIndices
                .Where(i => i >= 0 && i < positionSamplesWS.Count)
                .Select(i => positionSamplesWS[i])
                .ToList();

            List<Vector3> normals = breakPointsIndices
                .Where(i => i >= 0 && i < normalsWS.Count)
                .Select(i => normalsWS[i])
                .ToList();

            Transform segmentRoots = GetOrAddSegmentRoot();
            GameObject junctionGO = new GameObject($"~Junction_{anchor.name}");
            junctionGO.transform.SetParent(segmentRoots);
            junctionGO.transform.localPosition = Vector3.zero;
            junctionGO.transform.localRotation = Quaternion.identity;
            junctionGO.transform.localScale = Vector3.one;

            RiverSegment riverSegmentComp = junctionGO.AddComponent<RiverSegment>();
            m_segments.Add(riverSegmentComp);

            float widthRounded = Mathf.Ceil(meshDesc.width / meshDesc.vertexDistance) * meshDesc.vertexDistance;
            widthRounded = Mathf.Max(widthRounded, meshDesc.vertexDistance);
            int step = Mathf.FloorToInt(widthRounded / meshDesc.vertexDistance) + 1;



            List<Vector3> outterVerticesWS = new List<Vector3>();
            for (int iB = 0; iB < breakPoints.Count; ++iB)
            {
                Vector3 pWS = breakPoints[iB];
                Vector3 nWS = normals[iB];

                Vector3 startVertexWS = pWS + nWS * widthRounded * 0.5f;
                for (int iV = 0; iV < step; ++iV)
                {
                    Vector3 vWS = startVertexWS - nWS * meshDesc.vertexDistance * iV;
                    outterVerticesWS.Add(vWS);
                }
            }

            Vector3 centerVertexWS = ComputeCenterPoint(outterVerticesWS);

            //sort vertices [counter]-clock-wise
            outterVerticesWS.Sort((v0, v1) => { return Vector3.SignedAngle(v0 - centerVertexWS, Vector3.right, Vector3.up).CompareTo(Vector3.SignedAngle(v1 - centerVertexWS, Vector3.right, Vector3.up)); });

            {//In cases there is a long edge, usually between the end-vertex-of-first-break-point and start-vertex-of-second-break-point, break it into smaller pieces
                List<Vector3> additionalOutterVerticesWS = new List<Vector3>();
                for (int iV = 0; iV < outterVerticesWS.Count; ++iV)
                {
                    Vector3 p0WS = outterVerticesWS[iV];
                    Vector3 p1WS = outterVerticesWS[(iV + 1) % outterVerticesWS.Count];
                    Vector3 dirWS = (p1WS - p0WS).normalized;

                    int addVertexCount = Mathf.RoundToInt(Vector3.Distance(p0WS, p1WS) / meshDesc.vertexDistance);
                    addVertexCount = Mathf.Max(0, addVertexCount);
                    for (int iAV = 1; iAV < addVertexCount; ++iAV)
                    {
                        Vector3 addVertex = p0WS + iAV * dirWS * meshDesc.vertexDistance;
                        additionalOutterVerticesWS.Add(addVertex);
                    }
                }
                outterVerticesWS.AddRange(additionalOutterVerticesWS);
            }

            //new vertices added, sort the list again
            outterVerticesWS.Sort((v0, v1) => { return Vector3.SignedAngle(v0 - centerVertexWS, Vector3.right, Vector3.up).CompareTo(Vector3.SignedAngle(v1 - centerVertexWS, Vector3.right, Vector3.up)); });

            List<Vector3> delaunayVertices = GenerateVerticesForDelaunay(outterVerticesWS, centerVertexWS);
            debugDelaunayVerticesByAnchor[anchor] = delaunayVertices;


            List<Vector3> triangulatedVertices = Delaunay.TriangulateXZ(delaunayVertices, meshDesc.vertexDistance * 1.51f);
            int trisCount = triangulatedVertices.Count / 3;

            int vertexCount = 0;
            List<Vector3> verticesOS = new List<Vector3>();
            List<Vector4> texcoords0 = new List<Vector4>();
            List<Color> vertexColors = new List<Color>();
            List<int> indices = new List<int>();
            for (int iT = 0; iT < trisCount; ++iT)
            {
                Vector3 v0OS = junctionGO.transform.InverseTransformPoint(triangulatedVertices[iT * 3 + 0]);
                Vector3 v1OS = junctionGO.transform.InverseTransformPoint(triangulatedVertices[iT * 3 + 1]);
                Vector3 v2OS = junctionGO.transform.InverseTransformPoint(triangulatedVertices[iT * 3 + 2]);

                AddVerticesWithCorrectWinding(
                    verticesOS, texcoords0, vertexColors,
                    v0OS, v1OS, v2OS,
                    indices,
                    ref vertexCount);
            }

            Mesh junctionMesh = new Mesh() { name = junctionGO.name };
            junctionMesh.SetVertices(verticesOS);
            junctionMesh.SetUVs(0, texcoords0);
            junctionMesh.SetColors(vertexColors);
            junctionMesh.SetIndices(indices, 0, indices.Count, MeshTopology.Triangles, 0, true, 0);

            if (meshDesc.needNormals)
            {
                junctionMesh.RecalculateNormals();
            }
            else
            {
                junctionMesh.normals = null;
            }

            if (meshDesc.needTangents)
            {
                junctionMesh.RecalculateTangents();
            }
            else
            {
                junctionMesh.tangents = null;
            }

            riverSegmentComp.meshFilter.sharedMesh = junctionMesh;
            riverSegmentComp.meshRenderer.sharedMaterial = material;
        }

        private List<Vector3> GenerateVerticesForDelaunay(List<Vector3> outterVertices, Vector3 centerVertex)
        {
            List<Vector3> result = new List<Vector3>();

            for (int iV = 0; iV < outterVertices.Count; ++iV)
            {
                Vector3 outterVert = outterVertices[iV];
                Vector3 dir = Vector3.Normalize(centerVertex - outterVert);
                int step = Mathf.RoundToInt(Vector3.Distance(centerVertex, outterVert) / meshDesc.vertexDistance);
                for (int iStep = 1; iStep < step; ++iStep)
                {
                    Vector3 newVertex = outterVert + dir * meshDesc.vertexDistance * iStep;
                    result.Add(newVertex);
                }
            }

            result.Add(centerVertex);
            result.AddRange(outterVertices);

            return result;
        }

        private Vector3 ComputeCenterPoint(List<Vector3> points)
        {
            if (points.Count == 0)
                return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (Vector3 p in points)
            {
                sum += p;
            }
            return sum / points.Count;
        }

        private void AddVerticesWithCorrectWinding(List<Vector3> vertices, List<Vector4> texcoord0, List<Color> vertexColor, Vector4 v0, Vector4 v1, Vector4 v2, List<int> indices, ref int vertexCount)
        {
            if (Vector3.Cross(v1 - v0, v2 - v0).y < 0)
            {
                Vector3 temp = v1;
                v1 = v2;
                v2 = temp;
            }

            vertices.Add(v0); texcoord0.Add(v1); vertexColor.Add(v2); indices.Add(vertexCount++);
            vertices.Add(v1); texcoord0.Add(v2); vertexColor.Add(v0); indices.Add(vertexCount++);
            vertices.Add(v2); texcoord0.Add(v0); vertexColor.Add(v1); indices.Add(vertexCount++);
        }

        public Transform GetOrAddSegmentRoot()
        {
            Transform root = transform.Find(SEGMENT_ROOT_NAME);
            if (root == null)
            {
                root = new GameObject(SEGMENT_ROOT_NAME).transform;
                root.SetParent(transform);
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
            }
            return root;
        }

        public override void GetRenderers(List<MeshRenderer> container)
        {
            container.Clear();
            foreach (RiverSegment s in m_segments)
            {
                if (s.meshRenderer != null)
                {
                    container.Add(s.meshRenderer);
                }
            }
        }
    }
}

#endif