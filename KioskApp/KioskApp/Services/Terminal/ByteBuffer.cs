using System.Collections.Generic;

namespace OmniKiosk.Wpf.Services.Terminal
{
    public class ByteBuffer
    {
        private readonly List<byte> _buf = new List<byte>();

        public void Add(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            _buf.AddRange(data);
        }

        public byte[] ToArray() => _buf.ToArray();

        public int Length => _buf.Count;

        public void DropFront(int count)
        {
            if (count <= 0) return;
            if (count >= _buf.Count) { _buf.Clear(); return; }
            _buf.RemoveRange(0, count);
        }

        public void Clear() => _buf.Clear();
    }
}
