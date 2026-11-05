using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PhonePointer
{
    public enum PhoneTrackingMode { AR = 0, AirMouse = 1 }

    [Serializable]
    public sealed class PairingInfo
    {
        public int version = 2;
        public string host, token;
        public int tcpPort = 47777, udpPort = 47778;
        public bool IsValid() => version == 2 && System.Net.IPAddress.TryParse(host, out var ip) &&
            ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && tcpPort > 0 && tcpPort <= 65535 &&
            udpPort > 0 && udpPort <= 65535 && token != null && token.Length == 32;
    }

    [Serializable]
    public sealed class ControlMessage
    {
        public string type, token, session, text, resumeToken;
        public int version = 2, revision, generation;
        public bool anchorValid, preserve;
        public double sentAt, t0, t1, t2;
        public long nonce;
        public ConnectionOptions options;
        public long eventId;
        public float width, height, markerWidth;
        // Zero means a legacy v2 peer omitted the field. Nonzero values must be validated.
        public float sensitivity;
        public bool sensitivitySupported;
        public PhoneTrackingMode trackingMode;
        public bool airMouseSupported;
        public PoseSample pose;
        public string arState, trackingReason;
        public double poseRate;
        public double frameSeconds;
    }

    public static class FrameCodec
    {
        public const int MaxLength = 16384;
        public static byte[] Encode(string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            if (body.Length == 0 || body.Length > MaxLength) throw new InvalidDataException("Invalid message size");
            byte[] result = new byte[body.Length + 4];
            result[0] = (byte)(body.Length >> 24); result[1] = (byte)(body.Length >> 16);
            result[2] = (byte)(body.Length >> 8); result[3] = (byte)body.Length;
            Buffer.BlockCopy(body, 0, result, 4, body.Length); return result;
        }
        public static string Read(Stream stream)
        {
            var head = ReadExactly(stream, 4);
            int length = (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
            if (length <= 0 || length > MaxLength) throw new InvalidDataException("Invalid message size");
            return Encoding.UTF8.GetString(ReadExactly(stream, length));
        }
        static byte[] ReadExactly(Stream stream, int count)
        {
            var data = new byte[count]; int offset = 0;
            while (offset < count) { int read = stream.Read(data, offset, count-offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
            return data;
        }
    }

    public static class PoseCodec
    {
        // v2: magic, GUID, registration revision, connection generation, sequence,
        // capture time, send time, tracking, position, quaternion. Little endian, 81 bytes.
        public const int Size = 81;
        public static byte[] Encode(PoseSample p)
        { var bytes = new byte[Size]; EncodeInto(p, bytes); return bytes; }
        public static void EncodeInto(PoseSample p, byte[] bytes, byte[] sessionBytes = null)
        {
            if (bytes == null || bytes.Length != Size) throw new ArgumentException("Invalid pose buffer");
            int i=0; Put(bytes, ref i, 0x32505048, 4);
            Buffer.BlockCopy(sessionBytes ?? Guid.ParseExact(p.session,"N").ToByteArray(),0,bytes,i,16); i+=16;
            Put(bytes,ref i,p.revision,4); Put(bytes,ref i,p.generation,4); Put(bytes,ref i,p.sequence,8);
            Put(bytes,ref i,BitConverter.DoubleToInt64Bits(p.time),8); Put(bytes,ref i,BitConverter.DoubleToInt64Bits(p.sendTime),8);
            bytes[i++]=p.tracking ? (byte)1 : (byte)0;
            Float(bytes,ref i,p.position.x); Float(bytes,ref i,p.position.y); Float(bytes,ref i,p.position.z);
            Float(bytes,ref i,p.rotation.x); Float(bytes,ref i,p.rotation.y); Float(bytes,ref i,p.rotation.z); Float(bytes,ref i,p.rotation.w);
        }
        public static bool TryDecode(byte[] bytes, out PoseSample p)
        {
            p=null; if(bytes==null || bytes.Length!=Size) return false;
            int i=0; if(Get(bytes,ref i,4)!=0x32505048) return false;
            var guid=new byte[16]; Buffer.BlockCopy(bytes,i,guid,0,16); i+=16;
            p=new PoseSample { session=new Guid(guid).ToString("N"), revision=(int)Get(bytes,ref i,4), generation=(int)Get(bytes,ref i,4), sequence=Get(bytes,ref i,8),
                time=BitConverter.Int64BitsToDouble(Get(bytes,ref i,8)), sendTime=BitConverter.Int64BitsToDouble(Get(bytes,ref i,8)), tracking=bytes[i++]!=0,
                position=new Vector3(Float(bytes,ref i),Float(bytes,ref i),Float(bytes,ref i)),
                rotation=new Quaternion(Float(bytes,ref i),Float(bytes,ref i),Float(bytes,ref i),Float(bytes,ref i)) };
            return PointerMath.ValidPose(p) && !double.IsNaN(p.sendTime) && !double.IsInfinity(p.sendTime);
        }
        static void Put(byte[] b,ref int i,long v,int count) { for(int n=0;n<count;n++) b[i++]=(byte)((ulong)v>>(n*8)); }
        static long Get(byte[] b,ref int i,int count) { ulong v=0; for(int n=0;n<count;n++) v|=(ulong)b[i++]<<(n*8); return (long)v; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct FloatBits { [System.Runtime.InteropServices.FieldOffset(0)] public float value; [System.Runtime.InteropServices.FieldOffset(0)] public int bits; }
        static void Float(byte[] b,ref int i,float v) { var f=new FloatBits { value=v }; Put(b,ref i,f.bits,4); }
        static float Float(byte[] b,ref int i) { var f=new FloatBits { bits=(int)Get(b,ref i,4) }; return f.value; }
    }
}
