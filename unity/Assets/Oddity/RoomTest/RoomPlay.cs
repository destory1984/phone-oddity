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
            ("5", "KaIdle24Dance04"), ("6", "AGIA_Idle_surprise_01_hands_open_front"),
            ("7", "AGIA_Idle_pitiable_01_right_hand_on_back_head"), ("8", "KaIdle44GreetingBow"),
            ("9", "BlowKissSkin"), ("0", "CatwalkIdleSkin"),
            ("a", "CatwalkSkin"), ("shift", "KaRun01"), ("f", "UAL1Standard#Sword_Attack"), ("d", "CatwalkSkin"), ("w", "CatwalkSkin"), ("s", "CatwalkSkin"), ("left", "누르는 동안 시계 방향 회전"), ("right", "누르는 동안 반시계 회전"), ("n", "다음 모델, Shift+N 이전 모델"),("r", "현재 모델 다시 불러오기"), ("space", "기본 자세"), ("g", "다음 걷기"), ("escape", "재생 끝내기"), ("i", "줌 인"), ("o", "줌 아웃")
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
        const float Limit=1.08f, ZNear=-.6f, ZFar=15f;
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
                Time.captureDeltaTime=0; Application.targetFrameRate=60;
                RenderSettings.skybox=null;
                RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight=new Color(.42f,.41f,.44f); // a dimmer fill and a stronger key so cloth folds read (was .66 fill, .55 key)
                var light=new GameObject("KeyLight").AddComponent<Light>();
                light.type=LightType.Directional;light.color=Color.white;light.intensity=1.1f;
                light.transform.rotation=Quaternion.Euler(35,120,0);
                var cam=new GameObject("Camera").AddComponent<Camera>();
                cam.fieldOfView=15;cam.nearClipPlane=.05f;cam.farClipPlane=80;
                cam.transform.SetPositionAndRotation(new Vector3(0,1.13f,-6.79f),Quaternion.Euler(3,0,0));
                cameraView=cam;cam.aspect=16f/9;Smooth(cam);Letterbox();cam.gameObject.AddComponent<AudioListener>();Background(cam);blob=RoomTestRunner.MakeBlob();blob.localScale=new Vector3(.75f,.4f,1);
                SetWalk(Environment.GetEnvironmentVariable("ODDITY_PLAYWALK")??"KaWalk01");
                LoadModel(Environment.GetEnvironmentVariable("ODDITY_MODEL")??"Assets/Models/test/harin-6T-I1-30.vrm");
                var text=Environment.GetEnvironmentVariable("ODDITY_PLAYKEYS");scripted=!string.IsNullOrWhiteSpace(text);
                if(scripted)foreach(var part in text.Split(',')){
                    var pair=part.Split(':');
                    if(pair.Length!=2 || !float.TryParse(pair[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds) || seconds<=0 || !(pair[0]=="shifts" || Keys.Any(k=>k.key==pair[0])))throw new Exception("invalid ODDITY_PLAYKEYS entry");
                    script.Add((pair[0],seconds));
                }
                Debug.Log("ROOMPLAY_READY model="+modelName);
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
            if(a==null || a.avatar==null || !a.avatar.isHuman){Destroy(candidate);throw new Exception("invalid Humanoid Avatar");}
            var puppet=candidate.AddComponent<HarinPuppet>();puppet.SetFace("neutral");puppet.enabled=false;
            var oldModel=model;var oldGraph=graph;var oldMixer=mixer;var oldPlays=plays;
            var oldClips=clips;var oldAnchors=anchors;var oldTravels=travels;var oldWeights=weights;var oldFrom=from;
            float oldSpeed=speed, oldBlend=blend;
            double phase=plays!=null && clips[current]!=null ? plays[current].GetTime()/clips[current].length : 0;
            soleMesh=null;model=candidate;anim=a;RestFeet();Arm();anim.applyRootMotion=false;
            try{BuildMotion();if(clips[current]!=null)plays[current].SetTime(phase*clips[current].length);Evaluate(0);}
            catch{if(graph.IsValid())graph.Destroy();graph=oldGraph;mixer=oldMixer;plays=oldPlays;clips=oldClips;anchors=oldAnchors;travels=oldTravels;weights=oldWeights;from=oldFrom;speed=oldSpeed;blend=oldBlend;model=oldModel;anim=oldModel!=null?oldModel.GetComponent<Animator>():null;Destroy(candidate);throw;}
            modelName=Path.GetFileNameWithoutExtension(path);modelPath=path;LogSprings(candidate);
            if(oldGraph.IsValid())oldGraph.Destroy();if(oldModel!=null)Destroy(oldModel);
        }
        void BuildMotion()
        {
            graph=PlayableGraph.Create("RoomPlayMotion");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer=AnimationMixerPlayable.Create(graph,13);
            AnimationPlayableOutput.Create(graph,"Harin",anim).SetSourcePlayable(mixer);
            clips=new AnimationClip[13];plays=new AnimationClipPlayable[13];anchors=new Vector3[13];travels=new Vector3[13];weights=new float[13];from=new float[13];
            for(int i=0;i<13;i++){
                try{clips[i]=RoomTestRunner.Clip(Keys[i].clip);}catch{Debug.LogWarning("ROOMPLAY missing clip="+Keys[i].clip);continue;}
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
            else if(key=="g"){if(direction==0 && !busy){int i=(Array.IndexOf(Walks,Keys[10].clip)+1)%Walks.Length;SetWalk(Walks[i]);try{LoadModel(modelPath);Notice("걷기 "+Walks[i]);}catch(Exception e){Notice("걷기 실패: "+e.Message);}}}
            else if(key=="escape")EditorApplication.ExitPlaymode();
            else if(key=="left"||key=="right"){heading+=key=="left"?45:-45;if(direction==0)Turn(heading);}
            else if(direction==0){int i=Array.FindIndex(Keys,k=>k.key==key);if(i>=0 && (i<10 || key=="f"))Select(i);}
            Log(key);
        }
        void Log(string key)=>Debug.Log("ROOMPLAY key="+key+" clip="+Keys[current].clip+" x="+F(x)+" z="+F(z)+" yaw="+F(yaw)+" heading="+F(heading)+" zoom="+F(zoom)+" floor="+F(floorError));
        // N / Shift+N walk round every line (6T-I1 -> 6T-O1 -> 6T-O2 -> ... -> 6T-I1), each at its newest build; 0 stays on the current line
        string StepLine(int step)
        {
            var dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../hari/gpt"));
            var lines=Directory.GetFiles(dir,"harin-6T-*-*.vrm").Select(p=>Path.GetFileNameWithoutExtension(p).Split('-')).Where(a=>a.Length==4).Select(a=>a[1]+"-"+a[2]).Distinct()
                .OrderBy(l=>l[3]=='I'?0:1).ThenBy(l=>int.TryParse(l.Substring(4),out var n)?n:0).ToList();
            var parts=(modelName??"").Split('-');string here=parts.Length==4?parts[1]+"-"+parts[2]:"6T-I1";
            if(lines.Count==0)return here;
            int at=lines.IndexOf(here);if(at<0)return lines[0];
            return lines[((at+step)%lines.Count+lines.Count)%lines.Count];
        }
        IEnumerator Reload(string line,bool force)
        {
            busy=true;string latest=null;
            try{
                var dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../../hari/gpt"));
                latest=Directory.GetFiles(dir,"harin-"+line+"-*.vrm").OrderByDescending(p=>int.TryParse(Path.GetFileNameWithoutExtension(p).Split('-').Last(),out var n)?n:-1).FirstOrDefault();
                if(latest==null)throw new Exception("no "+line+" model");
            }catch(Exception e){Notice("모델 탐색 실패: "+e.Message);busy=false;yield break;}
            var name=Path.GetFileNameWithoutExtension(latest);
            if(name==modelName && !force){Notice("이미 최신 "+name.Replace("harin-",""));busy=false;yield break;}
            Notice(name.Replace("harin-","")+" 불러오는 중");yield return null;yield return new WaitForEndOfFrame();
            var timer=System.Diagnostics.Stopwatch.StartNew();
            try{
                string asset="Assets/Models/test/"+Path.GetFileName(latest);
                File.Copy(latest,Path.Combine(Application.dataPath,"Models/test",Path.GetFileName(latest)),true);
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
            if(model==null || busy)return;float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);var input=Vector2.zero;bool run=false;float zoomKey=0;
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
                var kb=Keyboard.current;if(kb==null)return;
                input=new Vector2((kb.dKey.isPressed?1:0)-(kb.aKey.isPressed?1:0),(kb.wKey.isPressed?1:0)-(kb.sKey.isPressed?1:0));run=kb.shiftKey.isPressed;zoomKey=(kb.oKey.isPressed?1:0)-(kb.iKey.isPressed?1:0);
                turning=(kb.leftArrowKey.isPressed?1:0)-(kb.rightArrowKey.isPressed?1:0);
                foreach(var entry in Keys)if(!Walk(entry.key) && entry.key!="left" && entry.key!="right"){
                    if(kb[KeyOf(entry.key)].wasPressedThisFrame && (input==Vector2.zero || !char.IsDigit(entry.key[0])))Command(entry.key);
                }
                // an action moves while its key is held and holds the pose the moment it is let go; space returns to the idle
                bool action=current<9 || current==12;bool pressed=action && kb[KeyOf(Keys[current].key)].isPressed;
                if(action && held && !pressed)Log("hold");held=pressed;
            }
            run=run && clips[11]!=null;float pace=run?runSpeed:speed;
            var wish=input.normalized;var next=new Vector2(Mathf.Clamp(x+wish.x*pace*dt,-Limit,Limit),Mathf.Clamp(z+wish.y*pace*dt,ZNear,ZFar));
            if((next-new Vector2(x,z)).sqrMagnitude<1e-10f||clips[10]==null)wish=Vector2.zero;
            int dir=wish==Vector2.zero?0:run?2:1;
            // letting go of the walk or run keys holds her in that stride (space returns to the idle); a scripted run still goes back to the idle
            if(dir!=direction){if(dir==0 && !scripted)heading=targetYaw;direction=dir;if(dir!=0 || scripted)Select(dir==0?9:dir==2?11:10);Log(dir==0?"stop":dir==2?"run":"walk");}
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
        // a blank face in the idle (key 0, space), a held smile in every other clip; it eases in and out over 0.3 s
        const float SmileWeight=.7f; // relaxed, the closed-mouth smile the room test films use
        float smile;
        void Smile(float dt)
        {
            var vrm=model.GetComponent<Vrm10Instance>();if(vrm==null || vrm.Runtime==null)return;
            smile=Mathf.MoveTowards(smile,current==9?0:SmileWeight,dt*SmileWeight/.3f);
            vrm.Runtime.Expression.SetWeight(ExpressionKey.Relaxed,smile);
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
        bool Frozen(int i)=>!scripted && i==current && ((i<9 || i==12) && !held || (i==10 || i==11) && direction==0);
        void Evaluate(float dt)
        {
            blend=Mathf.Min(1,blend+dt/.25f);bodyFix=walkTwist*weights[10]+runTwist*weights[11];Vector3 baseHip=Vector3.zero, drift=Vector3.zero;float sum=0;
            for(int i=0;i<13;i++)if(clips[i]!=null){
                weights[i]=Mathf.Lerp(from[i],i==current?1:0,blend);mixer.SetInputWeight(i,weights[i]);
                plays[i].SetTime((plays[i].GetTime()+(Frozen(i)?0:dt))%clips[i].length);baseHip+=anchors[i]*weights[i];drift+=travels[i]*(float)(plays[i].GetTime()/clips[i].length)*weights[i];sum+=weights[i];
            }
            model.transform.SetPositionAndRotation(new Vector3(x,0,z),Quaternion.Euler(0,yaw-bodyFix,0));graph.Evaluate(0);
            var hips=anim.GetBoneTransform(HumanBodyBones.Hips);var p=hips.localPosition;
            if(sum>0){p-=drift/sum;p.x=Mathf.Clamp(p.x,baseHip.x/sum-.2f,baseHip.x/sum+.2f);p.z=Mathf.Clamp(p.z,baseHip.z/sum-.2f,baseHip.z/sum+.2f);hips.localPosition=p;}
            model.transform.SetPositionAndRotation(new Vector3(x,0,z),Quaternion.Euler(0,yaw-bodyFix,0));
            DeepenBow();
            LimitWrists();
            LevelFeet(dt);
            Meow();
            Smile(dt);
            Ground(dt);
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
            lift=float.IsNaN(lift)?-lowest:Mathf.SmoothDamp(lift,-lowest,ref liftSpeed,.1f,Mathf.Infinity,Mathf.Max(dt,.001f));
            model.transform.position=new Vector3(x,lift,z);
            floorError=Mathf.Max(floorError,Mathf.Abs(lift+lowest));
        }
        // the camera stays put on the wide ground; O dollies out to see her, I dollies in and centres on her
        void Frame(float dt,float zoomKey)
        {
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
                style.normal.textColor=selected?Color.yellow:clips!=null && i<12 && clips[Math.Min(i,10)]==null?Color.gray:Color.white;
                GUI.Label(new Rect(16,34+i*17,330,18),(selected?"> ":"  ")+Keys[i].key+"  "+Keys[i].clip,style);
            }
            if(Time.realtimeSinceStartup<messageUntil)GUI.Label(new Rect(16,listEnd+4,900,40),message,new GUIStyle(GUI.skin.label){fontSize=24});
        }
        void OnDestroy(){if(graph.IsValid())graph.Destroy();if(boundsMesh!=null)Destroy(boundsMesh);if(soleBake!=null)Destroy(soleBake);EditorSettings.enterPlayModeOptionsEnabled=oldEnabled;EditorSettings.enterPlayModeOptions=oldOptions;}
    }
}
