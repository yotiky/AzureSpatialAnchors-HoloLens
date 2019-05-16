using HoloToolkit.Unity.InputModule;
using HoloToolkit.Unity.SpatialMapping;
using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Windows.Speech;
using UnityEngine.XR.WSA;
#if ENABLE_WINMD_SUPPORT
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
#endif // ENABLE_WINMD_SUPPORT

public class TestScript : MonoBehaviour, IInputClickHandler
{
    const string ACCOUNT_ID = "YOUR ACCOUNT ID";
    const string ACCOUNT_KEY = "YOUR ACCOUNT KEY";

    public Material cubeMaterial;
    public Material relocalizedCubeMaterial;
    public Text sessionButtonLabel;
    public Text logText;
    public Text msgText;

    List<GameObject> cubes;
    List<string> identifiers;
    CloudSpatialAnchorSession cloudAnchorSession;
    bool isDeleteProcess;

#if ENABLE_WINMD_SUPPORT
    SpeechSynthesizer synthesizer;
#endif // ENABLE_WINMD_SUPPORT

    void Start()
    {
        InputManager.Instance.AddGlobalListener(gameObject);

        this.cubes = new List<GameObject>();
        this.identifiers = new List<string>();
        this.sessionButtonLabel.text = "Connect";
        this.msgText.text = "start session";
        this.logText.text = "initialized";
    }

    async Task OnSessionAsync()
    {
        if (this.cloudAnchorSession == null)
        {
            this.cloudAnchorSession = new CloudSpatialAnchorSession();
            this.cloudAnchorSession.Configuration.AccountId = ACCOUNT_ID;
            this.cloudAnchorSession.Configuration.AccountKey = ACCOUNT_KEY;
            this.cloudAnchorSession.Error += async (s, e) => await this.SayAsync("Error");

            // for Load
            this.cloudAnchorSession.AnchorLocated += OnAnchorLocated;
            this.cloudAnchorSession.LocateAnchorsCompleted += OnLocateAnchorsCompleted;

            this.cloudAnchorSession.Start();

            this.sessionButtonLabel.text = "Disconnect";
            this.logText.text = "session connected";
            this.msgText.text = "create cube with anchor";
            await this.SayAsync("session connected");
        }
        else
        {
            this.cloudAnchorSession.Dispose();
            this.cloudAnchorSession = null;

            this.sessionButtonLabel.text = "Connect";
            this.logText.text = "session disconnected";
            this.msgText.text = "exit or try again";
            await this.SayAsync("session disconnected");
        }
    }
    async Task OnCreateCubeAsync()
    {
        var gazeHit = GazeManager.Instance.HitObject;
        if (gazeHit == null) { return; }
        if (gazeHit.layer != SpatialMappingManager.Instance.PhysicsLayer) { return; }

        RaycastHit hitInfo;
        if (!Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out hitInfo, 30f))
        {
            return;
        }

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

        cube.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);

        cube.transform.position = hitInfo.point;

        cube.GetComponent<Renderer>().material = this.cubeMaterial;

        this.cubes.Add(cube);

        //sample code
        //var worldAnchor = cube.AddComponent<WorldAnchor>();
        //var cloudSpatialAnchor = new CloudSpatialAnchor(worldAnchor.GetNativeSpatialAnchorPtr(), false);

        //fixed code
        cube.AddARAnchor();
        var cloudSpatialAnchor = new CloudSpatialAnchor();
        cloudSpatialAnchor.LocalAnchor = cube.GetNativeAnchorPointer();
        cloudSpatialAnchor.Expiration = DateTimeOffset.Now.AddDays(1);


        await this.WaitForSessionReadyToCreateAsync();

        await this.cloudAnchorSession.CreateAnchorAsync(cloudSpatialAnchor);

        // for Load
        identifiers.Add(cloudSpatialAnchor.Identifier);

        this.logText.text = "local anchor created and " + Environment.NewLine + "cloud anchor created";
        this.msgText.text = "continue to create or clear local cubes or download cloud cubes";
        await this.SayAsync("cloud anchor created");
    }
    async Task OnClearCubesAsync()
    {
        foreach (var cube in this.cubes)
        {
            Destroy(cube);
        }
        this.cubes.Clear();

        this.logText.text = "local cubes cleared";
        this.msgText.text = "create cube or download cloud cubes";
        await this.SayAsync("local cubes cleared");
    }
    async Task OnReloadCubesAsync()
    {
        if (this.identifiers.Count > 0)
        {
            await this.OnClearCubesAsync();

            this.msgText.text = "cloud cube now loading...";
            await this.SayAsync("cloud cube now loading");
            var watcher = this.cloudAnchorSession.CreateWatcher(
                new AnchorLocateCriteria()
                {
                    Identifiers = identifiers.ToArray(),
                    BypassCache = true,
                    RequestedCategories = AnchorDataCategory.Spatial,
                    Strategy = LocateStrategy.AnyStrategy
                }
            );
        }
    }
    async Task OnClearCloudAnchrosAsync()
    {
        isDeleteProcess = true;

        await OnReloadCubesAsync();
    }
    async Task SayAsync(string text)
    {
        // Ok, this is probably a fairly nasty way of playing a media stream in
        // Unity but it sort of works so I've gone with it for now <img draggable="false" class="emoji" alt="🙂" src="https://s0.wp.com/wp-content/mu-plugins/wpcom-smileys/twemoji/2/svg/1f642.svg">
#if ENABLE_WINMD_SUPPORT
        if (this.synthesizer == null)
        {
            this.synthesizer = new SpeechSynthesizer();
        }
        using (var stream = await this.synthesizer.SynthesizeTextToStreamAsync(text))
        {
            using (var player = new MediaPlayer())
            {
                var taskCompletionSource = new TaskCompletionSource<bool>();
 
                player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
 
                player.MediaEnded += (s, e) =>
                {
                    taskCompletionSource.SetResult(true);
                };
                player.Play();
                await taskCompletionSource.Task;
            }
        }
 
#endif // ENABLE_WINMD_SUPPORT
    }

    async Task WaitForSessionReadyToCreateAsync()
    {
        while (true)
        {
            var status = await this.cloudAnchorSession.GetSessionStatusAsync();

            if (status.ReadyForCreateProgress >= 1.0f)
            {
                break;
            }
            await Task.Delay(250);
        }
    }
    private void OnAnchorLocated(object sender, AnchorLocatedEventArgs args)
    {
        UnityEngine.WSA.Application.InvokeOnAppThread(
            async () =>
            {
                if (isDeleteProcess)
                {
                    await cloudAnchorSession.DeleteAnchorAsync(args.Anchor);
                }
                else
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

                    cube.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);

                    cube.GetComponent<Renderer>().material = this.relocalizedCubeMaterial;

                    var worldAnchor = cube.AddComponent<WorldAnchor>();

                    worldAnchor.SetNativeSpatialAnchorPtr(args.Anchor.LocalAnchor);

                    cube.name = args.Identifier;

                    cubes.Add(cube);
                }
            },
            false
        );
    }
    private async void OnLocateAnchorsCompleted(object sender, LocateAnchorsCompletedEventArgs args)
    {
        if (isDeleteProcess)
        {
            UnityEngine.WSA.Application.InvokeOnAppThread(
                () =>
                {
                    this.logText.text = "cloud cube delete completed";
                    this.msgText.text = "exit or try again";
                },
                false);
            await this.SayAsync("Anchor location deleted");
        }
        else
        {
            UnityEngine.WSA.Application.InvokeOnAppThread(
                () =>
                {
                    this.logText.text = "cloud cube load completed";
                    this.msgText.text = "Anchor location completed";
                },
                false);
            await this.SayAsync("Anchor location completed");
        }
        args.Watcher.Stop();

        if(isDeleteProcess)
        {
            identifiers.Clear();

            isDeleteProcess = false;
        }
    }

    private void OnDestroy()
    {
        this.cloudAnchorSession.Dispose();
        this.cloudAnchorSession = null;
    }

    public async void OnInputClicked(InputClickedEventData eventData)
    {
        if (cloudAnchorSession != null)
        {
            await OnCreateCubeAsync();
        }
    }
    public async void OnSessionClick()
    {
        await OnSessionAsync();
    }
    public async void OnLocalClearClick()
    {
        await OnClearCubesAsync();
    }
    public async void OnReloadClick()
    {
        await OnReloadCubesAsync();
    }
    public async void OnCloudClearClick()
    {
        if (cloudAnchorSession != null)
        {
            await OnClearCloudAnchrosAsync();
        }
    }
}