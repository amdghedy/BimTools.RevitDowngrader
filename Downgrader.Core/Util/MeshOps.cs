using System.Collections.Generic;
using Downgrader.Core.Storage;

namespace Downgrader.Core.Util
{
    public static class MeshOps
    {
        static int Find(int[] parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        /// <summary>Splits a mesh into its connected components (one closed shell each), so every shell can become its own solid.</summary>
        public static List<MeshRec> Split(MeshRec m)
        {
            int vc = m.Vertices.Length / 3;
            var parent = new int[vc];
            for (int i = 0; i < vc; i++) parent[i] = i;
            for (int i = 0; i + 2 < m.Indices.Length; i += 3)
                for (int k = 0; k < 2; k++)
                {
                    int a = Find(parent, m.Indices[i + k]), b = Find(parent, m.Indices[i + k + 1]);
                    if (a != b) parent[a] = b;
                }
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i + 2 < m.Indices.Length; i += 3)
            {
                int root = Find(parent, m.Indices[i]);
                List<int> g;
                if (!groups.TryGetValue(root, out g)) { g = new List<int>(); groups[root] = g; }
                g.Add(i);
            }
            var result = new List<MeshRec>();
            foreach (List<int> g in groups.Values)
            {
                var remap = new Dictionary<int, int>();
                var verts = new List<double>();
                var idx = new List<int>();
                foreach (int t in g)
                    for (int k = 0; k < 3; k++)
                    {
                        int v = m.Indices[t + k], nv;
                        if (!remap.TryGetValue(v, out nv))
                        {
                            nv = remap.Count;
                            remap[v] = nv;
                            verts.Add(m.Vertices[v * 3]); verts.Add(m.Vertices[v * 3 + 1]); verts.Add(m.Vertices[v * 3 + 2]);
                        }
                        idx.Add(nv);
                    }
                result.Add(new MeshRec { Material = m.Material, Vertices = verts.ToArray(), Indices = idx.ToArray() });
            }
            return result;
        }
    }
}
