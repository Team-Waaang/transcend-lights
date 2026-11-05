// Dev-only helpers for ClaudeBridge: drive and inspect Play mode from outside the editor (see CLAUDE.md).
using System.IO;
using System.Linq;
using System.Reflection;
using AiHackathon.Cutscene;
using AiHackathon.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

static class PlayProbe
{
    public static string Play(string scenePath)
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty) return "dirty scene open: " + EditorSceneManager.GetSceneAt(i).path;
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
        return "entering";
    }

    public static string Stop()
    {
        EditorApplication.ExitPlaymode();
        return "exiting";
    }

    public static string Status()
    {
        if (!Application.isPlaying) return "not playing";
        var p = Object.FindAnyObjectByType<BossPeriod>();
        var s = $"t={Time.time:0.0} frame={Time.frameCount}";
        if (p != null) s += $" stage={p.Current} hp={(p.boss != null ? p.boss.Hp : -1):0.0} fighting={(p.boss != null && p.boss.Fighting)} shield={(p.boss != null && p.boss.ShieldUp)} orbs={(p.boss != null ? p.boss.Orbs.Count : -1)} weak={(p.boss != null && p.boss.Weak)}";
        foreach (var d in Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None))
            s += $"\n director {d.gameObject.scene.name} {d.time:0.00}/{d.duration:0.00} {d.state}";
        var q = ResonanceQte.Active;
        if (q != null) s += $"\n qte {q.Current}";
        s += $"\n camera.main={(Camera.main != null ? Camera.main.gameObject.scene.name + "/" + Camera.main.name : "none")}";
        for (int i = 0; i < SceneManager.sceneCount; i++) s += "\n scene " + SceneManager.GetSceneAt(i).name;
        return s;
    }

    public static string SetHp(string hp)
    {
        var p = Object.FindAnyObjectByType<BossPeriod>();
        if (p == null || p.boss == null) return "no boss";
        p.boss.Hp = float.Parse(hp, System.Globalization.CultureInfo.InvariantCulture);
        return "hp " + p.boss.Hp;
    }

    public static string Damage(string amount)
    {
        var p = Object.FindAnyObjectByType<BossPeriod>();
        if (p == null || p.boss == null) return "no boss";
        p.boss.TakeDamage(float.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Color.white);
        return "hp " + p.boss.Hp;
    }

    /// <summary>Jumps the playing cutscene director to a time.</summary>
    public static string Seek(string time)
    {
        var d = Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None).FirstOrDefault();
        if (d == null) return "no director";
        d.time = double.Parse(time, System.Globalization.CultureInfo.InvariantCulture);
        return "seek " + d.time;
    }

    /// <summary>Fills both QTE gauges as if both players had mashed on target.</summary>
    public static string FillQte()
    {
        var q = ResonanceQte.Active;
        if (q == null) return "no qte";
        var g = (float[])typeof(ResonanceQte).GetField("gauge", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(q);
        g[0] = g[1] = 1f;
        return "filled, state " + q.Current;
    }

    /// <summary>Points a player's rig at a world point (e.g. "0,0.8,0").</summary>
    public static string Aim(string slot, string point)
    {
        var p = Object.FindAnyObjectByType<BossPeriod>();
        var v = point.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var rig = p.game.players[int.Parse(slot)].rig;
        var vp = Camera.main.WorldToViewportPoint(new Vector3(v[0], v[1], v[2]));
        rig.SetPointer(new Vector2(vp.x, vp.y));
        return $"aim {vp}";
    }

    public static string Shot(string name)
    {
        Directory.CreateDirectory("Temp/CutscenePreview");
        var path = Path.GetFullPath($"Temp/CutscenePreview/play_{name}.png");
        ScreenCapture.CaptureScreenshot(path);
        return path;
    }

    public static string Rigs()
    {
        var p = Object.FindAnyObjectByType<BossPeriod>();
        var s = "";
        var m = UnityEngine.InputSystem.Mouse.current;
        s += "mouse: " + (m == null ? "none" : $"L={m.leftButton.isPressed} R={m.rightButton.isPressed} pos={m.position.ReadValue()}") + System.Environment.NewLine;
        foreach (var pl in p.game.players)
            s += $"{pl.profile.displayName}: active={pl.rig.isActiveAndEnabled} mouseMode={pl.rig.mouseMode} btn={pl.rig.mouseButton} firing={pl.rig.Firing} hit={pl.rig.HasHit} col=" + (pl.rig.HitCollider != null ? pl.rig.HitCollider.name : "-") + System.Environment.NewLine;
        return s;
    }

    /// <summary>Clicks the left mouse button <paramref name="count"/> times at the QTE target via synthetic input events.</summary>
    public static string MashQte(string count)
    {
        var q = ResonanceQte.Active;
        if (q == null || Camera.main == null) return "no qte";
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return "no mouse";
        var sp = Camera.main.WorldToScreenPoint(q.clash.position);
        int left = int.Parse(count) * 2, frame = 0;
        void Step()
        {
            if (!Application.isPlaying || left <= 0) { EditorApplication.update -= Step; return; }
            if (++frame % 3 != 0) return;
            bool down = left % 2 == 0;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = new Vector2(sp.x, sp.y), buttons = (ushort)(down ? 1 : 0) });
            left--;
        }
        EditorApplication.update += Step;
        return $"mashing at {sp}";
    }

    public static string Clips(string fbx)
    {
        var s = "";
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbx))
            if (a is AnimationClip c && !c.name.StartsWith("__preview")) s += $"{c.length:0.00}s loop={c.isLooping} {c.name}" + System.Environment.NewLine;
        return s;
    }

    public static string Delete(string paths)
    {
        var failed = new System.Collections.Generic.List<string>();
        AssetDatabase.DeleteAssets(paths.Split(','), failed);
        return failed.Count == 0 ? "deleted" : "failed: " + string.Join(", ", failed);
    }

    /// <summary>What Enter does on the connection screen.</summary>
    public static string StartDay()
    {
        var flow = Object.FindAnyObjectByType<Waaang.ConnectionFlow>();
        if (flow == null) return "no connection flow";
        typeof(Waaang.ConnectionFlow).GetField("started", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(flow, true);
        Waaang.RegistrationBoard.StartedWith = 0;
        return "next: " + AiHackathon.Game.PeriodFlow.Next();
    }

    public static string NextPeriod() => "next: " + AiHackathon.Game.PeriodFlow.Next();

    /// <summary>Marks the rune lanes done as if both players had traced every rune.</summary>
    public static string ClearRunes()
    {
        var period = Object.FindAnyObjectByType<RunePeriod>();
        if (period == null) return "no rune period";
        foreach (var lane in period.lanes) { lane.done = true; lane.index = lane.sequence.Length; }
        typeof(RunePeriod).GetMethod("CheckCleared", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(period, null);
        return "cleared " + period.Cleared;
    }

    /// <summary>Lights every creature until it petrifies (as if both players hunted them all down).</summary>
    public static string ClearHunt()
    {
        var hunt = Object.FindAnyObjectByType<LightHuntPeriod>();
        if (hunt == null) return "no light hunt";
        float until = (float)EditorApplication.timeSinceStartup + 6f;
        void Step()
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup > until || hunt.Cleared) { EditorApplication.update -= Step; return; }
            foreach (var c in hunt.creatures)
                if (c != null && !c.Petrified) c.Illuminate(1.6f, 0, c.Center + Vector3.back * 2f, Vector3.forward);
        }
        EditorApplication.update += Step;
        return "lighting " + hunt.creatures.Length;
    }

    public static string Paused() => "paused=" + EditorApplication.isPaused + " playing=" + Application.isPlaying;
    public static string Unpause() { EditorApplication.isPaused = false; return "unpaused"; }

    /// <summary>Keeps Play mode ticking while the editor is in the background (this session only, not a project setting).</summary>
    public static string RunInBackground() { Application.runInBackground = true; return "runInBackground"; }

    public static string Scene() => SceneManager.GetActiveScene().path;

    public static string ResolvePackages()
    {
        UnityEditor.PackageManager.Client.Resolve();
        return "resolving";
    }

    public static string Errors()
    {
        return "see Editor.log";
    }
}
