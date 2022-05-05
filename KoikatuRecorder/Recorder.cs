using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public class Recorder : MonoBehaviour
{
    // 用户配置区
    public Camera targetCamera;
    public int outputWidth;
    public int outputHeight;
    public int frameRate;
    public int threadCount = 1;
    public int frameCount = 0;
    public string SavePath;
    public int counter = 0;
    public float fpsUpdateDeltaTime = 0.125f;                           // FPS数据的更新间隔

    public delegate void Recall();
    public Recall recall = null;
    // 用户配置区结束

    public bool shouldFinish = false;
    private bool ready = false;

    private RenderTexture backup;
    private int originFPS;
    private int originCaptureFrame;

    private RenderTexture buffer;
    private Material blitMat;

    private GameObject root;
    private RawImage screen;
    private Rect alerttPos;
    private GUIStyle alertStyle;
    private Rect infoTextPos;
    private GUIStyle infoTextStyle;
    private float timeCheckpoint;
    private float lastCheckpoint;
    private int fpsFrameCounter;
    private float fps;
    private int viewWidth;
    private int viewHeight;

    public void Init()
    {
        originFPS = Application.targetFrameRate;
        Application.targetFrameRate = frameRate;
        originCaptureFrame = Time.captureFramerate;
        Time.captureFramerate = frameRate;
        SavePath = Path.Combine(SavePath, Application.productName + "_" + System.DateTime.Now.ToString("yyyyMMddHHmmss"));
        if (!Directory.Exists(SavePath))
        {
            Directory.CreateDirectory(SavePath);
        }
        if (recall == null)
        {
            recall = delegate () { };
        }

        buffer = new RenderTexture(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32, 0);
        backup = targetCamera.targetTexture;
        targetCamera.targetTexture = buffer;

        GameObject cameraObj = new GameObject("Monitor");
        cameraObj.transform.position = new Vector3(0f, 10000f, 0f);
        Camera camera = cameraObj.AddComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.Color;
        camera.backgroundColor = Color.black;
        root = cameraObj;

        GameObject canvasObj = new GameObject("Canvas");
        canvasObj.AddComponent<RectTransform>().SetParent(cameraObj.transform);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();

        GameObject imageObj = new GameObject("RawImage");
        RectTransform imageTrans = imageObj.AddComponent<RectTransform>();
        imageTrans.SetParent(canvasObj.GetComponent<RectTransform>());
        imageTrans.localPosition = new Vector3(0f, 0f, 100f);
        imageTrans.localScale = Vector3.one;
        this.screen = imageObj.AddComponent<RawImage>();
        screen.texture = buffer;
        blitMat = new Material(Shader.Find("Unlit/Texture"));
        screen.material = blitMat;

        timeCheckpoint = fpsUpdateDeltaTime;
        lastCheckpoint = 0f;
        fpsFrameCounter = 0;
        fps = 0f;

        alerttPos = new Rect(20f, 20f, 160f, 80f);
        alertStyle = new GUIStyle();
        alertStyle.fontSize = 36;
        alertStyle.normal.textColor = Color.red;

        infoTextPos = new Rect(20f, 80f, 400f, 360f);
        infoTextStyle = new GUIStyle();
        infoTextStyle.fontSize = 24;
        infoTextStyle.normal.textColor = Color.white;

        resize();
        Encoder.GetInstance().Init(threadCount, Encoder.EncodeMode.Custom);
        StartCoroutine(Record());
        targetCamera.enabled = false;

        ready = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (!ready)
        {
            return;
        }

        fpsFrameCounter++;

        if (Time.realtimeSinceStartup >= timeCheckpoint)
        {
            fps = fpsFrameCounter / (Time.realtimeSinceStartup - lastCheckpoint);
            fpsFrameCounter = 0;
            lastCheckpoint = timeCheckpoint;
            timeCheckpoint = Time.realtimeSinceStartup + fpsUpdateDeltaTime;
        }

        if (viewWidth != Screen.width || viewHeight != Screen.height)
        {
            resize();
        }
    }

    private int flag = 0;
    private IEnumerator Record()
    {
        while (true)
        {
            counter++;
            yield return new WaitForEndOfFrame();

            targetCamera.Render();
            RenderTexture rt = RenderTexture.GetTemporary(buffer.descriptor);
            RenderTexture activeBackup = RenderTexture.active;
            Graphics.Blit(buffer, rt, blitMat);
            RenderTexture.active = activeBackup;
            StartCoroutine(WaitForGPUCallBack(rt, Path.Combine(SavePath, string.Format("Record_{0:D6}.png", counter))));
            flag++;

            if (counter >= frameCount || shouldFinish)
            {
                yield return new WaitWhile(() => flag > 0);

                shouldFinish = true;
                Encoder.GetInstance().EndRecord();

                targetCamera.targetTexture = backup;
                Application.targetFrameRate = originFPS;
                Time.captureFramerate = originCaptureFrame;
                targetCamera.enabled = true;
                Destroy(root);
                recall();

                Destroy(this);
                break;
            }
        }
    }

    private IEnumerator WaitForGPUCallBack(RenderTexture source, string path, Encoder.FileFormat format = Encoder.FileFormat.PNG)
    {
        GraphicsFormat graphicsFormat = source.graphicsFormat;
        AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(source, 0, null);
        yield return new WaitUntil(() => request.done);

        RenderTexture.ReleaseTemporary(source);
        if (request.hasError)
        {
            Debug.LogError("Error when saving " + path);
        }
        else
        {
            if (request.layerCount > 1)
            {
                for (int i = 0; i < request.layerCount; i++)
                {
                    string filename = Path.GetFileName(path) + string.Format("_{0:D2}", i + 1);
                    filename += Path.GetExtension(path);
                    filename = Path.Combine(Path.GetDirectoryName(path), filename);
                    Encoder.GetInstance().Encode(request.GetData<byte>().ToArray(), request.width, request.height, filename, graphicsFormat);
                }
            }
            else
            {
                Encoder.GetInstance().Encode(request.GetData<byte>().ToArray(), request.width, request.height, path, graphicsFormat);
            }
        }
        flag--;
    }

    void OnGUI()
    {
        if (counter <= 0)
        {
            return;
        }

        GUI.Label(alerttPos, "● REC", alertStyle);

        string recordStatus = "";
        if (frameCount == 0)
        {
            recordStatus = string.Format("Recording frame {0:D}", counter);
        }
        else
        {
            recordStatus = string.Format("Recording frame {0:D}\nStill {1:D} frames left", counter, frameCount - counter);
        }

        string saving = "";
        if (shouldFinish && flag > 0)
        {
            saving = "\nSaving to disk ......";
        }

        GUI.Label(infoTextPos, string.Format("{0:00.000} FPS\n{2}{3}", fps, counter, recordStatus, saving), infoTextStyle);
    }

    private void resize()
    {
        viewWidth = Screen.width;
        viewHeight = Screen.height;

        RectTransform screenTrans = screen.gameObject.GetComponent<RectTransform>();
        screenTrans.sizeDelta = new Vector2(viewWidth, viewHeight);
    }
}
