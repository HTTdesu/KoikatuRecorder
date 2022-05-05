using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.IO;
using System.Resources;
using UnityEngine;
using UnityEngine.UI;
using BepInEx;
using BepInEx.Configuration;

namespace KoikatuRecorder
{
    [BepInPlugin("com.httdesu.recorder", "Koikatu! Studio Recorder", "1.2.0")]
    class RecorderUI : BaseUnityPlugin
    {
        private ConfigEntry<KeyboardShortcut> toggleUIConfig;
        private ConfigEntry<KeyboardShortcut> toggleRecordConfig;
        private ConfigEntry<int> threadCountConfig;

        private ConfigEntry<int> widthConfig;
        private ConfigEntry<int> heightConfig;
        private ConfigEntry<int> frameRateConfig;
        private ConfigEntry<string> outputPathConfig;
        private InputField threadObj;
        private Sprite[] threadSprits;
        private int threadSpritsIndex = 0;
        private Toggle limitObj;
        private InputField frameCountObj;
        private Text operateObj;

        private GameObject UIobj;
        private Recorder Recorderobj;
        private Font font;
        private bool shouldShow = false;

        private ResourceManager resManager;
        private Sprite inputSprite;
        private List<Camera> cameraList;
        private int cameraIndex;

        void Awake()
        {
            toggleUIConfig = Config.Bind("General",   // The section under which the option is shown
                                     "ToggleRecorderUI",  // The key of the configuration option in the configuration file
                                     new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl), // The default value
                                     "To show/hide the recorder UI");
            if (toggleUIConfig.Value.MainKey == KeyCode.None)
            {
                toggleUIConfig.Value = new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl);
            }
            toggleRecordConfig = Config.Bind("General", "ToggleRecordStatus", new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl, KeyCode.LeftShift), "To start/stop recording. Just a shortcut.");
            if (toggleRecordConfig.Value.MainKey == KeyCode.None)
            {
                toggleRecordConfig.Value = new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl, KeyCode.LeftShift);
            }
            threadCountConfig = Config.Bind("General", "ThreadCount", Environment.ProcessorCount / 2, "Specify how many threads will be used to encode frames.");
            if (threadCountConfig.Value <= 0)
            {
                threadCountConfig.Value = Environment.ProcessorCount / 2;
            }

            widthConfig = Config.Bind("Output Config", "Width", 1920, "Frame width");
            if (widthConfig.Value <= 0)
            {
                widthConfig.Value = 1920;
            }
            heightConfig = Config.Bind("Output Config", "Height", 1080, "Frame height");
            if (heightConfig.Value <= 0)
            {
                heightConfig.Value = 0;
            }
            frameRateConfig = Config.Bind("Output Config", "FrameRate", 60, "Frame rate, also been called FPS. No limit if set to 0 (May cause problems on physics engine)");
            if (frameRateConfig.Value <= 0)
            {
                frameRateConfig.Value = 60;
            }
            outputPathConfig = Config.Bind("Output Config", "OutputPath", Path.Combine(Paths.GameRootPath, "RecordFrames"), "Directory that you want to save frames to");
            if (outputPathConfig.Value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                Logger.LogWarning(string.Format("{0} is not a valid directory. We'll try to redirect it to the game root path.", outputPathConfig.Value));
                outputPathConfig.Value = Path.Combine(Paths.GameRootPath, "RecordFrames");
            }
            outputPathConfig.Value = Path.GetFullPath(outputPathConfig.Value);

            resManager = new ResourceManager("KoikatuRecorder.g", this.GetType().Assembly);
            inputSprite = GenerateSpriteFromFile("img/inputbackground.png", 32, 32, Vector4.one * 12);
            cameraList = new List<Camera>();

            UIobj = null;
            Recorderobj = null;
        }

        void Update()
        {
            if (toggleUIConfig.Value.IsDown())
            {
                shouldShow = !shouldShow;
                if (UIobj == null)
                {
                    GenerateUI();
                    shouldShow = true;
                }
                UIobj.SetActive(shouldShow);
                Debug.Log("Koikatu Recorder: " + shouldShow);
            }

            if (toggleRecordConfig.Value.IsDown())
            {
                if (Recorderobj == null)
                {
                    RefreshCameraList();
                    PrepareRecorder();
                    Recorderobj.Init();
                    if (operateObj != null)
                    {
                        operateObj.text = "Stop";
                    }
                }
                else
                {
                    Recorderobj.shouldFinish = true;
                    if (operateObj != null)
                    {
                        operateObj.text = "Start";
                    }
                }
            }
        }

        void GenerateUI()
        {
            this.UIobj = new GameObject("KoikatuRecorderUI");
            RectTransform root = UIobj.AddComponent<RectTransform>();
            Canvas canvas = UIobj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            CanvasScaler canvasScaler = UIobj.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
            canvasScaler.referencePixelsPerUnit = 100;
            GraphicRaycaster graphicRaycaster = UIobj.AddComponent<GraphicRaycaster>();

            List<string> fonts = new List<string>(Font.GetOSInstalledFontNames());
            if (fonts.Contains("Arial"))
            {
                this.font = Font.CreateDynamicFontFromOSFont("Arial", 32);
            }
            else if (fonts.Contains("Times New Rome"))
            {
                this.font = Font.CreateDynamicFontFromOSFont("Times New Rome", 32);
            }
            else
            {
                this.font = Font.CreateDynamicFontFromOSFont(fonts[0], 32);
            }

            GameObject panelobj = Add2DComponent("Panel", root, new Vector3(0f, 0f, 0f), new Vector2(480f, 360f));
            Image img = panelobj.AddComponent<Image>();
            img.sprite = GenerateSpriteFromFile("img/panelbg.png", 480, 360, Vector4.zero);
            img.color = new Color(1f, 1f, 1f, 0.5f);

            Text titleobj = AddTextComponent("Title", root, new Vector3(0f, 170f, 0f), new Vector2(480f, 16f), "Koikatu Recorder", 14, TextAnchor.MiddleCenter);

            Text cameraobj = AddTextComponent("CameraLabel", root, new Vector3(-140f, 140f, 0f), new Vector2(120f, 32f), "Camera", 20, TextAnchor.MiddleLeft);
            Text cameraName = AddTextComponent("Camera Name", root, new Vector3(60f, 140f, 0f), new Vector2(120f, 32f), "", 20, TextAnchor.MiddleCenter);
            cameraName.color = new Color(1f, 1f, 210f / 255f, 1f);
            Button prevCamera = AddButtonComponent("Previous Camera", root, new Vector3(-28f, 140f, 0f), new Vector2(24f, 32f), "");
            prevCamera.image.sprite = GenerateSpriteFromFile("img/arrowleft.png", 24, 32, Vector4.zero);
            Button nextCamera = AddButtonComponent("Next Camera", root, new Vector3(148f, 140f, 0f), new Vector2(24f, 32f), "");
            nextCamera.image.sprite = GenerateSpriteFromFile("img/arrowright.png", 24, 32, Vector4.zero);
            Button refresh = AddButtonComponent("Refresh", root, new Vector3(184f, 140f, 0f), new Vector2(32f, 32f), "");
            refresh.image.sprite = GenerateSpriteFromFile("img/refresh.png", 32, 32, Vector4.zero);
            RefreshCameraList();
            cameraName.text = cameraList[cameraIndex].name;
            prevCamera.onClick.AddListener(delegate ()
            {
                cameraIndex = (cameraIndex + cameraList.Count - 1) % cameraList.Count;
                cameraName.text = cameraList[cameraIndex].name;
            });
            nextCamera.onClick.AddListener(delegate ()
            {
                cameraIndex = (cameraIndex + 1) % cameraList.Count;
                cameraName.text = cameraList[cameraIndex].name;
            });
            refresh.onClick.AddListener(delegate ()
            {
                RefreshCameraList();
                cameraName.text = cameraList[cameraIndex].name;
            });

            Text resolutionobj = AddTextComponent("ResolutionLabel", root, new Vector3(-140f, 100f, 0f), new Vector2(120f, 32f), "Resolution", 20, TextAnchor.MiddleLeft);
            InputField widthobj = AddInputFieldComponent("Width", root, new Vector3(10f, 100f, 0f), new Vector2(100f, 32f), "Width");
            widthobj.contentType = InputField.ContentType.IntegerNumber;
            widthobj.text = widthConfig.Value.ToString();
            Text Xobj = AddTextComponent("MultiplyLabel", root, new Vector3(80f, 100f, 0f), new Vector2(32f, 32f), "x", 20, TextAnchor.MiddleCenter);
            InputField heightobj = AddInputFieldComponent("Height", root, new Vector3(150f, 100f, 0f), new Vector2(100f, 32f), "Height");
            heightobj.contentType = InputField.ContentType.IntegerNumber;
            heightobj.text = heightConfig.Value.ToString();
            widthobj.onEndEdit.AddListener(delegate (string value)
            {
                int foo = 0;
                int.TryParse(value, out foo);
                if (foo <= 0)
                {
                    foo = 1;
                    widthobj.text = "1";
                }
                widthConfig.Value = foo;
            });
            heightobj.onEndEdit.AddListener(delegate (string value)
            {
                int foo = 0;
                int.TryParse(value, out foo);
                if (foo <= 0)
                {
                    foo = 1;
                    heightobj.text = "1";
                }
                heightConfig.Value = foo;
            });

            Text framerateobj = AddTextComponent("FrameRateLabel", root, new Vector3(-140f, 60f, 0f), new Vector2(120f, 32f), "Frame Rate", 20, TextAnchor.MiddleLeft);
            InputField rateobj = AddInputFieldComponent("FrameRate", root, new Vector3(0f, 60f, 0f), new Vector2(80f, 32f), "FPS");
            rateobj.contentType = InputField.ContentType.IntegerNumber;
            rateobj.text = frameRateConfig.Value.ToString();
            rateobj.onEndEdit.AddListener(delegate (string value)
            {
                int foo = 0;
                int.TryParse(value, out foo);
                if (foo < 0)
                {
                    foo = 0;
                    rateobj.text = "0";
                }
                frameRateConfig.Value = foo;
            });

            Text performanceobj = AddTextComponent("PerformanceLabel", root, new Vector3(-140f, 20f, 0f), new Vector2(120f, 32f), "Performance", 20, TextAnchor.MiddleLeft);
            threadObj = AddInputFieldComponent("Thread", root, new Vector3(0f, 20f, 0f), new Vector2(80f, 32f), "Thread");
            threadObj.contentType = InputField.ContentType.IntegerNumber;
            threadObj.text = threadCountConfig.Value.ToString();
            threadObj.onEndEdit.AddListener(delegate (string value)
            {
                int foo = 0;
                int.TryParse(value, out foo);
                if (foo <= 0)
                {
                    foo = 1;
                    threadObj.text = "1";
                }
                if (threadObj.interactable)
                {
                    threadCountConfig.Value = foo;
                }
            });

            Button switchobj = AddButtonComponent("PerformanceButton", root, new Vector3(65, 20f, 0f), new Vector2(20f, 20f), "");
            threadSprits = new Sprite[]
            {
                GenerateSpriteFromFile("img/full.png", 20, 20, Vector4.zero),
                GenerateSpriteFromFile("img/balance.png", 20, 20, Vector4.zero),
                GenerateSpriteFromFile("img/minium.png", 20, 20, Vector4.zero),
                GenerateSpriteFromFile("img/custom.png", 20, 20, Vector4.zero),
            };
            switchobj.image.sprite = threadSprits[0];
            switchobj.image.type = Image.Type.Simple;
            threadObj.text = (Mathf.Max(Environment.ProcessorCount - 2, 1)).ToString();
            threadObj.interactable = false;
            switchobj.onClick.AddListener(delegate ()
            {
                threadSpritsIndex = (threadSpritsIndex + 1) % threadSprits.Length;
                (switchobj.targetGraphic as Image).sprite = threadSprits[threadSpritsIndex];
                switch (threadSpritsIndex)
                {
                    case 0:
                        threadObj.text = (Mathf.Max(Environment.ProcessorCount - 2, 1)).ToString();
                        threadObj.interactable = false;
                        break;
                    case 1:
                        threadObj.text = (Mathf.Max(Environment.ProcessorCount / 2, 1)).ToString();
                        threadObj.interactable = false;
                        break;
                    case 2:
                        threadObj.text = "2";
                        threadObj.interactable = false;
                        break;
                    case 3:
                        threadObj.text = threadCountConfig.Value.ToString();
                        threadObj.interactable = true;
                        break;
                }
            });

            Text framecountobj = AddTextComponent("FrameCountLabel", root, new Vector3(-140f, -20f, 0f), new Vector2(120f, 32f), "Frame Count", 20, TextAnchor.MiddleLeft);
            frameCountObj = AddInputFieldComponent("FrameCount", root, new Vector3(0f, -20f, 0f), new Vector2(80f, 32f), "Frames");
            frameCountObj.contentType = InputField.ContentType.IntegerNumber;
            frameCountObj.text = "256";
            frameCountObj.interactable = true;
            limitObj = AddToggleComponent("FrameCountToogle", root, new Vector3(65f, -22f, 0f), new Vector2(20f, 20f));
            (limitObj.targetGraphic as Image).sprite = GenerateSpriteFromFile("img/togglebg.png", 20, 20, Vector4.zero);
            (limitObj.graphic as Image).sprite = GenerateSpriteFromFile("img/checkmark.png", 20, 20, Vector4.zero);
            limitObj.isOn = true;
            Text toggleexplainobj = AddTextComponent("ToggleLabel", root, new Vector3(185f, -20f, 0f), new Vector2(200f, 32f), "Limit By Frame", 20, TextAnchor.MiddleLeft);
            frameCountObj.onEndEdit.AddListener(delegate (string value)
            {
                int foo = 0;
                int.TryParse(value, out foo);
                if (foo <= 0)
                {
                    frameCountObj.text = "1";
                }
            });
            limitObj.onValueChanged.AddListener(delegate (bool value)
            {
                frameCountObj.interactable = value;
            });

            Text outputpathobj = AddTextComponent("OutputPathLabel", root, new Vector3(-140f, -60f, 0f), new Vector2(120f, 32f), "Output Path", 20, TextAnchor.MiddleLeft);
            InputField pathobj = AddInputFieldComponent("OutputPath", root, new Vector3(80f, -60f, 0f), new Vector2(240f, 32f), "Output Path");
            pathobj.contentType = InputField.ContentType.Standard;
            pathobj.text = outputPathConfig.Value;
            pathobj.onEndEdit.AddListener(delegate (string value)
            {
                outputPathConfig.Value = value;
            });

            Button operate = AddButtonComponent("Operate", root, new Vector3(-120f, -130f, 0f), new Vector2(120f, 40f), "Start");
            operate.image.sprite = GenerateSpriteFromFile("img/button.png", 120, 10, new Vector4(0f, 7f, 0f, 1f));
            operate.image.type = Image.Type.Sliced;
            operateObj = operate.GetComponentInChildren<Text>();
            Button close = AddButtonComponent("Close", root, new Vector3(120f, -130f, 0f), new Vector2(120f, 40f), "Close");
            close.image.sprite = operate.image.sprite;
            close.image.type = Image.Type.Sliced;
            operate.onClick.AddListener(delegate ()
            {
                if (Recorderobj == null)
                {
                    operateObj.text = "Stop";
                    PrepareRecorder();

                    Recorderobj.Init();
                }
                else
                {
                    Recorderobj.shouldFinish = true;
                    operateObj.text = "Start";
                }
            });
            close.onClick.AddListener(delegate ()
            {
                shouldShow = false;
                UIobj.SetActive(false);
            });
        }

        GameObject Add2DComponent(string name, Transform parent, Vector3 pos, Vector2 size)
        {
            GameObject result = new GameObject(name);
            RectTransform trans = result.AddComponent<RectTransform>();
            trans.SetParent(parent);
            trans.localPosition = pos;
            trans.sizeDelta = size;

            return result;
        }

        Text AddTextComponent(string name, Transform parent, Vector3 pos, Vector2 size, string msg, int textSize, TextAnchor align)
        {
            GameObject obj = Add2DComponent(name, parent, pos, size);
            Text t = obj.AddComponent<Text>();
            t.text = msg;
            t.font = font;
            t.color = Color.white;
            t.alignment = align;
            t.fontSize = textSize;

            return t;
        }

        InputField AddInputFieldComponent(string name, Transform parent, Vector3 pos, Vector2 size, string msg)
        {
            GameObject obj = Add2DComponent(name, parent, pos, size);
            Image bg = obj.AddComponent<Image>();
            bg.sprite = inputSprite;
            bg.type = Image.Type.Sliced;
            RectTransform root = obj.GetComponent<RectTransform>();

            Text placeholder = AddTextComponent("Placeholder", root, Vector3.zero, size - Vector2.right * 30f, msg, 20, TextAnchor.MiddleLeft);
            placeholder.color = new Color(0f, 0f, 0f, 0.5f);
            placeholder.fontStyle = FontStyle.Italic;

            Text text = AddTextComponent("Text", root, Vector3.zero, size - Vector2.right * 30f, "", 20, TextAnchor.MiddleLeft);
            text.color = Color.black;
            text.supportRichText = false;

            InputField input = obj.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine;

            return input;
        }

        Toggle AddToggleComponent(string name, Transform parent, Vector3 pos, Vector2 size)
        {
            GameObject obj = Add2DComponent(name, parent, pos, size);

            GameObject background = Add2DComponent("Background", obj.GetComponent<RectTransform>(), Vector3.zero, size);
            Image bg = background.AddComponent<Image>();

            GameObject checkmark = Add2DComponent("Checkmark", background.GetComponent<RectTransform>(), Vector3.zero, size);
            Image check = checkmark.AddComponent<Image>();

            Toggle toggle = obj.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.ColorTint;
            toggle.targetGraphic = bg;
            toggle.graphic = check;

            return toggle;
        }

        Button AddButtonComponent(string name, Transform parent, Vector3 pos, Vector2 size, string msg)
        {
            GameObject obj = Add2DComponent(name, parent, pos, size);
            Image bg = obj.AddComponent<Image>();
            bg.type = Image.Type.Sliced;
            Button button = obj.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.ColorTint;

            Text text = AddTextComponent("Text", obj.GetComponent<RectTransform>(), Vector3.zero, size, msg, 20, TextAnchor.MiddleCenter);
            text.color = Color.white;

            return button;
        }

        Texture2D LoadTexture(byte[] img, int width, int height)
        {
            Texture2D tex = new Texture2D(width, height);
            tex.LoadImage(img);

            return tex;
        }

        Sprite GenerateSprite(byte[] img, int width, int height, Vector4 border)
        {
            Texture2D tex = LoadTexture(img, width, height);
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), Vector2.one * 0.5f, 100, 1, SpriteMeshType.FullRect, border);
        }

        Sprite GenerateSpriteFromFile(string name, int width, int height, Vector4 border)
        {
            UnmanagedMemoryStream readStream = resManager.GetStream(name);
            byte[] rawImg = new byte[readStream.Length];
            readStream.Read(rawImg, 0, (int)readStream.Length);
            readStream.Close();

            return GenerateSprite(rawImg, width, height, border);
        }

        void RefreshCameraList()
        {
            cameraList.Clear();
            cameraList.AddRange(Resources.FindObjectsOfTypeAll<Camera>());
            for (int i = 0; i < cameraList.Count; i++)
            {
                if (cameraList[i].name == "Main Camera")
                {
                    cameraIndex = i;
                    return;
                }
            }

            cameraIndex = 0;
        }

        void PrepareRecorder()
        {
            Recorderobj = cameraList[cameraIndex].gameObject.AddComponent<Recorder>();
            Recorderobj.targetCamera = cameraList[cameraIndex];
            Recorderobj.outputWidth = widthConfig.Value;
            Recorderobj.outputHeight = heightConfig.Value;
            Recorderobj.frameRate = frameRateConfig.Value;
            Recorderobj.threadCount = threadCountConfig.Value;
            Recorderobj.SavePath = outputPathConfig.Value;

            if (UIobj != null)
            {
                if (limitObj.isOn)
                {
                    int f;
                    int.TryParse(frameCountObj.text, out f);
                    Recorderobj.frameCount = f;
                    Recorderobj.recall = delegate ()
                    {
                        if (operateObj != null)
                        {
                            operateObj.text = "Start";
                        }
                    };
                }
            }
        }
    }
}