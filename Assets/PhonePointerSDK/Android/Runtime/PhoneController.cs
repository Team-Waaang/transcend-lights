using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace PhonePointer
{
    public sealed class PhoneController : MonoBehaviour
    {
        public string Message { get; private set; } = "Starting AR camera...";
        public bool Connected => authenticated && channel != null && !channel.Closed && session != null;
        public ConnectionStatus ConnectionStatus { get; private set; }
        public DisconnectReason DisconnectReason { get; private set; }
        const string SensitivityKey = "PhonePointer.Sensitivity";
        public float Sensitivity { get; private set; } = 1f;
        public float AppliedSensitivity { get; private set; } = 1f;
        public bool SensitivitySupported { get; private set; }
        bool sensitivityDirty, saveSensitivity;
        double nextSensitivity;
        void Awake()
        {
            float saved=PlayerPrefs.GetFloat(SensitivityKey,1f);
            Sensitivity=PointerMath.ValidSensitivity(saved) ? saved : 1f;
        }
        public void SetSensitivity(float value)
        {
            if(!PointerMath.ValidSensitivity(value) || Mathf.Approximately(value,Sensitivity)) return;
            PressUp(); Sensitivity=value; sensitivityDirty=saveSensitivity=true;
            PlayerPrefs.SetFloat(SensitivityKey,value);
        }
        public void SaveSensitivity() { if(saveSensitivity) { PlayerPrefs.Save(); saveSensitivity=false; } }
        void FlushSensitivity()
        {
            if(!sensitivityDirty || !Connected || !SensitivitySupported) return;
            Send(new ControlMessage {type="sensitivity",sensitivity=Sensitivity});
            sensitivityDirty=false; nextSensitivity=NetworkClock.Now+.1;
        }
        public PhoneTrackingMode TrackingMode { get; private set; }
        public bool AirMouseSupported { get; private set; }
        public int PlayerSlot { get; private set; }
        public bool PlayerSlotAssigned { get; private set; }
        public bool ModeSwitchPending { get; private set; }
        bool airCentered;
        Quaternion airReference=Quaternion.identity;
        float gyroReadyAt;
        Vector2 airScreenSize;
        public bool GyroAvailable
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return SystemInfo.supportsGyroscope;
#else
                return false;
#endif
            }
        }
        Quaternion GyroAttitude()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return AirMouseMath.ToUnityAttitude(Input.gyro.attitude);
#else
            return Quaternion.identity;
#endif
        }
        void ApplyTrackingMode()
        {
            bool air=TrackingMode==PhoneTrackingMode.AirMouse;
            images.enabled=false;
            gyroReadyAt=Time.unscaledTime+.25f;
            cameraManager.enabled=!air; arSession.enabled=!air;
#if ENABLE_LEGACY_INPUT_MANAGER
            if(SystemInfo.supportsGyroscope) { Input.gyro.enabled=air; Input.gyro.updateInterval=1f/60f; }
#endif
            ApplyCameraVisibility();
        }
        public void ToggleTrackingMode()
        {
            if(!Connected || !AirMouseSupported || ModeSwitchPending) return;
            var next=TrackingMode==PhoneTrackingMode.AR ? PhoneTrackingMode.AirMouse : PhoneTrackingMode.AR;
            if(next==PhoneTrackingMode.AirMouse && !GyroAvailable)
            { Message="Gyroscope unavailable. Enable legacy input (Both) in Player Settings on supported phones."; return; }
            PressUp(); ModeSwitchPending=true; ClearRegistration();
            Send(new ControlMessage {type="mode",trackingMode=next});
            Message="Switching tracking mode...";
        }
        public bool CameraVisible => TrackingMode==PhoneTrackingMode.AR && (!registered || cameraVisible);
        public void ToggleCamera() { cameraVisible = !cameraVisible; ApplyCameraVisibility(); }
        public string DiagnosticPath => log?.FilePath;
        public string DiagnosticFileError => log?.FileError;
        public Task<string> ExportDiagnosticsAsync() => log != null ? log.ExportAsync(Path.Combine(Application.persistentDataPath,"PhonePointerExports")) : Task.FromResult<string>(null);
        DiagnosticLog log;
        [SerializeField] ARCameraBackground cameraBackground;
        bool rigReady;
        bool cameraVisible, authenticated, recovering;
        string resumeToken;
        int connectionGeneration, retry;
        ConnectionOptions options = new ConnectionOptions();
        double recoveryDeadline, nextRetry, nextMetrics, previousFrame, nextPose;
        int sentPoses;
        double lastMetricsTime;
        readonly PoseSample framePose = new PoseSample();
        byte[] poseBuffer = new byte[PoseCodec.Size], sessionBytes;
        void ApplyCameraVisibility()
        {
            if (cameraBackground != null) cameraBackground.enabled = CameraVisible;

        }
        public bool Registered => registered;
        public bool Calibrated { get; private set; }
        bool GyroReady()
        {
            if(!GyroAvailable || Time.unscaledTime<gyroReadyAt) return false;
            var q=GyroAttitude();
            float norm=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
            return PointerMath.Finite(norm) && Mathf.Abs(norm-1)<.02f;
        }
        public bool Tracking => TrackingMode==PhoneTrackingMode.AirMouse ? GyroReady() : anchor != null && anchor.trackingState == TrackingState.Tracking && ARSession.state == ARSessionState.SessionTracking;
        [SerializeField] ARSession arSession;
        [SerializeField] XROrigin origin;
        [SerializeField] Camera arCamera;
        [SerializeField] ARCameraManager cameraManager;
        [SerializeField] ARTrackedImageManager images;
        ARAnchor anchor;
        ControlChannel channel;
        TcpClient connectingSocket;
        Task connectTask;
        Task<string> decodeTask;
        UdpClient udp;
        PairingInfo pairing;
        string session;
        int revision, generation;
        long sequence, eventId;
        bool registered, libraryReady, paused, destroyed, wasTracking;
        float markerWidth, stableSince = -1, nextScan, anchorCreatedAt;
        Pose candidatePose;
        TrackableId candidateId;
        Guid referenceGuid;
        void OnEnable() { ARSession.stateChanged+=OnArStateChanged; }
        void OnDisable() { ARSession.stateChanged-=OnArStateChanged; }
        void OnArStateChanged(ARSessionStateChangedEventArgs args)
        {
            if(TrackingMode==PhoneTrackingMode.AR && registered && args.state==ARSessionState.SessionInitializing)
            {
                if(Connected) Send(new ControlMessage {type="lost"});
                ClearRegistration(); DisconnectReason=DisconnectReason.AnchorLost;
                log?.Add(session,"arReset","AR session initializing; registration invalidated");
            }
        }

        IEnumerator Start()
        {
            rigReady=arSession!=null && origin!=null && arCamera!=null && cameraManager!=null && cameraBackground!=null && images!=null;
            if(!rigReady)
            { Message="AR rig references missing. Use the PhoneController prefab or assign the Inspector bindings."; Debug.LogError(Message,this); yield break; }
            arSession.enabled=false; images.enabled=false;
            try { log=new DiagnosticLog(Path.Combine(Application.persistentDataPath,"PhonePointerLogs"),"android"); }
            catch(Exception e) { Debug.LogWarning("Phone Pointer diagnostic file unavailable: "+e.Message); }
            Screen.orientation = ScreenOrientation.Portrait;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                float deadline = Time.realtimeSinceStartup + 30;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                { Message = "Camera permission required. Enable it in Android Settings, then restart."; yield break; }
            }
#endif
            yield return ARSession.CheckAvailability();
            if (ARSession.state == ARSessionState.NeedsInstall) yield return ARSession.Install();
            if (ARSession.state == ARSessionState.Unsupported || ARSession.state == ARSessionState.NeedsInstall)
            { Message = "ARCore unavailable. Install/update Google Play Services for AR."; yield break; }
            arSession.enabled = true;
            cameraManager.enabled = true;
            ApplyCameraVisibility();
            Message = "Scan the QR inside the PC square and keep the entire square in view.";
        }

        void Update()
        {
            if(paused || !rigReady) return;
            double now=NetworkClock.Now;
            if(now>=nextSensitivity) FlushSensitivity();
            if(TrackingMode==PhoneTrackingMode.AR && registered && anchor==null)
            { if(Connected) Send(new ControlMessage {type="lost"}); ClearRegistration(); DisconnectReason=DisconnectReason.AnchorLost; }
            if(registered && !Tracking) Calibrated=false;
            if(recovering && now>=recoveryDeadline) { ForgetConnection(DisconnectReason.RecoveryExpired,"Recovery expired. Scan PC QR again."); }
            if(connectTask!=null && connectTask.IsCompleted)
            {
                var completed=connectTask; connectTask=null;
                if(completed.IsFaulted || completed.IsCanceled)
                {
                    if(completed.Exception!=null) log?.Add(session,"connectError",completed.Exception.GetBaseException().Message);
                    connectingSocket?.Close(); connectingSocket=null;
                    if(recovering) ScheduleRetry(now);
                    else ForgetConnection(DisconnectReason.SocketError,"Connection failed. Check Wi-Fi, displayed ports and PC firewall; scan QR again.");
                }
                else
                {
                    try
                    {
                        channel=new ControlChannel(connectingSocket); channel.Configure(options); connectingSocket=null;
                        udp?.Close(); udp=new UdpClient(); udp.Connect(pairing.host,pairing.udpPort);
                        channel.Send(new ControlMessage { type="hello",token=recovering ? null : pairing.token,
                            session=recovering ? session : null,resumeToken=recovering ? resumeToken : null,
                            revision=revision,anchorValid=registered && Tracking && (TrackingMode==PhoneTrackingMode.AR || airCentered) && !ModeSwitchPending,sensitivity=Sensitivity,trackingMode=TrackingMode });
                        Message="Connected. Negotiating v2 settings...";
                    }
                    catch(Exception e)
                    { log?.Add(session,"connectError",e.Message); channel?.Dispose(); channel=null; udp?.Close(); udp=null; connectingSocket?.Close(); connectingSocket=null;
                        if(recovering) ScheduleRetry(now); else ForgetConnection(DisconnectReason.SocketError,"Network setup failed. Check permissions, Wi-Fi and ports; scan QR again."); }
                }
            }
            if(channel!=null)
            {
                while(channel.Incoming.TryDequeue(out var input))
                {
                    try { Handle(JsonUtility.FromJson<ControlMessage>(input.Json)); }
                    catch(Exception e) { Message="Invalid PC message: "+e.Message; }
                    if(channel==null) break;
                }
                if(channel!=null && (channel.Closed || now-channel.LastReceived>options.silenceSeconds))
                {
                    var reason=channel.Closed ? channel.Reason : DisconnectReason.Timeout;
                    double suspected=channel.Closed ? channel.ClosedAt : channel.LastReceived+options.silenceSeconds;
                    BeginRecovery(reason,suspected);
                }
            }
            if(!Connected)
            {
                if(recovering && channel==null && connectTask==null && now>=nextRetry) Connect(pairing);
                else if(!recovering && channel==null && connectTask==null) ScanQr();
                ApplyCameraVisibility(); return;
            }
            if(TrackingMode==PhoneTrackingMode.AR)
            {
                // A temporarily limited camera/anchor requires CENTER; loss of the actual anchor
                // or reinitialisation of the AR session requires a new spatial registration.
                if(registered && (anchor==null || ARSession.state==ARSessionState.SessionInitializing))
                { RequestRegistration(); DisconnectReason=DisconnectReason.AnchorLost; }
                if(!registered && libraryReady) ObserveImage();
                if(anchor!=null && !registered && anchor.trackingState!=TrackingState.Tracking && Time.unscaledTime-anchorCreatedAt>5)
                { Destroy(anchor.gameObject); anchor=null; stableSince=-1; Message="Anchor could not stabilize. Scan the screen again."; }
                if(anchor!=null && !registered && anchor.trackingState==TrackingState.Tracking)
                {
                    registered=true; Calibrated=true; images.enabled=false; cameraVisible=false;
                    Send(new ControlMessage { type="registered" });
                    Message="Ready. Aim with the TOP edge. CENTER is optional fine-tuning.";
                }
                if(registered && !Tracking)
                {
                    Calibrated=false; Message="Tracking lost. Uncover camera, move slowly, then CENTER.";
                    if(Pressing) PressUp();
                    if(wasTracking) { Send(new ControlMessage { type="trackingLost" }); log?.Add(session,"tracking",ARSession.notTrackingReason.ToString()); }
                }
                if(registered && Tracking && !wasTracking && !Calibrated) Message="Tracking recovered. Aim at center and tap CENTER.";
            }
            else if(!airCentered) Message="Air mouse. Hold your aiming posture and tap CENTER.";
            wasTracking=Tracking;
            ApplyCameraVisibility();
            if(now>=nextMetrics)
            {
                double rate=lastMetricsTime>0 ? sentPoses/Math.Max(.001,now-lastMetricsTime) : 0;
                Send(new ControlMessage { type="diagnostics",arState=TrackingMode==PhoneTrackingMode.AirMouse ? "AirMouse" : ARSession.state.ToString(),trackingReason=TrackingMode==PhoneTrackingMode.AirMouse ? (airCentered ? "Gyroscope" : "CenterRequired") : ARSession.notTrackingReason.ToString(),poseRate=rate,frameSeconds=Time.unscaledDeltaTime });
                log?.Add(session,"metrics",$"captures={sentPoses} frame={now-previousFrame:F4} ar={ARSession.state} reason={ARSession.notTrackingReason}");
                sentPoses=0; nextMetrics=now+1; lastMetricsTime=now;
            }
            previousFrame=now;
        }
        void LateUpdate()
        {
            if(paused || !Connected || !registered || udp==null || (TrackingMode==PhoneTrackingMode.AirMouse && !airCentered)) return;
            double now=NetworkClock.Now;
            if(now<nextPose) return;
            nextPose=now+1.0/options.poseRate; // Never send accumulated old scheduling slots.
            var pose=Snapshot(framePose); pose.sendTime=NetworkClock.Now;
            PoseCodec.EncodeInto(pose,poseBuffer,sessionBytes);
            try { udp.Send(poseBuffer,poseBuffer.Length); sentPoses++; }
            catch(Exception e) { log?.Add(session,"udpError",e.GetType().Name); Message="Pose send failed. See diagnostic log."; }
        }
        void BeginRecovery(DisconnectReason reason,double suspected)
        {
            if(string.IsNullOrEmpty(resumeToken)) { ForgetConnection(reason,"Connection ended. Scan PC QR again."); return; }
            if(!recovering) { recovering=true; recoveryDeadline=suspected+options.recoverySeconds; retry=0; }
            DisconnectReason=reason; ConnectionStatus=ConnectionStatus.Reconnecting; authenticated=false; Pressing=false;
            channel?.Dispose(); channel=null; udp?.Close(); udp=null;
            log?.Add(session,"connection","Reconnecting "+reason); ScheduleRetry(NetworkClock.Now);
        }
        void ScheduleRetry(double now)
        { nextRetry=now+(retry==0 ? .5 : retry==1 ? 1 : 2); retry++; Message="Reconnecting to PC. Registration retained while anchor remains valid."; }
        void ForgetConnection(DisconnectReason reason,string message)
        { Disconnect(); pairing=null; resumeToken=null; recovering=false; ConnectionStatus=ConnectionStatus.Disconnected; DisconnectReason=reason; Message=message; log?.Add(null,"connection",reason.ToString()); }

        void ScanQr()
        {
            if (decodeTask != null)
            {
                if (!decodeTask.IsCompleted) return;
                string value = decodeTask.Status == TaskStatus.RanToCompletion ? decodeTask.Result : null;
                decodeTask = null;
                if (!string.IsNullOrEmpty(value))
                {
                    try
                    {
                        var info = JsonUtility.FromJson<PairingInfo>(value);
                        if (info != null && info.version != 2) Message="Protocol mismatch. Update both PC and Android to v2.";
                        if (info != null && info.IsValid()) { Connect(info); return; }
                    }
                    catch (ArgumentException) { }
                }
            }
            if (Time.unscaledTime < nextScan || !cameraManager.TryAcquireLatestCpuImage(out var image)) return;
            nextScan = Time.unscaledTime + .35f;
            using (image)
            {
                // ARCore Y plane: copy with actual row/pixel strides. No second camera session.
                var plane = image.GetPlane(0);
                int step = Mathf.Max(1, image.width / 800), width = image.width / step, height = image.height / step;
                var pixels = new byte[width * height];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    pixels[y * width + x] = plane.data[y * step * plane.rowStride + x * step * plane.pixelStride];
                decodeTask = Task.Run(() => QrUtility.Decode(pixels, width, height));
            }
        }

        void Connect(PairingInfo info)
        {
            pairing = info; connectingSocket = new TcpClient();
            ConnectionStatus=recovering ? ConnectionStatus.Reconnecting : ConnectionStatus.Connecting;
            var socket = connectingSocket;
            connectTask = Task.Run(async () =>
            {
                var attempt = socket.ConnectAsync(info.host, info.tcpPort);
                if (await Task.WhenAny(attempt, Task.Delay(2000)) != attempt) { socket.Close(); throw new TimeoutException(); }
                await attempt;
            });
            Message = "Connecting to " + info.host + "...";
        }

        void Handle(ControlMessage m)
        {
            if(m==null) return;
            if(m.version!=2) { ForgetConnection(DisconnectReason.VersionMismatch,"Update both PC and Android to protocol v2."); return; }
            if(m.type=="rejected") { ForgetConnection(m.text!=null && m.text.Contains("version") ? DisconnectReason.VersionMismatch : DisconnectReason.ResumeRejected,m.text); return; }
            if(m.type=="config")
            {
                if((m.trackingMode!=PhoneTrackingMode.AR && m.trackingMode!=PhoneTrackingMode.AirMouse) || !PointerMath.Finite(m.width) || !PointerMath.Finite(m.height) || m.width<=0 || m.height<=0 || !Guid.TryParseExact(m.session,"N",out var guid) || !PointerMath.Finite(m.markerWidth) || m.markerWidth<=0 || m.markerWidth>5 || m.options==null || !m.options.Validate(out _)) return;
                bool preserve=m.preserve && m.trackingMode==TrackingMode && session==m.session && revision==m.revision && registered && Tracking;
                PlayerSlotAssigned=m.playerSlotAssigned;
                PlayerSlot=m.playerSlotAssigned ? Mathf.Max(0,m.playerSlot) : 0;
                TrackingMode=m.trackingMode; AirMouseSupported=m.airMouseSupported; ModeSwitchPending=false;
                airScreenSize=new Vector2(m.width,m.height);
                session=m.session; sessionBytes=guid.ToByteArray(); revision=m.revision; connectionGeneration=m.generation;
                resumeToken=m.resumeToken; markerWidth=m.markerWidth; options=m.options.Copy(); channel.Configure(options);
                authenticated=true; recovering=false; ConnectionStatus=ConnectionStatus.Connected; DisconnectReason=DisconnectReason.None;
                sequence=eventId=0; Pressing=false; nextPose=0;
                SensitivitySupported=m.sensitivitySupported;
                AppliedSensitivity=PointerMath.ValidSensitivity(m.sensitivity) ? m.sensitivity : 1f;
                sensitivityDirty=SensitivitySupported && !Mathf.Approximately(Sensitivity,AppliedSensitivity);
                FlushSensitivity();
                if(!preserve)
                {
                    ClearRegistration(); ApplyTrackingMode();
                    if(TrackingMode==PhoneTrackingMode.AirMouse)
                    { registered=true; Message="Air mouse: hold your aiming posture and tap CENTER."; }
                    else if(!m.preserve) StartCoroutine(BuildLibrary(generation));
                    if(TrackingMode==PhoneTrackingMode.AR) Message="Connected. Keep the SAME square in view until registration completes.";
                    if(m.preserve) Send(new ControlMessage { type="reregister" });
                }
                else
                {
                    Message=Calibrated ? "Reconnected. Aim and select." : "Tracking recovered. Tap CENTER.";
                    if(!Calibrated) Send(new ControlMessage {type="trackingLost"});
                }
                log?.Add(session,"config",preserve ? "Preserved registration" : "Registration required");
            }
            else if(m.session==session && m.revision==revision && m.generation==connectionGeneration && m.type=="sensitivity")
            { if(PointerMath.ValidSensitivity(m.sensitivity)) AppliedSensitivity=m.sensitivity; }
            else if(m.session==session && m.revision==revision && m.generation==connectionGeneration && m.type=="calibrated")
            { if(m.text=="ok") Calibrated=true; Message=m.text=="ok" ? "Ready. Aim with top edge; tap SELECT." : m.text; }
        }

        IEnumerator BuildLibrary(int buildGeneration)
        {
            // Manager needs an active subsystem, created by the Android XR loader.
            float deadline=Time.unscaledTime+10;
            while (images.subsystem == null && buildGeneration == generation && Time.unscaledTime<deadline) { images.enabled = true; yield return null; }
            if (buildGeneration != generation) yield break;
            images.enabled = false;
            if(images.subsystem==null) { Message="Image tracking unavailable. Verify the Android ARCore loader and restart."; yield break; }
            var library = images.CreateRuntimeLibrary() as MutableRuntimeReferenceImageLibrary;
            if (library == null) { Message = "This AR provider cannot add reference images."; yield break; }
            // Game project: the phone tracks the rune slab image; ScreenMarker is the PC display marker here.
            var texture = Resources.Load<Texture2D>("RuneSlabReference") ?? Resources.Load<Texture2D>("ScreenMarker");
            if (texture == null) { Message = "ScreenMarker resource missing. Run project setup."; yield break; }
            var job = library.ScheduleAddImageWithValidationJob(texture, "ScreenMarker", markerWidth);
            // Do not stop this coroutine mid-job: the native texture memory must remain alive.
            while (!job.jobHandle.IsCompleted) yield return null;
            job.jobHandle.Complete();
            if (buildGeneration != generation || destroyed) yield break;
            if (job.status != AddReferenceImageJobStatus.Success)
            { Message = "Reference image rejected: " + job.status; Send(new ControlMessage { type = "error", text = Message }); yield break; }
            referenceGuid=library[0].guid;
            images.referenceLibrary = library; images.enabled = true; libraryReady = true;
        }

        void ObserveImage()
        {
            if (anchor != null || ARSession.state != ARSessionState.SessionTracking) return;
            ARTrackedImage found = null;
            foreach (var image in images.trackables) if (image.referenceImage.guid==referenceGuid && image.trackingState == TrackingState.Tracking) { found = image; break; }
            if (found == null) { stableSince = -1; return; }
            // AR image lies in XZ: +X right, -Z image up, +Y outward normal.
            var pose = new Pose(found.transform.position, Quaternion.LookRotation(found.transform.up, -found.transform.forward));
            if (stableSince < 0 || found.trackableId != candidateId || Vector3.Distance(candidatePose.position, pose.position) > .015f || Quaternion.Angle(candidatePose.rotation, pose.rotation) > 3)
            { candidateId = found.trackableId; candidatePose = pose; stableSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - stableSince < .75f) return;
            var anchorObject = new GameObject("Registered monitor");
            anchorObject.transform.SetParent(origin.TrackablesParent, false);
            anchorObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            anchor = anchorObject.AddComponent<ARAnchor>();
            anchorCreatedAt=Time.unscaledTime;
            stableSince = -1;
        }

        public bool Pressing { get; private set; } // True between a PressDown and the matching PressUp.

        PoseSample Snapshot(PoseSample reuse=null)
        {
            var p = reuse ?? new PoseSample();
            p.session=session; p.revision=revision; p.generation=connectionGeneration; p.sequence=++sequence; p.time=NetworkClock.Now; p.tracking=Tracking;
            if(TrackingMode==PhoneTrackingMode.AirMouse)
            { AirMouseMath.Pose(airReference,GyroAttitude(),airScreenSize,p); p.tracking=Tracking && airCentered; }
            else if (anchor != null)
            { p.position = anchor.transform.InverseTransformPoint(arCamera.transform.position); p.rotation = Quaternion.Inverse(anchor.transform.rotation) * arCamera.transform.rotation; }
            return p;
        }
        public void Center()
        {
            if(!(Connected && registered && Tracking) || ModeSwitchPending) return;
            PressUp();
            if(TrackingMode==PhoneTrackingMode.AirMouse)
            { airReference=GyroAttitude(); airCentered=true; Calibrated=false; }
            FlushSensitivity(); Send(new ControlMessage { type="calibrate", eventId=++eventId, pose=Snapshot() });
        }
        public void Select()
        { if (Connected && registered && Tracking && Calibrated) { FlushSensitivity(); Send(new ControlMessage { type = "select", eventId = ++eventId, pose = Snapshot() }); } }

        // Finger-down edge: send a "press" so the PC begins a drawing stroke, buzz for tactile feedback,
        // and also fire one discrete Select() so aim mode keeps working from the same button.
        public void PressDown()
        {
            if (Pressing || !(Connected && registered && Tracking && Calibrated)) return;
            FlushSensitivity();
            Pressing = true;
            Send(new ControlMessage { type = "press", eventId = ++eventId, pose = Snapshot() });
            Select();
            Vibrate();
        }
        // Finger-up edge: send a "release" so the PC ends the stroke.
        public void PressUp()
        {
            if (!Pressing) return;
            Pressing = false;
            if (Connected) Send(new ControlMessage { type = "release", eventId = ++eventId });
        }

        // Short haptic tick on press. Uses the Android Vibrator (with VibrationEffect on API 26+),
        // falling back to Handheld.Vibrate elsewhere. Safe to call in the editor (no-op).
        void Vibrate()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    if (vibrator == null || !vibrator.Call<bool>("hasVibrator")) return;
                    int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
                    if (sdk >= 26)
                    {
                        using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                        using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", 20L, effectClass.GetStatic<int>("DEFAULT_AMPLITUDE")))
                            vibrator.Call("vibrate", effect);
                    }
                    else vibrator.Call("vibrate", 20L);
                }
            }
            catch { Handheld.Vibrate(); }
#endif
        }
        public void RequestRegistration()
        { if (!Connected || ModeSwitchPending) return; PressUp(); ClearRegistration(); Send(new ControlMessage { type = "reregister" }); Message = "Waiting for screen registration..."; }
        void Send(ControlMessage m)
        { m.session = session; m.revision = revision; m.generation=connectionGeneration; m.sentAt=NetworkClock.Now; channel?.Send(m); }
        void ClearRegistration()
        {
            airCentered=false;
            generation++; registered = Calibrated = libraryReady = wasTracking = Pressing = false; stableSince = -1;
            if (images != null) images.enabled = false;
            if (anchor != null) Destroy(anchor.gameObject); anchor = null;
        }
        public void Disconnect()
        {
            Pressing = false; authenticated=false; recovering=false; ConnectionStatus=ConnectionStatus.Disconnected;
            ClearRegistration(); ModeSwitchPending=false; TrackingMode=PhoneTrackingMode.AR;
            if(rigReady && !destroyed) ApplyTrackingMode();
            session = null; channel?.Dispose(); channel = null; udp?.Close(); udp = null;
            connectingSocket?.Close(); connectingSocket = null; connectTask = null;
        }
        void OnApplicationPause(bool value)
        {
            paused = value;
            if(value) SaveSensitivity();
            if(value)
            {
                var oldChannel=channel;
                if(Connected) Send(new ControlMessage { type="paused" });
                channel=null; oldChannel?.CloseAfterSending();
                ClearRegistration(); BeginRecovery(DisconnectReason.AppPaused,NetworkClock.Now);
                connectingSocket?.Close(); connectingSocket=null; connectTask=null;
            }
            else if(pairing!=null && resumeToken!=null) { nextRetry=NetworkClock.Now; Message="Returning to PC. Register screen and CENTER again."; }
        }
        void OnDestroy() {
#if ENABLE_LEGACY_INPUT_MANAGER
            if(SystemInfo.supportsGyroscope) Input.gyro.enabled=false;
#endif
            SaveSensitivity(); destroyed = true; Disconnect(); log?.Dispose(); Screen.sleepTimeout = SleepTimeout.SystemSetting; }
    }
}
