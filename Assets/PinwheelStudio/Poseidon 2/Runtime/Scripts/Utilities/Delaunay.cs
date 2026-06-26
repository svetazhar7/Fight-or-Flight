#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Pinwheel.Poseidon
{
    public static class Delaunay
    {
        struct Line : IEquatable<Line>
        {
            public Vector3 A { get; private set; }
            public Vector3 B { get; private set; }

            public Line(Vector3 a, Vector3 b)
            {
                A = a;
                B = b;
            }

            public bool Equals(Line other)
            {
                return
                    (A == other.A && B == other.B) ||
                    (A == other.B && B == other.A);
            }

            public float length
            {
                get
                {
                    return Vector3.Distance(A, B);
                }
            }
        }

        struct Tris : IEquatable<Tris>
        {
            public Vector3 A { get; private set; }
            public Vector3 B { get; private set; }
            public Vector3 C { get; private set; }

            public Line[] edges { get; private set; }

            public Tris(Vector3 a, Vector3 b, Vector3 c)
            {
                if (IsCounterClockWise(a, b, c))
                {
                    A = a;
                    B = b;
                    C = c;
                }
                else
                {
                    A = a;
                    B = c;
                    C = b;
                }

                edges = new Line[3]
                {
                    new Line(A, B),
                    new Line(B, C),
                    new Line(C, A)
                };
            }

            public bool IsPointInsideCircumcircle(Vector2 p)
            {
                float ax = A.x - p.x, ay = A.y - p.y;
                float bx = B.x - p.x, by = B.y - p.y;
                float cx = C.x - p.x, cy = C.y - p.y;

                float det = (ax * ax + ay * ay) * (bx * cy - cx * by)
                          - (bx * bx + by * by) * (ax * cy - cx * ay)
                          + (cx * cx + cy * cy) * (ax * by - bx * ay);

                return det > 0; // assuming counter-clockwise vertex order
            }

            public static bool IsCounterClockWise(Vector2 a, Vector2 b, Vector2 c)
            {
                return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) > 0;
            }

            public bool Equals(Tris other)
            {
                return
                    (A == other.A && B == other.B && C == other.C) ||
                    (A == other.B && B == other.C && C == other.A) ||
                    (A == other.C && B == other.A && C == other.B);
            }

            public bool ContainsEdge(Line e)
            {
                return edges[0].Equals(e) || edges[1].Equals(e) || edges[2].Equals(e);
            }

            public bool ContainsVertex(Vector3 v)
            {
                return A == v || B == v || C == v;
            }
        }

        public static List<Vector3> TriangulateXZ(List<Vector3> vertices, float maxEdgeLength = -1)
        {
            //flip the Y and Z component so the process will be computed on XZ perspective
            //newZ will contain oldY so we can reconstruct the original points later
            List<Vector3> verticesXZY = vertices.Select(v => new Vector3(v.x, v.z, v.y)).ToList();

            List<Tris> triangles = new List<Tris>();
            float minX = verticesXZY.Min(v => v.x);
            float minY = verticesXZY.Min(v => v.y);
            float maxX = verticesXZY.Max(v => v.x);
            float maxY = verticesXZY.Max(v => v.y);

            Vector3 superPointA = new Vector3(minX, minY, 0);
            Vector3 midPointAB = new Vector3(minX, maxY, 0);
            Vector3 superPointB = 2 * midPointAB - superPointA;
            Vector3 midPointAC = new Vector3(maxX, minY, 0);
            Vector3 superPointC = 2 * midPointAC - superPointA;

            Tris superTris = new Tris(superPointA, superPointB, superPointC);
            triangles.Add(superTris);

            List<Tris> invalidTris = new List<Tris>();
            List<Line> holeEdges = new List<Line>();
            foreach (Vector3 v in verticesXZY)
            {
                invalidTris.Clear();

                foreach (Tris tris in triangles)
                {
                    if (tris.IsPointInsideCircumcircle(v))
                    {
                        invalidTris.Add(tris);
                    }
                }

                holeEdges.Clear();
                foreach (Tris tris in invalidTris)
                {
                    foreach (Line edge in tris.edges)
                    {
                        bool shared = invalidTris.Any(other => !other.Equals(tris) && other.ContainsEdge(edge));
                        if (!shared)
                            holeEdges.Add(edge);
                    }
                }

                triangles.RemoveAll(t => invalidTris.Contains(t));

                foreach (Line edge in holeEdges)
                    triangles.Add(new Tris(edge.A, edge.B, v));
            }

            // Remove triangles that use super-triangle vertices
            triangles.RemoveAll(t =>
                t.ContainsVertex(superPointA) || t.ContainsVertex(superPointB) || t.ContainsVertex(superPointC));

            //Remove triangle exceed max length, usually buggy ones that overlap others
            //Just a hack, there is a bug somewhere
            if (maxEdgeLength > 0)
            {
                triangles.RemoveAll(t =>
                {
                    foreach (Line e in t.edges)
                    {
                        if (e.length > maxEdgeLength)
                            return true;
                    }
                    return false;
                });
            }

            List<Vector3> result = new List<Vector3>();
            foreach (Tris t in triangles)
            {
                Vector3 a = t.A;
                Vector3 b = t.B;
                Vector3 c = t.C;
                result.Add(new Vector3(a.x, a.z, a.y));
                result.Add(new Vector3(b.x, b.z, b.y));
                result.Add(new Vector3(c.x, c.z, c.y));
            }

            return result;
        }
    }
}

#endif