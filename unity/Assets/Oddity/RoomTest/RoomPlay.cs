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
            ("a", "CatwalkSkin"), ("shift", "KaRun01"), ("f", "UAL1Standard#Sword_Attack"), ("z", "ZeroTwoDance"), ("0", "KaIdle73IdolPose"), ("c", "KaSitPutHandBetweensLoop"), ("v", "KaSitWithBothLegsUpLoop"), ("y", "MxYawn"), ("k", "KaSleepTurnToTheSideLoop"), ("d", "CatwalkSkin"), ("w", "CatwalkSkin"), ("s", "CatwalkSkin"), ("left", "누르는 동안 시계 방향 회전"), ("right", "누르는 동안 반시계 회전"), ("n", "다음 모델, Shift+N 이전 모델"),("r", "현재 모델 다시 불러오기"), ("l", "주인공(카메라) 바라보기 켜고 끄기"), ("h", "식탁 의자에서 턱 괴기 켜고 끄기"), ("g", "다음 걷기"), ("escape", "재생 끝내기"), ("up", "줌 인"), ("down", "줌 아웃"), ("m", "고양이(소말리) 동작 바꾸기"), ("tab", "Tab 하린, 고양이, 소말리, 흰 고양이 번갈아 조종"), ("o", "O 고양이 소리 켜고 끄기")
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
        public const int MotionCount=19;
        public const int ZeroTwoIndex=13, IdolIndex=14, SitIndex=15, CurlIndex=16, YawnIndex=17, NapIndex=18; // index 9 is the idle, on space only
        ZeroTwoDance zeroTwoDance;
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
        float walkTwist, runTwist, bodyFix, speed, runSpeed, x, z, heading=180, yaw=180, turnStart=180, targetYaw=180, turnTime=1, blend=1;
        int current=9, direction, blockedDirection;
        float Limit=1.08f, ZNear=-.6f, ZFar=15f; // the room view widens these to its floor
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
                Debug.Log("ZEROTWO_AUDIO loaded="+zeroTwoSource.clip.name+" seconds="+F(zeroTwoSource.clip.samples/(float)zeroTwoSource.clip.frequency)+" samples="+zeroTwoSource.clip.samples+" imported="+(zeroTwoTrack!=null));
                cameraView=cam;cam.aspect=16f/9;Smooth(cam);Letterbox();cam.gameObject.AddComponent<AudioListener>();room=Environment.GetEnvironmentVariable("ODDITY_ROOM")!="0";if(room)Room(cam);else Background(cam);blob=RoomTestRunner.MakeBlob();blob.localScale=new Vector3(.75f,.4f,1);
                SetWalk(Environment.GetEnvironmentVariable("ODDITY_PLAYWALK")??"KaWalk01");
                var sitClip=Environment.GetEnvironmentVariable("ODDITY_PLAYSIT");if(!string.IsNullOrWhiteSpace(sitClip))Keys[SitIndex].clip=sitClip; // which clip C sits with
                var curlClip=Environment.GetEnvironmentVariable("ODDITY_PLAYCURL");if(!string.IsNullOrWhiteSpace(curlClip))Keys[CurlIndex].clip=curlClip; // and V
                var napClip=Environment.GetEnvironmentVariable("ODDITY_PLAYNAP");if(!string.IsNullOrWhiteSpace(napClip))Keys[NapIndex].clip=napClip; // and K
                LoadModel(Environment.GetEnvironmentVariable("ODDITY_MODEL")??NewestInTest(DefaultLine)??NewestInTest(null)??"Assets/Models/test/harin-6T-I1-30.vrm"); // the newest O2 build by default, else the newest vrm of any line
                PlaceSofa();
                BuildPerches();
                SpawnPets();
                var text=Environment.GetEnvironmentVariable("ODDITY_PLAYKEYS");scripted=!string.IsNullOrWhiteSpace(text);
                if(scripted)foreach(var part in text.Split(',')){
                    var pair=part.Split(':');
                    if(pair.Length!=2 || !float.TryParse(pair[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds) || seconds<=0 || !(pair[0]=="shifts" || pair[0]=="realtab" || pair[0]=="wait" || pair[0].StartsWith("click") || pair[0]=="wheelup" || pair[0]=="wheeldown" || Keys.Any(k=>k.key==pair[0])))throw new Exception("invalid ODDITY_PLAYKEYS entry");
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
        void LoadModel(string path)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab==null && File.Exists(path)){AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);} // a model dropped in while the editor was closed may not be imported yet
            if(prefab==null)throw new Exception("model asset missing: "+Path.GetFileName(path));
            var candidate=Instantiate(prefab);
            foreach(var renderer in candidate.GetComponentsInChildren<SkinnedMeshRenderer>())renderer.updateWhenOffscreen=true; // the close shot culls the head on its stale bounds
            var a=candidate.GetComponent<Animator>();
            if(a==null || a.avatar==null || !a.avatar.isHuman){ShowStatue(candidate,path);return;} // a bare mesh (no rig yet) stands still so its shape can be seen
            var puppet=candidate.AddComponent<HarinPuppet>();puppet.SetFace("neutral");puppet.enabled=false;
            var oldDance=zeroTwoDance;var oldModel=model;var oldGraph=graph;var oldMixer=mixer;var oldPlays=plays;
            var oldClips=clips;var oldAnchors=anchors;var oldTravels=travels;var oldWeights=weights;var oldFrom=from;
            float oldSpeed=speed, oldBlend=blend;
            double phase=plays!=null && clips[current]!=null ? plays[current].GetTime()/clips[current].length : 0;
            soleMesh=null;model=candidate;anim=a;RestFeet();Arm();anim.applyRootMotion=false;
            try{zeroTwoDance=new ZeroTwoDance(anim);BuildMotion();if(clips[current]!=null)plays[current].SetTime(phase*clips[current].length);Evaluate(0);}
            catch{zeroTwoDance?.Dispose();zeroTwoDance=oldDance;if(graph.IsValid())graph.Destroy();graph=oldGraph;mixer=oldMixer;plays=oldPlays;clips=oldClips;anchors=oldAnchors;travels=oldTravels;weights=oldWeights;from=oldFrom;speed=oldSpeed;blend=oldBlend;model=oldModel;anim=oldModel!=null?oldModel.GetComponent<Animator>():null;Destroy(candidate);throw;}
            modelName=Path.GetFileNameWithoutExtension(path);modelPath=path;LogSprings(candidate);
            if(statue!=null){Destroy(statue);statue=null;}
            oldDance?.Dispose();if(oldGraph.IsValid())oldGraph.Destroy();if(oldClips!=null && oldClips[ZeroTwoIndex]!=null)Destroy(oldClips[ZeroTwoIndex]);if(oldModel!=null)Destroy(oldModel);
        }
        // a mesh without a Humanoid Avatar (a fresh Tripo glb): no motion, it stands on the floor at its own size (scaled to 1.6 m when it is far off),
        // left and right arrows turn it, up and down arrows zoom, N, Shift+N and R still step through the lines, Esc ends
        GameObject statue;const float StatueFlip=180; // a glTF mesh comes in facing away from the VRM front
        void ShowStatue(GameObject candidate,string path)
        {
            zeroTwoDance?.Dispose();zeroTwoDance=null;if(graph.IsValid())graph.Destroy();
            if(clips!=null && clips[ZeroTwoIndex]!=null)Destroy(clips[ZeroTwoIndex]);clips=null;plays=null;
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
        static void Face(Transform root,Vector3 dir){root.rotation=Quaternion.FromToRotation(Facing(root),dir)*root.rotation;}
        void PlaceSofa()
        {
            if(Environment.GetEnvironmentVariable("ODDITY_SOFA")=="0")return;
            seats.Clear();
            var s=LoadProp("sofa");
            if(s!=null){sofa=s.transform;sofa.position=room?new Vector3(-.75f,0,1.5f):new Vector3(0,0,SofaZ);Face(sofa,Vector3.back); // the seat faces the camera
                Leather(sofa);
                foreach(var t in sofa.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Seat")).OrderBy(t=>t.position.x))seats.Add((t,Facing(sofa),StandOff+.2f));} // the sofa seat is deep, she gets up clear of it
            var table=LoadProp("table");
            tableRoot=table!=null?table.transform:null;
            if(table!=null){table.transform.position=room?new Vector3(.70f,0,-.45f):new Vector3(0,0,TableZ);Face(table.transform,Vector3.right);
                foreach(var spot in table.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Chair")).OrderBy(t=>t.name).ToList()){
                    var chair=LoadProp("chair");if(chair==null)break;
                    chair.transform.SetPositionAndRotation(spot.position,spot.rotation);chair.name=spot.name;
                    var seat=Mark(chair.transform,"Seat");if(seat!=null)seats.Add((seat,Facing(chair.transform),-StandOff)); // a chair is left from behind, the table is in front
                }}
            if(tableRoot!=null)Laptop(tableRoot);
            if(sofa==null && seats.Count>0)sofa=seats[0].mark.root;
            propHits.Clear();foreach(var root in seats.Select(t=>t.mark.root).Distinct())foreach(var mf in root.GetComponentsInChildren<MeshFilter>())if(mf.sharedMesh!=null){var c=mf.gameObject.AddComponent<MeshCollider>();c.sharedMesh=mf.sharedMesh;propHits.Add(c);} // only for PropTop's rays
            clickHits.Clear();clickHits.AddRange(propHits);
            if(tableRoot!=null)foreach(var mf in tableRoot.GetComponentsInChildren<MeshFilter>())if(mf.sharedMesh!=null){var c=mf.gameObject.AddComponent<MeshCollider>();c.sharedMesh=mf.sharedMesh;clickHits.Add(c);}
            clickHits.AddRange(perchCols);
            Blockers();
            BuildSofaSpots();
            Debug.Log("ROOMPLAY_PROPS seats="+string.Join(" ",seats.Select(t=>t.mark.root.name+"/"+t.mark.name+"("+F(t.mark.position.x)+","+F(t.mark.position.y)+","+F(t.mark.position.z)+" face "+F(t.front.x)+","+F(t.front.z)+")")));
        }

        // the pets: the Sketchfab "Somali Cat" (SOMALI) and the realistic white cat (WHITE), both git-ignored. Each is on the floor facing the camera
        // at an angle, 0.30 m tall (the Somali 0.22 m). M cycles the controlled pet's clips in place. ODDITY_CAT=0 leaves out both, ODDITY_SOMALI=0 only the Somali; missing files only log a warning.
        // The Somali glb goes through UniGLTF like the props; its clips come out Legacy, so RoomPlay copies them as Mecanim clips. ODDITY_CAT=0 leaves out both cats, ODDITY_SOMALI=0 only the Somali.
        sealed class Pet
        {
            public string tag,kind="somali";public bool somali,white;public int oneClip=13;public float walkSpd=.5f,runSpd=1.2f,aiSpd=.35f,walkClipSpd=1; // somali: a pet with rest poses; white: also Groom, Stretch, Jump (both cats once their shared glb is loaded: kind says which one it is; oneClip: the standing one-shot clip that sit 11 plays); walkSpd/runSpd/aiSpd: m/s
            public string[] clipNames;public AnimationClip[] clips;
            public Transform root;public PlayableGraph graph;public AnimationPlayableOutput outp;
            public int index;public bool moving;public float heading=-1000;
            public bool onSofa;public Transform seat;public float baseY;
            public Vector2? goal;public string goalKind;public Transform goalSeat;public Vector2 goalLook;public float faceTo=float.NaN,stuck;
            public int job;public float jobT,jobDur,jobArc,jobHead;public Vector3 jobFrom,jobTo;public Transform jobSeat;
            public bool groomAfter; // white: the SitDown that is playing leads into Groom
            public int sit; // Somali and white only (10 grooming, 11 stretching): 0 standing, 1 SitDown playing, 2 sitting (SittingIdle), 3 StandUp playing, 4 LieDown, 5 lying (LyingIdle, sphinx), 6 GetUp, 7 LoafDown, 8 loafing (LoafIdle), 9 LoafUp
            public bool hasLie;public int restNext,arriveRest;public float settledAt; // restNext: the rest pose (1 sit, 2 lie, 3 loaf) to start when the Up clip ends; arriveRest: the pose to take on landing on the sofa or a perch (0 = sit); settledAt: when it last stopped moving, for the hiss
            public int mStep;
            public AudioSource shot,purr;
            public int loopCount;public float walkStart,idleTime,nextSoft,purrTarget;public bool purrOn;
            public Perch perch,jobPerch;public string chainNext;public List<string> hopPlan=new List<string>();public float hopWait;public bool auto; // perch: a high place it sits on now (null: floor or sofa); chainNext: counter first, then the fridge; hopPlan: the hops still to fly, planned at the start (fridge>counter>floor, floor>counter>fridge), no rest pose between them
            public List<Vector2> path;public int pathI;public Vector2? pathGoal;public Vector2 pathHarin;public bool replanned; // the planned walk to goal (FloorPath): waypoints, next one, the goal it was made for, where Harin was, whether the stuck replan is used
            public float surf,surfBase;public Vector3 fitFront,fitSide;public float[] ext=new float[4];public readonly List<Vector3> lowPts=new List<Vector3>();public float lastFit; // surfBase: the cushion height where it landed; fitFront/fitSide: the horizontal axes its footprint is measured on; ext: min,max of the footprint along each from the root; lowPts: the non-tail vertices within 3 cm of the lowest (the paws)
            public SkinnedMeshRenderer[] smrs;public bool[][] tailMask;public Mesh bake;public string gapKey=""; // surf: height of the surface it rests on (floor 0); the rest of these measure its lowest vertex
            public Spot spot,after,transit,fitSpot,jobSpot;public float radius=.2f,relocAt;public Vector2 cOff; // spot: the perch / cushion spot it holds (going there, or standing on it); after: the final spot when it passes through another (fridge via counter); transit: a spot it only hops through; fitSpot: the cushion it landed on; radius, cOff: the footprint circle (centre = root + rotated cOff)
            public bool[][] pawMask;public bool pawFallback;public int pawSeeds;public readonly List<Vector3> conPts=new List<Vector3>();public float pawLogAt; // pawMask: vertices mainly weighted to a paw / foot / toe bone; conPts: the lowest vertex of each paw and the lowest body vertices, each rests on its own surface
            public bool aiPaused;public float resumeIn;public int aiPhase;public string aiAct="",aiLast="",aiPlace="";public float aiT,aiDur,aiGo;public Vector2 aiTarget;public bool aiHasTarget;
            public bool Idleish=>index==0 || (somali && (index==3 || index==6 || index==9 || (white && index==12)));
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
        static bool WhiteLoops(int i)=>SomaliLoops(i)||i==12;
        static bool IsUp(int s)=>s==3||s==6||s==9;
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
            if(!room || PetEnv("ODDITY_CAT")=="0")return;
            if(PetEnv("ODDITY_SOMALI")!="0")SpawnSomali();
            if(PetEnv("ODDITY_WHITE")!="0")SpawnWhite();
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
                var names=WhiteClips;
                var p=new Pet{tag=tag,kind=kind,somali=true,white=true,hasLie=true,clipNames=names,clips=new AnimationClip[names.Length]};
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
                var head=Bone(headNames);var pelvis=Bone(pelvisNames);
                if(head!=null && pelvis!=null){
                    var fw=Vector3.ProjectOnPlane(head.position-pelvis.position,Vector3.up);
                    if(fw.sqrMagnitude>1e-8f){float ang=Mathf.Atan2(fw.x,fw.z)*Mathf.Rad2Deg;go.transform.localRotation=Quaternion.Euler(0,-ang,0);Debug.Log("ROOMPLAY_"+tag+" nose measured at "+F(ang)+" deg from +z ("+pelvis.name+" -> "+head.name+"), model turned by "+F(-ang));}
                }else Debug.LogWarning("ROOMPLAY_"+tag+" Head or Pelvis bone missing, facing unmeasured");
                PetSfxSetup(p,wrap);
                PetPlace(p,spot,yaw,byLength,target);
                float scale=p.root.localScale.x;var info=new SharedInfo();
                var jf=Path.Combine(Application.dataPath,"../"+Path.ChangeExtension(file,".json"));
                if(File.Exists(jf))info=JsonUtility.FromJson<SharedInfo>(File.ReadAllText(jf));
                if(info.walk_speed_units_per_s>0){p.walkSpd=info.walk_speed_units_per_s*scale;p.runSpd=p.walkSpd*1.6f;p.aiSpd=p.walkSpd*.8f;}
                var mat=go.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m!=null);
                Debug.Log("ROOMPLAY_"+tag+" shared file="+Path.GetFileName(file)+" scale="+F(scale)+" json body="+F(info.body_length_nose_to_tail_base)+" head="+F(info.head_top_height_above_floor)+" walk="+F(info.walk_speed_units_per_s)+" speed="+F(p.walkSpd)+" m/s run="+F(p.runSpd)+" ai="+F(p.aiSpd)+" clips="+string.Join(",",p.clips.Select(c=>c.name+":"+F(c.length)+(c.legacy?"L":"M")))+" renderers="+go.GetComponentsInChildren<Renderer>().Length+" material="+(mat!=null?mat.shader.name+" colour="+(mat.HasProperty("_BaseColor")?mat.GetColor("_BaseColor").ToString():"-")+" map="+(mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap")!=null):"none"));
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
        Vector2 SomaliSpot(){var a=PetEnv("ODDITY_SOMALISPOT")!=null?PetEnv("ODDITY_SOMALISPOT").Split(',').Select(t=>float.Parse(t,CultureInfo.InvariantCulture)).ToArray():new[]{-1.30f,-.85f};return new Vector2(a[0],a[1]);}
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
                var p=new Pet{tag="WHITE",kind="white",somali=true,white=true,hasLie=true,clipNames=WhiteClips,clips=new AnimationClip[WhiteClips.Length]};
                for(int i=0;i<WhiteClips.Length;i++){
                    var src=Find(WhiteClips[i]);
                    if(src==null){Debug.LogWarning("ROOMPLAY_WHITE clip missing "+WhiteClips[i]+" (have "+string.Join(",",all.Select(c=>c.name))+"), no white cat");return;}
                    p.clips[i]=PetClipCopy(src,WhiteLoops(i));}
                var wrap=new GameObject("White");var go=Instantiate(prefab,wrap.transform);go.name="WhiteModel";go.transform.localRotation=Quaternion.identity;
                p.root=wrap.transform;
                foreach(var a in go.GetComponentsInChildren<Animation>())Destroy(a);
                foreach(var r in go.GetComponentsInChildren<Renderer>())if(r is SkinnedMeshRenderer smr)smr.updateWhenOffscreen=true;
                var host=go.GetComponent<Animator>();if(host==null)host=go.AddComponent<Animator>();PetGraph(p,host);
                // measured, not assumed: the nose points from Pelvis to Head; the model is turned so that direction is the wrapper's +z, which is what every heading in this file means
                Transform Bone(string n)=>go.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name==n);
                var head=Bone("Head");var pelvis=Bone("Pelvis");
                if(head!=null && pelvis!=null){
                    var fw=Vector3.ProjectOnPlane(head.position-pelvis.position,Vector3.up);
                    if(fw.sqrMagnitude>1e-8f){float ang=Mathf.Atan2(fw.x,fw.z)*Mathf.Rad2Deg;go.transform.localRotation=Quaternion.Euler(0,WhiteFix-ang,0);Debug.Log("ROOMPLAY_WHITE nose measured at "+F(ang)+" deg from +z, model turned by "+F(-ang));}
                }else Debug.LogWarning("ROOMPLAY_WHITE Head or Pelvis bone missing, facing unmeasured");
                PetSfxSetup(p,wrap);
                // front left on the open floor in front of the TV stand (z -2 -> -1.62), clear of the Somali (-1.30, -0.85) and the rug cat, turned toward the camera at an angle the other way
                var spot=PetEnv("ODDITY_WHITESPOT")!=null?PetEnv("ODDITY_WHITESPOT").Split(',').Select(s=>float.Parse(s,CultureInfo.InvariantCulture)).ToArray():new[]{-.25f,-1.05f};
                PetPlace(p,new Vector2(spot[0],spot[1]),WhiteYaw,true,WhiteBodyLength);
                float scale=p.root.localScale.x;p.walkSpd=.70f*scale;p.runSpd=p.walkSpd*1.6f;p.aiSpd=p.walkSpd*.8f;
                var mat=go.GetComponentsInChildren<Renderer>().SelectMany(r=>r.sharedMaterials).FirstOrDefault(m=>m!=null);
                Debug.Log("ROOMPLAY_WHITE file="+Path.GetFileName(found)+" speed="+F(p.walkSpd)+" m/s run="+F(p.runSpd)+" ai="+F(p.aiSpd)+" clips="+string.Join(",",p.clips.Select(c=>c.name+":"+F(c.length)+(c.legacy?"L":"M")))+" renderers="+go.GetComponentsInChildren<Renderer>().Length+" material="+(mat!=null?mat.shader.name+" colour="+(mat.HasProperty("_BaseColor")?mat.GetColor("_BaseColor").ToString():"-")+" map="+(mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap")!=null):"none"));
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
        static readonly string[] RestNames={"","sit","lie","loaf","groom","stretch","jump"};
        void PetSit(Pet p)=>PetRest(p,1);
        // kinds 1 sit, 2 lie, 3 loaf; 4 groom (sits first, then loops Groom), 5 stretch (once, standing), 6 jump in place (once)
        void PetRest(Pet p,int kind)
        {
            if(!p.somali || p.job!=0)return;
            if(kind>=4 && kind!=6 && !p.white)return;
            if(kind!=1 && !p.hasLie){Notice("이 소말리 파일에는 눕는 동작이 없음 (somali_cat_lie.glb)");return;}
            int s=p.sit;
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
            else if(IsUp(p.sit) && t>=p.clips[p.sit==3?4:p.sit==6?7:10].length){
                p.sit=0;SetPetClip(p,0,true);
                int next=p.restNext;p.restNext=0;if(next>0)PetRest(p,next);
            }
        }

        // Tab hands WASD to the pets and back: HARIN -> SOMALI -> WHITE -> HARIN. A pet walks screen-relative like Harin (0.5 m/s, shift 1.2 m/s with the Walk clip played faster), no root motion,
        // inside the room and out of the furniture footprints (the same blockers as Harin's)
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
        // a pet's own keys while it is controlled: digits are clips (kept after release), space idle, C/V/K up onto the sofa, the rest have no pet meaning
        float labelPop=-10;
        const float CatJumpUpSeconds=.5f, CatJumpDownSeconds=.4f;
        void CatCommand(string key)
        {
            var p=ctl;
            if(key=="wait" || key=="up" || key=="down")return;
            if(key=="space"){
                if(p.job==0){
                    if(p.somali && p.sit!=0){PetStand(p);return;} // sitting, lying or loafing: get up, then Idle
                    p.goal=null;p.faceTo=float.NaN;p.moving=false;SetPetClip(p,0,true);}
                return;}
            int act=key.Length==1?"123456789".IndexOf(key[0]):-1; // 1 lie, 2 sit, 3 loaf, 4 walk in place, 5 jump, 6 meow, 7 groom, 8 stretch, 9 stand: the same for every cat; 0 does nothing
            if(key=="0")return; // no action on 0
            if(act>=0){if(p.job!=0)return;CatAction(p,act);return;}
            if(key=="left"||key=="right"){if(p.job==0){p.heading+=key=="left"?45:-45;p.faceTo=float.NaN;}return;}
            if(key=="c"||key=="v"||key=="k"){
                if(p.job!=0 || p.onSofa)return;
                var cp=new Vector2(p.root.position.x,p.root.position.z);
                if(!spotsOf.TryGetValue("sofa",out var sl) || sl.Count==0){Notice("소파가 없음");return;}
                var sp=FreeSpot("sofa",p,cp);if(sp==null){Notice("자리가 없어요");return;}
                PetGoToSofa(p,sp,cp);
                p.arriveRest=p.somali && key=="k"?2:0; // K: the Somali lies down on the cushion (sphinx), C and V sit
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
            p=new Vector2(Mathf.Clamp(p.x,-Limit,Limit),Mathf.Clamp(p.y,ZNear,ZFar));
            var fp=Plan();
            if(!Blocked(p) && fp.InMainArea(p))return p;
            for(float r=.05f;r<=2.5f;r+=.05f)for(int a=0;a<360;a+=15){
                var q=p+new Vector2(Mathf.Cos(a*Mathf.Deg2Rad),Mathf.Sin(a*Mathf.Deg2Rad))*r;q=new Vector2(Mathf.Clamp(q.x,-Limit,Limit),Mathf.Clamp(q.y,ZNear,ZFar));
                if(!Blocked(q) && fp.InMainArea(q))return q;}
            return p;
        }
        // the walk to p.goal around the furniture: FloorPath on the floor box and the blockers Collide uses, Harin a 0.35 m circle for the cats.
        // A goal inside a blocker snaps to the nearest free point (within 0.6 m); an unreachable one is dropped (a cat picks another activity)
        const float HarinAvoid=.35f;
        FloorPath floorPlan;Vector4 floorPlanKey;
        FloorPath Plan()
        {
            var key=new Vector4(Limit,ZNear,ZFar,blockers.Count);
            if(floorPlan==null || key!=floorPlanKey){floorPlan=new FloorPath(-Limit,Limit,ZNear,ZFar,blockers);floorPlanKey=key;}
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
            p.hopPlan.Clear();p.hopWait=0; // a new goal drops any hops still planned for the old one (a hiss during the beat on the counter used to send the cat on to the fridge, where the landing rest pose then wiped the new goal)
            Debug.Log("ROOMPLAY_"+p.tag+" goto="+(logKind??kind)+" at=("+F(goal.x)+","+F(goal.y)+")");
        }
        // to a perch (counter, table, fridge, sill): walk to its floor spot and hop up; the fridge goes by the counter. Already on a perch the hop is direct
        bool PetGoToPerch(Pet p,string name,Vector2? near=null,bool random=false)
        {
            if(!perches.TryGetValue(name,out var pr))return false;
            var cp=new Vector2(p.root.position.x,p.root.position.z);
            var sp=FreeSpot(name,p,near??cp,random);
            if(sp==null){if(!p.auto)Notice("자리가 없어요");return false;}
            if(p.perch==pr && p.spot==sp && AtSpot(p,sp)){p.goal=null;return true;}
            bool onCounter=p.perch!=null && p.perch.name=="counter",onFridge=p.perch!=null && p.perch.name=="fridge";
            if(name=="fridge" && !onCounter){
                var cs=FreeSpot("counter",p,cp,random);if(cs==null){if(!p.auto)Notice("자리가 없어요");return false;}
                PetGoTo(p,"counter",cs.stand,null,cs.land,"fridge via=counter",cs);p.chainNext="fridge";p.after=sp;return true;}
            if((name=="fridge" && onCounter) || (name=="counter" && onFridge)){PetGoTo(p,name,cp,null,cp,null,sp);return true;}
            PetGoTo(p,name,sp.stand,null,sp.land,null,sp);return true;
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
            }else if(new Plane(Vector3.right,new Vector3(-RoomHalf,0,0)).Raycast(ray,out var wd) && InWindow(ray.GetPoint(wd))){
                PetGoTo(p,"window",NearestFree(WindowSpot),null,new Vector2(-RoomHalf,WindowSpot.y));
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
            var counterStand=NearestFree(new Vector2(1.05f,1.10f));
            perches["counter"]=new Perch{name="counter",stand=counterStand,land=new Vector2(1.17f,1.50f),yaw=180,pick=c=>Is(c,"counter")};
            perches["fridge"]=new Perch{name="fridge",stand=counterStand,land=new Vector2(1.70f,1.62f),yaw=180,pick=c=>Is(c,"fridge")};
            perches["sill"]=new Perch{name="sill",stand=NearestFree(new Vector2(-1.38f,.2f)),land=new Vector2(-1.88f,.2f),yaw=-90,pick=c=>Is(c,"sill")};
            if(tableRoot!=null){
                var lap=tableRoot.Find("Laptop");
                var rs=tableRoot.GetComponentsInChildren<Renderer>().Where(r=>lap==null || !r.transform.IsChildOf(lap)).ToArray();
                if(rs.Length>0){
                    var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                    Debug.Log("ROOMPLAY_PERCH table bounds x "+F(b.min.x)+".."+F(b.max.x)+" y "+F(b.min.y)+".."+F(b.max.y)+" z "+F(b.min.z)+".."+F(b.max.z));
                    var land=new Vector2(b.center.x+Mathf.Min(.28f,b.extents.x-.15f),b.center.z); // the +x half: the laptop sits on the -x half
                    var stand=NearestFree(new Vector2(b.max.x+.35f,b.center.z));
                    perches["table"]=new Perch{name="table",stand=stand,land=land,yaw=180,pick=c=>c.transform.IsChildOf(tableRoot) && (lap==null || !c.transform.IsChildOf(lap))};
                }
            }
            foreach(var k in perches.Keys)spotsOf.Remove(k);
            void AddSpot(Perch pr,Vector2 land,Vector2 stand){if(!spotsOf.TryGetValue(pr.name,out var l))spotsOf[pr.name]=l=new List<Spot>();l.Add(new Spot{key=pr.name+"#"+l.Count,kind=pr.name,perch=pr,land=land,stand=stand,yaw=pr.yaw});}
            foreach(var pr in perches.Values){
                if(pr.name=="sill")foreach(float zz in new[]{.05f,.60f,1.15f})AddSpot(pr,new Vector2(-1.88f,zz),NearestFree(new Vector2(-1.38f,zz))); // 0.55 m apart: two cats of 0.46 m do not touch
                else if(pr.name=="counter")foreach(float xx in new[]{.62f,1.17f})AddSpot(pr,new Vector2(xx,1.50f),Mathf.Abs(xx-1.17f)<.01f?pr.stand:NearestFree(new Vector2(xx,1.10f)));
                else AddSpot(pr,pr.land,pr.stand); // fridge, table: one spot
                pr.land=spotsOf[pr.name][0].land;
                perchColsOf[pr]=perchCols.Where(c=>pr.pick(c)).ToList();
            }
            Physics.SyncTransforms();
            foreach(var pr in perches.Values)PerchTop(pr);
            Debug.Log("ROOMPLAY_SPOTS "+string.Join(" ",spotsOf.SelectMany(kv=>kv.Value).Select(sp=>sp.key+"("+F(sp.land.x)+","+F(sp.land.y)+(sp.perch!=null?" top "+F(SpotTop(sp)):" x "+F(sp.lo)+".."+F(sp.hi))+")")));
        }
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
                p.fitFront=front;p.fitSide=side;p.surfBase=p.jobTo.y-p.baseY;p.faceTo=Mathf.Atan2(side.x,side.z)*Mathf.Rad2Deg;if(p.goalKind=="sofa")p.goal=null;}
            else{p.fitFront=p.fitSide=Vector3.zero;}
            SetPetClip(p,0,true);
            if(up && p.somali && !p.goal.HasValue){int rk=p.arriveRest>0?p.arriveRest:1;p.arriveRest=0;PetRest(p,rk);} // the Somali rests on the cushion
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
            if(keys){p.goal=null;p.chainNext=null;} // the keys take over from a click
            if(p.onSofa && p.goal.HasValue && p.goalKind=="sofa" && p.goalSeat==p.seat)p.goal=null; // already on that seat
            if(p.perch!=null && p.goal.HasValue && p.goalKind==p.perch.name && p.spot!=null && AtSpot(p,p.spot))p.goal=null; // already up there, on that spot
            if(p.somali && p.sit!=0 && (keys || p.goal.HasValue)){PetStand(p);return;} // walking while sitting: stand up first, the walk (a click's goal is kept) starts when StandUp is done
            if(p.onSofa && (keys || p.goal.HasValue)){if(!p.goal.HasValue)SetSpot(p,null);StartPetJump(p,false);return;} // any walk starts with a jump down
            if(p.perch!=null && (keys || p.goal.HasValue)){ // up on a perch: counter to fridge and back are one hop, any other walk hops down first (the fridge only to the counter)
                if(!p.goal.HasValue)SetSpot(p,null);
                var gk=p.goal.HasValue && perches.ContainsKey(p.goalKind)?p.goalKind:null;
                p.hopPlan=HopsOff(p.perch.name,gk);
                if(gk!=null && p.hopPlan.Count==1 && p.hopPlan[0]==gk)p.goal=null; // counter <-> fridge as the goal itself: the one hop is the whole walk
                Debug.Log("ROOMPLAY_"+p.tag+" hops="+p.perch.name+">"+string.Join(">",p.hopPlan));
                NextHop(p);return;
            }
            var from=new Vector2(p.root.position.x,p.root.position.z);
            var wish=input.normalized;float pace=run?p.runSpd:p.walkSpd;float reach=float.MaxValue;
            if(keys){var fw=Vector3.ProjectOnPlane(cameraView.transform.forward,Vector3.up).normalized;var rt=new Vector3(fw.z,0,-fw.x);var w3=rt*wish.x+fw*wish.y;wish=new Vector2(w3.x,w3.z).normalized;}
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
            float stepLen=Mathf.Min(pace*dt,reach);
            var next=Collide(from,new Vector2(Mathf.Clamp(from.x+wish.x*stepLen,-Limit-.1f,Limit+.1f),Mathf.Clamp(from.y+wish.y*stepLen,ZNear-.1f,ZFar+.1f)));
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
                if(!float.IsNaN(p.faceTo)){p.heading=Mathf.LerpAngle(p.heading,p.faceTo,1-Mathf.Exp(-10*dt));if(Mathf.Abs(Mathf.DeltaAngle(p.heading,p.faceTo))<1)p.faceTo=float.NaN;}
            }
            p.root.rotation=Quaternion.Euler(0,p.heading,0);
            if(moving!=p.moving){
                if(moving)p.walkStart=Time.unscaledTime;
                else if(Time.unscaledTime-p.walkStart>=1f)PetShot(p,"cat_flap_ears",.4f); // stopped after walking at least 1 s
                p.moving=moving;if(!moving)p.settledAt=Time.unscaledTime;SetPetClip(p,moving?1:0,true);}
            if(moving){var pl=p.outp.GetSourcePlayable();if(pl.IsValid())pl.SetSpeed(run?p.runSpd/p.walkSpd:auto && !keys?p.aiSpd/p.walkSpd:1);}
            if(arrived){
                var kind=p.goalKind;var look=p.goalLook;p.goal=null;
                if(kind=="sofa")StartPetJump(p,true);
                else if(perches.TryGetValue(kind,out var pr)){p.hopPlan.Clear();if(p.chainNext!=null)p.hopPlan.Add(p.chainNext);Debug.Log("ROOMPLAY_"+p.tag+" hops=floor>"+pr.name+(p.chainNext!=null?">"+p.chainNext:""));p.chainNext=null;StartHop(p,pr);}
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
            int r=Rng().Next(100);
            if(p.white){ // sit 30, lie 30, loaf 25, groom 15 (sill: lie 45, loaf 30, sit 15, groom 10)
                if(act=="sill")return r<45?2:r<75?3:r<90?1:4;
                return r<30?1:r<60?2:r<85?3:4;}
            if(act=="sill")return r<50?2:r<80?3:1;
            return r<40?1:r<75?2:3;
        }
        bool AiStart(Pet p,string act)
        {
            var cp=PetXZ(p);string place=act;Vector2 at=cp;bool travel=true;
            switch(act){
                case "wander":case "rug":{
                    Func<Vector2> gen=act=="rug"?(Func<Vector2>)(()=>new Vector2(Rnd(-1.25f,-.25f),Rnd(.4f,1.0f))):(()=>new Vector2(Rnd(-1.6f,1.6f),Rnd(-1.6f,1.2f)));
                    var t=AiFloorPoint(p,gen);if(t==null)return false;
                    at=t.Value;place=act=="rug"?"rug":"floor";PetGoTo(p,"floor",at,null,cp);p.aiDur=Rnd(3,8);break;}
                case "sofa":{
                    var sp=FreeSpot("sofa",p,cp,true);if(sp==null)return false;
                    at=sp.land;PetGoToSofa(p,sp,cp);if(p.somali)p.arriveRest=AiRestKind(p,act);p.aiDur=Rnd(10,25);break;}
                case "window":{
                    float wz=Rnd(0f,1.1f);var spot=NearestFree(new Vector2(-1.40f,wz));if(!FarFromOthers(p,spot))return false;
                    at=spot;PetGoTo(p,"window",spot,null,new Vector2(-RoomHalf,wz));p.aiDur=Rnd(6,15);break;}
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
                    if(p.sit==0 || IsUp(p.sit))PetRest(p,AiRestKind(p,act));p.aiDur=Rnd(8,20);
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
                    at=p.spot!=null?p.spot.land:pr.land;if(p.somali)p.arriveRest=AiRestKind(p,act);p.aiDur=Rnd(10,30);break;}
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
            foreach(var (n,w) in new[]{("sill",15),("counter",8),("table",7),("fridge",5)})if(perches.ContainsKey(n))Add(n,w);
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
                case "sill":case "counter":case "table":case "fridge":return p.perch!=null && p.perch.name==p.aiAct;
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
        void PetsTick(float dt){foreach(var p in pets){PetAnimTick(p);PetAiTick(p,dt);PetRestTick(p,dt);PetSfxTick(p,dt);SpotLeakTick(p);}PetHissTick();PetOverlapTick();}
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
        bool SpotTaken(Spot sp,Pet except){foreach(var o in pets)if(o!=except && (o.spot==sp || o.after==sp || o.transit==sp))return true;return false;}
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
            var mk=seats.Where(t=>t.mark.root==sofa).ToList();if(sofa==null || mk.Count==0)return;
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
                    list.Add(new Spot{key="sofa#"+list.Count,kind="sofa",land=new Vector2(at.x,mid.z),mark=mark.mark,fwd=mark.front,axis=side,lo=s0+seg.lo,hi=s0+seg.hi,top=seat});
                }
            }
            Debug.Log("ROOMPLAY_SOFASPOTS seat top="+F(seat)+" cushions="+runs.Count+" spots="+string.Join(" ",list.Select(sp=>sp.key+" x="+F(sp.land.x)+" ("+F(sp.lo)+".."+F(sp.hi)+") mark="+sp.mark.name)));
        }
        // ---- footprints: a circle of p.radius around the body centre. A walking cat is pushed out of another cat's circle (it slides round it); the safety pass moves whichever arrived later if two still overlap
        Vector2 FootOff(Pet p){var o=p.root.rotation*new Vector3(p.cOff.x,0,p.cOff.y);return new Vector2(o.x,o.z);}
        Vector2 FootCentre(Pet p)=>PetXZ(p)+FootOff(p);
        string LevelOf(Pet p)=>p.job!=0?null:p.perch!=null?p.perch.name:p.onSofa?"sofa":"floor"; // null: in the air
        Vector2 PetSeparate(Pet p,Vector2 from,Vector2 next)
        {
            if(p.perch!=null || p.onSofa)return next;
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
            float lo=float.MaxValue;var verts=new List<Vector3>();p.lowPts.Clear();var pawPts=new List<Vector3>();var allPts=new List<Vector3>();
            float f0=float.MaxValue,f1=float.MinValue,s0=float.MaxValue,s1=float.MinValue;var rp=p.root.position;bool fit=p.onSofa && p.fitFront!=Vector3.zero;
            for(int i=0;i<p.smrs.Length;i++){
                var smr=p.smrs[i];smr.BakeMesh(p.bake,true);p.bake.GetVertices(verts);var m=smr.transform.localToWorldMatrix;
                for(int v=0;v<verts.Count;v++){
                    if(p.tailMask[i][v])continue;var w=m.MultiplyPoint3x4(verts[v]);
                    if(w.y<lo+.03f){if(w.y<lo){lo=w.y;}p.lowPts.Add(w);}
                    if(p.pawMask[i][v])pawPts.Add(w);else if(p.pawFallback && w.y<rp.y+.25f)allPts.Add(w);
                    if(fit){var d=w-rp;float a=Vector3.Dot(d,p.fitFront),b=Vector3.Dot(d,p.fitSide);if(a<f0)f0=a;if(a>f1)f1=a;if(b<s0)s0=b;if(b>s1)s1=b;}
                }
            }
            p.lowPts.RemoveAll(w=>w.y>lo+.03f);
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
        static void Laptop(Transform table)
        {
            var rs=table.GetComponentsInChildren<Renderer>();if(rs.Length==0)return;var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
            var lap=new GameObject("Laptop").transform;lap.SetParent(table,true);lap.position=new Vector3(b.center.x-.12f,b.max.y,b.center.z);
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
        static readonly string[] NapClips={"KaSleepTurnToTheSideLoop","KaSleepCurlUpSidewaysLoop"}; // K: side, face to the room; Shift+K: curled up, face in the armrest
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
        const float ClickArrive=.05f, ClickGiveUp=.6f;
        void ClickAt(Ray ray)
        {
            if(busy)return;
            if(ctl!=null){CatClickAt(ray);return;} // Harin ignores clicks while a pet is controlled
            if(chairJob!=null)return;
            float near=float.MaxValue;Collider got=null;Vector3 at=Vector3.zero;
            foreach(var c in clickHits)if(c!=null && !perchOf.ContainsKey(c) && c.Raycast(ray,out var hit,50f) && hit.distance<near){near=hit.distance;got=c;at=hit.point;} // the high perches are for the cats' clicks only
            if(got!=null){
                var root=got.transform.root;
                var pool=root==sofa?seats.Where(t=>t.mark.root==sofa):seats.Where(t=>t.mark.root!=sofa); // the table and both chairs pick a chair
                if(root!=sofa && root!=tableRoot)pool=seats.Where(t=>t.mark.root==root);
                var list=pool.ToList();if(list.Count==0)return;
                var seat=list.OrderBy(t=>new Vector2(t.mark.position.x-at.x,t.mark.position.z-at.z).sqrMagnitude).First();
                clickWindow=false;clickSeat=seat.mark;var stand=seat.mark.position+seat.front*seat.off;clickGoal=new Vector2(Mathf.Clamp(stand.x,-Limit,Limit),Mathf.Clamp(stand.z,ZNear,ZFar));
                Notice("클릭 "+root.name+"/"+seat.mark.name);
            }else if(new Plane(Vector3.right,new Vector3(-RoomHalf,0,0)).Raycast(ray,out var wd) && InWindow(ray.GetPoint(wd))){
                clickSeat=null;clickWindow=true;clickGoal=new Vector2(WindowSpot.x,WindowSpot.y);Notice("클릭 창문");
            }else{
                if(!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out var d))return;var p=ray.GetPoint(d);
                clickWindow=false;clickSeat=null;clickGoal=new Vector2(Mathf.Clamp(p.x,-Limit,Limit),Mathf.Clamp(p.z,ZNear,ZFar));Notice("클릭 바닥 "+F(p.x)+","+F(p.z));
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
            if(clickSeat!=null && clickSeat.root!=sofa){var seat=clickSeat;clickGoal=null;clickSeat=null;direction=0;Sit(SitIndex,false,seat);return Vector2.zero;} // the chair steps walk her there themselves
            var p=new Vector2(x,z);
            if(!hPathGoal.HasValue || hPathGoal.Value!=clickGoal.Value){HarinPlan(false);if(!clickGoal.HasValue)return Vector2.zero;}
            var tgt=clickGoal.Value;bool lastLeg=true;
            if(hPath!=null && hPath.Count>0){
                hPathI=Mathf.Clamp(hPathI,0,hPath.Count-1);
                while(hPathI<hPath.Count-1 && (hPath[hPathI]-p).sqrMagnitude<.0025f)hPathI++;
                lastLeg=hPathI==hPath.Count-1;tgt=lastLeg?clickGoal.Value:hPath[hPathI];}
            var d=tgt-p;
            var next=Collide(p,p+d.normalized*Mathf.Min(speed*dt,d.magnitude));
            if((next-p).sqrMagnitude<1e-10f)clickStuck+=dt;else clickStuck=0;
            bool canReplan=hPath!=null && !hReplanned;
            if(canReplan && clickStuck>1f){hReplanned=true;clickStuck=0;HarinPlan(true);return Vector2.zero;} // blocked for 1 s: one replan
            if((lastLeg && d.magnitude<ClickArrive) || (clickStuck>ClickGiveUp && !canReplan)){ // (a path that has not been replanned only gives up after the replan, above)
                var seat=clickSeat;bool window=clickWindow;clickGoal=null;clickSeat=null;clickWindow=false;direction=0;
                if(seat!=null)Sit(SitIndex,false,seat);
                else if(window){Select(9);Turn(-90);heading=-90;Notice("창밖 바라보기");} // faces out of the window, the left wall
                else{Select(9);heading=targetYaw;}
                return Vector2.zero;
            }
            return d.normalized;
        }
        void ScriptClick(string key)
        {
            Vector3 target=key=="clickwindow"?new Vector3(-RoomHalf,(WinY.x+WinY.y)/2,(WinZ.x+WinZ.y)/2):key=="clicksofa"&&sofa!=null?SeatOf("SeatL",sofa):key=="clicksofar"&&sofa!=null?SeatOf("SeatR",sofa):key=="clicksofac"&&sofa!=null?SeatOf("SeatC",sofa):key=="clicktable"&&tableRoot!=null?tableRoot.position+Vector3.up*.7f:key=="clickcounter"?new Vector3(1.17f,.86f,1.5f):key=="clickfridge"?new Vector3(1.7f,1.2f,1.5f):key=="clicksill"?new Vector3(-1.85f,.85f,.2f):ScriptFloorPoint();
            ClickAt(new Ray(cameraView.transform.position,target-cameraView.transform.position));
        }
        // clickfloor's fixed point (.9, .9); ODDITY_CLICKXZ="x,z" picks another one for a path test
        static Vector3 ScriptFloorPoint()
        {
            var v=Environment.GetEnvironmentVariable("ODDITY_CLICKXZ");
            if(!string.IsNullOrWhiteSpace(v)){var q=v.Split(',');if(q.Length==2 && float.TryParse(q[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var cx) && float.TryParse(q[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var cz))return new Vector3(cx,0,cz);}
            return new Vector3(.9f,0,.9f);
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
            foreach(var f in furniture)blockers.Add(Rect.MinMaxRect(f.xMin-BodyRadius,f.yMin-BodyRadius,f.xMax+BodyRadius,f.yMax+BodyRadius));
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
            onFloor=false;
            if(seats.Count==0 || clips[index]==null){Notice("앉을 자리가 없거나 앉기 클립이 없음");return;}
            var pool=sofaOnly?seats.Where(t=>t.mark.root==sofa && t.mark.name=="SeatC").ToList():seats;if(pool.Count==0)pool=seats;
            if(only!=null){var one=seats.Where(t=>t.mark==only).ToList();if(one.Count>0)pool=one;}
            var seat=pool.OrderBy(t=>new Vector2(t.mark.position.x-x,t.mark.position.z-z).sqrMagnitude).First();
            if(chairJob!=null)return;
            if(chairSat!=null){if(seat.mark==chairSat.mark){SelectRaw(index);Notice("앉기 "+chairSat.chair.name+" "+Keys[index].clip);}else StartChairStand(-1);return;} // another seat: get up first
            if(seat.mark.root!=sofa){StartChairSit(seat.mark,seat.front,index);return;}
            seatAt=seat.mark.position;
            seatFront=seat.front;seatEdge=seat.mark.root==sofa?.36f:.30f; // seat mark to the cushion's front edge: sofa-1 0.36 m, chair-1 0.21 + 0.09 m
            var stand=seatAt+seat.front*seat.off;x=Mathf.Clamp(stand.x,-Limit,Limit);z=Mathf.Clamp(stand.z,ZNear,ZFar);
            yaw=heading=targetYaw=turnStart=Mathf.Atan2(seat.front.x,seat.front.z)*Mathf.Rad2Deg+(index==NapIndex?NapTurn:0);turnTime=1;
            Select(index);Notice("앉기 "+seat.mark.root.name+" "+Keys[index].clip);
        }
        // a seated foot that has gone back into the seat (a folded leg) or under the floor is brought out in front of the seat edge with leg IK, knee forward
        Vector3 seatFront;float seatEdge;
        void FeetClear()
        {
            float w=weights[SitIndex]+weights[NapIndex]+(onFloor?0:weights[CurlIndex]);if(w<=0 || sofa==null)return;
            if(weights[NapIndex]>.5f){NapFeet(weights[NapIndex]);return;}
            if(!onFloor && weights[CurlIndex]>.5f){CurlFeet(weights[CurlIndex]);return;}
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
            onFloor=false;chairJob=new ChairWork{c=c,sitting=true,clip=clip};Notice("의자로 "+root.name+" "+Keys[clip].clip);
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
            float w=weights[SitIndex]+weights[NapIndex]+(onFloor?0:weights[CurlIndex]);if(w<=0 || sofa==null)return;
            var hips=anim.GetBoneTransform(HumanBodyBones.Hips);
            var aim=seatAt+(weights[NapIndex]>0?Vector3.up*NapLift:Vector3.zero);
            model.transform.position+=(aim-hips.position)*w;
            if(weights[NapIndex]<.5f)OffBackrest(w);
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
        static readonly Vector3 RoomEye=new Vector3(2.35f,2.9f,-2.55f), RoomAim=new Vector3(-.25f,.35f,.45f);
        const float RoomHalf=2f, RoomFov=46;
        void Room(Camera cam)
        {
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
            Limit=RoomHalf-.25f;ZNear=-RoomHalf+.25f;ZFar=RoomHalf-.25f;
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
            var through=new Vector3(-RoomHalf,(WinY.x+WinY.y)/2,(WinZ.x+WinZ.y)/2);var onto=new Vector3(-.75f,.45f,1.55f); // the window middle to the sofa seat
            keyLight.transform.rotation=Quaternion.LookRotation(onto-through);keyLight.color=new Color(1f,.9f,.72f);keyLight.intensity=1.6f;
            keyLight.shadows=LightShadows.Soft;keyLight.shadowStrength=.92f;keyLight.shadowBias=.03f;keyLight.shadowNormalBias=.3f;
            // ODDITY_LAMP=1 turns the ceiling lamp on; by default only the window sun lights the room, with a brighter ambient fill
            bool lamp=Environment.GetEnvironmentVariable("ODDITY_LAMP")=="1";
            RenderSettings.ambientLight=lamp?new Color(.26f,.26f,.27f):new Color(.40f,.39f,.38f);
            if(lamp)CeilingLamp();
        }
        // a fluorescent tube on the ceiling: two cool white point lights along its length light the room from above, no shadows
        static readonly Vector3 LampAt=new Vector3(-.2f,2.42f,.3f);
        const float LampHalf=.6f, LampPower=.8f, LampRange=5f;
        static readonly Color LampColor=new Color(.92f,.97f,1f);
        void CeilingLamp()
        {
            foreach(var dz in new[]{-LampHalf,LampHalf}){
                var l=new GameObject("CeilingLamp").AddComponent<Light>();l.type=LightType.Point;l.color=LampColor;l.intensity=LampPower;l.range=LampRange;l.shadows=LightShadows.None;
                l.transform.position=LampAt+new Vector3(0,0,dz);}
        }
        // the up arrow closes in on her (narrower lens, aim moves to her chest and up to her face at the closest), the down arrow goes back to the whole room.
        // the aim has slack: it only moves when she leaves a circle around it (RoomSlack of the half frame), so sway and breathing do not shake the view
        Vector3 roomFocus,lastWant;bool roomFocusSet;float wantPath,focusPath; // paths are logged per script step: how far the body point and the aim moved
        float wheelZoom;const float WheelNotch=.08f, WheelRate=.8f; // a wheel notch is 8% of the zoom range
        const float RoomFovNear=5f, RoomSlack=.3f, RoomFollow=5f;
        void RoomFrame(float dt,float zoomKey)
        {
            roomZoom=Mathf.Clamp01(roomZoom-zoomKey*dt*.55f);
            if(wheelZoom!=0){float take=Mathf.MoveTowards(wheelZoom,0,dt*WheelRate);roomZoom=Mathf.Clamp01(roomZoom+wheelZoom-take);wheelZoom=take;if(roomZoom<=0 || roomZoom>=1)wheelZoom=0;} // each notch eases in over about 0.1 s
            float fov=RoomFov*Mathf.Pow(RoomFovNear/RoomFov,roomZoom); // even steps from 46 to 5 degrees
            Transform chest=null,neck=null;if(anim!=null){chest=anim.GetBoneTransform(HumanBodyBones.Chest)??anim.GetBoneTransform(HumanBodyBones.Hips);neck=anim.GetBoneTransform(HumanBodyBones.Head);}
            bool onCat=ctl!=null;
            if(chest!=null || onCat){
                var want=onCat?ctl.root.position+Vector3.up*Mathf.Lerp(.15f,.2f,Mathf.InverseLerp(14,RoomFovNear,fov)):neck!=null?Vector3.Lerp(chest.position,neck.position,Mathf.InverseLerp(14,RoomFovNear,fov)):chest.position;
                if(!roomFocusSet){roomFocus=want;roomFocusSet=true;lastWant=want;}
                wantPath+=Vector3.Distance(want,lastWant);lastWant=want;var wasFocus=roomFocus;
                float slack=Vector3.Distance(RoomEye,roomFocus)*Mathf.Tan(fov*.5f*Mathf.Deg2Rad)*RoomSlack;
                var off=want-roomFocus;float m=off.magnitude;
                if(m>slack)roomFocus=Vector3.Lerp(roomFocus,roomFocus+off*(1-slack/m),1-Mathf.Exp(-RoomFollow*dt));
                focusPath+=Vector3.Distance(roomFocus,wasFocus);
            }
            var aim=(chest!=null || onCat)?Vector3.Lerp(RoomAim,roomFocus,Mathf.SmoothStep(0,1,Mathf.Min(1,roomZoom*2.5f))):RoomAim;
            cameraView.fieldOfView=fov;cameraView.transform.position=RoomEye;cameraView.transform.LookAt(aim);
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
        Vector3 headFront=Vector3.forward;float nap;
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
            graph=PlayableGraph.Create("RoomPlayMotion");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,MotionCount);
            AnimationPlayableOutput.Create(graph,"Harin",anim).SetSourcePlayable(mixer);
            clips=new AnimationClip[MotionCount];plays=new AnimationClipPlayable[MotionCount];anchors=new Vector3[MotionCount];travels=new Vector3[MotionCount];weights=new float[MotionCount];from=new float[MotionCount];
            for(int i=0;i<MotionCount;i++){
                try{clips[i]=i==ZeroTwoIndex?ZeroTwoDance.CreateClip():RoomTestRunner.Clip(Keys[i].clip);}catch{if(i==ZeroTwoIndex)throw;Debug.LogWarning("ROOMPLAY missing clip="+Keys[i].clip);continue;}
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
            speed=MeasureWalk(10);walkTwist=lastTwist;runSpeed=Mathf.Clamp(MeasureWalk(11),speed*1.5f,speed*3);runTwist=lastTwist;weights[current]=from[current]=1;blend=1;graph.Play();Evaluate(0);
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
            if(key=="o"){ToggleCatSfx();return;}
            if(key=="realtab"){StartCoroutine(RealTab());return;}
            if(key.StartsWith("click")){ScriptClick(key);return;}
            if(key=="wheelup"||key=="wheeldown"){wheelZoom=Mathf.Clamp(wheelZoom+(key=="wheelup"?WheelNotch:-WheelNotch),-1,1);return;} // one notch per script step
            if(ctl!=null && key!="tab" && key!="escape" && key!="m"){CatCommand(key);return;} // the cat has its own keys while it is controlled
            if(key=="n"){bool back=Keyboard.current!=null && Keyboard.current.shiftKey.isPressed;if(!busy)StartCoroutine(Reload(StepLine(back?-1:1),false));}
            else if(key=="r"){if(!busy)StartCoroutine(Reload(StepLine(0),true));}
            else if(key=="space"){if(direction==0)Select(9);}
            else if(key=="l"){lookOn=!lookOn;Notice(lookOn?"주인공 바라보기":"바라보기 끝");}
            else if(key=="h"){chinOn=!chinOn;Notice(chinOn?"턱 괴기 (식탁 의자에서)":"턱 괴기 끝");}
            else if(key=="k"){if(direction==0 && !busy){if(Keyboard.current!=null && Keyboard.current.shiftKey.isPressed)NextSit(NapIndex);else Sit(NapIndex,true);}}
            else if(key=="c"||key=="v"){if(direction==0 && !busy){int index=key=="c"?SitIndex:CurlIndex;
                if(Keyboard.current!=null && Keyboard.current.shiftKey.isPressed)NextSit(index);else Sit(index);}}
            else if(key=="g"){if(direction==0 && !busy){int i=(Array.IndexOf(Walks,Keys[10].clip)+1)%Walks.Length;SetWalk(Walks[i]);try{LoadModel(modelPath);Notice("걷기 "+Walks[i]);}catch(Exception e){Notice("걷기 실패: "+e.Message);}}}
            else if(key=="m")NextCat();
            else if(key=="tab")ToggleControl();
            else if(key=="escape")EditorApplication.ExitPlaymode();
            else if(key=="left"||key=="right"){heading+=key=="left"?45:-45;if(direction==0)Turn(heading);}
            else if(direction==0){int i=Array.FindIndex(Keys,k=>k.key==key);if(i>=0 && (i<9 || key=="f" || key=="z" || key=="0" || key=="y"))Select(i);}
            Log(key);
        }
        void Log(string key){Debug.Log("ROOMPLAY key="+key+" clip="+Keys[current].clip+" x="+F(x)+" z="+F(z)+" yaw="+F(yaw)+" heading="+F(heading)+" zoom="+F(zoom)+" floor="+F(floorError)+(room?" fov="+F(cameraView.fieldOfView)+" bodyPath="+F(wantPath)+" aimPath="+F(focusPath):""));wantPath=focusPath=0;}
        const string DefaultLine="6T-O2";
        static readonly string[] HiddenLines={"6T-I1","6T-O1"}; // hidden for now (2026-11-07): left out of the N / Shift+N cycle, still loadable with ODDITY_MODEL
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
                LoadModel(asset);Notice("불러오기 완료 "+name.Replace("harin-",""));
                Debug.Log("ROOMPLAY_RELOAD model="+modelName+" seconds="+F((float)timer.Elapsed.TotalSeconds)+" playing="+EditorApplication.isPlaying+" x="+F(x)+" clip="+Keys[current].clip);
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
        void Update()
        {
            if(cameraView!=null){float aspect=Screen.width/(float)Mathf.Max(1,Screen.height);float target=16f/9;
                cameraView.rect=aspect>target?new Rect((1-target/aspect)/2,0,target/aspect,1):new Rect(0,(1-aspect/target)/2,1,aspect/target);}
            TabPoll();
            PetsTick(Mathf.Min(Time.unscaledDeltaTime,.05f));
            if(statue!=null && !busy){StatueUpdate(Mathf.Min(Time.unscaledDeltaTime,.05f));return;}
            if(model==null || busy || reviewing)return;float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);var input=Vector2.zero;bool run=false;float zoomKey=0;
            turning=0;
            if(scripted){
                stepTime-=dt;
                if(step<0 || stepTime<=0){
                    Snap();
                    step++;if(step>=script.Count){Debug.Log("ROOMPLAY_ROLL planted foot tilt most raw="+F(rollRaw)+" kept="+F(rollKept)+" deg seated toe lowest="+F(toeLow));Debug.Log("ROOMPLAY_DONE");EditorApplication.ExitPlaymode();enabled=false;return;}
                    stepTime=script[step].seconds;
                    if(direction!=0 && !Walk(script[step].key)){direction=0;Select(9);Turn(heading);}
                    if(!Walk(script[step].key))Command(script[step].key);
                }
                input=WalkDir(script[step].key);run=script[step].key.StartsWith("shift");zoomKey=script[step].key=="up"?-1:script[step].key=="down"?1:0;
            }else{
                var kb=Keyboard.current;if(kb==null){zeroTwoInputReady=false;zeroTwoAudio?.SetState(current==ZeroTwoIndex,false,zeroTwoAudioSeconds);return;}
                input=new Vector2((kb.dKey.isPressed?1:0)-(kb.aKey.isPressed?1:0),(kb.wKey.isPressed?1:0)-(kb.sKey.isPressed?1:0));run=kb.shiftKey.isPressed;zoomKey=(kb.downArrowKey.isPressed?1:0)-(kb.upArrowKey.isPressed?1:0);
                turning=(kb.leftArrowKey.isPressed?1:0)-(kb.rightArrowKey.isPressed?1:0);
                var mouse=Mouse.current;if(mouse!=null && mouse.leftButton.wasPressedThisFrame && room)ClickAt(cameraView.ScreenPointToRay(mouse.position.ReadValue()));
                if(mouse!=null){float wheel=mouse.scroll.ReadValue().y;if(wheel!=0)wheelZoom=Mathf.Clamp(wheelZoom+Mathf.Sign(wheel)*WheelNotch,-1,1);} // wheel up zooms in
                foreach(var entry in Keys)if(!Walk(entry.key) && entry.key!="left" && entry.key!="right" && entry.key!="tab" && entry.key!="o"){ // Tab and O have their own poll (TabPoll)
                    if(kb[KeyOf(entry.key)].wasPressedThisFrame && (input==Vector2.zero || !char.IsDigit(entry.key[0])))Command(entry.key);
                }
                // an action moves while its key is held and holds the pose the moment it is let go; space returns to the idle
                bool action=current<9 || current==12 || current==ZeroTwoIndex || current==IdolIndex || current==YawnIndex;bool pressed=action && kb[KeyOf(Keys[current].key)].isPressed;
                if(action && held && !pressed)Log("hold");held=pressed;
            }
            if(ctl!=null){CatTick(dt,input,run,turning);input=Vector2.zero;run=false;turning=0;} // WASD and shift drive the cat; Harin keeps her pose
            if(busy)return;
            zeroTwoInputReady=true;
            if(input!=Vector2.zero && clickGoal.HasValue){clickGoal=null;clickSeat=null;} // the keys take over from a click
            if(chairJob==null && chairSat!=null && input!=Vector2.zero)StartChairStand(-1); // walking off a dining chair
            var goWish=chairJob==null?ClickStep(dt):Vector2.zero;
            if(chairJob!=null){ChairTick(dt);turnTime+=dt/.2f;yaw=Mathf.LerpAngle(turnStart,targetYaw,Mathf.SmoothStep(0,1,turnTime));heading=targetYaw;Evaluate(dt);Frame(dt,zoomKey);return;}
            run=run && clips[11]!=null;float pace=run?runSpeed:speed;
            var wish=input.normalized;if(goWish!=Vector2.zero){wish=goWish;run=false;pace=speed;}else if(room && wish!=Vector2.zero){var fw=Vector3.ProjectOnPlane(cameraView.transform.forward,Vector3.up).normalized;var rt=new Vector3(fw.z,0,-fw.x);var w3=rt*wish.x+fw*wish.y;wish=new Vector2(w3.x,w3.z);} // W walks away from the camera
            var next=new Vector2(Mathf.Clamp(x+wish.x*pace*dt,-Limit,Limit),Mathf.Clamp(z+wish.y*pace*dt,ZNear,ZFar));
            next=Collide(new Vector2(x,z),next);
            if((next-new Vector2(x,z)).sqrMagnitude<1e-10f||clips[10]==null)wish=Vector2.zero;
            int dir=wish==Vector2.zero?0:run?2:1;
            // letting go of the walk or run keys holds her in that stride (space returns to the idle); a scripted run still goes back to the idle
            if(dir!=direction){if(dir==0 && !scripted)heading=targetYaw;direction=dir;if(dir!=0 || scripted)Select(dir==0?9:dir==2?11:10);Log(dir==0?"stop":dir==2?"run":"walk");}
            if(current==SitIndex || current==CurlIndex || current==NapIndex)turning=0; // arrows would turn her off the seat
            if(dir==0 && turning!=0){heading+=turning*TurnRate*dt;yaw=turnStart=targetYaw=heading;turnTime=1;} // held arrows turn her smoothly, letting go stops her there
            Turn(dir==0?heading:Mathf.Atan2(wish.x,wish.y)*Mathf.Rad2Deg);
            turnTime+=dt/.2f;yaw=Mathf.LerpAngle(turnStart,targetYaw,Mathf.SmoothStep(0,1,turnTime));
            if(dir!=0){x=next.x;z=next.y;}
            Evaluate(dt);
            Frame(dt,zoomKey);
        }
        // ODDITY_PLAYSHOTS=1: a scripted run saves the game view at the end of each step -> shots/room_test/play/
        void Snap()
        {
            if(step<0 || Environment.GetEnvironmentVariable("ODDITY_PLAYSHOTS")!="1")return;
            var dir=Path.GetFullPath("../shots/room_test/play");Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir,step.ToString("00")+"_"+script[step].key+".png"));
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
        // a blank face in the idle (key 0, space), a held smile in every other clip; it eases in and out over 0.3 s
        const float SmileWeight=.7f; // relaxed, the closed-mouth smile the room test films use
        float smile;
        void Smile(float dt)
        {
            var vrm=model.GetComponent<Vrm10Instance>();if(vrm==null || vrm.Runtime==null)return;
            smile=Mathf.MoveTowards(smile,current==9?0:SmileWeight,dt*SmileWeight/.3f);
            vrm.Runtime.Expression.SetWeight(ExpressionKey.Relaxed,smile);
            nap=Mathf.MoveTowards(nap,current==NapIndex?1:0,dt/.6f);vrm.Runtime.Expression.SetWeight(ExpressionKey.Blink,nap); // asleep: eyes shut
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
        bool Frozen(int i)=>!scripted && i==current && ((i<9 || i==12 || i==ZeroTwoIndex || i==IdolIndex || i==YawnIndex) && !held || (i==10 || i==11) && direction==0);
        void Evaluate(float dt)
        {
            if(Frozen(current))dt=0;
            blend=Mathf.Min(1,blend+dt/blendTime);Vector3 baseHip=Vector3.zero, drift=Vector3.zero;float sum=0;
            bool musicAdvancing=current==ZeroTwoIndex && !busy && (zeroTwoInputReady || reviewing) && !Frozen(ZeroTwoIndex);
            if(musicAdvancing && dt>0){zeroTwoPhase=Mathf.Repeat(zeroTwoPhase+ZeroTwoDance.MotionSecondsBetween(zeroTwoAudioSeconds,dt),ZeroTwoMusic.Duration);zeroTwoAudioSeconds+=dt;}
            else if(current!=ZeroTwoIndex && dt>0)zeroTwoPhase=Mathf.Repeat(zeroTwoPhase+dt,ZeroTwoMusic.Duration);
            for(int i=0;i<MotionCount;i++)if(clips[i]!=null){
                weights[i]=Mathf.Lerp(from[i],i==current?1:0,blend);mixer.SetInputWeight(i,weights[i]);
                plays[i].SetTime(i==ZeroTwoIndex?zeroTwoPhase:(plays[i].GetTime()+(Frozen(i)?0:dt))%clips[i].length);baseHip+=anchors[i]*weights[i];drift+=travels[i]*(float)(plays[i].GetTime()/clips[i].length)*weights[i];sum+=weights[i];
            }
            bodyFix=walkTwist*weights[10]+runTwist*weights[11];
            model.transform.SetPositionAndRotation(new Vector3(x,0,z),Quaternion.Euler(0,yaw-bodyFix,0));graph.Evaluate(0);
            var hips=anim.GetBoneTransform(HumanBodyBones.Hips);var p=hips.localPosition;
            if(sum>0){p-=drift/sum;p.x=Mathf.Clamp(p.x,baseHip.x/sum-.2f,baseHip.x/sum+.2f);p.z=Mathf.Clamp(p.z,baseHip.z/sum-.2f,baseHip.z/sum+.2f);hips.localPosition=p;}
            model.transform.SetPositionAndRotation(new Vector3(x,0,z),Quaternion.Euler(0,yaw-bodyFix,0));
            DeepenBow();
            LimitWrists();
            if(weights[SitIndex]+weights[CurlIndex]+weights[NapIndex]<.5f)LevelFeet(dt);
            zeroTwoDance?.Apply(zeroTwoPhase,weights[ZeroTwoIndex]);
            zeroTwoAudio?.SetState(current==ZeroTwoIndex,musicAdvancing,zeroTwoAudioSeconds);
            Meow();
            Smile(dt);
            Ground(dt);
            PinToSeat();
            FeetClear();
            ChinRest(dt);
            LookAtPlayer(dt);
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
        static Key KeyOf(string key)=>key=="tab"?Key.Tab:key=="escape"?Key.Escape:key=="space"?Key.Space:key=="left"?Key.LeftArrow:key=="right"?Key.RightArrow:key=="up"?Key.UpArrow:key=="down"?Key.DownArrow:char.IsDigit(key[0])?(Key)Enum.Parse(typeof(Key),"Digit"+key):(Key)Enum.Parse(typeof(Key),key.ToUpperInvariant());
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
            var headBone=anim.GetBoneTransform(HumanBodyBones.Head);if(headBone!=null)headFront=Quaternion.Inverse(headBone.rotation)*anim.transform.forward;
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
        void Frame(float dt,float zoomKey)
        {
            if(room){RoomFrame(dt,zoomKey);return;}
            zoom=Mathf.Clamp(zoom*Mathf.Exp(zoomKey*.8f*dt),1.2f,30f);
            float near=Mathf.InverseLerp(6.79f,2.6f,zoom);
            cameraView.transform.position=new Vector3(x*near,Mathf.Lerp(1.13f,1.54f,near),Mathf.Lerp(-zoom,z-zoom,near));
            cameraView.transform.rotation=Quaternion.Euler(Mathf.Lerp(3,-1.5f,Mathf.InverseLerp(6.79f,20f,zoom)),0,0); // the true horizon meets the painted one as she walks into the distance
        }
        static GameObject Part(PrimitiveType type,Transform parent,Vector3 pos,Vector3 size,Color color)
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
        static readonly string[] CatCommonKeys={"space  멈추기 (앉았거나 누웠으면 일어서기)","W A S D  걷기, Shift 빠르게","← →  제자리 돌기","↑ ↓, 휠  줌","C V  소파에 올라가 앉기, K  올라가 눕기","클릭  바닥, 소파, 식탁, 조리대, 냉장고, 창","M  다음 동작","O  고양이 소리 켜고 끄기","Tab  하린으로"};
        static string CatRow(Pet p,int i){const string keys="123456789";return i<ActionNames.Length?keys[i]+"  "+ActionNames[i]+(CanDo(p,i)?"":" (없음)"):CatCommonKeys[i-ActionNames.Length];}
        void OnGUI()
        {
            var ev=Event.current;
            if(ev!=null && ev.keyCode==KeyCode.Tab){
                if(ev.type==EventType.KeyDown){if(!tabGuiDown){tabGuiDown=true;TabPress("ongui");}ev.Use();GUIUtility.keyboardControl=0;}
                else if(ev.type==EventType.KeyUp){tabGuiDown=false;ev.Use();}
            }
            bool cat=ctl!=null;
            int rows=cat?ActionNames.Length+CatCommonKeys.Length:Keys.Length;
            float listEnd=34+rows*17;GUI.Box(new Rect(10,10,340,listEnd-6),"");var style=new GUIStyle(GUI.skin.label){fontSize=12};
            GUI.Label(new Rect(16,13,330,20),cat?(ctl.kind=="white"?"흰 고양이":"소말리 고양이"):modelName??"준비 중",style);
            for(int i=0;i<rows;i++){
                if(cat){
                    bool on=i<ActionNames.Length && i==CurAction(ctl);style.normal.textColor=on?Color.yellow:i<ActionNames.Length && !CanDo(ctl,i)?Color.gray:Color.white;
                    GUI.Label(new Rect(16,34+i*17,330,18),(on?"> ":"  ")+CatRow(ctl,i),style);
                    continue;
                }
                bool selected=i==current;
                style.normal.textColor=selected?Color.yellow:clips!=null && i<MotionCount && clips[i]==null?Color.gray:Color.white;
                GUI.Label(new Rect(16,34+i*17,330,18),(selected?"> ":"  ")+Keys[i].key+"  "+Keys[i].clip,style);
            }
            if(Time.realtimeSinceStartup<messageUntil)GUI.Label(new Rect(16,listEnd+4,900,40),message,new GUIStyle(GUI.skin.label){fontSize=24});
            // who is controlled: top right, white with a dark outline; it pops (1.3 -> 1 in 0.2 s) and flashes when Tab hands control over
            float age=Time.realtimeSinceStartup-labelPop;
            var flash=cat?(ctl.kind=="white"?new Color(.62f,.80f,1f):new Color(1f,.62f,.30f)):Color.yellow;
            string who=cat?ctl.tag:"HARIN";
            var tint=age<.35f?Color.Lerp(flash,Color.white,Mathf.Clamp01(age/.35f)):Color.white;
            var big=new GUIStyle(GUI.skin.label){fontSize=18,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleRight};
            var rect=new Rect(Screen.width-230,12,220,32);var saved=GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one*(age<.2f?Mathf.Lerp(1.3f,1f,age/.2f):1f),new Vector2(rect.xMax,rect.center.y));
            big.normal.textColor=new Color(0,0,0,.9f);
            foreach(var o in new[]{new Vector2(-2,0),new Vector2(2,0),new Vector2(0,-2),new Vector2(0,2),new Vector2(-1.5f,-1.5f),new Vector2(1.5f,-1.5f),new Vector2(-1.5f,1.5f),new Vector2(1.5f,1.5f)})GUI.Label(new Rect(rect.x+o.x,rect.y+o.y,rect.width,rect.height),who,big);
            big.normal.textColor=tint;GUI.Label(rect,who,big);
            GUI.matrix=saved;
            if(!catSfxOn){var small=new GUIStyle(GUI.skin.label){fontSize=13,alignment=TextAnchor.MiddleRight};var r2=new Rect(Screen.width-230,46,220,22);
                small.normal.textColor=new Color(0,0,0,.9f);GUI.Label(new Rect(r2.x+1,r2.y+1,r2.width,r2.height),"고양이 소리 끔",small);small.normal.textColor=new Color(1f,.85f,.6f);GUI.Label(r2,"고양이 소리 끔",small);}
        }
        void OnDestroy(){foreach(var pet in pets)if(pet.graph.IsValid())pet.graph.Destroy();zeroTwoAudio?.Dispose();zeroTwoDance?.Dispose();if(clips!=null && clips[ZeroTwoIndex]!=null)Destroy(clips[ZeroTwoIndex]);if(graph.IsValid())graph.Destroy();if(boundsMesh!=null)Destroy(boundsMesh);if(soleBake!=null)Destroy(soleBake);EditorSettings.enterPlayModeOptionsEnabled=oldEnabled;EditorSettings.enterPlayModeOptions=oldOptions;}
    }
}
