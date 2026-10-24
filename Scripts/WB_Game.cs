// «Война шариков» — entry point. Press Play in any scene: the game builds itself from code.
using UnityEngine;

namespace WarOfBalls
{
    public static class Boot
    {
        // keeps Play working when "Enter Play Mode Options" skip the domain reload
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { WarGame.Inst = null; Mats.Reset(); Terra.WNODES = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Go()
        {
            if (WarGame.Inst != null) return;
            GameObject g = new GameObject("WarOfBalls");
            g.AddComponent<WarGame>();
        }
    }

    public partial class WarGame
    {
        void Awake()
        {
            Inst = this;
            Application.targetFrameRate = 60;
            save = SaveData.Load();
            Snd.Init(gameObject, !save.soundOff);
            Tex.Init();
            InitWorld(); InitRender(); InitShop();
            flock = new Vector4[14];
            for (int i = 0; i < flock.Length; i++) flock[i] = new Vector4(i * 0.45f, Rnd(-30, 30), Rnd(-15, 15), Rnd(0, 6));
            // a battle is set up behind the menu so the islands are visible; the menu then takes over
            SetupBattle(); BuildMiniTex();
            annT = 99; phase = Phase.Menu; zoneVisible = false; screen = Screen_.Menu; hintT = 0; hintA = 0;
        }

        void Update()
        {
            float realRaw = Time.unscaledDeltaTime, raw = Mathf.Clamp(realRaw, 0f, 0.05f);
            if (!paused) Tick(raw * speed);
            float t = Time.time;
            FitCamera(raw);
            RenderDecor(t, raw);
            RenderUnits(t);
            RenderRigs(t, raw);
            RenderFlags(t);
            RenderParts();
            if (hintT > 0) { hintT -= raw; if (hintT <= 0) hintT = 0; } else hintA = Mathf.Max(0, hintA - raw / 0.4f);
            annT += raw;
            barT -= raw; if (barT <= 0) { barT = 0.2f; UpdateBoardBtn(); }
        }

        void OnApplicationFocus(bool f) { if (!f) keys.Clear(); }
    }
}
