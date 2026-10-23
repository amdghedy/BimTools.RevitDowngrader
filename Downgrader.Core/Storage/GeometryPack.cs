using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Downgrader.Core.Storage
{
    /// <summary>Triangle mesh for one material. Vertices are x,y,z triples in feet.</summary>
    public class MeshRec
    {
        public string Material { get; set; }
        public double[] Vertices { get; set; }
        public int[] Indices { get; set; }
    }

    /// <summary>Binary sidecar for geometry: model.json stores only an offset per element, so the JSON stays small.</summary>
    public class GeometryPackWriter : IDisposable
    {
        readonly FileStream _fs;
        readonly BinaryWriter _w;

        public GeometryPackWriter(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            _fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            _w = new BinaryWriter(_fs, Encoding.UTF8);
            _w.Write(0x4B504744);   // magic
            _w.Write(1);            // version
        }

        public long Write(IList<MeshRec> meshes)
        {
            long offset = _fs.Position;
            _w.Write(meshes.Count);
            foreach (MeshRec m in meshes)
            {
                _w.Write(m.Material ?? "");
                _w.Write(m.Vertices.Length / 3);
                foreach (double d in m.Vertices) _w.Write(d);
                _w.Write(m.Indices.Length);
                foreach (int i in m.Indices) _w.Write(i);
            }
            return offset;
        }

        public void Dispose() { _w.Flush(); _w.Dispose(); _fs.Dispose(); }
    }

    public class GeometryPackReader : IDisposable
    {
        readonly FileStream _fs;
        readonly BinaryReader _r;

        public GeometryPackReader(string path)
        {
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            _r = new BinaryReader(_fs, Encoding.UTF8);
            if (_r.ReadInt32() != 0x4B504744) throw new InvalidDataException("Not a geometry pack: " + path);
            _r.ReadInt32();
        }

        public List<MeshRec> Read(long offset)
        {
            _fs.Position = offset;
            int n = _r.ReadInt32();
            var list = new List<MeshRec>(n);
            for (int k = 0; k < n; k++)
            {
                var m = new MeshRec { Material = _r.ReadString() };
                int vc = _r.ReadInt32();
                m.Vertices = new double[vc * 3];
                for (int i = 0; i < m.Vertices.Length; i++) m.Vertices[i] = _r.ReadDouble();
                int ic = _r.ReadInt32();
                m.Indices = new int[ic];
                for (int i = 0; i < ic; i++) m.Indices[i] = _r.ReadInt32();
                list.Add(m);
            }
            return list;
        }

        public void Dispose() { _r.Dispose(); _fs.Dispose(); }
    }
}
