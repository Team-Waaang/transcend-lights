using System.Collections;
using System.IO;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// Records the main screen as one seamless loop for the README (inert unless the player is started with
    /// <c>-titleLoop &lt;folder&gt;</c>). Time steps at a fixed rate, and everything that repeats is put on the title's
    /// 8 s beat: the pose clips are 8 s, the ribbons lap in 8 s, the magic circles turn one 45° symmetry step in 8 s,
    /// the camera drifts on 8 s, glows pulse every 2 s. The shooting star (random) is off, the clouds hold still and
    /// the UI is held still.
    /// It writes the window as JPGs for <see cref="loop"/> + <see cref="overlap"/> seconds after a warm-up, then quits;
    /// only the random particles need the short crossfade the encoder adds over the overlap.
    /// </summary>
    public class TitleLoopRecorder : MonoBehaviour
    {
        public float loop = 8f;
        public float overlap = 1f;
        public float warmUp = 5f;
        public int fps = 24;

        string folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Arm()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-titleLoop");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("Title Loop Recorder");
            DontDestroyOnLoad(go);
            go.AddComponent<TitleLoopRecorder>().folder = args[i + 1];
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory(folder);
            Time.captureFramerate = fps;
            yield return null;

            foreach (var r in FindObjectsByType<LightRibbon>()) r.lap = loop;
            foreach (var g in FindObjectsByType<GlowSprite>()) g.period = 2f;
            // The clouds drift by a few metres every loop and would jump back: hold them still.
            foreach (var d in FindObjectsByType<Drift>()) d.enabled = false;
            foreach (var s in FindObjectsByType<ShootingStar>())
            {
                s.enabled = false;
                var trail = s.GetComponent<TrailRenderer>();
                if (trail != null) { trail.emitting = false; trail.Clear(); }
            }
            var screen = FindAnyObjectByType<TitleScreen>();
            if (screen != null)
            {
                screen.enabled = false;
                if (screen.logo != null)
                {
                    screen.logo.Finish();
                    screen.logo.sweepEvery = 1e6f;
                    screen.logo.floatAmount = 0f;
                }
                if (screen.black != null) screen.black.enabled = false;
                if (screen.flash != null) screen.flash.enabled = false;
                if (screen.ornament != null) screen.ornament.alpha = 1f;
                if (screen.prompt != null) screen.prompt.alpha = 0.85f;
                if (screen.credit != null) screen.credit.alpha = 0.5f;
            }
            var rig = FindAnyObjectByType<TitleCamera>();
            var cam = rig != null ? rig.GetComponent<Camera>() : Camera.main;
            Vector3 home = cam.transform.position, look = rig != null ? rig.lookAt : home + cam.transform.forward;
            Vector3 drift = rig != null ? rig.drift : Vector3.zero;
            float baseFov = cam.fieldOfView, roll = rig != null ? rig.roll : 0f;
            if (rig != null) rig.enabled = false;

            int warm = Mathf.RoundToInt(warmUp * fps), total = Mathf.RoundToInt((loop + overlap) * fps);
            for (int f = 0; f < warm + total; f++)
            {
                // The same drift as the title camera, but every term on the loop's period.
                float t = Time.time, w = t / loop * Mathf.PI * 2f;
                var offset = new Vector3(drift.x * Mathf.Sin(w), drift.y * Mathf.Sin(2f * w + 1.9f), drift.z * Mathf.Sin(w + 3.8f));
                var pos = home + offset;
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look + offset * 0.33f - pos) * Quaternion.Euler(0f, 0f, roll * Mathf.Sin(w + 0.9f)));
                cam.fieldOfView = baseFov + 0.4f * Mathf.Sin(w + 5f);
                yield return new WaitForEndOfFrame();
                if (f >= warm)
                {
                    // Saved as captured (window pixels, sRGB): scaling it through a RenderTexture in this linear
                    // project encoded the colours twice and washed the frames out. The encoder scales them down.
                    var shot = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(folder, $"f{f - warm:0000}.jpg"), shot.EncodeToJPG(95));
                    Destroy(shot);
                }
            }
            File.WriteAllText(Path.Combine(folder, "done.txt"), $"{total} frames at {fps} fps, loop {loop}s + overlap {overlap}s");
            Application.Quit();
        }
    }
}
