using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

namespace PhonePointer
{
    public sealed class PhoneSession
    {
        public ControlChannel Channel;
        public readonly PointerModel Model = new PointerModel();
        public string Session;
        internal string ResumeToken;
        public int ColorIndex;
        public PhoneTrackingMode TrackingMode { get; internal set; }
        public Color Color => SessionPalette.Get(ColorIndex);
        public bool Authenticated, SelectionBound;
        public double AcceptedAt, LastHeartbeat;
        public ConnectionStatus ConnectionStatus { get; internal set; }
        public DisconnectReason DisconnectReason { get; internal set; }
        public readonly ConnectionDiagnostics Diagnostics = new ConnectionDiagnostics();
        internal double RecoveryDeadline;
        internal bool AwaitingResumePose;
        public PointerStatus Status => Model.Status;
        public bool TryGetPointerNormalized(out Vector2 p) => Model.TryGetPointer(out p);
        public double PoseAge => NetworkClock.Now - Model.LastPoseReceived;
        public double RecoveryRemaining => Math.Max(0, RecoveryDeadline - NetworkClock.Now);
    }

    [DefaultExecutionOrder(-100)]
    public sealed class PhonePointerReceiver : MonoBehaviour, IPhonePointer
    {
        [Min(1), Tooltip("Number of ready phones required for initial game start. Additional phones may join.")]
        public int MaxPhones = 4;
        public PhonePointerSettings Settings;
        public ConnectionOptions Options = new ConnectionOptions();
        ConnectionOptions activeOptions;
        public ConnectionOptions ActiveOptions => activeOptions?.Copy();
        readonly List<PhoneSession> sessions = new List<PhoneSession>();
        readonly List<PhoneSession> pending = new List<PhoneSession>();
        readonly Dictionary<string, PhoneSession> byGuid = new Dictionary<string, PhoneSession>();
        int nextColorIndex;
        const int MaxPendingHandshakes=128; // Pending authentication only; not an active-phone cap.
        public IReadOnlyList<PhoneSession> Sessions => sessions;
        PhoneSession Primary => sessions.Count > 0 ? sessions[0] : null;
        public PointerStatus Status => Primary != null ? Primary.Status : PointerStatus.Disconnected;
        public event Action<Vector2> Selected;
        public event Action<PhoneSession, Vector2> SelectedBy;
        // 3D games need the accepted trigger pose even when its ray is outside the 2D screen.
        public event Action<PhoneSession, PoseSample> TriggerPressedBy;
        public event Action<PhoneSession, ConnectionStatus, DisconnectReason> ConnectionChanged;
        public event Action<PhoneSession, ConnectionDiagnostics> DiagnosticsChanged;
        public PairingInfo Pairing { get; private set; }
        public string Error { get; private set; }
        public bool Listening => transport != null;
        public bool GameStarted { get; private set; }
        public bool DisplayValid => Application.isEditor || (Screen.fullScreen && Screen.width == displayWidth && Screen.height == displayHeight);
        public float MarkerWidth { get; private set; }
        public Vector2 Size => screenSize;
        public int ConnectedCount { get { int n=0; foreach(var s in sessions) if(s.ConnectionStatus==ConnectionStatus.Connected) n++; return n; } }
        public int RegisteredCount { get { int n=0; foreach(var s in sessions) if(s.ConnectionStatus==ConnectionStatus.Connected && s.Model.Registered) n++; return n; } }
        public int CalibratedCount { get { int n=0; foreach(var s in sessions) if(s.ConnectionStatus==ConnectionStatus.Connected && s.Model.Registered && s.Model.Calibrated) n++; return n; } }
        public bool ExpectedPlayersConnected => Listening && ConnectedCount >= MaxPhones;
        public bool ExpectedPlayersRegistered => ExpectedPlayersConnected && RegisteredCount >= MaxPhones;
        public bool ExpectedPlayersCalibrated => ExpectedPlayersRegistered && CalibratedCount >= MaxPhones;
        public double PoseAge => Primary != null ? Primary.PoseAge : double.PositiveInfinity;
        public PointerModel Model => Primary != null ? Primary.Model : idleModel;
        readonly PointerModel idleModel = new PointerModel();
        PointerServerTransport transport;
        DiagnosticLog log;
        int displayWidth, displayHeight;
        Vector2 screenSize;
        bool wasDisplayValid;
        double nextDiagnostic;
        public string DiagnosticPath => log?.FilePath;
        public string DiagnosticFileError => log?.FileError;
        public ConnectionDiagnostics GetDiagnostics(string session) => session!=null && byGuid.TryGetValue(session, out var s) ? s.Diagnostics : null;
        public Task<string> ExportDiagnosticsAsync() => log != null ? log.ExportAsync(Path.Combine(Application.persistentDataPath,"PhonePointerExports")) : Task.FromResult<string>(null);

        public void StartServer(string address, Vector2 size) => StartServer(address,size,MaxPhones);
        public void StartServer(string address, Vector2 size, int maxPhones)
        {
            StopServer(); Error=null;
            if(!IPAddress.TryParse(address,out var ip) || ip.AddressFamily!=AddressFamily.InterNetwork ||
                !PointerMath.Finite(size.x)||!PointerMath.Finite(size.y)||size.x<.1f||size.y<.1f||size.x>5||size.y>5)
            { Error="Enter a local IPv4 address and display size between 10 and 500 cm."; return; }
            activeOptions=(Settings!=null ? Settings.options : Options)?.Copy() ?? new ConnectionOptions();
            if(!activeOptions.Validate(out var validation)) { Error=validation; return; }
            if(maxPhones<1) { Error="Enter a positive initial phone count."; return; }
            MaxPhones=maxPhones;
            try
            {
                transport=new PointerServerTransport(activeOptions,MaxPhones);
                Pairing=new PairingInfo { host=address,token=Guid.NewGuid().ToString("N"),tcpPort=transport.TcpPort,udpPort=transport.UdpPort };
                screenSize=size; displayWidth=Screen.width; displayHeight=Screen.height; wasDisplayValid=DisplayValid;
                MarkerWidth=ScreenMarkerLayout.ReferenceWidthMeters(size,Screen.width,Screen.height);
                try { log=new DiagnosticLog(Path.Combine(Application.persistentDataPath,"PhonePointerLogs"),"pc"); }
                catch(Exception e) { Error="Diagnostic file unavailable: "+e.Message; }
                log?.Add(null,"server",$"Protocol v2 TCP {Pairing.tcpPort} UDP {Pairing.udpPort}");
            }
            catch(Exception e) { Error="Cannot bind ports. Close conflicting listeners or choose different ports: "+e.Message; StopServer(); }
        }
        public void StopServer()
        {
            foreach(var s in sessions) { Change(s,ConnectionStatus.Disconnected,DisconnectReason.ServerStopped); s.Channel?.Dispose(); s.Model.Disconnect(); }
            foreach(var s in pending) s.Channel?.Dispose();
            sessions.Clear(); pending.Clear(); byGuid.Clear(); nextColorIndex=0;
            transport?.Dispose(); transport=null; GameStarted=false; Pairing=null;
            log?.Dispose(); log=null;
        }
        public bool TryGetPointerNormalized(out Vector2 p)
        { if(Primary!=null) return Primary.Model.TryGetPointer(out p); p=default; return false; }
        public bool TryGetPointerScreenPosition(out Vector2 p)
        { bool ok=TryGetPointerNormalized(out p); p=new Vector2(p.x*Screen.width,p.y*Screen.height); return ok; }
        void Update()
        {
            if(transport==null) return;
            double now=NetworkClock.Now;
            if(transport.Error!=null && Error!=transport.Error) { Error=transport.Error; log?.Add(null,"transportError",transport.Error); }
            while(transport.TryAccept(out var socket))
            {
                if(pending.Count>=MaxPendingHandshakes) { socket.Close(); continue; }
                var channel=new ControlChannel(socket); channel.Configure(activeOptions);
                pending.Add(new PhoneSession { Channel=channel,AcceptedAt=now,ConnectionStatus=ConnectionStatus.Connecting });
            }
            // Determine expiry before accepting a resume, including after a long main-thread stall.
            foreach(var s in sessions)
            {
                if(s.ConnectionStatus==ConnectionStatus.Connected && s.Channel.Closed)
                    while(s.Channel.Incoming.TryDequeue(out var terminal))
                    {
                        ControlMessage m=null; try { m=JsonUtility.FromJson<ControlMessage>(terminal.Json); } catch { }
                        if(m!=null && (m.type=="paused" || m.type=="lost" || m.type=="trackingLost")) Handle(s,terminal,now);
                    }
                if(s.ConnectionStatus==ConnectionStatus.Connected && (s.Channel.Closed || now-s.Channel.LastReceived>activeOptions.silenceSeconds))
                    Reconnecting(s,s.Channel.Closed ? s.Channel.Reason : DisconnectReason.Timeout,
                        s.Channel.Closed ? s.Channel.ClosedAt : s.Channel.LastReceived+activeOptions.silenceSeconds);
            }
            for(int i=sessions.Count-1;i>=0;i--)
                if(sessions[i].ConnectionStatus==ConnectionStatus.Reconnecting && now>=sessions[i].RecoveryDeadline) Drop(sessions[i]);
            for(int i=pending.Count-1;i>=0;i--)
            {
                var s=pending[i];
                if(s.Channel.Incoming.TryDequeue(out var input)) Authenticate(s,input,now);
                if(s.Authenticated || s.Channel.Closed || now-s.AcceptedAt>4) { if(!s.Authenticated) s.Channel.Dispose(); pending.RemoveAt(i); }
            }
            bool validDisplay=DisplayValid;
            if(wasDisplayValid && !validDisplay)
            { Error="Display changed. Restore fullscreen and register again."; foreach(var s in sessions) Reregister(s,DisconnectReason.DisplayChanged); }
            wasDisplayValid=validDisplay;
            foreach(var s in sessions)
            {
                double poseProcessing=0;
                if(s.ConnectionStatus==ConnectionStatus.Connected)
                {
                    if(transport.TryTakePose(s.Session,out var data) && data.Address.Equals(s.Channel.Address))
                    {
                        var pose=data.Pose;
                        double started=NetworkClock.Now;
                        if(s.Model.AcceptPose(pose,data.Time))
                        { if(pose.tracking) s.AwaitingResumePose=false; s.Diagnostics.LastUdpReceived=data.Time; s.Diagnostics.CaptureToSendSeconds=Math.Max(0,pose.sendTime-pose.time); }
                        poseProcessing=NetworkClock.Now-started;
                    }
                    while(s.Channel.Incoming.TryDequeue(out var input)) Handle(s,input,now);
                }
                double tickStarted=NetworkClock.Now;
                s.Model.Tick(tickStarted,Time.unscaledDeltaTime);
                s.Diagnostics.PoseProcessingSeconds=poseProcessing+NetworkClock.Now-tickStarted;
                if(!DisplayValid) s.Model.SuspendInput();
                else if(!s.Model.InputEnabled && s.ConnectionStatus==ConnectionStatus.Connected)
                    s.Model.ResumeConnection(s.Model.Generation,now);
                if(now>=nextDiagnostic)
                {
                    s.Diagnostics.LastTcpReceived=s.Channel.LastReceived;
                    s.Diagnostics.RttSeconds=s.Channel.Clock.Rtt; s.Diagnostics.ClockUncertaintySeconds=s.Channel.Clock.Uncertainty;
                    s.Diagnostics.FrameSeconds=Time.unscaledDeltaTime; s.Diagnostics.SmoothingSeconds=s.Model.SmoothingSeconds;
                    s.Diagnostics.ReplacedPoses=transport.GetReplacedPoses(s.Session);
                    DiagnosticsChanged?.Invoke(s,s.Diagnostics);
                    log?.Add(s.Session,"metrics",$"tcpAge={now-s.Diagnostics.LastTcpReceived:F3} udpAge={s.PoseAge:F3} rtt={s.Diagnostics.RttSeconds:F4} frame={Time.unscaledDeltaTime:F4} compute={s.Diagnostics.PoseProcessingSeconds:F6}");
                }
            }
            if(now>=nextDiagnostic) nextDiagnostic=now+1;
            if(ExpectedPlayersCalibrated) GameStarted=true;
        }
        void Authenticate(PhoneSession candidate,ReceivedText input,double now)
        {
            ControlMessage m;
            try { m=JsonUtility.FromJson<ControlMessage>(input.Json); } catch { candidate.Channel.Dispose(); return; }
            if(m==null || m.version!=2) { Reject(candidate,"Protocol version mismatch. Update both PC and Android to v2."); return; }
            if(m.trackingMode!=PhoneTrackingMode.AR && m.trackingMode!=PhoneTrackingMode.AirMouse) { Reject(candidate,"Unsupported tracking mode."); return; }
            if(m.type!="hello") { Reject(candidate,"Expected v2 hello."); return; }
            if(m.sensitivity != 0 && !PointerMath.ValidSensitivity(m.sensitivity))
            { Reject(candidate,"Invalid sensitivity. Reset the phone sensitivity and reconnect."); return; }
            if(!string.IsNullOrEmpty(m.resumeToken))
            {
                if(!byGuid.TryGetValue(m.session ?? "",out var old) || old.ResumeToken!=m.resumeToken ||
                    (old.ConnectionStatus!=ConnectionStatus.Reconnecting && old.ConnectionStatus!=ConnectionStatus.Connected) ||
                    (old.ConnectionStatus==ConnectionStatus.Reconnecting && now>=old.RecoveryDeadline))
                { Reject(candidate,"Resume rejected. Scan the PC QR again."); return; }
                // A valid resume may reach us before the old socket's FIN/timeout. The resume
                // token authorises replacing that transport; the generation retires old input.
                if(old.ConnectionStatus==ConnectionStatus.Connected)
                { log?.Add(old.Session,"resume","Replacing previous transport before its close was observed"); Reconnecting(old,DisconnectReason.None,now); }
                bool preserve=m.trackingMode==old.TrackingMode && m.anchorValid && old.Model.Registered && m.revision==old.Model.Revision;
                old.Channel.Dispose(); old.Channel=candidate.Channel; candidate.Authenticated=true;
                old.Model.ResumeConnection(old.Model.Generation+1,now); old.AwaitingResumePose=preserve;
                if(m.sensitivity != 0) old.Model.SetSensitivity(m.sensitivity);
                old.TrackingMode=m.trackingMode;
                if(!preserve) old.Model.BeginRegistration();
                Change(old,ConnectionStatus.Connected,DisconnectReason.None); SendConfig(old,preserve); return;
            }
            if(Pairing==null || m.token!=Pairing.token) { Reject(candidate,"Pairing rejected. Scan the current PC QR."); return; }
            if(nextColorIndex==int.MaxValue) { Reject(candidate,"Session color identifiers exhausted. Restart the server."); return; }
            candidate.Authenticated=true; candidate.Session=Guid.NewGuid().ToString("N"); candidate.ResumeToken=Guid.NewGuid().ToString("N");
            candidate.TrackingMode=m.trackingMode;
            candidate.ColorIndex=nextColorIndex++; // Never recycle a departed session's color within this server run.
            candidate.Model.Connect(candidate.Session,screenSize,now);
            candidate.Model.ResumeConnection(1,now);
            candidate.Model.SetSensitivity(m.sensitivity == 0 ? 1f : m.sensitivity);
            candidate.Model.PoseStaleSeconds=activeOptions.poseStaleSeconds; candidate.Model.SmoothingSeconds=activeOptions.smoothingSeconds;
            candidate.Model.Selected+=uv=> { SelectedBy?.Invoke(candidate,uv); if(candidate==Primary) Selected?.Invoke(uv); };
            sessions.Add(candidate); byGuid[candidate.Session]=candidate;
            Change(candidate,ConnectionStatus.Connected,DisconnectReason.None); SendConfig(candidate,false);
        }
        void Reject(PhoneSession s,string reason)
        {
            Error=reason;
            s.Channel.Send(new ControlMessage { type="rejected",text=reason });
            log?.Add(null,"rejected",reason);
            // Let the writer deliver the explanation before closing; no Unity operations here.
            var rejected=s.Channel; Task.Run(async()=> { await Task.Delay(150); rejected.Dispose(); });
        }
        void Handle(PhoneSession s,ReceivedText input,double now)
        {
            ControlMessage m;
            try { m=JsonUtility.FromJson<ControlMessage>(input.Json); } catch { return; }
            if(m==null || m.version!=2 || m.session!=s.Session || m.generation!=s.Model.Generation) return;
            if(m.type!="paused" && m.revision!=s.Model.Revision) return;
            if(m.type=="mode")
            {
                if(m.trackingMode!=PhoneTrackingMode.AR && m.trackingMode!=PhoneTrackingMode.AirMouse) return;
                s.TrackingMode=m.trackingMode;
                log?.Add(s.Session,"mode",s.TrackingMode.ToString());
                Reregister(s,DisconnectReason.None); return;
            }
            if(m.type=="reregister" || m.type=="lost" || m.type=="paused")
            {
                var reason=m.type=="paused" ? DisconnectReason.AppPaused : m.type=="lost" ? DisconnectReason.AnchorLost : DisconnectReason.None;
                Reregister(s,reason);
                if(m.type=="paused") Reconnecting(s,reason,input.Time);
                return;
            }
            if(m.revision!=s.Model.Revision) return;
            if(m.type=="sensitivity")
            {
                if(s.Model.SetSensitivity(m.sensitivity))
                {
                    log?.Add(s.Session,"sensitivity",m.sensitivity.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
                    Send(s,new ControlMessage { type="sensitivity",sensitivity=s.Model.Sensitivity,sensitivitySupported=true });
                }
                else log?.Add(s.Session,"inputRejected","Invalid sensitivity");
                return;
            }
            if(m.type=="trackingLost") { s.Model.LoseTracking(); Change(s,s.ConnectionStatus,DisconnectReason.TrackingLost); log?.Add(s.Session,"tracking","Tracking lost; CENTER required"); return; }
            if(m.type=="registered" && DisplayValid && !s.Model.Registered) { s.Model.MarkRegistered(); return; }
            if(m.type=="diagnostics")
            {
                s.Diagnostics.ArState=m.arState; s.Diagnostics.TrackingReason=m.trackingReason;
                s.Diagnostics.ActualPoseRate=m.poseRate; s.Diagnostics.PhoneFrameSeconds=m.frameSeconds;
                log?.Add(s.Session,"ar",$"{m.arState} {m.trackingReason} rate={m.poseRate:F1} phoneFrame={m.frameSeconds:F4}"); return;
            }
            if(m.type=="error") { Error=m.text; return; }
            if(m.type=="release") { if(s.Model.AcceptEvent(m.eventId)) s.Model.Release(); return; }
            if(m.type!="calibrate" && m.type!="select" && m.type!="press") return;
            now=NetworkClock.Now; // Validate at processing time, not the frame's earlier start time.
            string rejection=null;
            bool fresh=m.pose!=null && s.Channel.Clock.EventFresh(m.pose.time,input.Time,now,activeOptions.eventLifetimeSeconds,out rejection);
            bool ok=fresh && DisplayValid && !s.AwaitingResumePose && s.Model.AcceptEvent(m.eventId);
            if(m.type=="calibrate")
            {
                ok=ok && s.Model.Registered && s.Model.Calibrate(m.pose,now);
                Send(s,new ControlMessage { type="calibrated",text=ok ? "ok" : rejection ?? "Wait for registration/tracking and CENTER again." });
            }
            else if(m.type=="select") ok=ok && (GameStarted || ExpectedPlayersCalibrated) && s.Model.Select(m.pose,now);
            else
            {
                ok=ok && (GameStarted || ExpectedPlayersCalibrated) && s.Model.Press(m.pose);
                if(ok) TriggerPressedBy?.Invoke(s,m.pose);
            }
            if(!ok) { s.Diagnostics.RejectedEvents++; log?.Add(s.Session,"inputRejected",m.type+": "+(rejection ?? "Invalid state, snapshot or duplicate event")); }
        }
        public void Reregister()
        {
            if(Screen.fullScreen || Application.isEditor) { displayWidth=Screen.width; displayHeight=Screen.height; }
            MarkerWidth=ScreenMarkerLayout.ReferenceWidthMeters(screenSize,Screen.width,Screen.height);
            foreach(var s in sessions) Reregister(s,DisconnectReason.None);
        }
        void Reregister(PhoneSession s,DisconnectReason reason)
        {
            s.Model.BeginRegistration(); s.AwaitingResumePose=false; s.DisconnectReason=reason; s.Diagnostics.LastReason=reason;
            ConnectionChanged?.Invoke(s,s.ConnectionStatus,reason);
            log?.Add(s.Session,"registration",reason.ToString());
            if(s.ConnectionStatus==ConnectionStatus.Connected) SendConfig(s,false);
        }
        void SendConfig(PhoneSession s,bool preserve)
        {
            if(!preserve && s.TrackingMode==PhoneTrackingMode.AirMouse)
            { s.Model.MarkRegistered(); s.Model.LoseTracking(); }
            transport.RegisterSession(s.Session,s.Model.Revision,s.Model.Generation,s.Channel.Address);
            Send(s,new ControlMessage { type="config",width=screenSize.x,height=screenSize.y,
                markerWidth=MarkerWidth,resumeToken=s.ResumeToken,preserve=preserve,options=activeOptions.Copy(),sensitivity=s.Model.Sensitivity,sensitivitySupported=true,trackingMode=s.TrackingMode,airMouseSupported=true });
        }
        void Send(PhoneSession s,ControlMessage m)
        { m.session=s.Session; m.revision=s.Model.Revision; m.generation=s.Model.Generation; s.Channel?.Send(m); }
        void Reconnecting(PhoneSession s,DisconnectReason reason,double suspectedAt)
        {
            if(s.ConnectionStatus==ConnectionStatus.Reconnecting) return;
            s.RecoveryDeadline=suspectedAt+activeOptions.recoverySeconds; s.Model.SuspendInput(); s.Channel.Dispose();
            if(!string.IsNullOrEmpty(s.Channel.Error)) log?.Add(s.Session,"socketError",s.Channel.Error);
            Change(s,ConnectionStatus.Reconnecting,reason);
        }
        void Change(PhoneSession s,ConnectionStatus status,DisconnectReason reason)
        {
            s.ConnectionStatus=status; s.DisconnectReason=reason; s.Diagnostics.LastReason=reason;
            log?.Add(s.Session,"connection",status+" "+reason); ConnectionChanged?.Invoke(s,status,reason);
        }
        void Drop(PhoneSession s)
        {
            Change(s,ConnectionStatus.Disconnected,DisconnectReason.RecoveryExpired);
            byGuid.Remove(s.Session); transport.RemoveSession(s.Session); s.Channel.Dispose(); s.Model.Disconnect(); sessions.Remove(s);
        }
        public void MarkPointerDisplayed(PhoneSession s)
        { if(Event.current==null || Event.current.type==EventType.Repaint) s.Diagnostics.ReceiveToDisplaySeconds=NetworkClock.Now-s.Model.LastPoseReceived; }
        void OnDestroy() => StopServer();
        void OnApplicationQuit() => StopServer();
    }
}
