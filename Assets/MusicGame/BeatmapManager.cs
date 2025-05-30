using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

public class BeatmapManager : MonoBehaviour
{
    float OnPlayingTime = 0;
    float BeforeTime = 3;
    float iniOffset = 3;
    float BPM = 0;
    float offset = DataStorager.settings.offsetMs / 1000;
    float videoOffset = 0;
    float MaxPoint = 0;
    float NowPoint = 0;
    float MaxPlusPoint = 0;
    float NowPlusPoint = 0;
    int Combo = 0;
    int MaxCombo = 0;
    int FullCombo = 0;

    public struct Point_Detail {
        public int perfect;
        public int great;
        public int miss;
        public int break_p;
        public int break_g;
        public int break_m;
    }

    Point_Detail point_detail;

    bool isPlaying = false;
    bool hasVideo = false;
    bool isVideoPlaying = false;
    bool isEnd = false;
    bool isSaved = false;
    bool isAutoPlay = DataStorager.settings.isAutoPlay;
    float distance = 10;
    public GameObject Player;
    public GameObject[] ObstacleList;
    public AudioSource MusicPlayer;
    public RawImage BackForVideo;
    public RawImage BackForImage;
    public RawImage BackForVideo2;
    public RawImage BackForImage2;
    public VideoPlayer videoPlayer;
    public GameObject ComboDisplay;
    public GameObject ResultCanvas;
    public GameObject AutoPlayImage;
    public GameObject CinemaImage;
    public GameObject RelaxModImage;
    public Animator ShowFrontVideo;
    public Animator MapInfo;

    // 谱面信息展示
    public RawImage DisplayInfoImage;
    public TMP_Text DisplayInfoText;
    public LevelDisplayer levelDisplayer;

    // 自动游玩变量
    bool last_record = false;
    float last_change_time;
    float should_change_time;
    bool ready_to_change_bpm = false;
    bool ready_to_change_hidden = false;
    List<float> should_change_bpm = new();
    List<float> should_change_bpm_time = new();
    List<float> should_change_hidden_time = new();
    float autoShift = 0.0f;

    string dataFolder;

    enum B_TYPE {
        BEAT_TYPE,
        BEST_BEAT_TYPE,
        GAINT_BEAT_TYPE,
        BPM_TYPE,
        HIDE_FRONT_TYPE,
        SHOW_BEAT_TYPE,
        FINISH,
    }
    struct SingleBeat {
        public int type;
        public float beat_time;
        public float track;
        public int stack;
        public int rem_stack;
        public float size;
        public float y_offset;
        public float BPM;
    }

    private List<SingleBeat> remain_beats = new();
    private List<SingleBeat> auto_remain_beats = new();

    public float getBPM(){
        return BPM;
    }

    public void LoadData(string beatmap_name){
        if(File.Exists($"{dataFolder}/{beatmap_name}/music.wav")){
            StartCoroutine(LoadMusic($"file://{dataFolder}/{beatmap_name}/music.wav", AudioType.WAV));
        } else if(File.Exists($"{dataFolder}/{beatmap_name}/music.mp3")){
            StartCoroutine(LoadMusic($"file://{dataFolder}/{beatmap_name}/music.mp3", AudioType.MPEG));
        };
        // 读取图片或视频
        if(File.Exists($"{dataFolder}/{beatmap_name}/bg.mp4")){
            videoPlayer.targetTexture = (RenderTexture)BackForVideo.texture;
            videoPlayer.playOnAwake = false;
            videoPlayer.url = $"file://{dataFolder}/{beatmap_name}/bg.mp4";
            hasVideo = true;
        } else {
            BackForVideo.GameObject().SetActive(false);
        }
        if(File.Exists($"{dataFolder}/{beatmap_name}/bg.png")){
            byte[] fileData = File.ReadAllBytes($"{dataFolder}/{beatmap_name}/bg.png");
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(fileData); // 自动调整纹理大小
            BackForImage.texture = texture;
            BackForImage2.texture = texture;
            BackForImage.GetComponent<AspectRatioFitter>().aspectRatio = (float)texture.width / texture.height;
            BackForImage2.GetComponent<AspectRatioFitter>().aspectRatio = (float)texture.width / texture.height;
            DisplayInfoImage.texture = texture;
            DisplayInfoImage.GetComponent<AspectRatioFitter>().aspectRatio = (float)texture.width / texture.height;
        }
        // 读取谱面
        string path = $"{dataFolder}/{beatmap_name}/data.sdz";
        if(!Directory.Exists(dataFolder)){
            Directory.CreateDirectory(dataFolder);
        }
        float last_time = 0;

        List<SingleBeat> storage_beats = new();

        foreach( string line in File.ReadAllText(path).Split("\n")){
            string[] data = line.Split("=");
            if(data[0].Trim() == "bpm"){
                BPM = float.Parse(data[1].Trim());
                remain_beats.Add(
                    new SingleBeat(){
                        type = (int)B_TYPE.BPM_TYPE,
                        beat_time = 0,
                        BPM = float.Parse(data[1].Trim())
                    }
                );
                continue;
            }
            if(data[0].Trim() == "offset"){
                offset += float.Parse(data[1].Trim());
                continue;
            }
            if(data[0].Trim() == "bg_offset"){
                videoOffset = float.Parse(data[1].Trim());
                continue;
            }
            if(data[0].Trim() == "title"){
                DisplayInfoText.text = data[1].Trim();
                continue;
            }
            if(data[0].Trim() == "level"){
                levelDisplayer.level = float.Parse(data[1].Trim());
                continue;
            }
            data = line.Split(",");
            // 表演墩子
            if(data[0] == "S"){
                float slice_beat = float.Parse(data[3]) > 0 ? float.Parse(data[2]) / float.Parse(data[3]) : 0;
                float beat_time = last_time + (float.Parse(data[1]) + slice_beat) * (60 / BPM) + offset;
                int stack_count = int.Parse(data[5]);
                int rem_stack = 0;
                if(data.Count() >= 7){
                    rem_stack = int.Parse(data[6]);
                }
                float size = 1;
                if(data.Count() >= 8){
                    size = float.Parse(data[7]);
                }
                float y_offset = 0;
                if(data.Count() >= 9){
                    y_offset = float.Parse(data[8]);
                }
                storage_beats.Add(
                    new SingleBeat(){
                        type = (int)B_TYPE.SHOW_BEAT_TYPE,
                        beat_time = beat_time,
                        track = float.Parse(data[4]),
                        stack = stack_count,
                        rem_stack = rem_stack,
                        size = size,
                        y_offset = y_offset
                    }
                );
                continue;
            }
            if(data[0] == "D"){
                float slice_beat = float.Parse(data[3]) > 0 ? float.Parse(data[2]) / float.Parse(data[3]) : 0;
                float beat_time = last_time + (float.Parse(data[1]) + slice_beat) * (60 / BPM) + offset;
                int stack_count = int.Parse(data[5]);
                int rem_stack = 0;
                if(data.Count() >= 7){
                    rem_stack = int.Parse(data[6]);
                }
                float size = 1;
                if(data.Count() >= 8){
                    size = float.Parse(data[7]);
                }
                float y_offset = 0;
                if(data.Count() >= 9){
                    y_offset = float.Parse(data[8]);
                }
                storage_beats.Add(
                    new SingleBeat(){
                        type = (int)B_TYPE.BEAT_TYPE,
                        beat_time = beat_time,
                        track = float.Parse(data[4]),
                        stack = stack_count,
                        rem_stack = rem_stack,
                        size = size,
                        y_offset = y_offset
                    }
                );
                MaxPoint += stack_count - rem_stack;
                FullCombo += stack_count - rem_stack;
                continue;
            }
            if(data[0] == "X"){
                float slice_beat = float.Parse(data[3]) > 0 ? float.Parse(data[2]) / float.Parse(data[3]) : 0;
                float beat_time = last_time + (float.Parse(data[1]) + slice_beat) * (60 / BPM) + offset;
                int stack_count = int.Parse(data[5]);
                int rem_stack = 0;
                if(data.Count() >= 7){
                    rem_stack = int.Parse(data[6]);
                }
                float size = 1;
                if(data.Count() >= 8){
                    size = float.Parse(data[7]);
                }
                float y_offset = 0;
                if(data.Count() >= 9){
                    y_offset = float.Parse(data[8]);
                }
                storage_beats.Add(
                    new SingleBeat(){
                        type = (int)B_TYPE.BEST_BEAT_TYPE,
                        beat_time = beat_time,
                        track = float.Parse(data[4]),
                        stack = stack_count,
                        rem_stack = rem_stack,
                        size = size,
                        y_offset = y_offset
                    }
                );
                MaxPoint += stack_count - rem_stack;
                MaxPlusPoint += stack_count - rem_stack;
                FullCombo += stack_count - rem_stack;
                continue;
            }
            if(data[0] == "H"){
                float slice_beat = float.Parse(data[3]) > 0 ? float.Parse(data[2]) / float.Parse(data[3]) : 0;
                float beat_time = last_time + (float.Parse(data[1]) + slice_beat) * (60 / BPM) + offset;
                storage_beats.Add(
                    new SingleBeat(){
                        type = (int)B_TYPE.HIDE_FRONT_TYPE,
                        beat_time = beat_time,
                    }
                );
                continue;
            }
            // BPM 是刷新 storage_beats 并存入的标志。
            if(data[0] == "B"){
                float slice_beat = float.Parse(data[3]) > 0 ? float.Parse(data[2]) / float.Parse(data[3]) : 0;
                float beat_time = last_time + (float.Parse(data[1]) + slice_beat) * (60 / BPM) + offset;
                last_time = beat_time - offset;
                BPM = float.Parse(data[4]);
                storage_beats.Add(
                    new SingleBeat(){
                        type = (int)B_TYPE.BPM_TYPE,
                        beat_time = beat_time,
                        BPM = float.Parse(data[4])
                    }
                );
                storage_beats.Sort((x,y) => x.beat_time > y.beat_time ? 1 : -1);
                remain_beats.AddRange(storage_beats);
                storage_beats.Clear();
                continue;
            }
        }
        // 最后再加一次。
        storage_beats.Sort((x,y) => x.beat_time > y.beat_time ? 1 : -1);
        remain_beats.AddRange(storage_beats);
        storage_beats.Clear();
    }

    IEnumerator LoadMusic(string path, AudioType audioType)
    {
        using UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(path, audioType);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.ConnectionError)
        {
            Debug.Log(www.error);
        }
        else
        {
            AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
            MusicPlayer.clip = clip;
        }
    }

    // IEnumerator LoadVideo(string path)
    // {
    //     using UnityWebRequest www = UnityWebRequest.Get(path);
    //     yield return www.SendWebRequest();

    //     if (www.result == UnityWebRequest.Result.ConnectionError)
    //     {
    //         Debug.Log(www.error);
    //     }
    //     else
    //     {
    //         videoPlayer.url = path;
    //     }
    // }

    private void Awake() {
        Application.targetFrameRate = 300;

        dataFolder = $"{Application.persistentDataPath}/music";
        LoadData(BeatmapInfo.beatmap_name);
        remain_beats.Add(
            new SingleBeat(){
                type = (int)B_TYPE.FINISH,
                track = 2
            }
        );
        ComboDisplay.SetActive(false);
        ResultCanvas.SetActive(false);
        auto_remain_beats.AddRange(remain_beats);
        if(!isAutoPlay){
            AutoPlayImage.SetActive(false);
        }
        if(!DataStorager.settings.relaxMod){
            RelaxModImage.SetActive(false);
        }
        if(!DataStorager.settings.cinemaMod){
            CinemaImage.SetActive(false);
        }
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    int[] detect_list = {
        (int)B_TYPE.BEAT_TYPE,
        (int)B_TYPE.BEST_BEAT_TYPE,
        (int)B_TYPE.SHOW_BEAT_TYPE
    };


    bool Intersects(float a1, float b1, float a2, float b2)
    {
        return (a1 < b2) && (a2 < b1);
    }

    int[] toTouchTracks(float track, float size = 1){
        List<int> move_tracks = new();
        float left_track = 2 * track - size;
        float right_track = 2 * track + size;
        for(int k = 1;k <= 3;k ++){
            if(Intersects(left_track, right_track, k * 2 - 1, k * 2 + 1)){
                move_tracks.Add(k);
            }
        }
        return move_tracks.ToArray();
    }

    public float GetPlayingTime(){
        return -BeforeTime + OnPlayingTime + iniOffset;
    }

    public Point_Detail GetPointDetail() {
        return point_detail;
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        // 自动游玩
        autoplay();

        while( detect_list.Contains(remain_beats[0].type) && remain_beats[0].beat_time - OnPlayingTime + BeforeTime < 5){
            Vector3 place_pos;
            place_pos.z = (remain_beats[0].beat_time + iniOffset) * Player.GetComponent<Player>().GetVelocity();
            place_pos.x = (float)((remain_beats[0].track - 2) * 3);
            for(int i = remain_beats[0].rem_stack;i < remain_beats[0].stack; i++){
                place_pos.y = remain_beats[0].y_offset + i * 2 * remain_beats[0].size;
                GameObject obs;
                switch(remain_beats[0].type){
                    case (int)B_TYPE.BEAT_TYPE: {
                        obs = Instantiate(ObstacleList[0]);
                        break;
                    };
                    case (int)B_TYPE.SHOW_BEAT_TYPE: {
                        obs = Instantiate(ObstacleList[0]);
                        obs.GetComponent<MusicObstacle>().setShowNote();
                        break;
                    };
                    case (int)B_TYPE.BEST_BEAT_TYPE: {
                        obs = Instantiate(ObstacleList[1]);
                        obs.GetComponent<MusicObstacle>().setBestNote();
                        break;
                    };
                    default: {
                        obs = Instantiate(ObstacleList[0]);
                        break;
                    }
                };
                obs.GetComponent<MusicObstacle>().setNote();
                obs.GetComponent<MusicObstacle>().track = toTouchTracks(remain_beats[0].track, remain_beats[0].size);
                obs.transform.position = place_pos;
                obs.transform.localScale *= remain_beats[0].size;
                if(remain_beats[0].size != 1){
                    obs.transform.localScale *= (float)4 / 3;
                }
                if(remain_beats[1].type == (int)B_TYPE.FINISH){
                    obs.GetComponent<MusicObstacle>().setLastNote();
                }
            }
            remain_beats.RemoveAt(0);
        }
        if(remain_beats[0].type == (int)B_TYPE.BPM_TYPE || remain_beats[0].type == (int)B_TYPE.HIDE_FRONT_TYPE){
            remain_beats.RemoveAt(0);
        }
        if(hasVideo && !isVideoPlaying){
            if(-BeforeTime + OnPlayingTime >= videoOffset){
                videoPlayer.Play();
                BackForVideo.GetComponent<AspectRatioFitter>().aspectRatio = (float)videoPlayer.width / videoPlayer.height;
                BackForVideo2.GetComponent<AspectRatioFitter>().aspectRatio = (float)videoPlayer.width / videoPlayer.height;
            }
        }

        if(BeforeTime > 0){
            BeforeTime -= Time.fixedDeltaTime;
            return;
        }
        if(!isPlaying){
            isPlaying = !isPlaying;
            MusicPlayer.Play();
        }
        if(isEnd && !isSaved){
            ResultCanvas.SetActive(true);
            MapInfo.SetTrigger("ResultTrigger");
            if(!isAutoPlay && !DataStorager.settings.relaxMod && !DataStorager.settings.cinemaMod
                && !(DateTime.Now.Day == 1 && DateTime.Now.Month == 4) ){
                SaveResult();
            }
            isSaved = true;
        }
        OnPlayingTime = MusicPlayer.time;
    }


    public struct BeatmapResult {
        public int rating;
        public float achievement;
        public int maxCombo;
        public long achieveTime;
        public Point_Detail point_detail;
    }

    enum Rating {SSSp,SSS,SSp,SS,Sp,S,AAA,AA,A,BBB,BB,B,C,D,F};

    void autoplay() {
        if(auto_remain_beats[0].type == (int)B_TYPE.BPM_TYPE){
            ready_to_change_bpm = true;
            should_change_bpm_time.Add(auto_remain_beats[0].beat_time);
            should_change_bpm.Add(auto_remain_beats[0].BPM);
            auto_remain_beats.RemoveAt(0);
        }
        if(auto_remain_beats[0].type == (int)B_TYPE.HIDE_FRONT_TYPE){
            ready_to_change_hidden = true;
            should_change_hidden_time.Add(auto_remain_beats[0].beat_time);
            auto_remain_beats.RemoveAt(0);
        }
        if(ready_to_change_bpm){
            if(OnPlayingTime - BeforeTime >= should_change_bpm_time[0]){
                BPM = should_change_bpm[0];
                should_change_bpm_time.RemoveAt(0);
                should_change_bpm.RemoveAt(0);
                if(should_change_bpm_time.Count <= 0){
                    ready_to_change_bpm = false;
                }
            }
        }
        if(ready_to_change_hidden){
            if(OnPlayingTime - BeforeTime >= should_change_hidden_time[0]){
                ShowFrontVideo.SetBool("ShowBool",!ShowFrontVideo.GetBool("ShowBool"));
                should_change_hidden_time.RemoveAt(0);
                if(should_change_hidden_time.Count <= 0){
                    ready_to_change_hidden = false;
                }
            }
        }
        if(isAutoPlay){
            if(!detect_list.Contains(auto_remain_beats[0].type) && auto_remain_beats[0].type != (int)B_TYPE.FINISH){
                auto_remain_beats.RemoveAt(0);
                return;
            }
            // 先判断是不是需要大跳
            if((auto_remain_beats[0].stack > 1 || auto_remain_beats[0].y_offset > 1) && Player.GetComponent<Player>().GetPos().y < 0.01f){
                float jump_should_remain_time = (float)Math.Sqrt(Math.Pow(2,(int)Math.Log(auto_remain_beats[0].stack * auto_remain_beats[0].size + auto_remain_beats[0].y_offset,2) + 1) * 2 / Player.GetComponent<Player>().GetGravity());
                if(Player.GetComponent<Player>().GetPos().z / Player.GetComponent<Player>().GetVelocity() + jump_should_remain_time - autoShift > auto_remain_beats[0].beat_time + iniOffset){
                    int jump_times = (int)Math.Log(auto_remain_beats[0].stack * auto_remain_beats[0].size + auto_remain_beats[0].y_offset,2);
                    for(int k = 0;k < jump_times; k++){
                        Player.GetComponent<Player>().moveUp();
                    }
                }
            }
            int[] should_tracks = toTouchTracks(auto_remain_beats[0].track, remain_beats[0].size);
            if(!should_tracks.Contains(Player.GetComponent<Player>().GetNowTrack()) && (should_tracks.Count() > 0)){
                if(!last_record){
                    last_change_time = OnPlayingTime - BeforeTime;
                    last_record = true;
                    float switch_time = (auto_remain_beats[0].beat_time - last_change_time) * 1 / 2;
                    if(switch_time < 0.25){
                        switch_time = 0;
                    }
                    should_change_time = last_change_time + switch_time;
                }
                if(OnPlayingTime - BeforeTime >= should_change_time){
                    int should_move_times = should_tracks[0] - Player.GetComponent<Player>().GetNowTrack();
                    // 移动
                    if(should_move_times > 0){
                        for(int j = 0; j < should_move_times; j++){
                            Player.GetComponent<Player>().moveRight();
                        }
                    } else {
                        for(int j = 0; j < -should_move_times; j++){
                            Player.GetComponent<Player>().moveLeft();
                        }
                    }
                }
            }
        }
        while(Player.GetComponent<Player>().GetPos().z >= (auto_remain_beats[0].beat_time + iniOffset - autoShift) * Player.GetComponent<Player>().GetVelocity() && auto_remain_beats[0].type != (int)B_TYPE.FINISH){
            int[] should_tracks = toTouchTracks(auto_remain_beats[0].track, remain_beats[0].size);

            // 补足 Auto 的痛（
            if(!should_tracks.Contains(Player.GetComponent<Player>().GetNowTrack()) && (should_tracks.Count() > 0) && isAutoPlay){
                int should_move_times = should_tracks[0] - Player.GetComponent<Player>().GetNowTrack();
                // 移动
                if(should_move_times > 0){
                    for(int j = 0; j < should_move_times; j++){
                        Player.GetComponent<Player>().moveRight();
                    }
                } else {
                    for(int j = 0; j < -should_move_times; j++){
                        Player.GetComponent<Player>().moveLeft();
                    }
                }
            }

            if(((auto_remain_beats[0].stack > 1 && Player.GetComponent<Player>().GetPos().y > 0.1) || (Player.GetComponent<Player>().GetPos().y > 1 + auto_remain_beats[0].y_offset)) && isAutoPlay){
                Player.GetComponent<Player>().moveDown();
            }

            // 设置跨越速度
            if(detect_list.Contains(auto_remain_beats[1].type)){
                Player.GetComponent<Player>().setCrossTime(auto_remain_beats[1].beat_time - auto_remain_beats[0].beat_time);
            }

            auto_remain_beats.RemoveAt(0);
            last_record = false;
        }
    }

    void SaveResult(){
        string path = $"{Application.persistentDataPath}/record/{BeatmapInfo.beatmap_name}.dat";

        if(!Directory.Exists($"{Application.persistentDataPath}/record/")){
            Directory.CreateDirectory($"{Application.persistentDataPath}/record/");
        }

        List<BeatmapResult> data_list = new();
        if(File.Exists(path)){
            data_list = JsonConvert.DeserializeObject<List<BeatmapResult>>(File.ReadAllText(path));
        }

        BeatmapResult data = new BeatmapResult(){
            rating = GetRating(),
            achievement = GetProgress() * 100,
            maxCombo = MaxCombo,
            achieveTime = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond,
            point_detail = point_detail
        };

        data_list.Add(data);

        var jsonData = JsonConvert.SerializeObject(data_list.ToArray(), Formatting.Indented);
        File.WriteAllText(path,jsonData);
    }

    public float GetProgress() {
        float plus_point = MaxPlusPoint > 0 ? NowPlusPoint / MaxPlusPoint : 1;
        return (float)(NowPoint / MaxPoint +  plus_point * 0.01);
    }

    public void triggerEnd(){
        isEnd = true;
    }

    public enum M_TYPE {
        Perfect,
        Great,
        Miss,
        Break_P,
        Break_G,
        Break_M
    }

    public void AddNowPoint(M_TYPE mtype, bool hasPoint = true) {
        if(!hasPoint){
            return;
        }
        float point = 0;
        switch(mtype){
            case M_TYPE.Perfect: {
                point = 1;
                point_detail.perfect += 1;
                break;
            }
            case M_TYPE.Great: {
                point = 0.95f;
                point_detail.great += 1;
                break;
            }
            case M_TYPE.Miss: {
                point_detail.miss += 1;
                return;
            }
        }
        Combo += 1;
        NowPoint += point;
        MaxCombo = Math.Max(Combo,MaxCombo);
        ComboDisplay.SetActive(true);
        ComboDisplay.GetComponent<Animator>().SetTrigger("NewCombo");
    }

    public void AddNowBest(M_TYPE mtype, bool hasPoint = true) {
        if(!hasPoint){
            return;
        }
        float point = 0;
        switch(mtype){
            case M_TYPE.Break_P: {
                point = 1;
                point_detail.break_p += 1;
                break;
            }
            case M_TYPE.Break_G: {
                point = 0.95f;
                point_detail.break_g += 1;
                break;
            }
            case M_TYPE.Break_M: {
                point_detail.break_m += 1;
                return;
            }
        }
        NowPlusPoint += point;
    }

    public void Miss() {
        Combo = 0;
        ComboDisplay.SetActive(false);
    }

    public int GetCombo() {
        return Combo;
    }

    public int GetMaxCombo() {
        return MaxCombo;
    }

    public int GetFullCombo() {
        return FullCombo;
    }

    public int GetRating() {
        float proress = GetProgress();
        if(proress == 0){
            return (int)Rating.F;
        }
        else if(proress < 0.5){
            return (int)Rating.D;
        }
        else if(proress < 0.6){
            return (int)Rating.C;
        }
        else if(proress < 0.7){
            return (int)Rating.B;
        }
        else if(proress < 0.75){
            return (int)Rating.BB;
        }
        else if(proress < 0.8){
            return (int)Rating.BBB;
        }
        else if(proress < 0.9){
            return (int)Rating.A;
        }
        else if(proress < 0.94){
            return (int)Rating.AA;
        }
        else if(proress < 0.97){
            return (int)Rating.AAA;
        }
        else if(proress < 0.98){
            return (int)Rating.S;
        }
        else if(proress < 0.99){
            return (int)Rating.Sp;
        }
        else if(proress < 0.995){
            return (int)Rating.SS;
        }
        else if(proress < 1){
            return (int)Rating.SSp;
        }
        else if(proress < 1.005){
            return (int)Rating.SSS;
        } else {
            return (int)Rating.SSSp;
        }
    }
}
