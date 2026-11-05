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
            ("a", "CatwalkSkin"), ("shift", "KaRun01"), ("f", "UAL1Standard#Sword_Attack"), ("z", "ZeroTwoDance"), ("0", "KaIdle73IdolPose"), ("c", "KaSitPutHandBetweensLoop"), ("v", "KaSitWithBothLegsUpLoop"), ("y", "MxYawn"), ("k", "KaSleepTurnToTheSideLoop"), ("d", "CatwalkSkin"), ("w", "CatwalkSkin"), ("s", "CatwalkSkin"), ("left", "누르는 동안 시계 방향 회전"), ("right", "누르는 동안 반시계 회전"), ("n", "다음 모델, Shift+N 이전 모델"),("r", "현재 모델 다시 불러오기"), ("l", "주인공(카메라) 바라보기 켜고 끄기"), ("h", "식탁 의자에서 턱 괴기 켜고 끄기"), ("g", "다음 걷기"), ("escape", "재생 끝내기"), ("i", "줌 인"), ("o", "줌 아웃")
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
                Time.captureDeltaTime=0; Application.targetFrameRate=60; Application.runInBackground=true; // a scripted run kept stalling at frame 2 when the editor window was not in front
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
                LoadModel(Environment.GetEnvironmentVariable("ODDITY_MODEL")??"Assets/Models/test/harin-6T-I1-30.vrm");
                PlaceSofa();
                var text=Environment.GetEnvironmentVariable("ODDITY_PLAYKEYS");scripted=!string.IsNullOrWhiteSpace(text);
                if(scripted)foreach(var part in text.Split(',')){
                    var pair=part.Split(':');
                    if(pair.Length!=2 || !float.TryParse(pair[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds) || seconds<=0 || !(pair[0]=="shifts" || Keys.Any(k=>k.key==pair[0])))throw new Exception("invalid ODDITY_PLAYKEYS entry");
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
        // left and right arrows turn it, I and O zoom, N, Shift+N and R still step through the lines, Esc ends
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
            Notice("맨 메시(뼈대 없음): ← → 돌리기, I O 줌, N 다음 모델");
            Debug.Log("ROOMPLAY_STATUE model="+modelName+" height="+F(b.size.y)+" scale="+F(scale)+" renderers="+renderers.Length+" grey="+(grey!=null));
        }
        void StatueUpdate(float dt)
        {
            var kb=Keyboard.current;if(kb==null)return;
            foreach(var key in new[]{"n","r","escape"})if(kb[KeyOf(key)].wasPressedThisFrame)Command(key);
            if(statue==null)return;
            yaw+=((kb.leftArrowKey.isPressed?1:0)-(kb.rightArrowKey.isPressed?1:0))*TurnRate*dt;heading=targetYaw=turnStart=yaw;
            statue.transform.rotation=Quaternion.Euler(0,yaw+StatueFlip,0);
            Frame(dt,(kb.oKey.isPressed?1:0)-(kb.iKey.isPressed?1:0));
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
                foreach(var t in sofa.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Seat")).OrderBy(t=>t.position.x))seats.Add((t,Facing(sofa),StandOff+.2f));} // the sofa seat is deep, she gets up clear of it
            var table=LoadProp("table");
            if(table!=null){table.transform.position=room?new Vector3(.55f,0,-.45f):new Vector3(0,0,TableZ);Face(table.transform,Vector3.right);
                foreach(var spot in table.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Chair")).OrderBy(t=>t.name).ToList()){
                    var chair=LoadProp("chair");if(chair==null)break;
                    chair.transform.SetPositionAndRotation(spot.position,spot.rotation);chair.name=spot.name;
                    var seat=Mark(chair.transform,"Seat");if(seat!=null)seats.Add((seat,Facing(chair.transform),-StandOff)); // a chair is left from behind, the table is in front
                }}
            if(sofa==null && seats.Count>0)sofa=seats[0].mark.root;
            Blockers();
            Debug.Log("ROOMPLAY_PROPS seats="+string.Join(" ",seats.Select(t=>t.mark.root.name+"/"+t.mark.name+"("+F(t.mark.position.x)+","+F(t.mark.position.y)+","+F(t.mark.position.z)+" face "+F(t.front.x)+","+F(t.front.z)+")")));
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
        // furniture footprints on the floor (world boxes of each prop's meshes), grown by her body radius; walking into one slides along its side
        readonly List<Rect> blockers=new List<Rect>();
        const float BodyRadius=.2f;
        void Blockers()
        {
            blockers.Clear();
            var props=new List<Transform>();if(sofa!=null)props.Add(sofa);
            props.AddRange(FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t=>t.parent==null && (t.name.StartsWith("table-") || t.name.StartsWith("Chair"))));
            foreach(var t in props.Distinct()){
                var rs=t.GetComponentsInChildren<Renderer>();if(rs.Length==0)continue;
                var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);
                blockers.Add(Rect.MinMaxRect(b.min.x-BodyRadius,b.min.z-BodyRadius,b.max.x+BodyRadius,b.max.z+BodyRadius));
            }
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
        void Sit(int index)=>Sit(index,false);
        // sofaOnly: the nap curls up on the sunny sofa seat whatever seat is nearer
        void Sit(int index,bool sofaOnly)
        {
            if(clips[index]!=null && FloorClip(Keys[index].clip)){onFloor=true;Select(index);Notice("바닥 "+Keys[index].clip);return;}
            onFloor=false;
            if(seats.Count==0 || clips[index]==null){Notice("앉을 자리가 없거나 앉기 클립이 없음");return;}
            var pool=sofaOnly?seats.Where(t=>t.mark.root==sofa && t.mark.name=="SeatC").ToList():seats;if(pool.Count==0)pool=seats;
            var seat=pool.OrderBy(t=>new Vector2(t.mark.position.x-x,t.mark.position.z-z).sqrMagnitude).First();seatAt=seat.mark.position;
            var stand=seatAt+seat.front*seat.off;x=Mathf.Clamp(stand.x,-Limit,Limit);z=Mathf.Clamp(stand.z,ZNear,ZFar);
            yaw=heading=targetYaw=turnStart=Mathf.Atan2(seat.front.x,seat.front.z)*Mathf.Rad2Deg+(index==NapIndex?NapTurn:0);turnTime=1;
            Select(index);Notice("앉기 "+seat.mark.root.name+" "+Keys[index].clip);
        }
        // pulls her so the hips meet the seat mark, by the sit clip's weight so standing up blends back
        void PinToSeat()
        {
            float w=weights[SitIndex]+weights[NapIndex]+(onFloor?0:weights[CurlIndex]);if(w<=0 || sofa==null)return;
            var hips=anim.GetBoneTransform(HumanBodyBones.Hips);
            var aim=seatAt+(weights[NapIndex]>0?Vector3.up*NapLift:Vector3.zero);
            model.transform.position+=(aim-hips.position)*w;
        }
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
            Limit=RoomHalf-.25f;ZNear=-RoomHalf+.25f;ZFar=RoomHalf-.25f;
            x=-.6f;z=-.3f; // she starts on the open floor between the table and the window
            cam.transform.position=RoomEye;cam.transform.LookAt(RoomAim);
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
            var r=sky.GetComponent<Renderer>();r.material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){color=new Color(1f,.97f,.86f)};r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // the sun comes in through the window and lands on the sofa seat; the walls cast shadows so only the window patch is bright
        Light keyLight;
        void Sun()
        {
            if(keyLight==null)return;
            var through=new Vector3(-RoomHalf,(WinY.x+WinY.y)/2,(WinZ.x+WinZ.y)/2);var onto=new Vector3(-.75f,.45f,1.55f); // the window middle to the sofa seat
            keyLight.transform.rotation=Quaternion.LookRotation(onto-through);keyLight.color=new Color(1f,.9f,.72f);keyLight.intensity=1.9f;
            keyLight.shadows=LightShadows.Soft;keyLight.shadowStrength=.92f;keyLight.shadowBias=.03f;keyLight.shadowNormalBias=.3f;
            RenderSettings.ambientLight=new Color(.46f,.45f,.43f);
        }
        // I closes in on her (narrower lens, aim moves to her chest), O goes back to the whole room
        void RoomFrame(float dt,float zoomKey)
        {
            roomZoom=Mathf.Clamp01(roomZoom-zoomKey*dt*.7f);
            Transform chest=null;if(anim!=null)chest=anim.GetBoneTransform(HumanBodyBones.Chest)??anim.GetBoneTransform(HumanBodyBones.Hips);
            var aim=chest!=null?Vector3.Lerp(RoomAim,chest.position,Mathf.SmoothStep(0,1,roomZoom)):RoomAim;
            cameraView.fieldOfView=Mathf.Lerp(RoomFov,14,roomZoom);cameraView.transform.position=RoomEye;cameraView.transform.LookAt(aim);
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
        void Select(int index)
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
            if(key=="n"){bool back=Keyboard.current!=null && Keyboard.current.shiftKey.isPressed;if(!busy)StartCoroutine(Reload(StepLine(back?-1:1),false));}
            else if(key=="r"){if(!busy)StartCoroutine(Reload(StepLine(0),true));}
            else if(key=="space"){if(direction==0)Select(9);}
            else if(key=="l"){lookOn=!lookOn;Notice(lookOn?"주인공 바라보기":"바라보기 끝");}
            else if(key=="h"){chinOn=!chinOn;Notice(chinOn?"턱 괴기 (식탁 의자에서)":"턱 괴기 끝");}
            else if(key=="k"){if(direction==0 && !busy){if(Keyboard.current!=null && Keyboard.current.shiftKey.isPressed)NextSit(NapIndex);else Sit(NapIndex,true);}}
            else if(key=="c"||key=="v"){if(direction==0 && !busy){int index=key=="c"?SitIndex:CurlIndex;
                if(Keyboard.current!=null && Keyboard.current.shiftKey.isPressed)NextSit(index);else Sit(index);}}
            else if(key=="g"){if(direction==0 && !busy){int i=(Array.IndexOf(Walks,Keys[10].clip)+1)%Walks.Length;SetWalk(Walks[i]);try{LoadModel(modelPath);Notice("걷기 "+Walks[i]);}catch(Exception e){Notice("걷기 실패: "+e.Message);}}}
            else if(key=="escape")EditorApplication.ExitPlaymode();
            else if(key=="left"||key=="right"){heading+=key=="left"?45:-45;if(direction==0)Turn(heading);}
            else if(direction==0){int i=Array.FindIndex(Keys,k=>k.key==key);if(i>=0 && (i<9 || key=="f" || key=="z" || key=="0" || key=="y"))Select(i);}
            Log(key);
        }
        void Log(string key)=>Debug.Log("ROOMPLAY key="+key+" clip="+Keys[current].clip+" x="+F(x)+" z="+F(z)+" yaw="+F(yaw)+" heading="+F(heading)+" zoom="+F(zoom)+" floor="+F(floorError));
        // N / Shift+N walk round every line (6T-I1 -> 6T-O1 -> 6T-O2 -> ... -> 6T-I1), each at its newest build; 0 stays on the current line
        string StepLine(int step)
        {
            var dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../hari/gpt"));
            var lines=Directory.GetFiles(dir,"harin-6T-*-*.vrm").Concat(Directory.GetFiles(Path.Combine(Application.dataPath,"Models/test"),"harin-6T-*-*.glb")).Select(p=>Path.GetFileNameWithoutExtension(p).Split('-')).Where(a=>a.Length==4).Select(a=>a[1]+"-"+a[2]).Distinct()
                .OrderBy(l=>l[3]=='I'?0:l[3]=='O'?1:2).ThenBy(l=>int.TryParse(l.Substring(4),out var n)?n:0).ToList();
            var parts=(modelName??"").Split('-');string here=parts.Length==4?parts[1]+"-"+parts[2]:"6T-I1";
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
        void Update()
        {
            if(cameraView!=null){float aspect=Screen.width/(float)Mathf.Max(1,Screen.height);float target=16f/9;
                cameraView.rect=aspect>target?new Rect((1-target/aspect)/2,0,target/aspect,1):new Rect(0,(1-aspect/target)/2,1,aspect/target);}
            if(statue!=null && !busy){StatueUpdate(Mathf.Min(Time.unscaledDeltaTime,.05f));return;}
            if(model==null || busy || reviewing)return;float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);var input=Vector2.zero;bool run=false;float zoomKey=0;
            turning=0;
            if(scripted){
                stepTime-=dt;
                if(step<0 || stepTime<=0){
                    Snap();
                    step++;if(step>=script.Count){Debug.Log("ROOMPLAY_ROLL planted foot tilt most raw="+F(rollRaw)+" kept="+F(rollKept)+" deg");Debug.Log("ROOMPLAY_DONE");EditorApplication.ExitPlaymode();enabled=false;return;}
                    stepTime=script[step].seconds;
                    if(direction!=0 && !Walk(script[step].key)){direction=0;Select(9);Turn(heading);}
                    if(!Walk(script[step].key))Command(script[step].key);
                }
                input=WalkDir(script[step].key);run=script[step].key.StartsWith("shift");zoomKey=script[step].key=="i"?-1:script[step].key=="o"?1:0;
            }else{
                var kb=Keyboard.current;if(kb==null){zeroTwoInputReady=false;zeroTwoAudio?.SetState(current==ZeroTwoIndex,false,zeroTwoAudioSeconds);return;}
                input=new Vector2((kb.dKey.isPressed?1:0)-(kb.aKey.isPressed?1:0),(kb.wKey.isPressed?1:0)-(kb.sKey.isPressed?1:0));run=kb.shiftKey.isPressed;zoomKey=(kb.oKey.isPressed?1:0)-(kb.iKey.isPressed?1:0);
                turning=(kb.leftArrowKey.isPressed?1:0)-(kb.rightArrowKey.isPressed?1:0);
                foreach(var entry in Keys)if(!Walk(entry.key) && entry.key!="left" && entry.key!="right"){
                    if(kb[KeyOf(entry.key)].wasPressedThisFrame && (input==Vector2.zero || !char.IsDigit(entry.key[0])))Command(entry.key);
                }
                // an action moves while its key is held and holds the pose the moment it is let go; space returns to the idle
                bool action=current<9 || current==12 || current==ZeroTwoIndex || current==IdolIndex || current==YawnIndex;bool pressed=action && kb[KeyOf(Keys[current].key)].isPressed;
                if(action && held && !pressed)Log("hold");held=pressed;
            }
            if(busy)return;
            zeroTwoInputReady=true;
            run=run && clips[11]!=null;float pace=run?runSpeed:speed;
            var wish=input.normalized;if(room && wish!=Vector2.zero){var fw=Vector3.ProjectOnPlane(cameraView.transform.forward,Vector3.up).normalized;var rt=new Vector3(fw.z,0,-fw.x);var w3=rt*wish.x+fw*wish.y;wish=new Vector2(w3.x,w3.z);} // W walks away from the camera
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
            blend=Mathf.Min(1,blend+dt/.25f);Vector3 baseHip=Vector3.zero, drift=Vector3.zero;float sum=0;
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
        static Key KeyOf(string key)=>key=="escape"?Key.Escape:key=="space"?Key.Space:key=="left"?Key.LeftArrow:key=="right"?Key.RightArrow:char.IsDigit(key[0])?(Key)Enum.Parse(typeof(Key),"Digit"+key):(Key)Enum.Parse(typeof(Key),key.ToUpperInvariant());
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
        // the camera stays put on the wide ground; O dollies out to see her, I dollies in and centres on her
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
        void OnGUI()
        {
            float listEnd=34+Keys.Length*17;GUI.Box(new Rect(10,10,340,listEnd-6),"");var style=new GUIStyle(GUI.skin.label){fontSize=12};
            GUI.Label(new Rect(16,13,330,20),modelName??"준비 중",style);
            for(int i=0;i<Keys.Length;i++){
                bool selected=i==current;
                style.normal.textColor=selected?Color.yellow:clips!=null && i<MotionCount && clips[i]==null?Color.gray:Color.white;
                GUI.Label(new Rect(16,34+i*17,330,18),(selected?"> ":"  ")+Keys[i].key+"  "+Keys[i].clip,style);
            }
            if(Time.realtimeSinceStartup<messageUntil)GUI.Label(new Rect(16,listEnd+4,900,40),message,new GUIStyle(GUI.skin.label){fontSize=24});
        }
        void OnDestroy(){zeroTwoAudio?.Dispose();zeroTwoDance?.Dispose();if(clips!=null && clips[ZeroTwoIndex]!=null)Destroy(clips[ZeroTwoIndex]);if(graph.IsValid())graph.Destroy();if(boundsMesh!=null)Destroy(boundsMesh);if(soleBake!=null)Destroy(soleBake);EditorSettings.enterPlayModeOptionsEnabled=oldEnabled;EditorSettings.enterPlayModeOptions=oldOptions;}
    }
}
