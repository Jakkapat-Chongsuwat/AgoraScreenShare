using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using agora_gaming_rtc;
using UnityEngine.UI;


/* NOTE: 
 *
 * This script handles the Agora-related functionality:
 * - Joining / Leaving Channels
 * - Creating / Deleting VideoSurface objects that enable us to see the camera feed of Agora party chat
 * - Managing the UI that contains the VideoSurface objects 
 *
 */



public class AgoraVideoChat : MonoBehaviourPun
{
    [Header("Agora Properties")]

    // *** ADD YOUR APP ID HERE BEFORE GETTING STARTED *** //
    [SerializeField] private string appID = "ADD YOUR APP ID HERE";
    [SerializeField] private string channel = "unity3d";
    private string originalChannel;
    private IRtcEngine mRtcEngine;
    private uint myUID = 0;

    [Header("Player Video Panel Properties")]
    [SerializeField] private GameObject userVideoPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float spaceBetweenUserVideos = 150f;
    private List<GameObject> playerVideoList;

    public delegate void AgoraCustomEvent();
    public static event AgoraCustomEvent PlayerChatIsEmpty;
    public static event AgoraCustomEvent PlayerChatIsPopulated;

    // Share Screen part
    static IVideoChatClient app = null;
    bool isSharing = false;

    [SerializeField]
    GameObject screenSharingCanvas;
    [SerializeField]
    Collider screenSharingZone;

    string audioRecordingDeviceName;
    string audioRecordingDeviceId;
    AudioRecordingDeviceManager audioRecordingDeviceManager;

    void Start()
    {

        if (!photonView.IsMine)
        {
            return;
        }

        app = new DesktopScreenShare();

        playerVideoList = new List<GameObject>();

        // Setup Agora Engine and Callbacks.
        if(mRtcEngine != null)
        {
            IRtcEngine.Destroy();
        }

        originalChannel = channel;

        // -- These are all necessary steps to initialize the Agora engine -- //
        // Initialize Agora engine
        mRtcEngine = IRtcEngine.GetEngine(appID);

        // Setup square video profile
        VideoEncoderConfiguration config = new VideoEncoderConfiguration();
        config.dimensions.width = 480;
        config.dimensions.height = 480;
        config.frameRate = FRAME_RATE.FRAME_RATE_FPS_60;
        config.bitrate = 750;
        config.degradationPreference = DEGRADATION_PREFERENCE.MAINTAIN_FRAMERATE;
        mRtcEngine.SetVideoEncoderConfiguration(config);
        mRtcEngine.SetAudioProfile(AUDIO_PROFILE_TYPE.AUDIO_PROFILE_MUSIC_HIGH_QUALITY_STEREO, AUDIO_SCENARIO_TYPE.AUDIO_SCENARIO_GAME_STREAMING);

        // Setup our callbacks.
        mRtcEngine.OnJoinChannelSuccess = OnJoinChannelSuccessHandler;
        mRtcEngine.OnUserJoined = OnUserJoinedHandler;
        mRtcEngine.OnLeaveChannel = OnLeaveChannelHandler;

        // Your video feed will not render if EnableVideo() isn't called. 
        mRtcEngine.EnableVideo();
        mRtcEngine.EnableVideoObserver();
        mRtcEngine.EnableLocalVideo(false);
        
        // Check audio device
        audioRecordingDeviceManager = (AudioRecordingDeviceManager) mRtcEngine.GetAudioRecordingDeviceManager();
        audioRecordingDeviceManager.CreateAAudioRecordingDeviceManager();

        int count = audioRecordingDeviceManager.GetAudioRecordingDeviceCount();
        Debug.Log("Device count = " + count);
        audioRecordingDeviceManager.GetAudioRecordingDevice(0, ref audioRecordingDeviceName, ref audioRecordingDeviceId);
        Debug.Log("audioRecordingDeviceName " + audioRecordingDeviceName + ", audioRecordingDeviceId" + audioRecordingDeviceId);

        // Setup audio
        mRtcEngine.EnableAudio();
        audioRecordingDeviceManager.SetAudioRecordingDeviceMute(true);
        mRtcEngine.EnableLoopbackRecording(true, null);



        // By setting our UID to "0" the Agora Engine creates a unique UID and returns it in the OnJoinChannelSuccess callback. 
        mRtcEngine.JoinChannel(channel, null, 0);
    }
    private void Update()
    {
        Debug.Log(audioRecordingDeviceManager.IsAudioRecordingDeviceMute());
    }

    public string GetCurrentChannel() => channel;

    
    #region Agora Callbacks
    // Local Client Joins Channel.
    private void OnJoinChannelSuccessHandler(string channelName, uint uid, int elapsed)
    {
        if (!photonView.IsMine)
        {
            return;
        }   

        myUID = uid;

        Debug.Log("Join success: uid = " + uid + "Channel name = " + channelName);
        //CreateUserVideoSurface(uid, true);
    }

    public void OnViewControllerFinish()
    {
        if (!ReferenceEquals(app, null))
        {
            app = null; // delete app
        }
        Destroy(gameObject);
    }

    // Remote Client Joins Channel.
    private void OnUserJoinedHandler(uint uid, int elapsed)
    {
        if (!photonView.IsMine)
        {
            return;
        }

        //CreateUserVideoSurface(uid, false);
    }

    // Local user leaves channel.
    private void OnLeaveChannelHandler(RtcStats stats)
    {
        if (!photonView.IsMine)
        {
            return;
        }

        foreach (GameObject player in playerVideoList)
        {
            Destroy(player.gameObject);
        }
        playerVideoList.Clear();
    }

    #endregion

    // Create new image plane to display.
    private void CreateUserVideoSurface(uint uid, bool isLocalUser)
    {
        // Avoid duplicating Local player VideoSurface image plane.
        for (int i = 0; i < playerVideoList.Count; i++)
        {
            if (playerVideoList[i].name != uid.ToString())
            {
                return;
            }
        }

        // Create Gameobject that will serve as our VideoSurface.
        GameObject newUserVideo = Instantiate(userVideoPrefab, Vector3.zero , spawnPoint.rotation);

        if (newUserVideo == null)
        {
            Debug.LogError("CreateUserVideoSurface() - newUserVideoIsNull");
            return;
        }

        newUserVideo.name = uid.ToString();
        newUserVideo.transform.SetParent(spawnPoint, false);
        newUserVideo.transform.rotation = Quaternion.Euler(Vector3.forward * 180);
        newUserVideo.GetComponent<RectTransform>().sizeDelta = new Vector2(10, 10);

        playerVideoList.Add(newUserVideo);

        // Update our VideoSurface to reflect new users
        VideoSurface newVideoSurface = newUserVideo.GetComponent<VideoSurface>();
        if(newVideoSurface == null)
        {
            Debug.LogError("CreateUserVideoSurface() - VideoSurface component is null on newly joined user");
            return;
        }

        if (isLocalUser == false)
        {
            newVideoSurface.SetForUser(uid);
        }

        newVideoSurface.videoFps = 60;
    }

    private void RemoveUserVideoSurface(uint deletedUID)
    {
        foreach (GameObject player in playerVideoList)
        {
            if (player.name == deletedUID.ToString())
            {
                playerVideoList.Remove(player);
                Destroy(player.gameObject);
                break;
            }
        }

       
    }

    private void UpdatePlayerVideoPostions()
    {
        for (int i = 0; i < playerVideoList.Count; i++)
        {
            playerVideoList[i].GetComponent<RectTransform>().anchoredPosition = Vector2.down * spaceBetweenUserVideos * i;
        }
    }

    private void UpdateLeavePartyButtonState()
    {
        if (playerVideoList.Count > 1)
        {
            PlayerChatIsPopulated();
        }
        else
        {
            PlayerChatIsEmpty();
        }
    }

    private void TerminateAgoraEngine()
    {
        if (mRtcEngine != null)
        {
            mRtcEngine.LeaveChannel();
            mRtcEngine = null;
            IRtcEngine.Destroy();
        }
    }

    private IEnumerator OnLeftRoom()
    {
        //Wait untill Photon is properly disconnected (empty room, and connected back to main server)
        while (PhotonNetwork.CurrentRoom != null || PhotonNetwork.IsConnected == false)
        {
            yield return 0;
        }

        TerminateAgoraEngine();
    }

    // Cleaning up the Agora engine during OnApplicationQuit() is an essential part of the Agora process with Unity. 
    private void OnApplicationQuit()
    {
        TerminateAgoraEngine();
    }


    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("Stage") /*&& PhotonNetwork.IsMasterClient*/)
        {
            // set screen sharing canvas active
            if (screenSharingCanvas != null && !screenSharingCanvas.activeSelf && photonView.IsMine)
            {

                if (!isSharing)
                {
                    isSharing = true;

                    CreateUserVideoSurface(myUID, true);
                    screenSharingCanvas.SetActive(true);
                    app.LoadEngine(appID);
                    app.OnSceneLoaded();

                    string stringUID = myUID.ToString();
                    photonView.RPC("OnScreenShare", RpcTarget.Others, stringUID, false);

                }

            }

        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Stage") /*&& PhotonNetwork.IsMasterClient*/)
        {
            // set screen sharing canvas active
            if (screenSharingCanvas != null && screenSharingCanvas.activeSelf && photonView.IsMine)
            {
                isSharing = false;
                string stringUID = myUID.ToString();
                photonView.RPC("RemoveScreenShare", RpcTarget.Others, stringUID);

                screenSharingCanvas.SetActive(false);
                mRtcEngine.StopScreenCapture();
                RemoveUserVideoSurface(myUID);
            }

        }
    }


    [PunRPC]
    void OnScreenShare(string shareUid,bool isLocalUser)
    {
        uint newUid = uint.Parse(shareUid);
        CreateUserVideoSurface(newUid, isLocalUser);
    }

    [PunRPC]
    void RemoveScreenShare(string shareUid)
    {
        uint newUid = uint.Parse(shareUid);
        RemoveUserVideoSurface(newUid);
    }
}