using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Oddity.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UniVRM10;

namespace Oddity.RoomTest
{
    public static class RoomPlay
    {
        public static void Run() => RoomPlayRunner.Run();
    }

    public class RoomPlayRunner : MonoBehaviour
    {
        // Single key/clip table, shared by input, script and overlay.
        static readonly (string key, string clip)[] Keys = {
            ("1", "KaIdle13Dance01"), ("2", "KaIdle71CatPose"),
            ("3", "AGIA_Idle_energetic_02_right_hand_piece"), ("4", "KaIdle23Dance03"),
            ("5", "KaIdle24Dance04"), ("6", "KaIdle89CheekPointPose"),
            ("7", "KaIdle41CuteShyPose"), ("8", "KaIdle44GreetingBow"),
            ("9", "BlowKissSkin"), ("space", "CatwalkIdleSkin"),
            ("a", "CatwalkSkin"), ("shift", "KaRun01"), ("f", "UAL1Standard#Sword_Attack"), ("z", "ZeroTwoDance"), ("0", "KaIdle73IdolPose"), ("c", "KaSitPutHandBetweensLoop"), ("v", "KaSitWithBothLegsUpLoop"), ("y", "MxYawn"), ("k", "KaSleepTurnToTheSideLoop"), ("j", "HairSweep"), ("u", "HairToss"), ("b", "MxLayStomachElbows"), ("d", "CatwalkSkin"), ("w", "CatwalkSkin"), ("s", "CatwalkSkin"), ("left", "누르는 동안 시계 방향 회전"), ("right", "누르는 동안 반시계 회전"), ("n", "다음 모델, Shift+N 이전 모델"),("r", "현재 모델 다시 불러오기"), ("l", "주인공(카메라) 바라보기 켜고 끄기"), ("h", "식탁 의자에서 턱 괴기 켜고 끄기"), ("g", "다음 걷기"), ("escape", "재생 끝내기"), ("up", "줌 인"), ("down", "줌 아웃"), ("m", "고양이(소말리) 동작 바꾸기"), ("tab", "Tab 하린, 고양이, 소말리, 흰 고양이 번갈아 조종"), ("o", "O 고양이 소리 켜고 끄기"), ("i", "방 안 카메라 (다시 누르면 밖)"), ("p", "소파에서 핸드폰 보기"), ("t", "소파에서 TV 보기"), ("e", "전화 통화 (걸어 다니며)"), ("q", "창가에서 커피"), ("x", "X 커튼 치기 (Shift+X 열기)"), ("comma", "아침 기지개 (침대에 앉아)"), ("period", "홈트 (요가 매트에서)"), ("semicolon", "라면 끓이기 (주방에서)"), ("quote", "바닥에 앉아 먹기 (낮은 상)"), ("slash", "빨래 개기 (침대에 앉아)"), ("lbracket", "거울 보기 (침대에 앉아)"), ("rbracket", "고양이와 놀기 (] 쓰다듬기, Shift+] 낚싯대)")
        };
        // G cycles the walk; ODDITY_PLAYWALK picks the first one
        static readonly string[] Walks={"CatwalkSkin","KaWalk01","KaWalk02","KaWalk03","KaWalk04","KaWalk05","KaWalk06","KaWalk07","WalkFemale","WalkFeminine","WalkStandardSkin"};
        static void SetWalk(string clip){for(int i=0;i<Keys.Length;i++)if(Walk(Keys[i].key)&&Keys[i].key!="shift")Keys[i].clip=clip;}
        static bool oldEnabled;
        static EnterPlayModeOptions oldOptions;
        public static void Run()
        {
            oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.update += Boot;
            EditorApplication.EnterPlaymode();
        }
        static void Boot()
        {
            if (!EditorApplication.isPlaying) return;
            EditorApplication.update -= Boot;
            new GameObject("RoomPlay").AddComponent<RoomPlayRunner>();
            var view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            view.position = new Rect(80,80,1280,720); view.Focus();
        }
        public const int MotionCount=22;
        public const int ZeroTwoIndex=13, IdolIndex=14, SitIndex=15, CurlIndex=16, YawnIndex=17, NapIndex=18, SweepIndex=19, TossIndex=20, BedIndex=21; // index 9 is the idle, on space only
        ZeroTwoDance zeroTwoDance;
        HairSweepRig hairRig; // j: puts the right hand on the head during the hair sweep
        int gestureOn=-1; // the hair gesture playing now (SweepIndex or TossIndex), -1 none
        ZeroTwoDanceAudio zeroTwoAudio;
        float zeroTwoPhase;
        float zeroTwoAudioSeconds;
        bool zeroTwoInputReady;
        bool reviewing;
        Animator anim;
        GameObject model;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] plays;
        AnimationClip[] clips;
        Vector3[] anchors, travels;
        Camera cameraView;
        float zoom=6.79f;
        GameObject sword, shield;
        Transform blob;
        SkinnedMeshRenderer soleMesh;
        Mesh soleBake;
        int[] soles;
        readonly List<Vector3> soleVerts=new List<Vector3>();
        float lift, liftSpeed, floorError;
        Mesh boundsMesh;
        float[] weights, from;
        float startYaw=45; // the first-frame facing in the apartment (ODDITY_HARINAT can change it)
        float walkTwist, runTwist, bodyFix, speed, runSpeed, x, z, heading=180, yaw=180, turnStart=180, targetYaw=180, turnTime=1, blend=1;
        int current=9, direction, blockedDirection;
        float Limit=1.08f, LimX0=-1.08f, LimX1=1.08f, ZNear=-.6f, ZFar=15f; // the room view widens these to its floor
        readonly Transform[] feet=new Transform[2], toes=new Transform[2], shins=new Transform[2];
        readonly float[] restPitch=new float[2], plant=new float[2], restAnkle=new float[2];
        readonly Vector3[] shinFront=new Vector3[2];
        const float KneeWalk=35; // the walk clips fold the knee to kick the shoe up behind; the walk keeps it under this
        const float AnkleReach=5, SwingReach=0; // degrees the foot may point down past its rest angle to the shin, toes on the floor or in the air
        readonly Quaternion[] restToe=new Quaternion[2];
        readonly Vector3[] footSide=new Vector3[2];float rollRaw, rollKept; // the sole's sideways tilt, before and after LevelRoll, the most seen on a planted foot
        readonly Transform[] hands=new Transform[2];readonly Quaternion[] restHand=new Quaternion[2];
        const float BowDepth=90; // key 8: the greeting bow leans 60 degrees at most; the spine adds the rest so its deepest point is a 90 degree bow
        float bowPeak;
        const float WristReach=40; // degrees a hand may turn from its rest on the forearm; the cuff opens a gap past this
        string modelName, modelPath, message="";
        float messageUntil;
        bool busy, scripted, held;
        int turning;const float TurnRate=100; // degrees a second while an arrow is held, 10 degrees every 0.1 s
        readonly List<(string key,float seconds)> script=new List<(string,float)>();
        int step=-1;
        float stepTime;
        static string F(float n)=>n.ToString("0.000",CultureInfo.InvariantCulture);
        void Start()
        {
            try {
                catSfxOn=CatSfxInitial();Time.captureDeltaTime=0; Application.targetFrameRate=60; Application.runInBackground=true; // a scripted run kept stalling at frame 2 when the editor window was not in front
                RenderSettings.skybox=null;
                RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight=new Color(.42f,.41f,.44f); // a dimmer fill and a stronger key so cloth folds read (was .66 fill, .55 key)
                var light=new GameObject("KeyLight").AddComponent<Light>();
                light.type=LightType.Directional;light.color=Color.white;light.intensity=1.1f;
                light.transform.rotation=Quaternion.Euler(35,120,0);keyLight=light;
                var cam=new GameObject("Camera").AddComponent<Camera>();
                cam.fieldOfView=15;cam.nearClipPlane=.05f;cam.farClipPlane=80;
                cam.transform.SetPositionAndRotation(new Vector3(0,1.13f,-6.79f),Quaternion.Euler(3,0,0));
                var zeroTwoTrack=Resources.Load<AudioClip>("Sfx/ZeroTwoTrack");
                var zeroTwoSource=cam.gameObject.AddComponent<AudioSource>();
                zeroTwoAudio=new ZeroTwoDanceAudio(zeroTwoSource,zeroTwoTrack);
                zeroTwoSource.mute=Environment.GetEnvironmentVariable("ODDITY_DANCEMUSIC")!="1"; // the dance music is muted by default (the track still runs, so the dance keeps its timing); ODDITY_DANCEMUSIC=1 plays it
                Debug.Log("ZEROTWO_AUDIO loaded="+zeroTwoSource.clip.name+" seconds="+F(zeroTwoSource.clip.samples/(float)zeroTwoSource.clip.frequency)+" samples="+zeroTwoSource.clip.samples+" imported="+(zeroTwoTrack!=null));
                cameraView=cam;cam.aspect=16f/9;Smooth(cam);Letterbox();cam.gameObject.AddComponent<AudioListener>();room=Environment.GetEnvironmentVariable("ODDITY_ROOM")!="0";var houseEnv=Environment.GetEnvironmentVariable("ODDITY_HOUSE");apt=room && houseEnv!="room";one=apt && houseEnv!="apt";Apartment.Use(one?OneRoom.Layout:null);Debug.Log("ROOMPLAY_HOUSE "+(one?"oneroom":apt?"apt":"room"));if(room)Room(cam);else Background(cam);blob=RoomTestRunner.MakeBlob();blob.localScale=new Vector3(.75f,.4f,1);
                SetWalk(Environment.GetEnvironmentVariable("ODDITY_PLAYWALK")??"KaWalk01");
                var sitClip=Environment.GetEnvironmentVariable("ODDITY_PLAYSIT");if(!string.IsNullOrWhiteSpace(sitClip))Keys[SitIndex].clip=sitClip; // which clip C sits with
                var curlClip=Environment.GetEnvironmentVariable("ODDITY_PLAYCURL");if(!string.IsNullOrWhiteSpace(curlClip))Keys[CurlIndex].clip=curlClip; // and V
                var napClip=Environment.GetEnvironmentVariable("ODDITY_PLAYNAP");if(!string.IsNullOrWhiteSpace(napClip))Keys[NapIndex].clip=napClip; // and K
                else if(apt && Apartment.HasBed)Keys[NapIndex].clip="KaSleepCurlUpSidewaysLoop"; // the bed: curled on her side rests on the quilt with the head on the pillow, the side-turn clip hovers
                var envModel=Environment.GetEnvironmentVariable("ODDITY_MODEL");var startLine=apt?IndoorLine:DefaultLine; // indoors (the studio and the apartment) she starts in the indoor clothes (I2), the old room in the outing ones (O2)
                var startPath=envModel??NewestInTest(startLine)??NewestInTest(DefaultLine)??NewestInTest(null)??"Assets/Models/test/harin-6T-I1-30.vrm"; // the newest build of that line, else O2, else the newest vrm of any line
                Debug.Log("ROOMPLAY_STARTMODEL "+Path.GetFileNameWithoutExtension(startPath)+" why="+(envModel!=null?"ODDITY_MODEL":apt?"indoor house -> "+IndoorLine:"outdoor room -> "+DefaultLine));
                LoadModel(startPath);
                PlaceSofa();
                BuildPerches();
                SpawnPets();
                if(apt){startYaw=Apartment.StartYaw;heading=yaw=targetYaw=turnStart=startYaw;turnTime=1;Select(9);} // she starts facing the house's first camera
                if(Solo && pets.Count>0){ctl=pets[0];AiPause(ctl);}
                var at=Environment.GetEnvironmentVariable("ODDITY_HARINAT"); // "x,z,yaw"
                if(Solo && one && string.IsNullOrWhiteSpace(at))at="0.7,4.8,0"; // the hidden Harin waits by the entrance, out of the cat's way: where Harin starts and which way she faces (0 = north, 180 = south), for lighting tests
                if(!string.IsNullOrWhiteSpace(at)){var v=at.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();x=v[0];z=v[1];heading=yaw=targetYaw=turnStart=startYaw=v[2];Debug.Log("ROOMPLAY_HARINAT x="+F(x)+" z="+F(z)+" yaw="+F(yaw));}
                var text=Environment.GetEnvironmentVariable("ODDITY_PLAYKEYS");scripted=!string.IsNullOrWhiteSpace(text);
                if(scripted)foreach(var part in text.Split(',')){
                    var pair=part.Split(':');
                    if(pair.Length!=2 || !float.TryParse(pair[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds) || seconds<=0 || !(pair[0]=="shifts" || pair[0]=="lietv" || pair[0].StartsWith("cam") || pair[0]=="realtab" || pair[0]=="wait" || pair[0].StartsWith("click") || pair[0].StartsWith("view") || pair[0]=="wheelup" || pair[0]=="liftup" || pair[0].StartsWith("orbit") || pair[0].StartsWith("insideswing") || pair[0]=="l" || pair[0].StartsWith("catsleep") || pair[0]=="liftdown" || pair[0]=="swingleft" || pair[0]=="shiftx" || pair[0]=="shiftrbracket" || pair[0]=="shiftleft" || pair[0]=="shiftright" || pair[0]=="shiftup" || pair[0]=="shiftdown" ||pair[0]=="swingright" || pair[0]=="tiltup" || pair[0]=="tiltdown" || pair[0]=="freeze" || pair[0]=="unfreeze" || (pair[0].StartsWith("gi") && int.TryParse(pair[0].Substring(2),out _)) || (pair[0].StartsWith("shade") && int.TryParse(pair[0].Substring(5),out _)) || pair[0]=="wheeldown" || FaceKeys.ScriptDigit(pair[0])>=0 || Keys.Any(k=>k.key==pair[0])))throw new Exception("invalid ODDITY_PLAYKEYS entry");
                    script.Add((pair[0],seconds));
                }
                Debug.Log("ROOMPLAY_READY model="+modelName);
                if(Environment.GetEnvironmentVariable("ODDITY_ZEROTWO_REVIEW")=="1"){reviewing=true;StartCoroutine(ReviewZeroTwo());}
            }catch(Exception e){Debug.LogError("ROOMPLAY_FAIL "+e.Message);EditorApplication.ExitPlaymode();}
        }
        // the stage camera only clears its 16:9 rect; without this the bars keep every old notice drawn over each other
        static void Letterbox()
        {
            var bars=new GameObject("Letterbox").AddComponent<Camera>();
            bars.depth=-10;bars.cullingMask=0;bars.clearFlags=CameraClearFlags.SolidColor;bars.backgroundColor=Color.black;
        }
        // SMAA on the stage camera smooths the hair, skirt and limb edges; the project's MSAA setting is left alone for the game
        static void Smooth(Camera cam)
        {
            var data=cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()??cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.antialiasing=UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;data.antialiasingQuality=UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            // Neutral tonemapping: without it the sun plus the ambient pushed her skin past 1, red clipped first and the skin went orange (cheek 255,226,169).
            // A global volume of our own keeps only the tonemapping (the pipeline's default profile also has bloom and a vignette). ODDITY_TONEMAP=0 turns it off
            if(Environment.GetEnvironmentVariable("ODDITY_TONEMAP")=="0")return;
            data.renderPostProcessing=true;
            var profile=ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            var tone=profile.Add<UnityEngine.Rendering.Universal.Tonemapping>(true);tone.mode.value=UnityEngine.Rendering.Universal.TonemappingMode.Neutral;
            var bloom=profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);bloom.intensity.value=0;
            var vig=profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);vig.intensity.value=0;
            var vol=new GameObject("RoomVolume").AddComponent<UnityEngine.Rendering.Volume>();vol.isGlobal=true;vol.priority=100;vol.sharedProfile=profile;
            Debug.Log("ROOMPLAY_TONEMAP neutral");
        }
        void Background(Camera cam)
        {
            var name=Environment.GetEnvironmentVariable("ODDITY_BG")??"bg6_stone_dusk.png";
            var tex=RoomTestRunner.Load(name);
            var q=RoomTestRunner.MakeQuad("Backdrop",tex,"Unlit/Texture");
            q.SetParent(cam.transform,false);
            float w=80*Mathf.Tan(7.5f*Mathf.Deg2Rad)*16/9, h=w*tex.height/tex.width;
            q.localPosition=new Vector3(0,(tex.width*9f/32-tex.height/2f)/tex.height*h,40);
            q.localScale=new Vector3(w,h,1);
        }
        // Harin's MToon shade: the VRM's shade colours are about 98% of the lit colour and GI equalization is 0.9, so her face never darkens.
        // ODDITY_SHADE=percent (script keys shade100, shade95, ...) darkens it: shade = original x percent (warm), GI equalization 0.9 - 1.5 (1 - k) and
        // shading shift -0.25 + 0.625 (1 - k) (85% gives 0.675 and -0.156); 100 = as imported
        // ODDITY_SHADEGI=percent (script keys gi90, gi30, ...) sets GI equalization on its own instead of following the shade
        int giWant=int.TryParse(Environment.GetEnvironmentVariable("ODDITY_SHADEGI"),out var gv)?Mathf.Clamp(gv,0,100):-1,giHave=-2;
        int shadeWant=int.TryParse(Environment.GetEnvironmentVariable("ODDITY_SHADE"),out var sv)?Mathf.Clamp(sv,40,100):100,shadeHave=-1;GameObject shadeFor;
        readonly Dictionary<Material,(Color shade,float gi,float shift)> shadeOrig=new Dictionary<Material,(Color,float,float)>();
        void ShadeTick()
        {
            if(model==null || (shadeFor==model && shadeHave==shadeWant && giHave==giWant))return;
            if(shadeFor!=model){shadeOrig.Clear();shadeFor=model;}
            int n=0;
            foreach(var r in model.GetComponentsInChildren<Renderer>(true))foreach(var m in r.materials){
                if(m==null || !m.HasProperty("_ShadeColor"))continue;
                if(!shadeOrig.TryGetValue(m,out var o)){o=(m.GetColor("_ShadeColor"),m.GetFloat("_GiEqualization"),m.GetFloat("_ShadingShiftFactor"));shadeOrig[m]=o;}
                float k=shadeWant/100f;var c=o.shade;bool orig=shadeWant>=100;
                m.SetColor("_ShadeColor",orig?c:new Color(c.r*k,c.g*k*(1-.4f*(1-k)),c.b*k*(1-.6f*(1-k)),c.a));
                m.SetFloat("_GiEqualization",giWant>=0?giWant/100f:orig?o.gi:Mathf.Clamp01(.9f-1.5f*(1-k)));
                m.SetFloat("_ShadingShiftFactor",orig?o.shift:-.25f+.625f*(1-k));n++;}
            shadeHave=shadeWant;giHave=giWant;Debug.Log("ROOMPLAY_SHADE look="+shadeWant+" gi="+giWant+" materials="+n);
        }
        void LoadModel(string path)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null && File.Exists(path)){AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);} // a model dropped in while the editor was closed may not be imported yet
            if(prefab==null)throw new Exception("model asset missing: "+Path.GetFileName(path));
            var candidate=Instantiate(prefab);
            Oddity.Game.FaceSdfShading.Attach(candidate); // anime two-tone face shadow test (ODDITY_FACESDF=0 turns it off)
            foreach(var renderer in candidate.GetComponentsInChildren<SkinnedMeshRenderer>())renderer.updateWhenOffscreen=true; // the close shot culls the head on its stale bounds
            var a=candidate.GetComponent<Animator>();
            if(a==null || a.avatar==null || !a.avatar.isHuman){ShowStatue(candidate,path);return;} // a bare mesh (no rig yet) stands still so its shape can be seen
            var puppet=candidate.AddComponent<HarinPuppet>();puppet.SetFace("neutral");puppet.enabled=false;
            var oldCall=callRig;callRig=null;callOn=false;var oldCoffee=coffeeRig;coffeeRig=null;coffeeOn=false;var oldCurtain=curtainRig;curtainRig=null;curtainOn=false;var oldWake=wakeRig;wakeRig=null;wakeOn=false;wakePend=false;var oldWorkout=workoutRig;workoutRig=null;workoutOn=false;workoutWalk=false;WorkoutBlockOff();var oldRamen=ramenRig;ramenRig=null;ramenOn=false;ramenWalk=false;RamenBlockOff();var oldMeal=mealRig;mealRig=null;mealOn=false;mealWalk=false;MealBlockOff();var oldLaundry=laundryRig;laundryRig=null;laundryOn=false;laundryPend=false;var oldMirror=mirrorRig;mirrorRig=null;mirrorOn=false;mirrorPend=false;var oldCatPlay=catRig;CatPlayReset();catRig=null;var oldDance=zeroTwoDance;var oldHair=hairRig;var oldSofa=sofaRig;var oldModel=model;var oldGraph=graph;var oldMixer=mixer;var oldPlays=plays;
            var oldClips=clips;var oldAnchors=anchors;var oldTravels=travels;var oldWeights=weights;var oldFrom=from;
            float oldSpeed=speed, oldBlend=blend;
            double phase=plays!=null && clips[current]!=null ? plays[current].GetTime()/clips[current].length : 0;
            int keepCurrent=current;soleMesh=null;model=candidate;anim=a;RestFeet();Arm();anim.applyRootMotion=false;
            try{zeroTwoDance=new ZeroTwoDance(anim);try{hairRig=new HairSweepRig(anim);}catch(Exception e){hairRig=null;Debug.LogWarning("ROOMPLAY_HAIR rig failed: "+e.Message);}current=IdleIndex;BuildMotion();try{sofaRig=new SofaRig(anim,handFingersLocal,handPalmLocal,headFront);}catch(Exception e){sofaRig=null;Debug.LogWarning("ROOMPLAY_SOFA rig failed: "+e.Message);}try{callRig=new PhoneCallRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var cs)?cs+11:Environment.TickCount,headFrameRest);}catch(Exception e){callRig=null;Debug.LogWarning("ROOMPLAY_CALL rig failed: "+e.Message);}try{coffeeRig=new CoffeeRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var cfs)?cfs+17:Environment.TickCount,headFrameRest);}catch(Exception e){coffeeRig=null;Debug.LogWarning("ROOMPLAY_COFFEE rig failed: "+e.Message);}try{curtainRig=new CurtainRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var cus)?cus+23:Environment.TickCount);}catch(Exception e){curtainRig=null;Debug.LogWarning("ROOMPLAY_CURTAIN rig failed: "+e.Message);}try{wakeRig=new WakeRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var wks)?wks+29:Environment.TickCount,headFrameRest);}catch(Exception e){wakeRig=null;Debug.LogWarning("ROOMPLAY_WAKE rig failed: "+e.Message);}try{workoutRig=new WorkoutRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var wos)?wos+31:Environment.TickCount,headFrameRest);workoutRig.Mat.gameObject.SetActive(false);}catch(Exception e){workoutRig=null;Debug.LogWarning("ROOMPLAY_WORKOUT rig failed: "+e.Message);}try{ramenRig=new RamenRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var rms)?rms+37:Environment.TickCount,headFrameRest);ramenRig.Sink=OneRoom.RamenSink;ramenRig.Burner=OneRoom.RamenBurner;ramenRig.Knob=OneRoom.RamenKnob;ramenRig.TapControl=OneRoom.RamenTap;}catch(Exception e){ramenRig=null;Debug.LogWarning("ROOMPLAY_RAMEN rig failed: "+e.Message);}try{mealRig=new FloorMealRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var mls)?mls+41:Environment.TickCount,headFrameRest);mealRig.Props.gameObject.SetActive(false);}catch(Exception e){mealRig=null;Debug.LogWarning("ROOMPLAY_MEAL rig failed: "+e.Message);}try{laundryRig=new LaundryRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var lds)?lds+43:Environment.TickCount);}catch(Exception e){laundryRig=null;Debug.LogWarning("ROOMPLAY_LAUNDRY rig failed: "+e.Message);}try{mirrorRig=new MirrorRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var mrs)?mrs+47:Environment.TickCount,headFrameRest);}catch(Exception e){mirrorRig=null;Debug.LogWarning("ROOMPLAY_MIRROR rig failed: "+e.Message);}try{catRig=new CatPlayRig(anim,Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var cps)?cps+53:Environment.TickCount,headFrameRest);}catch(Exception e){catRig=null;Debug.LogWarning("ROOMPLAY_CATPLAY rig failed: "+e.Message);}RestoreAfterRigs(keepCurrent);sofaOn=SofaMode.None;sofaWant=SofaMode.None;if(clips[current]!=null)plays[current].SetTime(phase*clips[current].length);Evaluate(0);}
            catch{current=keepCurrent;callRig?.Dispose();callRig=oldCall;coffeeRig?.Dispose();coffeeRig=oldCoffee;curtainRig?.Dispose();curtainRig=oldCurtain;wakeRig?.Dispose();wakeRig=oldWake;workoutRig?.Dispose();workoutRig=oldWorkout;ramenRig?.Dispose();ramenRig=oldRamen;mealRig?.Dispose();mealRig=oldMeal;laundryRig?.Dispose();laundryRig=oldLaundry;mirrorRig?.Dispose();mirrorRig=oldMirror;catRig?.Dispose();catRig=oldCatPlay;zeroTwoDance?.Dispose();zeroTwoDance=oldDance;hairRig=oldHair;sofaRig?.Dispose();sofaRig=oldSofa;if(graph.IsValid())graph.Destroy();graph=oldGraph;mixer=oldMixer;plays=oldPlays;clips=oldClips;anchors=oldAnchors;travels=oldTravels;weights=oldWeights;from=oldFrom;speed=oldSpeed;blend=oldBlend;model=oldModel;anim=oldModel!=null?oldModel.GetComponent<Animator>():null;Destroy(candidate);throw;}
            modelName=Path.GetFileNameWithoutExtension(path);modelPath=path;LogSprings(candidate);
            if(statue!=null){Destroy(statue);statue=null;}
            oldCall?.Dispose();oldCoffee?.Dispose();oldCurtain?.Dispose();oldWake?.Dispose();oldWorkout?.Dispose();oldRamen?.Dispose();oldMeal?.Dispose();oldLaundry?.Dispose();oldMirror?.Dispose();oldCatPlay?.Dispose();oldDance?.Dispose();oldSofa?.Dispose();HideIndoorShoes(candidate);if(oldGraph.IsValid())oldGraph.Destroy();if(oldClips!=null && oldClips[ZeroTwoIndex]!=null)Destroy(oldClips[ZeroTwoIndex]);DestroyGestureClips(oldClips);if(oldModel!=null)Destroy(oldModel);
        }
        // a mesh without a Humanoid Avatar (a fresh Tripo glb): no motion, it stands on the floor at its own size (scaled to 1.6 m when it is far off),
        // left and right arrows turn it, up and down arrows zoom, N, Shift+N and R still step through the lines, Esc ends
        GameObject statue;const float StatueFlip=180; // a glTF mesh comes in facing away from the VRM front
        void ShowStatue(GameObject candidate,string path)
        {
            zeroTwoDance?.Dispose();zeroTwoDance=null;if(graph.IsValid())graph.Destroy();
            if(clips!=null && clips[ZeroTwoIndex]!=null)Destroy(clips[ZeroTwoIndex]);DestroyGestureClips(clips);clips=null;plays=null;
            if(model!=null)Destroy(model);model=null;anim=null;direction=0;
            if(statue!=null)Destroy(statue);
            var renderers=candidate.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0){Destroy(candidate);throw new Exception("no mesh in "+Path.GetFileName(path));}
            Material grey=null;
            foreach(var r in renderers){if(r is SkinnedMeshRenderer sk)sk.updateWhenOffscreen=true;
                var mats=r.sharedMaterials;bool bad=mats.Length==0;foreach(var m in mats)bad|=m==null || m.shader==null || !m.shader.isSupported;
                if(bad){grey??=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=new Color(.8f,.8f,.8f)};r.sharedMaterials=Enumerable.Repeat(grey,Mathf.Max(1,mats.Length)).ToArray();}}
            candidate.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);candidate.transform.localScale=Vector3.one;
            Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
            float scale=b.size.y>1.2f && b.size.y<2.1f?1:1.6f/Mathf.Max(b.size.y,1e-4f);
            var holder=new GameObject("Statue "+Path.GetFileNameWithoutExtension(path));
            candidate.transform.SetParent(holder.transform,false);candidate.transform.localScale=Vector3.one*scale;
            candidate.transform.localPosition=new Vector3(-b.center.x,-b.min.y,-b.center.z)*scale;
            statue=holder;statue.transform.SetPositionAndRotation(new Vector3(x,0,z),Quaternion.Euler(0,yaw+StatueFlip,0));
            modelName=Path.GetFileNameWithoutExtension(path);modelPath=path;
            Notice("맨 메시(뼈대 없음): ← → 돌리기, ↑ ↓ 줌, N 다음 모델");
            Debug.Log("ROOMPLAY_STATUE model="+modelName+" height="+F(b.size.y)+" scale="+F(scale)+" renderers="+renderers.Length+" grey="+(grey!=null));
        }
        void StatueUpdate(float dt)
        {
            var kb=Keyboard.current;if(kb==null)return;
            foreach(var key in new[]{"n","r","escape"})if(kb[KeyOf(key)].wasPressedThisFrame)Command(key);
            if(statue==null)return;
            yaw+=((kb.leftArrowKey.isPressed?1:0)-(kb.rightArrowKey.isPressed?1:0))*TurnRate*dt;heading=targetYaw=turnStart=yaw;
            statue.transform.rotation=Quaternion.Euler(0,yaw+StatueFlip,0);
            Frame(dt,(kb.downArrowKey.isPressed?1:0)-(kb.upArrowKey.isPressed?1:0));
        }
        // C (hands between the knees) and V (knees hugged, curled like a cat): she sits on the nearest seat (sofa, or a chair at the dining table), her hips pinned to its seat mark
        // while the sit clip plays; walking or space gets her up (in front of the sofa, behind a chair). Props come from hari/gpt/props via Assets/Models/props
        // (newest sofa-n, table-n, chair-n.glb); ODDITY_SOFA=0 leaves them out. No colliders, she can walk through them.
        Transform sofa;readonly List<(Transform mark,Vector3 front,float off)> seats=new List<(Transform,Vector3,float)>();Vector3 seatAt;
        const float SofaZ=5f, TableZ=1.8f, StandOff=.45f; // the dining set stands sideways in front so a seated Harin shows in profile, the sofa faces the camera behind it
        static GameObject LoadProp(string stem)
        {
            var dir=Path.Combine(Application.dataPath,"Models/props");if(!Directory.Exists(dir))return null;
            var file=Directory.GetFiles(dir,stem+"-*.glb").OrderByDescending(p=>int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(),out var n)?n:-1).FirstOrDefault();
            if(file==null)return null;
            var asset="Assets/Models/props/"+Path.GetFileName(file);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            if(prefab==null){AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);prefab=AssetDatabase.LoadAssetAtPath<GameObject>(asset);}
            if(prefab==null){Debug.LogWarning("ROOMPLAY prop missing "+asset);return null;}
            var go=Instantiate(prefab);go.name=Path.GetFileNameWithoutExtension(file);return go;
        }
        // the sofa's two fabric colours (body and the darker trim) become one leather: the lighter blue grey, a smooth URP Lit surface with a soft sheen
        static readonly Color LeatherColor=new Color(.30f,.40f,.47f).gamma; // the glb factor is linear
        const float LeatherSmooth=.55f;
        static void Leather(Transform sofa)
        {
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="SofaLeather"};
            mat.SetColor("_BaseColor",LeatherColor);mat.SetFloat("_Smoothness",LeatherSmooth);mat.SetFloat("_Metallic",0);
            foreach(var r in sofa.GetComponentsInChildren<Renderer>()){
                var ms=r.sharedMaterials;bool hit=false;
                for(int i=0;i<ms.Length;i++)if(ms[i]!=null && ms[i].name.StartsWith("SofaFabric")){ms[i]=mat;hit=true;}
                if(hit)r.sharedMaterials=ms;}
        }
        static Transform Mark(Transform root,string name)=>root.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==name);
        static Vector3 Facing(Transform root){var f=Mark(root,"Front");var d=f!=null?Vector3.ProjectOnPlane(f.position-root.position,Vector3.up):Vector3.forward;return d.sqrMagnitude>1e-6f?d.normalized:Vector3.forward;}
        // turns a prop about its origin so its Front mark points along dir
        static void Face(Transform root,Vector3 dir){root.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(Facing(root),dir,Vector3.up),Vector3.up)*root.rotation;} // a turn about the vertical (FromToRotation flips a prop upside down when the two directions are opposite)
        // the studio's bed is a second seat of the sofa object: an invisible box collider (the mattress top, for the rays that find the surface) and a seat mark, so K, C and the cats treat it like a cushion
        void AddBedSeat()
        {
            var size=Apartment.BedSize;
            var c=GameObject.CreatePrimitive(PrimitiveType.Cube);c.name="BedProxy";DestroyImmediate(c.GetComponent<Collider>());DestroyImmediate(c.GetComponent<MeshRenderer>());
            c.transform.SetParent(sofa,false);c.transform.rotation=Quaternion.identity;
            c.transform.position=new Vector3(size.x/2,size.y/2,Apartment.BedSeat.z);c.transform.localScale=size/sofa.localScale.x;
            var m=new GameObject("SeatBed").transform;m.SetParent(sofa,false);m.position=Apartment.BedSeat;
            var bf=Apartment.BedFront;var bfe=Environment.GetEnvironmentVariable("ODDITY_BEDFRONT"); // "x,z": the direction she faces on the bed (tuning)
            if(!string.IsNullOrWhiteSpace(bfe)){var q=bfe.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();bf=new Vector3(q[0],0,q[1]).normalized;}
            seats.Add((m,bf,StandOff+.2f));
            var e=new GameObject("SeatBedEdge").transform;e.SetParent(sofa,false);e.position=Apartment.BedEdgeSeat; // C, P, T: she sits on the bed's east edge, feet on the floor, facing the TV
            seats.Add((e,Apartment.BedFront,StandOff+.2f));
        }
        void PlaceSofa()
        {
            if(Environment.GetEnvironmentVariable("ODDITY_SOFA")=="0")return;
            seats.Clear();
            var s=apt && Apartment.NoSofa?null:LoadProp("sofa");
            if(s==null && apt && Apartment.NoSofa && Apartment.HasBed){sofa=new GameObject("BedHost").transform;sofa.position=Vector3.zero;} // the studio has no sofa: a bare host carries the bed seats
            if(s!=null){sofa=s.transform;sofa.position=apt?Apartment.SofaAt:room?new Vector3(-.75f,0,1.5f):new Vector3(0,0,SofaZ);Face(sofa,apt?Apartment.SofaFace:Vector3.back); // the seat faces the camera
                if(apt && Apartment.SofaScale!=1)sofa.localScale=Vector3.one*Apartment.SofaScale; // the studio's loveseat is the prop scaled down
                Leather(sofa);
                foreach(var t in sofa.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Seat")).OrderBy(t=>t.position.x))seats.Add((t,Facing(sofa),StandOff+.2f));} // the sofa seat is deep, she gets up clear of it
            var table=apt && !Apartment.HasTable?null:LoadProp("table"); // the studio has no dining set
            tableRoot=table!=null?table.transform:null;
            if(table!=null){table.transform.position=apt?Apartment.TableAt:room?new Vector3(.70f,0,-.45f):new Vector3(0,0,TableZ);Face(table.transform,apt?Apartment.TableFace:Vector3.right);
                foreach(var spot in table.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Chair")).OrderBy(t=>t.name).ToList()){
                    var chair=LoadProp("chair");if(chair==null)break;
                    chair.transform.SetPositionAndRotation(spot.position,spot.rotation);chair.name=spot.name;
                    var seat=Mark(chair.transform,"Seat");if(seat!=null)seats.Add((seat,Facing(chair.transform),-StandOff)); // a chair is left from behind, the table is in front
                }}
            if(tableRoot!=null)Laptop(tableRoot,apt?-90:0);
            curtain?.Dispose();curtain=null;if(one)curtain=new CurtainPanel();
            tv?.Dispose();tv=null;if(apt && Apartment.HasTv)tv=new SofaTv(null,Apartment.TvAt,Apartment.TvFace,Apartment.TvSize);
            if(apt && Apartment.HasBed && sofa!=null)AddBedSeat();
            if(sofa==null && seats.Count>0)sofa=seats[0].mark.root;
            propHits.Clear();foreach(var root in seats.Select(t=>t.mark.root).Distinct())foreach(var mf in root.GetComponentsInChildren<MeshFilter>())if(mf.sharedMesh!=null){var c=mf.gameObject.AddComponent<MeshCollider>();c.sharedMesh=mf.sharedMesh;propHits.Add(c);} // only for PropTop's rays
            clickHits.Clear();clickHits.AddRange(propHits);
            if(tableRoot!=null)foreach(var mf in tableRoot.GetComponentsInChildren<MeshFilter>())if(mf.sharedMesh!=null){var c=mf.gameObject.AddComponent<MeshCollider>();c.sharedMesh=mf.sharedMesh;clickHits.Add(c);}
            clickHits.AddRange(perchCols);
            if(aptView!=null){if(tv!=null)aptView.Register(tv.Root);if(sofa!=null)aptView.Register(sofa);if(tableRoot!=null)aptView.Register(tableRoot);foreach(var rt in seats.Select(t=>t.mark.root).Distinct())aptView.Register(rt);}
            Blockers();
            BuildSofaSpots();
            Debug.Log("ROOMPLAY_PROPS seats="+string.Join(" ",seats.Select(t=>t.mark.root.name+"/"+t.mark.name+"("+F(t.mark.position.x)+","+F(t.mark.position.y)+","+F(t.mark.position.z)+" face "+F(t.front.x)+","+F(t.front.z)+")")));
        }

        // the pets: the Sketchfab "Somali Cat" (SOMALI) and the realistic white cat (WHITE), both git-ignored. Each is on the floor facing the camera
        // at an angle, 0.30 m tall (the Somali 0.22 m). M cycles the controlled pet's clips in place. ODDITY_CAT=0 leaves out both, ODDITY_SOMALI=0 only the Somali; missing files only log a warning.
        // The Somali glb goes through UniGLTF like the props; its clips come out Legacy, so RoomPlay copies them as Mecanim clips. ODDITY_CAT=0 leaves out both cats, ODDITY_SOMALI=0 only the Somali.
        sealed class Pet
        {
            public string tag,kind="somali";public bool somali,white;public int oneClip=13;public float walkSpd=1f,runSpd=2f,aiSpd=.70f,walkClipSpd=1,runMix,curPace;public bool wasRun;public bool lookOn,lookCal;public float lookW;public Transform headB;public Vector3 headFront; // somali: a pet with rest poses; white: also Groom, Stretch, Jump (both cats once their shared glb is loaded: kind says which one it is; oneClip: the standing one-shot clip that sit 11 plays); walkSpd/runSpd/aiSpd: m/s
            public string[] clipNames;public AnimationClip[] clips;
            public Transform root;public PlayableGraph graph;public AnimationPlayableOutput outp;
            public int index;public bool moving;public float heading=-1000;
            public bool onSofa;public Transform seat;public float baseY;
            public bool sofaTurn;public Vector3 sofaFront;public int pendingRest;public Vector2? goal;public string goalKind;public Transform goalSeat;public Vector2 goalLook;public float faceTo=float.NaN,stuck;
            public int job;public float jobT,jobDur,jobArc,jobHead;public Vector3 jobFrom,jobTo;public Transform jobSeat;
            public bool groomAfter; // white: the SitDown that is playing leads into Groom
            public int sit; // Somali and white only (10 grooming, 11 stretching): 0 standing, 1 SitDown playing, 2 sitting (SittingIdle), 3 StandUp playing, 4 LieDown, 5 lying (LyingIdle, sphinx), 6 GetUp, 7 LoafDown, 8 loafing (LoafIdle), 9 LoafUp
            public bool hasLie;public int restNext,arriveRest;public float settledAt; // restNext: the rest pose (1 sit, 2 lie, 3 loaf) to start when the Up clip ends; arriveRest: the pose to take on landing on the sofa or a perch (0 = sit); settledAt: when it last stopped moving, for the hiss
            public int mStep;
            public AudioSource shot,purr;
            public int loopCount;public float walkStart,idleTime,nextSoft,purrTarget;public bool purrOn;
            public int towerGoal;public Perch perch,jobPerch;public string chainNext;public List<string> hopPlan=new List<string>();public float hopWait;public bool auto; // perch: a high place it sits on now (null: floor or sofa); chainNext: counter first, then the fridge; hopPlan: the hops still to fly, planned at the start (fridge>counter>floor, floor>counter>fridge), no rest pose between them
            public List<Vector2> path;public int pathI;public Vector2? pathGoal;public Vector2 pathHarin;public bool replanned; // the planned walk to goal (FloorPath): waypoints, next one, the goal it was made for, where Harin was, whether the stuck replan is used
            public float surf,surfBase;public Vector3 fitFront,fitSide;public float[] ext=new float[4];public readonly List<Vector3> lowPts=new List<Vector3>();public float lastFit; // surfBase: the cushion height where it landed; fitFront/fitSide: the horizontal axes its footprint is measured on; ext: min,max of the footprint along each from the root; lowPts: the non-tail vertices within 3 cm of the lowest (the paws)
            public SkinnedMeshRenderer[] smrs;public bool[][] tailMask;public Mesh bake;public string gapKey=""; // surf: height of the surface it rests on (floor 0); the rest of these measure its lowest vertex
            public Spot spot,after,transit,fitSpot,jobSpot;public float radius=.2f,relocAt;public Vector2 cOff; // spot: the perch / cushion spot it holds (going there, or standing on it); after: the final spot when it passes through another (fridge via counter); transit: a spot it only hops through; fitSpot: the cushion it landed on; radius, cOff: the footprint circle (centre = root + rotated cOff)
            public bool[][] pawMask;public bool pawFallback;public int pawSeeds;public readonly List<Vector3> conPts=new List<Vector3>();public float pawLogAt; // pawMask: vertices mainly weighted to a paw / foot / toe bone; conPts: the lowest vertex of each paw and the lowest body vertices, each rests on its own surface
            public bool hasSleep,aiSleeps;public int sleepPose=-1,sleepCur;public (float lng,float sht)[] sleepFp; // sleep: the 21 Sleep clips are loaded (indices 14 + pose*3 + Down/Idle/Up); sleepPose: the pose the next sleep takes (K cycles it), sleepCur: the pose playing now; sleepFp: Idle footprints from the json (model units); aiSleeps: the AI chose a sleep for its current rest
            public bool aiPaused;public float resumeIn;public int aiPhase;public string aiAct="",aiLast="",aiPlace="";public float aiT,aiDur,aiGo;public Vector2 aiTarget;public bool aiHasTarget;
            public bool Idleish=>index==0 || (somali && (index==3 || index==6 || index==9 || (white && index==12) || (hasSleep && index>=SleepBase && (index-SleepBase)%3==1)));
            public int JumpClip=>white?11:somali?0:2; // the Somali has no jump clip: the arc is flown in the Idle pose
        }
        // a high place a cat can sit on: it walks to stand (a floor spot), hops to land (top view x, z; the height comes from a downward ray), and faces yaw
        sealed class Perch{public string name;public Vector2 stand,land;public float yaw,top=float.NaN;public Func<Collider,bool> pick;}
        // a named place a cat can take: sill, counter, fridge, table spots and sofa cushions. land = where the root ends (top view), stand = the floor spot it hops from / to,
        // sofa spots also have the seat mark, the sofa front, and lo..hi = the cushion's extent along axis (world dot product)
        sealed class Spot{public string key,kind;public Perch perch;public Vector2 land,stand;public float yaw,top=float.NaN;public Transform mark;public Vector3 fwd,axis;public float lo,hi;}
        readonly Dictionary<string,List<Spot>> spotsOf=new Dictionary<string,List<Spot>>();
        readonly Dictionary<Perch,List<Collider>> perchColsOf=new Dictionary<Perch,List<Collider>>();
        List<Collider> sofaCols;
        readonly Dictionary<string,Perch> perches=new Dictionary<string,Perch>();
        readonly List<Collider> perchCols=new List<Collider>();readonly Dictionary<Collider,string> perchOf=new Dictionary<Collider,string>();
        static readonly string[] SomaliClips={"Idle","WalkClean","SitDown","SittingIdle","StandUp","LieDown","LyingIdle","GetUp","LoafDown","LoafIdle","LoafUp"}; // the last six only come with somali_cat_lie.glb (hasLie)
        static bool SomaliLoops(int i)=>i==0||i==1||i==3||i==6||i==9;
        // the white cat: the Somali's eleven clips in the same order, then Jump (11), Groom (12, sitting, loops), Stretch (13)
        static readonly string[] WhiteClips={"Idle","Walk","SitDown","SittingIdle","StandUp","LieDown","LyingIdle","GetUp","LoafDown","LoafIdle","LoafUp","Jump","Groom","Stretch"};
        static bool WhiteLoops(int i)=>SomaliLoops(i)||i==12||(i>=SleepBase && (i-SleepBase)%3==1);
        // the seven sleeping poses (CatSleep) follow the 14 shared clips: index SleepBase + pose*3 + 0 Down, 1 Idle (loops), 2 Up. Cats whose file lacks them keep the first 14 and hasSleep false
        const int SleepBase=14;
        static int SleepClip(int pose,int phase)=>SleepBase+pose*3+phase;
        static string[] SleepAwareNames(Func<string,bool> has)=>CatSleep.AllClipNames().All(has)?WhiteClips.Concat(CatSleep.AllClipNames()).ToArray():WhiteClips;
        static bool IsUp(int s)=>s==3||s==6||s==9||s==14; // 14: the Up clip of a sleep pose. sit 12: sleep Down playing, 13: asleep (the pose's Idle)
        const string SomaliSharedFile="Assets/Oddity/DevMotion/Cat/Somali/somali_cat_shared.glb";
        const string WhiteDir="Assets/Models/test/";
        const string SomaliFile="Assets/Oddity/DevMotion/Cat/Somali/somali_cat.glb",SomaliLieFile="Assets/Oddity/DevMotion/Cat/Somali/somali_cat_lie.glb";
        // sizes: by body length nose tip to rump, tail excluded (0.46 m, a typical adult house cat); env overrides in metres
        static readonly float WhiteBodyLength=PetEnv("ODDITY_WHITELENGTH")!=null?float.Parse(PetEnv("ODDITY_WHITELENGTH"),CultureInfo.InvariantCulture):.46f;
        static readonly float SomaliBodyLength=PetEnv("ODDITY_SOMALILENGTH")!=null?float.Parse(PetEnv("ODDITY_SOMALILENGTH"),CultureInfo.InvariantCulture):.46f;
        readonly List<Pet> pets=new List<Pet>();Pet ctl; // ctl: the controlled pet, null while Harin is
        static string PetEnv(string name)=>Environment.GetEnvironmentVariable(name);
        void SpawnPets()
        {
            if(!room || PetEnv("ODDITY_CAT")=="0" || (OrbitEnv && PetEnv("ODDITY_CAT")!="1"))return; // the orbit shot has no cats unless ODDITY_CAT=1
            if(Solo?PetEnv("ODDITY_SOMALI")=="1":PetEnv("ODDITY_SOMALI")!="0")SpawnSomali();
            if(PetEnv("ODDITY_WHITE")!="0")SpawnWhite();
            if(Solo?PetEnv("ODDITY_TABBY")=="1":PetEnv("ODDITY_TABBY")!="0")SpawnTabby();
            Debug.Log("ROOMPLAY_SOLO "+(Solo?1:0)+" pets="+string.Join("|",pets.Select(q=>q.tag)));
        }
        // For now (2026-11-11, while the white cat's sleep poses are reworked one by one) a hand-played run shows the WHITE cat alone: the Somali and the tabby
        // are not spawned (ODDITY_SOMALI=1 / ODDITY_TABBY=1 bring one back), Harin is invisible, parked by the entrance and does not live on her own, and the
        // keys start on the cat. ODDITY_SOLO=0 turns this off, =1 forces it; script runs (ODDITY_PLAYKEYS) are untouched unless ODDITY_SOLO=1.
        static bool Solo=>PetEnv("ODDITY_SOLO")=="1" || (PetEnv("ODDITY_SOLO")!="0" && string.IsNullOrWhiteSpace(PetEnv("ODDITY_PLAYKEYS")));
        // the third cat TABBY: the striped tabby "cat4" (CC BY 4.0, git-ignored), the same 14 shared clips; body length 0.44 m, ODDITY_TABBYLENGTH overrides, ODDITY_TABBY=0 leaves it out
        const string TabbySharedFile="Assets/Oddity/DevMotion/Cat/Cat4/cat4_shared.glb";
        static readonly float TabbyBodyLength=PetEnv("ODDITY_TABBYLENGTH")!=null?float.Parse(PetEnv("ODDITY_TABBYLENGTH"),CultureInfo.InvariantCulture):.44f;
        float TabbyYaw=PetEnv("ODDITY_TABBYYAW")!=null?float.Parse(PetEnv("ODDITY_TABBYYAW"),CultureInfo.InvariantCulture):180+60;
        void SpawnTabby()
        {
            if(!File.Exists(Path.Combine(Application.dataPath,"../"+TabbySharedFile))){Debug.LogWarning("ROOMPLAY_TABBY file missing "+TabbySharedFile+", no tabby cat");return;}
            if(apt && PetEnv("ODDITY_TABBYYAW")==null)TabbyYaw=Apartment.TabbyYaw;
            var a=PetEnv("ODDITY_TABBYSPOT")!=null?PetEnv("ODDITY_TABBYSPOT").Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray():apt?new[]{Apartment.TabbyStart.x,Apartment.TabbyStart.y}:new[]{1.35f,-1.15f};
            if(!SpawnShared("TABBY","tabby",TabbySharedFile,TabbyYaw,new Vector2(a[0],a[1]),true,TabbyBodyLength,new[]{"Pelvis"},new[]{"Head"}))Debug.LogWarning("ROOMPLAY_TABBY could not be spawned");
        }
        // what the pet measures in the pose now playing, from the baked vertices of its skinned meshes only (a stray static mesh in a glb, like a ground plane, counts for nothing)
        // and without the tail (vertices mainly weighted to a bone named "tail" or below one): length = nose tip to rump along the root's forward axis, minY/maxY = floor to head top
        struct PetSize{public float length,minY,maxY,cx,cz;public int used,skipped;public float Height=>maxY-minY;}
        PetSize PetMeasure(Pet p)
        {
            var fwd=p.root.forward;float lo=float.MaxValue,hi=float.MinValue,y0=float.MaxValue,y1=float.MinValue,x0=float.MaxValue,x1=float.MinValue,z0=float.MaxValue,z1=float.MinValue;int used=0,skipped=0;var verts=new List<Vector3>();
            foreach(var smr in p.root.GetComponentsInChildren<SkinnedMeshRenderer>()){
                var mesh=new Mesh();smr.BakeMesh(mesh,true);mesh.GetVertices(verts);
                var src=smr.sharedMesh;var bones=smr.bones;var perVertex=src.GetBonesPerVertex();var weights=src.GetAllBoneWeights();int w=0;
                bool IsTail(Transform tr){for(;tr!=null && tr!=p.root;tr=tr.parent)if(tr.name.IndexOf("tail",StringComparison.OrdinalIgnoreCase)>=0)return true;return false;}
                for(int v=0;v<verts.Count && v<perVertex.Length;v++){
                    int n=perVertex[v];float best=-1;int bone=-1;for(int k=0;k<n;k++){var bw=weights[w+k];if(bw.weight>best){best=bw.weight;bone=bw.boneIndex;}}w+=n;
                    if(bone>=0 && bone<bones.Length && IsTail(bones[bone])){skipped++;continue;}
                    var wp=smr.transform.position+smr.transform.rotation*verts[v];
                    float d=Vector3.Dot(wp,fwd);lo=Mathf.Min(lo,d);hi=Mathf.Max(hi,d);
                    y0=Mathf.Min(y0,wp.y);y1=Mathf.Max(y1,wp.y);x0=Mathf.Min(x0,wp.x);x1=Mathf.Max(x1,wp.x);z0=Mathf.Min(z0,wp.z);z1=Mathf.Max(z1,wp.z);used++;}
                Destroy(mesh);
            }
            if(used==0)throw new Exception("no skinned vertices to measure");
            return new PetSize{length=hi-lo,minY=y0,maxY=y1,cx=(x0+x1)/2,cz=(z0+z1)/2,used=used,skipped=skipped};
        }
        // scales the pet (by body length when byLength, else by head-top height) and stands it on the floor at spot (x, z), turned to yaw
        void PetPlace(Pet p,Vector2 spot,float yaw,bool byLength,float target)
        {
            var go=p.root.gameObject;
            go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            // measured once at the identity pose (the skinned mesh is only re-evaluated on the next frame, so a second measure would be stale); the rest is arithmetic
            var s0=PetMeasure(p);float scale=target/Mathf.Max(1e-4f,byLength?s0.length:s0.Height);go.transform.localScale=Vector3.one*scale;
            foreach(var f in furniture)if(f.Contains(spot))Debug.LogWarning("ROOMPLAY_"+p.tag+" spot is inside a furniture footprint");
            var rot=Quaternion.Euler(0,yaw,0);go.transform.rotation=rot;
            var centre=rot*new Vector3(s0.cx*scale,0,s0.cz*scale);
            go.transform.position=new Vector3(spot.x-centre.x,-s0.minY*scale,spot.y-centre.z);p.baseY=go.transform.position.y;
            Debug.Log("ROOMPLAY_"+p.tag+" pos=("+F(go.transform.position.x)+","+F(go.transform.position.y)+","+F(go.transform.position.z)+") headheight="+F(s0.Height*scale)+" bodylength="+F(s0.length*scale)+" native headheight="+F(s0.Height)+" native bodylength="+F(s0.length)+" scale="+F(scale)+" vertices="+s0.used+" tailskipped="+s0.skipped+" clips="+p.clips.Length);
            p.cOff=new Vector2(s0.cx*scale,s0.cz*scale);p.radius=Mathf.Clamp(s0.length*scale*.5f*.9f,.1f,.24f);
            Debug.Log("ROOMPLAY_"+p.tag+" footprint radius="+F(p.radius)+" centreOffset=("+F(p.cOff.x)+","+F(p.cOff.y)+")");
        }
        void PetGraph(Pet p,Animator animator)
        {
            animator.applyRootMotion=false;
            p.graph=PlayableGraph.Create(p.tag);p.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            p.outp=AnimationPlayableOutput.Create(p.graph,p.tag,animator);p.graph.Play();
            SetPetClip(p,0,false);p.graph.Evaluate(0);
        }
        [Serializable] sealed class SharedInfo{public float body_length_nose_to_tail_base,head_top_height_above_floor,walk_speed_units_per_s;}
        // a cat from a shared-clip glb (tools/blender/retarget_cats.py): the same 14 clips as the white cat, through UniGLTF, in a wrapper root. Returns false (nothing added) when the file is missing or broken,
        // so the caller falls back to its older files. The model is turned so pelvis -> head is the wrapper's +z; the walk speed is the json's in-place speed times the scale
        bool SpawnShared(string tag,string kind,string file,float yaw,Vector2 spot,bool byLength,float target,string[] pelvisNames,string[] headNames)
        {
            try{
                if(!File.Exists(Path.Combine(Application.dataPath,"../"+file)))return false;
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(file);
                if(prefab==null){AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);prefab=AssetDatabase.LoadAssetAtPath<GameObject>(file);}
                if(prefab==null){Debug.LogWarning("ROOMPLAY_"+tag+" import failed "+file+", using the older files");return false;}
                var all=AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>().ToList();
                AnimationClip Find(string n)=>all.FirstOrDefault(c=>c.name==n) ?? all.FirstOrDefault(c=>c.name.EndsWith("|"+n) && !c.name.StartsWith("__preview__"));
                var names=SleepAwareNames(n=>Find(n)!=null);
                var p=new Pet{tag=tag,kind=kind,somali=true,white=true,hasLie=true,hasSleep=names.Length>WhiteClips.Length,clipNames=names,clips=new AnimationClip[names.Length]};
                for(int i=0;i<names.Length;i++){
                    var src=Find(names[i]);
                    if(src==null){Debug.LogWarning("ROOMPLAY_"+tag+" clip missing "+names[i]+" in "+file+" (have "+string.Join(",",all.Select(c=>c.name))+"), using the older files");return false;}
                    p.clips[i]=PetClipCopy(src,WhiteLoops(i));}
                var wrap=new GameObject(char.ToUpperInvariant(kind[0])+kind.Substring(1));var go=Instantiate(prefab,wrap.transform);go.name=wrap.name+"Model";go.transform.localRotation=Quaternion.identity;
                p.root=wrap.transform;
                foreach(var a in go.GetComponentsInChildren<Animation>())Destroy(a);
                if(kind=="somali"){ // a ground/shadow plane came inside the glb: any static mesh (not skinned) that is flat and wider than it is tall, or named like one, goes
                    var removed=new List<string>();
                    foreach(var mr in go.GetComponentsInChildren<MeshRenderer>(true)){
                        var bb=mr.bounds;string nm=mr.name.ToLowerInvariant();
                        if(nm.Contains("plane")||nm.Contains("ground")||nm.Contains("floor")||nm.Contains("shadow")||bb.size.y<.25f*Mathf.Max(bb.size.x,bb.size.z)){removed.Add(mr.name+" "+F(bb.size.x)+"x"+F(bb.size.y)+"x"+F(bb.size.z));mr.gameObject.SetActive(false);Destroy(mr.gameObject);}}
                    Debug.Log("ROOMPLAY_"+tag+" removed="+(removed.Count==0?"none":string.Join(",",removed)));
                }
                foreach(var r in go.GetComponentsInChildren<Renderer>())if(r is SkinnedMeshRenderer smr)smr.updateWhenOffscreen=true;
                var host=go.GetComponent<Animator>();if(host==null)host=go.AddComponent<Animator>();PetGraph(p,host);
                var bonesAll=go.GetComponentsInChildren<Transform>();
                Transform Bone(string[] ns)=>ns.Select(n=>bonesAll.FirstOrDefault(t=>t.name==n)).FirstOrDefault(t=>t!=null);
                var head=Bone(headNames);var pelvis=Bone(pelvisNames);p.headB=head;
                if(head!=null && pelvis!=null){
                    var fw=Vector3.ProjectOnPlane(head.position-pelvis.position,Vector3.up);
                    if(fw.sqrMagnitude>1e-8f){float ang=Mathf.Atan2(fw.x,fw.z)*Mathf.Rad2Deg;go.transform.localRotation=Quaternion.Euler(0,-ang,0);Debug.Log("ROOMPLAY_"+tag+" nose measured at "+F(ang)+" deg from +z ("+pelvis.name+" -> "+head.name+"), model turned by "+F(-ang));}
                }else Debug.LogWarning("ROOMPLAY_"+tag+" Head or Pelvis bone missing, facing unmeasured");
                PetSfxSetup(p,wrap);
                PetPlace(p,spot,yaw,byLength,target);
                float scale=p.root.localScale.x;var info=new SharedInfo();
                var jf=Path.Combine(Application.dataPath,"../"+Path.ChangeExtension(file,".json"));
                if(File.Exists(jf))info=JsonUtility.FromJson<SharedInfo>(File.ReadAllText(jf));
                if(File.Exists(jf) && p.hasSleep)p.sleepFp=CatSleep.ParseFootprints(File.ReadAllText(jf));
                Debug.Log("ROOMPLAY_CATSLEEP who="+p.tag+" clips="+(p.hasSleep?CatSleep.Count*3:0)+" footprints="+(p.sleepFp!=null?string.Join("/",p.sleepFp.Select(f=>F(f.lng*scale)+"x"+F(f.sht*scale))):"none")+" (m, long x short)");
                if(info.walk_speed_units_per_s>0){p.walkSpd=info.walk_speed_units_per_s*scale*CatSpeedMul;p.runSpd=p.walkSpd*2f;p.aiSpd=p.walkSpd*.8f;}
                var mat=go.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m!=null);
                Debug.Log("ROOMPLAY_CATSPEED name="+tag+" walk="+F(p.walkSpd)+" run="+F(p.runSpd)+" ai="+F(p.aiSpd));Debug.Log("ROOMPLAY_"+tag+" shared file="+Path.GetFileName(file)+" scale="+F(scale)+" json body="+F(info.body_length_nose_to_tail_base)+" head="+F(info.head_top_height_above_floor)+" walk="+F(info.walk_speed_units_per_s)+" speed="+F(p.walkSpd)+" m/s run="+F(p.runSpd)+" ai="+F(p.aiSpd)+" clips="+string.Join(",",p.clips.Select(c=>c.name+":"+F(c.length)+(c.legacy?"L":"M")))+" renderers="+go.GetComponentsInChildren<Renderer>().Length+" material="+(mat!=null?mat.shader.name+" colour="+(mat.HasProperty("_BaseColor")?mat.GetColor("_BaseColor").ToString():"-")+" map="+(mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap")!=null):"none"));
                pets.Add(p);return true;
            }catch(Exception e){Debug.LogWarning("ROOMPLAY_"+tag+" shared load failed: "+e.Message+", using the older files");return false;}
        }
        // the Somali: a wrapper object (the pet's root, scaled, moved and turned) holding the glb, which is turned by SomaliFix if the model's front is not +z
        float SomaliYaw=PetEnv("ODDITY_SOMALIYAW")!=null?float.Parse(PetEnv("ODDITY_SOMALIYAW"),CultureInfo.InvariantCulture):180+30;
        float SomaliFix=PetEnv("ODDITY_SOMALIFIX")!=null?float.Parse(PetEnv("ODDITY_SOMALIFIX"),CultureInfo.InvariantCulture):0;
        AnimationClip PetClipCopy(AnimationClip src,bool loop)
        {
            var c=Instantiate(src);c.name=src.name;c.legacy=false;
            var s=AnimationUtility.GetAnimationClipSettings(c);s.loopTime=loop;s.loopBlend=false;AnimationUtility.SetAnimationClipSettings(c,s);
            c.wrapMode=loop?WrapMode.Loop:WrapMode.ClampForever;return c;
        }
        Vector2 SomaliSpot(){var a=PetEnv("ODDITY_SOMALISPOT")!=null?PetEnv("ODDITY_SOMALISPOT").Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray():apt?new[]{Apartment.SomaliStart.x,Apartment.SomaliStart.y}:new[]{-1.30f,-.85f};return new Vector2(a[0],a[1]);}
        void SpawnSomali()
        {
            if(SpawnShared("SOMALI","somali",SomaliSharedFile,SomaliYaw,SomaliSpot(),true,SomaliBodyLength,new[]{"SpineRoot_30"},new[]{"Head_22"}))return;
            try{
                // the lying clips live in somali_cat_lie.glb (the same model plus six clips); without it the plain glb is used and the lie keys only show a notice
                string file=File.Exists(Path.Combine(Application.dataPath,"../"+SomaliLieFile))?SomaliLieFile:SomaliFile;
                if(!File.Exists(Path.Combine(Application.dataPath,"../"+file))){Debug.LogWarning("ROOMPLAY_SOMALI file missing "+file+", no Somali");return;}
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(file);
                if(prefab==null){AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);prefab=AssetDatabase.LoadAssetAtPath<GameObject>(file);}
                if(prefab==null){Debug.LogWarning("ROOMPLAY_SOMALI import failed "+file+", no Somali");return;}
                var all=AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>().ToList();
                AnimationClip Find(string n)=>all.FirstOrDefault(c=>c.name==n) ?? all.FirstOrDefault(c=>c.name.EndsWith(n) && !c.name.StartsWith("__preview__"));
                bool lie=Enumerable.Range(5,6).All(i=>Find(SomaliClips[i])!=null);
                int count=lie?SomaliClips.Length:5;
                var p=new Pet{tag="SOMALI",somali=true,hasLie=lie,clipNames=SomaliClips.Take(count).ToArray(),clips=new AnimationClip[count]};
                if(!lie)Debug.LogWarning("ROOMPLAY_SOMALI no lying clips in "+file+", keys 5 and 6 show a notice");
                for(int i=0;i<count;i++){
                    var src=Find(SomaliClips[i]);
                    if(src==null){Debug.LogWarning("ROOMPLAY_SOMALI clip missing "+SomaliClips[i]+" (have "+string.Join(",",all.Select(c=>c.name))+"), no Somali");return;}
                    p.clips[i]=PetClipCopy(src,SomaliLoops(i));}
                var wrap=new GameObject("Somali");var go=Instantiate(prefab,wrap.transform);go.name="SomaliModel";go.transform.localRotation=Quaternion.Euler(0,SomaliFix,0);
                p.root=wrap.transform;
                foreach(var a in go.GetComponentsInChildren<Animation>())Destroy(a);
                // a ground/shadow plane came inside the glb: any static mesh (not skinned) that is flat and wider than it is tall, or named like one, goes
                var removed=new List<string>();
                foreach(var mr in go.GetComponentsInChildren<MeshRenderer>(true)){
                    var bb=mr.bounds;string nm=mr.name.ToLowerInvariant();
                    if(nm.Contains("plane")||nm.Contains("ground")||nm.Contains("floor")||nm.Contains("shadow")||bb.size.y<.25f*Mathf.Max(bb.size.x,bb.size.z)){removed.Add(mr.name+" "+F(bb.size.x)+"x"+F(bb.size.y)+"x"+F(bb.size.z));mr.gameObject.SetActive(false);Destroy(mr.gameObject);}}
                Debug.Log("ROOMPLAY_SOMALI removed="+(removed.Count==0?"none":string.Join(",",removed)));
                foreach(var r in go.GetComponentsInChildren<Renderer>())if(r is SkinnedMeshRenderer smr)smr.updateWhenOffscreen=true;
                var host=go.GetComponent<Animator>();if(host==null)host=go.AddComponent<Animator>();PetGraph(p,host);
                PetSfxSetup(p,wrap);
                // on the floor in front of the plant corner, clear of every footprint (plant x -1.92 -> -1.64, z -0.66 -> -0.38; TV stand z -2 -> -1.62), in the open view of the default camera and away from the cartoon cat, turned toward the camera
                PetPlace(p,SomaliSpot(),SomaliYaw,true,SomaliBodyLength);
                Debug.Log("ROOMPLAY_SOMALI clips="+string.Join(",",p.clips.Select(c=>c.name+":"+F(c.length)+(c.legacy?"L":"M")))+" renderers="+go.GetComponentsInChildren<Renderer>().Length+" material="+go.GetComponentsInChildren<Renderer>()[0].sharedMaterial.shader.name);
                pets.Add(p);
            }catch(Exception e){Debug.LogWarning("ROOMPLAY_SOMALI failed: "+e.Message);}
        }

        // the realistic white cat (Blender-made clips, git-ignored Assets/Models/test/whitecat-2-N-anim.glb, the newest N): a wrapper root like the Somali's. Its Walk clip covers 0.70 model units a second,
        // so at the body length scale it walks 0.70 * scale m/s; Shift 1.6x, autonomous 0.8x
        float WhiteYaw=PetEnv("ODDITY_WHITEYAW")!=null?float.Parse(PetEnv("ODDITY_WHITEYAW"),CultureInfo.InvariantCulture):180-25;
        float WhiteFix=PetEnv("ODDITY_WHITEFIX")!=null?float.Parse(PetEnv("ODDITY_WHITEFIX"),CultureInfo.InvariantCulture):0;
        void SpawnWhite()
        {
            try{
                var dir=Path.Combine(Application.dataPath,"Models/test");
                var found=Directory.Exists(dir)?Directory.GetFiles(dir,"whitecat-2-*-anim.glb").OrderByDescending(f=>{var parts=Path.GetFileNameWithoutExtension(f).Split('-');return parts.Length>=3 && int.TryParse(parts[2],out var n)?n:-1;}).FirstOrDefault():null;
                if(found==null){Debug.LogWarning("ROOMPLAY_WHITE no whitecat-2-N-anim.glb in "+WhiteDir+", no white cat");return;}
                string file=WhiteDir+Path.GetFileName(found);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(file);
                if(prefab==null){AssetDatabase.ImportAsset(file,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);prefab=AssetDatabase.LoadAssetAtPath<GameObject>(file);}
                if(prefab==null){Debug.LogWarning("ROOMPLAY_WHITE import failed "+file+", no white cat");return;}
                var all=AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>().ToList();
                AnimationClip Find(string n)=>all.FirstOrDefault(c=>c.name==n) ?? all.FirstOrDefault(c=>c.name.EndsWith(n) && !c.name.StartsWith("__preview__"));
                var wnames=SleepAwareNames(n=>Find(n)!=null);
                var p=new Pet{tag="WHITE",kind="white",somali=true,white=true,hasLie=true,hasSleep=wnames.Length>WhiteClips.Length,clipNames=wnames,clips=new AnimationClip[wnames.Length]};
                for(int i=0;i<wnames.Length;i++){
                    var src=Find(wnames[i]);
                    if(src==null){Debug.LogWarning("ROOMPLAY_WHITE clip missing "+wnames[i]+" (have "+string.Join(",",all.Select(c=>c.name))+"), no white cat");return;}
                    p.clips[i]=PetClipCopy(src,WhiteLoops(i));}
                var wrap=new GameObject("White");var go=Instantiate(prefab,wrap.transform);go.name="WhiteModel";go.transform.localRotation=Quaternion.identity;
                p.root=wrap.transform;
                foreach(var a in go.GetComponentsInChildren<Animation>())Destroy(a);
                foreach(var r in go.GetComponentsInChildren<Renderer>())if(r is SkinnedMeshRenderer smr)smr.updateWhenOffscreen=true;
                var host=go.GetComponent<Animator>();if(host==null)host=go.AddComponent<Animator>();PetGraph(p,host);
                // measured, not assumed: the nose points from Pelvis to Head; the model is turned so that direction is the wrapper's +z, which is what every heading in this file means
                Transform Bone(string n)=>go.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==n);
                var head=Bone("Head");var pelvis=Bone("Pelvis");p.headB=head;
                if(head!=null && pelvis!=null){
                    var fw=Vector3.ProjectOnPlane(head.position-pelvis.position,Vector3.up);
                    if(fw.sqrMagnitude>1e-8f){float ang=Mathf.Atan2(fw.x,fw.z)*Mathf.Rad2Deg;go.transform.localRotation=Quaternion.Euler(0,WhiteFix-ang,0);Debug.Log("ROOMPLAY_WHITE nose measured at "+F(ang)+" deg from +z, model turned by "+F(-ang));}
                }else Debug.LogWarning("ROOMPLAY_WHITE Head or Pelvis bone missing, facing unmeasured");
                PetSfxSetup(p,wrap);
                // front left on the open floor in front of the TV stand (z -2 -> -1.62), clear of the Somali (-1.30, -0.85) and the rug cat, turned toward the camera at an angle the other way
                var spot=PetEnv("ODDITY_WHITESPOT")!=null?PetEnv("ODDITY_WHITESPOT").Split(',').Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray():apt?new[]{Apartment.WhiteStart.x,Apartment.WhiteStart.y}:new[]{-.25f,-1.05f};
                PetPlace(p,new Vector2(spot[0],spot[1]),WhiteYaw,true,WhiteBodyLength);
                float scale=p.root.localScale.x;
                var wjson=Path.ChangeExtension(found,".json");if(File.Exists(wjson) && p.hasSleep)p.sleepFp=CatSleep.ParseFootprints(File.ReadAllText(wjson));
                Debug.Log("ROOMPLAY_CATSLEEP who=WHITE clips="+(p.hasSleep?CatSleep.Count*3:0)+" footprints="+(p.sleepFp!=null?string.Join("/",p.sleepFp.Select(f=>F(f.lng*scale)+"x"+F(f.sht*scale))):"none")+" (m, long x short)");
                p.walkSpd=.70f*scale*CatSpeedMul;p.runSpd=p.walkSpd*2f;p.aiSpd=p.walkSpd*.8f;
                var mat=go.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m!=null);
                Debug.Log("ROOMPLAY_CATSPEED name=WHITE walk="+F(p.walkSpd)+" run="+F(p.runSpd)+" ai="+F(p.aiSpd));Debug.Log("ROOMPLAY_WHITE file="+Path.GetFileName(found)+" speed="+F(p.walkSpd)+" m/s run="+F(p.runSpd)+" ai="+F(p.aiSpd)+" clips="+string.Join(",",p.clips.Select(c=>c.name+":"+F(c.length)+(c.legacy?"L":"M")))+" renderers="+go.GetComponentsInChildren<Renderer>().Length+" material="+(mat!=null?mat.shader.name+" colour="+(mat.HasProperty("_BaseColor")?mat.GetColor("_BaseColor").ToString():"-")+" map="+(mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap")!=null):"none"));
                Debug.Log("ROOMPLAY_FUR who=WHITE white="+CatFur.Whiten(go)+" fur="+CatFur.Apply(go)); // shell fur (trial): ODDITY_FUR=0 turns it off
                pets.Add(p);
            }catch(Exception e){Debug.LogWarning("ROOMPLAY_WHITE failed: "+e.Message);}
        }
        void SetPetClip(Pet p,int i,bool log)
        {
            bool changed=i!=p.index || p.loopCount<0;
            p.index=i;p.loopCount=0;
            if(changed)PetSfxClipStart(p,i);
            var old=p.outp.GetSourcePlayable();if(old.IsValid())p.graph.DestroyPlayable(old);
            var pl=AnimationClipPlayable.Create(p.graph,p.clips[i]);pl.SetApplyFootIK(false);p.outp.SetSourcePlayable(pl);
            if(log)Debug.Log("ROOMPLAY_"+p.tag+" clip="+p.clipNames[i]);
        }
        // the Somali rests in one of three poses: sits (SitDown once, then SittingIdle), lies like a sphinx (LieDown, LyingIdle) or loafs (LoafDown, LoafIdle). It gets up with the matching Up clip
        // (StandUp, GetUp, LoafUp) and then goes on; changing between poses stands up first (restNext). PetAnimTick moves between the clips
        static readonly string[] RestNames={"","sit","lie","loaf","groom","stretch","jump","sleep"};
        void PetSit(Pet p)=>PetRest(p,1);
        // kinds 1 sit, 2 lie, 3 loaf; 4 groom (sits first, then loops Groom), 5 stretch (once, standing), 6 jump in place (once)
        void PetRest(Pet p,int kind)
        {
            if(!p.somali || p.job!=0)return;
            if(kind>=4 && kind!=6 && !p.white)return;
            if(kind!=1 && !p.hasLie){Notice("이 소말리 파일에는 눕는 동작이 없음 (somali_cat_lie.glb)");return;}
            int s=p.sit;
            if(kind==7){PetSleepStart(p);return;}
            if(kind==5 || kind==6){
                if(s!=0){PetStand(p,kind);return;} // up first, then the stretch or the jump
                p.goal=null;p.faceTo=float.NaN;p.moving=false;p.restNext=0;
                Debug.Log("ROOMPLAY_"+p.tag+" rest pose="+RestNames[kind]);
                if(kind==5){p.sit=11;p.oneClip=13;SetPetClip(p,p.oneClip,true);}
                else if(p.white){p.jobFrom=p.jobTo=p.root.position;p.job=4;p.jobT=0;p.jobDur=p.clips[11].length;p.jobArc=.12f;p.jobHead=p.heading;SetPetClip(p,11,true);}
                else{p.jobFrom=p.jobTo=p.root.position;p.job=4;p.jobT=0;p.jobDur=.3f;p.jobArc=.1f;p.jobHead=p.heading;SetPetClip(p,0,true);} // the Somali has no jump clip: a 0.3 s hop in its Idle pose
                return;}
            if(kind==4){
                if(s==10){p.restNext=0;return;}
                if(s==2){p.sit=10;p.restNext=0;SetPetClip(p,12,true);Debug.Log("ROOMPLAY_"+p.tag+" rest pose=groom");return;}
                if(s==1){p.groomAfter=true;p.restNext=0;return;}
                if(s!=0){PetStand(p,4);return;}
                p.groomAfter=true;kind=1; // from standing: sit down, the Groom follows
            }
            else if(kind==1 && s==10){p.sit=2;p.restNext=0;SetPetClip(p,3,true);return;}
            if((kind==1 && (s==1||s==2)) || (kind==2 && (s==4||s==5)) || (kind==3 && (s==7||s==8))){p.restNext=0;if(kind==1)p.groomAfter=false;return;} // already in that pose
            if(s!=0){PetStand(p,kind);return;} // another pose or an Up clip playing: up first, then this pose
            p.goal=null;p.faceTo=float.NaN;p.moving=false;p.restNext=0;
            p.sit=kind==1?1:kind==2?4:7;SetPetClip(p,kind==1?2:kind==2?5:8,true);
            Debug.Log("ROOMPLAY_"+p.tag+" rest pose="+(p.groomAfter?"groom":RestNames[kind]));
        }
        // ---- sleeping: the seven poses of CatSleep. Down once, Idle loops, Up when it leaves (Space, a walk, another pose, a new AI choice). PetRestTick grounds it like any lying clip (the lowest vertex on the surface)
        static readonly bool LampEnv=Environment.GetEnvironmentVariable("ODDITY_LAMP")=="1";
        static CatSleep.Place PlaceOf(string perchOrAct)
        {
            if(string.IsNullOrEmpty(perchOrAct))return CatSleep.Place.Floor;
            if(perchOrAct=="bed")return CatSleep.Place.Bed;
            if(perchOrAct.StartsWith("tower"))return CatSleep.Place.Tower;
            if(perchOrAct=="sill")return CatSleep.Place.Sill;
            switch(perchOrAct){case "floor":case "rug":case "here":case "harin":case "window":case "clip":case "wander":case "stretch":case "idle":return CatSleep.Place.Floor;}
            return CatSleep.Place.Other;
        }
        CatSleep.Place PlaceOfPet(Pet p)=>p.perch!=null?PlaceOf(p.perch.name):p.onSofa?CatSleep.Place.Other:CatSleep.Place.Floor;
        // the top of a perch in metres (width, depth) from its colliders, null when it has none
        Vector2? PerchSize(string name)
        {
            if(name==null || !perches.TryGetValue(name,out var pr) || !perchColsOf.TryGetValue(pr,out var cols) || cols.Count==0)return null;
            Bounds b=default;bool first=true;foreach(var c in cols){if(c==null)continue;if(first){b=c.bounds;first=false;}else b.Encapsulate(c.bounds);}
            return first?(Vector2?)null:new Vector2(b.size.x,b.size.z);
        }
        // does the pose's footprint (json x the cat's scale) fit the perch (null: the floor, which always does)? Without a json every pose is believed to fit
        bool SleepFits(Pet p,int pose,string perch)
        {
            if(p.sleepFp==null || p.root==null)return true;
            var size=PerchSize(perch);if(size==null)return true;
            float sc=p.root.localScale.x;var fp=p.sleepFp[pose];
            return CatSleep.Fits(fp.lng*sc,fp.sht*sc,size.Value.x,size.Value.y);
        }
        // the pose a cat takes where it is (or at the perch/act it is heading for): weighted by CatSleep, -1 when none is allowed and fits
        int SleepChoose(Pet p,string perchOrAct)
        {
            string perch=perchOrAct!=null && perches.ContainsKey(perchOrAct)?perchOrAct:null;
            return CatSleep.Choose(PlaceOf(perchOrAct),LampEnv,k=>SleepFits(p,k,perch),Rng());
        }
        void PetSleepStart(Pet p)
        {
            if(!p.hasSleep){Notice("이 고양이는 잠자는 동작이 없음");return;}
            int s=p.sit;
            if(s==12||s==13){if(p.sleepCur==p.sleepPose){p.restNext=0;return;}PetStand(p,7);return;} // another pose: the Up clip of this one first
            if(s!=0){PetStand(p,7);return;}
            string pn=p.perch!=null?p.perch.name:null;var place=PlaceOfPet(p);
            if(!CatSleep.Allowed(p.sleepPose,place) || !SleepFits(p,p.sleepPose,pn)){ // the pose chosen on the way does not fit up here
                int alt=SleepChoose(p,pn??(p.onSofa?"sofa":null));
                if(alt<0){Debug.Log("ROOMPLAY_CATSLEEP who="+p.tag+" pose="+CatSleep.Id[p.sleepPose]+" on="+(pn??"floor")+" does not fit, loaf instead");PetRest(p,3);return;}
                p.sleepPose=alt;}
            p.goal=null;p.faceTo=float.NaN;p.moving=false;p.restNext=0;p.groomAfter=false;
            if(p.perch!=null && p.perch.name=="sill")p.faceTo=p.perch.yaw+90f; // along the sill, not across its narrow depth
            p.sleepCur=p.sleepPose;p.sit=12;SetPetClip(p,SleepClip(p.sleepCur,0),true);
            Debug.Log("ROOMPLAY_CATSLEEP who="+p.tag+" pose="+CatSleep.Id[p.sleepCur]+" on="+(pn??(p.onSofa?"sofa":"floor")));
        }
        // K: the next pose that is allowed and fits, a notice with its Korean name; Space or a walk wakes the cat
        void CatSleepKey(Pet p,int forcePose=-1)
        {
            if(!p.hasSleep){Notice("이 고양이는 잠자는 동작이 없음");return;}
            if(p.job!=0)return;
            string pn=p.perch!=null?p.perch.name:null;var place=PlaceOfPet(p);
            int cur=(p.sit==12||p.sit==13)?p.sleepCur:p.sleepPose;
            int pick=-1;
            if(forcePose>=0){pick=forcePose;}
            else for(int i=1;i<=CatSleep.Count;i++){int k=(cur+i)%CatSleep.Count;if(CatSleep.Allowed(k,place) && SleepFits(p,k,pn)){pick=k;break;}}
            if(pick<0){Notice("여기엔 맞는 자세가 없음");return;}
            p.sleepPose=pick;Notice("잠자기: "+CatSleep.Korean[pick]);
            PetRest(p,7);
        }
        void PetStand(Pet p,int then=0)
        {
            if(!p.somali)return;
            p.moving=false;p.restNext=then;p.groomAfter=false;
            if(IsUp(p.sit))return;
            int s=p.sit;
            if(s==11){p.sit=0;SetPetClip(p,0,true);int n=p.restNext;p.restNext=0;if(n>0)PetRest(p,n);return;} // a stretch cut short
            if(p.white && s==0){p.restNext=0;if(then>0)PetRest(p,then);return;} // already standing
            if(s==4||s==5){p.sit=6;SetPetClip(p,7,true);}
            else if(s==7||s==8){p.sit=9;SetPetClip(p,10,true);}
            else if(s==12||s==13){p.sit=14;SetPetClip(p,SleepClip(p.sleepCur,2),true);} // asleep (or still lying down): the pose's own Up clip
            else{p.sit=3;SetPetClip(p,4,true);} // sitting or grooming
        }
        void PetAnimTick(Pet p)
        {
            if(!p.somali || !p.graph.IsValid())return;
            var pl=p.outp.GetSourcePlayable();if(!pl.IsValid())return;
            double t=pl.GetTime();
            if(p.sit==1 && t>=p.clips[2].length){if(p.groomAfter){p.groomAfter=false;p.sit=10;SetPetClip(p,12,true);Debug.Log("ROOMPLAY_"+p.tag+" rest pose=groom");}else{p.sit=2;SetPetClip(p,3,true);}}
            else if(p.sit==11 && t>=p.clips[p.oneClip].length){p.sit=0;SetPetClip(p,0,true);}
            else if(p.hasLie && p.sit==4 && t>=p.clips[5].length){p.sit=5;SetPetClip(p,6,true);}
            else if(p.hasLie && p.sit==7 && t>=p.clips[8].length){p.sit=8;SetPetClip(p,9,true);}
            else if(p.sit==12 && t>=p.clips[SleepClip(p.sleepCur,0)].length){p.sit=13;SetPetClip(p,SleepClip(p.sleepCur,1),true);} // the lying-down clip is over: asleep, its Idle loops
            else if(IsUp(p.sit) && t>=p.clips[p.sit==3?4:p.sit==6?7:p.sit==9?10:SleepClip(p.sleepCur,2)].length){
                p.sit=0;SetPetClip(p,0,true);
                int next=p.restNext;p.restNext=0;if(next>0)PetRest(p,next);
            }
        }

        // Tab hands WASD to the pets and back: HARIN -> SOMALI -> WHITE -> TABBY -> HARIN. A pet walks screen-relative like Harin (0.5 m/s, shift 1.2 m/s with the Walk clip played faster), no root motion,
        // inside the room and out of the furniture footprints (the same blockers as Harin's)
        const float CatSpeedMul=2f; // every cat walks twice as fast as the clip's in-place speed, and the Walk clip plays twice as fast so the feet stay planted
        const float AiWalkSpeed=.35f, AiResumeDelay=3f;
        void ToggleControl()
        {
            if(pets.Count==0){Debug.Log("ROOMPLAY_CONTROL target=harin (no cat)");return;}
            if(ctl!=null){ // leaving this pet: a jump in the air lands at once, it stops walking; it lives on its own again after 3 s
                var p=ctl;
                p.hopPlan.Clear();p.chainNext=null;p.hopWait=0;
                if(p.job!=0)PetJobTick(p,100);
                p.goal=null;p.faceTo=float.NaN;
                if(p.moving){p.moving=false;SetPetClip(p,0,false);}
                p.aiPaused=true;p.resumeIn=AiResumeDelay;p.aiPhase=0;
            }
            int next=ctl==null?0:pets.IndexOf(ctl)+1;
            ctl=next<pets.Count?pets[next]:null;
            if(ctl!=null)AiPause(ctl);
            labelPop=Time.realtimeSinceStartup; // the HARIN / CAT / SOMALI label pops and flashes
            if(ctl!=null)PetShot(ctl,"meow.ogg",.5f); // feedback that control moved to a pet; nothing when it goes back to Harin
            Debug.Log("ROOMPLAY_CONTROL target="+(ctl==null?"harin":ctl.tag.ToLowerInvariant()));
        }
        // R while a cat is controlled: re-import its files (the newest whitecat-2-N-anim for WHITE), take the old cat out and spawn it again in the same place in the Tab order,
        // standing where it stood if it was on the floor (on the sofa or a perch it starts from its spawn spot), still controlled. Spot reservations go with the old cat
        void ReloadPet(Pet old)
        {
            int at=pets.IndexOf(old);if(at<0)return;
            var xz=PetXZ(old);float yaw=old.root!=null?old.root.eulerAngles.y:0;bool onFloor=!old.onSofa && old.perch==null && old.job==0;
            if(old.graph.IsValid())old.graph.Destroy();if(old.root!=null)Destroy(old.root.gameObject);
            pets.RemoveAt(at);ctl=null;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var f in new[]{SomaliSharedFile,TabbySharedFile})if(File.Exists(Path.Combine(Application.dataPath,"../"+f)))AssetDatabase.ImportAsset(f,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            int before=pets.Count;
            switch(old.tag){case "SOMALI":SpawnSomali();break;case "WHITE":SpawnWhite();break;case "TABBY":SpawnTabby();break;}
            if(pets.Count==before){Notice(old.tag+" 다시 불러오기 실패");Debug.LogWarning("ROOMPLAY_RELOAD "+old.tag+" failed");return;}
            var p=pets[pets.Count-1];pets.RemoveAt(pets.Count-1);pets.Insert(at,p);
            if(onFloor && p.root!=null){var pos=p.root.position;p.root.position=new Vector3(xz.x,pos.y,xz.y);p.root.rotation=Quaternion.Euler(0,yaw,0);}
            ctl=p;AiPause(p);labelPop=Time.realtimeSinceStartup;
            Notice(old.tag+" 다시 불러옴");Debug.Log("ROOMPLAY_RELOAD "+old.tag+" at=("+F(PetXZ(p).x)+","+F(PetXZ(p).y)+") floor="+(onFloor?1:0));
        }
        // a pet's own keys while it is controlled: digits are clips (kept after release), space idle, C/V/K up onto the sofa, the rest have no pet meaning
        float labelPop=-10;
        static readonly int[] SleepKeyPose={1,0,6,2}; // CatSleep pose index for Z X C V
        // B while a cat is controlled: a guest cat (git-ignored DevMotion/Cat/Guest/an_animated_cat.glb, a sitting cat licking its paw, one 6 s clip) stands in for it on the same spot,
        // scaled to GuestHeight and turned the way the cat faced; the cat itself is hidden meanwhile. B again, any other cat key, a walk or Tab brings the cat back
        const string GuestFile="Assets/Oddity/DevMotion/Cat/Guest/an_animated_cat.glb";
        static readonly float GuestHeight=PetEnv("ODDITY_GUESTHEIGHT")!=null?float.Parse(PetEnv("ODDITY_GUESTHEIGHT"),CultureInfo.InvariantCulture):.30f;
        GameObject guest;PlayableGraph guestGraph;Pet guestOf;readonly List<Renderer> guestHid=new List<Renderer>();
        void GuestEnd()
        {
            if(guest==null)return;
            if(guestGraph.IsValid())guestGraph.Destroy();Destroy(guest);guest=null;
            foreach(var r in guestHid)if(r!=null)r.enabled=true;guestHid.Clear();guestOf=null;
            Debug.Log("ROOMPLAY_GUEST off");
        }
        void GuestTick(){if(guest!=null && (guestOf!=ctl || guestOf.root==null || guestOf.moving || guestOf.goal!=null || guestOf.job!=0))GuestEnd();}
        void GuestToggle(Pet p)
        {
            if(guest!=null){GuestEnd();return;}
            if(p.job!=0 || p.root==null)return;
            try{
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(GuestFile);
                if(prefab==null){AssetDatabase.ImportAsset(GuestFile,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);prefab=AssetDatabase.LoadAssetAtPath<GameObject>(GuestFile);}
                if(prefab==null){Notice("손님 고양이 파일이 없음");Debug.LogWarning("ROOMPLAY_GUEST missing "+GuestFile);return;}
                var src=AssetDatabase.LoadAllAssetsAtPath(GuestFile).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview__"));
                if(src==null){Notice("손님 고양이 동작이 없음");return;}
                guest=new GameObject("GuestCat");var go=Instantiate(prefab,guest.transform);
                foreach(var a in go.GetComponentsInChildren<Animation>())Destroy(a);
                var smrs=go.GetComponentsInChildren<SkinnedMeshRenderer>();foreach(var r in smrs)r.updateWhenOffscreen=true;
                var an=go.GetComponent<Animator>();if(an==null)an=go.AddComponent<Animator>();an.applyRootMotion=false;
                guestGraph=PlayableGraph.Create("GUEST");guestGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var outp=AnimationPlayableOutput.Create(guestGraph,"GUEST",an);var pl=AnimationClipPlayable.Create(guestGraph,PetClipCopy(src,true));pl.SetApplyFootIK(false);outp.SetSourcePlayable(pl);guestGraph.Play();guestGraph.Evaluate(0);
                // measured on the posed skin at scale 1: height, lowest point, the middle of the footprint and the nose direction (hips -> head)
                var lo=new Vector3(float.MaxValue,float.MaxValue,float.MaxValue);var hi=-lo;var bake=new Mesh();var vs=new List<Vector3>();int n=0;
                foreach(var r in smrs){r.BakeMesh(bake,true);bake.GetVertices(vs);var m=r.transform.localToWorldMatrix;foreach(var v in vs){var w=m.MultiplyPoint3x4(v);lo=Vector3.Min(lo,w);hi=Vector3.Max(hi,w);n++;}}
                Destroy(bake);
                if(n==0){Notice("손님 고양이에 메시가 없음");GuestEnd();return;}
                float scale=GuestHeight/Mathf.Max(1e-4f,hi.y-lo.y);
                Transform Bone(string k)=>go.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name.Contains(k));
                var head=Bone("j_head");var hips=Bone("j_hips");float nose=0;
                if(head!=null && hips!=null){var fw=Vector3.ProjectOnPlane(head.position-hips.position,Vector3.up);if(fw.sqrMagnitude>1e-8f)nose=Mathf.Atan2(fw.x,fw.z)*Mathf.Rad2Deg;}
                float yaw=p.root.eulerAngles.y-nose;var rot=Quaternion.Euler(0,yaw,0);
                var mid=rot*new Vector3((lo.x+hi.x)/2*scale,0,(lo.z+hi.z)/2*scale);var xz=PetXZ(p);
                float floorY=p.surf; // the height of what the cat stands on
                guest.transform.localScale=Vector3.one*scale;guest.transform.rotation=rot;guest.transform.position=new Vector3(xz.x-mid.x,floorY-lo.y*scale,xz.y-mid.z);
                foreach(var r in p.root.GetComponentsInChildren<Renderer>())if(r.enabled){r.enabled=false;guestHid.Add(r);}
                guestOf=p;Notice("손님 고양이: 앉아서 앞발 핥기 (B 다시 누르면 끝)");
                Debug.Log("ROOMPLAY_GUEST on for="+p.tag+" clip="+src.name+":"+F(src.length)+" height="+F((hi.y-lo.y)*scale)+" scale="+F(scale)+" nose="+F(nose)+" at=("+F(xz.x)+","+F(xz.y)+") y="+F(guest.transform.position.y)+" vertices="+n);
            }catch(Exception e){Debug.LogWarning("ROOMPLAY_GUEST failed: "+e.Message);GuestEnd();}
        }
        const float CatJumpUpSeconds=.5f, CatJumpDownSeconds=.4f;
        void CatCommand(string key)
        {
            var p=ctl;
            if(key=="wait" || key=="up" || key=="down")return;
            if(key=="b"){GuestToggle(p);return;} // B: the guest cat licks its paw in this cat's place
            if(guest!=null)GuestEnd(); // any other cat key brings the cat itself back first
            if(key=="space"){
                if(p.job==0){
                    if(p.somali && p.sit!=0){PetStand(p);return;} // sitting, lying or loafing: get up, then Idle
                    p.goal=null;p.faceTo=float.NaN;p.moving=false;SetPetClip(p,0,true);}
                return;}
            int act=key.Length==1?"123456789".IndexOf(key[0]):-1; // 1 lie, 2 sit, 3 loaf, 4 walk in place, 5 jump, 6 meow, 7 groom, 8 stretch, 9 stand: the same for every cat; 0 does nothing
            if(key=="0")return; // no action on 0
            if(key=="l"){if(p.headB==null){Notice("이 고양이는 머리 뼈가 없음");return;}p.lookOn=!p.lookOn;Notice(p.lookOn?p.tag+" 카메라 바라보기":p.tag+" 바라보기 끝");Debug.Log("ROOMPLAY_CATLOOK name="+p.tag+" on="+(p.lookOn?1:0));return;} // L: this cat looks at the camera (each cat keeps its own)
            if(act>=0){if(p.job!=0)return;CatAction(p,act);return;}
            if(key=="left"||key=="right"){if(p.job==0){p.heading+=key=="left"?45:-45;p.faceTo=float.NaN;}return;}
            if(key=="k"){CatSleepKey(p);return;} // K: sleep, each press the next pose
            if(key.StartsWith("catsleep") && int.TryParse(key.Substring(8),out var csn) && csn>=1 && csn<=CatSleep.Count){CatSleepKey(p,csn-1);return;} // script keys catsleep1..7: that pose
            // Z X C V: one sleep pose each, in the order of the reference chart (curl ball, loaf, side curl, side loose); full stretch, belly up and paw cover stay on K
            {int zi=key.Length==1?"zxcv".IndexOf(key[0]):-1;if(zi>=0){CatSleepKey(p,SleepKeyPose[zi]);return;}}
            if(key=="catsofa"){ // script key: up onto the nearest sofa seat (was C and V until 2026-11-11)
                if(p.job!=0 || p.onSofa)return;
                var cp=new Vector2(p.root.position.x,p.root.position.z);
                if(!spotsOf.TryGetValue("sofa",out var sl) || sl.Count==0){Notice("소파가 없음");return;}
                var sp=FreeSpot("sofa",p,cp);if(sp==null){Notice("자리가 없어요");return;}
                PetGoToSofa(p,sp,cp);
                p.arriveRest=0; // C and V sit
                return;
            }
            Notice("고양이에게 없는 동작");
        }
        // one shared table of actions for CAT, SOMALI and WHITE: the digit keys 1 2 3 4 5 6 7 8 9 0 are actions 0..9. A cat plays the clip it has; without it the notice says so
        static readonly string[] ActionNames={"눕기 (스핑크스)","앉기","식빵","걷기 (제자리)","점프 (제자리)","울기","그루밍 (앉아서)","기지개","일어서기"};
        static readonly string[] ActionLog={"lie","sit","loaf","walk","jump","meow","groom","stretch","stand"};
        static bool CanDo(Pet p,int a)
        {
            switch(a){
                case 0:case 2:return p.somali && p.hasLie;
                case 1:return p.somali;
                case 3:case 4:case 5:case 8:return true; // jump: the shared Jump clip; meow: a meow sound
                case 6:case 7:return p.white;
            }
            return false;
        }
        // the action the pet is in now (for the key list and M), -1 none
        int CurAction(Pet p)
        {
            if(p.job==4)return 4;
            switch(p.index){case 2:case 3:return 1;case 5:case 6:return 0;case 8:case 9:return 2;case 1:return 3;case 11:return 4;case 12:return 6;case 13:return 7;case 0:return 8;}
            return -1;
        }
        void CatAction(Pet p,int a)
        {
            if(!CanDo(p,a)){Notice("이 고양이는 못 하는 동작");Debug.Log("ROOMPLAY_"+p.tag+" action="+ActionLog[a]+" unavailable");return;}
            Debug.Log("ROOMPLAY_"+p.tag+" action="+ActionLog[a]);
            switch(a){
                case 0:PetRest(p,2);return;
                case 1:PetRest(p,1);return;
                case 2:PetRest(p,3);return;
                case 3:p.goal=null;p.faceTo=float.NaN;p.moving=false;p.sit=0;SetPetClip(p,1,true);return;
                case 4:PetRest(p,6);return; // the shared Jump clip (the older Somali file: a small hop in Idle)
                case 5:PetRandomMeow(p,.9f);return; // sound only
                case 6:PetRest(p,4);return;
                case 7:PetRest(p,5);return;
                case 8:p.goal=null;p.faceTo=float.NaN;if(p.sit!=0)PetStand(p);else{p.moving=false;SetPetClip(p,0,true);}return;
            }
        }
        Vector2 SofaStand(Transform mark,Vector3 front){var s=mark.position+front*.7f;return NearestFree(new Vector2(s.x,s.z));}
        Vector2 SofaStand(Spot sp)=>NearestFree(sp.land+new Vector2(sp.fwd.x,sp.fwd.z)*.7f);
        void PetGoToSofa(Pet p,Spot sp,Vector2 look)=>PetGoTo(p,"sofa",SofaStand(sp),sp.mark,look,null,sp);
        // the nearest spot that is out of the footprints and in the room's main open area (not a sealed pocket such as the strip between a chair and the wall)
        Vector2 NearestFree(Vector2 p)
        {
            p=new Vector2(Mathf.Clamp(p.x,LimX0,LimX1),Mathf.Clamp(p.y,ZNear,ZFar));
            var fp=Plan();
            if(!Blocked(p) && fp.InMainArea(p))return p;
            for(float r=.05f;r<=2.5f;r+=.05f)for(int a=0;a<360;a+=15){
                var q=p+new Vector2(Mathf.Cos(a*Mathf.Deg2Rad),Mathf.Sin(a*Mathf.Deg2Rad))*r;q=new Vector2(Mathf.Clamp(q.x,LimX0,LimX1),Mathf.Clamp(q.y,ZNear,ZFar));
                if(!Blocked(q) && fp.InMainArea(q))return q;}
            return p;
        }
        // the walk to p.goal around the furniture: FloorPath on the floor box and the blockers Collide uses, Harin a 0.35 m circle for the cats.
        // A goal inside a blocker snaps to the nearest free point (within 0.6 m); an unreachable one is dropped (a cat picks another activity)
        const float HarinAvoid=.35f;
        FloorPath floorPlan;Vector4 floorPlanKey;
        FloorPath Plan()
        {
            var key=new Vector4(LimX1,ZNear,ZFar,blockers.Count);
            if(floorPlan==null || key!=floorPlanKey){floorPlan=new FloorPath(LimX0,LimX1,ZNear,ZFar,blockers);floorPlanKey=key;}
            return floorPlan;
        }
        static string Who(Pet p)=>p.kind;
        string PathLog(string who,Vector2 from,Vector2 to,List<Vector2> path)
            =>"ROOMPLAY_PATH who="+who+" from=("+F(from.x)+","+F(from.y)+") to=("+F(to.x)+","+F(to.y)+") len="+F(path!=null?FloorPath.Length(path):0)+" straight="+F((to-from).magnitude)+" points="+(path!=null?path.Count:0);
        void PetPlan(Pet p,bool again)
        {
            var from=PetXZ(p);var goal=p.goal.Value;var hp=new Vector2(x,z);
            var path=Plan().Find(from,goal,out bool snapped,hp,HarinAvoid);
            p.pathHarin=hp;
            if(path==null){
                Debug.Log(PathLog(Who(p),from,goal,null)+" unreachable");
                p.goal=null;p.path=null;p.pathGoal=null;p.chainNext=null;
                if(p.auto){p.aiLast=p.aiAct;p.aiPhase=0;p.aiHasTarget=false;Debug.Log("ROOMPLAY_"+p.tag+" ai=pick-again "+p.aiAct+" unreachable");}
                else Notice("갈 수 없는 곳");
                return;
            }
            if(snapped){p.goal=path[path.Count-1];Debug.Log(PathLog(Who(p),from,goal,path)+" snapped to=("+F(p.goal.Value.x)+","+F(p.goal.Value.y)+")");}
            p.pathGoal=p.goal;p.path=path;p.pathI=path.Count>1?1:0;
            Debug.Log(PathLog(Who(p),from,p.goal.Value,path)+(again?" replan":""));
        }
        void PetGoTo(Pet p,string kind,Vector2 goal,Transform seat,Vector2 look,string logKind=null,Spot spot=null)
        {
            SetSpot(p,spot);p.after=null;
            p.pathGoal=null;p.replanned=false;p.goal=goal;p.goalKind=kind;p.goalSeat=seat;p.goalLook=look;p.stuck=0;p.faceTo=float.NaN;p.chainNext=null;p.arriveRest=0;
            p.hopPlan.Clear();p.hopWait=0;p.towerGoal=0; // a new goal drops any hops still planned for the old one (a hiss during the beat on the counter used to send the cat on to the fridge, where the landing rest pose then wiped the new goal)
            Debug.Log("ROOMPLAY_"+p.tag+" goto="+(logKind??kind)+" at=("+F(goal.x)+","+F(goal.y)+")");
        }
        // to a perch (counter, table, fridge, sill): walk to its floor spot and hop up; the fridge goes by the counter. Already on a perch the hop is direct
        bool PetGoToPerch(Pet p,string name,Vector2? near=null,bool random=false)
        {
            if(name=="tower"){name=HighestFreeTower(p);if(name==null){if(!p.auto)Notice("자리가 없어요");return false;}}
            if(!perches.TryGetValue(name,out var pr))return false;
            var cp=new Vector2(p.root.position.x,p.root.position.z);
            var sp=FreeSpot(name,p,near??cp,random);
            if(sp==null){if(!p.auto)Notice("자리가 없어요");return false;}
            if(p.perch==pr && p.spot==sp && AtSpot(p,sp)){p.goal=null;return true;}
            if(name.StartsWith("tower")){ // the cat tower: level by level, one hop (0.4 - 0.5 s) per level; from the floor it walks to the foot first
                if(p.perch!=null && p.perch.name.StartsWith("tower")){PetGoTo(p,name,cp,null,cp,null,sp);return true;}
                int lv=TowerLevel(name);var s1=lv==1?sp:FreeSpot("tower1",p,cp,random);
                if(s1==null){if(!p.auto)Notice("자리가 없어요");return false;}
                PetGoTo(p,"tower1",s1.stand,null,s1.land,lv>1?name+" via=tower1":null,s1);
                if(lv>1){p.towerGoal=lv;p.after=sp;}
                return true;}
            bool onCounter=p.perch!=null && p.perch.name=="counter",onFridge=p.perch!=null && p.perch.name=="fridge";
            if(name=="fridge" && !onCounter){
                var cs=FreeSpot("counter",p,cp,random);if(cs==null){if(!p.auto)Notice("자리가 없어요");return false;}
                PetGoTo(p,"counter",cs.stand,null,cs.land,"fridge via=counter",cs);p.chainNext="fridge";p.after=sp;return true;}
            if((name=="fridge" && onCounter) || (name=="counter" && onFridge)){PetGoTo(p,name,cp,null,cp,null,sp);return true;}
            PetGoTo(p,name,sp.stand,null,sp.land,null,sp);return true;
        }
        static int TowerLevel(string n)=>n.Length>5 && int.TryParse(n.Substring(5),out var v)?v:0;
        // the highest tower level nobody holds (clicking the tower's foot or posts)
        string HighestFreeTower(Pet p){for(int i=9;i>=1;i--)if(perches.ContainsKey("tower"+i) && FreeSpot("tower"+i,p,PetXZ(p))!=null)return "tower"+i;return null;}
        void ScriptClickTower(string key)
        {
            if(ctl==null){Notice("고양이를 Tab 으로 조종해야 함");return;}
            if(!perches.ContainsKey("tower1")){Notice("이 집에는 캣타워가 없음");return;}
            var name=key=="clicktower"?"tower":"tower"+key.Substring(10);
            Debug.Log("ROOMPLAY_CLICKTOWER "+key+" cat="+ctl.tag);
            PetGoToPerch(ctl,name);
        }
        // a left click while a pet is controlled: the same hit sets as Harin's, a different answer
        void CatClickAt(Ray ray)
        {
            var p=ctl;
            if(p.job!=0)return;
            float near=float.MaxValue;Collider got=null;Vector3 at=Vector3.zero;
            foreach(var c in clickHits)if(c!=null && c.Raycast(ray,out var hit,50f) && hit.distance<near){near=hit.distance;got=c;at=hit.point;}
            var cp=new Vector2(p.root.position.x,p.root.position.z);
            if(got!=null){
                var root=got.transform.root;
                if(root==sofa){
                    if(!spotsOf.TryGetValue("sofa",out var sl) || sl.Count==0)return;
                    var sp=FreeSpot("sofa",p,new Vector2(at.x,at.z));if(sp==null){Notice("자리가 없어요");return;}
                    PetGoToSofa(p,sp,cp);
                }else if(perchOf.TryGetValue(got,out var pn))PetGoToPerch(p,pn,new Vector2(at.x,at.z));
                else if(root==tableRoot && perches.ContainsKey("table"))PetGoToPerch(p,"table");
                else PetGoTo(p,"chair",NearestFree(new Vector2(at.x,at.z)),null,new Vector2(at.x,at.z)); // a chair: walk beside it, turned to it
            }else if(WindowRay(ray)){
                PetGoTo(p,"window",NearestFree(WinSpotNow),null,WinLookNow);
            }else{
                if(!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out var d))return;var pt=ray.GetPoint(d);
                PetGoTo(p,"floor",NearestFree(new Vector2(pt.x,pt.z)),null,cp);
            }
        }
        Vector3 SofaCentre(){var rs=sofa.GetComponentsInChildren<Renderer>();if(rs.Length==0)return sofa.position;var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b.center;}
        // the top of the cushion under a world point
        float CushionTop(Vector3 p,float fallback)
        {
            var ray=new Ray(new Vector3(p.x,fallback+.3f,p.z),Vector3.down);float top=float.NaN;
            foreach(var c in propHits)if(c!=null && c.Raycast(ray,out var hit,1f) && !(hit.point.y<=top))top=hit.point.y;
            return float.IsNaN(top)?fallback:top;
        }
        // where on the cushion a pet lands: the seat mark, then moved off the backrest and the armrests (horizontal rays just above the cushion): at least 0.30 m from the backrest, 0.19 m from an arm
        const float SofaBackClear=.30f, SofaSideClear=.19f;
        Vector3 SofaLanding(Spot sp)
        {
            var mark=sp.mark;var front=sp.fwd;
            var m=new Vector3(sp.land.x,mark.position.y,sp.land.y)-front*.05f;float top=CushionTop(m,mark.position.y);
            var side=Vector3.Cross(Vector3.up,front).normalized;var o=new Vector3(m.x,top+.10f,m.z);
            float Probe(Vector3 d){float best=1f;foreach(var c in propHits)if(c!=null && c.transform.root==sofa && c.Raycast(new Ray(o,d),out var h,1f) && h.distance<best)best=h.distance;return best;}
            float back=Probe(-front),pos=Probe(side),neg=Probe(-side);
            var r=m;
            if(back<SofaBackClear)r+=front*Mathf.Min(.2f,SofaBackClear-back);
            if(pos<SofaSideClear)r-=side*Mathf.Min(.15f,SofaSideClear-pos);
            if(neg<SofaSideClear)r+=side*Mathf.Min(.15f,SofaSideClear-neg);
            Debug.Log("ROOMPLAY_SOFALAND seat="+mark.name+" back="+F(back)+" side+="+F(pos)+" side-="+F(neg)+" from=("+F(m.x)+","+F(m.z)+") to=("+F(r.x)+","+F(r.z)+")");
            return r;
        }
        void StartPetJump(Pet p,bool up)
        {
            p.moving=false;p.job=up?1:2;p.jobT=0;p.jobFrom=p.root.position;
            if(up){
                var here=new Vector2(p.jobFrom.x,p.jobFrom.z);
                var sp=p.spot!=null && p.spot.kind=="sofa"?p.spot:FreeSpot("sofa",p,here)??spotsOf["sofa"][0];
                SetSpot(p,sp);p.jobSpot=sp;p.jobSeat=sp.mark;var m=SofaLanding(sp);
                float top=CushionTop(m,sp.mark.position.y);
                p.jobTo=new Vector3(m.x,p.baseY+top,m.z);p.jobDur=CatJumpUpSeconds;p.jobArc=.14f;
            }else{
                var seat=seats.First(t=>t.mark==p.seat);var f=p.fitSpot!=null?SofaStand(p.fitSpot):SofaStand(seat.mark,seat.front);
                p.jobTo=new Vector3(f.x,p.baseY,f.y);p.jobDur=CatJumpDownSeconds;p.jobArc=.1f;
            }
            var dir=p.jobTo-p.jobFrom;dir.y=0;p.jobHead=dir.sqrMagnitude>1e-4f?Mathf.Atan2(dir.x,dir.z)*Mathf.Rad2Deg:p.heading;
            SetPetClip(p,p.JumpClip,true);
        }
        // the hops to get off a perch, fixed: fridge > counter > floor, table / sill / counter > floor; counter <-> fridge only when that perch is the goal itself
        List<string> HopsOff(string at,string goalPerch)
        {
            if(at.StartsWith("tower")){ // level by level: up or down one step at a time, off the tower from level 1 straight to the floor
                int k=TowerLevel(at),g=goalPerch!=null && goalPerch.StartsWith("tower")?TowerLevel(goalPerch):0;var l=new List<string>();
                if(g>k)for(int i=k+1;i<=g;i++)l.Add("tower"+i);
                else if(g>0 && g<k)for(int i=k-1;i>=g;i--)l.Add("tower"+i);
                else if(g==0){for(int i=k-1;i>=1;i--)l.Add("tower"+i);l.Add("floor");}
                return l;}
            if(at=="fridge")return goalPerch=="counter"?new List<string>{"counter"}:new List<string>{"counter","floor"};
            if(at=="counter" && goalPerch=="fridge")return new List<string>{"fridge"};
            return new List<string>{"floor"};
        }
        void NextHop(Pet p)
        {
            if(p.hopPlan.Count==0)return;
            var nx=p.hopPlan[0];
            if(nx=="floor"){p.hopPlan.RemoveAt(0);StartHop(p,null);return;}
            if(perches.TryGetValue(nx,out var np)){
                bool mine=(p.after!=null && p.after.perch==np) || (p.spot!=null && p.spot.perch==np);
                if(!mine && FreeSpot(np.name,p,PetXZ(p))==null){p.hopWait=.5f;return;} // every spot up there is taken: a beat, then again
                p.hopPlan.RemoveAt(0);StartHop(p,np);}
        }
        // a hop between the floor and a perch (or counter and fridge): to is the perch to land on, null lands on the floor spot in front of the perch it is on.
        // 0.5 s for the sofa; here the time grows with the height (about 0.7 s up to a 0.85 m counter, table or sill), the cats play Jump (the older Somali file flies the arc in Idle)
        void StartHop(Pet p,Perch to)
        {
            p.moving=false;p.job=3;p.jobT=0;p.jobFrom=p.root.position;p.jobPerch=to;
            Vector3 end;
            if(to!=null){
                Spot sp;
                if(p.after!=null && p.after.perch==to){sp=p.after;p.after=null;SetSpot(p,sp);p.transit=null;} // the final spot after passing the counter
                else if(p.spot!=null && p.spot.perch==to){sp=p.spot;p.transit=null;}
                else{sp=FreeSpot(to.name,p,PetXZ(p))??spotsOf[to.name][0];p.transit=sp;} // only passing through
                p.jobSpot=sp;var l=sp.land;end=new Vector3(l.x,p.baseY+SpotTop(sp),l.y);
            }
            else{p.transit=null;var os=OccSpot(p);var st=FloorLanding(p,os!=null?os.stand:p.perch.stand);end=new Vector3(st.x,p.baseY,st.y);}
            float dh=end.y-p.jobFrom.y,h=Mathf.Abs(dh);bool up=dh>0;
            p.jobTo=end;
            p.jobDur=up?CatJumpUpSeconds+Mathf.Max(0,h-.45f)*.45f:CatJumpDownSeconds+Mathf.Max(0,h-.45f)*.3f;
            p.jobArc=up?Mathf.Min(.35f,.12f+.2f*h):.1f;
            var dir=end-p.jobFrom;dir.y=0;p.jobHead=dir.sqrMagnitude>1e-4f?Mathf.Atan2(dir.x,dir.z)*Mathf.Rad2Deg:p.heading;
            SetPetClip(p,p.JumpClip,true);
        }
        float PerchTop(Perch pr)
        {
            if(!float.IsNaN(pr.top))return pr.top;
            Physics.SyncTransforms();
            var ray=new Ray(new Vector3(pr.land.x,3.5f,pr.land.y),Vector3.down);float top=float.NaN;
            foreach(var c in clickHits)if(c!=null && pr.pick(c) && c.Raycast(ray,out var hit,6f) && !(hit.point.y<=top))top=hit.point.y;
            if(float.IsNaN(top)){Debug.LogWarning("ROOMPLAY_PERCH "+pr.name+" no surface under ("+F(pr.land.x)+","+F(pr.land.y)+")");top=.8f;}
            Debug.Log("ROOMPLAY_PERCH "+pr.name+" top="+F(top)+" land=("+F(pr.land.x)+","+F(pr.land.y)+") stand=("+F(pr.stand.x)+","+F(pr.stand.y)+")");
            return pr.top=top;
        }
        void BuildPerches()
        {
            perches.Clear();if(!room || perchCols.Count==0)return;
            bool Is(Collider c,string n)=>perchOf.TryGetValue(c,out var v) && v==n;
            var counterStand=NearestFree(apt?Apartment.CounterStand:new Vector2(1.05f,1.10f));
            float runYaw=apt?-90:180; // the apartment's counter run and fridge are on the east wall: a cat sits on them facing west
            perches["counter"]=new Perch{name="counter",stand=counterStand,land=apt?Apartment.CounterLand[0]:new Vector2(1.17f,1.50f),yaw=runYaw,pick=c=>Is(c,"counter")};
            if(!apt || Apartment.HasFridge)perches["fridge"]=new Perch{name="fridge",stand=counterStand,land=apt?Apartment.FridgeLand:new Vector2(1.70f,1.62f),yaw=runYaw,pick=c=>Is(c,"fridge")};
            if(apt){
                perches["sill"]=new Perch{name="sill",stand=NearestFree(new Vector2(Apartment.SillLand[0].x,Apartment.WindowSpot.y)),land=Apartment.SillLand[0],yaw=180,pick=c=>Is(c,"sill")};
                if(Apartment.HasLedge)perches["ledge"]=new Perch{name="ledge",stand=NearestFree(new Vector2(Apartment.LedgeLand[0].x,.75f)),land=Apartment.LedgeLand[0],yaw=180,pick=c=>Is(c,"ledge")};
                if(Apartment.HasBed && Apartment.HasTv)perches["bed"]=new Perch{name="bed",stand=NearestFree(new Vector2(Apartment.BedStand.x+.05f,1.2f)),land=new Vector2(.5f,1.2f),yaw=90,pick=c=>Is(c,"bed")}; // the studio bed (cats may jump on it, not when she lies on it)
                if(Apartment.HasTea)perches["teatable"]=new Perch{name="teatable",stand=NearestFree(new Vector2(Apartment.TeaX1+.35f,(Apartment.TeaZ0+Apartment.TeaZ1)/2)),land=new Vector2((Apartment.TeaX0+Apartment.TeaX1)/2,(Apartment.TeaZ0+Apartment.TeaZ1)/2),yaw=-90,pick=c=>Is(c,"teatable")}; // a cat on the tea table faces the sofa
            }else perches["sill"]=new Perch{name="sill",stand=NearestFree(new Vector2(-1.38f,.2f)),land=new Vector2(-1.88f,.2f),yaw=-90,pick=c=>Is(c,"sill")};
            if(apt && Apartment.HasTower)for(int ti=0;ti<Apartment.TowerLand.Length;ti++){string tn="tower"+(ti+1);perches[tn]=new Perch{name=tn,stand=NearestFree(Apartment.TowerStand),land=Apartment.TowerLand[ti],yaw=180,pick=c=>Is(c,tn)};} // a cat on the tower faces the window
            if(tableRoot!=null){
                var lap=tableRoot.Find("Laptop");
                var rs=tableRoot.GetComponentsInChildren<Renderer>().Where(r=>lap==null || !r.transform.IsChildOf(lap)).ToArray();
                if(rs.Length>0){
                    var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                    Debug.Log("ROOMPLAY_PERCH table bounds x "+F(b.min.x)+".."+F(b.max.x)+" y "+F(b.min.y)+".."+F(b.max.y)+" z "+F(b.min.z)+".."+F(b.max.z));
                    var land=new Vector2(b.center.x+Mathf.Min(.28f,b.extents.x-.15f),b.center.z); // the +x half: the laptop sits on the -x half
                    var stand=NearestFree(new Vector2(b.max.x+.35f,b.center.z));
                    perches["table"]=new Perch{name="table",stand=stand,land=land,yaw=apt?90:180,pick=c=>c.transform.IsChildOf(tableRoot) && (lap==null || !c.transform.IsChildOf(lap))};
                }
            }
            foreach(var k in perches.Keys)spotsOf.Remove(k);
            void AddSpot(Perch pr,Vector2 land,Vector2 stand){if(!spotsOf.TryGetValue(pr.name,out var l))spotsOf[pr.name]=l=new List<Spot>();l.Add(new Spot{key=pr.name+"#"+l.Count,kind=pr.name,perch=pr,land=land,stand=stand,yaw=pr.yaw});}
            foreach(var pr in perches.Values){
                if(apt && pr.name=="sill")foreach(var sl in Apartment.SillLand)AddSpot(pr,sl,NearestFree(new Vector2(sl.x,Apartment.WindowSpot.y))); // 0.5 m apart on the deep sill
                else if(apt && pr.name=="ledge")foreach(var ll in Apartment.LedgeLand)AddSpot(pr,ll,NearestFree(new Vector2(ll.x,.75f)));
                else if(apt && pr.name=="counter")foreach(var cl in Apartment.CounterLand)AddSpot(pr,cl,cl==Apartment.CounterLand[0]?pr.stand:NearestFree(new Vector2(Apartment.CounterStand.x,cl.y))); // 1.2 m apart, the cooktop between is not a spot
                else if(pr.name=="sill")foreach(float zz in new[]{.05f,.60f,1.15f})AddSpot(pr,new Vector2(-1.88f,zz),NearestFree(new Vector2(-1.38f,zz))); // 0.55 m apart: two cats of 0.46 m do not touch
                else if(pr.name=="counter")foreach(float xx in new[]{.62f,1.17f})AddSpot(pr,new Vector2(xx,1.50f),Mathf.Abs(xx-1.17f)<.01f?pr.stand:NearestFree(new Vector2(xx,1.10f)));
                else AddSpot(pr,pr.land,pr.stand); // fridge, table: one spot
                pr.land=spotsOf[pr.name][0].land;
                perchColsOf[pr]=perchCols.Where(c=>pr.pick(c)).ToList();
            }
            Physics.SyncTransforms();
            foreach(var pr in perches.Values)PerchTop(pr);
            Debug.Log("ROOMPLAY_SPOTS "+string.Join(" ",spotsOf.SelectMany(kv=>kv.Value).Select(sp=>sp.key+"("+F(sp.land.x)+","+F(sp.land.y)+(sp.perch!=null?" top "+F(SpotTop(sp)):" x "+F(sp.lo)+".."+F(sp.hi))+")")));
        }
        const float SofaTurnRate=180;
        void PetJobTick(Pet p,float dt)
        {
            p.jobT+=dt;float k=Mathf.Clamp01(p.jobT/p.jobDur);
            var pos=Vector3.Lerp(p.jobFrom,p.jobTo,k);pos.y+=Mathf.Sin(k*Mathf.PI)*p.jobArc;
            p.root.position=pos;
            p.heading=Mathf.LerpAngle(p.heading,p.jobHead,1-Mathf.Exp(-14*Mathf.Min(dt,.05f)));
            p.root.rotation=Quaternion.Euler(0,p.heading,0);
            if(k<1)return;
            if(p.job==3){
                p.job=0;p.root.position=p.jobTo;p.onSofa=false;p.seat=null;p.perch=p.jobPerch;p.surf=p.perch!=null?(p.jobSpot!=null && p.jobSpot.perch==p.perch?SpotTop(p.jobSpot):PerchTop(p.perch)):0;p.settledAt=Time.unscaledTime;p.fitSpot=null;
                SetPetClip(p,0,true);
                if(p.perch!=null){
                    p.faceTo=p.perch.yaw;
                    if(p.hopPlan.Count>0)p.hopWait=.2f; // an intermediate perch on a planned hop chain: a beat, no rest pose
                    else if(p.somali && !p.goal.HasValue){int rk=p.arriveRest>0?p.arriveRest:1;p.arriveRest=0;PetRest(p,rk);} // the Somali rests up there, unless a goal is already waiting (resting would wipe it)
                }
                Debug.Log("ROOMPLAY_"+p.tag+" perch="+(p.perch!=null?p.perch.name:"floor")+" pos=("+F(p.jobTo.x)+","+F(p.jobTo.y)+","+F(p.jobTo.z)+")");
                return;
            }
            if(p.job==4){p.job=0;p.root.position=p.jobTo;SetPetClip(p,0,true);return;} // the jump in place is over
            bool up=p.job==1;p.job=0;p.root.position=p.jobTo;p.onSofa=up;p.surf=up?p.jobTo.y-p.baseY:0;p.settledAt=Time.unscaledTime;p.perch=null;p.fitSpot=up?p.jobSpot:null;
            if(up){p.seat=p.jobSeat;var front=seats.First(t=>t.mark==p.seat).front;
                var side=Vector3.Cross(Vector3.up,front).normalized;var mid=SofaCentre();var toMid=new Vector3(mid.x-p.root.position.x,0,mid.z-p.root.position.z);
                if(Vector3.Dot(side,toMid)<0)side=-side; // along the seat, towards the middle of the sofa: parallel to the backrest, the whole body on the cushion
                p.fitFront=front;p.fitSide=side;p.surfBase=p.jobTo.y-p.baseY;p.faceTo=Mathf.Atan2(front.x,front.z)*Mathf.Rad2Deg;p.sofaTurn=true;p.sofaFront=front;if(p.goalKind=="sofa")p.goal=null;} // after the landing it turns to face the front of the seat (toward the table and TV), then rests
            else{p.fitFront=p.fitSide=Vector3.zero;}
            SetPetClip(p,0,true);
            if(up && p.somali && !p.goal.HasValue){int rk=p.arriveRest>0?p.arriveRest:1;p.arriveRest=0;p.pendingRest=rk;} // the Somali rests on the cushion, once it has turned to face the front
            Debug.Log("ROOMPLAY_"+p.tag+" onsofa="+(up?1:0)+" pos=("+F(p.jobTo.x)+","+F(p.jobTo.y)+","+F(p.jobTo.z)+")");
        }
        void CatTick(float dt,Vector2 input,bool run,float turn)=>PetMove(ctl,dt,input,run,turn,false);
        // one pet's walking, hops and turning: the player's keys and clicks (auto false) or the autonomous state machine (auto true: calmer pace, only goals)
        void PetMove(Pet p,float dt,Vector2 input,bool run,float turn,bool auto)
        {
            p.auto=auto;
            if(p.heading<-999)p.heading=p.root.eulerAngles.y;
            if(p.hopWait>0){p.hopWait-=dt;if(p.hopWait<=0)NextHop(p);return;} // a beat on the intermediate perch, then the next planned hop
            if(p.job!=0){PetJobTick(p,dt);return;}
            bool keys=input!=Vector2.zero;
            if(keys || turn!=0){p.sofaTurn=false;p.pendingRest=0;} // steering cancels the turn on the sofa
            if(keys){p.goal=null;p.chainNext=null;} // the keys take over from a click
            if(p.onSofa && p.goal.HasValue && p.goalKind=="sofa" && p.goalSeat==p.seat)p.goal=null; // already on that seat
            if(p.perch!=null && p.goal.HasValue && p.goalKind==p.perch.name && p.spot!=null && AtSpot(p,p.spot))p.goal=null; // already up there, on that spot
            if(p.somali && p.sit!=0 && (keys || p.goal.HasValue)){PetStand(p);return;} // walking while sitting: stand up first, the walk (a click's goal is kept) starts when StandUp is done
            if(p.onSofa && (keys || p.goal.HasValue)){if(!p.goal.HasValue)SetSpot(p,null);StartPetJump(p,false);return;} // any walk starts with a jump down
            if(p.perch!=null && (keys || p.goal.HasValue)){ // up on a perch: counter to fridge and back are one hop, any other walk hops down first (the fridge only to the counter)
                if(!p.goal.HasValue)SetSpot(p,null);
                var gk=p.goal.HasValue && perches.ContainsKey(p.goalKind)?p.goalKind:null;
                p.hopPlan=HopsOff(p.perch.name,gk);
                if(gk!=null && p.hopPlan.Count>0 && p.hopPlan[p.hopPlan.Count-1]==gk)p.goal=null; // counter <-> fridge, or a level of the tower, as the goal itself: the hops are the whole walk
                Debug.Log("ROOMPLAY_"+p.tag+" hops="+p.perch.name+">"+string.Join(">",p.hopPlan));
                NextHop(p);return;
            }
            var from=new Vector2(p.root.position.x,p.root.position.z);
            var wish=input.normalized;float pace=run?p.runSpd:p.walkSpd;float reach=float.MaxValue;
            if(keys){var fw=MoveFwd();var rt=new Vector3(fw.z,0,-fw.x);var w3=rt*wish.x+fw*wish.y;wish=new Vector2(w3.x,w3.z).normalized;}
            bool lastLeg=true;
            if(!keys && p.goal.HasValue){
                if(!p.pathGoal.HasValue || p.pathGoal.Value!=p.goal.Value)PetPlan(p,false);
                else if(p.path!=null && (new Vector2(x,z)-p.pathHarin).magnitude>.3f)PetPlan(p,true); // Harin moved: the cat goes around her again
            }
            if(!keys && p.goal.HasValue){
                var tgt=p.goal.Value;
                if(p.path!=null && p.path.Count>0){
                    p.pathI=Mathf.Clamp(p.pathI,0,p.path.Count-1);
                    while(p.pathI<p.path.Count-1 && (p.path[p.pathI]-from).sqrMagnitude<.0025f)p.pathI++;
                    tgt=p.path[p.pathI];lastLeg=p.pathI==p.path.Count-1;if(lastLeg)tgt=p.goal.Value;}
                var d=tgt-from;wish=d.normalized;pace=auto?p.aiSpd:p.walkSpd;run=false;reach=d.magnitude;
            }
            { // Shift runs (2x the walk ground speed, no run clip: the Walk clip just plays faster), eased over 0.2 s; a small forward lean
                bool wantRun=run && keys && wish!=Vector2.zero;
                p.runMix=Mathf.MoveTowards(p.runMix,wantRun?1:0,dt/.2f);
                if(keys)pace=Mathf.Lerp(p.walkSpd,p.runSpd,p.runMix);
                if(wantRun && !p.wasRun)Debug.Log("ROOMPLAY_CATRUN name="+p.tag+" speed="+F(p.runSpd)+" clipRate="+F(p.runSpd/p.walkSpd*CatSpeedMul));
                p.wasRun=wantRun;
                p.curPace=pace;
            }
            float stepLen=Mathf.Min(pace*dt,reach);
            var next=Collide(from,new Vector2(Mathf.Clamp(from.x+wish.x*stepLen,LimX0-.1f,LimX1+.1f),Mathf.Clamp(from.y+wish.y*stepLen,ZNear-.1f,ZFar+.1f)));
            next=PetSeparate(p,from,next);
            bool moving=wish!=Vector2.zero && (next-from).sqrMagnitude>1e-10f;
            bool arrived=false;
            if(p.goal.HasValue){
                if(!moving)p.stuck+=dt;else p.stuck=0;
                bool canReplan=p.path!=null && !p.replanned;
                if(p.stuck>1f && canReplan){p.replanned=true;p.stuck=0;PetPlan(p,true);} // blocked for 1 s: one replan
                else if((lastLeg && reach<ClickArrive) || (p.stuck>ClickGiveUp && !canReplan)){arrived=true;moving=false;}
            }
            if(moving){p.heading=Mathf.LerpAngle(p.heading,Mathf.Atan2(wish.x,wish.y)*Mathf.Rad2Deg,1-Mathf.Exp(-12*dt));p.faceTo=float.NaN;
                p.root.position=new Vector3(next.x,p.root.position.y,next.y);}
            else{
                if(turn!=0){p.heading+=turn*TurnRate*dt;p.faceTo=float.NaN;} // held arrows turn it in place like Harin
                if(p.sofaTurn && !float.IsNaN(p.faceTo)){ // the turn on the sofa: 180 degrees a second at most, then it settles
                    p.heading=Mathf.MoveTowardsAngle(p.heading,p.faceTo,SofaTurnRate*dt);
                    if(Mathf.Abs(Mathf.DeltaAngle(p.heading,p.faceTo))<1){p.heading=p.faceTo;p.faceTo=float.NaN;p.sofaTurn=false;
                        Debug.Log("ROOMPLAY_"+p.tag+" sofaface yaw="+F(p.heading)+" front="+F(Mathf.Atan2(p.sofaFront.x,p.sofaFront.z)*Mathf.Rad2Deg));
                        if(p.pendingRest>0){int rk=p.pendingRest;p.pendingRest=0;PetRest(p,rk);}}
                }
                else if(!float.IsNaN(p.faceTo)){p.heading=Mathf.LerpAngle(p.heading,p.faceTo,1-Mathf.Exp(-10*dt));if(Mathf.Abs(Mathf.DeltaAngle(p.heading,p.faceTo))<1)p.faceTo=float.NaN;}
                else if(p.sofaTurn){p.sofaTurn=false;p.pendingRest=0;}
            }
            p.root.rotation=Quaternion.Euler(moving?8f*p.runMix:0,p.heading,0);
            if(moving!=p.moving){
                if(moving)p.walkStart=Time.unscaledTime;
                else if(Time.unscaledTime-p.walkStart>=1f)PetShot(p,"cat_flap_ears",.4f); // stopped after walking at least 1 s
                p.moving=moving;if(!moving)p.settledAt=Time.unscaledTime;SetPetClip(p,moving?1:0,true);}
            if(moving){var pl=p.outp.GetSourcePlayable();if(pl.IsValid())pl.SetSpeed(keys?p.curPace/p.walkSpd*CatSpeedMul:auto?p.aiSpd/p.walkSpd*CatSpeedMul:CatSpeedMul);}
            if(arrived){
                var kind=p.goalKind;var look=p.goalLook;p.goal=null;
                if(kind=="sofa")StartPetJump(p,true);
                else if(perches.TryGetValue(kind,out var pr)){p.hopPlan.Clear();if(p.chainNext!=null)p.hopPlan.Add(p.chainNext);if(p.towerGoal>1){for(int tl=2;tl<=p.towerGoal;tl++)p.hopPlan.Add("tower"+tl);p.towerGoal=0;}Debug.Log("ROOMPLAY_"+p.tag+" hops=floor>"+pr.name+(p.chainNext!=null?">"+p.chainNext:""));p.chainNext=null;StartHop(p,pr);}
                else if(kind=="chair"||kind=="window"||kind=="harin"){if(kind=="harin")look=new Vector2(x,z);var to=look-new Vector2(p.root.position.x,p.root.position.z);if(to.sqrMagnitude>1e-4f)p.faceTo=Mathf.Atan2(to.x,to.y)*Mathf.Rad2Deg;}
            }
        }
        // ---- cats that are not controlled live on their own: one small state machine per cat (0 pick, 1 going, 2 dwelling), built on the same goals, hops and sit code as the player's clicks.
        // Tab to a cat pauses it at once (a hop in the air lands, a walk stops, the pose stays); when Tab moves on it waits 3 s and carries on. ODDITY_CATAI=0 turns it off, ODDITY_CATSEED=n repeats a run.
        readonly bool aiOn=PetEnv("ODDITY_CATAI")!="0";
        System.Random aiRng;
        System.Random Rng(){if(aiRng==null){int seed;if(int.TryParse(PetEnv("ODDITY_CATSEED"),out seed)){aiRng=new System.Random(seed);UnityEngine.Random.InitState(seed);}else aiRng=new System.Random();}return aiRng;}
        float Rnd(float a,float b)=>a+(float)Rng().NextDouble()*(b-a);
        Vector2 PetXZ(Pet p)=>new Vector2(p.root.position.x,p.root.position.z);
        void AiPause(Pet p)
        {
            p.hopPlan.Clear();p.chainNext=null;p.hopWait=0;
            if(p.job!=0)PetJobTick(p,100);
            p.goal=null;p.faceTo=float.NaN;
            if(p.moving){p.moving=false;SetPetClip(p,0,false);}
            p.aiPaused=true;p.resumeIn=0;p.aiPhase=0;p.aiHasTarget=false;
            if(aiOn)Debug.Log("ROOMPLAY_"+p.tag+" ai=paused");
        }
        // a spot at least 0.5 m from the other pets, where they stand, are heading, or have chosen to go
        bool FarFromOthers(Pet p,Vector2 t)
        {
            foreach(var o in pets){
                if(o==p)continue;
                if((PetXZ(o)-t).magnitude<.5f)return false;
                if(o.goal.HasValue && (o.goal.Value-t).magnitude<.5f)return false;
                if(o.aiHasTarget && (o.aiTarget-t).magnitude<.5f)return false;
            }
            return true;
        }
        Vector2? AiFloorPoint(Pet p,Func<Vector2> gen)
        {
            for(int i=0;i<30;i++){
                var t=NearestFree(gen());
                if(Blocked(t) || (t-PetXZ(p)).magnitude<.6f || (t-new Vector2(x,z)).magnitude<.4f || !FarFromOthers(p,t))continue;
                return t;
            }
            return null;
        }
        void AiLog(Pet p,string act,string place,Vector2 at)=>Debug.Log("ROOMPLAY_"+p.tag+" ai="+act+" target="+place+" at=("+F(at.x)+","+F(at.y)+")");
        // the Somali's rest pose when it settles somewhere on its own: sit 40, lie 35, loaf 25 percent (on the sill lie 50, loaf 30, sit 20); only sitting without the lying clips
        int AiRestKind(Pet p,string act)
        {
            if(!p.hasLie)return 1;
            p.aiSleeps=false;
            if(p.hasSleep && Rng().Next(100)<35){int sk=SleepChoose(p,act);if(sk>=0){p.sleepPose=sk;p.aiSleeps=true;return 7;}} // sometimes the rest is a sleep, in a pose that is allowed and fits there (CatSleep)
            int r=Rng().Next(100);
            if(p.white){ // sit 30, lie 30, loaf 25, groom 15 (sill: lie 45, loaf 30, sit 15, groom 10)
                if(act=="sill" || act.StartsWith("tower"))return r<45?2:r<75?3:r<90?1:4;
                return r<30?1:r<60?2:r<85?3:4;}
            if(act=="sill" || act.StartsWith("tower"))return r<50?2:r<80?3:1;
            return r<40?1:r<75?2:3;
        }
        bool AiStart(Pet p,string act)
        {
            var cp=PetXZ(p);string place=act;Vector2 at=cp;bool travel=true;
            if(act=="tower"){var lvs=Enumerable.Range(1,9).Select(i=>"tower"+i).Where(n=>perches.ContainsKey(n) && FreeSpot(n,p,cp,true)!=null).ToList();if(lvs.Count==0)return false;act=lvs[Rng().Next(lvs.Count)];} // the tower: one of its free levels
            switch(act){
                case "wander":case "rug":{
                    Func<Vector2> gen=apt?(act=="rug"?(Func<Vector2>)(()=>new Vector2(Rnd(Apartment.RugBox.xMin,Apartment.RugBox.xMax),Rnd(Apartment.RugBox.yMin,Apartment.RugBox.yMax))):(Func<Vector2>)AptWander):act=="rug"?(Func<Vector2>)(()=>new Vector2(Rnd(-1.25f,-.25f),Rnd(.4f,1.0f))):(()=>new Vector2(Rnd(-1.6f,1.6f),Rnd(-1.6f,1.2f)));
                    var t=AiFloorPoint(p,gen);if(t==null)return false;
                    at=t.Value;place=act=="rug"?"rug":"floor";PetGoTo(p,"floor",at,null,cp);p.aiDur=Rnd(3,8);break;}
                case "sofa":{
                    var sp=FreeSpot("sofa",p,cp,true);if(sp==null)return false;
                    at=sp.land;PetGoToSofa(p,sp,cp);if(p.somali)p.arriveRest=AiRestKind(p,act);p.aiDur=p.aiSleeps?Rnd(20,60):Rnd(10,25);break;}
                case "window":{
                    float wz=Rnd(0f,1.1f);var spot=NearestFree(apt?new Vector2(Rnd(Apartment.WindowRangeX.x,Apartment.WindowRangeX.y),Apartment.WindowSpot.y):new Vector2(-1.40f,wz));if(!FarFromOthers(p,spot))return false;
                    at=spot;PetGoTo(p,"window",spot,null,apt?new Vector2(spot.x,Apartment.WindowLook.y):new Vector2(-RoomHalf,wz));p.aiDur=Rnd(6,15);break;}
                case "harin":{
                    var hp=new Vector2(x,z);var dir=cp-hp;if(dir.sqrMagnitude<1e-4f)dir=Vector2.right;dir.Normalize();
                    Vector2? found=null;
                    foreach(float a in new float[]{0,40,-40,80,-80,120,-120,160}){
                        var d=Quaternion.Euler(0,0,a)*new Vector3(dir.x,dir.y,0);var t=NearestFree(hp+new Vector2(d.x,d.y)*.6f);
                        float dist=(t-hp).magnitude;if(dist>=.45f && dist<=.95f && FarFromOthers(p,t)){found=t;break;}}
                    if(found==null)return false;
                    at=found.Value;PetGoTo(p,"harin",at,null,hp);p.aiDur=Rnd(5,10);break;}
                case "clip":
                    travel=false;
                    p.aiSleeps=false;if(p.sit==0 || IsUp(p.sit))PetRest(p,AiRestKind(p,p.perch!=null?p.perch.name:act));p.aiDur=p.aiSleeps?Rnd(20,60):Rnd(8,20);
                    place="here";break;
                case "idle":
                    travel=false;p.aiDur=Rnd(4,10);place="here";break;
                case "stretch": // the white cat, after a rest: up, a stretch
                    if(!p.white || p.sit==0 || p.job!=0)return false;
                    travel=false;PetStand(p,5);p.aiDur=4.5f;place="here";break;
                default:{ // sill, counter, table, fridge
                    if(!perches.TryGetValue(act,out var pr))return false;
                    if(act=="fridge" && !(p.perch!=null && p.perch.name=="counter") && FreeSpot("counter",p,cp)==null)return false;
                    if(!PetGoToPerch(p,act,null,true))return false;
                    at=p.spot!=null?p.spot.land:pr.land;if(p.somali)p.arriveRest=AiRestKind(p,act);p.aiDur=p.aiSleeps?Rnd(20,60):Rnd(10,30);break;}
            }
            p.aiAct=act;p.aiPlace=place;p.aiTarget=at;p.aiHasTarget=travel;p.aiGo=0;
            AiLog(p,act,place,at);
            if(travel)p.aiPhase=1;else{p.aiPhase=2;p.aiT=p.aiDur;}
            return true;
        }
        void AiPick(Pet p)
        {
            var opts=new List<KeyValuePair<string,int>>();
            void Add(string n,int w){if(n!=p.aiLast)opts.Add(new KeyValuePair<string,int>(n,w));}
            Add("wander",12);Add("rug",6);if(sofa!=null && spotsOf.TryGetValue("sofa",out var sofaSpots) && sofaSpots.Count>0)Add("sofa",20);Add("window",15);Add("harin",15);Add("clip",15);Add("idle",5);
            if(p.white && p.sit!=0)Add("stretch",8);
            if(perches.ContainsKey("tower1"))Add("tower",15);
            foreach(var (n,w) in new[]{("sill",15),("counter",8),("table",7),("fridge",5),("ledge",6),("teatable",5),("bed",6)})if(perches.ContainsKey(n))Add(n,w);
            while(opts.Count>0){
                int total=opts.Sum(o=>o.Value),r=Rng().Next(total),i=0;
                for(;i<opts.Count-1;i++){if(r<opts[i].Value)break;r-=opts[i].Value;}
                if(AiStart(p,opts[i].Key))return;
                opts.RemoveAt(i);
            }
            AiStart(p,"idle");
        }
        bool AiArrived(Pet p)
        {
            if(p.job!=0 || p.hopWait>0 || p.hopPlan.Count>0 || p.chainNext!=null || p.goal.HasValue)return false;
            if(p.somali && IsUp(p.sit))return false;
            switch(p.aiAct){
                case "sofa":return p.onSofa;
                case "sill":case "counter":case "table":case "fridge":case "teatable":case "tower1":case "tower2":case "tower3":case "tower4":case "tower5":return p.perch!=null && p.perch.name==p.aiAct;
                default:return !p.moving && !p.onSofa && p.perch==null;
            }
        }
        void PetAiTick(Pet p,float dt)
        {
            if(!aiOn || p.root==null || !p.graph.IsValid() || p==ctl)return;
            if(p.aiPaused){p.resumeIn-=dt;if(p.resumeIn>0)return;p.aiPaused=false;p.aiPhase=0;Debug.Log("ROOMPLAY_"+p.tag+" ai=resumed");}
            if(p.aiAct==""){p.aiAct="start";p.aiPhase=2;p.aiT=Rnd(.5f,2.5f);} // a short look around before the first choice
            PetMove(p,dt,Vector2.zero,false,0,true);
            switch(p.aiPhase){
                case 0:AiPick(p);break;
                case 1:
                    p.aiGo+=dt;
                    if(AiArrived(p)){p.aiPhase=2;p.aiT=p.aiDur;if(p.aiAct=="harin")PetRandomMeow(p,.6f);} // one meow on arriving next to Harin
                    else if(p.aiGo>45f && p.job==0){Debug.Log("ROOMPLAY_"+p.tag+" ai=giveup "+p.aiAct+" state: perch="+(p.perch!=null?p.perch.name:"none")+" onSofa="+p.onSofa+" sit="+p.sit+" moving="+p.moving+" goal="+(p.goal.HasValue?p.goalKind+"("+F(p.goal.Value.x)+","+F(p.goal.Value.y)+")":"none")+" hopPlan="+string.Join(">",p.hopPlan)+" hopWait="+F(p.hopWait)+" pos=("+F(p.root.position.x)+","+F(p.root.position.y)+","+F(p.root.position.z)+")");p.goal=null;p.chainNext=null;p.hopPlan.Clear();p.hopWait=0;p.aiLast=p.aiAct;p.aiPhase=0;}
                    break;
                case 2:
                    p.aiT-=dt;
                    if(p.aiAct=="harin" && p.job==0 && !p.moving){var d=new Vector2(x,z)-PetXZ(p);if(d.sqrMagnitude>1e-4f)p.faceTo=Mathf.Atan2(d.x,d.y)*Mathf.Rad2Deg;}
                    if(p.aiT<=0){
                        if(p.aiAct=="clip" && p.perch==null && !p.onSofa)PetStand(p);
                        p.aiLast=p.aiAct;p.aiPhase=0;p.aiHasTarget=false;
                    }
                    break;
            }
        }
        // pet sounds (git-ignored files in Assets/Oddity/DevMotion/Cat/Sfx): 3D from each pet, one source for one-shots and one looping for the purr.
        // Idle, SittingIdle, LyingIdle, LoafIdle: sleepy purr after 3 s, a soft meow every 12-25 s; Walk: ear flap on stopping
        const string CatSfxDir="Assets/Oddity/DevMotion/Cat/Sfx/";
        static readonly string[] CatMeows={"meow.ogg","kitten_mew.wav","cat_softmew.wav","cat_mewpurr.wav","cat_mewpurr2.wav","cat_sfx_1.wav","cat_sfx_2.wav","cat_sfx_3.wav","cat_sfx_4.wav","cat_sfx_5.wav","cat_sfx_6.wav","cat_sfx_7.wav","cat_sfx_8.wav"};
        static readonly string[] CatSoftMeows={"cat_softmew.wav","kitten_mew.wav","cat_mewpurr.wav"};
        readonly Dictionary<string,AudioClip> catSfx=new Dictionary<string,AudioClip>();
        int lastMeow=-1;
        // the Korean shorthair recordings, cut by tools/cut_cat_sfx.py into gongu/cut/<source>_<nn>.wav (git-ignored): meows join the meow pool, purr loops the purr choices, hiss pieces have their own use
        const string KrDir="gongu/cut/";
        List<string> meowPool,krPurr,krHiss;
        List<string> KrPieces(string prefix)
        {
            var dir=Path.Combine(Application.dataPath,"../"+CatSfxDir+KrDir);
            if(!Directory.Exists(dir))return new List<string>();
            return Directory.GetFiles(dir,prefix+"*.wav").Select(Path.GetFileName).OrderBy(n=>n,StringComparer.Ordinal).Select(n=>KrDir+n).ToList();
        }
        void KrLoad()
        {
            if(meowPool!=null)return;
            krPurr=KrPieces("kr_purr_");krHiss=KrPieces("kr_hiss_");
            meowPool=CatMeows.ToList();meowPool.AddRange(KrPieces("kr_meow_"));meowPool.AddRange(KrPieces("kr_kitten_"));
            Debug.Log("ROOMPLAY_CATSFX korean pieces meow="+(meowPool.Count-CatMeows.Length)+" purr="+krPurr.Count+" hiss="+krHiss.Count);
        }
        string Pick(List<string> list)=>list.Count==0?null:list[UnityEngine.Random.Range(0,list.Count)];
        AudioClip CatSfx(string file)
        {
            if(!file.Contains("."))file+=".wav";
            if(catSfx.TryGetValue(file,out var c))return c;
            c=AssetDatabase.LoadAssetAtPath<AudioClip>(CatSfxDir+file);
            if(c==null && File.Exists(Path.Combine(Application.dataPath,"../"+CatSfxDir+file))){AssetDatabase.ImportAsset(CatSfxDir+file,ImportAssetOptions.ForceSynchronousImport);c=AssetDatabase.LoadAssetAtPath<AudioClip>(CatSfxDir+file);} // cut after the editor started
            if(c==null)Debug.LogWarning("ROOMPLAY_CATSFX missing "+file+", silent");
            catSfx[file]=c;return c; // a null stays cached so the warning comes once
        }
        void PetSfxSetup(Pet p,GameObject go)
        {
            AudioSource Make(bool loop){var a=go.AddComponent<AudioSource>();a.playOnAwake=false;a.loop=loop;a.spatialBlend=1;a.rolloffMode=AudioRolloffMode.Logarithmic;a.minDistance=6;a.maxDistance=12;return a;} // the camera sits about 5.4 m from the rug: inside 6 m the set volumes hold, the cat still pans left/right
            p.shot=Make(false);p.purr=Make(true);p.purr.volume=0;
            p.purr.clip=CatSfx("cat_purrsleepy_loop.wav");
            p.loopCount=-1;p.nextSoft=UnityEngine.Random.Range(12f,25f);
        }
        void PetShot(Pet p,string file,float vol)
        {
            if(p.shot==null || !catSfxOn)return;
            var c=CatSfx(file);if(c==null)return;
            p.shot.PlayOneShot(c,vol);
            Debug.Log("ROOMPLAY_"+p.tag+"SFX "+(file.Contains(".")?file:file+".wav"));
        }
        void PetRandomMeow(Pet p,float vol)
        {
            KrLoad();
            int k;do{k=UnityEngine.Random.Range(0,meowPool.Count);}while(k==lastMeow && meowPool.Count>1);
            lastMeow=k;PetShot(p,meowPool[k],vol);
        }
        void PetSfxClipStart(Pet p,int i)
        {
            p.idleTime=0;p.nextSoft=UnityEngine.Random.Range(12f,25f);
        }
        void PetSfxTick(Pet p,float dt)
        {
            if(p.root==null || p.shot==null || !p.graph.IsValid())return;
            if(!catSfxOn){if(p.purr.isPlaying)p.purr.Stop();p.purr.volume=0;p.purrOn=false;p.purrTarget=0;return;}
            if(p.Idleish){
                p.idleTime+=dt;
                if(p.idleTime>=(p.somali && (p.index==6 || p.index==9)?2:3) && !p.purrOn && p.purr.clip!=null){
                    p.purrOn=true;p.purrTarget=.25f;
                    if(!p.purr.isPlaying){KrLoad();var choices=new List<string>{"cat_purrsleepy_loop.wav"};choices.AddRange(krPurr);var file=Pick(choices);var pc=CatSfx(file);if(pc!=null)p.purr.clip=pc;p.purr.Play();Debug.Log("ROOMPLAY_"+p.tag+"SFX purr on "+file);}
                    else Debug.Log("ROOMPLAY_"+p.tag+"SFX purr on");}
                p.nextSoft-=dt;
                if(p.nextSoft<=0){p.nextSoft=UnityEngine.Random.Range(12f,25f);PetShot(p,CatSoftMeows[UnityEngine.Random.Range(0,CatSoftMeows.Length)],.5f);}
            }else if(p.purrOn){p.purrOn=false;p.purrTarget=0;Debug.Log("ROOMPLAY_"+p.tag+"SFX purr off");}
            // the purr fades in over 1 s and out over 0.3 s
            float rate=p.purrTarget>p.purr.volume?.25f/1f:.25f/.3f;
            p.purr.volume=Mathf.MoveTowards(p.purr.volume,p.purrTarget,rate*dt);
            if(!p.purrOn && p.purr.isPlaying && p.purr.volume<=0)p.purr.Stop();
        }
        void PetsTick(float dt){foreach(var p in pets){PetAnimTick(p);PetAiTick(p,dt);PetRestTick(p,dt);PetSfxTick(p,dt);SpotLeakTick(p);}PetHissTick();PetOverlapTick();GuestTick();}
        // two cats that are not controlled come within 0.4 m of each other: the one that arrived later (still walking, or stopped last) hisses, at most once per 30 s, then walks off to a new place
        float lastHiss=-100;
        // ---- spots: a cat reserves a spot when it decides to go there and keeps it while it stands there; the reservation ends when it leaves (a new goal, keys, or it is found elsewhere)
        void SetSpot(Pet p,Spot sp)
        {
            if(p.spot==sp)return;
            if(p.spot!=null)Debug.Log("ROOMPLAY_"+p.tag+" release="+p.spot.key);
            p.spot=sp;
            if(sp!=null)Debug.Log("ROOMPLAY_"+p.tag+" reserve="+sp.key);
        }
        bool SpotTaken(Spot sp,Pet except){if(sp.kind=="sofa" && HarinOnSeat(sp.mark))return true;if(sp.kind=="bed" && seatIsBed && (current==BedIndex || current==NapIndex))return true;if(sp.kind=="bed" && sofa!=null){var eg=seats.FirstOrDefault(t=>t.mark!=null && t.mark.name=="SeatBedEdge");if(eg.mark!=null && HarinOnSeat(eg.mark))return true;}foreach(var o in pets)if(o!=except && (o.spot==sp || o.after==sp || o.transit==sp))return true;return false;}
        // a free spot of that kind ("sofa", "sill", ...): the nearest to near, or a random one
        Spot FreeSpot(string kind,Pet p,Vector2 near,bool random=false)
        {
            if(!spotsOf.TryGetValue(kind,out var list))return null;
            var free=list.Where(sp=>!SpotTaken(sp,p)).ToList();if(free.Count==0)return null;
            if(random)return free[Rng().Next(free.Count)];
            return free.OrderBy(sp=>(sp.land-near).sqrMagnitude).First();
        }
        bool AtSpot(Pet p,Spot sp)
        {
            if(p.root==null)return false;
            if(sp.kind=="sofa")return p.onSofa && p.seat==sp.mark;
            return p.perch==sp.perch && (PetXZ(p)-sp.land).magnitude<.5f;
        }
        // the spot of the perch it stands on (nearest to it)
        Spot OccSpot(Pet p)=>p.perch!=null && spotsOf.TryGetValue(p.perch.name,out var l)?l.OrderBy(sp=>(sp.land-PetXZ(p)).sqrMagnitude).First():null;
        float SpotTop(Spot sp)
        {
            if(!float.IsNaN(sp.top))return sp.top;
            Physics.SyncTransforms();
            var ray=new Ray(new Vector3(sp.land.x,3.5f,sp.land.y),Vector3.down);float top=float.NaN;
            foreach(var c in clickHits)if(c!=null && sp.perch.pick(c) && c.Raycast(ray,out var hit,6f) && !(hit.point.y<=top))top=hit.point.y;
            if(float.IsNaN(top)){Debug.LogWarning("ROOMPLAY_SPOT "+sp.key+" no surface, using the perch top");top=PerchTop(sp.perch);}
            return sp.top=top;
        }
        // where a cat coming down lands on the floor: its stand spot, or the nearest point round it that is clear of every other cat's footprint
        Vector2 FloorLanding(Pet p,Vector2 st)
        {
            bool Clear(Vector2 q){foreach(var o in pets)if(o!=p && o.root!=null && (LevelOf(o)=="floor" || o.job!=0) && (FootCentre(o)-q).magnitude<p.radius+o.radius+.05f)return false;return true;}
            if(Clear(st))return st;
            for(float r=.1f;r<=.8f;r+=.1f)for(int a=0;a<360;a+=30){var q=st+new Vector2(Mathf.Cos(a*Mathf.Deg2Rad),Mathf.Sin(a*Mathf.Deg2Rad))*r;if(!Blocked(q) && Clear(q))return q;}
            return st;
        }
        // the sofa's seat cushions, from a scan along the seat: the most common top height is the seat, runs of it at least 0.3 m wide are cushions, the seam between two is a dip of more than 5 cm.
        // A cat lands in the middle of a cushion (one spot per 0.55 m of width), never on the seam
        void BuildSofaSpots()
        {
            var list=new List<Spot>();spotsOf["sofa"]=list;
            var mk=seats.Where(t=>t.mark.root==sofa && !t.mark.name.StartsWith("SeatBed")).ToList();if(sofa==null || mk.Count==0)return;
            var front=mk[0].front;var side=Vector3.Cross(Vector3.up,front).normalized;var mid=mk[mk.Count/2].mark.position;float s0=Vector3.Dot(mid,side);
            Physics.SyncTransforms();
            sofaCols=propHits.Where(c=>c!=null && c.transform.root==sofa).Cast<Collider>().ToList();
            var ss=new List<float>();var tops=new List<float>();
            for(float u=-1.5f;u<=1.5f;u+=.01f){var o=mid+side*u;float top=float.NaN;foreach(var c in sofaCols)if(c.Raycast(new Ray(new Vector3(o.x,2f,o.z),Vector3.down),out var h,3f) && !(h.point.y<=top))top=h.point.y;ss.Add(u);tops.Add(top);}
            float maxTop=tops.Where(t=>!float.IsNaN(t)).DefaultIfEmpty(0).Max();
            var count=new Dictionary<int,int>();foreach(var t in tops)if(!float.IsNaN(t) && t<maxTop*.8f){int k=Mathf.RoundToInt(t*100);count[k]=count.TryGetValue(k,out var n)?n+1:1;}
            if(count.Count==0)return;
            float seat=count.OrderByDescending(kv=>kv.Value).First().Key/100f;
            int start=-1;var runs=new List<(float a,float b)>();
            for(int i=0;i<=tops.Count;i++){
                bool inside=i<tops.Count && !float.IsNaN(tops[i]) && Mathf.Abs(tops[i]-seat)<=.03f;
                if(inside && start<0)start=i;
                if(!inside && start>=0){if(ss[i-1]-ss[start]>=.3f)runs.Add((ss[start],ss[i-1]));start=-1;}
            }
            foreach(var run in runs){
                int n=Mathf.Max(1,Mathf.FloorToInt((run.b-run.a)/.55f));
                for(int k=0;k<n;k++){
                    float c=run.a+(run.b-run.a)*(k+.5f)/n;var at=mid+side*c;
                    var seg=(lo:run.a+(run.b-run.a)*k/n,hi:run.a+(run.b-run.a)*(k+1)/n);
                    var mark=mk.OrderBy(t=>(new Vector2(t.mark.position.x,t.mark.position.z)-new Vector2(at.x,at.z)).sqrMagnitude).First();
                    list.Add(new Spot{key="sofa#"+list.Count,kind="sofa",land=new Vector2(at.x,at.z),mark=mark.mark,fwd=mark.front,axis=side,lo=s0+seg.lo,hi=s0+seg.hi,top=seat});
                }
            }
            Debug.Log("ROOMPLAY_SOFASPOTS seat top="+F(seat)+" cushions="+runs.Count+" spots="+string.Join(" ",list.Select(sp=>sp.key+" x="+F(sp.land.x)+" ("+F(sp.lo)+".."+F(sp.hi)+") mark="+sp.mark.name)));
        }
        // ---- footprints: a circle of p.radius around the body centre. A walking cat is pushed out of another cat's circle (it slides round it); the safety pass moves whichever arrived later if two still overlap
        Vector2 FootOff(Pet p){var o=p.root.rotation*new Vector3(p.cOff.x,0,p.cOff.y);return new Vector2(o.x,o.z);}
        Vector2 FootCentre(Pet p)=>PetXZ(p)+FootOff(p);
        string LevelOf(Pet p)=>p.job!=0?null:p.perch!=null?p.perch.name:p.onSofa?"sofa":"floor"; // null: in the air
        // Harin and the cats are solid to each other (BodyBlock): her step may not end inside a cat standing on the floor, it slides round the cat; seats are shared by nobody
        Vector2 HarinBodyBlock(Vector2 from,Vector2 next)
        {
            foreach(var o in pets){
                if(o.root==null || LevelOf(o)!="floor")continue;
                var slid=BodyBlock.Slide(from,next,FootCentre(o),BodyBlock.Need(o.radius));
                if((slid-next).sqrMagnitude>1e-10f){LogBlock("HARIN",o.tag);next=Collide(from,slid);}
            }
            return next;
        }
        float blockLogAt;
        void LogBlock(string who,string by){if(Time.unscaledTime-blockLogAt<.5f)return;blockLogAt=Time.unscaledTime;Debug.Log("ROOMPLAY_BLOCK who="+who+" by="+by+" harin=("+F(x)+","+F(z)+")");}
        bool CatOnSeat(Transform mark){foreach(var o in pets)if(o.onSofa && o.seat==mark)return true;return false;}
        bool HarinOnSeat(Transform mark)=>mark!=null && chairSat==null && !onFloor && (current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex) && Vector3.Distance(mark.position,seatAt)<.05f;
        Vector2 PetSeparate(Pet p,Vector2 from,Vector2 next)
        {
            if(p.perch!=null || p.onSofa)return next;
            { // Harin is solid too: the cat's foot centre may not enter her circle, it slides round it
                var hc=new Vector2(x,z);var fo=FootOff(p);var slid=BodyBlock.Slide(from+fo,next+fo,hc,BodyBlock.Need(p.radius));
                if((slid-(next+fo)).sqrMagnitude>1e-10f){LogBlock(p.tag,"HARIN");next=Collide(from,slid-fo);}
            }
            for(int pass=0;pass<2;pass++)foreach(var o in pets){
                if(o==p || o.root==null || LevelOf(o)!="floor")continue;
                var cn=next+FootOff(p);var co=FootCentre(o);float need=p.radius+o.radius+.01f;var d=cn-co;
                if(d.magnitude>=need)continue;
                if(d.sqrMagnitude<1e-6f)d=from-co;if(d.sqrMagnitude<1e-6f)d=Vector2.right;
                next=Collide(from,co+d.normalized*need-FootOff(p));
            }
            return next;
        }
        float overlapLogAt,overlapMin=float.MaxValue;string overlapPair="";
        void PetOverlapTick()
        {
            if(pets.Count<2)return;
            for(int i=0;i<pets.Count;i++)for(int j=i+1;j<pets.Count;j++){
                Pet a=pets[i],b=pets[j];if(a.root==null || b.root==null)continue;
                var la=LevelOf(a);var lb=LevelOf(b);if(la==null || la!=lb)continue;
                float gap=(FootCentre(a)-FootCentre(b)).magnitude-a.radius-b.radius;
                if(gap<overlapMin){overlapMin=gap;overlapPair=a.tag+"-"+b.tag+"@"+la;}
                if(gap<0)ResolveOverlap(a,b,gap);
            }
            if(Time.unscaledTime-overlapLogAt>=1f){
                overlapLogAt=Time.unscaledTime;
                if(overlapMin<float.MaxValue)Debug.Log("ROOMPLAY_OVERLAP min_gap="+F(overlapMin)+" pair="+overlapPair+(overlapMin>0?" ok":" OVERLAP"));
                overlapMin=float.MaxValue;
            }
        }
        // the cat that came later (still walking, or stopped last; the controlled cat is never the one) hisses once per 30 s and goes to a free place
        void ResolveOverlap(Pet a,Pet b,float gap)
        {
            Pet victim;
            if(a==ctl)victim=b;else if(b==ctl)victim=a;
            else{float ta=a.moving?float.MaxValue:a.settledAt,tb=b.moving?float.MaxValue:b.settledAt;victim=ta>tb?a:b;}
            if(Time.unscaledTime<victim.relocAt)return;
            victim.relocAt=Time.unscaledTime+2f;
            var other=victim==a?b:a;
            Debug.Log("ROOMPLAY_"+victim.tag+" overlaps "+other.tag+" gap="+F(gap)+" on "+LevelOf(victim)+" -> moves to a free place");
            if(Time.unscaledTime-lastHiss>=30f){KrLoad();var piece=Pick(krHiss);if(piece!=null){lastHiss=Time.unscaledTime;Debug.Log("ROOMPLAY_"+victim.tag+"SFX hiss");PetShot(victim,piece,.7f);}}
            if(aiOn){victim.aiPaused=false;AiStart(victim,"wander");return;}
            var t=AiFloorPoint(victim,()=>new Vector2(Rnd(-1.6f,1.6f),Rnd(-1.6f,1.2f)));
            if(t.HasValue)PetGoTo(victim,"floor",t.Value,null,PetXZ(victim));
        }
        // a reservation that no longer matches where the cat is (a walk that was cancelled) is given back
        void SpotLeakTick(Pet p)
        {
            if(p.root==null || p.job!=0 || p.moving || p.goal.HasValue || p.hopWait>0 || p.hopPlan.Count>0 || p.chainNext!=null)return;
            p.transit=null;p.after=null;
            if(p.spot!=null && !AtSpot(p,p.spot))SetSpot(p,null);
            if(p.spot==null && (p.perch!=null || p.onSofa)){
                var sp=p.onSofa?(p.fitSpot!=null && AtSpot(p,p.fitSpot)?p.fitSpot:null):OccSpot(p);
                if(sp!=null && !SpotTaken(sp,p))SetSpot(p,sp);
            }
        }
        void PetHissTick()
        {
            if(!aiOn || pets.Count<2 || Time.unscaledTime-lastHiss<30f)return;
            for(int i=0;i<pets.Count;i++)for(int j=i+1;j<pets.Count;j++){
                Pet a=pets[i],b=pets[j];
                if(a==ctl || b==ctl || a.aiPaused || b.aiPaused || a.root==null || b.root==null || a.job!=0 || b.job!=0 || a.hopWait>0 || b.hopWait>0 || a.hopPlan.Count>0 || b.hopPlan.Count>0)continue;
                if((PetXZ(a)-PetXZ(b)).magnitude>=.4f || Mathf.Abs(a.root.position.y-b.root.position.y)>.5f)continue;
                float sa=a.moving?float.MaxValue:a.settledAt,sb=b.moving?float.MaxValue:b.settledAt;
                var later=sa>sb?a:sb>sa?b:(UnityEngine.Random.value<.5f?a:b);
                KrLoad();var piece=Pick(krHiss);if(piece==null)return;
                lastHiss=Time.unscaledTime;
                Debug.Log("ROOMPLAY_"+later.tag+"SFX hiss");
                PetShot(later,piece,.7f);
                later.aiPaused=false;AiStart(later,"wander"); // away to a new floor spot
                return;
            }
        }
        // the lowest skinned vertex (tail left out) in the pose it plays now, world y: the Somali's SittingIdle drops its hips below its Idle paws, so the landing height from the standing pose sinks it into the cushion
        float PetLowest(Pet p)
        {
            if(p.smrs==null){
                p.smrs=p.root.GetComponentsInChildren<SkinnedMeshRenderer>();p.tailMask=new bool[p.smrs.Length][];p.bake=new Mesh();
                bool IsTail(Transform tr){for(;tr!=null && tr!=p.root;tr=tr.parent)if(tr.name.IndexOf("tail",StringComparison.OrdinalIgnoreCase)>=0)return true;return false;}
                for(int i=0;i<p.smrs.Length;i++){
                    var src=p.smrs[i].sharedMesh;var bones=p.smrs[i].bones;var perVertex=src.GetBonesPerVertex();var weights=src.GetAllBoneWeights();var mask=new bool[src.vertexCount];int w=0;
                    for(int v=0;v<mask.Length && v<perVertex.Length;v++){
                        int n=perVertex[v];float best=-1;int bone=-1;for(int k=0;k<n;k++){var bw=weights[w+k];if(bw.weight>best){best=bw.weight;bone=bw.boneIndex;}}w+=n;
                        mask[v]=bone>=0 && bone<bones.Length && IsTail(bones[bone]);}
                    p.tailMask[i]=mask;}
                // paw vertices: mainly weighted to a bone named paw, foot or toe (the Somali's Paw / Foot, the white cat's Paw / Toes)
                p.pawMask=new bool[p.smrs.Length][];int pawCount=0;
                for(int i=0;i<p.smrs.Length;i++){
                    var src=p.smrs[i].sharedMesh;var bones=p.smrs[i].bones;var perVertex=src.GetBonesPerVertex();var weights=src.GetAllBoneWeights();var mask=new bool[src.vertexCount];int w=0;
                    for(int v=0;v<mask.Length && v<perVertex.Length;v++){
                        int n=perVertex[v];float best=-1;int bone=-1;for(int k=0;k<n;k++){var bw=weights[w+k];if(bw.weight>best){best=bw.weight;bone=bw.boneIndex;}}w+=n;
                        if(bone>=0 && bone<bones.Length && !p.tailMask[i][v]){var nm=bones[bone].name.ToLowerInvariant();mask[v]=nm.Contains("paw") || nm.Contains("foot") || nm.Contains("toe");if(mask[v])pawCount++;}}
                    p.pawMask[i]=mask;}
                p.pawFallback=pawCount<20;Debug.Log("ROOMPLAY_"+p.tag+" paw vertices="+pawCount+(p.pawFallback?" (too few: the four lowest clusters are used)":""));
            }
            var verts=new List<Vector3>();p.lowPts.Clear();var pawPts=new List<Vector3>();var allPts=new List<Vector3>();var bodyPts=new List<Vector3>();
            float f0=float.MaxValue,f1=float.MinValue,s0=float.MaxValue,s1=float.MinValue;var rp=p.root.position;bool fit=p.onSofa && p.fitFront!=Vector3.zero;
            for(int i=0;i<p.smrs.Length;i++){
                var smr=p.smrs[i];smr.BakeMesh(p.bake,true);p.bake.GetVertices(verts);var m=smr.transform.localToWorldMatrix;
                for(int v=0;v<verts.Count;v++){
                    if(p.tailMask[i][v])continue;var w=m.MultiplyPoint3x4(verts[v]);
                    bodyPts.Add(w);
                    if(p.pawMask[i][v])pawPts.Add(w);else if(p.pawFallback && w.y<rp.y+.25f)allPts.Add(w);
                    if(fit){var d=w-rp;float a=Vector3.Dot(d,p.fitFront),b=Vector3.Dot(d,p.fitSide);if(a<f0)f0=a;if(a>f1)f1=a;if(b<s0)s0=b;if(b>s1)s1=b;}
                }
            }
            // the body's lowest point is the 1 % quantile of the vertex heights, not the single lowest vertex: the retargeted Somali has a patch of four vertices skinned 100 % to Hip.R that a lying pose swings
            // 4-5 cm under the belly (a spike), and snapping that spike to the surface left the whole cat hovering. Paws are still grounded by their own lowest vertex (conPts below)
            float lo=float.MaxValue;
            if(bodyPts.Count>0){var ys=new float[bodyPts.Count];for(int k=0;k<ys.Length;k++)ys[k]=bodyPts[k].y;Array.Sort(ys);lo=ys[Mathf.Min(ys.Length-1,(int)(ys.Length*.01f))];}
            foreach(var w in bodyPts)if(w.y<lo+.03f && w.y>=lo-.005f)p.lowPts.Add(w);
            if(fit){p.ext[0]=f0;p.ext[1]=f1;p.ext[2]=s0;p.ext[3]=s1;}
            // the points that must not sink: the lowest vertex of each paw (up to four clusters of 4.5 cm), a few of its neighbours within 1 cm, and the lowest body vertices (a lying belly)
            p.conPts.Clear();
            var src2=p.pawFallback?allPts:pawPts;src2.Sort((u,w)=>u.y.CompareTo(w.y));
            var seeds=new List<Vector3>();var seedN=new List<int>();
            foreach(var w in src2){
                int hit=-1;for(int k=0;k<seeds.Count;k++)if(new Vector2(w.x-seeds[k].x,w.z-seeds[k].z).sqrMagnitude<.045f*.045f){hit=k;break;}
                if(hit>=0){if(seedN[hit]<4 && w.y<seeds[hit].y+.01f){seedN[hit]++;p.conPts.Add(w);}}
                else if(seeds.Count<4){seeds.Add(w);seedN.Add(1);p.conPts.Add(w);}
            }
            var cells=new HashSet<Vector2Int>();
            foreach(var w in p.lowPts){var c=new Vector2Int(Mathf.RoundToInt(w.x/.03f),Mathf.RoundToInt(w.z/.03f));if(cells.Count<8 && cells.Add(c))p.conPts.Add(w);}
            p.pawSeeds=seeds.Count;
            return lo;
        }
        // the surface under a point of the cat: on the sofa the cushion top (rays down on the sofa colliders, within a band round the height it landed at), on a perch the perch top, else the floor.
        // A ray that falls into a narrow gap (the seam between cushions) is not trusted: if the best of 8 neighbours within 4 cm is more than 2 cm higher, that neighbour's top is used
        float SurfaceUnder(Pet p,Vector3 w,out float raw)
        {
            raw=float.NaN;
            List<Collider> cols;float centre;
            if(p.onSofa && p.seat!=null && sofaCols!=null){cols=sofaCols;centre=p.surfBase;}
            else if(p.perch!=null && perchColsOf.TryGetValue(p.perch,out cols)){centre=p.surf;}
            else return p.surf;
            float lo=centre-.12f,hi=centre+.08f;
            float Hit(float x,float z){var ray=new Ray(new Vector3(x,hi+.15f,z),Vector3.down);float best=float.NaN;foreach(var c in cols)if(c!=null && c.Raycast(ray,out var h,.6f) && h.point.y>=lo && h.point.y<=hi && !(h.point.y<=best))best=h.point.y;return best;}
            float own=Hit(w.x,w.z);raw=own;float nb=float.NaN;
            for(int a=0;a<8;a++){float ang=a*45f*Mathf.Deg2Rad;float hn=Hit(w.x+Mathf.Cos(ang)*.04f,w.z+Mathf.Sin(ang)*.04f);if(!float.IsNaN(hn) && !(hn<=nb))nb=hn;}
            if(float.IsNaN(own))return float.IsNaN(nb)?centre:nb;
            return !float.IsNaN(nb) && nb-own>.02f?nb:own;
        }
        const float SofaFitMargin=.03f;
        // the whole footprint on the seat: from the rump, and from each end of the body along the seat, a horizontal ray (at body height above the cushion) to the backrest and the arms; the root is eased until each has 3 cm
        void SofaFit(Pet p,float dt)
        {
            if(p.job!=0 || p.moving || p.fitFront==Vector3.zero)return;
            var rp=p.root.position;float h=p.surf+.12f;var shift=Vector3.zero;
            float Clear(Vector3 dir,float extent){
                var o=new Vector3(rp.x+dir.x*extent,h,rp.z+dir.z*extent);float best=1f;
                foreach(var c in propHits)if(c!=null && c.transform.root==sofa && c.Raycast(new Ray(o,dir),out var hit,1f) && hit.distance<best)best=hit.distance;
                return best;}
            float back=Clear(-p.fitFront,-p.ext[0]);if(back<SofaFitMargin)shift+=p.fitFront*(SofaFitMargin-back);
            float pos=Clear(p.fitSide,p.ext[3]);if(pos<SofaFitMargin)shift-=p.fitSide*(SofaFitMargin-pos);
            float neg=Clear(-p.fitSide,-p.ext[2]);if(neg<SofaFitMargin)shift+=p.fitSide*(SofaFitMargin-neg);
            var cs=p.fitSpot; // inside the cushion it landed on: the footprint along the seat stays 2 cm off the seam and the arm
            if(cs!=null){
                float sgn=Vector3.Dot(p.fitSide,cs.axis)>=0?1:-1;float c0=Vector3.Dot(rp,p.fitSide);float e0=c0+p.ext[2],e1=c0+p.ext[3];
                float a=sgn>0?cs.lo:-cs.hi,b=sgn>0?cs.hi:-cs.lo;const float cm=.02f;
                if(e1-e0>b-a-2*cm)shift+=p.fitSide*((a+b)/2-(e0+e1)/2);
                else{if(e0<a+cm)shift+=p.fitSide*(a+cm-e0);if(e1>b-cm)shift-=p.fitSide*(e1-(b-cm));}
            }
            if(shift.sqrMagnitude<1e-8f)return;
            shift=Vector3.ClampMagnitude(shift,.15f);
            p.root.position=rp+shift*(1-Mathf.Exp(-dt*8));
            if(Time.unscaledTime-p.lastFit>1f){p.lastFit=Time.unscaledTime;Debug.Log("ROOMPLAY_"+p.tag+" sofafit back="+F(back)+" side+="+F(pos)+" side-="+F(neg)+" shift=("+F(shift.x)+","+F(shift.z)+")");}
        }
        // while a pet rests (not walking, not in a hop) every paw (and the lowest body vertices) is eased onto its own surface, 0.1 s like Harin's sole lift: the body is lifted until no point is below
        // its surface, so the most sunk paw decides. Runs every frame on the sofa, a perch and the floor, in every pose
        void PetRestTick(Pet p,float dt)
        {
            if(p.root==null || !p.graph.IsValid() || p.job!=0 || p.moving || p.hopWait>0)return;
            float lo=PetLowest(p);
            bool soft=p.onSofa && p.seat!=null;
            float gap=float.MaxValue,top=float.MinValue;var sb=Time.unscaledTime-p.pawLogAt>=1f && PetEnv("ODDITY_PAWLOG")=="1"?new System.Text.StringBuilder():null;
            for(int i=0;i<p.conPts.Count;i++){
                var w=p.conPts[i];float s=SurfaceUnder(p,w,out var raw);top=Mathf.Max(top,s);gap=Mathf.Min(gap,w.y-s);
                if(sb!=null && i<p.pawSeeds)sb.Append(" paw").Append(i).Append("(y=").Append(F(w.y)).Append(" surface=").Append(F(s)).Append(" raw=").Append(float.IsNaN(raw)?"none":F(raw)).Append(" gap=").Append(F((w.y-s)*1000)).Append("mm)");
            }
            if(p.conPts.Count==0){gap=lo-p.surf;top=p.surf;}
            if(soft || p.perch!=null)p.surf=soft?top:p.surf;
            if(soft){SofaFit(p,dt);}
            if(sb!=null){p.pawLogAt=Time.unscaledTime;Debug.Log("ROOMPLAY_"+p.tag+" PAWS clip="+p.clipNames[p.index]+" spot="+(p.spot!=null?p.spot.key:"-")+" lowest-vertex="+F(lo)+" worst-gap="+F(gap*1000)+"mm"+sb);}
            if(Mathf.Abs(gap)<.0005f)return;
            if(Mathf.Abs(gap)<.003f){var key=p.surf.ToString("0.000")+"/"+p.index;if(key!=p.gapKey){p.gapKey=key;Debug.Log("ROOMPLAY_"+p.tag+" rest surface="+F(p.surf)+" clip="+p.clipNames[p.index]+" lowest-point gap="+F(gap*1000)+" mm rootY="+F(p.root.position.y));}}
            var pos=p.root.position;pos.y-=Mathf.Clamp(gap,-.25f,.25f)*(1-Mathf.Exp(-dt*30));p.root.position=pos;
        }
        // M: the controlled pet's next clip; the Somali cycles Idle -> Walk -> Sit -> Stand -> Lie -> Loaf
        void NextCat()
        {
            var p=ctl??pets.FirstOrDefault();if(p==null || !p.graph.IsValid() || p.job!=0)return;
            int start=CurAction(p);if(start<0)start=p.mStep;
            for(int k=1;k<=ActionNames.Length;k++){int a=(start+k)%ActionNames.Length;if(CanDo(p,a)){p.mStep=a;CatAction(p,a);return;}}
        }
        // the computer lives on the dining table: an open laptop in front of the chair on the -x side, screen tilted back and facing that seat
        static void Laptop(Transform table,float yaw=0) // yaw turns the laptop about the table's middle: the screen faces -x at 0, -z at -90 (the apartment's table has its chairs along z)
        {
            var rs=table.GetComponentsInChildren<Renderer>();if(rs.Length==0)return;var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
            var lap=new GameObject("Laptop").transform;lap.SetParent(table,true);var turn=Quaternion.Euler(0,yaw,0);lap.position=new Vector3(b.center.x,b.max.y,b.center.z)+turn*new Vector3(-.12f,0,0);lap.rotation=turn;
            Color body=new Color(.72f,.73f,.76f),screen=new Color(.10f,.12f,.16f);
            Part(PrimitiveType.Cube,lap,new Vector3(0,.009f,0),new Vector3(.22f,.018f,.32f),body);                                   // base, keys toward -x
            Part(PrimitiveType.Cube,lap,new Vector3(-.01f,.0185f,0),new Vector3(.12f,.002f,.28f),new Color(.25f,.26f,.28f));          // keyboard
            var lid=new GameObject("Lid").transform;lid.SetParent(lap,false);lid.localPosition=new Vector3(.11f,.018f,0);lid.localRotation=Quaternion.Euler(0,0,-15); // hinge at the +x edge, leaning back 15 degrees away from the seat
            Part(PrimitiveType.Cube,lid,new Vector3(0,.11f,0),new Vector3(.012f,.22f,.32f),body);
            Part(PrimitiveType.Cube,lid,new Vector3(-.007f,.115f,0),new Vector3(.002f,.18f,.28f),screen);                            // screen faces the -x chair
        }
        // Shift+C and Shift+V step through these; the floor ones (lying, crouching) play where she stands instead of on a seat
        static readonly string[] SitClips={"KaSitPutHandBetweensLoop","KaSitSwingingLegsLoop","KaSitCrossLegsLoop","MxSitCrossedRecline","MxSitCrossedArmKnee","MxSitHoldKnee"};
        static readonly string[] CurlClips={"KaSitWithBothLegsUpLoop","MxLayStomachElbows","MxCrouchPose","MxCrouchIdle"};
        static readonly string[] NapClips={"KaSleepTurnToTheSideLoop","KaSleepCurlUpSidewaysLoop","KaSleepBendOneKneeLoop"}; // K: side, face to the room; Shift+K: curled up, face in the armrest
        static bool FloorClip(string clip)=>clip.StartsWith("MxLay")||clip.StartsWith("MxCrouch");
        bool onFloor;
        void NextSit(int index)
        {
            var list=index==SitIndex?SitClips:index==NapIndex?NapClips:CurlClips;var next=list[(Array.IndexOf(list,Keys[index].clip)+1)%list.Length];Keys[index].clip=next;
            try{LoadModel(modelPath);Sit(index,index==NapIndex);}catch(Exception e){Notice("앉기 바꾸기 실패: "+e.Message);}
        }
        // mouse: a click on the sofa walks her to the clicked seat and sits (C); on the table or a chair she goes to the chair nearer the click and sits;
        // on the floor she walks there. any walk key cancels. scripts: clicksofa, clicktable, clickfloor (a fixed point in the open)
        Transform tableRoot;readonly List<Collider> clickHits=new List<Collider>();
        Vector2? clickGoal;Transform clickSeat;float clickStuck;bool clickWindow;
        List<Vector2> hPath;int hPathI;Vector2? hPathGoal;bool hReplanned; // her planned walk to clickGoal (FloorPath)
        // a click on the window walks her to the floor in front of it (between the bedroom door and the sofa) and she looks out
        static readonly Vector2 WindowSpot=new Vector2(-1.45f,.55f);
        static bool InWindow(Vector3 p)=>p.z>WinZ.x-.06f && p.z<WinZ.y+.06f && p.y>WinY.x-.06f && p.y<WinY.y+.06f;
        Vector2 WinSpotNow=>apt?Apartment.WindowSpot:WindowSpot;
        Vector2 WinLookNow=>apt?Apartment.WindowLook:new Vector2(-RoomHalf,WindowSpot.y);
        // the click ray hits the window: the old room's opening in the left wall, or the apartment's living-room window in the partition's inner face (z 1.30)
        bool WindowRay(Ray ray)
        {
            if(apt){
                if(Apartment.Peep && ray.origin.z<Apartment.WinPlaneZ)return false; // the studio's camera is outside: the glass lets the click through to the room
                if(!new Plane(Vector3.forward,new Vector3(0,0,Apartment.WinPlaneZ)).Raycast(ray,out var d))return false;var q=ray.GetPoint(d);
                return q.x>Apartment.WinX0-.06f && q.x<Apartment.WinX1+.06f && q.y>Apartment.WinY0-.06f && q.y<Apartment.WinY1+.06f;}
            return new Plane(Vector3.right,new Vector3(-RoomHalf,0,0)).Raycast(ray,out var wd) && InWindow(ray.GetPoint(wd));
        }
        const float ClickArrive=.05f, ClickGiveUp=.6f;
        // a real mouse press counts only inside the Game view, 4 px clear of its edges: a press on the window border, the tab bar or another editor window must not walk her there
        static bool ClickInGame(Vector2 p)
        {
            const float Edge=4f;
            if(p.x<Edge || p.y<Edge || p.x>Screen.width-Edge || p.y>Screen.height-Edge)return false;
            var over=EditorWindow.mouseOverWindow;
            return over!=null && over.GetType().Name=="GameView";
        }
        void ClickAt(Ray ray)
        {
            if(busy)return;
            if(ctl!=null){CatClickAt(ray);return;} // Harin ignores clicks while a pet is controlled
            if(chairJob!=null)return;
            if(ramenOn || mealOn || laundryOn || laundryPend || mirrorOn || mirrorPend || catOn)EndNewActs("click");
            float near=float.MaxValue;Collider got=null;Vector3 at=Vector3.zero;
            foreach(var c in clickHits)if(c!=null && !perchOf.ContainsKey(c) && c.Raycast(ray,out var hit,50f) && hit.distance<near){near=hit.distance;got=c;at=hit.point;} // the high perches are for the cats' clicks only
            if(got!=null){
                var root=got.transform.root;
                var pool=root==sofa?seats.Where(t=>t.mark.root==sofa && t.mark.name!="SeatBed"):seats.Where(t=>t.mark.root!=sofa); // the table and both chairs pick a chair
                if(root!=sofa && root!=tableRoot)pool=seats.Where(t=>t.mark.root==root);
                var list=pool.ToList();if(list.Count==0)return;
                var seat=list.OrderBy(t=>new Vector2(t.mark.position.x-at.x,t.mark.position.z-at.z).sqrMagnitude).First();
                clickWindow=false;clickSeat=seat.mark;var stand=seat.mark.position+seat.front*seat.off;clickGoal=new Vector2(Mathf.Clamp(stand.x,LimX0,LimX1),Mathf.Clamp(stand.z,ZNear,ZFar));
                Notice("클릭 "+root.name+"/"+seat.mark.name);
            }else if(WindowRay(ray)){
                clickSeat=null;clickWindow=true;clickGoal=WinSpotNow;Notice("클릭 창문");
            }else{
                if(!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out var d))return;var p=ray.GetPoint(d);
                clickWindow=false;clickSeat=null;clickGoal=new Vector2(Mathf.Clamp(p.x,LimX0,LimX1),Mathf.Clamp(p.z,ZNear,ZFar));Notice("클릭 바닥 "+F(p.x)+","+F(p.z));
            }
            clickStuck=0;hPathGoal=null;hReplanned=false;
        }
        // her walk to clickGoal around the furniture; a goal inside a blocker snaps, an unreachable one is dropped with a notice
        void HarinPlan(bool again)
        {
            var from=new Vector2(x,z);var goal=clickGoal.Value;
            var path=Plan().Find(from,goal,out bool snapped);
            if(path==null){Debug.Log(PathLog("harin",from,goal,null)+" unreachable");clickGoal=null;clickSeat=null;clickWindow=false;hPath=null;hPathGoal=null;Notice("갈 수 없는 곳");return;}
            if(snapped){clickGoal=path[path.Count-1];Debug.Log(PathLog("harin",from,goal,path)+" snapped to=("+F(clickGoal.Value.x)+","+F(clickGoal.Value.y)+")");}
            hPath=path;hPathI=path.Count>1?1:0;hPathGoal=clickGoal;
            Debug.Log(PathLog("harin",from,clickGoal.Value,path)+(again?" replan":""));
        }
        // a world direction to walk this frame toward the click goal, or zero; arriving at a seat sits on it
        Vector2 ClickStep(float dt)
        {
            if(!clickGoal.HasValue)return Vector2.zero;
            if(chairSat!=null){if(chairSat.mark==clickSeat){clickGoal=null;clickSeat=null;}else StartChairStand(-1);return Vector2.zero;} // off the dining chair first
            if(clickSeat!=null && clickSeat.root!=sofa && (!apt || (new Vector2(x,z)-new Vector2(clickSeat.position.x,clickSeat.position.z)).magnitude<1.2f)){var seat=clickSeat;clickGoal=null;clickSeat=null;direction=0;Sit(SitIndex,false,seat);return Vector2.zero;} // the chair steps walk her there themselves
            var p=new Vector2(x,z);
            if(!hPathGoal.HasValue || hPathGoal.Value!=clickGoal.Value){HarinPlan(false);if(!clickGoal.HasValue)return Vector2.zero;}
            var tgt=clickGoal.Value;bool lastLeg=true;
            if(hPath!=null && hPath.Count>0){
                hPathI=Mathf.Clamp(hPathI,0,hPath.Count-1);
                while(hPathI<hPath.Count-1 && (hPath[hPathI]-p).sqrMagnitude<.0025f)hPathI++;
                lastLeg=hPathI==hPath.Count-1;tgt=lastLeg?clickGoal.Value:hPath[hPathI];}
            var d=tgt-p;
            var next=HarinBodyBlock(p,Collide(p,p+d.normalized*Mathf.Min(speed*dt,d.magnitude)));
            if((next-p).sqrMagnitude<1e-10f)clickStuck+=dt;else clickStuck=0;
            bool canReplan=hPath!=null && !hReplanned;
            if(canReplan && clickStuck>1f){hReplanned=true;clickStuck=0;HarinPlan(true);return Vector2.zero;} // blocked for 1 s: one replan
            if((lastLeg && d.magnitude<ClickArrive) || (clickStuck>ClickGiveUp && !canReplan)){ // (a path that has not been replanned only gives up after the replan, above)
                var seat=clickSeat;bool window=clickWindow;clickGoal=null;clickSeat=null;clickWindow=false;direction=0;
                if(seat!=null)Sit(SitIndex,false,seat);
                else if(window){float wy=clickYaw??(apt?Apartment.WindowYaw:-90);clickYaw=null;Select(9);Turn(wy);heading=wy;Notice("창밖 바라보기");} // faces out of the window (the left wall; the apartment's south window)
                else{Select(9);heading=targetYaw;}
                return Vector2.zero;
            }
            return d.normalized;
        }
        void ScriptClick(string key)
        {
            if(key=="clickbed" && ctl!=null){if(!PetGoToPerch(ctl,"bed"))Notice("침대 자리가 없음");return;} // script: the controlled cat goes up on the bed
            if(apt && ((key=="clicktable" && tableRoot==null) || (key=="clickfridge" && !Apartment.HasFridge) || (key=="clickteatable" && !Apartment.HasTea) || (key=="clickledge" && !Apartment.HasLedge))){Notice("이 집에는 그런 가구가 없음");return;}
            if(apt && Apartment.NoSofa && key.StartsWith("clicksofa")){Notice("이 집에는 소파가 없음 - 침대 가장자리로");key="clickbededge";}
            if(key.StartsWith("clicktower")){ScriptClickTower(key);return;}
            Vector3 target=key=="clickbededge"?Apartment.BedEdgeSeat+Vector3.up*.05f:key=="clickwindow"?(apt?new Vector3((Apartment.WinX0+Apartment.WinX1)/2,(Apartment.WinY0+Apartment.WinY1)/2,Apartment.WinPlaneZ):new Vector3(-RoomHalf,(WinY.x+WinY.y)/2,(WinZ.x+WinZ.y)/2)):key=="clicksofa"&&sofa!=null?SeatOf("SeatL",sofa):key=="clicksofar"&&sofa!=null?SeatOf("SeatR",sofa):key=="clicksofac"&&sofa!=null?SeatOf("SeatC",sofa):key=="clicktable"&&tableRoot!=null?tableRoot.position+Vector3.up*.7f:key=="clickcounter"?(apt?new Vector3(Apartment.CounterLand[0].x,.86f,Apartment.CounterLand[0].y):new Vector3(1.17f,.86f,1.5f)):key=="clickfridge"?(apt?new Vector3(4.03f,1.2f,8.5f):new Vector3(1.7f,1.2f,1.5f)):key=="clickteatable"?new Vector3((Apartment.TeaX0+Apartment.TeaX1)/2,.4f,(Apartment.TeaZ0+Apartment.TeaZ1)/2):key=="clicksill"?(apt?new Vector3((Apartment.SillLand[0].x+Apartment.SillLand[Apartment.SillLand.Length-1].x)/2,Apartment.WinY0,Apartment.SillLand[0].y):new Vector3(-1.85f,.85f,.2f)):key=="clickledge"?new Vector3(.8f,.9f,.15f):key=="clickbed"?new Vector3(.5f,.43f,1.4f):ScriptFloorPoint();
            ClickAt(new Ray(cameraView.transform.position,target-cameraView.transform.position));
        }
        // clickfloor's fixed point (.9, .9); ODDITY_CLICKXZ="x,z" picks another one for a path test
        static int scriptPoint;
        static Vector3 ScriptFloorPoint()
        {
            var v=Environment.GetEnvironmentVariable("ODDITY_CLICKXZ"); // "x,z" or several "x,z;x,z;..." that successive clickfloor steps take in turn
            if(!string.IsNullOrWhiteSpace(v)){
                var pts=v.Split(';');var q=pts[scriptPoint++%pts.Length].Split(',');
                if(q.Length==2 && float.TryParse(q[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var cx) && float.TryParse(q[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var cz))return new Vector3(cx,0,cz);}
            return Apartment.Peep?new Vector3(1.7f,0,2.9f):new Vector3(.9f,0,.9f);
        }
        Vector3 SeatOf(string name,Transform root){var t=seats.FirstOrDefault(s=>s.mark.root==root && s.mark.name==name).mark;return t!=null?t.position:root.position;}
        // furniture footprints on the floor (world boxes of each prop's meshes), grown by her body radius; walking into one slides along its side
        readonly List<Rect> blockers=new List<Rect>();
        const float BodyRadius=.2f;
        void Blockers()
        {
            blockers.Clear();floorPlan=null;
            var props=new List<Transform>();if(sofa!=null)props.Add(sofa);
            props.AddRange(FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.parent==null && (t.name.StartsWith("table-") || t.name.StartsWith("Chair"))));
            foreach(var t in props.Distinct()){
                var rs=t.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;
                var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                blockers.Add(Rect.MinMaxRect(b.min.x-BodyRadius,b.min.z-BodyRadius,b.max.x+BodyRadius,b.max.z+BodyRadius));
            }
            foreach(var f in furniture){float rx=one && Mathf.Approximately(f.xMin,2.45f) && Mathf.Approximately(f.xMax,3f)?OneRoom.CounterBodyRadius:BodyRadius;blockers.Add(Rect.MinMaxRect(f.xMin-rx,f.yMin-BodyRadius,f.xMax+BodyRadius,f.yMax+BodyRadius));} // the kitchen counter lets her stand 0.12 m off its front so her hand reaches the pots
            if(apt)foreach(var w in Apartment.WallBlockers())blockers.Add(Rect.MinMaxRect(w.xMin-BodyRadius,w.yMin-BodyRadius,w.xMax+BodyRadius,w.yMax+BodyRadius)); // walls with their door gaps
            if(apt)foreach(var w in Apartment.LeafBlockers())blockers.Add(Rect.MinMaxRect(w.xMin-BodyRadius,w.yMin-BodyRadius,w.xMax+BodyRadius,w.yMax+BodyRadius)); // open door leaves and the stacked sliding panels
            Debug.Log("ROOMPLAY_BLOCKERS "+string.Join(" ",blockers.Select(r=>"("+F(r.xMin)+".."+F(r.xMax)+","+F(r.yMin)+".."+F(r.yMax)+")")));
        }
        bool Blocked(Vector2 p)=>blockers.Any(r=>r.Contains(p));
        float Depth(Vector2 p)=>blockers.Where(r=>r.Contains(p)).Select(r=>Mathf.Min(Mathf.Min(p.x-r.xMin,r.xMax-p.x),Mathf.Min(p.y-r.yMin,r.yMax-p.y))).DefaultIfEmpty(0).Max();
        // a step that would enter a footprint keeps only the part along its side; one that starts inside (getting up from a seat) is let out
        Vector2 Collide(Vector2 from,Vector2 to)
        {
            if(blockers.Count==0 || !Blocked(to))return to;
            if(Blocked(from))return Depth(to)<Depth(from)?to:from; // inside already (getting up, or dropped in): only steps that lead out
            var alongX=new Vector2(to.x,from.y);if(!Blocked(alongX))return alongX;
            var alongZ=new Vector2(from.x,to.y);if(!Blocked(alongZ))return alongZ;
            return from;
        }
        void Sit(int index)=>Sit(index,false,null);
        void Sit(int index,bool sofaOnly)=>Sit(index,sofaOnly,null);
        // sofaOnly: the nap curls up on the sunny sofa seat whatever seat is nearer
        void Sit(int index,bool sofaOnly,Transform only)
        {
            if(clips[index]!=null && FloorClip(Keys[index].clip)){onFloor=true;Select(index);Notice("바닥 "+Keys[index].clip);return;}
            onFloor=false;faced=true; // her first-frame facing is not to be applied over a seat
            if(seats.Count==0 || clips[index]==null){Notice("앉을 자리가 없거나 앉기 클립이 없음");return;}
            var pool=sofaOnly?seats.Where(t=>t.mark.root==sofa && t.mark.name==(apt && Apartment.HasBed?"SeatBed":"SeatC")).ToList():seats.Where(t=>index==CurlIndex && apt && Apartment.HasBed?t.mark.name!="SeatBedEdge":t.mark.name!="SeatBed").ToList();if(pool.Count==0)pool=seats; // the nap: the sofa's middle seat, in the studio the bed
            if(only!=null){var one=seats.Where(t=>t.mark==only).ToList();if(one.Count>0)pool=one;}
            pool=pool.Where(t=>!CatOnSeat(t.mark)).ToList();if(pool.Count==0){Notice("고양이가 그 자리에 있어요");return;} // a seat a cat lies on is not hers
            var seat=pool.OrderBy(t=>new Vector2(t.mark.position.x-x,t.mark.position.z-z).sqrMagnitude).First();
            if(chairJob!=null)return;
            if(chairSat!=null){if(seat.mark==chairSat.mark){SelectRaw(index);Notice("앉기 "+chairSat.chair.name+" "+Keys[index].clip);}else StartChairStand(-1);return;} // another seat: get up first
            if(seat.mark.root!=sofa){StartChairSit(seat.mark,seat.front,index);return;}
            seatAt=seat.mark.position;seatIsBed=seat.mark.name=="SeatBed";
            seatFront=seat.front;seatEdge=seat.mark.name=="SeatBedEdge"?.17f:seat.mark.root==sofa?.36f:.30f; // seat mark to the cushion's front edge: sofa-1 0.36 m, chair-1 0.21 + 0.09 m
            var stand=seatAt+seat.front*seat.off;x=Mathf.Clamp(stand.x,LimX0,LimX1);z=Mathf.Clamp(stand.z,ZNear,ZFar);
            yaw=heading=targetYaw=turnStart=seatIsBed && index==NapIndex?LieYaw(NapIndex,0)+NapTurn:Mathf.Atan2(seat.front.x,seat.front.z)*Mathf.Rad2Deg+(index==NapIndex?NapTurn:0);turnTime=1;
            Select(index);Notice("앉기 "+seat.mark.root.name+" "+Keys[index].clip);Debug.Log("ROOMPLAY_SITYAW yaw="+F(yaw)+" heading="+F(heading)+" bed="+seatIsBed+" lie="+F(lieAxisYaw[NapIndex])+" napTurn="+F(NapTurn));
        }
        // a seated foot that has gone back into the seat (a folded leg) or under the floor is brought out in front of the seat edge with leg IK, knee forward
        Vector3 seatFront;float seatEdge;
        void FeetClear()
        {
            float w=weights[SitIndex]+weights[NapIndex]+(onFloor?0:weights[CurlIndex]);if(w<=0 || sofa==null)return;
            if(weights[NapIndex]>.5f){if(!seatIsBed)NapFeet(weights[NapIndex]);return;} // the sofa's armrest pull-in would drag a bed sleeper's legs across each other
            if(!onFloor && weights[CurlIndex]>.5f){if(!seatIsBed)CurlFeet(weights[CurlIndex]);return;} // on the bed CurlBedFinish puts the feet on the quilt
            for(int side=0;side<2;side++){
                var upper=anim.GetBoneTransform(side==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
                var lower=anim.GetBoneTransform(side==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
                var foot=anim.GetBoneTransform(side==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                if(upper==null || lower==null || foot==null)continue;
                var f=foot.position;float ahead=Vector3.Dot(f-seatAt,seatFront);
                bool inSeat=f.y<seatAt.y-.04f && ahead<seatEdge+.05f;bool sunk=f.y<.07f;
                if(!inSeat && !sunk)continue;
                var target=f;if(inSeat)target+=seatFront*(seatEdge+.07f-ahead);target.y=Mathf.Max(target.y,.08f);
                TwoBone(upper,lower,foot,target,lower.position+seatFront*.4f,w);
            }
            SeatToes(weights[SitIndex]);
        }
        // the sit clips point the toes down; with shoes on, the toe cap went into the floor while the ankle stayed clear.
        // the toe joint is held straight (a rigid shoe), then a seated foot whose toe bone is lower than ToeClear tips up at the ankle until it is not (the shoe's front reaches about 4 cm past the toe bone)
        const float ToeClear=.05f;float toeLow=float.PositiveInfinity;
        void SeatToes(float w)
        {
            if(w<=0 || toes[0]==null || toes[1]==null)return;
            for(int i=0;i<2;i++){
                if(feet[i]==null)continue;
                toes[i].localRotation=Quaternion.Slerp(toes[i].localRotation,restToe[i],w); // a shoe does not bend at the toe joint: the clip's curled toes are straightened first
                float len=(toes[i].position-feet[i].position).magnitude;if(len<1e-4f)continue;
                float lack=(ToeClear-toes[i].position.y)*w;
                for(int k=0;k<3 && lack>.002f;k++){TurnPitch(i,-Mathf.Asin(Mathf.Clamp01(lack/len))*Mathf.Rad2Deg*w);lack=ToeClear-toes[i].position.y;} // the arc is not a straight lift, three passes close the gap
                if(w>.9f)toeLow=Mathf.Min(toeLow,toes[i].position.y);
            }
        }
        // the knee hug (V) is a floor pose: on a seat her feet come up onto the cushion, a little behind its front edge, instead of hanging in front of it
        readonly List<MeshCollider> propHits=new List<MeshCollider>();
        const float AnkleUp=.075f, EdgeIn=.07f; // foot bone above the sole; how far behind the cushion's front edge the heel stays
        float PropTop(Vector3 p)
        {
            var ray=new Ray(new Vector3(p.x,seatAt.y+.3f,p.z),Vector3.down);float top=float.NaN;
            foreach(var c in propHits)if(c!=null && c.Raycast(ray,out var hit,1f) && !(hit.point.y<=top))top=hit.point.y;
            return top;
        }
        void CurlFeet(float w)
        {
            for(int side=0;side<2;side++){
                var upper=anim.GetBoneTransform(side==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
                var lower=anim.GetBoneTransform(side==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
                var foot=anim.GetBoneTransform(side==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                if(upper==null || lower==null || foot==null)continue;
                var target=foot.position;float ahead=Vector3.Dot(target-seatAt,seatFront);
                if(ahead>seatEdge-EdgeIn)target-=seatFront*(ahead-(seatEdge-EdgeIn));
                float top=PropTop(target);if(float.IsNaN(top))continue;
                target.y=top+AnkleUp;
                TwoBone(upper,lower,foot,target,lower.position+seatFront*.4f+Vector3.up*.2f,w);
            }
        }
        // lying on the sofa her stretched legs reach past the armrest: a foot further than SofaInner from the middle seat bends its knee to come back in
        const float SofaInner=.56f; // half of sofa-1's 1.30 m between the armrests, less a little
        void NapFeet(float w)
        {
            var along=Vector3.Cross(Vector3.up,seatFront).normalized;
            for(int side=0;side<2;side++){
                var upper=anim.GetBoneTransform(side==0?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
                var lower=anim.GetBoneTransform(side==0?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
                var foot=anim.GetBoneTransform(side==0?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
                if(upper==null || lower==null || foot==null)continue;
                float a=Vector3.Dot(foot.position-seatAt,along);if(Mathf.Abs(a)<=SofaInner)continue;
                var target=foot.position-along*(a-Mathf.Sign(a)*SofaInner);target.y=Mathf.Max(target.y,seatAt.y-.02f);
                TwoBone(upper,lower,foot,target,lower.position+seatFront*.3f+Vector3.up*.1f,w);
            }
        }
        // a hand resting on a sofa cushion must lie on top of it: the clips put the hand about where a chair seat or the hip is, the sofa's cushion is soft and higher.
        // After the pose, a hand whose palm is under the cushion's top is lifted by a two-bone arm IK to 7 mm above it, the wrist keeping its world rotation (palm down).
        const float HandThick=.02f, HandAbove=.007f; // the wrist bone to the underside of the palm; the gap left over the cushion
        static readonly bool SitHandOn=Environment.GetEnvironmentVariable("ODDITY_SITHAND")!="0";
        readonly bool[] sitHandLogged=new bool[2];
        // the cushion's top under a point and its surface normal (NaN top when the ray misses the sofa)
        float PropTopNormal(Vector3 p,out Vector3 normal)
        {
            var ray=new Ray(new Vector3(p.x,seatAt.y+.3f,p.z),Vector3.down);float top=float.NaN;normal=Vector3.up;
            foreach(var c in propHits)if(c!=null && c.Raycast(ray,out var hit,1f) && !(hit.point.y<=top)){top=hit.point.y;normal=hit.normal;}
            return top;
        }
        readonly Vector3[] handFingersLocal=new Vector3[2],handPalmLocal=new Vector3[2];bool handAxesDone;
        const float PropOut=25; // degrees the fingers point outward, away from the body, from the seat's forward direction
        void SitHands()
        {
            float w=Mathf.Clamp01(weights[SitIndex]+(onFloor?0:weights[CurlIndex]));
            if(!SitHandOn || w<=0 || sofa==null || chairSat!=null || chairJob!=null || onFloor)return;
            var along0=Vector3.Cross(Vector3.up,seatFront).normalized; // her right, as she faces the seat's front
            for(int side=0;side<2;side++){
                var upper=anim.GetBoneTransform(side==0?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
                var lower=anim.GetBoneTransform(side==0?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
                var hand=anim.GetBoneTransform(side==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                if(upper==null || lower==null || hand==null)continue;
                float total=0;var normal=Vector3.up;
                for(int pass=0;pass<2;pass++){
                    var along=(hand.position-lower.position).normalized;float need=0;
                    foreach(float k in new[]{0f,.05f,.10f}){ // the wrist and two points along the palm
                        var pt=hand.position+along*k;float top=PropTopNormal(pt,out var n);if(float.IsNaN(top))continue;
                        if(top+HandAbove-(pt.y-HandThick)>need){need=top+HandAbove-(pt.y-HandThick);normal=n;}
                    }
                    if(need<.002f || need>.25f)break; // already on top, or far below (behind the cushion): leave it
                    TwoBone(upper,lower,hand,hand.position+Vector3.up*need,lower.position,w);total+=need*w;
                }
                if(total<=.002f)continue;
                // lean on it: palm flat on the cushion (palm normal down along the surface), fingers forward and a little outward
                var down=-normal;var outward=side==0?-along0:along0;
                var fwd=Vector3.ProjectOnPlane(seatFront,normal).normalized;
                var fingersWant=(Quaternion.AngleAxis(side==0?-PropOut:PropOut,normal)*fwd); // turns toward her left for the left hand, right for the right hand
                if(Vector3.Dot(fingersWant,outward)<Vector3.Dot(fwd,outward))fingersWant=Quaternion.AngleAxis(side==0?PropOut:-PropOut,normal)*fwd; // whichever way is away from the body
                var fingersNow=(hand.rotation*handFingersLocal[side]).normalized;var palmNow=(hand.rotation*handPalmLocal[side]).normalized;
                var wantRot=Quaternion.LookRotation(fingersWant,down)*Quaternion.Inverse(Quaternion.LookRotation(fingersNow,palmNow))*hand.rotation;
                var keepRot=hand.rotation;
                hand.rotation=wantRot; // measure the palm's underside in its new direction and re-place the wrist 3-8 mm above the cushion
                float shift=float.MinValue;var fw=(hand.rotation*handFingersLocal[side]).normalized;var pn=(hand.rotation*handPalmLocal[side]).normalized;
                foreach(float k in new[]{0f,.05f,.10f}){var pt=hand.position+fw*k+pn*HandThick;float top=PropTopNormal(pt,out _);if(float.IsNaN(top))continue;shift=Mathf.Max(shift,top+.005f-pt.y);}
                if(shift>float.MinValue){shift=Mathf.Clamp(shift,-.06f,.06f);hand.rotation=keepRot;TwoBone(upper,lower,hand,hand.position+Vector3.up*shift,lower.position,w);total+=shift*w;}
                hand.rotation=Quaternion.Slerp(keepRot,wantRot,w);
                if(w>.9f && !sitHandLogged[side]){sitHandLogged[side]=true;Debug.Log("ROOMPLAY_SITHAND side="+(side==0?"L":"R")+" lift="+F(total)+" m");}
            }
        }
        // a dining chair is tucked under the table and moves by itself: it slides out while she walks beside it, she steps in front of it, sits and it slides in with her;
        // getting up it slides out with her, she stands and steps aside, and it slides back under the table as she walks off. no hands on the chair
        class Chair{public Transform chair,mark;public Vector3 home,front,side;}
        class ChairWork{public Chair c;public bool sitting;public int phase,clip,leg;public float t,stuck,ct;}
        Chair chairSat;ChairWork chairJob;float blendTime=.25f;
        const float ChairPull=.4f,ChairSlide=.8f,ChairBeside=.45f,ChairBehind=.35f,ChairStep=.42f,ChairFace=.35f,SitBlend=.6f;
        // beside the chair's tucked-in place, a little behind its seat
        static Vector3 ChairSide(Chair c)=>c.home+(c.mark.position-c.chair.position)-c.front*ChairBehind+c.side*ChairBeside;
        static Vector3 ChairFrontSpot(Chair c)=>c.mark.position+c.front*ChairStep; // her calves just clear the seat's front edge (0.30 m)
        static Vector3 ChairFrontSide(Chair c)=>ChairFrontSpot(c)+c.side*ChairBeside; // she walks along the chair's side to here, then steps across in front of it
        static float YawOf(Vector3 d)=>Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg;
        void StartChairSit(Transform mark,Vector3 front,int clip)
        {
            var root=mark.root;var c=new Chair{chair=root,mark=mark,home=root.position,front=front};
            c.side=Vector3.Cross(Vector3.up,front).normalized;if(Vector3.Dot(new Vector3(x,0,z)-mark.position,c.side)<0)c.side=-c.side; // the side she comes from
            if(apt){ // the apartment's chairs stand beside a wall: she goes round on the side with more room (further from every wall), whichever side she came from
                var walls=Apartment.WallBlockers();
                float Away(Vector3 q)=>walls.Min(r=>{float dx=Mathf.Max(r.xMin-q.x,0,q.x-r.xMax),dz=Mathf.Max(r.yMin-q.z,0,q.z-r.yMax);return Mathf.Sqrt(dx*dx+dz*dz);});
                if(Away(mark.position+c.side*ChairBeside)<Away(mark.position-c.side*ChairBeside))c.side=-c.side;
            }
            onFloor=false;seatIsBed=false;chairJob=new ChairWork{c=c,sitting=true,clip=clip};Notice("의자로 "+root.name+" "+Keys[clip].clip);
        }
        void StartChairStand(int then)
        {
            if(chairSat==null)return;
            chairJob=new ChairWork{c=chairSat,sitting=false,clip=then};chairSat=null;Notice("의자에서 일어나기");
        }
        void NextChairPhase(){chairJob.phase++;chairJob.t=0;chairJob.stuck=0;chairJob.leg=0;}
        // walks toward a floor spot; true on arrival. collide keeps her out of the furniture until she has been stuck for half a second
        bool WalkTo(Vector3 target,float dt,bool collide)
        {
            var p=new Vector2(x,z);var d=new Vector2(target.x,target.z)-p;
            if(d.magnitude<.03f){if(current==10){direction=0;SelectRaw(9);}return true;}
            var next=p+d.normalized*Mathf.Min(speed*dt,d.magnitude);
            if(collide && chairJob.stuck<.5f){var c=Collide(p,next);if((c-p).sqrMagnitude<1e-8f)chairJob.stuck+=dt;else next=c;}
            if(current!=10){direction=1;SelectRaw(10);}
            Turn(YawOf(new Vector3(next.x-p.x,0,next.y-p.y)));x=next.x;z=next.y;return false;
        }
        // moves the chair to k (0 tucked in, 1 pulled out); she moves with it when she sits on it
        void SlideChair(float k,bool carry)
        {
            var c=chairJob.c;var was=c.chair.position;c.chair.position=c.home-c.front*ChairPull*k;
            if(carry){var dlt=c.chair.position-was;x+=dlt.x;z+=dlt.z;}
            seatAt=c.mark.position;
        }
        void ChairTick(float dt)
        {
            var j=chairJob;var c=j.c;bool first=j.t==0;j.t+=dt;float k=Mathf.SmoothStep(0,1,j.t/ChairSlide);
            if(j.sitting)switch(j.phase){
                case 0: // the chair slides out on its own from the start; she goes beside it, along its side, and steps in front once it is out
                    j.ct+=dt;SlideChair(Mathf.SmoothStep(0,1,j.ct/ChairSlide),false);
                    if(j.leg==0){if(WalkTo(ChairSide(c),dt,true))j.leg=1;}
                    else if(j.leg==1){if(WalkTo(ChairFrontSide(c),dt,false) && j.ct>=ChairSlide)j.leg=2;}
                    else if(WalkTo(ChairFrontSpot(c),dt,false))NextChairPhase();break;
                case 1:Turn(YawOf(c.front));if(j.t>ChairFace)NextChairPhase();break; // back to the seat
                case 2:if(first){seatAt=c.mark.position;seatFront=c.front;seatEdge=.30f;blendTime=SitBlend;SelectRaw(j.clip);}
                    if(j.t>SitBlend+.1f){blendTime=.25f;NextChairPhase();}break;
                case 3:SlideChair(1-k,true);if(j.t>=ChairSlide){chairSat=c;chairJob=null;Notice("앉기 "+c.chair.name+" "+Keys[current].clip);}break;
            }
            else switch(j.phase){
                case 0:SlideChair(k,true);if(j.t>=ChairSlide)NextChairPhase();break;
                case 1:if(first){blendTime=SitBlend;SelectRaw(9);}if(j.t>SitBlend+.1f){blendTime=.25f;NextChairPhase();}break;
                case 2: // she steps out to the chair's side; from there it slides back under the table by itself while she walks on
                    if(j.leg==0){if(WalkTo(ChairFrontSide(c),dt,false))j.leg=1;break;}
                    j.ct+=dt;SlideChair(1-Mathf.SmoothStep(0,1,j.ct/ChairSlide),false);
                    if(j.leg==1 && WalkTo(ChairSide(c),dt,false))j.leg=2;
                    if(j.leg==2 && j.ct>=ChairSlide){chairJob=null;if(j.clip>=0)SelectRaw(j.clip);Notice("의자 넣기 끝");}break;
            }
        }
        // pulls her so the hips meet the seat mark, by the sit clip's weight so standing up blends back
        void PinToSeat()
        {
            float w=weights[SitIndex]+weights[NapIndex]+weights[BedIndex]+(onFloor?0:weights[CurlIndex]);if(w<=0 || sofa==null)return;
            var hips=anim.GetBoneTransform(HumanBodyBones.Hips);
            var aim=seatAt+(weights[NapIndex]>0?Vector3.up*NapLift:Vector3.zero);
            model.transform.position+=(aim-hips.position)*w;
            if(weights[NapIndex]+weights[BedIndex]<.5f)OffBackrest(w);
        }
        // a sit clip that leans back puts her back and shoulders into the backrest: the deepest bone is measured against the backrest with a ray from the front and she is moved forward by that much
        // segments of her back and arms with the body's thickness behind each bone; each is sampled at both ends and the middle
        static readonly (HumanBodyBones from,HumanBodyBones to,float radius)[] BackParts={
            (HumanBodyBones.Hips,HumanBodyBones.Spine,.13f),(HumanBodyBones.Spine,HumanBodyBones.Chest,.14f),(HumanBodyBones.Chest,HumanBodyBones.UpperChest,.15f),(HumanBodyBones.UpperChest,HumanBodyBones.Neck,.13f),(HumanBodyBones.Neck,HumanBodyBones.Head,.07f),
            (HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,.05f),(HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,.05f),
            (HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,.08f),(HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,.08f)};
        const float BackPushMost=.25f;
        float BackGap(Vector3 p)
        {
            var ray=new Ray(p+seatFront*.6f,-seatFront);float near=float.PositiveInfinity;
            foreach(var c in propHits)if(c!=null && c.Raycast(ray,out var hit,1.2f) && hit.distance<near && Vector3.Dot(hit.point-seatAt,seatFront)<0)near=hit.distance; // behind the seat mark: the backrest, not an armrest's front
            return near-.6f; // how far in front of the backrest p is; infinite above or beside it
        }
        void OffBackrest(float w)
        {
            float push=0;
            foreach(var (from,to,radius) in BackParts){
                var a=anim.GetBoneTransform(from);var b=anim.GetBoneTransform(to);if(a==null || b==null)continue;
                for(int k=0;k<3;k++){float gap=BackGap(Vector3.Lerp(a.position,b.position,k*.5f));if(!float.IsInfinity(gap))push=Mathf.Max(push,radius-gap);}
            }
            push=Mathf.Min(push,BackPushMost);if(push<=0)return;
            if(push>backMax+.01f){backMax=push;Debug.Log("ROOMPLAY_BACKREST push="+F(push)+" clip="+Keys[current].clip);}
            model.transform.position+=seatFront*push*w;
        }
        float backMax;
        // ODDITY_ROOM (default on; 0 is the old painted dusk): a 4 x 4 m room seen from high in the front right corner, looking down like a doll house
        bool room;float roomZoom;
        // ODDITY_HOUSE: apt (default) is the 17-pyeong flat in Apartment.cs with one camera per room, room is the old 4 x 4 m room
        bool apt,one;Apartment.View aptView; // one: the 5-pyeong studio seen from across the street (the default house); apt: the 17-pyeong flat, ODDITY_HOUSE=apt
        static readonly Vector3 RoomEye=new Vector3(2.35f,2.9f,-2.55f), RoomAim=new Vector3(-.25f,.35f,.45f);
        Vector3 curEye=RoomEye,curAim=RoomAim;float curFov=RoomFov;
        const float RoomHalf=2f, RoomFov=46;
        // the apartment: the shell, doors and plain-box furniture from Apartment.cs; she starts in the living room, every room has its own camera (AptTick cuts between them)
        void AptRoom(Camera cam)
        {
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=one?new Color(.55f,.62f,.70f):new Color(.16f,.15f,.13f);
            RenderSettings.ambientLight=new Color(.55f,.53f,.48f);
            var root=new GameObject("Apartment").transform;
            furniture.Clear();perchCols.Clear();perchOf.Clear();
            var holder=new GameObject("PerchCols").transform;holder.SetParent(root,false);
            void PerchBox(string name,float x0,float x1,float y0,float y1,float z0,float z1)
            {
                var g=new GameObject("PerchCol_"+name);g.transform.SetParent(holder,false);g.transform.localPosition=new Vector3((x0+x1)/2,(y0+y1)/2,(z0+z1)/2);
                var c=g.AddComponent<BoxCollider>();c.size=new Vector3(x1-x0,y1-y0,z1-z0);perchCols.Add(c);perchOf[c]=name;
            }
            aptView=Apartment.Build(root,StreetView(),furniture,PerchBox);
            Sun();
            LimX0=Apartment.LimX0;LimX1=Apartment.LimX1;ZNear=Apartment.LimZ0;ZFar=Apartment.LimZ1;
            x=Apartment.HarinStart.x;z=Apartment.HarinStart.y;
            startYaw=Apartment.StartYaw;heading=yaw=targetYaw=turnStart=startYaw;turnTime=1; // she starts facing the living room's camera (north-east); in the studio the street
            if(PetEnv("ODDITY_SOMALIYAW")==null)SomaliYaw=Apartment.SomaliYaw;if(PetEnv("ODDITY_WHITEYAW")==null)WhiteYaw=Apartment.WhiteYaw; // the cats start facing the living room's camera (north-east)
            var rd=aptView.Room(one?"main":"living");curEye=rd.eye;curAim=rd.aim;curFov=rd.fov;cam.fieldOfView=curFov;
            cam.transform.position=curEye;cam.transform.LookAt(curAim);
            if(one){aptForced=true;aptView.ShowAll(true);PeepInit(cam);} // the studio: every wall and the ceiling shown, one camera outside the window
            Debug.Log("ROOMPLAY_APT walls="+aptView.PieceCount+" rooms="+string.Join(",",aptView.rooms.Select(r=>r.name+"(hidden "+aptView.HiddenCount(r.name)+": "+aptView.HiddenNames(r.name)+")")));
        }
        // the camera follows the room of the controlled character (Harin or the cat under control): a cut when its centre is 0.2 m past a door's threshold, the walls between that camera and the room are cut away.
        // WASD keeps the direction it had before the cut until the keys are let go, so a walk through a door does not turn round in the player's hands
        Vector3 latchFwd;bool latched;float keysAt=-10;
        Vector3 MoveFwd()
        {
            keysAt=Time.unscaledTime;
            return latched?latchFwd:Vector3.ProjectOnPlane(cameraView.transform.forward,Vector3.up).normalized;
        }
        bool aptForced,faced;
        // the orbit shot (ODDITY_ORBIT=1 or script key orbit): Harin in the middle of the living room, every wall and the ceiling shown, the LED lights on, the camera once round her in 12 s;
        // script keys orbitaN hold the camera N degrees round from the start for stills
        static readonly bool OrbitEnv=Environment.GetEnvironmentVariable("ODDITY_ORBIT")=="1";
        bool orbitWant=OrbitEnv,orbitOn,orbitAuto;float orbitT,orbitAngle,orbitLog;const float OrbitSeconds=12f;
        void StartOrbit()
        {
            if(!apt || aptView==null || one)return;
            if(insideOn)InsideOff(false);
            orbitOn=true;orbitAuto=true;orbitT=0;orbitAngle=Apartment.OrbitFacingYaw;aptForced=true;aptView.ShowAll(true);aptView.SetLamps(true);
            clickGoal=null;clickSeat=null;x=Apartment.OrbitCentre.x;z=Apartment.OrbitCentre.y;
            heading=Apartment.OrbitFacingYaw;Turn(heading);yaw=targetYaw=turnStart=heading;turnTime=1;Select(9);
            Debug.Log("ROOMPLAY_ORBIT start harin=("+F(x)+","+F(z)+") yaw="+F(heading)+" seconds="+F(OrbitSeconds));
        }
        void OrbitCamera(float dt)
        {
            if(orbitAuto){orbitT=Mathf.Min(OrbitSeconds,orbitT+dt);orbitAngle=Apartment.OrbitFacingYaw+360*Mathf.SmoothStep(0,1,orbitT/OrbitSeconds);}
            var cp=Apartment.OrbitCamera(orbitAngle);var aim=new Vector3(x,Apartment.OrbitAim,z);
            cameraView.fieldOfView=50;cameraView.transform.position=cp;cameraView.transform.LookAt(aim);
            if(Time.unscaledTime-orbitLog>.5f){orbitLog=Time.unscaledTime;Debug.Log("ROOMPLAY_ORBIT harin_face="+F(Mathf.Repeat(Mathf.Atan2(anim.transform.forward.x,anim.transform.forward.z)*Mathf.Rad2Deg,360))+" angle="+F(Mathf.Repeat(orbitAngle,360))+" cam=("+F(cp.x)+","+F(cp.y)+","+F(cp.z)+") radius="+F(Vector2.Distance(new Vector2(cp.x,cp.z),Apartment.OrbitCentre)));}
        }
        // I: the camera inside the room of the character it follows (Harin or the steered cat). Eye height 1.35 m on a free spot (Apartment.InsideSpot) about 1.6 m away, all walls of the room and its ceiling shown, fov 60.
        // It turns to keep the target in view and stays put until the target leaves a 2.4 m circle round the spot it was placed for, comes within 0.4 m, or a tall thing blocks the view; then (and at a room change or Tab) it re-places with a cut.
        // Wheel and arrows zoom (fov 70 -> 15), Shift+wheel swings round the target, Ctrl+wheel changes the height (0.5 -> 2.3 m), Alt+wheel tilts; all kept apart from the outside camera's values
        bool insideOn;Pet insideWho;Vector2 insideAnchor,insideCamXZ;float insideBase,insidePlacedAt,insideZoom=InsideZoom0,inSwing,inSwingWant,inLift=Apartment.InsideCamY,inLiftWant=Apartment.InsideCamY,inTilt,inTiltWant,insideLog,insideRotPath,insidePosPath;Quaternion insideRot=Quaternion.identity,lastInsideRot=Quaternion.identity;Vector3 insideLastPos;float insideFov;
        static readonly float InsideZoom0=Mathf.Log(60f/70f)/Mathf.Log(15f/70f); // fov 60 on a zoom scale that runs 70 -> 15
        const float InsideFovFar=70f,InsideFovNear=15f,InsideLiftMin=.5f,InsideLiftMax=2.3f,InsideLeash=2.4f;
        Vector2 InsideTargetXZ()=>ctl!=null?PetXZ(ctl):new Vector2(x,z);
        static readonly float InsideAimY=float.TryParse(Environment.GetEnvironmentVariable("ODDITY_INSIDEAIMY"),NumberStyles.Float,CultureInfo.InvariantCulture,out var aimY)?aimY:1.25f; // height the inside camera looks at on Harin (stills of squatting, sitting or lying poses lower it)
        Vector3 InsideAim()=>ctl!=null?ctl.root.position+Vector3.up*.2f:new Vector3(x,InsideAimY,z);
        Vector2 InsideFacing(){Vector3 f=ctl!=null?ctl.root.forward:anim!=null?anim.transform.forward:Vector3.forward;f.y=0;if(f.sqrMagnitude<1e-4f)f=Vector3.forward;f.Normalize();return new Vector2(f.x,f.z);}
        void ToggleInside()
        {
            if(!apt || aptView==null){Notice("방 안 카메라는 아파트에서만 쓸 수 있음");return;}
            if(insideOn){InsideOff(true);Notice("방 안 카메라 끝");return;}
            orbitOn=false;orbitWant=false;aptForced=false;insideOn=true;insideWho=ctl;
            var pos=InsideTargetXZ();aptView.SetInside(true);aptView.Show(Apartment.RoomAt(pos));
            InsidePlace("on");Notice("방 안 카메라");
        }
        void InsideOff(bool restore)
        {
            insideOn=false;if(aptView==null)return;
            aptView.SetInside(false);
            if(one)aptView.ShowAll(true); // the studio's outside camera sees every wall
            if(restore && aptView.current!=null){var rd=aptView.Room(aptView.current);curEye=rd.eye;curAim=rd.aim;curFov=rd.fov;roomFocusSet=false;}
            Debug.Log("ROOMPLAY_INSIDE off room="+aptView.current+" cam="+F(cameraView.transform.position.x)+","+F(cameraView.transform.position.y)+","+F(cameraView.transform.position.z));
        }
        // stands the camera for the target where it is now: the anchor is the target's floor point, the base angle the way it faces (seen from the front)
        void InsidePlace(string why)
        {
            var room=aptView.current;insideAnchor=InsideTargetXZ();var f=InsideFacing();insideBase=Mathf.Atan2(f.x,f.y)*Mathf.Rad2Deg;insidePlacedAt=Time.unscaledTime;
            if(!Apartment.InsideSpot(room,insideAnchor,insideBase,inSwing,out var spot)){Debug.LogWarning("ROOMPLAY_INSIDE no free spot in "+room);spot=insideAnchor;}
            insideCamXZ=spot;inLift=inLiftWant;insideFov=0;
            var cp=new Vector3(spot.x,inLift,spot.y);cameraView.transform.position=cp;cameraView.transform.LookAt(InsideAim());insideRot=lastInsideRot=cameraView.transform.rotation;insideLastPos=cp;
            Debug.Log("ROOMPLAY_INSIDE "+(why=="on"?"on":"place why="+why)+" room="+room+" who="+(ctl!=null?ctl.tag:"HARIN")+" cam=("+F(cp.x)+","+F(cp.y)+","+F(cp.z)+") dist="+F(Vector2.Distance(spot,insideAnchor))+" fov="+F(Apartment.InsideFov(Vector2.Distance(spot,insideAnchor))*Mathf.Pow(InsideFovNear/InsideFovFar,insideZoom-InsideZoom0))+" swing="+F(inSwing));
        }
        void InsideFrame(float dt)
        {
            if(ctl!=insideWho){insideWho=ctl;var nr=Apartment.RoomAt(InsideTargetXZ());if(nr!=aptView.current)aptView.Show(nr);InsidePlace("tab");}
            var t=InsideTargetXZ();var room=aptView.current;
            if(Time.unscaledTime-insidePlacedAt>.5f){
                string why=Vector2.Distance(t,insideAnchor)>InsideLeash?"leash":Vector2.Distance(t,insideCamXZ)<.4f?"close":!Apartment.InsideSight(insideCamXZ,t)?"blocked":null;
                if(why!=null)InsidePlace(why);
            }
            inSwing=Mathf.MoveTowardsAngle(inSwing,inSwingWant,dt*60f);inLift=Mathf.MoveTowards(inLift,inLiftWant,dt*1.5f);inTilt=Mathf.MoveTowards(inTilt,inTiltWant,dt*24f);
            if(Apartment.InsideSpot(room,insideAnchor,insideBase,inSwing,out var spot)) // a swing keeps the camera on free spots; a long jump (a skipped blocked angle) is a cut
                insideCamXZ=Vector2.Distance(spot,insideCamXZ)>.8f?spot:Vector2.Lerp(insideCamXZ,spot,1-Mathf.Exp(-12*dt));
            var cp=new Vector3(insideCamXZ.x,inLift,insideCamXZ.y);
            var aimAt=InsideAim();
            if(ctl!=null){ // following a cat: its root moves with every head and paw move, so the aim gets slack (a cut, a Tab or a long jump re-centres at once)
                if(!insideFocusSet || insideFocusWho!=ctl || Vector3.Distance(insideFocus,aimAt)>.8f){insideFocus=aimAt;insideFocusWho=ctl;insideFocusSet=true;}
                insideFocus=SlackFollow(insideFocus,aimAt,cp,insideFov>0?insideFov:InsideFovFar,InsideCatSlack,8,dt);aimAt=insideFocus;
            }
            else insideFocusSet=false;
            var want=Quaternion.LookRotation(aimAt-cp,Vector3.up);
            insideRot=Quaternion.Slerp(insideRot,want,1-Mathf.Exp(-8*dt));
            var rot=inTilt!=0?Quaternion.AngleAxis(-inTilt,insideRot*Vector3.right)*insideRot:insideRot;
            float fovWant=Mathf.Clamp(Apartment.InsideFov(Vector2.Distance(t,insideCamXZ))*Mathf.Pow(InsideFovNear/InsideFovFar,insideZoom-InsideZoom0),InsideFovNear*.6f,90f); // a close camera widens the default lens (60 at 1.2 m -> 80 at 0.6 m); the zoom keys work relative to that
            insideFov=insideFov<=0?fovWant:Mathf.Lerp(insideFov,fovWant,1-Mathf.Exp(-6*dt));
            cameraView.fieldOfView=insideFov;cameraView.transform.SetPositionAndRotation(cp,rot);
            insideRotPath+=Quaternion.Angle(rot,lastInsideRot);lastInsideRot=rot;insidePosPath+=Vector3.Distance(cp,insideLastPos);insideLastPos=cp;
            if(Time.unscaledTime-insideLog>1f){insideLog=Time.unscaledTime;Debug.Log("ROOMPLAY_INSIDE track room="+room+" cam=("+F(cp.x)+","+F(cp.y)+","+F(cp.z)+") fov="+F(cameraView.fieldOfView)+" rotpath="+F(insideRotPath)+" pospath="+F(insidePosPath)+" target_dist="+F(Vector2.Distance(t,insideCamXZ)));insideRotPath=insidePosPath=0;}
        }
        void AptForce(string name)
        {
            if(aptView==null)return;
            if(one){Notice("이 집은 창밖 카메라 하나뿐 (I 는 방 안 카메라)");return;}
            if(insideOn)InsideOff(false);
            orbitOn=false;orbitWant=false;
            if(name.StartsWith("corner")){ // wide stills from the living room's corners with the ceiling, the walls and the lights on
                aptForced=true;aptView.ShowAll(true);aptView.SetLamps(true);
                if(name=="cornerne"){curEye=new Vector3(4.10f,1.95f,4.75f);curAim=new Vector3(.5f,1.0f,1.9f);}else{curEye=new Vector3(.30f,1.95f,1.60f);curAim=new Vector3(3.9f,1.0f,4.3f);}
                curFov=75;roomFocusSet=false;Debug.Log("ROOMPLAY_ROOM view="+name+" corner");return;}
            if(name=="auto"){aptForced=false;return;}
            if(name=="overview"){aptForced=true;aptView.ShowAll();curEye=new Vector3(2.19f,13f,-3.5f);curAim=new Vector3(2.19f,0,5.2f);curFov=50;roomFocusSet=false;return;} // debug: the whole flat, every wall shown
            aptForced=true;aptView.Show(name);var rd=aptView.Room(name);curEye=rd.eye;curAim=rd.aim;curFov=rd.fov;roomFocusSet=false;Debug.Log("ROOMPLAY_ROOM view="+name+" forced");
        }
        void AptTick()
        {
            if(!faced && model!=null && !busy && apt){faced=true;heading=startYaw;Turn(startYaw);yaw=targetYaw=turnStart=startYaw;turnTime=1;} // she starts facing the living room's camera
            if(orbitWant && !orbitOn && faced && model!=null && !busy){orbitWant=false;StartOrbit();}
            if(aptView==null || aptForced)return;
            var pos=ctl!=null?PetXZ(ctl):new Vector2(x,z);
            var oldFwd=Vector3.ProjectOnPlane(cameraView.transform.forward,Vector3.up).normalized;
            if(aptView.Tick(pos,Time.unscaledTime,out var from)){
                var rd=aptView.Room(aptView.current);curEye=rd.eye;curAim=rd.aim;curFov=rd.fov;roomFocusSet=false;
                if(from!=null && Time.unscaledTime-keysAt<.25f && oldFwd.sqrMagnitude>.1f){latchFwd=oldFwd;latched=true;}
                if(insideOn && ctl==insideWho)InsidePlace("room"); // after a Tab the next InsideFrame places for the new target
                Debug.Log("ROOMPLAY_ROOM view="+aptView.current+(from!=null?" from="+from:"")+" at=("+F(pos.x)+","+F(pos.y)+") who="+(ctl!=null?ctl.tag:"HARIN"));
            }
            if(latched && Time.unscaledTime-keysAt>.25f)latched=false;
            foreach(var p in pets)if(p.root!=null)foreach(var r in p.root.GetComponentsInChildren<Renderer>())r.enabled=aptView.Near(PetXZ(p)); // a cat outside the shown room is not in the picture
        }
        // a random floor point for a wandering cat: the living room most, the kitchen's passage, the balcony
        Vector2 AptWander()
        {
            var boxes=Apartment.WanderBoxes;var w=Apartment.WanderWeights;float r=Rnd(0,w.Sum());int i=0;
            for(;i<boxes.Length-1;i++){if(r<w[i])break;r-=w[i];}
            var b=boxes[i];return new Vector2(Rnd(b.xMin,b.xMax),Rnd(b.yMin,b.yMax));
        }
        void Room(Camera cam)
        {
            if(apt){AptRoom(cam);return;}
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.16f,.15f,.13f);cam.fieldOfView=RoomFov;
            RenderSettings.ambientLight=new Color(.55f,.53f,.48f);
            var root=new GameObject("Room").transform;
            Color floor=new Color(.72f,.52f,.32f), wall=new Color(.93f,.91f,.72f), trim=new Color(.62f,.42f,.26f);
            Part(PrimitiveType.Cube,root,new Vector3(0,-.01f,0),new Vector3(RoomHalf*2,.02f,RoomHalf*2),floor);
            Part(PrimitiveType.Cube,root,new Vector3(0,1.25f,RoomHalf+.025f),new Vector3(RoomHalf*2+.05f,2.5f,.05f),wall); // back wall
            LeftWall(root,wall,trim);
            Part(PrimitiveType.Cube,root,new Vector3(0,.05f,RoomHalf-.005f),new Vector3(RoomHalf*2,.1f,.02f),trim); // skirting
            Part(PrimitiveType.Cube,root,new Vector3(-RoomHalf+.005f,.05f,0),new Vector3(.02f,.1f,RoomHalf*2),trim);
            for(int i=-3;i<=3;i++)Part(PrimitiveType.Cube,root,new Vector3(i*.5f,.001f,0),new Vector3(.006f,.002f,RoomHalf*2),trim); // floor boards
            // a ceiling and a front wall that only cast shadows: the camera looks over them, the sun can only come in through the window
            foreach(var (pos,size) in new[]{(new Vector3(0,2.525f,0),new Vector3(RoomHalf*2+.3f,.05f,RoomHalf*2+.3f)),(new Vector3(0,1.25f,-RoomHalf-.025f),new Vector3(RoomHalf*2+.3f,2.6f,.05f)),(new Vector3(RoomHalf+.025f,1.25f,0),new Vector3(.05f,2.6f,RoomHalf*2+.3f))}){
                var g=Part(PrimitiveType.Cube,root,pos,size,wall);g.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;}
            Sun();
            Furnish(root);
            Limit=RoomHalf-.25f;LimX0=-Limit;LimX1=Limit;ZNear=-RoomHalf+.25f;ZFar=RoomHalf-.25f;
            x=-.6f;z=-.3f; // she starts on the open floor between the table and the window
            cam.transform.position=RoomEye;cam.transform.LookAt(RoomAim);
        }
        // the living room and kitchen of a small Korean flat (the bed is in the bedroom behind the door in the left wall): the entrance and shoe cabinet in the front right corner (the camera side), the kitchen run along the back wall
        // beside the sofa (sink, cooktop, built-in washer, upper cabinets, fridge in the corner), the bedroom door in the left wall,
        // a low TV stand on the front wall facing the sofa (sofa and TV on the two walls at right angles to the window, as Korean living rooms do,
        // so the sun never lands on the screen), the dining table between the entrance and the living area, clear of the sofa front, a laptop on it,
        // a floor lamp between the sofa and the counter, a plant in the corner by the window, a rug in front of the sofa. plain boxes; furniture footprints block walking
        readonly List<Rect> furniture=new List<Rect>();
        void Furnish(Transform root)
        {
            furniture.Clear();
            var f=new GameObject("Furniture").transform;f.SetParent(root,false);
            Color oak=new Color(.74f,.57f,.40f), white=new Color(.93f,.93f,.91f), top=new Color(.30f,.30f,.32f), steel=new Color(.80f,.81f,.82f), dark=new Color(.12f,.12f,.13f);
            GameObject Box(float x0,float x1,float y0,float y1,float z0,float z1,Color c,bool block=false)
            {
                if(block)furniture.Add(Rect.MinMaxRect(x0,z0,x1,z1));
                return Part(PrimitiveType.Cube,f,new Vector3((x0+x1)/2,(y0+y1)/2,(z0+z1)/2),new Vector3(x1-x0,y1-y0,z1-z0),c);
            }
            // kitchen: counter x 0.40 -> 1.35 along the back wall, 0.85 m high, 0.6 m deep
            Box(.40f,1.35f,0,.82f,1.40f,2f,white,true);
            Box(.39f,1.36f,.82f,.86f,1.38f,2f,top);                                    // worktop
            Box(.52f,.82f,.84f,.865f,1.52f,1.86f,steel);                               // sink
            Box(.63f,.66f,.86f,1.05f,1.88f,1.91f,steel);                               // tap
            Box(1.00f,1.30f,.86f,.87f,1.55f,1.85f,dark);                               // induction cooktop
            var drum=Part(PrimitiveType.Cylinder,f,new Vector3(.62f,.42f,1.395f),new Vector3(.36f,.01f,.36f),new Color(.55f,.58f,.62f));drum.transform.localRotation=Quaternion.Euler(90,0,0); // washer door under the sink
            Box(.40f,1.35f,1.48f,2.10f,1.65f,2f,white);                                // upper cabinets
            Box(.98f,1.32f,1.36f,1.48f,1.62f,2f,steel);                                // hood
            // fridge in the back right corner
            Box(1.37f,2f,0,1.78f,1.36f,2f,steel,true);
            Box(1.40f,1.97f,1.05f,1.06f,1.355f,1.36f,dark);                            // door split
            // entrance: grey tiles a step down and a shoe cabinet against the front wall
            Box(1.05f,2f,-.005f,.003f,-2f,-1.05f,new Color(.58f,.58f,.57f));
            Box(1.10f,2f,0,1.05f,-2f,-1.65f,oak,true);
            // the bedroom door in the left wall near the front corner (the bed is in that room): frame, closed door, handle
            Box(-2f,-1.96f,0,2.10f,-1.86f,-.84f,oak);                                  // frame
            Box(-1.985f,-1.95f,0,2.04f,-1.80f,-.90f,new Color(.86f,.82f,.74f));       // door
            Box(-1.95f,-1.90f,.95f,1.00f,-1.02f,-.94f,steel);                          // handle
            // a low TV stand against the front wall across from the sofa, the TV facing it; low and small so it hides little of the room from the camera
            Box(-1.30f,-.20f,0,.40f,-2f,-1.62f,oak,true);                              // centred on the sofa (x -0.75)
            Box(-1.22f,-.28f,.44f,.96f,-1.84f,-1.81f,dark);                            // TV
            Box(-.80f,-.70f,.40f,.44f,-1.88f,-1.76f,dark);                             // stand
            // a floor lamp in the gap between the sofa's right arm and the kitchen counter
            Box(.12f,.32f,0,.03f,1.62f,1.82f,dark,true);
            Box(.205f,.235f,.03f,1.45f,1.705f,1.735f,dark);
            var shade=Part(PrimitiveType.Cylinder,f,new Vector3(.22f,1.52f,1.72f),new Vector3(.30f,.11f,.30f),new Color(.95f,.90f,.80f));
            // a potted plant in the corner between the window and the bedroom door
            var pot=Part(PrimitiveType.Cylinder,f,new Vector3(-1.78f,.17f,-.52f),new Vector3(.28f,.17f,.28f),new Color(.80f,.78f,.74f));furniture.Add(Rect.MinMaxRect(-1.92f,-.66f,-1.64f,-.38f));
            foreach(var (dx,dy,dz,k) in new[]{(0f,.62f,0f,.46f),(.08f,.82f,-.06f,.34f),(-.07f,.76f,.07f,.32f)})Part(PrimitiveType.Sphere,f,new Vector3(-1.78f+dx,dy,-.52f+dz),Vector3.one*k,new Color(.30f,.48f,.28f));
            // rug in front of the sofa
            Box(-1.30f,-.20f,0,.006f,.35f,1.05f,new Color(.82f,.76f,.66f));
            // an interior sill board under the window, 0.30 m deep into the room, as long as the opening, top at the window's bottom edge, the frame's wood: a cat can sit on it
            Box(-2f,-1.70f,WinY.x-.04f,WinY.x,WinZ.x,WinZ.y,new Color(.62f,.42f,.26f),true);
            // colliders for the cats' perches (rays for the landing height, clicks): the counter run with its cooktop and cabinets, the fridge, the sill board
            perchCols.Clear();perchOf.Clear();
            void PerchBox(string name,float x0,float x1,float y0,float y1,float z0,float z1)
            {
                var g=new GameObject("PerchCol_"+name);g.transform.SetParent(f,false);g.transform.localPosition=new Vector3((x0+x1)/2,(y0+y1)/2,(z0+z1)/2);
                var c=g.AddComponent<BoxCollider>();c.size=new Vector3(x1-x0,y1-y0,z1-z0);perchCols.Add(c);perchOf[c]=name;
            }
            PerchBox("counter",.40f,1.35f,0,.86f,1.40f,2f);PerchBox("counter",1.00f,1.30f,.86f,.87f,1.55f,1.85f);PerchBox("counter",.40f,1.35f,1.48f,2.10f,1.65f,2f);
            PerchBox("fridge",1.37f,2f,0,1.78f,1.36f,2f);
            PerchBox("sill",-2f,-1.70f,WinY.x-.04f,WinY.x,WinZ.x,WinZ.y);
        }
        // a big square window in the left wall: the wall is four blocks around the opening, a wooden frame and cross bars, a bright sky card outside
        static readonly Vector2 WinZ=new Vector2(-.2f,1.4f), WinY=new Vector2(.85f,2.15f);
        void LeftWall(Transform root,Color wall,Color trim)
        {
            const float H=2.5f,T=.05f;float x=-RoomHalf-T/2,z0=-RoomHalf,z1=RoomHalf;
            void Block(float za,float zb,float ya,float yb){if(zb-za>1e-3f && yb-ya>1e-3f)Part(PrimitiveType.Cube,root,new Vector3(x,(ya+yb)/2,(za+zb)/2),new Vector3(T,yb-ya,zb-za),wall);}
            Block(z0,WinZ.x,0,H);Block(WinZ.y,z1,0,H);Block(WinZ.x,WinZ.y,0,WinY.x);Block(WinZ.x,WinZ.y,WinY.y,H);
            float w=WinZ.y-WinZ.x,h=WinY.y-WinY.x,zc=(WinZ.x+WinZ.y)/2,yc=(WinY.x+WinY.y)/2;const float F=.06f;
            Part(PrimitiveType.Cube,root,new Vector3(x+.01f,WinY.x-F/2,zc),new Vector3(.12f,F,w+F*2),trim); // sill
            Part(PrimitiveType.Cube,root,new Vector3(x,WinY.y+F/2,zc),new Vector3(.08f,F,w+F*2),trim);
            Part(PrimitiveType.Cube,root,new Vector3(x,yc,WinZ.x-F/2),new Vector3(.08f,h,F),trim);
            Part(PrimitiveType.Cube,root,new Vector3(x,yc,WinZ.y+F/2),new Vector3(.08f,h,F),trim);
            var bar=Part(PrimitiveType.Cube,root,new Vector3(x,yc,zc),new Vector3(.04f,h,.03f),trim);bar.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            Part(PrimitiveType.Cube,root,new Vector3(x,yc,zc),new Vector3(.04f,.03f,w),trim);
            var sky=GameObject.CreatePrimitive(PrimitiveType.Quad);Destroy(sky.GetComponent<Collider>());sky.name="Sky";sky.transform.SetParent(root,false);
            sky.transform.SetPositionAndRotation(new Vector3(-RoomHalf-1.2f,yc+.2f,zc),Quaternion.Euler(0,-90,0));sky.transform.localScale=new Vector3(w*2.2f,h*2.2f,1);
            var r=sky.GetComponent<Renderer>();r.material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){color=Color.white,mainTexture=StreetView()};r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // the view out of the window, painted on the card behind it (real buildings out there would show past the room's open sides): sky, a row of
        // apartment blocks across the street with window rows and roof bands, tree tops below. the card sits close behind the window so it never shows outside it
        static Texture2D StreetView()
        {
            const int W=512,H=384;var t=new Texture2D(W,H,TextureFormat.RGBA32,false){wrapMode=TextureWrapMode.Clamp};var px=new Color[W*H];
            for(int y=0;y<H;y++){var sky=Color.Lerp(new Color(.86f,.92f,.97f),new Color(.55f,.74f,.92f),y/(float)H);for(int x=0;x<W;x++)px[y*W+x]=sky;}
            void Rect(int x0,int y0,int x1,int y1,Color c){for(int y=Mathf.Max(0,y0);y<Mathf.Min(H,y1);y++)for(int x=Mathf.Max(0,x0);x<Mathf.Min(W,x1);x++)px[y*W+x]=c;}
            int[] tops={300,330,290,345,310};
            for(int b=0;b<5;b++){
                int x0=b*104-6,x1=x0+92;var wall=b%2==0?new Color(.92f,.90f,.84f):new Color(.85f,.83f,.78f);
                Rect(x0,0,x1,tops[b],wall);Rect(x0,tops[b]-10,x1,tops[b],new Color(.62f,.36f,.30f));
                for(int y=40;y<tops[b]-22;y+=22)Rect(x0+8,y,x1-8,y+9,new Color(.45f,.56f,.64f));
                Rect(x0+44,40,x0+48,tops[b]-16,wall*.9f); // the stair core line
            }
            Rect(0,0,W,70,new Color(.36f,.50f,.34f));for(int x=0;x<W;x+=18)Rect(x,60,x+12,78+(x*37%11),new Color(.30f,.45f,.30f)); // tree tops
            t.SetPixels(px);t.Apply();return t;
        }
        // the sun comes in through the window and lands on the sofa seat; the walls cast shadows so only the window patch is bright
        Light keyLight;
        void Sun()
        {
            if(keyLight==null)return;
            var through=apt?Apartment.SunThrough:new Vector3(-RoomHalf,(WinY.x+WinY.y)/2,(WinZ.x+WinZ.y)/2);var onto=apt?Apartment.SunOnto:new Vector3(-.75f,.45f,1.55f); // the window middle to the sofa seat (the apartment: the balcony window's middle, the sun comes in through the south windows)
            keyLight.transform.rotation=Quaternion.LookRotation(onto-through);keyLight.color=new Color(1f,.95f,.88f);keyLight.intensity=1.2f; // was (1,.9,.72) at 1.6: too yellow and too strong, her skin clipped to orange
            keyLight.shadows=LightShadows.Soft;keyLight.shadowStrength=.92f;keyLight.shadowBias=.03f;keyLight.shadowNormalBias=.3f;
            // ODDITY_LAMP=1 turns the ceiling lamp on; by default only the window sun lights the room, with a brighter ambient fill
            bool lamp=Environment.GetEnvironmentVariable("ODDITY_LAMP")=="1" || OrbitEnv;
            RenderSettings.ambientLight=lamp?(apt?new Color(.40f,.39f,.38f):new Color(.26f,.26f,.27f)):apt?new Color(.55f,.54f,.52f):new Color(.40f,.39f,.38f); // the apartment's rooms away from the sun are lit by this alone: a little brighter
            if(one && Environment.GetEnvironmentVariable("ODDITY_NIGHT")=="1"){keyLight.intensity=.02f;RenderSettings.ambientLight=new Color(.045f,.05f,.075f);} // a dark room: no sun, no LED, only what glows (TV, phone)
            if(one && lamp){keyLight.intensity=.04f;RenderSettings.ambientLight=new Color(.12f,.13f,.18f);} // the studio at night: only its LEDs and a faint blue fill
            if(apt){aptView?.SetLamps(lamp);if(lamp)Debug.Log("ROOMPLAY_LAMP on LED intensities/range: "+aptView?.LampInfo()+" ambient="+RenderSettings.ambientLight.r.ToString("0.00",CultureInfo.InvariantCulture));}else if(lamp)CeilingLamp();
        }
        // a fluorescent tube on the ceiling: two cool white point lights along its length light the room from above, no shadows
        static readonly Vector3 LampAt=new Vector3(-.2f,2.42f,.3f);
        const float LampHalf=.6f, LampPower=.8f, LampRange=5f;
        static readonly Color LampColor=new Color(.92f,.97f,1f);
        void CeilingLamp()
        {
            foreach(var dz in new[]{-LampHalf,LampHalf}){
                var l=new GameObject("CeilingLamp").AddComponent<Light>();l.type=LightType.Point;l.color=LampColor;l.intensity=LampPower;l.range=LampRange;l.shadows=LightShadows.None;
                l.transform.position=(apt?Apartment.LampAt:LampAt)+new Vector3(0,0,dz);}
        }
        // the up arrow closes in on her (narrower lens, aim moves to her chest and up to her face at the closest), the down arrow goes back to the whole room.
        // the aim has slack: it only moves when she leaves a circle around it (RoomSlack of the half frame), so sway and breathing do not shake the view
        Vector3 roomFocus,lastWant;bool roomFocusSet;float wantPath,focusPath; // paths are logged per script step: how far the body point and the aim moved
        float wheelZoom;const float WheelNotch=.08f, WheelRate=.8f; // a wheel notch is 8% of the zoom range
        // Ctrl + wheel raises or lowers the camera eye (the aim stays, so the view tilts): 0.15 m a notch, from 1.2 m below the room's eye to 3.5 m above, eased like the zoom; it carries over room cuts
        float camLift,camLiftWant;const float LiftNotch=.15f, LiftDown=-1.2f, LiftUp=3.5f;
        // Shift + wheel swings the camera left or right round the room's aim point (wheel up = left): 5 degrees a notch, at most 60 either way, eased; it also carries over room cuts
        float camSwing,camSwingWant;const float SwingNotch=5f, SwingMax=60f;
        void SwingNotchBy(int dir){if(Peeping){peepSlideWant=Mathf.Clamp(peepSlideWant-dir*PeepSlideNotch,-PeepSlideMax,PeepSlideMax);peepTouched=Time.unscaledTime;return;}if(insideOn){inSwingWant=Mathf.Repeat(inSwingWant+dir*SwingNotch,360);Debug.Log("ROOMPLAY_CAMSWING inside deg="+inSwingWant.ToString("0",CultureInfo.InvariantCulture));return;}camSwingWant=Mathf.Clamp(camSwingWant+dir*SwingNotch,-SwingMax,SwingMax);Debug.Log("ROOMPLAY_CAMSWING deg="+camSwingWant.ToString("0",CultureInfo.InvariantCulture));}
        // Alt + wheel tilts the view up or down with the eye where it is (wheel up = look up): 2 degrees a notch, at most 30 either way, eased; it also carries over room cuts
        float camTilt,camTiltWant;const float TiltNotch=2f, TiltMax=30f, TiltUpMax=80f; // Alt + wheel: up to 80 deg up (the ceiling and its lights), 30 deg down
        void TiltNotchBy(int dir){if(Peeping){peepTiltWant=Mathf.Clamp(peepTiltWant+dir*2f,-PeepTiltMax,PeepTiltMax);peepTouched=Time.unscaledTime;return;}if(insideOn){inTiltWant=Mathf.Clamp(inTiltWant+dir*TiltNotch,-TiltMax,TiltUpMax);Debug.Log("ROOMPLAY_CAMTILT inside deg="+inTiltWant.ToString("0",CultureInfo.InvariantCulture));return;}camTiltWant=Mathf.Clamp(camTiltWant+dir*TiltNotch,-TiltMax,TiltUpMax);Debug.Log("ROOMPLAY_CAMTILT deg="+camTiltWant.ToString("0",CultureInfo.InvariantCulture));}
        // Shift + arrows move the same camera values while held: Shift+left/right swings (60 deg/s, left = wheel up), Shift+up/down raises or lowers the camera (1.2 m/s, up = higher);
        // a tap under 0.15 s still moves one notch. The arrows' normal jobs (turning, zoom) are off while Shift is held.
        const float ShiftSwingRate=60f, ShiftLiftRate=1.2f, ShiftTapSeconds=.15f, ShiftLogGap=.25f;
        int shSwDir,shTiDir;float shSwHold,shTiHold,shSwMoved,shTiMoved,shSwLog,shTiLog;
        void AddSwing(float deg){if(Peeping){peepPanWant=Mathf.Clamp(peepPanWant+deg,-PeepPanMax,PeepPanMax);peepTouched=Time.unscaledTime;return;}if(insideOn)inSwingWant=Mathf.Repeat(inSwingWant+deg,360);else camSwingWant=Mathf.Clamp(camSwingWant+deg,-SwingMax,SwingMax);}
        void AddLift(float m){if(Peeping){peepTiltWant=Mathf.Clamp(peepTiltWant+m*12f,-PeepTiltMax,PeepTiltMax);peepTouched=Time.unscaledTime;return;}if(insideOn)inLiftWant=Mathf.Clamp(inLiftWant+m,InsideLiftMin,InsideLiftMax);else camLiftWant=Mathf.Clamp(camLiftWant+m,LiftDown,LiftUp);}
        void LogSwing(){Debug.Log("ROOMPLAY_CAMSWING "+(insideOn?"inside ":"")+"deg="+(insideOn?inSwingWant:camSwingWant).ToString("0",CultureInfo.InvariantCulture)+" shift=1");}
        void LogLift(){Debug.Log("ROOMPLAY_CAMLIFT "+(insideOn?"inside height=":"lift=")+(insideOn?inLiftWant:camLiftWant).ToString("0.00",CultureInfo.InvariantCulture)+" shift=1");}
        void ShiftArrowTick(float dt,int sx,int ty)
        {
            if(sx!=shSwDir){
                if(shSwDir!=0 && shSwHold<ShiftTapSeconds && shSwMoved<SwingNotch){AddSwing(shSwDir*(SwingNotch-shSwMoved));LogSwing();} // a short tap is at least one notch
                else if(shSwDir!=0)LogSwing();
                shSwDir=sx;shSwHold=0;shSwMoved=0;shSwLog=0;
            }
            if(sx!=0){float step=ShiftSwingRate*dt;AddSwing(sx*step);shSwHold+=dt;shSwMoved+=step;shSwLog+=dt;if(shSwLog>=ShiftLogGap){shSwLog=0;LogSwing();}}
            if(ty!=shTiDir){
                if(shTiDir!=0 && shTiHold<ShiftTapSeconds && shTiMoved<LiftNotch){AddLift(shTiDir*(LiftNotch-shTiMoved));LogLift();}
                else if(shTiDir!=0)LogLift();
                shTiDir=ty;shTiHold=0;shTiMoved=0;shTiLog=0;
            }
            if(ty!=0){float step=ShiftLiftRate*dt;AddLift(ty*step);shTiHold+=dt;shTiMoved+=step;shTiLog+=dt;if(shTiLog>=ShiftLogGap){shTiLog=0;LogLift();}}
        }
        void LiftNotchBy(int dir){if(Peeping){peepLiftWant=Mathf.Clamp(peepLiftWant+dir*LiftNotch,-PeepLiftMax,PeepLiftMax);peepTouched=Time.unscaledTime;return;}if(insideOn){inLiftWant=Mathf.Clamp(inLiftWant+dir*LiftNotch,InsideLiftMin,InsideLiftMax);Debug.Log("ROOMPLAY_CAMLIFT inside height="+inLiftWant.ToString("0.00",CultureInfo.InvariantCulture));return;}camLiftWant=Mathf.Clamp(camLiftWant+dir*LiftNotch,LiftDown,LiftUp);Debug.Log("ROOMPLAY_CAMLIFT lift="+camLiftWant.ToString("0.00",CultureInfo.InvariantCulture)+" eye="+(curEye.y+camLiftWant).ToString("0.00",CultureInfo.InvariantCulture));}
        // the studio's peeping camera: outside the south window across the street (OneRoom.PeepEye, ODDITY_PEEP="x,y,z,fov" tunes it). Arrows and the wheel zoom (fov 30 -> 6, like binoculars), Shift + left/right pans (+-25 deg, left positive),
        // Shift + up/down (or Alt + wheel) tilts (+-15 deg), Ctrl + wheel raises or lowers the camera (+-1.5 m), Shift + wheel slides it along the street (+-3 m). With a narrow lens (fov under 15) it re-aims by itself to keep the steered one in frame, unless the player panned in the last 2 s
        bool Peeping=>one && !insideOn;
        Vector3 peepEye=OneRoom.PeepEye,peepAim=OneRoom.PeepAim;float peepPan,peepPanWant,peepTilt,peepTiltWant,peepSlide,peepSlideWant,peepLift,peepLiftWant,peepTouched=-10,peepAutoW,peepLog;Vector3 peepFocus;bool peepFocusSet;
        const float PeepFovFar=30f,PeepFovNear=6f,PeepPanMax=25f,PeepTiltMax=15f,PeepSlideMax=3f,PeepSlideNotch=.3f,PeepLiftMax=1.5f,PeepAutoFov=15f,PeepAutoFull=9f,PeepIdle=2f;
        void PeepInit(Camera cam)
        {
            float fov0=OneRoom.PeepFov;var env=Environment.GetEnvironmentVariable("ODDITY_PEEP");
            if(!string.IsNullOrWhiteSpace(env)){var v=env.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();if(v.Length>=3)peepEye=new Vector3(v[0],v[1],v[2]);if(v.Length>=4)fov0=v[3];}
            roomZoom=Mathf.Clamp01(Mathf.Log(fov0/PeepFovFar)/Mathf.Log(PeepFovNear/PeepFovFar));
            cam.fieldOfView=fov0;cam.transform.position=peepEye;cam.transform.LookAt(peepAim);
            Debug.Log("ROOMPLAY_PEEP init eye=("+F(peepEye.x)+","+F(peepEye.y)+","+F(peepEye.z)+") aim=("+F(peepAim.x)+","+F(peepAim.y)+","+F(peepAim.z)+") fov="+F(fov0));
        }
        void PeepFrame(float dt)
        {
            peepPan=Mathf.MoveTowards(peepPan,peepPanWant,dt*40f);peepTilt=Mathf.MoveTowards(peepTilt,peepTiltWant,dt*25f);peepSlide=Mathf.MoveTowards(peepSlide,peepSlideWant,dt*3f);peepLift=Mathf.MoveTowards(peepLift,peepLiftWant,dt*2f);
            float fov=PeepFovFar*Mathf.Pow(PeepFovNear/PeepFovFar,roomZoom);
            var eye=new Vector3(peepEye.x+peepSlide,peepEye.y+peepLift,peepEye.z);
            Vector3 want;
            if(ctl!=null)want=ctl.root.position+Vector3.up*.15f;
            else{var chest=anim!=null?(anim.GetBoneTransform(HumanBodyBones.Chest)??anim.GetBoneTransform(HumanBodyBones.Hips)):null;want=chest!=null?chest.position:new Vector3(x,1.1f,z);}
            if(!peepFocusSet){peepFocus=want;peepFocusSet=true;}
            if(ctl!=null)peepFocus=SlackFollow(peepFocus,want,eye,fov,RoomSlack,4,dt); // a cat's root shifts with every head and paw move (ground snap, spot fit): the aim only follows once the cat leaves the slack circle, like Harin's aim in the old room
            else peepFocus=Vector3.Lerp(peepFocus,want,1-Mathf.Exp(-4*dt));
            float wAuto=Mathf.SmoothStep(0,1,Mathf.InverseLerp(PeepAutoFov,PeepAutoFull,fov)); // 0 above fov 15, full at 9 and below
            peepAutoW=Mathf.MoveTowards(peepAutoW,Time.unscaledTime-peepTouched>PeepIdle?wAuto:0,dt/.6f);
            var aim=Vector3.Lerp(peepAim,peepFocus,peepAutoW);
            var rot=Quaternion.AngleAxis(-peepPan,Vector3.up)*Quaternion.LookRotation(aim-eye,Vector3.up);
            rot=Quaternion.AngleAxis(-peepTilt,rot*Vector3.right)*rot;
            cameraView.fieldOfView=fov;cameraView.transform.SetPositionAndRotation(eye,rot);
            if(Time.unscaledTime-peepLog>1f){peepLog=Time.unscaledTime;Debug.Log("ROOMPLAY_PEEP fov="+F(fov)+" pan="+F(peepPan)+" tilt="+F(peepTilt)+" cam=("+F(eye.x)+","+F(eye.y)+","+F(eye.z)+") auto="+F(peepAutoW));}
        }
        const float RoomFovNear=5f, RoomSlack=.3f, RoomFollow=5f, InsideCatSlack=.15f;
        // The aim stays put while the target is inside a circle of `ratio` of the half frame around it, and eases after it once it leaves (never closer than the circle's edge)
        public static Vector3 SlackFollow(Vector3 focus,Vector3 want,Vector3 eye,float fov,float ratio,float rate,float dt)
        {
            float slack=Vector3.Distance(eye,focus)*Mathf.Tan(fov*.5f*Mathf.Deg2Rad)*ratio;var off=want-focus;float m=off.magnitude;
            return m>slack?Vector3.Lerp(focus,focus+off*(1-slack/m),1-Mathf.Exp(-rate*dt)):focus;
        }
        Vector3 insideFocus;Pet insideFocusWho;bool insideFocusSet;
        void RoomFrame(float dt,float zoomKey)
        {
            float outZoom=roomZoom;if(insideOn)roomZoom=insideZoom; // the inside camera has its own zoom; the outside one comes back untouched
            roomZoom=Mathf.Clamp01(roomZoom-zoomKey*dt*.55f);
            if(wheelZoom!=0){float take=Mathf.MoveTowards(wheelZoom,0,dt*WheelRate);roomZoom=Mathf.Clamp01(roomZoom+wheelZoom-take);wheelZoom=take;if(roomZoom<=0 || roomZoom>=1)wheelZoom=0;} // each notch eases in over about 0.1 s
            if(insideOn){insideZoom=roomZoom;roomZoom=outZoom;}
            if(apt)AptTick();
            if(insideOn){InsideFrame(dt);return;}
            if(one){PeepFrame(dt);return;}
            camLift=Mathf.MoveTowards(camLift,camLiftWant,dt*1.5f);camSwing=Mathf.MoveTowards(camSwing,camSwingWant,dt*60f);camTilt=Mathf.MoveTowards(camTilt,camTiltWant,dt*24f);
            var eye=curAim+Quaternion.AngleAxis(camSwing,Vector3.up)*(curEye+Vector3.up*camLift-curAim);
            float fov=curFov*Mathf.Pow(RoomFovNear/curFov,roomZoom); // even steps from the room's lens (46 degrees in the old room) to 5
            Transform chest=null,neck=null;if(anim!=null){chest=anim.GetBoneTransform(HumanBodyBones.Chest)??anim.GetBoneTransform(HumanBodyBones.Hips);neck=anim.GetBoneTransform(HumanBodyBones.Head);}
            bool onCat=ctl!=null;
            if(chest!=null || onCat){
                var want=onCat?ctl.root.position+Vector3.up*Mathf.Lerp(.15f,.2f,Mathf.InverseLerp(14,RoomFovNear,fov)):neck!=null?Vector3.Lerp(chest.position,neck.position,Mathf.InverseLerp(14,RoomFovNear,fov)):chest.position;
                if(!roomFocusSet){roomFocus=want;roomFocusSet=true;lastWant=want;}
                wantPath+=Vector3.Distance(want,lastWant);lastWant=want;var wasFocus=roomFocus;
                float slack=Vector3.Distance(eye,roomFocus)*Mathf.Tan(fov*.5f*Mathf.Deg2Rad)*RoomSlack;
                var off=want-roomFocus;float m=off.magnitude;
                if(m>slack)roomFocus=Vector3.Lerp(roomFocus,roomFocus+off*(1-slack/m),1-Mathf.Exp(-RoomFollow*dt));
                focusPath+=Vector3.Distance(roomFocus,wasFocus);
            }
            var aim=(chest!=null || onCat)?Vector3.Lerp(curAim,roomFocus,Mathf.SmoothStep(0,1,Mathf.Min(1,roomZoom*2.5f))):curAim;
            cameraView.fieldOfView=fov;cameraView.transform.position=eye;cameraView.transform.LookAt(aim);
            if(camTilt!=0)cameraView.transform.rotation=Quaternion.AngleAxis(-camTilt,cameraView.transform.right)*cameraView.transform.rotation;
            if(orbitOn)OrbitCamera(dt);
        }
        // L: head and eyes turn to the camera (the player), up to 70 degrees, blended over 0.3 s
        bool lookOn;float lookW;
        void LookAtPlayer(float dt)
        {
            lookW=Mathf.MoveTowards(lookW,lookOn?1:0,dt/.3f);
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null){vrm.LookAtTargetType=VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;vrm.LookAtTarget=lookW>.5f?cameraView.transform:null;}
            if(lookW<=0)return;
            var head=anim.GetBoneTransform(HumanBodyBones.Head);var neck=anim.GetBoneTransform(HumanBodyBones.Neck);if(head==null)return;
            var face=head.rotation*headFront;var want=cameraView.transform.position-head.position;
            Quaternion.FromToRotation(face,want).ToAngleAxis(out float angle,out var axis);if(angle>180)angle-=360;
            angle=Mathf.Clamp(angle,-70,70)*lookW;
            if(neck!=null)neck.rotation=Quaternion.AngleAxis(angle*.4f,axis)*neck.rotation;
            head.rotation=Quaternion.AngleAxis(angle*(neck!=null?.6f:1),axis)*head.rotation;
        }
        Vector3 headFront=Vector3.forward;Quaternion headFrameRest=Quaternion.identity;float nap; // headFrameRest: the head frame of the imported T-pose, taken before any clip runs and handed to the rigs that measure the nose
        static readonly float NapTurn=float.TryParse(Environment.GetEnvironmentVariable("ODDITY_NAPTURN"),NumberStyles.Float,CultureInfo.InvariantCulture,out var t)?t:0, NapLift=0; // the sleep clip's own facing and hip height on the seat; set from the test shots
        // H on a dining chair: she leans in and props her chin on both hands, elbows toward the table
        bool chinOn;float chinW;
        const float TableTop=.70f;
        void ChinRest(float dt)
        {
            bool can=chinOn && (current==SitIndex || current==CurlIndex) && !onFloor && seatAt.y>.5f; // only the chairs sit this high
            chinW=Mathf.MoveTowards(chinW,can?1:0,dt/.4f);if(chinW<=0)return;
            var spine=anim.GetBoneTransform(HumanBodyBones.Spine);var chest=anim.GetBoneTransform(HumanBodyBones.Chest);
            var fwd=anim.transform.forward;var right=anim.transform.right;
            if(spine!=null)spine.rotation=Quaternion.AngleAxis(14*chinW,right)*spine.rotation;
            if(chest!=null)chest.rotation=Quaternion.AngleAxis(10*chinW,right)*chest.rotation;
            var head=anim.GetBoneTransform(HumanBodyBones.Head);if(head==null)return;
            var chin=head.position+fwd*.06f-Vector3.up*.09f;
            for(int side=0;side<2;side++){
                float sign=side==0?-1:1; // left, right
                var upper=anim.GetBoneTransform(side==0?HumanBodyBones.LeftUpperArm:HumanBodyBones.RightUpperArm);
                var lower=anim.GetBoneTransform(side==0?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
                var hand=anim.GetBoneTransform(side==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
                if(upper==null || lower==null || hand==null)continue;
                var target=chin+right*sign*.045f;
                var flat=Vector3.ProjectOnPlane(chin,Vector3.up);
                var elbow=new Vector3(flat.x,TableTop+.02f,flat.z)+fwd*.05f+right*sign*.13f;
                TwoBone(upper,lower,hand,target,elbow,chinW);
            }
        }
        // turns upper then lower so the wrist reaches target with the elbow bent toward pole, by weight w
        static void TwoBone(Transform upper,Transform lower,Transform hand,Vector3 target,Vector3 pole,float w)
        {
            Vector3 s=upper.position,e=lower.position,h=hand.position;
            float a=(e-s).magnitude,b=(h-e).magnitude;var d=target-s;float len=Mathf.Clamp(d.magnitude,Mathf.Abs(a-b)+1e-3f,a+b-1e-3f);var dir=d.normalized;
            var bend=Vector3.ProjectOnPlane(pole-s,dir);if(bend.sqrMagnitude<1e-6f)bend=Vector3.ProjectOnPlane(e-s,dir);bend.Normalize();
            float cosA=Mathf.Clamp((a*a+len*len-b*b)/(2*a*len),-1,1);var elbowAt=s+dir*a*cosA+bend*a*Mathf.Sqrt(1-cosA*cosA);
            upper.rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(e-s,elbowAt-s),w)*upper.rotation;
            e=lower.position;h=hand.position;
            lower.rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(h-e,s+dir*len-e),w)*lower.rotation;
        }
        void BuildMotion()
        {
            for(int hs=0;hs<2;hs++){ // the T-pose: palms down, fingers along the arm (for SitHands; the loaded rig has no finger bones)
                var hb=anim.GetBoneTransform(hs==0?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);var lb=anim.GetBoneTransform(hs==0?HumanBodyBones.LeftLowerArm:HumanBodyBones.RightLowerArm);
                if(hb==null || lb==null)continue;
                handFingersLocal[hs]=Quaternion.Inverse(hb.rotation)*(hb.position-lb.position).normalized;handPalmLocal[hs]=Quaternion.Inverse(hb.rotation)*Vector3.down;
            }
            graph=PlayableGraph.Create("RoomPlayMotion");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,MotionCount);
            AnimationPlayableOutput.Create(graph,"Harin",anim).SetSourcePlayable(mixer);
            clips=new AnimationClip[MotionCount];plays=new AnimationClipPlayable[MotionCount];anchors=new Vector3[MotionCount];travels=new Vector3[MotionCount];weights=new float[MotionCount];from=new float[MotionCount];
            for(int i=0;i<MotionCount;i++){
                try{clips[i]=i==ZeroTwoIndex?ZeroTwoDance.CreateClip():i==SweepIndex?HairGestures.CreateSweepClip(clips[9]):i==TossIndex?HairGestures.CreateTossClip(clips[9]):RoomTestRunner.Clip(Keys[i].clip);}catch(Exception e){if(i==ZeroTwoIndex)throw;Debug.LogWarning("ROOMPLAY missing clip="+Keys[i].clip+(HairGestures.IsGesture(i)?" "+e.Message:""));continue;}
                plays[i]=AnimationClipPlayable.Create(graph,clips[i]);plays[i].SetApplyFootIK(false);
                graph.Connect(plays[i],0,mixer,i);mixer.SetInputWeight(i,1);graph.Evaluate(0);
                anchors[i]=anim.GetBoneTransform(HumanBodyBones.Hips).localPosition;
                plays[i].SetTime(clips[i].length);graph.Evaluate(0);
                travels[i]=anim.GetBoneTransform(HumanBodyBones.Hips).localPosition-anchors[i];travels[i].y=0;
                if(Keys[i].key=="f" && hands[0]!=null && hands[1]!=null){float most=0;for(int j=0;j<=60;j++){plays[i].SetTime(j*clips[i].length/60);graph.Evaluate(0);most=Mathf.Max(most,WristAngle(0),WristAngle(1));}
                    Debug.Log("ROOMPLAY_WRIST "+Keys[i].clip+" raw max="+F(most)+" deg, limit="+F(WristReach));}
                if(Keys[i].key=="8"){float most=0,at=0;for(int j=0;j<=60;j++){plays[i].SetTime(j*clips[i].length/60);graph.Evaluate(0);float b=Lean();if(b>most){most=b;at=j*clips[i].length/60;}}
                    bowPeak=most;Debug.Log("ROOMPLAY_BOW "+Keys[i].clip+" torso max="+F(most)+" deg at "+F(at)+" s of "+F(clips[i].length)+", deepened to "+F(BowDepth));}
                plays[i].SetTime(0);mixer.SetInputWeight(i,0);
            }
            if(clips[current]==null){int available=Array.FindIndex(clips,c=>c!=null);if(available<0)throw new Exception("no available motion clips");current=available;}
            try{MeasureHair();}catch(Exception e){Debug.LogWarning("ROOMPLAY_HAIR measure failed: "+e);}
            try{Array.Clear(lieAxisYaw,0,lieAxisYaw.Length);MeasureLie(NapIndex);MeasureLie(BedIndex);}catch(Exception e){Debug.LogWarning("ROOMPLAY_LIE measure failed: "+e);}
            speed=MeasureWalk(10);walkTwist=lastTwist;runSpeed=Mathf.Clamp(MeasureWalk(11),speed*1.5f,speed*3);runTwist=lastTwist;weights[current]=from[current]=1;blend=1;graph.Play();Evaluate(0);
        }
        const int IdleIndex=9;
        // the rigs are built right after BuildMotion evaluated the current clip; a model reloaded in the middle of V, K or C would hand them a bent head, hand and foot as their neutral.
        // LoadModel therefore builds the motion with the standing idle as the current clip (same as a model loaded standing) and puts the old clip back here
        void RestoreAfterRigs(int keep)
        {
            current=clips[keep]!=null?keep:IdleIndex;
            Array.Clear(weights,0,weights.Length);Array.Clear(from,0,from.Length);weights[current]=from[current]=1;blend=1;
        }
        float lastTwist;
        // some clips run with the whole body turned; the mean shoulder and hip line says by how much, Evaluate turns her back
        float MeasureWalk(int c)
        {
            if(clips[c]==null)return 0;
            var hips=anim.GetBoneTransform(HumanBodyBones.Hips);
            var feet=new[]{anim.GetBoneTransform(HumanBodyBones.LeftFoot),anim.GetBoneTransform(HumanBodyBones.RightFoot)};
            var legL=anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);var legR=anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);var armL=anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);var armR=anim.GetBoneTransform(HumanBodyBones.RightUpperArm);var across=Vector3.zero;
            const int n=120;var hp=new Vector3[n+1];var fp=new Vector3[2,n+1];float min=100;
            mixer.SetInputWeight(c,1);float dt=clips[c].length/n;
            for(int j=0;j<=n;j++){
                plays[c].SetTime(j*dt);graph.Evaluate(0);hp[j]=anim.transform.InverseTransformPoint(hips.position);
                across+=anim.transform.InverseTransformVector(legR.position-legL.position+armR.position-armL.position);
                for(int f=0;f<2;f++){fp[f,j]=anim.transform.InverseTransformPoint(feet[f].position);min=Mathf.Min(min,fp[f,j].y);}
            }
            across.y=0;var facing=Vector3.Cross(across,Vector3.up);lastTwist=Mathf.Atan2(facing.x,facing.z)*Mathf.Rad2Deg;
            Debug.Log("ROOMPLAY_TWIST "+Keys[c].clip+" body yaw "+F(lastTwist));
            var travel=hp[n]-hp[0];travel.y=0;var speeds=new List<float>();var plantedVelocities=new List<Vector3>();
            for(int f=0;f<2;f++)for(int j=1;j<=n;j++)if(fp[f,j].y<min+.02f && fp[f,j-1].y<min+.02f){
                var d=fp[f,j]-fp[f,j-1]-travel/n;d.y=0;speeds.Add(d.magnitude/dt);plantedVelocities.Add(d/dt);
            }
            speeds.Sort();float result=speeds.Count>0?speeds[speeds.Count/2]:travel.magnitude/clips[c].length;
            Debug.Log("ROOMPLAY_SPEED "+Keys[c].clip+" hips="+F(travel.magnitude/clips[c].length)+" planted="+F(result)+" m/s samples="+speeds.Count);
            var forward=travel.sqrMagnitude>1e-6f?travel.normalized:Vector3.forward;
            var slip=plantedVelocities.Select(v=>(v+forward*result).magnitude).OrderBy(v=>v).ToArray();
            Debug.Log("ROOMPLAY_SLIP sampled planted residual median="+(slip.Length>0?F(slip[slip.Length/2]):"n/a")+" m/s");
            mixer.SetInputWeight(c,0);
            return result;
        }
        // getting off a dining chair goes through the chair steps first; index is played when she is clear of it
        void Select(int index)
        {
            if(chairJob!=null)return; // the chair steps play out first
            if(chairSat!=null && index!=SitIndex && index!=CurlIndex){StartChairStand(index);return;}
            SelectRaw(index);
        }
        void SelectRaw(int index)
        {
            if(clips[index]==null || current==index)return;
            if(index==SitIndex || index==CurlIndex)sitHandLogged[0]=sitHandLogged[1]=false;
            Array.Copy(weights,from,weights.Length);current=index;blend=0;plays[index].SetTime(0);
            if(index==ZeroTwoIndex){zeroTwoPhase=0;zeroTwoAudioSeconds=0;}
        }
        void Turn(float target)
        {
            if(Mathf.Abs(Mathf.DeltaAngle(targetYaw,target))<.1f)return;
            turnStart=yaw;targetYaw=target;turnTime=0;
        }
        void Command(string key)
        {
            if(ctl==null && LifeControl.IsHarinKey(key))LifeInput("key "+key);
            if(FaceKeys.ScriptDigit(key)>=0){SetFace(FaceKeys.ScriptDigit(key));return;} // Shift+digit: the face only, before the body and cat keys
            if(key=="shiftleft"||key=="shiftright"||key=="shiftup"||key=="shiftdown"){Log(key);return;} // script only: held by Update for the step's seconds
            if((sofaOn!=SofaMode.None || sofaPend) && ctl==null && SofaBodyKeys.Contains(key))SofaStop("key "+key);
            if(coffeeOn && ctl==null && key!="q" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key=="e" || key.StartsWith("click")))CoffeeEnd("key "+key);
            if(curtainOn && ctl==null && key!="x" && key!="shiftx" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key=="e" || key=="q" || key.StartsWith("click")))CurtainEnd("key "+key);
            if((wakeOn || wakePend) && ctl==null && key!="comma" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key=="e" || key=="q" || key=="x" || key=="shiftx" || key.StartsWith("click")))WakeEnd("key "+key);
            if(workoutOn && ctl==null && key!="period" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key=="e" || key=="q" || key=="x" || key=="shiftx" || key.StartsWith("click")))WorkoutEnd("key "+key);
            if(ramenOn && ctl==null && key!="semicolon" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key=="e" || key=="q" || key=="x" || key=="shiftx" || key=="comma" || key=="period" || key.StartsWith("click")))RamenEnd("key "+key);
            if(mealOn && ctl==null && key!="quote" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key=="e" || key=="q" || key=="x" || key=="shiftx" || key=="comma" || key=="period" || key.StartsWith("click")))MealEnd("key "+key);
            if((laundryOn || laundryPend) && ctl==null && key!="slash" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key=="e" || key=="q" || key=="x" || key=="shiftx" || key=="comma" || key=="period" || key.StartsWith("click")))LaundryEnd("key "+key);
            if((mirrorOn || mirrorPend) && ctl==null && key!="lbracket" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key=="e" || key=="q" || key=="x" || key=="shiftx" || key=="comma" || key=="period" || key.StartsWith("click")))MirrorEnd("key "+key);
            if(catOn && ctl==null && key!="rbracket" && key!="shiftrbracket" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key=="e" || key=="q" || key=="x" || key=="shiftx" || key=="comma" || key=="period" || key.StartsWith("click")))CatPlayEnd("key "+key);
            if(callOn && ctl==null && key!="e" && (SofaBodyKeys.Contains(key) || key=="p" || key=="t" || key=="b" || key=="lietv" || key.StartsWith("click")))CallEnd("key "+key);
            if(key.StartsWith("cam") && StillCamKey(key)){return;}
            if(key=="o"){ToggleCatSfx();return;}
            if(key=="i"){ToggleInside();return;}
            if(key.StartsWith("insideswing") && float.TryParse(key.Substring(11),NumberStyles.Float,CultureInfo.InvariantCulture,out var isw)){if(insideOn){inSwingWant=inSwing=Mathf.Repeat(isw,360);Debug.Log("ROOMPLAY_CAMSWING inside deg="+F(inSwingWant));}return;} // a still: the inside swing set to N degrees
            if(key=="realtab"){StartCoroutine(RealTab());return;}
            if(key.StartsWith("click")){ScriptClick(key);return;}
            if(key=="orbit"){StartOrbit();return;}
            if(key.StartsWith("orbita") && float.TryParse(key.Substring(6),NumberStyles.Float,CultureInfo.InvariantCulture,out var oa)){if(!orbitOn)StartOrbit();orbitAuto=false;orbitAngle=Apartment.OrbitFacingYaw+oa;return;} // a still: N degrees round from the start
            if(key.StartsWith("view")){AptForce(key.Substring(4));return;} // script only: a room's camera by name (viewkitchen), viewauto goes back to following the character
            if(key.StartsWith("gi") && int.TryParse(key.Substring(2),out var gp)){giWant=Mathf.Clamp(gp,0,100);return;} // script only: GI equalization in percent
            if(key=="freeze"||key=="unfreeze"){poseFreeze=key=="freeze";return;} // script only: hold Harin's pose
            if(key.StartsWith("shade") && int.TryParse(key.Substring(5),out var pc)){shadeWant=Mathf.Clamp(pc,40,100);return;} // script only: Harin's shade in percent
            if(key=="tiltup"||key=="tiltdown"){TiltNotchBy(key=="tiltup"?1:-1);return;} // Alt + wheel, one notch per script step
            if(key=="swingleft"||key=="swingright"){SwingNotchBy(key=="swingleft"?1:-1);return;} // Shift + wheel, one notch per script step
            if(key=="liftup"||key=="liftdown"){LiftNotchBy(key=="liftup"?1:-1);return;} // Ctrl + wheel, one notch per script step
            if(key=="wheelup"||key=="wheeldown"){wheelZoom=Mathf.Clamp(wheelZoom+(key=="wheelup"?WheelNotch:-WheelNotch),-1,1);return;} // one notch per script step
            if(ctl!=null && key=="r"){ReloadPet(ctl);return;} // R on a controlled cat reads its newest files again
            if(ctl!=null && key!="tab" && key!="escape" && key!="m"){CatCommand(key);return;} // the cat has its own keys while it is controlled
            if(key=="n"){bool back=Keyboard.current!=null && Keyboard.current.shiftKey.isPressed;if(!busy)StartCoroutine(Reload(StepLine(back?-1:1),false));}
            else if(key=="r"){if(!busy)StartCoroutine(Reload(StepLine(0),true));}
            else if(key=="space"){if(direction==0)Select(9);}
            else if(key=="l"){lookOn=!lookOn;Notice(lookOn?"주인공 바라보기":"바라보기 끝");}
            else if(key=="h"){chinOn=!chinOn;Notice(chinOn?"턱 괴기 (식탁 의자에서)":"턱 괴기 끝");}
            else if(key=="p"||key=="t"||key=="b"||key=="lietv"){if(direction==0 && !busy){if(key=="lietv" || (key=="t" && Keyboard.current!=null && Keyboard.current.shiftKey.isPressed))LieTvKey();else SofaKey(key=="p"?SofaMode.Phone:key=="t"?SofaMode.Tv:SofaMode.Bed);}}
            else if(key=="e"){if(direction==0 && !busy)CallKey();}
            else if(key=="q"){if(direction==0 && !busy)CoffeeKey();}
            else if(key=="comma"){if(direction==0 && !busy)WakeKey();}
            else if(key=="period"){if(direction==0 && !busy)WorkoutKey();}
            else if(key=="semicolon"){if(direction==0 && !busy)RamenKey();}
            else if(key=="quote"){if(direction==0 && !busy)MealKey();}
            else if(key=="slash"){if(direction==0 && !busy)LaundryKey();}
            else if(key=="lbracket"){if(direction==0 && !busy)MirrorKey();}
            else if(key=="rbracket" || key=="shiftrbracket"){if(direction==0 && !busy)CatPlayKey(key=="shiftrbracket" || (Keyboard.current!=null && Keyboard.current.shiftKey.isPressed));}
            else if(key=="x" || key=="shiftx"){if(direction==0 && !busy)CurtainKey(key=="shiftx" || (Keyboard.current!=null && Keyboard.current.shiftKey.isPressed));}
            else if(key=="k"){if(direction==0 && !busy){if(Keyboard.current!=null && Keyboard.current.shiftKey.isPressed)NextSit(NapIndex);else Sit(NapIndex,true);}}
            else if(key=="c"||key=="v"){if(direction==0 && !busy){int index=key=="c"?SitIndex:CurlIndex;
                if(Keyboard.current!=null && Keyboard.current.shiftKey.isPressed)NextSit(index);else Sit(index);}}
            else if(key=="g"){if(direction==0 && !busy){int i=(Array.IndexOf(Walks,Keys[10].clip)+1)%Walks.Length;SetWalk(Walks[i]);try{LoadModel(modelPath);Notice("걷기 "+Walks[i]);}catch(Exception e){Notice("걷기 실패: "+e.Message);}}}
            else if(key=="m")NextCat();
            else if(key=="tab")ToggleControl();
            else if(key=="escape")EditorApplication.ExitPlaymode();
            else if(key=="left"||key=="right"){heading+=key=="left"?45:-45;if(direction==0)Turn(heading);}
            else if(direction==0){int i=Array.FindIndex(Keys,k=>k.key==key);if(i>=0 && (i<9 || key=="f" || key=="z" || key=="0" || key=="y"))Select(i);else if(i>=0 && HairGestures.IsGesture(i))StartGesture(i);}
            Log(key);
        }
        void Log(string key){Debug.Log("ROOMPLAY key="+key+" clip="+Keys[current].clip+" x="+F(x)+" z="+F(z)+" yaw="+F(yaw)+" heading="+F(heading)+" zoom="+F(zoom)+" floor="+F(floorError)+(room?" fov="+F(cameraView.fieldOfView)+" bodyPath="+F(wantPath)+" aimPath="+F(focusPath):""));wantPath=focusPath=0;}
        const string DefaultLine="6T-O2", IndoorLine="6T-I2";
        static readonly string[] HiddenLines={"6T-I1","6T-O1"}; // hidden for now (I1, O1 2026-11-07): left out of the N / Shift+N cycle, still loadable with ODDITY_MODEL
        // the newest imported vrm of a line in Assets/Models/test, as an asset path
        static string NewestInTest(string line)
        {
            var f=Directory.GetFiles(Path.Combine(Application.dataPath,"Models/test"),"harin-"+(line??"6T-*")+"-*.vrm").OrderByDescending(p=>int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(),out var n)?n:-1).FirstOrDefault();
            return f==null?null:"Assets/Models/test/"+Path.GetFileName(f);
        }
        // N / Shift+N walk round every line (6T-I1 -> 6T-O1 -> 6T-O2 -> ... -> 6T-I1), each at its newest build; 0 stays on the current line
        string StepLine(int step)
        {
            var dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../hari/gpt"));
            var lines=Directory.GetFiles(dir,"harin-6T-*-*.vrm").Concat(Directory.GetFiles(Path.Combine(Application.dataPath,"Models/test"),"harin-6T-*-*.glb")).Select(p=>Path.GetFileNameWithoutExtension(p).Split('-')).Where(a=>a.Length==4).Select(a=>a[1]+"-"+a[2]).Where(l=>!HiddenLines.Contains(l)).Distinct()
                .OrderBy(l=>l[3]=='I'?0:l[3]=='O'?1:2).ThenBy(l=>int.TryParse(l.Substring(4),out var n)?n:0).ToList();
            var parts=(modelName??"").Split('-');string here=parts.Length==4?parts[1]+"-"+parts[2]:DefaultLine;
            if(lines.Count==0)return here;
            int at=lines.IndexOf(here);if(at<0)return lines[0];
            return lines[((at+step)%lines.Count+lines.Count)%lines.Count];
        }
        IEnumerator Reload(string line,bool force)
        {
            busy=true;zeroTwoInputReady=false;held=false;
            zeroTwoAudio?.SetState(current==ZeroTwoIndex,false,zeroTwoAudioSeconds);
            string latest=null;
            try{
                var dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../hari/gpt"));
                latest=Directory.GetFiles(dir,"harin-"+line+"-*.vrm").OrderByDescending(p=>int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(),out var n)?n:-1).FirstOrDefault();
                latest??=Directory.GetFiles(Path.Combine(Application.dataPath,"Models/test"),"harin-"+line+"-*.glb").OrderByDescending(p=>int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(),out var n)?n:-1).FirstOrDefault(); // a line with only a bare mesh so far
                if(latest==null)throw new Exception("no "+line+" model");
            }catch(Exception e){Notice("모델 탐색 실패: "+e.Message);busy=false;yield break;}
            var name=Path.GetFileNameWithoutExtension(latest);
            if(name==modelName && !force){Notice("이미 최신 "+name.Replace("harin-",""));busy=false;yield break;}
            Notice(name.Replace("harin-","")+" 불러오는 중");yield return null;yield return new WaitForEndOfFrame();
            var timer=System.Diagnostics.Stopwatch.StartNew();
            try{
                string asset="Assets/Models/test/"+Path.GetFileName(latest);
                var copy=Path.Combine(Application.dataPath,"Models/test",Path.GetFileName(latest));
                if(Path.GetFullPath(latest)!=Path.GetFullPath(copy))File.Copy(latest,copy,true);
                AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                LoadModel(asset);long ms=timer.ElapsedMilliseconds;Notice("불러오기 완료 "+name.Replace("harin-","")+" "+ms.ToString("N0",CultureInfo.InvariantCulture)+" ms");
                Debug.Log("ROOMPLAY_RELOAD model="+modelName+" seconds="+F((float)timer.Elapsed.TotalSeconds)+" ms="+ms+" playing="+EditorApplication.isPlaying+" x="+F(x)+" clip="+Keys[current].clip);
            }catch(Exception e){Notice("불러오기 실패: "+e.Message);}
            busy=false;
        }
        void Notice(string text){message=text;messageUntil=Time.realtimeSinceStartup+5;Debug.Log("ROOMPLAY "+text);}
        // Tab hands control between Harin and the pets. It is polled at the top of every Update, before any early return (statue, busy, reviewing, no model, scripted), through the Input System,
        // and again from OnGUI because the editor's Game view can take Tab for IMGUI focus navigation before the Input System reports it. One press switches once (0.15 s dedupe, OnGUI auto-repeat ignored)
        // O: every cat sound on or off (one-shots and purrs), remembered between runs in EditorPrefs; ODDITY_CATSFX=0|1 overrides it
        const string CatSfxPref="Oddity.RoomPlay.CatSfx";
        bool catSfxOn=true; // read from EditorPrefs in Start (EditorPrefs cannot be called from a field initializer)
        static bool CatSfxInitial(){var e=PetEnv("ODDITY_CATSFX");if(e=="0")return false;if(e=="1")return true;return EditorPrefs.GetBool(CatSfxPref,true);}
        void ToggleCatSfx()
        {
            catSfxOn=!catSfxOn;
            if(PetEnv("ODDITY_CATSFX")==null && !scripted)EditorPrefs.SetBool(CatSfxPref,catSfxOn); // a scripted run never changes the user's saved choice
            foreach(var p in pets){
                if(p.shot!=null && !catSfxOn)p.shot.Stop();
                if(p.purr!=null && !catSfxOn){p.purr.Stop();p.purr.volume=0;}
                p.purrOn=false;p.purrTarget=0;p.idleTime=0; // back on: a purr starts again only after a cat has rested 2-3 s
            }
            Notice(catSfxOn?"고양이 소리 켬":"고양이 소리 끔");
            Debug.Log("ROOMPLAY_CATSFX enabled="+(catSfxOn?1:0));
        }
        float lastTabAt=-10;bool tabGuiDown;
        void TabPress(string source)
        {
            if(scripted && !tabWindow){Debug.Log("ROOMPLAY_TABKEY source="+source+" ignored (scripted run, no realtab step)");return;} // a stray real Tab must not change a scripted run
            if(Time.realtimeSinceStartup-lastTabAt<.15f)return;
            lastTabAt=Time.realtimeSinceStartup;
            Debug.Log("ROOMPLAY_TABKEY source="+source);
            ToggleControl();
        }
        bool tabPolled;
        void TabPoll()
        {
            var kb=Keyboard.current;
            bool down=kb!=null && kb.tabKey.isPressed;
            if(down && !tabPolled)TabPress("inputsystem"); // an edge on the held state, so it does not depend on which Input System update saw the event
            tabPolled=down;
            if(kb!=null && kb.oKey.wasPressedThisFrame)ToggleCatSfx();
        }
        // scripted "realtab": a real Tab press and release queued as Input System state, so it reaches TabPoll the way a keyboard does
        // The Input System drops keyboard events while the Game view is not focused (editorInputBehaviorInPlayMode / backgroundBehavior), which is always so in a started-by-script editor, so for this one press the
        // settings are switched to "everything goes to the game view" and put back afterwards. A real user keeps the default settings: there Tab arrives through OnGUI (logged source=ongui).
        bool tabWindow;
        IEnumerator RealTab()
        {
            var settings=InputSystem.settings;var editorBefore=settings.editorInputBehaviorInPlayMode;var backBefore=settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            var kb=Keyboard.current??InputSystem.AddDevice<Keyboard>();
            tabWindow=true;
            Debug.Log("ROOMPLAY_REALTAB press focused="+Application.isFocused+" device="+kb.name+" current="+(Keyboard.current==kb));
            InputSystem.QueueStateEvent(kb,new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.Tab));
            yield return null;yield return null;
            InputSystem.QueueStateEvent(kb,new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return null;yield return null;
            tabWindow=false;settings.editorInputBehaviorInPlayMode=editorBefore;settings.backgroundBehavior=backBefore;
        }
        // a cat looking at the camera: after the animation, its neck (40%) and head turn toward the camera, at most 70 degrees sideways and 35 up or down from the body's heading, eased over 0.25 s
        void LateUpdate(){foreach(var p in pets)CatLook(p,Mathf.Min(Time.unscaledDeltaTime,.05f));}
        void CatLook(Pet p,float dt)
        {
            if(p.headB==null || p.root==null || cameraView==null)return;
            var head=p.headB;
            if(!p.lookCal && p.lookW<=0 && (p.sit==0 && !p.moving && p.job==0 || p.lookOn)){p.headFront=head.InverseTransformDirection(p.root.forward);p.lookCal=true;} // the head's nose axis, taken from a calm pose where the head faces the body's heading
            p.lookW=Mathf.MoveTowards(p.lookW,p.lookOn?1:0,dt/.25f);
            if(p.lookW<=0)return;
            var want=cameraView.transform.position-head.position;if(want.sqrMagnitude<1e-6f)return;
            var local=p.root.InverseTransformDirection(want.normalized);
            float yaw=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-70,70),pitch=Mathf.Clamp(Mathf.Asin(Mathf.Clamp(local.y,-1,1))*Mathf.Rad2Deg,-35,35);
            var dir=p.root.TransformDirection(Quaternion.Euler(-pitch,yaw,0)*Vector3.forward);
            var neck=head.parent;
            if(neck!=null && neck!=p.root){var d1=Quaternion.FromToRotation(head.rotation*p.headFront,dir);neck.rotation=Quaternion.Slerp(Quaternion.identity,d1,.4f*p.lookW)*neck.rotation;}
            var d2=Quaternion.FromToRotation(head.rotation*p.headFront,dir);head.rotation=Quaternion.Slerp(Quaternion.identity,d2,p.lookW)*head.rotation;
        }
        void Update()
        {
            if(cameraView!=null){float aspect=Screen.width/(float)Mathf.Max(1,Screen.height);float target=16f/9;
                cameraView.rect=aspect>target?new Rect((1-target/aspect)/2,0,target/aspect,1):new Rect(0,(1-aspect/target)/2,1,aspect/target);}
            TabPoll();
            PetsTick(Mathf.Min(Time.unscaledDeltaTime,.05f));
            if(statue!=null && !busy){StatueUpdate(Mathf.Min(Time.unscaledDeltaTime,.05f));return;}
            ShadeTick();
            if(model==null || busy || reviewing)return;float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);var input=Vector2.zero;bool run=false;float zoomKey=0;int camSx=0,camTy=0;
            turning=0;
            if(scripted){
                stepTime-=dt;
                if(step>=0 && !stepSnapped && stepTime<=dt*1.01f){stepSnapped=true;Snap();} // the shot is of the last frame of the step, before the next key changes anything
                if(step<0 || stepTime<=0){
                    stepSnapped=false;
                    step++;if(step>=script.Count){Debug.Log("ROOMPLAY_ROLL planted foot tilt most raw="+F(rollRaw)+" kept="+F(rollKept)+" deg seated toe lowest="+F(toeLow));Debug.Log("ROOMPLAY_DONE");EditorApplication.ExitPlaymode();enabled=false;return;}
                    stepTime=script[step].seconds;
                    if(direction!=0 && !Walk(script[step].key)){direction=0;Select(9);Turn(heading);}
                    if(!Walk(script[step].key))Command(script[step].key);
                }
                var sk=script[step].key;
                input=WalkDir(sk);run=sk=="shift"||sk=="shifts";zoomKey=sk=="up"?-1:sk=="down"?1:0;
                camSx=sk=="shiftleft"?1:sk=="shiftright"?-1:0;camTy=sk=="shiftup"?1:sk=="shiftdown"?-1:0;
            }else{
                var kb=Keyboard.current;if(kb==null){zeroTwoInputReady=false;zeroTwoAudio?.SetState(current==ZeroTwoIndex,false,zeroTwoAudioSeconds);return;}
                input=new Vector2((kb.dKey.isPressed?1:0)-(kb.aKey.isPressed?1:0),(kb.wKey.isPressed?1:0)-(kb.sKey.isPressed?1:0));run=kb.shiftKey.isPressed;zoomKey=(kb.downArrowKey.isPressed?1:0)-(kb.upArrowKey.isPressed?1:0);
                turning=(kb.leftArrowKey.isPressed?1:0)-(kb.rightArrowKey.isPressed?1:0);
                if(kb.shiftKey.isPressed){camSx=turning;camTy=(kb.upArrowKey.isPressed?1:0)-(kb.downArrowKey.isPressed?1:0);turning=0;zoomKey=0;} // Shift + arrows move the camera instead
                var mouse=Mouse.current;if(mouse!=null && mouse.leftButton.wasPressedThisFrame && room){var mp=mouse.position.ReadValue();if(ClickInGame(mp)){if(ctl==null)LifeInput("click");ClickAt(cameraView.ScreenPointToRay(mp));}else Debug.Log("ROOMPLAY_CLICK ignored at=("+F(mp.x)+","+F(mp.y)+") screen="+Screen.width+"x"+Screen.height+" over="+(EditorWindow.mouseOverWindow!=null?EditorWindow.mouseOverWindow.GetType().Name:"none"));}
                if(mouse!=null){var sc=mouse.scroll.ReadValue();float wheel=sc.y!=0?sc.y:kb.shiftKey.isPressed?sc.x:0; // Windows may turn Shift + wheel into a sideways scroll
                    if(wheel!=0){if(kb.altKey.isPressed)TiltNotchBy(wheel>0?1:-1);else if(kb.shiftKey.isPressed)SwingNotchBy(wheel>0?1:-1);else if(kb.ctrlKey.isPressed)LiftNotchBy(wheel>0?1:-1);else wheelZoom=Mathf.Clamp(wheelZoom+Mathf.Sign(wheel)*WheelNotch,-1,1);}} // wheel up zooms in, Ctrl + wheel up raises the camera
                foreach(var entry in Keys)if(!Walk(entry.key) && entry.key!="left" && entry.key!="right" && entry.key!="tab" && entry.key!="o"){ // Tab and O have their own poll (TabPoll)
                    bool digit=char.IsDigit(entry.key[0]);
                    if(kb[KeyOf(entry.key)].wasPressedThisFrame){
                        if(digit && kb.shiftKey.isPressed)Command("face"+entry.key); // Shift+digit is a face, not the digit's body motion
                        else if(input==Vector2.zero || !digit)Command(entry.key);
                    }
                }
                // an action moves while its key is held and holds the pose the moment it is let go; space returns to the idle
                bool action=current<9 || current==12 || current==ZeroTwoIndex || current==IdolIndex || current==YawnIndex;bool pressed=action && kb[KeyOf(Keys[current].key)].isPressed;
                if(action && held && !pressed)Log("hold");held=pressed;
            }
            ShiftArrowTick(dt,camSx,camTy);
            if(ctl==null && input!=Vector2.zero)LifeInput("walk");
            LifeTick(dt);
            if(ctl!=null){CatTick(dt,input,run,turning);input=Vector2.zero;run=false;turning=0;} // WASD and shift drive the cat; Harin keeps her pose
            if(busy)return;
            zeroTwoInputReady=true;
            if(input!=Vector2.zero && clickGoal.HasValue){clickGoal=null;clickSeat=null;} // the keys take over from a click
            if(chairJob==null && chairSat!=null && input!=Vector2.zero)StartChairStand(-1); // walking off a dining chair
            if(callOn && input!=Vector2.zero)CallEnd("walk key");
            if(coffeeOn && input!=Vector2.zero)CoffeeEnd("walk key");
            if(curtainOn && input!=Vector2.zero)CurtainEnd("walk key");
            if((wakeOn || wakePend) && input!=Vector2.zero)WakeEnd("walk key");
            if(workoutOn && input!=Vector2.zero)WorkoutEnd("walk key");
            if(ramenOn && input!=Vector2.zero)RamenEnd("walk key");
            if(mealOn && input!=Vector2.zero)MealEnd("walk key");
            if((laundryOn || laundryPend) && input!=Vector2.zero)LaundryEnd("walk key");
            if((mirrorOn || mirrorPend) && input!=Vector2.zero)MirrorEnd("walk key");
            if(catOn && input!=Vector2.zero)CatPlayEnd("walk key");
            CurtainTick(dt);
            WakeTick(dt);
            WorkoutTick(dt);
            RamenTick(dt);
            MealTick(dt);
            LaundryTick(dt);
            MirrorTick(dt);
            CatPlayTick(dt);
            CoffeeTick(dt);
            CallTick(dt);
            var goWish=chairJob==null?ClickStep(dt):Vector2.zero;
            if(chairJob!=null){ChairTick(dt);turnTime+=dt/.2f;yaw=Mathf.LerpAngle(turnStart,targetYaw,Mathf.SmoothStep(0,1,turnTime));heading=targetYaw;Evaluate(dt);Frame(dt,zoomKey);return;}
            run=run && clips[11]!=null;float pace=run?runSpeed:speed;
            var wish=input.normalized;if(goWish!=Vector2.zero){wish=goWish;run=false;pace=speed;}else if(room && wish!=Vector2.zero){var fw=MoveFwd();var rt=new Vector3(fw.z,0,-fw.x);var w3=rt*wish.x+fw*wish.y;wish=new Vector2(w3.x,w3.z);} // W walks away from the camera
            var next=new Vector2(Mathf.Clamp(x+wish.x*pace*dt,LimX0,LimX1),Mathf.Clamp(z+wish.y*pace*dt,ZNear,ZFar));
            next=Collide(new Vector2(x,z),next);next=HarinBodyBlock(new Vector2(x,z),next);
            if((next-new Vector2(x,z)).sqrMagnitude<1e-10f||clips[10]==null)wish=Vector2.zero;
            int dir=wish==Vector2.zero?0:run?2:1;
            // letting go of the walk or run keys holds her in that stride (space returns to the idle); a scripted run still goes back to the idle
            if(dir!=direction){if(dir==0 && !scripted)heading=targetYaw;direction=dir;if(dir!=0 || scripted)Select(dir==0?9:dir==2?11:10);Log(dir==0?"stop":dir==2?"run":"walk");}
            if(current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex)turning=0; // arrows would turn her off the seat
            if(dir==0 && turning!=0){heading+=turning*TurnRate*dt;yaw=turnStart=targetYaw=heading;turnTime=1;} // held arrows turn her smoothly, letting go stops her there
            Turn(dir==0?heading:Mathf.Atan2(wish.x,wish.y)*Mathf.Rad2Deg);
            turnTime+=dt/.2f;yaw=Mathf.LerpAngle(turnStart,targetYaw,Mathf.SmoothStep(0,1,turnTime));
            if(dir!=0){x=next.x;z=next.y;}
            Evaluate(dt);
            Frame(dt,zoomKey);
        }
        // ODDITY_PLAYSHOTS=1: a scripted run saves the game view at the end of each step -> shots/room_test/play/
        bool stepSnapped;
        void Snap()
        {
            if(step<0 || Environment.GetEnvironmentVariable("ODDITY_PLAYSHOTS")!="1")return;
            var dir=Path.GetFullPath("../shots/room_test/play");Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir,step.ToString("00")+"_"+script[step].key+".png"));
            var sp=cameraView.transform.position;Debug.Log("ROOMPLAY_SNAP step="+step+" coffee="+(coffeeOn&&coffeeRig!=null?coffeeRig.Phase+" headDown="+F(coffeeRig.HeadDownDegrees):"-")+" laundry="+(laundryOn&&laundryRig!=null?laundryRig.Phase:"-")+" meal="+(mealOn&&mealRig!=null?mealRig.Phase+"@"+F(mealRig.Progress)+(mealRig.BiteIsDrink?"D":"")+"b"+mealRig.BiteNumber:"-")+" ramen="+(ramenOn&&ramenRig!=null?ramenRig.Phase:"-")+" cat="+(catOn&&catRig!=null?catRig.Phase:"-")+" key="+script[step].key+" cam=("+F(sp.x)+","+F(sp.y)+","+F(sp.z)+") fov="+F(cameraView.fieldOfView)+" inside="+(insideOn?1:0));
        }
        IEnumerator ReviewZeroTwo()
        {
            var dir=Path.GetFullPath("../shots/room_test/zero_two");Directory.CreateDirectory(dir);
            scripted=false;held=true;Select(ZeroTwoIndex);Evaluate(.05f);
            var audio=cameraView.GetComponent<AudioSource>();bool started=audio!=null && audio.isPlaying;
            if(started)Debug.Log("ZEROTWO_AUDIO start PASS");else Debug.LogError("ZEROTWO_AUDIO start FAIL");
            held=false;Evaluate(0);float frozenPhase=zeroTwoPhase,frozenBlend=blend,frozenAudioSeconds=zeroTwoAudioSeconds;
            var bones=Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>().Where(b=>b!=HumanBodyBones.LastBone).Select(b=>anim.GetBoneTransform(b)).Where(t=>t!=null).ToArray();
            Vector3[] frozen=null;
            var phases=new[]{zeroTwoPhase,zeroTwoPhase,0f,ZeroTwoMusic.Duration/16,ZeroTwoMusic.Duration/8,ZeroTwoMusic.Duration*3/16,ZeroTwoMusic.Duration/4,ZeroTwoMusic.Duration*5/16};
            for(int i=0;i<phases.Length;i++){
                if(i==1){for(int j=0;j<30;j++)Evaluate(.05f);float movement=bones.Select((b,j)=>Vector3.Distance(b.position,frozen[j])).Max();bool stopped=zeroTwoPhase==frozenPhase && blend==frozenBlend;string result="ZEROTWO_FREEZE max_bone_delta="+movement.ToString("R",CultureInfo.InvariantCulture)+" phase="+zeroTwoPhase.ToString("R",CultureInfo.InvariantCulture)+" blend="+blend.ToString("R",CultureInfo.InvariantCulture);if(movement>=.0001f || !stopped)Debug.LogError(result+" FAIL");else Debug.Log(result+" PASS");if(audio!=null && !audio.isPlaying && stopped)Debug.Log("ZEROTWO_AUDIO pause PASS");else Debug.LogError("ZEROTWO_AUDIO pause FAIL");}
                else if(i>=2){held=true;blend=1;zeroTwoPhase=phases[i];Evaluate(0);}
                if(i==0)frozen=bones.Select(b=>b.position).ToArray();
                // Keep the skeleton and phase fixed while VRM's spring simulation
                // settles after a phase jump; these are real rendered frames.
                if(i>=2)for(int settle=0;settle<24;settle++)yield return null;
                yield return new WaitForEndOfFrame();
                var rt=RenderTexture.GetTemporary(800,450,24);var old=cameraView.targetTexture;var active=RenderTexture.active;var rect=cameraView.rect;
                var image=new Texture2D(800,450,TextureFormat.RGB24,false);
                try{cameraView.rect=new Rect(0,0,1,1);cameraView.targetTexture=rt;cameraView.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,800,450),0,0);image.Apply();File.WriteAllBytes(Path.Combine(dir,modelName+"_"+i.ToString("00")+".png"),image.EncodeToPNG());}
                finally{cameraView.targetTexture=old;cameraView.rect=rect;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);Destroy(image);}
                var head=anim.GetBoneTransform(HumanBodyBones.Head);
                Debug.Log("ZEROTWO_POSE frame="+i+" phase="+F(zeroTwoPhase)+" left_hand_head_dy="+F(hands[0].position.y-head.position.y)+" right_hand_head_dy="+F(hands[1].position.y-head.position.y)+" foot_gap="+F(Vector3.Distance(feet[0].position,feet[1].position)));
                if(i==1){held=true;Evaluate(.05f);bool resumed=audio!=null && audio.isPlaying && Mathf.Abs(zeroTwoPhase-Mathf.Repeat(frozenPhase+ZeroTwoDance.MotionSecondsBetween(frozenAudioSeconds,.05f),ZeroTwoMusic.Duration))<.00001f;if(resumed)Debug.Log("ZEROTWO_AUDIO resume PASS");else Debug.LogError("ZEROTWO_AUDIO resume FAIL");}
            }
            if(audio!=null && audio.clip!=null && audio.clip.samples/(double)audio.clip.frequency>70)
                yield return ReviewZeroTwoFullTrack(audio,bones);
            Debug.Log("ZEROTWO_REVIEW_DONE");EditorApplication.ExitPlaymode();
        }
        IEnumerator ReviewZeroTwoFullTrack(AudioSource audio,Transform[] bones)
        {
            // Change only the music clock: the last review pose must stay fixed.
            var pose=bones.Select(b=>b.position).ToArray();
            float posePhase=zeroTwoPhase;
            zeroTwoAudioSeconds=5.25f;held=true;Evaluate(0);
            float movement=bones.Select((b,i)=>Vector3.Distance(b.position,pose[i])).Max();
            float seek=audio.timeSamples/(float)audio.clip.frequency;
            bool seekOk=Mathf.Abs(seek-5.25f)<.15f && zeroTwoPhase==posePhase && movement<.0001f;
            string diagnostic="ZEROTWO_AUDIO full_track seconds="+F(audio.clip.samples/(float)audio.clip.frequency)+" seek="+F(seek)+" pose_delta="+F(movement);
            if(seekOk)Debug.Log(diagnostic+" PASS");else Debug.LogError(diagnostic+" FAIL");
            held=false;Evaluate(0);int pausedSample=audio.timeSamples;
            for(int i=0;i<8;i++){yield return null;Evaluate(.05f);}
            bool paused=!audio.isPlaying && audio.timeSamples==pausedSample && zeroTwoAudioSeconds==5.25f && zeroTwoPhase==posePhase;
            if(paused)Debug.Log("ZEROTWO_AUDIO full_track pause PASS");else Debug.LogError("ZEROTWO_AUDIO full_track pause FAIL");
            held=true;Evaluate(.05f);
            bool resumed=audio.isPlaying && Mathf.Abs(zeroTwoAudioSeconds-5.30f)<.0001f && Mathf.Abs(audio.timeSamples/(float)audio.clip.frequency-5.30f)<.15f;
            if(resumed)Debug.Log("ZEROTWO_AUDIO full_track resume PASS");else Debug.LogError("ZEROTWO_AUDIO full_track resume FAIL");
        }
        // a slight smile in the idle (key 0, space), a held smile in every other clip; it eases in and out over 0.3 s
        const float SmileWeight=.7f; // relaxed, the closed-mouth smile the room test films use
        float smile;
        // the idle (key 0, space) keeps a slight smile of weight 0.5 by default; ODDITY_SMILE=w changes it (0 = blank face)
        static readonly float IdleSmile=float.TryParse(Environment.GetEnvironmentVariable("ODDITY_SMILE"),NumberStyles.Float,CultureInfo.InvariantCulture,out var s)?Mathf.Clamp01(s):.5f;
        // Shift+1 .. Shift+0: one custom expression held at weight 1 (fades in over 0.2 s, the old one fades out) until cleared; the automatic smile fades out under it
        string faceHeld;readonly Dictionary<string,float> faceW=new Dictionary<string,float>();readonly HashSet<string> faceWarned=new HashSet<string>();
        void SetFace(int digit)
        {
            faceHeld=FaceKeys.Press(digit,faceHeld);
            if(faceHeld!=null)Debug.Log("ROOMPLAY_FACE set="+faceHeld);else Debug.Log("ROOMPLAY_FACE clear");
            Notice(faceHeld!=null?"표정 "+faceHeld:"표정 원래대로");
        }
        void FaceTick(Vrm10Instance vrm,float dt)
        {
            foreach(var name in FaceKeys.Names){
                faceW.TryGetValue(name,out float w);w=Mathf.MoveTowards(w,name==faceHeld?1:0,dt/.2f);faceW[name]=w;
                var key=ExpressionKey.CreateCustom(name);
                if(!vrm.Runtime.Expression.ExpressionKeys.Contains(key)){if(name==faceHeld && faceWarned.Add(name))Debug.LogWarning("ROOMPLAY_FACE model has no expression "+name);continue;}
                vrm.Runtime.Expression.SetWeight(key,w);
            }
        }
        void Smile(float dt)
        {
            var vrm=model.GetComponent<Vrm10Instance>();if(vrm==null || vrm.Runtime==null)return;
            float smileWant=faceHeld!=null?0:current==9 || HairGestures.IsGesture(current)?IdleSmile:SmileWeight;if(faceHeld==null && sofaRig!=null && sofaRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,sofaRig.Smile,Mathf.Clamp01(sofaRig.Weight*2));if(faceHeld==null && callRig!=null && callRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,Mathf.Max(.3f,callRig.Smile*SmileWeight),Mathf.Clamp01(callRig.Weight*2));if(faceHeld==null && coffeeRig!=null && coffeeRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,Mathf.Max(.3f,coffeeRig.Smile*SmileWeight),Mathf.Clamp01(coffeeRig.Weight*2));if(faceHeld==null && curtainRig!=null && curtainRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,Mathf.Max(IdleSmile,curtainRig.Smile*SmileWeight),Mathf.Clamp01(curtainRig.Weight*2));if(faceHeld==null && workoutRig!=null && workoutRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,Mathf.Max(IdleSmile,workoutRig.Smile),Mathf.Clamp01(workoutRig.Weight*2));if(faceHeld==null && ramenRig!=null && ramenRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,Mathf.Max(IdleSmile,ramenRig.Smile*SmileWeight),Mathf.Clamp01(ramenRig.Weight*2));if(faceHeld==null && mealRig!=null && mealRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,Mathf.Max(IdleSmile,mealRig.Smile*SmileWeight),Mathf.Clamp01(mealRig.Weight*2));if(faceHeld==null && laundryRig!=null && laundryRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,Mathf.Max(IdleSmile,laundryRig.Smile*SmileWeight),Mathf.Clamp01(laundryRig.Weight*2));if(faceHeld==null && mirrorRig!=null && mirrorRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,Mathf.Max(IdleSmile,mirrorRig.Smile*SmileWeight),Mathf.Clamp01(mirrorRig.Weight*2));if(faceHeld==null && catRig!=null && catRig.Weight>.01f)smileWant=Mathf.Lerp(smileWant,Mathf.Max(IdleSmile,catRig.Smile*SmileWeight),Mathf.Clamp01(catRig.Weight*2));smile=Mathf.MoveTowards(smile,smileWant,dt*SmileWeight/.3f);
            FaceTick(vrm,dt);
            vrm.Runtime.Expression.SetWeight(ExpressionKey.Relaxed,smile);
            nap=Mathf.MoveTowards(nap,current==NapIndex && !lieTv?1:0,dt/.6f); // eyes open while she watches the TV lying downvrm.Runtime.Expression.SetWeight(ExpressionKey.Blink,Mathf.Max(nap,sofaRig!=null?sofaRig.Blink*Mathf.Clamp01(sofaRig.Weight*2):0,coffeeRig!=null?coffeeRig.EyesClosed:0,curtainRig!=null?curtainRig.EyesClosed:0,wakeRig!=null?wakeRig.EyesClosed:0,workoutRig!=null?workoutRig.EyesClosed:0,ramenRig!=null?ramenRig.EyesClosed:0,mealRig!=null?mealRig.EyesClosed:0,laundryRig!=null?laundryRig.EyesClosed:0,mirrorRig!=null?mirrorRig.EyesClosed*MirrorSquintBlink:0,catRig!=null?catRig.EyesClosed:0,clips[TossIndex]!=null?HairGestures.TossBlink((float)plays[TossIndex].GetTime())*weights[TossIndex]:0)); // asleep: eyes shut; the head toss blinks once
        }
        // J hair sweep and U head toss: one shot each, back to the idle (space) by themselves; pressing again restarts, walking or another key cancels
        void StartGesture(int index)
        {
            if(clips[index]==null)return;
            if(current==index){GestureEnd("restart");plays[index].SetTime(0);gestureOn=index;Debug.Log("ROOMPLAY_HAIR start="+(index==SweepIndex?"sweep":"toss"));return;}
            Select(index);
        }
        void GestureEnd(string why){if(gestureOn>=0)Debug.Log("ROOMPLAY_HAIR end="+(gestureOn==SweepIndex?"sweep":"toss")+" "+why);gestureOn=-1;}
        void GestureTick()
        {
            int g=HairGestures.IsGesture(current)?current:-1;
            if(g!=gestureOn){GestureEnd("cancel");if(g>=0)Debug.Log("ROOMPLAY_HAIR start="+(g==SweepIndex?"sweep":"toss"));gestureOn=g;}
            if(g>=0 && !poseFreeze && plays[g].GetTime()>=clips[g].length-1e-4f){GestureEnd("done");Select(9);}
        }
        static void DestroyGestureClips(AnimationClip[] c){if(c==null)return;if(c.Length>SweepIndex && c[SweepIndex]!=null)Destroy(c[SweepIndex]);if(c.Length>TossIndex && c[TossIndex]!=null)Destroy(c[TossIndex]);}
        // plays both gestures once in the graph and logs how the hand clears the head (HAIRGESTURE ...) and how far the toss turns the head
        void MeasureHair()
        {
            if(hairRig==null)return;
            var head=anim.GetBoneTransform(HumanBodyBones.Head);var rig=hairRig;
            if(clips[SweepIndex]!=null){
                mixer.SetInputWeight(SweepIndex,1);rig.MinBone=rig.MinCenter=rig.MinMesh=float.MaxValue;rig.MaxPalmForward=-1;
                plays[SweepIndex].SetTime(0);graph.Evaluate(0);var rest=head.rotation;float tilt=0;
                for(float t=0;t<=HairGestures.SweepLength+1e-4f;t+=1f/60){plays[SweepIndex].SetTime(Mathf.Min(t,HairGestures.SweepLength));graph.Evaluate(0);rig.Apply(t,1);rig.Sample(t);tilt=Mathf.Max(tilt,Quaternion.Angle(rest,head.rotation));}
                Debug.Log("HAIRGESTURE sweep minDist="+F(rig.MinBone)+" headRadius="+F(rig.HeadRadius)+" minCenter="+F(rig.MinCenter)+" meshClear="+F(rig.MinMesh)+" radii="+F(rig.Radii.x)+","+F(rig.Radii.y)+","+F(rig.Radii.z)+" headTurnMax="+F(tilt)+" deg palmForwardMax="+F(rig.MaxPalmForward)+" at "+F(rig.PalmForwardAt)+" s");
                mixer.SetInputWeight(SweepIndex,0);plays[SweepIndex].SetTime(0);
            }
            if(clips[TossIndex]!=null){
                mixer.SetInputWeight(TossIndex,1);plays[TossIndex].SetTime(0);graph.Evaluate(0);var rest=head.rotation;var up0=head.up;float most=0,roll=0,nod=0;float t1=0;
                var rootT=anim.transform;
                for(float t=0;t<=HairGestures.TossLength+1e-4f;t+=1f/60){plays[TossIndex].SetTime(Mathf.Min(t,HairGestures.TossLength));graph.Evaluate(0);
                    float a=Quaternion.Angle(rest,head.rotation);if(a>most){most=a;t1=t;}
                    var side=Vector3.Dot(head.up,rootT.right)-Vector3.Dot(up0,rootT.right);roll=Mathf.Max(roll,Mathf.Abs(Mathf.Asin(Mathf.Clamp(side,-1,1))*Mathf.Rad2Deg));
                    var fwd=Vector3.Dot(head.up,rootT.forward)-Vector3.Dot(up0,rootT.forward);nod=Mathf.Max(nod,Mathf.Abs(Mathf.Asin(Mathf.Clamp(fwd,-1,1))*Mathf.Rad2Deg));}
                Debug.Log("HAIRGESTURE toss swingMax="+F(most)+" deg at "+F(t1)+" s, sideLean="+F(roll)+" deg, frontBack="+F(nod)+" deg, length="+F(clips[TossIndex].length)+" s");
                mixer.SetInputWeight(TossIndex,0);plays[TossIndex].SetTime(0);
            }
        }
        // the cat pose (key 2) mouths "meow" on its two paw strokes, without a sound: the mouth opens to aa and closes through ou
        static readonly (float from,float to)[] Meows={(.35f,.95f),(3.55f,4.15f)};
        void Meow()
        {
            var vrm=model.GetComponent<Vrm10Instance>();if(vrm==null || vrm.Runtime==null || clips[1]==null)return;
            float time=(float)plays[1].GetTime(),aa=0,ou=0;
            foreach(var (from,to) in Meows)if(time>from && time<to){
                float phase=(time-from)/(to-from);
                aa=Mathf.Sin(Mathf.PI*phase)*.75f;ou=phase>.4f?Mathf.Sin(Mathf.PI*(phase-.4f)/.6f)*.5f:0;
            }
            vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa,aa*weights[1]);vrm.Runtime.Expression.SetWeight(ExpressionKey.Ou,ou*weights[1]);
        }
        bool poseFreeze; // script key "freeze": every clip stops where it is (for side-by-side shots), "unfreeze" lets them run again
        bool Frozen(int i)=>poseFreeze || !scripted && i==current && ((i<9 || i==12 || i==ZeroTwoIndex || i==IdolIndex || i==YawnIndex) && !held || (i==10 || i==11) && direction==0);
        void Evaluate(float dt)
        {
            float faceDt=dt; // the face keeps real time while the body holds a frozen pose (Shift+digit faces, nap eyes, smile)
            if(Frozen(current))dt=0;
            blend=Mathf.Min(1,blend+dt/blendTime);Vector3 baseHip=Vector3.zero, drift=Vector3.zero;float sum=0;
            bool musicAdvancing=current==ZeroTwoIndex && !busy && (zeroTwoInputReady || reviewing) && !Frozen(ZeroTwoIndex);
            if(musicAdvancing && dt>0){zeroTwoPhase=Mathf.Repeat(zeroTwoPhase+ZeroTwoDance.MotionSecondsBetween(zeroTwoAudioSeconds,dt),ZeroTwoMusic.Duration);zeroTwoAudioSeconds+=dt;}
            else if(current!=ZeroTwoIndex && dt>0)zeroTwoPhase=Mathf.Repeat(zeroTwoPhase+dt,ZeroTwoMusic.Duration);
            for(int i=0;i<MotionCount;i++)if(clips[i]!=null){
                weights[i]=Mathf.Lerp(from[i],i==current?1:0,blend);mixer.SetInputWeight(i,weights[i]);
                plays[i].SetTime(i==ZeroTwoIndex?zeroTwoPhase:HairGestures.IsGesture(i)?Mathf.Min(clips[i].length,(float)plays[i].GetTime()+(Frozen(i)?0:dt)):(plays[i].GetTime()+(Frozen(i)?0:dt))%clips[i].length);baseHip+=anchors[i]*weights[i];drift+=travels[i]*(float)(plays[i].GetTime()/clips[i].length)*weights[i];sum+=weights[i];
            }
            bodyFix=walkTwist*weights[10]+runTwist*weights[11];
            model.transform.SetPositionAndRotation(new Vector3(x,0,z),Quaternion.Euler(0,yaw-bodyFix,0));graph.Evaluate(0);
            var hips=anim.GetBoneTransform(HumanBodyBones.Hips);var p=hips.localPosition;
            if(sum>0){p-=drift/sum;p.x=Mathf.Clamp(p.x,baseHip.x/sum-.2f,baseHip.x/sum+.2f);p.z=Mathf.Clamp(p.z,baseHip.z/sum-.2f,baseHip.z/sum+.2f);hips.localPosition=p;}
            model.transform.SetPositionAndRotation(new Vector3(x,0,z),Quaternion.Euler(0,yaw-bodyFix,0));
            DeepenBow();
            if(hairRig!=null && clips[SweepIndex]!=null)hairRig.Apply((float)plays[SweepIndex].GetTime(),weights[SweepIndex]);
            LimitWrists();
            if(weights[SitIndex]+weights[CurlIndex]+weights[NapIndex]+weights[BedIndex]<.5f)LevelFeet(dt);
            zeroTwoDance?.Apply(zeroTwoPhase,weights[ZeroTwoIndex]);
            zeroTwoAudio?.SetState(current==ZeroTwoIndex,musicAdvancing,zeroTwoAudioSeconds);
            Meow();
            Smile(faceDt);
            GestureTick();
            Ground(dt);
            PinToSeat();
            FeetClear();
            SitHands();
            ChinRest(dt);
            LookAtPlayer(dt);
            SofaTick(dt);
            CallApply(dt);
            CoffeeApply(dt);
            CurtainApply(dt);
            WakeApply(dt);
            WorkoutApply(dt);
            RamenApply(dt);
            MealApply(dt);
            LaundryApply(dt);
            MirrorApply(dt);
            CatPlayApply(dt);
            LieFinish();
            if(sofaRig!=null && sofaOn==SofaMode.Bed)sofaRig.FinishBed(); // the knee limit and the gaze from the real eyes to the real phone, after the body is on the quilt
            CurlBedFinish();
            if(sword!=null){sword.SetActive(current==12);shield.SetActive(current==12);if(current==12)PlaceShield();}
            if(blob!=null)blob.position=new Vector3(x,.004f,z+.02f);
        }
        // the retargeted sword clip folds the wrist past 90 degrees and the sleeve cuff stays behind; each hand keeps within WristReach of its rest, the sword follows
        void LimitWrists()
        {
            for(int i=0;i<2;i++)if(hands[i]!=null){
                var turn=Quaternion.Inverse(restHand[i])*hands[i].localRotation;float angle=WristAngle(i);
                if(angle>WristReach)hands[i].localRotation=restHand[i]*Quaternion.Slerp(Quaternion.identity,turn,WristReach/angle);
            }
        }
        // ROOMPLAY_SPRING: which spring chains run on the loaded model and which collider groups they test, so cloth fixes can be checked against the game
        void LogSprings(GameObject m)
        {
            var vrm=m.GetComponent<Vrm10Instance>();if(vrm==null){Debug.Log("ROOMPLAY_SPRING no Vrm10Instance");return;}
            var sb=vrm.SpringBone;
            Debug.Log("ROOMPLAY_SPRING model="+modelName+" update="+vrm.UpdateType+" springs="+sb.Springs.Count+" groups="+sb.ColliderGroups.Count);
            foreach(var g in sb.ColliderGroups)Debug.Log("ROOMPLAY_SPRING group "+g.Name+" "+string.Join(", ",g.Colliders.Select(c=>c.ColliderType+" r="+F(c.Radius)+" on "+c.transform.name)));
            foreach(var s in sb.Springs){var j=s.Joints.FirstOrDefault(x=>x!=null);
                Debug.Log("ROOMPLAY_SPRING chain "+s.Name+" joints="+s.Joints.Count+" groups=["+string.Join(",",s.ColliderGroups.Select(g=>g!=null?g.Name:"null"))+"] hit="+(j!=null?F(j.m_jointRadius):"-")+" stiff="+(j!=null?F(j.m_stiffnessForce):"-"));}
        }
        // the extra lean grows with the clip's own, so the bow keeps its timing and only goes deeper
        void DeepenBow()
        {
            int i=Array.FindIndex(Keys,k=>k.key=="8");var spine=anim.GetBoneTransform(HumanBodyBones.Spine);
            if(i<0 || clips[i]==null || spine==null || bowPeak<1 || weights[i]<=0)return;
            float extra=Lean()*(BowDepth/bowPeak-1)*weights[i];
            spine.rotation=Quaternion.AngleAxis(extra,anim.transform.right)*spine.rotation;
        }
        // degrees the hips-to-neck line leans off the vertical
        float Lean(){var hips=anim.GetBoneTransform(HumanBodyBones.Hips);var neck=anim.GetBoneTransform(HumanBodyBones.Neck);return Vector3.Angle(Vector3.up,neck.position-hips.position);}
        float WristAngle(int i)=>Quaternion.Angle(restHand[i],hands[i].localRotation);
        // like the bake: her lowest sole vertex rests on the floor every frame, the lift follows over 0.1 s
        static Key KeyOf(string key)=>key=="lbracket"?Key.LeftBracket:key=="rbracket"?Key.RightBracket:key=="semicolon"?Key.Semicolon:key=="quote"?Key.Quote:key=="slash"?Key.Slash:key=="comma"?Key.Comma:key=="period"?Key.Period:key=="tab"?Key.Tab:key=="escape"?Key.Escape:key=="space"?Key.Space:key=="left"?Key.LeftArrow:key=="right"?Key.RightArrow:key=="up"?Key.UpArrow:key=="down"?Key.DownArrow:char.IsDigit(key[0])?(Key)Enum.Parse(typeof(Key),"Digit"+key):(Key)Enum.Parse(typeof(Key),key.ToUpperInvariant());
        static bool Walk(string key)=>key=="shift"||key=="shifts"||key=="a"||key=="d"||key=="w"||key=="s";
        static Vector2 WalkDir(string key)=>key=="a"?Vector2.left:key=="d"||key=="shift"?Vector2.right:key=="w"?Vector2.up:key=="s"||key=="shifts"?Vector2.down:Vector2.zero;
        void RestFeet()
        {
            feet[0]=anim.GetBoneTransform(HumanBodyBones.LeftFoot);feet[1]=anim.GetBoneTransform(HumanBodyBones.RightFoot);
            toes[0]=anim.GetBoneTransform(HumanBodyBones.LeftToes);toes[1]=anim.GetBoneTransform(HumanBodyBones.RightToes);
            shins[0]=anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);shins[1]=anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            for(int i=0;i<2;i++)if(feet[i]!=null && toes[i]!=null){
                restToe[i]=toes[i].localRotation;
                if(shins[i]!=null){shinFront[i]=shins[i].InverseTransformDirection(anim.transform.forward);restAnkle[i]=Ankle(i);}
                restPitch[i]=LegPitch(i);
            }
            for(int i=0;i<2;i++)if(feet[i]!=null)footSide[i]=Quaternion.Inverse(feet[i].rotation)*anim.transform.right; // flat in the T-pose
            var headBone=anim.GetBoneTransform(HumanBodyBones.Head);if(headBone!=null){headFront=Quaternion.Inverse(headBone.rotation)*anim.transform.forward;headFrameRest=Quaternion.Inverse(headBone.rotation)*anim.transform.rotation;}
            hands[0]=anim.GetBoneTransform(HumanBodyBones.LeftHand);hands[1]=anim.GetBoneTransform(HumanBodyBones.RightHand);
            for(int i=0;i<2;i++)if(hands[i]!=null)restHand[i]=hands[i].localRotation;
        }
        // the clips are made for heeled shoes: a planted bare foot lies flat so the heel reaches the floor, a lifted foot and the run keep their pose
        // the walk points the lifted foot down hard, which reads as a flick; it stays mostly flat too
        float Flat(int i,float running)=>Mathf.Max(plant[i],weights[10]*.8f)*(1-running);
        void LevelFeet(float dt)
        {
            if(toes[0]==null || toes[1]==null || feet[0]==null || feet[1]==null)return;
            LimitKnees();
            AimFeet();
            float lower=Mathf.Min(toes[0].position.y,toes[1].position.y);float walking=weights[11];
            for(int i=0;i<2;i++){
                plant[i]=Mathf.MoveTowards(plant[i],1-Mathf.InverseLerp(.03f,.12f,toes[i].position.y-lower),dt/.15f); // a sudden release flicks the foot as it leaves the floor
                float over=LegPitch(i)-restPitch[i];if(over<=0)continue;
                TurnPitch(i,-over*Flat(i,walking));
                toes[i].localRotation=Quaternion.Slerp(toes[i].localRotation,restToe[i],Flat(i,walking)); // the heeled clips bend the toes up to meet the floor
            }
            LimitAnkles();
            LevelRoll();
        }
        static readonly bool NoRoll=Environment.GetEnvironmentVariable("ODDITY_NOROLL")=="1"; // the old feet, for a before and after
        float Roll(int i)=>Mathf.Asin(Mathf.Clamp((feet[i].rotation*footSide[i]).y,-1,1))*Mathf.Rad2Deg;
        // the cute walks roll the foot onto its outer edge and turning the toes in rolls it more, so the ankle reads as twisted;
        // the foot turns about its own heel-to-toe line until the sole lies level side to side, planted or in the air
        void LevelRoll()
        {
            for(int i=0;i<2;i++){
                var along=toes[i].position-feet[i].position;if(along.sqrMagnitude<1e-8f)continue;along.Normalize();
                if(Mathf.Abs(along.y)>.9f)continue; // toes straight down: no side to level
                float raw=Roll(i);if(plant[i]>.5f)rollRaw=Mathf.Max(rollRaw,Mathf.Abs(raw));
                var side=feet[i].rotation*footSide[i];var flat=Vector3.Cross(along,Vector3.up);if(Vector3.Dot(flat,side)<0)flat=-flat;
                float turn=Vector3.SignedAngle(Vector3.ProjectOnPlane(side,along),Vector3.ProjectOnPlane(flat,along),along);
                if(!NoRoll)feet[i].rotation=Quaternion.AngleAxis(turn,along)*feet[i].rotation;
                if(plant[i]>.5f)rollKept=Mathf.Max(rollKept,Mathf.Abs(Roll(i)));
            }
        }
        // how far the toes point down from level, seen from the side of that leg: 90 is straight down, past 90 they point back
        // (the old asin pitch read a foot past vertical as flatter again, so the flattening let go for a frame and the foot flicked)
        Vector3 LegFront(int i)=>shins[i]!=null?Vector3.ProjectOnPlane(shins[i].TransformDirection(shinFront[i]),Vector3.up).normalized:anim.transform.forward;
        float LegPitch(int i){var v=toes[i].position-feet[i].position;return Mathf.Atan2(-v.y,Vector3.Dot(v,LegFront(i)))*Mathf.Rad2Deg;}
        // turns the foot about the leg's side axis so LegPitch changes by delta
        void TurnPitch(int i,float delta)
        {
            var side=Vector3.Cross(Vector3.up,LegFront(i)).normalized;var before=feet[i].rotation;float from=LegPitch(i);
            feet[i].rotation=Quaternion.AngleAxis(delta,side)*before;
            if(Mathf.Abs(Mathf.DeltaAngle(LegPitch(i),from+delta))>Mathf.Abs(delta)*.5f)feet[i].rotation=Quaternion.AngleAxis(-delta,side)*before;
        }
        // the foot's angle from the line of the shin towards the front of that leg; past 90 the toes point back
        // the leg's front turns with the leg, so a dance step that turns the foot out still bends the ankle the right way
        float Ankle(int i)
        {
            var shin=(feet[i].position-shins[i].position).normalized;var front=Vector3.ProjectOnPlane(shins[i].TransformDirection(shinFront[i]),shin).normalized;
            var v=toes[i].position-feet[i].position;return Mathf.Atan2(Vector3.Dot(v,front),Vector3.Dot(v,shin))*Mathf.Rad2Deg;
        }
        // the cute walks turn the toes in; while walking each foot points along her heading, toes out by ToeOut
        const float ToeOut=8;
        // the knee cap and toe aim are for the stylised walks (KAWAII, catwalk); a plain walk keeps its own legs
        static bool Styled=>Keys[10].clip.StartsWith("Ka")||Keys[10].clip.StartsWith("Catwalk");
        void AimFeet()
        {
            float walk=weights[10];if(walk<=0 || !Styled)return;
            for(int i=0;i<2;i++){
                var h=Vector3.ProjectOnPlane(toes[i].position-feet[i].position,Vector3.up);if(h.sqrMagnitude<1e-6f)continue;
                var aim=Quaternion.AngleAxis(i==0?-ToeOut:ToeOut,Vector3.up)*Vector3.ProjectOnPlane(anim.transform.forward,Vector3.up);
                float turn=Vector3.SignedAngle(h,aim,Vector3.up);if(Mathf.Abs(turn)>120)continue; // a foot flicked back past the shin is LimitAnkles' job; KaWalk01 swings the leaving foot 70 → 90 degrees in, which must still be turned
                feet[i].rotation=Quaternion.AngleAxis(turn*walk,Vector3.up)*feet[i].rotation;
            }
        }
        void LimitKnees()
        {
            if(!Styled)return;float most=Mathf.Lerp(180,KneeWalk,weights[10]);if(most>=179)return;
            for(int i=0;i<2;i++){
                if(shins[i]==null)continue;var thigh=shins[i].parent;if(thigh==null)continue;
                var up=shins[i].position-thigh.position;var low=feet[i].position-shins[i].position;
                float bend=Vector3.Angle(up,low);if(bend<=most)continue;
                var axis=Vector3.Cross(low,up);if(axis.sqrMagnitude<1e-8f)continue;
                shins[i].rotation=Quaternion.AngleAxis(bend-most,axis.normalized)*shins[i].rotation;
            }
        }
        // the walk flicks the leaving foot past the line of the shin so the toes point backwards; a rigid shoe shows it as a broken ankle
        // Pitch cannot tell that foot from a flat one, so the ankle is measured against the shin in the leg's own side view
        void LimitAnkles()
        {
            for(int i=0;i<2;i++){
                if(shins[i]==null)continue;
                float lack=restAnkle[i]-Mathf.Lerp(SwingReach,AnkleReach,plant[i])-Ankle(i);if(lack<=0)continue;
                var shin=feet[i].position-shins[i].position;var side=Vector3.Cross(shin,shins[i].TransformDirection(shinFront[i])).normalized;
                var before=feet[i].rotation;feet[i].rotation=Quaternion.AngleAxis(lack,side)*before;
                if(restAnkle[i]-Mathf.Lerp(SwingReach,AnkleReach,plant[i])-Ankle(i)>lack)feet[i].rotation=Quaternion.AngleAxis(-lack,side)*before;
            }
        }
        void Ground(float dt)
        {
            if(soleMesh==null){
                foreach(var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if(renderer.sharedMesh!=null && (soleMesh==null || renderer.sharedMesh.bounds.min.y<soleMesh.sharedMesh.bounds.min.y))soleMesh=renderer;
                if(soleMesh==null)return;
                var rest=soleMesh.sharedMesh.vertices;float low=soleMesh.sharedMesh.bounds.min.y;
                soles=Enumerable.Range(0,rest.Length).Where(i=>rest[i].y<low+.25f).ToArray();
                lift=float.NaN;
            }
            if(soleBake==null)soleBake=new Mesh();
            soleMesh.BakeMesh(soleBake);soleBake.GetVertices(soleVerts);
            var matrix=soleMesh.transform.localToWorldMatrix;float lowest=float.PositiveInfinity;
            foreach(int i in soles)lowest=Mathf.Min(lowest,matrix.MultiplyPoint3x4(soleVerts[i]).y);
            lift=float.IsNaN(lift)?-lowest:dt<=0?lift:Mathf.SmoothDamp(lift,-lowest,ref liftSpeed,.1f,Mathf.Infinity,Mathf.Max(dt,.001f));
            model.transform.position=new Vector3(x,lift,z);
            floorError=Mathf.Max(floorError,Mathf.Abs(lift+lowest));
        }
        // the camera stays put on the wide ground; the down arrow dollies out to see her, the up arrow dollies in and centres on her
        // ODDITY_BEDCAM=x,y,z,tx,ty,tz,fov: a fixed still camera for the lying-pose pictures (eye, target, lens)
        static readonly float[] BedCamSet=ParseCam(Environment.GetEnvironmentVariable("ODDITY_BEDCAM"));
        static float[] ParseCam(string t){if(string.IsNullOrWhiteSpace(t))return null;var p=t.Split(',');if(p.Length!=7)return null;var r=new float[7];for(int i=0;i<7;i++)if(!float.TryParse(p[i],NumberStyles.Float,CultureInfo.InvariantCulture,out r[i]))return null;return r;}
        void BedCam()
        {
            if(BedCamSet==null || cameraView==null)return;
            var eye=new Vector3(BedCamSet[0],BedCamSet[1],BedCamSet[2]);var d=new Vector3(BedCamSet[3],BedCamSet[4],BedCamSet[5])-eye;
            cameraView.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(d,Mathf.Abs(d.normalized.y)>.95f?Vector3.forward:Vector3.up));cameraView.fieldOfView=BedCamSet[6];
        }
        void Frame(float dt,float zoomKey)
        {
            if(room){RoomFrame(dt,zoomKey);StillCam();BedCam();return;}
            zoom=Mathf.Clamp(zoom*Mathf.Exp(zoomKey*.8f*dt),1.2f,30f);
            float near=Mathf.InverseLerp(6.79f,2.6f,zoom);
            cameraView.transform.position=new Vector3(x*near,Mathf.Lerp(1.13f,1.54f,near),Mathf.Lerp(-zoom,z-zoom,near));
            cameraView.transform.rotation=Quaternion.Euler(Mathf.Lerp(3,-1.5f,Mathf.InverseLerp(6.79f,20f,zoom)),0,0); // the true horizon meets the painted one as she walks into the distance
        }
        internal static GameObject Part(PrimitiveType type,Transform parent,Vector3 pos,Vector3 size,Color color)
        {
            var go=GameObject.CreatePrimitive(type);Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;
            var r=go.GetComponent<Renderer>();r.material=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=color};
            return go;
        }
        // made while she is still in her T-pose: the blade runs along the thumb out of the fist, the shield sits on the back of the left hand
        // a shield rides on the outer face of the left forearm, the side away from her chest, whatever way the hand is turned
        void PlaceShield()
        {
            var elbow=anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);var wrist=anim.GetBoneTransform(HumanBodyBones.LeftHand);var chest=anim.GetBoneTransform(HumanBodyBones.Chest)??anim.GetBoneTransform(HumanBodyBones.Spine);
            if(shield==null || elbow==null || wrist==null || chest==null)return;
            var axis=(wrist.position-elbow.position).normalized;var mid=Vector3.Lerp(elbow.position,wrist.position,.6f);
            var outward=Vector3.ProjectOnPlane(mid-chest.position,axis);if(outward.sqrMagnitude<1e-6f)outward=anim.transform.right*-1;
            outward.Normalize();shield.transform.SetPositionAndRotation(mid+outward*.07f,Quaternion.LookRotation(axis,outward)); // the disc's thin axis (y) points outward
        }
        void Arm()
        {
            var right=anim.GetBoneTransform(HumanBodyBones.RightHand);var rightFinger=anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);var rightThumb=anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            var left=anim.GetBoneTransform(HumanBodyBones.LeftHand);var leftFinger=anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);var leftThumb=anim.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
            sword=shield=null;
            if(right==null || left==null || rightFinger==null || rightThumb==null || leftFinger==null || leftThumb==null)return;
            var f=(rightFinger.position-right.position).normalized;var blade=Vector3.ProjectOnPlane(rightThumb.position-right.position,f).normalized;
            sword=new GameObject("Sword");sword.transform.SetPositionAndRotation(right.position+f*.08f,Quaternion.LookRotation(blade,f));sword.transform.SetParent(right,true);
            Part(PrimitiveType.Cube,sword.transform,new Vector3(0,0,.42f),new Vector3(.05f,.008f,.72f),new Color(.82f,.85f,.9f));
            Part(PrimitiveType.Cube,sword.transform,new Vector3(0,0,.05f),new Vector3(.18f,.025f,.03f),new Color(.75f,.62f,.25f));
            Part(PrimitiveType.Cube,sword.transform,new Vector3(0,0,-.03f),new Vector3(.03f,.03f,.15f),new Color(.2f,.12f,.08f));
            shield=new GameObject("Shield");PlaceShield(); // not parented: PlaceShield keeps it on the outside of the forearm every frame
            Part(PrimitiveType.Cylinder,shield.transform,Vector3.zero,new Vector3(.5f,.012f,.5f),new Color(.45f,.27f,.13f));
            Part(PrimitiveType.Cylinder,shield.transform,new Vector3(0,.015f,0),new Vector3(.12f,.012f,.12f),new Color(.78f,.8f,.84f));
        }
        static readonly string[] CatCommonKeys={"space  멈추기 (앉았거나 누웠으면 일어서기)","W A S D  걷기, Shift  달리기 (2배)","← →  제자리 돌기","↑ ↓, 휠  줌, Ctrl+휠  카메라 높이","Shift+휠  카메라 좌우, Alt+휠  기울기","Shift+← →  카메라 좌우 돌기, Shift+↑ ↓  카메라 높이","Z 둥글게 말기, X 식빵 졸기, C 옆으로 말고 자기","V 느슨하게 뻗기, B 손님 고양이 (앉아서 앞발 핥기), K 잠자기 (누를 때마다 다음 자세)","클릭  바닥, 소파, 식탁, 조리대, 냉장고, 창","M  다음 동작, R  다시 불러오기, L  카메라 보기","O  고양이 소리 켜고 끄기","I  방 안 카메라 (다시 누르면 밖)","Tab  하린으로"};
        static string CatRow(Pet p,int i){const string keys="123456789";return i<ActionNames.Length?keys[i]+"  "+ActionNames[i]+(CanDo(p,i)?"":" (없음)"):CatCommonKeys[i-ActionNames.Length];}
        static readonly bool noHud=Environment.GetEnvironmentVariable("ODDITY_NOHUD")=="1" || Environment.GetEnvironmentVariable("ODDITY_ORBIT")=="1";
        // script-only still cameras (for the shots): cam1..cam9 take "ex,ey,ez,ax,ay,az,fov" from the environment ODDITY_CAM1..9; camphone / camface / camback are placed from the pose; camoff gives the normal camera back
        string stillCam;
        bool StillCamKey(string key){if(key=="camoff"){stillCam=null;return true;}if(key=="camphone"||key=="camface"||key=="camback"||key=="camcat"||(key.Length==4 && char.IsDigit(key[3]))){stillCam=key;return true;}return false;}
        void StillCam()
        {
            if(stillCam==null || cameraView==null || anim==null)return;
            Vector3 eye,aim;float fov=30;
            if(stillCam=="camcat"){ // a low side view of the controlled cat (nose to the left), for comparing the sleeping poses with the Blender sheets
                var cp=ctl!=null?ctl:pets.FirstOrDefault();if(cp==null || cp.root==null)return;
                var rs=cp.root.GetComponentsInChildren<Renderer>();if(rs.Length==0)return;var bb=rs[0].bounds;foreach(var rr in rs)bb.Encapsulate(rr.bounds);
                var ctr=new Vector3(bb.center.x,bb.min.y+Mathf.Max(.1f,bb.size.y*.5f),bb.center.z);var side=-cp.root.right;side.y=0;side.Normalize();
                float eyeH=cp.perch!=null?.12f:.18f;eye=new Vector3(ctr.x,bb.min.y+eyeH,ctr.z)+side*1.0f;aim=cp.perch!=null?ctr:new Vector3(ctr.x,bb.min.y+.1f,ctr.z); // on the floor: an eye 2 cm above it looking level, so any gap under the body shows as light
                bool InRoom(Vector3 e)=>e.x>.2f && e.x<2.8f && e.z>.2f && e.z<5.3f;
                if(cp.perch==null){ // floor: the open strip between the bed, the TV cabinet and the kitchen is x 1.05..2.15, so try both sides and both ends of the cat at 1.0 then 0.75 m
                    var fw=cp.root.forward;fw.y=0;fw.Normalize();bool found=false;
                    foreach(var dist in new[]{1f,.75f})foreach(var dir in new[]{side,-side,fw,-fw}){var cand=new Vector3(ctr.x,bb.min.y+eyeH,ctr.z)+dir*dist;if(cand.x>.95f && cand.x<2.3f && cand.z>.5f && cand.z<5f){eye=cand;found=true;break;}if(found)break;}
                }
                if(cp.perch!=null && !InRoom(eye)){var e2=new Vector3(ctr.x,bb.min.y+eyeH,ctr.z)-side*1.15f;eye=InRoom(e2)?e2:new Vector3(Mathf.Clamp(eye.x,.2f,2.8f),eye.y,Mathf.Clamp(eye.z,.2f,5.3f));} // the studio's walls: look from the other side (the nose then points right) or from as far as the room allows
                if(cp.perch!=null && cp.perch.name.StartsWith("tower")){ // on the cat tower: from the room's middle side, a little above the perch top, so the whole pose and the rim show
                    var inward=new Vector3(1.5f-ctr.x,0,2.75f-ctr.z);if(inward.sqrMagnitude<.01f)inward=Vector3.back;inward.Normalize();
                    eye=new Vector3(ctr.x,bb.min.y+.3f,ctr.z)+inward*1.3f;}
                cameraView.fieldOfView=28;cameraView.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(aim-eye,Vector3.up));return;}
            if(stillCam=="camphone" || stillCam=="camface" || stillCam=="camback"){
                if(coffeeRig!=null && coffeeRig.Weight>.01f){ // the coffee: camphone close on the mug and hands from in front and a little to her left, camface in front of her, camback from her right side
                    var f3=Vector3.ProjectOnPlane(anim.transform.forward,Vector3.up).normalized;var r3=Vector3.Cross(Vector3.up,f3).normalized;var c3=coffeeRig.Mug.position;var hd3=anim.GetBoneTransform(HumanBodyBones.Head).position;
                    if(stillCam=="camphone"){eye=c3+f3*.5f-r3*.1f+Vector3.up*.12f;aim=c3+Vector3.up*.02f;fov=26;}
                    else if(stillCam=="camface"){eye=hd3+f3*.8f+Vector3.up*.05f;aim=hd3-Vector3.up*.05f;fov=30;}
                    else{eye=hd3+r3*1.1f+f3*.1f+Vector3.up*.1f;aim=Vector3.Lerp(hd3,c3,.4f);fov=32;}
                    cameraView.fieldOfView=fov;cameraView.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(aim-eye,Vector3.up));return;}
                if(callRig!=null && callRig.Weight>.01f){ // the phone call: the phone at her right ear (camphone close on it, camface in front of her, camback from her right side)
                    var f2=Vector3.ProjectOnPlane(anim.transform.forward,Vector3.up).normalized;var r2=Vector3.Cross(Vector3.up,f2).normalized;var c2=callRig.Phone.position;var hd2=anim.GetBoneTransform(HumanBodyBones.Head).position;
                    if(stillCam=="camphone"){eye=c2+r2*.45f+f2*.12f+Vector3.up*.04f;aim=c2;fov=24;}
                    else if(stillCam=="camface"){eye=hd2+f2*.9f+Vector3.up*.05f;aim=hd2+Vector3.up*.02f;fov=28;}
                    else{eye=hd2+r2*1.1f+f2*.1f+Vector3.up*.08f;aim=hd2;fov=28;}
                    cameraView.fieldOfView=fov;cameraView.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(aim-eye,Vector3.up));return;}
                if(laundryRig!=null && laundryRig.Weight>.01f){ // the laundry: all three close on her hands and the cloth (camphone from in front and to her right, camface from her right side, camback from above)
                    var f5=Vector3.ProjectOnPlane(anim.transform.forward,Vector3.up).normalized;var r5=Vector3.Cross(Vector3.up,f5).normalized;var mid5=(laundryRig.LeftContact+laundryRig.RightContact)/2;
                    if(stillCam=="camphone")eye=mid5+f5*.9f+r5*.3f+Vector3.up*.1f;
                    else if(stillCam=="camface")eye=mid5+r5*1.0f+Vector3.up*.1f;
                    else eye=mid5+Vector3.up*.9f+f5*.15f;
                    aim=mid5;cameraView.fieldOfView=32;cameraView.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(aim-eye,Vector3.up));return;}
                if(mealRig!=null && mealRig.Weight>.01f){ // the floor meal: camphone from her left and a little in front (low), camface from the far side of the table just above the table top, camback from her left-behind and above
                    var bw=mealRig.Bowl.position;var mf=mealRig.Props.forward;var mr=mealRig.Props.right;
                    if(stillCam=="camphone")eye=bw-mr*.5f+mf*.3f-Vector3.up*.02f;
                    else if(stillCam=="camface")eye=bw+mf*.9f-Vector3.up*.1f;
                    else eye=bw-mr*.45f-mf*.35f+Vector3.up*.2f;
                    aim=bw;cameraView.fieldOfView=30;cameraView.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(aim-eye,Vector3.up));return;}
                if(mirrorRig!=null && mirrorRig.Weight>.01f){ // the hand mirror: all three close on her left hand holding the handle (camphone from her side of the mirror, camface from her left, camback from behind the mirror)
                    var f4=Vector3.ProjectOnPlane(anim.transform.forward,Vector3.up).normalized;var r4=Vector3.Cross(Vector3.up,f4).normalized;var hp4=mirrorRig.Mirror.TransformPoint(Vector3.down*.1f);var n4=mirrorRig.MirrorNormal;
                    if(stillCam=="camphone")eye=hp4+n4*.15f-r4*.3f+Vector3.up*.12f;
                    else if(stillCam=="camface")eye=hp4-r4*.4f+f4*.1f+Vector3.up*.1f;
                    else eye=hp4-n4*.4f+Vector3.up*.1f;
                    aim=hp4;cameraView.fieldOfView=22;cameraView.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(aim-eye,Vector3.up));return;}
                if(sofaRig==null)return;
                var f=sofaRig.BodyForward;var r=Vector3.Cross(Vector3.up,f).normalized;var c=sofaRig.PhoneCentre;var hd=anim.GetBoneTransform(HumanBodyBones.Head).position;
                bool tvLook=sofaOn==SofaMode.Tv;
                if(stillCam=="camphone"){eye=c+f*.38f+r*.20f+Vector3.up*.10f;aim=c;fov=22;}
                else if(stillCam=="camface"){eye=hd+f*.85f+r*.30f+Vector3.up*.05f;aim=tvLook?hd-Vector3.up*.12f:Vector3.Lerp(hd,c,.5f);fov=tvLook?34:30;}
                else{eye=hd-f*.9f+r*.55f+Vector3.up*.45f;aim=Vector3.Lerp(hd,c,.5f);fov=32;}
            }else{
                var v=Environment.GetEnvironmentVariable("ODDITY_CAM"+stillCam.Substring(3));if(string.IsNullOrWhiteSpace(v))return;
                var q=v.Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray();eye=new Vector3(q[0],q[1],q[2]);aim=new Vector3(q[3],q[4],q[5]);fov=q[6];
            }
            cameraView.fieldOfView=fov;cameraView.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(aim-eye,Vector3.up));
        }
        // ---- P: phone on the sofa, T: TV from the sofa, B: lying on the stomach on the bed with the phone (SofaMotions.cs does the posing; this is the keys and the walk there)
        SofaRig sofaRig;SofaTv tv;SofaMode sofaOn,sofaWant;bool sofaPend,seatIsBed;float sofaPendT;bool sofaLogged;
        static readonly HashSet<string> SofaBodyKeys=new HashSet<string>{"space","c","v","k","y","j","u","z","f","0","1","2","3","4","5","6","7","8","9"};
        bool SeatedOnSofa()=>sofa!=null && current==SitIndex && !onFloor && chairSat==null && chairJob==null && !seatIsBed && seats.Any(t=>t.mark.root==sofa && t.mark.name!="SeatBed" && Vector3.Distance(t.mark.position,seatAt)<.05f);
        bool LyingOnBed()=>current==BedIndex && seatIsBed;
        static string SofaWord(SofaMode m)=>m==SofaMode.Tv?"tv":"phone";
        void SofaKey(SofaMode mode)
        {
            if(sofaRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            if(mode==SofaMode.Tv && tv==null){Notice("이 집에는 TV가 없음");return;}
            if(mode==SofaMode.Bed && !(apt && Apartment.HasBed && sofa!=null)){Notice("이 집에는 침대가 없음");return;}
            if(sofaOn==mode){SofaEnd("again",false);return;} // the same key again puts the phone down / stops
            if(sofaPend && sofaWant==mode && !lieTv){SofaCancel("again");return;}
            if(sofaPend && lieTv)SofaCancel("switch");
            lieTv=false;
            bool seated=mode==SofaMode.Bed?LyingOnBed():SeatedOnSofa();
            if(seated){SofaStart(mode,sofaOn!=SofaMode.None?"switch":"seated");return;} // P <-> T while sitting: direct, no standing up
            if(sofaOn!=SofaMode.None)SofaEnd("switch",true);
            sofaWant=mode;sofaPend=true;sofaPendT=0;
            if(mode==SofaMode.Bed){var b=Apartment.BedStand;clickSeat=null;clickWindow=false;clickGoal=new Vector2(b.x,b.z);hPathGoal=null;hReplanned=false;clickStuck=0;Notice("침대로 걸어가기");return;}
            var pool=seats.Where(t=>t.mark.root==sofa && t.mark.name!="SeatBed" && !CatOnSeat(t.mark)).OrderBy(t=>new Vector2(t.mark.position.x-x,t.mark.position.z-z).sqrMagnitude).ToList();
            if(pool.Count==0){sofaPend=false;sofaWant=SofaMode.None;Notice("앉을 소파 자리가 없음");return;}
            var seat=pool[0];var stand=seat.mark.position+seat.front*seat.off;
            if(chairSat!=null){StartChairStand(-1);return;}
            clickSeat=seat.mark;clickWindow=false;clickGoal=new Vector2(Mathf.Clamp(stand.x,LimX0,LimX1),Mathf.Clamp(stand.z,ZNear,ZFar));hPathGoal=null;hReplanned=false;clickStuck=0;Notice("소파로 걸어가기");
        }
        void SofaCancel(string why){if(!sofaPend)return;lieTv=false;lieTvSat=false;sofaPend=false;sofaWant=SofaMode.None;if(clickGoal.HasValue && clickSeat!=null && clickSeat.root==sofa){clickGoal=null;clickSeat=null;}Debug.Log("ROOMPLAY_SOFA cancel "+why);}
        void SofaStop(string why){if(sofaPend)SofaCancel(why);if(sofaOn!=SofaMode.None)SofaEnd(why,true);}
        void SofaStart(SofaMode mode,string why)
        {
            sofaOn=mode;sofaWant=mode;sofaPend=false;sofaLogged=false;sofaRig.LyingTv=lieTv && mode==SofaMode.Tv;sofaRig.SetMode(mode);
            string seatName=mode==SofaMode.Bed?"SeatBed":seats.Where(t=>t.mark.root==sofa && Vector3.Distance(t.mark.position,seatAt)<.05f).Select(t=>t.mark.name).FirstOrDefault()??"?";
            Debug.Log((mode==SofaMode.Bed?"ROOMPLAY_BED":"ROOMPLAY_SOFA")+" start="+SofaWord(mode)+(lieTv && mode==SofaMode.Tv?"(lying)":"")+" seat="+seatName+" why="+why+" model="+modelName);
            Notice(mode==SofaMode.Phone?"핸드폰 보기":mode==SofaMode.Tv?(lieTv?"누워서 TV 보기":"TV 보기"):"엎드려 핸드폰 보기");
        }
        void SofaEnd(string why,bool immediate)
        {
            if(sofaOn==SofaMode.None)return;
            Debug.Log((sofaOn==SofaMode.Bed?"ROOMPLAY_BED":"ROOMPLAY_SOFA")+" end="+SofaWord(sofaOn)+" why="+why);
            if(immediate)sofaRig.Clear();else sofaRig.SetMode(SofaMode.None);
            sofaOn=SofaMode.None;sofaWant=SofaMode.None;lieTv=false;lieTvSat=false;
            var vrm=model!=null?model.GetComponent<Vrm10Instance>():null;if(vrm!=null && !lookOn)vrm.LookAtTarget=null;
        }
        // ---- E: a phone call while pacing (PhoneCall.cs: PhoneCallRig poses the arm, head and mouth, PacePlan picks where she stands next; the walk is the click walk along FloorPath)
        PhoneCallRig callRig;bool callOn;int callPhase;float callStandT;Vector2 callPrev;System.Random callRng;bool callLastTalking;float callLogT;string callLastReaction="";int callReactions;
        void CallKey()
        {
            if(callOn){CallEnd("again");return;}
            if(callRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            if(coffeeOn)CoffeeEnd("call");if(curtainOn)CurtainEnd("call");if(wakeOn || wakePend)WakeEnd("call");if(workoutOn)WorkoutEnd("call");
            EndNewActs("call");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("call");
            if(chairSat!=null){StartChairStand(-1);}
            callOn=true;callPhase=0;callStandT=0;callPrev=new Vector2(x,z);callRng=new System.Random(Environment.GetEnvironmentVariable("ODDITY_CATSEED")!=null && int.TryParse(Environment.GetEnvironmentVariable("ODDITY_CATSEED"),out var sd)?sd+5:Environment.TickCount);
            callLastTalking=true;callLastReaction="";callReactions=0;callRig.Start();
            if(current!=9 && current!=10 && current!=11){onFloor=false;Select(9);}
            Debug.Log("ROOMPLAY_CALL start model="+modelName);Notice("전화 통화");
        }
        void CallEnd(string why)
        {
            if(!callOn)return;
            callOn=false;callRig.Stop();if(clickGoal.HasValue){clickGoal=null;clickSeat=null;}
            Debug.Log("ROOMPLAY_CALL end why="+why);
        }
        void CallTick(float dt)
        {
            if(!callOn)return;
            if(current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex || current==ZeroTwoIndex){CallEnd("pose");return;}
            if(callPhase==1 && !clickGoal.HasValue){callPhase=2;callPrev=new Vector2(x,z);}  // arrived (or gave up): stand and talk
            else if(callPhase==2){callStandT-=dt;if(callStandT<=0)callPhase=0;}
            if(callPhase==0 && !clickGoal.HasValue){
                var floors=new List<Rect>{Apartment.WanderBoxes.Length>0?Apartment.WanderBoxes[0]:Rect.MinMaxRect(LimX0,ZNear,LimX1,ZFar)};
                if(PacePlan.TryNext(floors,blockers,new Vector2(x,z),callRng,out var step)){
                    clickSeat=null;clickWindow=false;clickGoal=step.Target;hPathGoal=null;hReplanned=false;clickStuck=0;callPhase=1;callStandT=step.StandSeconds;
                    Debug.Log("ROOMPLAY_CALL pace to=("+F(step.Target.x)+","+F(step.Target.y)+") stand="+F(step.StandSeconds));
                }else{callPhase=2;callStandT=2f;}
            }
        }
        // after the pose is complete: tick and apply the rig, and the mouth
        void CallApply(float dt)
        {
            if(callRig==null || model==null)return;
            if(!callOn && callRig.Weight<=0){callRig.Apply(dt);return;}
            callRig.Tick(dt);callRig.Apply(dt);
            if(callRig.Talking!=callLastTalking){callLastTalking=callRig.Talking;Debug.Log("PHONECALL "+(callRig.Talking?"talk":"listen")+" for="+F(callRig.StateDuration));}
            if(callRig.ReactionCount!=callReactions){callReactions=callRig.ReactionCount;Debug.Log("PHONECALL "+(callRig.LastReaction=="Laugh"?"laugh":"sigh"));}
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null && vrm.Runtime!=null)vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa,callRig.MouthOpen*.7f);
            if(Time.unscaledTime-callLogT>3 && callRig.Weight>.99f){callLogT=Time.unscaledTime;Debug.Log("ROOMPLAY_CALLPOSE phone=("+F(callRig.Phone.position.x)+","+F(callRig.Phone.position.y)+","+F(callRig.Phone.position.z)+") ear=("+F(callRig.EarTarget.x)+","+F(callRig.EarTarget.y)+","+F(callRig.EarTarget.z)+") mouth="+F(callRig.MouthOpen)+" talking="+(callRig.Talking?1:0));}
        }
        // ---- Q: coffee at the big window (Coffee.cs: CoffeeRig poses both arms with a mug, the head and the mouth; she walks to the free floor in front of the window first, faces south, and now and then looks across the street at the peeping camera)
        Vector2 CoffeeSpotNow=>one?OneRoom.CoffeeSpot:WinSpotNow;
        CoffeeRig coffeeRig;bool coffeeOn,coffeeWalk;int coffeeTries;float coffeeLogT;string coffeeLastPhase="";
        void CoffeeKey()
        {
            if(coffeeOn){CoffeeEnd("again");return;}
            if(coffeeRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            if(callOn)CallEnd("coffee");if(curtainOn)CurtainEnd("coffee");if(wakeOn || wakePend)WakeEnd("coffee");if(workoutOn)WorkoutEnd("coffee");
            EndNewActs("coffee");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("coffee");
            if(chairSat!=null){StartChairStand(-1);}
            coffeeOn=true;coffeeWalk=true;coffeeTries=0;coffeeLastPhase="";
            if(current!=9 && current!=10 && current!=11){onFloor=false;Select(9);}
            clickSeat=null;clickWindow=true;clickGoal=CoffeeSpotNow;hPathGoal=null;hReplanned=false;clickStuck=0;
            Debug.Log("ROOMPLAY_COFFEE walk to=("+F(CoffeeSpotNow.x)+","+F(CoffeeSpotNow.y)+") model="+modelName);Notice("창가에서 커피");
        }
        // ---- X: pull the studio curtain shut (Shift+X: open it). Curtain.cs: CurtainPanel (rod and two sheer panels over the window, room-owned) and CurtainRig (arms, head, face); she walks to the free floor mid-window, faces south, checks the street, then pulls. The curtain stays where the pull left it.
        CurtainRig curtainRig;CurtainPanel curtain;bool curtainOn,curtainWalk;int curtainTries;string curtainLastPhase="";
        Vector2 CurtainSpotNow=>one?OneRoom.CurtainSpot:WinSpotNow;
        void CurtainKey(bool open)
        {
            if(curtainOn){CurtainEnd("again");return;}
            if(curtainRig==null || curtain==null || model==null){Notice(curtain==null?"이 집에는 커튼이 없음":"이 모델로는 할 수 없음");return;}
            if(callOn)CallEnd("curtain");
            if(coffeeOn)CoffeeEnd("curtain");if(wakeOn || wakePend)WakeEnd("curtain");if(workoutOn)WorkoutEnd("curtain");
            EndNewActs("curtain");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("curtain");
            if(chairSat!=null){StartChairStand(-1);}
            curtainRig.Curtain=curtain;curtainRig.Open=open;
            curtainOn=true;curtainWalk=true;curtainTries=0;curtainLastPhase="";
            if(current!=9 && current!=10 && current!=11){onFloor=false;Select(9);}
            clickSeat=null;clickWindow=true;clickGoal=CurtainSpotNow;hPathGoal=null;hReplanned=false;clickStuck=0;
            Debug.Log("ROOMPLAY_CURTAIN walk to=("+F(CurtainSpotNow.x)+","+F(CurtainSpotNow.y)+") open="+(open?1:0)+" openness="+F(curtain.Openness)+" model="+modelName);Notice(open?"커튼 열기":"커튼 치기");
        }
        void CurtainEnd(string why)
        {
            if(!curtainOn)return;
            curtainOn=false;curtainWalk=false;curtainRig.Stop();if(clickGoal.HasValue){clickGoal=null;clickSeat=null;clickWindow=false;}
            Debug.Log("ROOMPLAY_CURTAIN end why="+why+" openness="+F(curtain!=null?curtain.Openness:-1));
        }
        void CurtainTick(float dt)
        {
            if(!curtainOn)return;
            if(current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex || current==ZeroTwoIndex){CurtainEnd("pose");return;}
            if(curtainWalk && !clickGoal.HasValue && (new Vector2(x,z)-CurtainSpotNow).magnitude>.25f && curtainTries<4){
                curtainTries++;clickSeat=null;clickWindow=true;clickGoal=CurtainSpotNow;hPathGoal=null;hReplanned=false;clickStuck=0;Debug.Log("ROOMPLAY_CURTAIN retry "+curtainTries+" at=("+F(x)+","+F(z)+")");return;}
            if(curtainWalk && !clickGoal.HasValue){curtainWalk=false;curtainRig.Start();Debug.Log("ROOMPLAY_CURTAIN start at=("+F(x)+","+F(z)+") yaw="+F(yaw));}
        }
        void CurtainApply(float dt)
        {
            if(curtainRig==null || model==null)return;
            if(!curtainOn && curtainRig.Weight<=0){curtainRig.Apply(dt);return;}
            if(!curtainWalk)curtainRig.Tick(dt);
            curtainRig.Apply(dt);
            if(curtainRig.Phase!=curtainLastPhase){curtainLastPhase=curtainRig.Phase;Debug.Log("CURTAIN phase="+curtainLastPhase+" w="+F(curtainRig.Weight)+" openness="+F(curtain.Openness));}
            if(curtainOn && !curtainWalk && !curtainRig.Active)CurtainEnd("done"); // the motion runs once
        }
        void CoffeeEnd(string why)
        {
            if(!coffeeOn)return;
            coffeeOn=false;coffeeWalk=false;coffeeRig.Stop();if(clickGoal.HasValue){clickGoal=null;clickSeat=null;clickWindow=false;}
            Debug.Log("ROOMPLAY_COFFEE end why="+why);
        }
        void CoffeeTick(float dt)
        {
            if(!coffeeOn)return;
            if(current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex || current==ZeroTwoIndex){CoffeeEnd("pose");return;}
            if(coffeeWalk && !clickGoal.HasValue && (new Vector2(x,z)-CoffeeSpotNow).magnitude>.25f && coffeeTries<4){ // a cat or a give-up stopped her short of the window: walk again
                coffeeTries++;clickSeat=null;clickWindow=true;clickGoal=CoffeeSpotNow;hPathGoal=null;hReplanned=false;clickStuck=0;Debug.Log("ROOMPLAY_COFFEE retry "+coffeeTries+" at=("+F(x)+","+F(z)+")");return;}
            if(coffeeWalk && !clickGoal.HasValue){ // arrived (ClickStep already turned her south): the mug comes up
                coffeeWalk=false;coffeeRig.Start();Debug.Log("ROOMPLAY_COFFEEIDLE headDownT="+F(HeadDownT())+" clip="+Keys[current].clip);Debug.Log("ROOMPLAY_COFFEE start at=("+F(x)+","+F(z)+") yaw="+F(yaw));}
        }
        float HeadDownT(){var hb=anim.GetBoneTransform(HumanBodyBones.Head);if(hb==null)return 0;var f=hb.rotation*headFront;return -Mathf.Asin(Mathf.Clamp(f.y,-1,1))*Mathf.Rad2Deg;} // the head's pitch from the T-pose nose axis RoomPlay measured at load
        void CoffeeApply(float dt)
        {
            if(coffeeRig==null || model==null)return;
            if(!coffeeOn && coffeeRig.Weight<=0){coffeeRig.Apply(dt);return;}
            if(!coffeeWalk)coffeeRig.Tick(dt);
            coffeeRig.Apply(dt);
            if(coffeeRig.Phase!=coffeeLastPhase){coffeeLastPhase=coffeeRig.Phase;Debug.Log("COFFEE phase="+coffeeLastPhase+" w="+F(coffeeRig.Weight)+(coffeeRig.LookingOut?" lookingout=1":""));}
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null && vrm.Runtime!=null)vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa,coffeeRig.MouthOpen*.7f);
            if(Time.unscaledTime-coffeeLogT>3 && coffeeRig.Weight>.99f){coffeeLogT=Time.unscaledTime;Debug.Log("ROOMPLAY_COFFEEPOSE mug=("+F(coffeeRig.Mug.position.x)+","+F(coffeeRig.Mug.position.y)+","+F(coffeeRig.Mug.position.z)+") lip=("+F(coffeeRig.LipPoint.x)+","+F(coffeeRig.LipPoint.y)+","+F(coffeeRig.LipPoint.z)+") phase="+coffeeRig.Phase+" mouth="+F(coffeeRig.MouthOpen)+" headDown="+F(coffeeRig.HeadDownDegrees)+" headDownT="+F(HeadDownT())+" chestDown="+F(coffeeRig.ChestDownDegrees));}
        }
        // ---- , (comma): wake up on the bed edge and stretch (Wake.cs: WakeRig, 8 s, one shot, arms, spine, head, mouth). From lying she first sits up on the bed's east edge; from elsewhere she walks to the edge and sits (the C path)
        WakeRig wakeRig;bool wakeOn,wakePend;float wakePendT;string wakeLastPhase="";
        bool OnBedEdge(){var e=seats.FirstOrDefault(t=>t.mark!=null && t.mark.name=="SeatBedEdge");return e.mark!=null && current==SitIndex && !onFloor && chairSat==null && chairJob==null && Vector3.Distance(e.mark.position,seatAt)<.05f && weights[SitIndex]>.95f;}
        void WakeKey()
        {
            if(wakeOn || wakePend){WakeEnd("again");return;}
            if(wakeRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            var edge=seats.FirstOrDefault(t=>t.mark!=null && t.mark.name=="SeatBedEdge");
            if(edge.mark==null || !(apt && Apartment.HasBed)){Notice("이 집에는 침대가 없음");return;}
            if(CatOnSeat(edge.mark)){Notice("고양이가 그 자리에 있어요");return;}
            if(callOn)CallEnd("wake");if(coffeeOn)CoffeeEnd("wake");if(curtainOn)CurtainEnd("wake");if(workoutOn)WorkoutEnd("wake");
            EndNewActs("wake");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("wake");
            wakePend=true;wakePendT=0;wakeLastPhase="";
            if(chairSat!=null){StartChairStand(-1);Debug.Log("ROOMPLAY_WAKE pend (chair)");return;}
            if(current==NapIndex || current==BedIndex){Sit(SitIndex,false,edge.mark);Debug.Log("ROOMPLAY_WAKE sit up from lying model="+modelName);Notice("일어나 앉기");return;} // lying: she sits up on the edge in place
            if(OnBedEdge())return;
            if(current!=9 && current!=10 && current!=11 && current!=SitIndex){onFloor=false;Select(9);}
            var stand=edge.mark.position+edge.front*edge.off;
            clickSeat=edge.mark;clickWindow=false;clickGoal=new Vector2(Mathf.Clamp(stand.x,LimX0,LimX1),Mathf.Clamp(stand.z,ZNear,ZFar));hPathGoal=null;hReplanned=false;clickStuck=0;
            Debug.Log("ROOMPLAY_WAKE walk to=("+F(clickGoal.Value.x)+","+F(clickGoal.Value.y)+") model="+modelName);Notice("침대로 걸어가기");
        }
        void WakeEnd(string why)
        {
            if(!wakeOn && !wakePend)return;
            if(wakePend && clickGoal.HasValue && clickSeat!=null){clickGoal=null;clickSeat=null;}
            wakePend=false;wakeOn=false;wakeRig.Stop();
            Debug.Log("ROOMPLAY_WAKE end why="+why);
        }
        void WakeTick(float dt)
        {
            if(wakePend){
                wakePendT+=dt;
                if(OnBedEdge()){wakePend=false;wakeOn=true;wakeRig.Start();Debug.Log("ROOMPLAY_WAKE start at=("+F(x)+","+F(z)+") yaw="+F(yaw)+" model="+modelName);Notice("아침 기지개");}
                else if(!clickGoal.HasValue && chairSat==null && chairJob==null && wakePendT>.3f && current!=SitIndex)WakeEnd("no walk");
                else if(wakePendT>30)WakeEnd("timeout");
            }
            else if(wakeOn && current!=SitIndex)WakeEnd("pose");
        }
        void WakeApply(float dt)
        {
            if(wakeRig==null || model==null)return;
            if(!wakeOn && wakeRig.Weight<=0){wakeRig.Apply(dt);return;}
            wakeRig.Tick(dt);wakeRig.Apply(dt);
            if(wakeRig.Phase!=wakeLastPhase){wakeLastPhase=wakeRig.Phase;Debug.Log("WAKE phase="+wakeLastPhase+" w="+F(wakeRig.Weight)+" mouth="+F(wakeRig.MouthOpen));}
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null && vrm.Runtime!=null)vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa,wakeRig.MouthOpen*.8f);
            if(wakeOn && !wakeRig.Active)WakeEnd("done"); // the motion runs once
        }
        // ---- . (period): home workout on a yoga mat (Workout.cs: WorkoutRig owns the whole body incl. leg IK and creates the mat prop). OneRoom.WorkoutSpot is the free floor between the bed and the TV cabinet; the mat shows (the rug hides) from the key press until the motion has faded out, and the cats' paths treat the mat area as furniture while she exercises
        Transform tvGaze;WorkoutRig workoutRig;bool workoutOn,workoutWalk,workoutBlocked;int workoutTries;float workoutWait,workoutLogT;string workoutLastPhase="";
        void WorkoutShowMat(bool on)
        {
            if(workoutRig==null || workoutRig.Mat==null)return;
            if(on){var sp=OneRoom.WorkoutMatCentre;workoutRig.Mat.position=new Vector3(sp.x,-WorkoutRig.MatThickness/2f,sp.y);workoutRig.Mat.rotation=Quaternion.Euler(0,OneRoom.WorkoutYaw,0);}
            workoutRig.Mat.gameObject.SetActive(on);
            foreach(var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))if(t.name=="rug" && t.GetComponent<Renderer>()!=null)t.GetComponent<Renderer>().enabled=!on;
        }
        void WorkoutBlockOn(){if(workoutBlocked)return;workoutBlocked=true;var a=OneRoom.WorkoutArea;blockers.Add(Rect.MinMaxRect(a.xMin,a.yMin,a.xMax,a.yMax));floorPlan=null;}
        void WorkoutBlockOff(){if(!workoutBlocked)return;workoutBlocked=false;var a=OneRoom.WorkoutArea;blockers.RemoveAll(r=>Mathf.Approximately(r.xMin,a.xMin) && Mathf.Approximately(r.yMin,a.yMin) && Mathf.Approximately(r.xMax,a.xMax) && Mathf.Approximately(r.yMax,a.yMax));floorPlan=null;}
        void WorkoutKey()
        {
            if(workoutOn){WorkoutEnd("again");return;}
            if(workoutRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            if(!one){Notice("이 집에는 매트 자리가 없음");return;}
            if(callOn)CallEnd("workout");if(coffeeOn)CoffeeEnd("workout");if(curtainOn)CurtainEnd("workout");if(wakeOn || wakePend)WakeEnd("workout");
            EndNewActs("workout");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("workout");
            if(chairSat!=null){StartChairStand(-1);}
            workoutOn=true;workoutWalk=true;workoutTries=0;workoutWait=0;workoutLastPhase="";
            if(current!=9 && current!=10 && current!=11){onFloor=false;Select(9);}
            WorkoutShowMat(false); // the mat is laid when she arrives
            clickSeat=null;clickWindow=true;clickYaw=OneRoom.WorkoutYaw;clickGoal=OneRoom.WorkoutSpot;hPathGoal=null;hReplanned=false;clickStuck=0;
            Debug.Log("ROOMPLAY_WORKOUT walk to=("+F(OneRoom.WorkoutSpot.x)+","+F(OneRoom.WorkoutSpot.y)+") model="+modelName);Notice("홈트");
        }
        void WorkoutEnd(string why)
        {
            if(!workoutOn)return;
            workoutOn=false;workoutWalk=false;workoutRig.Stop();WorkoutBlockOff();if(clickGoal.HasValue){clickGoal=null;clickSeat=null;clickWindow=false;}clickYaw=null;
            Debug.Log("ROOMPLAY_WORKOUT end why="+why+" reps="+workoutRig.TotalReps);
        }
        void WorkoutTick(float dt)
        {
            if(!workoutOn)return;
            if(current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex || current==ZeroTwoIndex){WorkoutEnd("pose");return;}
            if(workoutWalk && !clickGoal.HasValue && (new Vector2(x,z)-OneRoom.WorkoutSpot).magnitude>.25f && workoutTries<4){
                workoutTries++;clickSeat=null;clickWindow=true;clickYaw=OneRoom.WorkoutYaw;clickGoal=OneRoom.WorkoutSpot;hPathGoal=null;hReplanned=false;clickStuck=0;Debug.Log("ROOMPLAY_WORKOUT retry "+workoutTries+" at=("+F(x)+","+F(z)+")");return;}
            if(workoutWalk && !clickGoal.HasValue){ // arrived: wait (at most 2 s) until she has turned to face the window, then the circuit starts
                workoutWait+=dt;
                if(Mathf.Abs(Mathf.DeltaAngle(yaw,OneRoom.WorkoutYaw))<3f || workoutWait>2f){workoutWalk=false;WorkoutBlockOn();WorkoutShowMat(true);workoutRig.Start();Debug.Log("ROOMPLAY_WORKOUT start at=("+F(x)+","+F(z)+") yaw="+F(yaw));}}
        }
        void WorkoutApply(float dt)
        {
            if(workoutRig==null || model==null)return;
            if(!workoutOn && workoutRig.Weight<=0){if(workoutRig.Mat!=null && workoutRig.Mat.gameObject.activeSelf)WorkoutShowMat(false);workoutRig.Apply(dt);return;}
            // watching the TV: the rig turns the head to the screen centre, the eyes follow it here (L, looking at the camera, wins)
            workoutRig.GazeTarget=tv!=null && workoutOn && !workoutWalk && !lookOn?tv.ScreenCentre:(Vector3?)null;
            if(!workoutWalk)workoutRig.Tick(dt);
            workoutRig.Apply(dt);
            {var gv=model.GetComponent<Vrm10Instance>();
             if(gv!=null && !lookOn){
                if(workoutRig.GazeBlend>.5f && tv!=null){if(tvGaze==null)tvGaze=new GameObject("TvGazeTarget").transform;tvGaze.position=tv.ScreenCentre;gv.LookAtTargetType=VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;gv.LookAtTarget=tvGaze;}
                else if(tvGaze!=null && gv.LookAtTarget==tvGaze)gv.LookAtTarget=null;}}
            if(workoutRig.Phase!=workoutLastPhase){workoutLastPhase=workoutRig.Phase;Debug.Log("WORKOUT phase="+workoutLastPhase+" T="+F(Time.unscaledTime)+" rep="+workoutRig.Rep+" w="+F(workoutRig.Weight)+" footErr="+F(workoutRig.MaxFootError));}
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null && vrm.Runtime!=null)vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa,workoutRig.MouthOpen*.7f);
            if(Time.unscaledTime-workoutLogT>3 && workoutRig.Weight>.99f){workoutLogT=Time.unscaledTime;Debug.Log("ROOMPLAY_WORKOUTPOSE phase="+workoutRig.Phase+" lfoot=("+F(workoutRig.LeftContact.x)+","+F(workoutRig.LeftContact.y)+","+F(workoutRig.LeftContact.z)+") rfoot=("+F(workoutRig.RightContact.x)+","+F(workoutRig.RightContact.y)+","+F(workoutRig.RightContact.z)+") footErr="+F(workoutRig.MaxFootError)+" reps="+workoutRig.TotalReps);}
        }
        // ---- ; (semicolon) instant ramen at the kitchenette, ' (quote) a floor meal at a low table, / (slash) laundry folding on the bed edge. Each rig (Ramen.cs, FloorMeal.cs, Laundry.cs) was written by GPT; the wiring copies , and . above
        // ;: she walks to OneRoom.RamenSpot and faces east (clickYaw), RamenRig owns arms/head/torso and its props; the room adds the tap stream and the induction glow from InductionOn / TapOn
        RamenRig ramenRig;bool ramenOn,ramenWalk,ramenBlocked;int ramenTries;float ramenWait,ramenLogT,cooktopGlow;string ramenLastPhase="";
        Transform ramenStream;Renderer cooktopRing;float? clickYaw;
        void AreaBlock(ref bool flag,Rect a,bool on)
        {
            if(on){if(flag)return;flag=true;blockers.Add(Rect.MinMaxRect(a.xMin,a.yMin,a.xMax,a.yMax));floorPlan=null;}
            else{if(!flag)return;flag=false;blockers.RemoveAll(r=>Mathf.Approximately(r.xMin,a.xMin) && Mathf.Approximately(r.yMin,a.yMin) && Mathf.Approximately(r.xMax,a.xMax) && Mathf.Approximately(r.yMax,a.yMax));floorPlan=null;}
        }
        void RamenBlockOff(){AreaBlock(ref ramenBlocked,OneRoom.RamenArea,false);}
        void EndNewActs(string why){if(ramenOn)RamenEnd(why);if(mealOn)MealEnd(why);if(laundryOn || laundryPend)LaundryEnd(why);MirrorEnd(why);CatPlayEnd(why);}
        void RamenKey()
        {
            if(ramenOn){RamenEnd("again");return;}
            if(ramenRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            if(!one){Notice("이 집에는 주방이 없음");return;}
            if(callOn)CallEnd("ramen");if(coffeeOn)CoffeeEnd("ramen");if(curtainOn)CurtainEnd("ramen");if(wakeOn || wakePend)WakeEnd("ramen");if(workoutOn)WorkoutEnd("ramen");if(mealOn)MealEnd("ramen");if(laundryOn || laundryPend)LaundryEnd("ramen");MirrorEnd("ramen");CatPlayEnd("ramen");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("ramen");
            if(chairSat!=null){StartChairStand(-1);}
            ramenOn=true;ramenWalk=true;ramenTries=0;ramenWait=0;ramenLastPhase="";
            if(current!=9 && current!=10 && current!=11){onFloor=false;Select(9);}
            clickSeat=null;clickWindow=true;clickYaw=OneRoom.RamenYaw;clickGoal=OneRoom.RamenSpot;hPathGoal=null;hReplanned=false;clickStuck=0;
            Debug.Log("ROOMPLAY_RAMEN walk to=("+F(OneRoom.RamenSpot.x)+","+F(OneRoom.RamenSpot.y)+") model="+modelName);Notice("라면 끓이기");
        }
        void RamenEnd(string why)
        {
            if(!ramenOn)return;
            ramenOn=false;ramenWalk=false;ramenRig.Stop();RamenBlockOff();if(clickGoal.HasValue){clickGoal=null;clickSeat=null;clickWindow=false;}clickYaw=null;
            Debug.Log("ROOMPLAY_RAMEN end why="+why+" phase="+ramenRig.Phase);
        }
        void RamenTick(float dt)
        {
            if(!ramenOn)return;
            if(current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex || current==ZeroTwoIndex){RamenEnd("pose");return;}
            if(ramenWalk && !clickGoal.HasValue && (new Vector2(x,z)-OneRoom.RamenSpot).magnitude>.25f && ramenTries<4){
                ramenTries++;clickSeat=null;clickWindow=true;clickYaw=OneRoom.RamenYaw;clickGoal=OneRoom.RamenSpot;hPathGoal=null;hReplanned=false;clickStuck=0;Debug.Log("ROOMPLAY_RAMEN retry "+ramenTries+" at=("+F(x)+","+F(z)+")");return;}
            if(ramenWalk && !clickGoal.HasValue){ // arrived: wait (at most 2 s) until she faces the counter, then the cooking starts
                ramenWait+=dt;
                if(Mathf.Abs(Mathf.DeltaAngle(yaw,OneRoom.RamenYaw))<3f || ramenWait>2f){ramenWalk=false;AreaBlock(ref ramenBlocked,OneRoom.RamenArea,true);
                    ramenRig.Sink=OneRoom.RamenSink;ramenRig.Burner=OneRoom.RamenBurner;ramenRig.Knob=OneRoom.RamenKnob;ramenRig.TapControl=OneRoom.RamenTap;
                    ramenRig.Start();Debug.Log("ROOMPLAY_RAMEN start at=("+F(x)+","+F(z)+") yaw="+F(yaw));}}
        }
        // the tap stream (a thin blue cylinder from the nozzle into the pot) and the cooktop's faint glow while the induction is on
        void RamenRoomFx(float dt)
        {
            bool induction=ramenRig!=null && ramenRig.InductionOn,tap=ramenRig!=null && ramenRig.TapOn;
            if(cooktopGlow<=0 && !induction && ramenStream==null && !tap)return;
            cooktopGlow=Mathf.MoveTowards(cooktopGlow,induction?1:0,dt/.8f);
            if(cooktopRing==null){if(cooktopGlow<=0)goto stream;var g=GameObject.CreatePrimitive(PrimitiveType.Cylinder);g.name="RamenBurnerRing";Destroy(g.GetComponent<Collider>());g.transform.position=OneRoom.RamenBurner+Vector3.up*.0006f;g.transform.localScale=new Vector3(.215f,.0006f,.215f);
                var rm=new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));g.GetComponent<Renderer>().sharedMaterial=rm;cooktopRing=g.GetComponent<Renderer>();}
            {var m=cooktopRing.sharedMaterial;var c=Color.Lerp(new Color(.08f,.08f,.09f),new Color(.95f,.32f,.12f),cooktopGlow);m.color=c;if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);
                m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(.9f,.25f,.08f)*cooktopGlow*.8f);cooktopRing.enabled=cooktopGlow>0;}
            stream:
            if(ramenStream==null){if(!tap)return;var g=GameObject.CreatePrimitive(PrimitiveType.Cylinder);g.name="RamenStream";Destroy(g.GetComponent<Collider>());var mat=new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));mat.color=new Color(.65f,.82f,.92f);g.GetComponent<Renderer>().sharedMaterial=mat;ramenStream=g.transform;}
            ramenStream.gameObject.SetActive(tap);
            if(tap){var top=new Vector3(2.62f,1.03f,3.95f);var potTop=ramenRig.Pot.position.y+.04f;float len=Mathf.Max(.02f,top.y-potTop);ramenStream.position=new Vector3(top.x,top.y-len/2,top.z);ramenStream.localScale=new Vector3(.012f,len/2,.012f);}
        }
        void RamenApply(float dt)
        {
            if(ramenRig==null || model==null)return;
            RamenRoomFx(dt);
            if(!ramenOn && ramenRig.Weight<=0){ramenRig.Apply(dt);return;}
            if(!ramenWalk)ramenRig.Tick(dt);
            ramenRig.Apply(dt);
            if(ramenRig.Phase!=ramenLastPhase){ramenLastPhase=ramenRig.Phase;Debug.Log("RAMEN phase="+ramenLastPhase+" T="+F(Time.unscaledTime)+" w="+F(ramenRig.Weight)+" L=("+F(ramenRig.LeftTarget.x)+","+F(ramenRig.LeftTarget.y)+","+F(ramenRig.LeftTarget.z)+") R=("+F(ramenRig.RightTarget.x)+","+F(ramenRig.RightTarget.y)+","+F(ramenRig.RightTarget.z)+") errL="+F(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.LeftHand).position,ramenRig.LeftTarget))+" errR="+F(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position,ramenRig.RightTarget)));}
            if(Time.unscaledTime-ramenLogT>2 && ramenRig.Weight>.99f){ramenLogT=Time.unscaledTime;Debug.Log("ROOMPLAY_RAMENPOSE phase="+ramenRig.Phase+" errL="+F(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.LeftHand).position,ramenRig.LeftTarget))+" errR="+F(Vector3.Distance(anim.GetBoneTransform(HumanBodyBones.RightHand).position,ramenRig.RightTarget))+" at=("+F(x)+","+F(z)+")");}
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null && vrm.Runtime!=null)vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa,ramenRig.MouthOpen*.7f);
        }
        // ' : cross-legged on a cushion at a low folding table (FloorMeal.cs: FloorMealRig owns hips, legs, arms, head and its props). The props show from the moment she has arrived at OneRoom.MealSpot until the motion has faded out
        FloorMealRig mealRig;bool mealOn,mealWalk,mealBlocked;int mealTries;float mealWait,mealLogT;string mealLastPhase="";
        void MealBlockOff(){AreaBlock(ref mealBlocked,OneRoom.MealArea,false);}
        void MealShow(bool on)
        {
            if(mealRig==null || mealRig.Props==null)return;
            if(on){var sp=OneRoom.MealSpot;mealRig.Props.SetPositionAndRotation(new Vector3(sp.x,0,sp.y),Quaternion.Euler(0,OneRoom.MealYaw,0));}
            mealRig.Props.gameObject.SetActive(on);
        }
        void MealKey()
        {
            if(mealOn){MealEnd("again");return;}
            if(mealRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            if(!one){Notice("이 집에는 상 놓을 자리가 없음");return;}
            if(callOn)CallEnd("meal");if(coffeeOn)CoffeeEnd("meal");if(curtainOn)CurtainEnd("meal");if(wakeOn || wakePend)WakeEnd("meal");if(workoutOn)WorkoutEnd("meal");if(ramenOn)RamenEnd("meal");if(laundryOn || laundryPend)LaundryEnd("meal");MirrorEnd("meal");CatPlayEnd("meal");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("meal");
            if(chairSat!=null){StartChairStand(-1);}
            mealOn=true;mealWalk=true;mealTries=0;mealWait=0;mealLastPhase="";
            if(current!=9 && current!=10 && current!=11){onFloor=false;Select(9);}
            clickSeat=null;clickWindow=true;clickYaw=OneRoom.MealYaw;clickGoal=OneRoom.MealSpot;hPathGoal=null;hReplanned=false;clickStuck=0;
            Debug.Log("ROOMPLAY_MEAL walk to=("+F(OneRoom.MealSpot.x)+","+F(OneRoom.MealSpot.y)+") model="+modelName);Notice("바닥에 앉아 먹기");
        }
        void MealEnd(string why)
        {
            if(!mealOn)return;
            mealOn=false;mealWalk=false;mealRig.Stop();MealBlockOff();if(clickGoal.HasValue){clickGoal=null;clickSeat=null;clickWindow=false;}clickYaw=null;
            Debug.Log("ROOMPLAY_MEAL end why="+why+" phase="+mealRig.Phase);
        }
        void MealTick(float dt)
        {
            if(!mealOn)return;
            if(current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex || current==ZeroTwoIndex){MealEnd("pose");return;}
            if(mealWalk && !clickGoal.HasValue && (new Vector2(x,z)-OneRoom.MealSpot).magnitude>.25f && mealTries<4){
                mealTries++;clickSeat=null;clickWindow=true;clickYaw=OneRoom.MealYaw;clickGoal=OneRoom.MealSpot;hPathGoal=null;hReplanned=false;clickStuck=0;Debug.Log("ROOMPLAY_MEAL retry "+mealTries+" at=("+F(x)+","+F(z)+")");return;}
            if(mealWalk && !clickGoal.HasValue){
                mealWait+=dt;
                if(Mathf.Abs(Mathf.DeltaAngle(yaw,OneRoom.MealYaw))<3f || mealWait>2f){mealWalk=false;AreaBlock(ref mealBlocked,OneRoom.MealArea,true);MealShow(true);mealRig.Start();Debug.Log("ROOMPLAY_MEAL start at=("+F(x)+","+F(z)+") yaw="+F(yaw));}}
        }
        void MealApply(float dt)
        {
            if(mealRig==null || model==null)return;
            if(!mealOn && mealRig.Weight<=0){if(mealRig.Props.gameObject.activeSelf)MealShow(false);mealRig.Apply(dt);return;}
            if(!mealWalk)mealRig.Tick(dt);
            mealRig.Apply(dt);
            if(mealRig.Phase!=mealLastPhase){mealLastPhase=mealRig.Phase;Debug.Log("MEAL phase="+mealLastPhase+" T="+F(Time.unscaledTime)+" w="+F(mealRig.Weight)+" mouth="+F(mealRig.MouthOpen));}
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null && vrm.Runtime!=null)vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa,mealRig.MouthOpen*.7f);vrm.Runtime.Expression.SetWeight(ExpressionKey.Ou,mealRig.MouthPurse*.8f);
        }
        // / : fold laundry on the bed's east edge (Laundry.cs: LaundryRig, five cloths, heap on her left, folded stack on her right). Like , she first sits up from lying or walks to the edge and sits; when the fifth cloth is folded and patted the stack stays 6 s and then the motion ends by itself and the props vanish with the blend-out
        LaundryRig laundryRig;bool laundryOn,laundryPend;float laundryPendT,laundryDoneT;string laundryLastPhase="";
        void LaundryKey()
        {
            if(laundryOn || laundryPend){LaundryEnd("again");return;}
            if(laundryRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            var edge=seats.FirstOrDefault(t=>t.mark!=null && t.mark.name=="SeatBedEdge");
            if(edge.mark==null || !(apt && Apartment.HasBed)){Notice("이 집에는 침대가 없음");return;}
            if(CatOnSeat(edge.mark)){Notice("고양이가 그 자리에 있어요");return;}
            if(callOn)CallEnd("laundry");if(coffeeOn)CoffeeEnd("laundry");if(curtainOn)CurtainEnd("laundry");if(wakeOn || wakePend)WakeEnd("laundry");if(workoutOn)WorkoutEnd("laundry");if(ramenOn)RamenEnd("laundry");if(mealOn)MealEnd("laundry");MirrorEnd("laundry");CatPlayEnd("laundry");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("laundry");
            laundryPend=true;laundryPendT=0;laundryDoneT=0;laundryLastPhase="";
            if(chairSat!=null){StartChairStand(-1);Debug.Log("ROOMPLAY_LAUNDRY pend (chair)");return;}
            if(current==NapIndex || current==BedIndex){Sit(SitIndex,false,edge.mark);Debug.Log("ROOMPLAY_LAUNDRY sit up from lying model="+modelName);Notice("일어나 앉기");return;}
            if(OnBedEdge())return;
            if(current!=9 && current!=10 && current!=11 && current!=SitIndex){onFloor=false;Select(9);}
            var stand=edge.mark.position+edge.front*edge.off;
            clickSeat=edge.mark;clickWindow=false;clickGoal=new Vector2(Mathf.Clamp(stand.x,LimX0,LimX1),Mathf.Clamp(stand.z,ZNear,ZFar));hPathGoal=null;hReplanned=false;clickStuck=0;
            Debug.Log("ROOMPLAY_LAUNDRY walk to=("+F(clickGoal.Value.x)+","+F(clickGoal.Value.y)+") model="+modelName);Notice("침대로 걸어가기");
        }
        void LaundryEnd(string why)
        {
            if(!laundryOn && !laundryPend)return;
            if(laundryPend && clickGoal.HasValue && clickSeat!=null){clickGoal=null;clickSeat=null;}
            laundryPend=false;laundryOn=false;laundryRig.Stop();
            Debug.Log("ROOMPLAY_LAUNDRY end why="+why+" done="+laundryRig.Completed+" phase="+laundryRig.Phase);
        }
        void LaundryTick(float dt)
        {
            if(laundryPend){
                laundryPendT+=dt;
                if(OnBedEdge()){laundryPend=false;laundryOn=true;laundryDoneT=0;laundryRig.Props.SetPositionAndRotation(OneRoom.LaundryAt,Quaternion.Euler(0,OneRoom.LaundryYaw,0));laundryRig.Start();Debug.Log("ROOMPLAY_LAUNDRY start at=("+F(x)+","+F(z)+") yaw="+F(yaw)+" model="+modelName);Notice("빨래 개기");}
                else if(!clickGoal.HasValue && chairSat==null && chairJob==null && laundryPendT>.3f && current!=SitIndex)LaundryEnd("no walk");
                else if(laundryPendT>30)LaundryEnd("timeout");
            }
            else if(laundryOn){
                if(current!=SitIndex)LaundryEnd("pose");
                else if(laundryRig.Done){laundryDoneT+=dt;if(laundryDoneT>6f)LaundryEnd("done");}
            }
        }
        void LaundryApply(float dt)
        {
            if(laundryRig==null || model==null)return;
            if(!laundryOn && laundryRig.Weight<=0){laundryRig.Apply(dt);return;}
            laundryRig.Tick(dt);laundryRig.Apply(dt);
            if(laundryRig.Phase!=laundryLastPhase){laundryLastPhase=laundryRig.Phase;Debug.Log("LAUNDRY phase="+laundryLastPhase+" piece="+laundryRig.Completed+" w="+F(laundryRig.Weight));}
        }
        // ---- [ (left bracket): a hand mirror in the left hand and a lip tint stick in the right, on the bed's east edge (Mirror.cs: MirrorRig, 25 s loop, written by GPT). The wiring copies / above: from lying she sits up, from elsewhere she walks to the edge and sits; it loops until the key, Space, a walk or another action
        MirrorRig mirrorRig;bool mirrorOn,mirrorPend;float mirrorPendT;string mirrorLastPhase="";
        const float MirrorSquintBlink=.75f; // the rig's squint closes both eyes up to .8; the room shows it as a squint, not a shut eye
        void MirrorKey()
        {
            if(mirrorOn || mirrorPend){MirrorEnd("again");return;}
            if(mirrorRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            var edge=seats.FirstOrDefault(t=>t.mark!=null && t.mark.name=="SeatBedEdge");
            if(edge.mark==null || !(apt && Apartment.HasBed)){Notice("이 집에는 침대가 없음");return;}
            if(CatOnSeat(edge.mark)){Notice("고양이가 그 자리에 있어요");return;}
            if(callOn)CallEnd("mirror");if(coffeeOn)CoffeeEnd("mirror");if(curtainOn)CurtainEnd("mirror");if(wakeOn || wakePend)WakeEnd("mirror");if(workoutOn)WorkoutEnd("mirror");if(ramenOn)RamenEnd("mirror");if(mealOn)MealEnd("mirror");if(laundryOn || laundryPend)LaundryEnd("mirror");CatPlayEnd("mirror");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("mirror");
            mirrorPend=true;mirrorPendT=0;mirrorLastPhase="";
            if(chairSat!=null){StartChairStand(-1);Debug.Log("ROOMPLAY_MIRROR pend (chair)");return;}
            if(current==NapIndex || current==BedIndex){Sit(SitIndex,false,edge.mark);Debug.Log("ROOMPLAY_MIRROR sit up from lying model="+modelName);Notice("일어나 앉기");return;}
            if(OnBedEdge())return;
            if(current!=9 && current!=10 && current!=11 && current!=SitIndex){onFloor=false;Select(9);}
            var stand=edge.mark.position+edge.front*edge.off;
            clickSeat=edge.mark;clickWindow=false;clickGoal=new Vector2(Mathf.Clamp(stand.x,LimX0,LimX1),Mathf.Clamp(stand.z,ZNear,ZFar));hPathGoal=null;hReplanned=false;clickStuck=0;
            Debug.Log("ROOMPLAY_MIRROR walk to=("+F(clickGoal.Value.x)+","+F(clickGoal.Value.y)+") model="+modelName);Notice("침대로 걸어가기");
        }
        void MirrorEnd(string why)
        {
            if(!mirrorOn && !mirrorPend)return;
            if(mirrorPend && clickGoal.HasValue && clickSeat!=null){clickGoal=null;clickSeat=null;}
            mirrorPend=false;mirrorOn=false;mirrorRig.Stop();
            Debug.Log("ROOMPLAY_MIRROR end why="+why+" phase="+mirrorRig.Phase+" loops="+mirrorRig.Loops);
        }
        void MirrorTick(float dt)
        {
            if(mirrorPend){
                mirrorPendT+=dt;
                if(OnBedEdge()){mirrorPend=false;mirrorOn=true;mirrorRig.Start();Debug.Log("ROOMPLAY_MIRROR start at=("+F(x)+","+F(z)+") yaw="+F(yaw)+" model="+modelName);Notice("거울 보기");}
                else if(!clickGoal.HasValue && chairSat==null && chairJob==null && mirrorPendT>.3f && current!=SitIndex)MirrorEnd("no walk");
                else if(mirrorPendT>30)MirrorEnd("timeout");
            }
            else if(mirrorOn && current!=SitIndex)MirrorEnd("pose");
        }
        void MirrorApply(float dt)
        {
            if(mirrorRig==null || model==null)return;
            if(!mirrorOn && mirrorRig.Weight<=0){mirrorRig.Apply(dt);return;}
            mirrorRig.Tick(dt);mirrorRig.Apply(dt);
            if(mirrorRig.Phase!=mirrorLastPhase){mirrorLastPhase=mirrorRig.Phase;Debug.Log("MIRROR phase="+mirrorLastPhase+" loop="+mirrorRig.Loops+" w="+F(mirrorRig.Weight)+" mouth="+F(mirrorRig.MouthOpen)+" smile="+F(mirrorRig.Smile)+" squint="+F(mirrorRig.EyesClosed));}
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null && vrm.Runtime!=null)vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa,mirrorRig.MouthOpen*.7f);
        }
        // ---- ] (right bracket) plays with the nearest cat that is on the floor (CatPlay.cs: CatPlayRig, written by GPT, owns her legs, arms, spine, head and the wand props): ] petting (a loaf-resting cat, her right hand strokes head to tail), Shift+] a feather wand (the cat chases the feather tip and hops at it).
        // With a cat on the floor she walks to its flank and the cat is held still (its AI is paused, PetMove still runs); with none on the floor the nearest cat is called down to a free floor spot in front of her. A cat steered with Tab is never used. While on, ] / Shift+] switch the mode and the same key again ends; the cat AI resumes 1.5 s after the end
        CatPlayRig catRig;bool catOn,catWalk,catWantWand,catCome,catRecall;Pet catPet;int catTries,catCatTries;float catWait,catSettle,catJumpT,catBakeT,catTimeout,catLogT;string catLastPhase="";
        Vector2 catSpot,catHarinSpot;float catYaw;Vector3 catHeadTarget,catTailTarget;bool catPoints;Mesh catBake;
        void CatPlayReset()
        {
            if(catPet!=null){catPet.aiPaused=true;catPet.resumeIn=1.5f;catPet.goal=null;}
            catPet=null;catOn=false;catWalk=false;catRecall=false;catPoints=false;
        }
        void CatPlayKey(bool wand)
        {
            if(catOn){
                if(catWantWand==wand){CatPlayEnd("again");return;}
                catWantWand=wand;catRecall=false;if(wand && catRig.Mode!=CatPlayMode.Wand && !catWalk)catRig.Mode=CatPlayMode.Wand;
                Debug.Log("ROOMPLAY_CATPLAY mode="+(wand?"wand":"pet"));Notice(wand?"낚싯대 놀이":"고양이 쓰다듬기");return;
            }
            if(catRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            if(!apt){Notice("이 집에서는 할 수 없음");return;}
            Pet pick=null;float best=float.MaxValue;bool floorPick=false;var me=new Vector2(x,z);
            foreach(var o in pets){
                if(o==ctl || o.root==null || !o.graph.IsValid() || !o.somali)continue;
                bool fl=o.perch==null && !o.onSofa && o.job==0 && o.hopPlan.Count==0;float d=(PetXZ(o)-me).magnitude;
                if(pick==null || (fl && !floorPick) || (fl==floorPick && d<best)){pick=o;best=d;floorPick=fl;}
            }
            if(pick==null){Notice("고양이가 없어요");return;}
            if(callOn)CallEnd("catplay");if(coffeeOn)CoffeeEnd("catplay");if(curtainOn)CurtainEnd("catplay");if(wakeOn || wakePend)WakeEnd("catplay");if(workoutOn)WorkoutEnd("catplay");if(ramenOn)RamenEnd("catplay");if(mealOn)MealEnd("catplay");if(laundryOn || laundryPend)LaundryEnd("catplay");MirrorEnd("catplay");
            if(sofaOn!=SofaMode.None || sofaPend)SofaStop("catplay");
            if(chairSat!=null){StartChairStand(-1);}
            Vector2 hSpot;float hYaw;
            if(floorPick){
                var c=PetXZ(pick);float h=pick.heading<-999?pick.root.eulerAngles.y:pick.heading;
                var f=new Vector2(Mathf.Sin(h*Mathf.Deg2Rad),Mathf.Cos(h*Mathf.Deg2Rad));var sd=new Vector2(f.y,-f.x);
                var cands=new List<Vector2>();foreach(var sgn in new[]{1f,-1f})foreach(float dist in new[]{.36f,.38f})cands.Add(c+sd*(sgn*dist));
                for(int a=0;a<8;a++){var dd=Quaternion.Euler(0,0,a*45)*new Vector3(sd.x,sd.y,0);cands.Add(c+new Vector2(dd.x,dd.y)*.4f);}
                hSpot=default;bool got=false;float bestD=float.MaxValue;
                foreach(var cand in cands){var t=NearestFree(cand);float dc=(t-c).magnitude;if(dc<.34f || dc>.8f || Blocked(t))continue;float dm=(t-me).magnitude;if(dm<bestD-.001f){bestD=dm;hSpot=t;got=true;}}
                if(!got){Notice("고양이 곁에 설 자리가 없어요");return;}
                hYaw=Mathf.Atan2(c.x-hSpot.x,c.y-hSpot.y)*Mathf.Rad2Deg;
            }else{
                var rug=Apartment.RugBox;hSpot=NearestFree(new Vector2(rug.center.x,rug.center.y+.3f));hYaw=180f;
                catSpot=NearestFree(hSpot+new Vector2(0,-.5f));
            }
            catPet=pick;AiPause(pick);pick.resumeIn=1e6f;
            catCome=!floorPick;catWantWand=wand;catRecall=false;catTries=0;catCatTries=0;catWait=0;catSettle=0;catJumpT=1f;catTimeout=0;catLastPhase="";catPoints=false;
            if(catCome)PetGoTo(pick,"floor",catSpot,null,catSpot+new Vector2(1,0),"catplay");
            catHarinSpot=hSpot;catYaw=hYaw;catOn=true;catWalk=true;
            if(current!=9 && current!=10 && current!=11){onFloor=false;Select(9);}
            clickSeat=null;clickWindow=true;clickYaw=catYaw;clickGoal=catHarinSpot;hPathGoal=null;hReplanned=false;clickStuck=0;
            Debug.Log("ROOMPLAY_CATPLAY walk to=("+F(hSpot.x)+","+F(hSpot.y)+") yaw="+F(hYaw)+" cat="+pick.tag+" floor="+floorPick+" mode="+(wand?"wand":"pet")+" model="+modelName);Notice(wand?"낚싯대 놀이":"고양이 쓰다듬기");
        }
        void CatPlayEnd(string why)
        {
            if(!catOn)return;
            catOn=false;catWalk=false;catRecall=false;catRig.Stop();
            if(catPet!=null){catPet.aiPaused=true;catPet.resumeIn=1.5f;catPet.goal=null;catPet.faceTo=float.NaN;}
            if(clickGoal.HasValue){clickGoal=null;clickSeat=null;clickWindow=false;}clickYaw=null;
            Debug.Log("ROOMPLAY_CATPLAY end why="+why+" phase="+catRig.Phase);catPet=null;
        }
        static bool CatRested(Pet p)=>p.sit==2 || p.sit==5 || p.sit==8;
        void CatPlayStart()
        {
            catWalk=false;catRig.Mode=catWantWand?CatPlayMode.Wand:CatPlayMode.Pet;catRig.Start();
            Debug.Log("ROOMPLAY_CATPLAY start at=("+F(x)+","+F(z)+") yaw="+F(yaw)+" cat="+catPet.tag+" at=("+F(catPet.root.position.x)+","+F(catPet.root.position.z)+") sit="+catPet.sit+" mode="+catRig.Mode+" model="+modelName);
        }
        void CatPlayTick(float dt)
        {
            if(!catOn)return;
            if(current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex || current==ZeroTwoIndex){CatPlayEnd("pose");return;}
            var p=catPet;
            if(p==null || p.root==null || !p.graph.IsValid() || ctl==p){CatPlayEnd("no cat");return;}
            p.aiPaused=true;p.resumeIn=1e6f; // the cat AI stays off while she plays; PetMove below is the only thing that moves it
            var hp=new Vector2(x,z);
            if(catWalk){
                catTimeout+=dt;
                if(!clickGoal.HasValue && (hp-catHarinSpot).magnitude>.25f && catTries<4){
                    catTries++;clickSeat=null;clickWindow=true;clickYaw=catYaw;clickGoal=catHarinSpot;hPathGoal=null;hReplanned=false;clickStuck=0;Debug.Log("ROOMPLAY_CATPLAY retry "+catTries+" at=("+F(x)+","+F(z)+")");
                }
                if(catCome && !p.goal.HasValue && p.job==0 && !p.moving && (PetXZ(p)-catSpot).magnitude>.25f && catCatTries<3){catCatTries++;PetGoTo(p,"floor",catSpot,null,catSpot+new Vector2(1,0),"catplay");}
                PetMove(p,dt,Vector2.zero,false,0,true);
                bool harinHere=false;
                if(!clickGoal.HasValue){catWait+=dt;harinHere=Mathf.Abs(Mathf.DeltaAngle(yaw,catYaw))<3f || catWait>2f;}
                bool catHere=!catCome || ((PetXZ(p)-catSpot).magnitude<.3f && !p.goal.HasValue && !p.moving && p.job==0 && p.perch==null && !p.onSofa);
                if(harinHere && catHere){
                    if(catWantWand)CatPlayStart();
                    else{
                        catSettle+=dt;
                        if(CatRested(p) || catSettle>5f)CatPlayStart();
                        else if(p.sit==0 && !p.moving && p.job==0 && !p.goal.HasValue)PetRest(p,p.hasLie?3:1);
                    }
                }
                else if(catTimeout>25f)CatPlayEnd("timeout");
                return;
            }
            if(catWantWand){
                catRecall=false;if(catRig.Mode!=CatPlayMode.Wand)catRig.Mode=CatPlayMode.Wand;
                var tip=catRig.FeatherTipWorld;var to=new Vector2(tip.x,tip.z)-PetXZ(p);float dist=to.magnitude;
                Vector2 inp=Vector2.zero;
                if(p.job==0 && dist>.2f){var dir=new Vector3(to.x,0,to.y)/dist;var fw=MoveFwd();inp=new Vector2(Vector3.Dot(dir,new Vector3(fw.z,0,-fw.x)),Vector3.Dot(dir,fw));}
                catJumpT-=dt;
                if(p.job==0 && dist<=.36f){
                    p.faceTo=Mathf.Atan2(to.x,to.y)*Mathf.Rad2Deg;
                    if(catJumpT<=0 && p.sit==0 && !p.moving){PetRest(p,6);catJumpT=Rnd(1f,1.9f);Debug.Log("ROOMPLAY_CATPLAY hop dist="+F(dist)+" feather y="+F(tip.y));}
                }
                PetMove(p,dt,inp,false,0,true);
            }else{
                float d=(PetXZ(p)-hp).magnitude;
                if(catRig.Mode==CatPlayMode.Wand){ // coming back from the wand: the cat is called to her front and rests there, then the stroking starts
                    var fwd=new Vector2(Mathf.Sin(yaw*Mathf.Deg2Rad),Mathf.Cos(yaw*Mathf.Deg2Rad));
                    var spot=NearestFree(hp+fwd*.5f);
                    if(!catRecall && (d<.3f || d>.85f)){catRecall=true;PetGoTo(p,"floor",spot,null,spot+new Vector2(fwd.y,-fwd.x),"catplay");}
                    else if(!catRecall && !p.moving && p.job==0 && p.sit==0 && !p.goal.HasValue)catRecall=true;
                    if(catRecall && !p.goal.HasValue && !p.moving && p.job==0){
                        if(CatRested(p) && d>=.3f && d<=.85f){catRig.Mode=CatPlayMode.Pet;catRecall=false;Debug.Log("ROOMPLAY_CATPLAY pet settled cat="+p.tag+" sit="+p.sit);}
                        else if(p.sit==0)PetRest(p,p.hasLie?3:1);
                    }
                }
                PetMove(p,dt,Vector2.zero,false,0,true);
            }
        }
        // the cat's back line for the stroke: top of the body just behind the head and just in front of the rump (vertices of the baked skin, tail left out), every 0.15 s, smoothed
        void CatPoints(Pet p,float dt)
        {
            catBakeT-=dt;if(catBakeT>0)return;catBakeT=.15f;
            var smrs=p.root.GetComponentsInChildren<SkinnedMeshRenderer>();if(smrs.Length==0)return;
            if(catBake==null)catBake=new Mesh();
            var pts=new List<Vector3>();var verts=new List<Vector3>();
            for(int i=0;i<smrs.Length;i++){
                smrs[i].BakeMesh(catBake,true);catBake.GetVertices(verts);var m=smrs[i].transform.localToWorldMatrix;
                bool mask=p.smrs!=null && i<p.smrs.Length && p.smrs[i]==smrs[i] && p.tailMask!=null && p.tailMask[i]!=null && p.tailMask[i].Length==verts.Count;
                for(int v=0;v<verts.Count;v++){if(mask && p.tailMask[i][v])continue;pts.Add(m.MultiplyPoint3x4(verts[v]));}
            }
            if(pts.Count<10)return;
            float h=p.heading<-999?p.root.eulerAngles.y:p.heading;
            var f=new Vector3(Mathf.Sin(h*Mathf.Deg2Rad),0,Mathf.Cos(h*Mathf.Deg2Rad));var r=new Vector3(f.z,0,-f.x);
            var c=Vector3.zero;foreach(var q in pts)c+=q;c/=pts.Count;
            var ts=pts.Select(q=>Vector3.Dot(q-c,f)).OrderBy(t=>t).ToArray();
            float tMax=ts[(int)(ts.Length*.985f)],tMin=ts[(int)(ts.Length*.015f)];
            float Top(float t0,float w){float top=float.MinValue;foreach(var q in pts){if(Mathf.Abs(Vector3.Dot(q-c,f)-t0)<w && Mathf.Abs(Vector3.Dot(q-c,r))<.07f && q.y>top)top=q.y;}return top==float.MinValue?c.y:top;}
            float hy=Top(tMax-.07f,.035f),ty=Top(tMin+.1f,.035f),my=Top((tMax+tMin)/2,.05f);
            var headP=c+f*(tMax-.07f);var tailP=c+f*(tMin+.1f);
            headP.y=Mathf.Max(hy,my);tailP.y=Mathf.Max(ty,my);
            float k=catPoints?.35f:1f;catHeadTarget=Vector3.Lerp(catHeadTarget,headP,k);catTailTarget=Vector3.Lerp(catTailTarget,tailP,k);catPoints=true;
            catRig.CatHead=catHeadTarget;catRig.CatTail=catTailTarget;
        }
        void CatPlayApply(float dt)
        {
            if(catRig==null || model==null)return;
            if(!catOn && catRig.Weight<=0){catRig.Apply(dt);return;}
            catRig.FloorY=model.transform.position.y;
            if(catOn && catPet!=null && catPet.root!=null && catRig.Mode==CatPlayMode.Pet)CatPoints(catPet,dt);
            if(!catWalk)catRig.Tick(dt);
            catRig.Apply(dt);
            if(catRig.Phase!=catLastPhase){catLastPhase=catRig.Phase;Debug.Log("CATPLAY phase="+catLastPhase+" mode="+catRig.Mode+" w="+F(catRig.Weight)+" smile="+F(catRig.Smile)+" mouth="+F(catRig.MouthOpen));}
            if(Time.unscaledTime-catLogT>2 && catRig.Weight>.99f){catLogT=Time.unscaledTime;var rh=anim.GetBoneTransform(HumanBodyBones.RightHand);Debug.Log("ROOMPLAY_CATPLAYPOSE mode="+catRig.Mode+" phase="+catRig.Phase+" errR="+F(Vector3.Distance(rh.position,catRig.RightWristTarget))+" head=("+F(catRig.CatHead.x)+","+F(catRig.CatHead.y)+","+F(catRig.CatHead.z)+") tail=("+F(catRig.CatTail.x)+","+F(catRig.CatTail.y)+","+F(catRig.CatTail.z)+") feather=("+F(catRig.FeatherTipWorld.x)+","+F(catRig.FeatherTipWorld.y)+","+F(catRig.FeatherTipWorld.z)+")");}
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null && vrm.Runtime!=null)vrm.Runtime.Expression.SetWeight(ExpressionKey.Aa,catRig.MouthOpen*.7f);
        }
        // indoors in Korea no shoes: a separate shoe mesh of the model is hidden (the apartment and the studio); a model with shoes baked into its body stays as it is.
        // Off for now (2026-11-10): the Tripo O2/O6 models have no feet under their shoe mesh, so hiding it left bare stumps. ODDITY_SHOESOFF=1 turns the hiding back on
        static readonly string[] ShoeWords={"shoe","boot","sneaker","loafer","sandal","heel","pumps"};
        void HideIndoorShoes(GameObject m)
        {
            if(m==null)return;
            var all=m.GetComponentsInChildren<Renderer>(true);var hid=new List<string>();
            if(apt && Environment.GetEnvironmentVariable("ODDITY_SHOESOFF")=="1")foreach(var r in all){var n=r.name.ToLowerInvariant();if(ShoeWords.Any(w=>n.Contains(w))){r.enabled=false;hid.Add(r.name);}}
            if(Solo)foreach(var r in all)r.enabled=false;
            Debug.Log("ROOMPLAY_SHOES model="+Path.GetFileNameWithoutExtension(modelPath??modelName??"")+" hidden="+(hid.Count==0?"none":string.Join("|",hid))+" renderers="+string.Join("|",all.Select(r=>r.name)));
        }
        // Shift+T: lie on the bed (the K pose, on her side, head on the pillow) and watch the TV on the east wall
        bool lieTv,lieTvSat;
        bool LyingNap()=>current==NapIndex && seatIsBed && !onFloor;
        void LieTvKey()
        {
            if(sofaRig==null || model==null){Notice("이 모델로는 할 수 없음");return;}
            if(tv==null || !(apt && Apartment.HasBed && sofa!=null)){Notice("이 집에는 TV나 침대가 없음");return;}
            if(sofaOn==SofaMode.Tv && lieTv){SofaEnd("again",false);return;}
            if(sofaPend && lieTv){SofaCancel("again");return;}
            if(sofaOn!=SofaMode.None)SofaEnd("switch",true);
            if(sofaPend)SofaCancel("switch");
            if(pets.Any(o=>o.perch!=null && o.perch.name=="bed")){Notice("고양이가 침대에 있어요");return;}
            lieTv=true;lieTvSat=false;
            if(LyingNap()){SofaStart(SofaMode.Tv,"lying");return;}
            sofaWant=SofaMode.Tv;sofaPend=true;sofaPendT=0;
            if(chairSat!=null){StartChairStand(-1);return;}
            var b=Apartment.BedStand;clickSeat=null;clickWindow=false;clickGoal=new Vector2(b.x,b.z);hPathGoal=null;hReplanned=false;clickStuck=0;Notice("침대로 걸어가기");
        }
        // the bed: she walked to its side; now she lies down on her stomach along it, head to the window
        void LieOnBed()
        {
            var seat=seats.FirstOrDefault(t=>t.mark!=null && t.mark.name=="SeatBed");if(seat.mark==null){SofaCancel("no bed seat");return;}
            if(CatOnSeat(seat.mark) || pets.Any(o=>o.perch!=null && o.perch.name=="bed")){SofaCancel("cat on the bed");Notice("고양이가 침대에 있어요");return;}
            onFloor=false;faced=true;seatAt=seat.mark.position;seatIsBed=true;seatFront=seat.front;seatEdge=.30f;
            var b=Apartment.BedStand;x=b.x;z=b.z;
            yaw=heading=targetYaw=turnStart=LieYaw(BedIndex,180);turnTime=1;
            SelectRaw(BedIndex);
            sofaPend=false;SofaStart(SofaMode.Bed,"walked");
        }
        // the yaw that turns the model so the clip's hips -> head axis points along wantAxisYaw (0 = north +z, 180 = south): the clip's own axis was measured unturned
        float LieYaw(int clip,float wantAxisYaw)=>Mathf.Repeat(wantAxisYaw-lieAxisYaw[clip]+180,360)-180;
        readonly float[] lieAxisYaw=new float[MotionCount];
        void MeasureLie(int c)
        {
            if(clips[c]==null)return;
            var hips=anim.GetBoneTransform(HumanBodyBones.Hips);var headB=anim.GetBoneTransform(HumanBodyBones.Head);if(hips==null || headB==null)return;
            var keepP=model.transform.position;var keepR=model.transform.rotation;
            model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);mixer.SetInputWeight(c,1);plays[c].SetTime(0);graph.Evaluate(0);
            var axis=headB.position-hips.position;axis.y=0;
            var face=headB.rotation*headFront;face.y=0;
            lieAxisYaw[c]=axis.sqrMagnitude>1e-4f?Mathf.Atan2(axis.x,axis.z)*Mathf.Rad2Deg:0;
            Debug.Log("ROOMPLAY_LIE clip="+Keys[c].clip+" axisYaw="+F(lieAxisYaw[c])+" faceYaw="+F(face.sqrMagnitude>1e-4f?Mathf.Atan2(face.x,face.z)*Mathf.Rad2Deg:0)+" hipsY="+F(hips.position.y));
            mixer.SetInputWeight(c,0);plays[c].SetTime(0);model.transform.SetPositionAndRotation(keepP,keepR);
        }
        // Lying on the bed, after everything else has posed her: the legs are uncrossed, the head is put over the pillow (the neck bends sideways until the head's lowest vertex is on the pillow top),
        // then the model is lifted or lowered so the skinned body's lowest surface (hair excluded, see BedProbe) touches the quilt. Measured every 3rd frame (every frame while blending).
        LieRig lieRig;GameObject lieRigFor;bool lieMeshLogged;float lieLogged=-9;
        static readonly bool LieLegsOn=Environment.GetEnvironmentVariable("ODDITY_LIELEGS")!="0";
        void LieFinish()
        {
            float w=Mathf.Clamp01(weights[NapIndex]+weights[BedIndex]);
            if(!(seatIsBed && w>0 && model!=null && apt && Apartment.HasBed)){if(lieRig!=null)lieRig.Neck=0;return;}
            if(lieRig==null || lieRigFor!=model){lieRig=new LieRig(model,anim);lieRigFor=model;}
            bool pillow=weights[NapIndex]>=weights[BedIndex];
            lieRig.LegsOnQuilt=Keys[NapIndex].clip.Contains("CurlUp");
            lieRig.Place(w,pillow,Apartment.BedLieTop,LieLegsOn,false);
            if(w>.99f && Time.unscaledTime-lieLogged>2){
                lieLogged=Time.unscaledTime;var hp=anim.GetBoneTransform(HumanBodyBones.Hips).position;var head=anim.GetBoneTransform(HumanBodyBones.Head);var hd=head.position;var hc=BedProbe.HeadCentre(head,anim.GetBoneTransform(HumanBodyBones.Neck));
                if(!lieMeshLogged){lieMeshLogged=true;Debug.Log("ROOMPLAY_LIEMESH "+lieRig.Describe());}Debug.Log("ROOMPLAY_LIEGROUND lowest="+F(lieRig.LowestY)+" trueLowest="+F(lieRig.TrueLowestY)+" top="+F(Apartment.BedLieTop)+" headLow="+F(lieRig.HeadLowY)+" pillowTop="+F(Apartment.PillowTopAt(hc.x,hc.z))+" neck="+F(lieRig.Neck)+" legsApart="+F(lieRig.LegsApart)+" head=("+F(hd.x)+","+F(hd.y)+","+F(hd.z)+") modelYaw="+F(model.transform.eulerAngles.y)+" axisNow="+F(Mathf.Atan2(hd.x-hp.x,hd.z-hp.z)*Mathf.Rad2Deg)+" hips=("+F(hp.x)+","+F(hp.y)+","+F(hp.z)+") pillow="+(pillow?1:0)+" meshes="+lieRig.Meshes);
            }
        }
        // V on the bed: sitting on the quilt, knees up, feet flat on it, grounded with the skin probe (see CurlRig)
        CurlRig curlRig;GameObject curlRigFor;float curlLogged=-9;
        void CurlBedFinish()
        {
            float w=Mathf.Clamp01(weights[CurlIndex]);
            if(!(seatIsBed && !onFloor && w>0 && model!=null && apt && Apartment.HasBed && chairSat==null))return;
            if(curlRig==null || curlRigFor!=model){curlRig=new CurlRig(model,anim);curlRigFor=model;}
            curlRig.Place(w,seatFront,Apartment.BedLieTop,false);
            if(w>.99f && Time.unscaledTime-curlLogged>2){curlLogged=Time.unscaledTime;var lh=anim.GetBoneTransform(HumanBodyBones.LeftHand).position;var lk=anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position;var la=anim.GetBoneTransform(HumanBodyBones.LeftFoot).position;Debug.Log("ROOMPLAY_CURLBED lhand=("+F(lh.x)+","+F(lh.y)+","+F(lh.z)+") lknee=("+F(lk.x)+","+F(lk.y)+","+F(lk.z)+") lankle=("+F(la.x)+","+F(la.y)+","+F(la.z)+") lowest="+F(curlRig.LowestY)+" trueLowest="+F(curlRig.TrueLowestY)+" top="+F(Apartment.BedLieTop));}
        }
        void SofaTick(float dt)
        {
            if(tv!=null){tv.On=sofaOn==SofaMode.Tv || (workoutOn && !workoutWalk);tv.Tick(dt);}
            if(sofaRig==null || model==null)return;
            if(sofaPend){
                sofaPendT+=dt;
                if(lieTv){
                    if(LyingNap())SofaStart(SofaMode.Tv,"walked");
                    else if(!clickGoal.HasValue && sofaPendT>.05f && !lieTvSat){if(current==10)SofaCancel("walk key");else if(current==9 || current==SitIndex){lieTvSat=true;Sit(NapIndex,true);}else SofaCancel("busy");}
                }else if(sofaWant==SofaMode.Bed){
                    if(!clickGoal.HasValue && sofaPendT>.05f){if(current==10)SofaCancel("walk key");else if(current==9 || current==SitIndex)LieOnBed();else SofaCancel("busy");}
                }else if(SeatedOnSofa())SofaStart(sofaWant,"walked");
                else if(!clickGoal.HasValue && chairSat==null && chairJob==null && sofaPendT>.1f && current!=SitIndex)SofaCancel("no walk");
                if(sofaPend && sofaPendT>30)SofaCancel("timeout");
            }
            if(sofaOn==SofaMode.Phone || sofaOn==SofaMode.Tv){if(lieTv && sofaOn==SofaMode.Tv?!LyingNap():!SeatedOnSofa())SofaEnd("moved",true);}
            else if(sofaOn==SofaMode.Bed && !LyingOnBed())SofaEnd("moved",true);
            if(sofaRig.Weight<=0 && sofaOn==SofaMode.None){sofaRig.Apply(dt);return;}
            sofaRig.TvCentre=tv!=null?tv.ScreenCentre:sofaRig.TvCentre;
            float ly=LieYawNow();sofaRig.BedForward=new Vector3(Mathf.Sin(ly*Mathf.Deg2Rad),0,Mathf.Cos(ly*Mathf.Deg2Rad));sofaRig.MattressTop=Apartment.HasBed?Apartment.BedLieTop:0;
            sofaRig.Tick(dt);sofaRig.Apply(dt);
            var vrm=model.GetComponent<Vrm10Instance>();
            if(vrm!=null && sofaRig.Weight>.05f){vrm.LookAtTargetType=VRM10ObjectLookAt.LookAtTargetTypes.SpecifiedTransform;vrm.LookAtTarget=sofaRig.LookTarget;}
            if((sofaOn==SofaMode.Phone || sofaOn==SofaMode.Tv) && !lieTv)OffBackrest(Mathf.Clamp01(weights[SitIndex])*sofaRig.Weight); // leaning back must not put her back through the backrest
            if(!sofaLogged && sofaOn!=SofaMode.None && sofaRig.Weight>.99f){sofaLogged=true;Debug.Log("SOFAMOTION mode="+SofaWord(sofaOn)+" head="+F(sofaRig.LastHeadPitch)+" hand="+F(sofaRig.LastHandLeft)+","+F(sofaRig.LastHandRight)+" phone=("+F(sofaRig.PhoneCentre.x)+","+F(sofaRig.PhoneCentre.y)+","+F(sofaRig.PhoneCentre.z)+") thumb="+(sofaRig.HasThumb?1:0));}
        }
        // the way her head points along the bed when she lies prone: the lie yaw plus the clip's own axis
        float LieYawNow()=>yaw-bodyFix+lieAxisYaw[BedIndex];
        string KeyText(int i){var k=Keys[i].key;if(k=="b")return "침대에 엎드려 핸드폰 보기";if(one && k=="p")return "침대에 앉아 핸드폰";if(one && k=="t")return "침대에서 TV 보기 (Shift+T 누워서)";return Keys[i].clip;}
        void OnGUI()
        {
            var ev=Event.current;
            if(ev!=null && ev.keyCode==KeyCode.Tab){
                if(ev.type==EventType.KeyDown){if(!tabGuiDown){tabGuiDown=true;TabPress("ongui");}ev.Use();GUIUtility.keyboardControl=0;}
                else if(ev.type==EventType.KeyUp){tabGuiDown=false;ev.Use();}
            }
            if(noHud)return; // ODDITY_NOHUD=1: clean screenshots
            bool cat=ctl!=null;
            int rows=cat?ActionNames.Length+CatCommonKeys.Length:Keys.Length+2;
            float listEnd=34+rows*17;GUI.Box(new Rect(10,10,340,listEnd-6),"");var style=new GUIStyle(GUI.skin.label){fontSize=12};
            GUI.Label(new Rect(16,13,330,20),cat?(ctl.kind=="white"?"흰 고양이":ctl.kind=="tabby"?"줄무늬 고양이":"소말리 고양이"):modelName??"준비 중",style);
            for(int i=0;i<rows;i++){
                if(cat){
                    bool on=i<ActionNames.Length && i==CurAction(ctl);style.normal.textColor=on?Color.yellow:i<ActionNames.Length && !CanDo(ctl,i)?Color.gray:Color.white;
                    GUI.Label(new Rect(16,34+i*17,330,18),(on?"> ":"  ")+CatRow(ctl,i),style);
                    continue;
                }
                if(i==Keys.Length){style.normal.textColor=faceHeld!=null?Color.yellow:Color.white;GUI.Label(new Rect(16,34+i*17,330,18),"  Shift+1→0  표정 "+(faceHeld??"(0 원래대로)"),style);continue;}
                if(i==Keys.Length+1){style.normal.textColor=Color.white;GUI.Label(new Rect(16,34+i*17,330,18),"  Shift+← →  카메라 좌우 돌기, Shift+↑ ↓  카메라 높이",style);continue;}
                bool selected=i==current || (Keys[i].key=="p" && sofaOn==SofaMode.Phone) || (Keys[i].key=="t" && sofaOn==SofaMode.Tv) || (Keys[i].key=="e" && callOn) || (Keys[i].key=="q" && coffeeOn) || (Keys[i].key=="x" && curtainOn) || (Keys[i].key=="comma" && (wakeOn || wakePend)) || (Keys[i].key=="period" && workoutOn) || (Keys[i].key=="semicolon" && ramenOn) || (Keys[i].key=="quote" && mealOn) || (Keys[i].key=="slash" && (laundryOn || laundryPend)) || (Keys[i].key=="lbracket" && (mirrorOn || mirrorPend)) || (Keys[i].key=="rbracket" && catOn);
                style.normal.textColor=selected?Color.yellow:clips!=null && i<MotionCount && clips[i]==null?Color.gray:Color.white;
                GUI.Label(new Rect(16,34+i*17,330,18),(selected?"> ":"  ")+(Keys[i].key=="comma"?",":Keys[i].key=="period"?".":Keys[i].key=="semicolon"?";":Keys[i].key=="quote"?"'":Keys[i].key=="slash"?"/":Keys[i].key=="lbracket"?"[":Keys[i].key=="rbracket"?"]":Keys[i].key.Length==1?Keys[i].key.ToUpperInvariant():Keys[i].key)+"  "+KeyText(i),style);
            }
            if(Time.realtimeSinceStartup<messageUntil)GUI.Label(new Rect(16,listEnd+4,900,40),message,new GUIStyle(GUI.skin.label){fontSize=24});
            // who is controlled: top right, white with a dark outline; it pops (1.3 -> 1 in 0.2 s) and flashes when Tab hands control over
            float age=Time.realtimeSinceStartup-labelPop;
            var flash=cat?(ctl.kind=="white"?new Color(.62f,.80f,1f):ctl.kind=="tabby"?new Color(.72f,.50f,.22f):new Color(1f,.62f,.30f)):Color.yellow;
            string who=cat?ctl.tag:"HARIN";
            var tint=age<.35f?Color.Lerp(flash,Color.white,Mathf.Clamp01(age/.35f)):Color.white;
            var big=new GUIStyle(GUI.skin.label){fontSize=18,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleRight};
            var rect=new Rect(Screen.width-230,12,220,32);var saved=GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one*(age<.2f?Mathf.Lerp(1.3f,1f,age/.2f):1f),new Vector2(rect.xMax,rect.center.y));
            big.normal.textColor=new Color(0,0,0,.9f);
            foreach(var o in new[]{new Vector2(-2,0),new Vector2(2,0),new Vector2(0,-2),new Vector2(0,2),new Vector2(-1.5f,-1.5f),new Vector2(1.5f,-1.5f),new Vector2(-1.5f,1.5f),new Vector2(1.5f,1.5f)})GUI.Label(new Rect(rect.x+o.x,rect.y+o.y,rect.width,rect.height),who,big);
            big.normal.textColor=tint;GUI.Label(rect,who,big);
            GUI.matrix=saved;
            if(LifeAuto){var auto=new GUIStyle(GUI.skin.label){fontSize=13,alignment=TextAnchor.MiddleRight};var ra=new Rect(Screen.width-230,40,220,22);auto.normal.textColor=new Color(0,0,0,.9f);GUI.Label(new Rect(ra.x+1,ra.y+1,ra.width,ra.height),"자동",auto);auto.normal.textColor=new Color(.7f,1f,.75f);GUI.Label(ra,"자동",auto);}
            if(!catSfxOn){var small=new GUIStyle(GUI.skin.label){fontSize=13,alignment=TextAnchor.MiddleRight};var r2=new Rect(Screen.width-230,LifeAuto?60:46,220,22);
                small.normal.textColor=new Color(0,0,0,.9f);GUI.Label(new Rect(r2.x+1,r2.y+1,r2.width,r2.height),"고양이 소리 끔",small);small.normal.textColor=new Color(1f,.85f,.6f);GUI.Label(r2,"고양이 소리 끔",small);}
        }
        // ---- HarinLife (HarinLife.cs): when nobody steers Harin she picks her next activity by weighted random and runs it through the same key functions the player uses.
        // Any Harin key, walking or click hands her to the player at once (ROOMPLAY_LIFE off); ODDITY_LIFEIDLE seconds (20) without Harin input give her back. ODDITY_AUTOLIFE=0 off, =1 on; by default on in the studio, off in the other houses and in script runs. ODDITY_LIFESEED=n repeats a run.
        LifeControl life;System.Random lifeRng;bool lifeInit,lifeEnabled,lifeEngaged;LifeAct lifeAct=LifeAct.None,lifeLast=LifeAct.None;float lifeSecs,lifeT,lifeSince,lifeGap;
        void LifeSetup()
        {
            lifeInit=true;var e=Environment.GetEnvironmentVariable("ODDITY_AUTOLIFE");
            lifeEnabled=e=="0"?false:e=="1"?true:(one && !scripted && !Solo);
            if(!lifeEnabled)return;
            float il=20f;if(!float.TryParse(Environment.GetEnvironmentVariable("ODDITY_LIFEIDLE"),NumberStyles.Float,CultureInfo.InvariantCulture,out il) || il<=0)il=20f;
            int seed;bool seeded=int.TryParse(Environment.GetEnvironmentVariable("ODDITY_LIFESEED"),out seed);lifeRng=seeded?new System.Random(seed):new System.Random();
            life=new LifeControl(il,true);lifeGap=3f;
            Debug.Log("ROOMPLAY_LIFE on idle="+F(il)+(seeded?" seed="+seed:""));
        }
        bool LifeAuto=>lifeEnabled && life!=null && life.Auto;
        void LifeInput(string by)
        {
            if(!lifeEnabled || life==null)return;
            if(life.Input()){Debug.Log("ROOMPLAY_LIFE off by="+by+" act="+lifeAct);lifeAct=LifeAct.None;lifeEngaged=false;}
        }
        bool LifeCatOnFloor()
        {
            foreach(var o in pets)if(o!=ctl && o.root!=null && o.graph.IsValid() && o.somali && o.perch==null && !o.onSofa && o.job==0 && o.hopPlan.Count==0 && o.sit!=12 && o.sit!=13)return true;
            return false;
        }
        void LifeStopActs(string why){SofaStop(why);CallEnd(why);CoffeeEnd(why);CurtainEnd(why);WakeEnd(why);WorkoutEnd(why);EndNewActs(why);}
        bool LifeSitting()=>current==SitIndex || current==CurlIndex || current==NapIndex || current==BedIndex;
        bool LifeBusy()=>clickGoal.HasValue || chairJob!=null || sofaPend || callOn || coffeeOn || curtainOn || wakeOn || wakePend || workoutOn || ramenOn || mealOn || laundryOn || laundryPend || mirrorOn || mirrorPend || catOn || sofaOn!=SofaMode.None;
        // true once the act is really under way (she has walked there and started), false while she is still walking to it
        bool LifeEngagedNow(LifeAct a)
        {
            switch(a){
                case LifeAct.Phone:return sofaOn==SofaMode.Phone;
                case LifeAct.Tv:case LifeAct.LieTv:return sofaOn==SofaMode.Tv;
                case LifeAct.Bed:return sofaOn==SofaMode.Bed;
                case LifeAct.Nap:return current==NapIndex && weights[NapIndex]>.95f && !clickGoal.HasValue;
                case LifeAct.Coffee:return coffeeOn && !coffeeWalk;
                case LifeAct.Call:return callOn;
                case LifeAct.Wake:return wakeOn;
                case LifeAct.Workout:return workoutOn && !workoutWalk;
                case LifeAct.Ramen:return ramenOn && !ramenWalk;
                case LifeAct.Meal:return mealOn && !mealWalk;
                case LifeAct.Laundry:return laundryOn;
                case LifeAct.Mirror:return mirrorOn;
                case LifeAct.Pet:case LifeAct.Wand:return catOn && !catWalk;
                case LifeAct.CurtainClose:case LifeAct.CurtainOpen:return curtainOn && !curtainWalk;
                case LifeAct.WindowStand:case LifeAct.RandomWalk:return !clickGoal.HasValue && lifeSince>.4f && direction==0;
                case LifeAct.Yawn:return current==YawnIndex;
                case LifeAct.HairSweep:return current==SweepIndex;
                case LifeAct.HairToss:return current==TossIndex;
            }
            return false;
        }
        // false once an engaged act has stopped on its own
        bool LifeStillOn(LifeAct a)
        {
            switch(a){
                case LifeAct.Phone:return sofaOn==SofaMode.Phone;
                case LifeAct.Tv:case LifeAct.LieTv:return sofaOn==SofaMode.Tv;
                case LifeAct.Bed:return sofaOn==SofaMode.Bed;
                case LifeAct.Nap:return current==NapIndex;
                case LifeAct.Coffee:return coffeeOn;
                case LifeAct.Call:return callOn;
                case LifeAct.Wake:return wakeOn;
                case LifeAct.Workout:return workoutOn;
                case LifeAct.Ramen:return ramenOn;
                case LifeAct.Meal:return mealOn;
                case LifeAct.Laundry:return laundryOn;
                case LifeAct.Mirror:return mirrorOn;
                case LifeAct.Pet:case LifeAct.Wand:return catOn;
                case LifeAct.CurtainClose:case LifeAct.CurtainOpen:return curtainOn;
                case LifeAct.Yawn:return current==YawnIndex;
                case LifeAct.HairSweep:return current==SweepIndex;
                case LifeAct.HairToss:return current==TossIndex;
            }
            return true; // window stand and random walk are timed or one step
        }
        void LifeEnd(string why)
        {
            Debug.Log("ROOMPLAY_LIFE end act="+lifeAct+" why="+why+" ran="+F(lifeT)+"s walk="+F(lifeSince-lifeT)+"s at=("+F(x)+","+F(z)+")");
            var a=lifeAct;lifeLast=a;lifeAct=LifeAct.None;lifeEngaged=false;lifeGap=1f+(float)lifeRng.NextDouble();
            if(a==LifeAct.Yawn || a==LifeAct.HairSweep || a==LifeAct.HairToss){if(direction==0 && current!=9)Select(9);}
            else if(a!=LifeAct.Nap)LifeStopActs("life "+why);
        }
        void LifeBegin()
        {
            if(direction!=0 || clickGoal.HasValue || busy || chairJob!=null)return; // wait until she stands still
            var ctx=new LifeContext{Last=lifeLast,Night=LampEnv || Environment.GetEnvironmentVariable("ODDITY_NIGHT")=="1",CurtainOpen=curtain!=null && curtain.Openness>.5f,CatOnFloor=LifeCatOnFloor()};
            var pick=HarinLife.Pick(lifeRng,ctx);
            lifeAct=pick.Act;lifeSecs=pick.Seconds;lifeT=0;lifeSince=0;lifeEngaged=false;
            Debug.Log("ROOMPLAY_LIFE pick="+pick.Act+" for="+(pick.Seconds>0?F(pick.Seconds):"auto")+"s from=("+F(x)+","+F(z)+") night="+(ctx.Night?1:0)+" curtainOpen="+(ctx.CurtainOpen?1:0)+" cat="+(ctx.CatOnFloor?1:0));
            bool sofaAct=pick.Act==LifeAct.Phone || pick.Act==LifeAct.Tv || pick.Act==LifeAct.LieTv || pick.Act==LifeAct.Bed;
            if(!sofaAct)LifeStopActs("life next"); // an act of the player's own that is still running must not be toggled off by the same key
            else if(sofaOn!=SofaMode.None)LifeStopActs("life next");
            switch(pick.Act){
                case LifeAct.Phone:SofaKey(SofaMode.Phone);break;
                case LifeAct.Tv:SofaKey(SofaMode.Tv);break;
                case LifeAct.LieTv:LieTvKey();break;
                case LifeAct.Bed:SofaKey(SofaMode.Bed);break;
                case LifeAct.Nap:Sit(NapIndex,true);break;
                case LifeAct.Coffee:CoffeeKey();break;
                case LifeAct.Call:CallKey();break;
                case LifeAct.Wake:WakeKey();break;
                case LifeAct.Workout:WorkoutKey();break;
                case LifeAct.Ramen:RamenKey();break;
                case LifeAct.Meal:MealKey();break;
                case LifeAct.Laundry:LaundryKey();break;
                case LifeAct.Mirror:MirrorKey();break;
                case LifeAct.Pet:CatPlayKey(false);break;
                case LifeAct.Wand:CatPlayKey(true);break;
                case LifeAct.CurtainClose:CurtainKey(false);break;
                case LifeAct.CurtainOpen:CurtainKey(true);break;
                case LifeAct.WindowStand:
                    if(LifeSitting())Select(9);
                    clickSeat=null;clickWindow=true;clickGoal=WinSpotNow;hPathGoal=null;hReplanned=false;clickStuck=0;break;
                case LifeAct.RandomWalk:{
                    if(LifeSitting())Select(9);
                    var floors=new List<Rect>{Apartment.WanderBoxes.Length>0?Apartment.WanderBoxes[0]:Rect.MinMaxRect(LimX0,ZNear,LimX1,ZFar)};
                    if(PacePlan.TryNext(floors,blockers,new Vector2(x,z),lifeRng,out var step)){clickSeat=null;clickWindow=false;clickGoal=step.Target;hPathGoal=null;hReplanned=false;clickStuck=0;}
                    break;}
                case LifeAct.Yawn:if(clips[YawnIndex]!=null)Select(YawnIndex);break;
                case LifeAct.HairSweep:StartGesture(SweepIndex);break;
                case LifeAct.HairToss:StartGesture(TossIndex);break;
            }
        }
        void LifeTick(float dt)
        {
            if(!lifeInit)LifeSetup();
            if(!lifeEnabled || model==null || busy || orbitOn)return;
            if(life.Tick(dt)){Debug.Log("ROOMPLAY_LIFE resume");lifeAct=LifeAct.None;lifeEngaged=false;lifeGap=.5f;}
            if(!life.Auto)return;
            if(lifeAct==LifeAct.None){lifeGap-=dt;if(lifeGap<=0)LifeBegin();return;}
            lifeSince+=dt;
            if(!lifeEngaged){
                if(LifeEngagedNow(lifeAct)){lifeEngaged=true;lifeT=0;Debug.Log("ROOMPLAY_LIFE engaged act="+lifeAct+" walk="+F(lifeSince)+"s at=("+F(x)+","+F(z)+")");}
                else if(lifeSince>3f && !LifeBusy() && !(lifeAct==LifeAct.Nap && current==NapIndex) && current!=YawnIndex && !HairGestures.IsGesture(current)){LifeEnd("refused");return;}
                else if(lifeSince>60f){LifeEnd("walk timeout");return;}
                if(!lifeEngaged)return;
            }
            lifeT+=dt;
            if(lifeAct==LifeAct.Yawn){if(lifeT>=(clips[YawnIndex]!=null?clips[YawnIndex].length:3f) || current!=YawnIndex)LifeEnd("done");return;}
            if(!LifeStillOn(lifeAct)){LifeEnd("ended by itself");return;}
            if(lifeSecs>0){if(lifeT>=lifeSecs)LifeEnd("time");}
            else if(lifeAct==LifeAct.RandomWalk)LifeEnd("arrived");
            else if(lifeT>HarinLife.Cap(lifeAct))LifeEnd("cap");
        }
        static void SafeDispose(System.IDisposable d){try{d?.Dispose();}catch(System.Exception e){Debug.LogWarning("ROOMPLAY_DISPOSE "+e.GetType().Name);}}
        void OnDestroy(){foreach(var pet in pets)if(pet.graph.IsValid())pet.graph.Destroy();SafeDispose(zeroTwoAudio);SafeDispose(zeroTwoDance);SafeDispose(sofaRig);SafeDispose(callRig);SafeDispose(coffeeRig);SafeDispose(curtainRig);SafeDispose(wakeRig);SafeDispose(workoutRig);SafeDispose(ramenRig);SafeDispose(mealRig);SafeDispose(laundryRig);SafeDispose(mirrorRig);SafeDispose(catRig);SafeDispose(curtain);SafeDispose(tv);if(clips!=null && clips[ZeroTwoIndex]!=null)Destroy(clips[ZeroTwoIndex]);DestroyGestureClips(clips);if(graph.IsValid())graph.Destroy();if(boundsMesh!=null)Destroy(boundsMesh);if(soleBake!=null)Destroy(soleBake);EditorSettings.enterPlayModeOptionsEnabled=oldEnabled;EditorSettings.enterPlayModeOptions=oldOptions;}
    }
}
