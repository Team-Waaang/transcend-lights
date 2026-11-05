using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

namespace PhonePointer
{
    public static class NetworkClock { public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; }
    public sealed class ReceivedText { public string Json; public double Time; }
    public sealed class ReceivedPose { public byte[] Bytes; public PoseSample Pose; public double Time; public IPAddress Address; }

    public sealed class ControlChannel : IDisposable
    {
        readonly TcpClient client;
        readonly BlockingCollection<byte[]> outgoing = new BlockingCollection<byte[]>(64);
        public readonly ConcurrentQueue<ReceivedText> Incoming = new ConcurrentQueue<ReceivedText>();
        public volatile bool Closed;
        public string Error { get; private set; }
        public IPAddress Address { get; }
        public readonly ClockEstimate Clock = new ClockEstimate();
        long lastReceivedBits = BitConverter.DoubleToInt64Bits(NetworkClock.Now);
        public double LastReceived => BitConverter.Int64BitsToDouble(Interlocked.Read(ref lastReceivedBits));
        public DisconnectReason Reason { get; private set; }
        public double ClosedAt { get; private set; }
        readonly CancellationTokenSource stopping = new CancellationTokenSource();
        readonly object closing = new object();
        volatile ConnectionOptions options = new ConnectionOptions();
        readonly Dictionary<long, double> probes = new Dictionary<long, double>();
        long nonce;
        public void Configure(ConnectionOptions value)
        { if (!value.Validate(out var error)) throw new ArgumentException(error); options = value.Copy(); client.SendTimeout=Math.Max(1,(int)Math.Ceiling(value.silenceSeconds*1000)); }

        public ControlChannel(TcpClient socket)
        {
            client = socket; client.NoDelay = true; client.SendTimeout = 2000;
            Address = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
            Task.Run(ReadLoop); Task.Run(WriteLoop); Task.Run(HeartbeatLoop);
        }
        public void Send(ControlMessage message) => Send(JsonUtility.ToJson(message));
        async Task HeartbeatLoop()
        {
            try
            {
                while (!Closed)
                {
                    double now = NetworkClock.Now;
                    long id = Interlocked.Increment(ref nonce);
                    lock (probes) { if (probes.Count >= 32) probes.Clear(); probes[id] = now; }
                    Send(new ControlMessage { type = "clockPing", nonce = id, t0 = now });
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(.001,options.heartbeatSeconds)), stopping.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!Closed) Fail(e.Message); }
        }
        public void Send(string json)
        {
            if (Closed) return;
            try { if (!outgoing.TryAdd(FrameCodec.Encode(json))) Fail("Control queue overflow"); }
            catch (InvalidOperationException) { }
        }
        void ReadLoop()
        {
            try
            {
                var stream = client.GetStream();
                while (!Closed)
                {
                    string json = FrameCodec.Read(stream);
                    double received = NetworkClock.Now;
                    Interlocked.Exchange(ref lastReceivedBits, BitConverter.DoubleToInt64Bits(received));
                    ControlMessage message = null;
                    try { message = JsonUtility.FromJson<ControlMessage>(json); } catch (ArgumentException) { }
                    if (message != null && message.version == 2 && message.type == "clockPing")
                    {
                        Send(new ControlMessage { type = "clockPong", nonce = message.nonce, t0 = message.t0,
                            t1 = received, t2 = NetworkClock.Now });
                        continue;
                    }
                    if (message != null && message.version == 2 && message.type == "clockPong")
                    {
                        lock (probes)
                            if (probes.TryGetValue(message.nonce, out var started) && started == message.t0)
                            { probes.Remove(message.nonce); Clock.Observe(started, message.t1, message.t2, received); }
                        continue;
                    }
                    if (Incoming.Count >= 128) throw new System.IO.IOException("Receive queue overflow");
                    Incoming.Enqueue(new ReceivedText { Json = json, Time = received });
                }
            }
            catch (Exception e) { if (!Closed) Fail(e.Message); }
        }
        void WriteLoop()
        {
            try
            {
                var stream = client.GetStream();
                foreach (var bytes in outgoing.GetConsumingEnumerable())
                {
                    if (Closed) break;
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception e) { if (!Closed) Fail(e.Message); }
            finally { Dispose(); }
        }
        public void CloseAfterSending()
        { lock(closing) { if(Closed) return; stopping.Cancel(); outgoing.CompleteAdding(); } }
        void Fail(string error) { lock(closing) { if(Closed) return; Error = error; Reason = error.Contains("overflow") ? DisconnectReason.QueueOverflow : DisconnectReason.SocketError; Dispose(); } }
        public void Dispose() { lock(closing) { if (Closed) return; ClosedAt = NetworkClock.Now; Closed = true; stopping.Cancel(); outgoing.CompleteAdding(); client.Close(); } }
    }

    public sealed class PointerServerTransport : IDisposable
    {
        readonly TcpListener listener;
        readonly UdpClient udp;
        readonly ConcurrentQueue<TcpClient> pending = new ConcurrentQueue<TcpClient>();
        readonly Dictionary<string, ReceivedPose> latest = new Dictionary<string, ReceivedPose>();
        readonly Dictionary<string, Tuple<int,int,IPAddress>> routes = new Dictionary<string, Tuple<int,int,IPAddress>>();
        readonly Dictionary<string,long> replacements = new Dictionary<string,long>();
        public long GetReplacedPoses(string session) { lock(latest) return replacements.TryGetValue(session,out var n) ? n : 0; }
        public void RegisterSession(string session,int revision,int generation,IPAddress address)
        { lock(latest) { routes[session]=Tuple.Create(revision,generation,address); if(!replacements.ContainsKey(session)) replacements[session]=0; latest.Remove(session); } }
        public void RemoveSession(string session) { lock(latest) { routes.Remove(session); latest.Remove(session); replacements.Remove(session); } }
        public long ReplacedPoses { get; private set; }
        public bool TryTakePose(string session, out ReceivedPose pose)
        { lock (latest) { if (!latest.TryGetValue(session, out pose)) return false; latest.Remove(session); return true; } }
        volatile bool closed;
        readonly int maxPending;
        public string Error { get; private set; }
        public int TcpPort => ((IPEndPoint)listener.LocalEndpoint).Port;
        public int UdpPort => ((IPEndPoint)udp.Client.LocalEndPoint).Port;
        public PointerServerTransport(ConnectionOptions options, int maxClients)
        {
            if(!options.Validate(out var error)) throw new ArgumentException(error);
            maxPending=128; // Bounded unauthenticated backlog, independent of active participant count.
            listener=BindTcp(options,maxPending);
            try { udp=BindUdp(options); } catch { listener.Stop(); throw; }
            Task.Run(AcceptLoop); Task.Run(UdpLoop);
        }
        static TcpListener BindTcp(ConnectionOptions o,int backlog)
        {
            if (!o.Validate(out var error)) throw new ArgumentException(error);
            for (int port = o.tcpPort; port <= Math.Min(65535,o.tcpPort + (o.automaticPorts ? 20 : 0)); port++)
            {
                var probe = new TcpListener(IPAddress.Any, port); probe.Server.ExclusiveAddressUse = true;
                try { probe.Start(backlog); return probe; }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse) { probe.Stop(); }
                catch { probe.Stop(); throw; }
            }
            throw new SocketException((int)SocketError.AddressAlreadyInUse);
        }
        static UdpClient BindUdp(ConnectionOptions o)
        {
            for (int port = o.udpPort; port <= Math.Min(65535,o.udpPort + (o.automaticPorts ? 20 : 0)); port++)
            {
                var probe = new UdpClient(AddressFamily.InterNetwork);
                {
                    probe.Client.ExclusiveAddressUse = true;
                    try { probe.Client.Bind(new IPEndPoint(IPAddress.Any, port)); return probe; }
                    catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse) { probe.Close(); }
                    catch { probe.Close(); throw; }
                }
            }
            throw new SocketException((int)SocketError.AddressAlreadyInUse);
        }
        public PointerServerTransport(int tcpPort, int udpPort, int maxClients = 1)
        {
            // Compatibility parameter retained; only unauthenticated sockets are bounded.
            maxPending = 128;
            listener = new TcpListener(IPAddress.Any, tcpPort);
            listener.Server.ExclusiveAddressUse = true;
            try { listener.Start(maxPending); udp = new UdpClient(udpPort); }
            catch { listener.Stop(); udp?.Close(); throw; }
            Task.Run(AcceptLoop); Task.Run(UdpLoop);
        }
        async Task AcceptLoop()
        {
            try
            {
                while (!closed)
                {
                    var socket = await listener.AcceptTcpClientAsync();
                    if (closed || pending.Count >= maxPending) socket.Close(); else pending.Enqueue(socket);
                }
            }
            catch (Exception e) { if (!closed) Error = e.Message; }
        }
        async Task UdpLoop()
        {
            try
            {
                while (!closed)
                {
                    var packet = await udp.ReceiveAsync();
                    if (packet.Buffer.Length != PoseCodec.Size) continue;
                    if (!PoseCodec.TryDecode(packet.Buffer, out var pose)) continue;
                    var received = new ReceivedPose { Bytes = packet.Buffer, Pose=pose, Address = packet.RemoteEndPoint.Address, Time = NetworkClock.Now };
                    lock (latest)
                    {
                        if(!routes.TryGetValue(pose.session,out var route) || pose.revision!=route.Item1 || pose.generation!=route.Item2 || !route.Item3.Equals(received.Address)) continue;
                        if (latest.TryGetValue(pose.session, out var previous))
                        {
                            var old=previous.Pose;
                            if (pose.generation < old.generation || (pose.generation == old.generation && pose.sequence <= old.sequence)) continue;
                            ReplacedPoses++;
                            replacements[pose.session]++;
                        }
                        // Authenticated routes bound this table to one latest pose per session.
                        latest[pose.session] = received;
                    }
                }
            }
            catch (Exception e) { if (!closed) Error = e.Message; }
        }
        public bool TryAccept(out TcpClient socket) => pending.TryDequeue(out socket);
        public void Dispose()
        {
            closed = true; listener.Stop(); udp.Close();
            while (pending.TryDequeue(out var socket)) socket.Close();
        }
    }
}
