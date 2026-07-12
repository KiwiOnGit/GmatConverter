using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace GmatConverter
{
    // A minimal triangle mesh: positions/normals/uvs indexed in parallel, plus a flat
    // triangle-list index buffer (3 indices per triangle).
    public class SimpleMesh
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] UVs;
        public int[] Indices;
    }

    // Parses the handful of fields this project needs out of a Unity-native "as project"
    // exported Mesh .asset (AssetRipper YAML). Not a general YAML parser -- these files have a
    // very regular, predictable structure, so simple line scanning is enough and avoids
    // pulling in a YAML dependency for three small bundled files.
    //
    // Vertex data layout: Unity packs the mesh's channels (a fixed 14-slot order -- Position,
    // Normal, Tangent, Color, TexCoord0..7, BlendWeight, BlendIndices) across one or more
    // interleaved streams. Each channel says which stream it lives in, its byte offset within
    // that stream's per-vertex stride, its component format and dimension. Streams are packed
    // back-to-back in _typelessdata, each one's start padded up to a 16-byte boundary.
    public static class UnityMeshAsset
    {
        private struct Channel
        {
            public int Stream, Offset, Format, Dimension;
        }

        public static SimpleMesh Parse(string yaml)
        {
            string[] lines = yaml.Replace("\r\n", "\n").Split('\n');

            int vertexCount = 0;
            var channels = new List<Channel>();
            string typelessHex = null;
            string indexHex = null;
            int indexFormat = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("m_IndexFormat:"))
                {
                    indexFormat = int.Parse(t.Substring("m_IndexFormat:".Length).Trim(), CultureInfo.InvariantCulture);
                }
                else if (t.StartsWith("m_IndexBuffer:"))
                {
                    indexHex = t.Substring("m_IndexBuffer:".Length).Trim();
                }
                else if (t.StartsWith("m_VertexCount:"))
                {
                    vertexCount = int.Parse(t.Substring("m_VertexCount:".Length).Trim(), CultureInfo.InvariantCulture);
                }
                else if (t.StartsWith("m_Channels:"))
                {
                    int j = i + 1;
                    while (j + 3 < lines.Length && lines[j].TrimStart().StartsWith("- stream:"))
                    {
                        channels.Add(new Channel
                        {
                            Stream = ParseIntAfter(lines[j], "- stream:"),
                            Offset = ParseIntAfter(lines[j + 1], "offset:"),
                            Format = ParseIntAfter(lines[j + 2], "format:"),
                            Dimension = ParseIntAfter(lines[j + 3], "dimension:")
                        });
                        j += 4;
                    }
                    i = j - 1;
                }
                else if (t.StartsWith("_typelessdata:"))
                {
                    typelessHex = t.Substring("_typelessdata:".Length).Trim();
                }
            }

            if (vertexCount == 0 || channels.Count < 5 || string.IsNullOrEmpty(typelessHex))
                throw new Exception("Could not parse mesh asset (unexpected format).");

            byte[] data = HexToBytes(typelessHex);
            byte[] indexBytes = HexToBytes(indexHex ?? "");

            // Fixed Unity channel order: 0=Position 1=Normal 2=Tangent 3=Color 4=TexCoord0 ...
            Channel posCh = channels[0];
            Channel normCh = channels[1];
            Channel uvCh = channels[4];

            int streamCount = 0;
            foreach (var c in channels) streamCount = Math.Max(streamCount, c.Stream + 1);

            int[] stride = new int[streamCount];
            foreach (var c in channels)
            {
                if (c.Dimension <= 0) continue;
                int compSize = FormatSize(c.Format);
                stride[c.Stream] = Math.Max(stride[c.Stream], c.Offset + c.Dimension * compSize);
            }

            int[] streamStart = new int[streamCount];
            for (int s = 1; s < streamCount; s++)
                streamStart[s] = Align16(streamStart[s - 1] + vertexCount * stride[s - 1]);

            var positions = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            for (int v = 0; v < vertexCount; v++)
            {
                int posBase = streamStart[posCh.Stream] + v * stride[posCh.Stream] + posCh.Offset;
                positions[v] = new Vector3(
                    BitConverter.ToSingle(data, posBase),
                    BitConverter.ToSingle(data, posBase + 4),
                    BitConverter.ToSingle(data, posBase + 8));

                int normBase = streamStart[normCh.Stream] + v * stride[normCh.Stream] + normCh.Offset;
                normals[v] = new Vector3(
                    BitConverter.ToSingle(data, normBase),
                    BitConverter.ToSingle(data, normBase + 4),
                    BitConverter.ToSingle(data, normBase + 8));

                int uvBase = streamStart[uvCh.Stream] + v * stride[uvCh.Stream] + uvCh.Offset;
                uvs[v] = new Vector2(
                    BitConverter.ToSingle(data, uvBase),
                    BitConverter.ToSingle(data, uvBase + 4));
            }

            int[] indices;
            if (indexFormat == 1)
            {
                indices = new int[indexBytes.Length / 4];
                for (int k = 0; k < indices.Length; k++)
                    indices[k] = BitConverter.ToInt32(indexBytes, k * 4);
            }
            else
            {
                indices = new int[indexBytes.Length / 2];
                for (int k = 0; k < indices.Length; k++)
                    indices[k] = BitConverter.ToUInt16(indexBytes, k * 2);
            }

            return new SimpleMesh { Positions = positions, Normals = normals, UVs = uvs, Indices = indices };
        }

        private static int ParseIntAfter(string line, string key)
        {
            int idx = line.IndexOf(key, StringComparison.Ordinal);
            return int.Parse(line.Substring(idx + key.Length).Trim(), CultureInfo.InvariantCulture);
        }

        // Unity VertexAttributeFormat component byte sizes (only the ones these meshes use).
        private static int FormatSize(int format) => format switch
        {
            0 => 4, // Float32
            1 => 2, // Float16
            2 => 1, // UNorm8
            3 => 1, // SNorm8
            4 => 2, // UNorm16
            5 => 2, // SNorm16
            6 => 1, // UInt8
            7 => 1, // SInt8
            8 => 2, // UInt16
            9 => 2, // SInt16
            10 => 4, // UInt32
            11 => 4, // SInt32
            _ => 4
        };

        private static int Align16(int n) => (n + 15) & ~15;

        private static byte[] HexToBytes(string hex)
        {
            int len = hex.Length / 2;
            var bytes = new byte[len];
            for (int i = 0; i < len; i++)
                bytes[i] = (byte)((HexVal(hex[i * 2]) << 4) | HexVal(hex[i * 2 + 1]));
            return bytes;
        }

        private static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }
    }
}
