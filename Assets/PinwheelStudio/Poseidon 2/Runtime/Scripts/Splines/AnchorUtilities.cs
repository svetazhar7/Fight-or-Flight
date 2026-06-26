#if POSEIDON_2
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Pinwheel.Poseidon
{
    public static class AnchorUtilities
    {
        public static void Insert(List<Vector3> anchors, Vector3 newAnchor)
        {
            if (anchors.Count < 2)
            {
                anchors.Add(newAnchor);
            }
            else
            {
                int insertIndex = GetInsertIndex(anchors, newAnchor);
                anchors.Insert(insertIndex, newAnchor);
            }
        }

        public static int GetInsertIndex(List<Vector3> anchors, Vector3 newAnchor)
        {
            int insertIndex = -1;
            if (anchors.Count < 2)
            {
                return insertIndex;
            }

            float d = 0;
            float minDistance = float.MaxValue;
            Vector3 center;

            center = (anchors[0] + anchors[anchors.Count - 1]) * 0.5f;
            d = Vector3.Distance(newAnchor, center);
            if (d < minDistance)
            {
                minDistance = d;
                insertIndex = 0;
            }
            for (int i = 1; i < anchors.Count; ++i)
            {
                center = (anchors[i] + anchors[i - 1]) * 0.5f;
                d = Vector3.Distance(newAnchor, center);
                if (d < minDistance)
                {
                    minDistance = d;
                    insertIndex = i;
                }
            }
            return insertIndex;
        }

    }
}

#endif