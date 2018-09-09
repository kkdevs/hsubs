using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
//using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Harmony;
using MessagePack;
using UnityEngine;
using UnityEngine.UI;
using Logger = BepInEx.Logger;

namespace HSubs
{
    [BepInPlugin(GUID: "org.bepinex.kk.hsubs", Name: "HSubs", Version: "3.0")]
    public class HSubsPlugin : BaseUnityPlugin
    {
#if DEBUG
        void INFO(string s) => BepInEx.Logger.Log(BepInEx.Logging.LogLevel.Info, s);
        void WARN(string s) => BepInEx.Logger.Log(BepInEx.Logging.LogLevel.Warning, s);
        void ERROR(string s) => BepInEx.Logger.Log(BepInEx.Logging.LogLevel.Error, s);
        void SPAM(string s) => BepInEx.Logger.Log(BepInEx.Logging.LogLevel.Debug, s);
#endif
        public const string GUID = "org.bepinex.kk.hsubs";
        public const string SSURL = "https://docs.google.com/spreadsheets/d/";
        public const string SHEET_KEY = "1U0pRyY8e2fIg0E4iBXXRIzpGGDBs5W_g9KfjObS-xI0";
        public const string GID = "677855862";
        public const string RANGE = "A1:C";

        #region ConfigMgr
        public static SavedKeyboardShortcut ReloadTrans { get; set; }
        public static SavedKeyboardShortcut DisplayLang { get; set; }
        public static SavedKeyboardShortcut CBCopy { get; set; }
        public static SavedKeyboardShortcut OpenURL { get; set; }

        [DisplayName("Font")]
        [Category("Caption Text")]
        [Browsable(false)]
        private ConfigWrapper<string> fontName { get; set; }
        [DisplayName("Size")]
        [Category("Caption Text")]
        [Description("Positive values in px, negative values in % of screen size")]
        [AcceptableValueRange(-100, 300, false)]
        private ConfigWrapper<int> fontSize { get; set; }
        [DisplayName("Style")]
        [Category("Caption Text")]
        private ConfigWrapper<FontStyle> fontStyle { get; set; }
        [DisplayName("Alignment")]
        [Category("Caption Text")]
        private ConfigWrapper<TextAnchor> textAlign { get; set; }
        [DisplayName("Text Offset")]
        [Category("Caption Text")]
//        [Description("Padding from bottom of screen")]
        [AcceptableValueRange(0, 300, false)]
        private ConfigWrapper<float> textOffset { get; set; }
        [DisplayName("Outline Thickness")]
        [Category("Caption Text")]
        [AcceptableValueRange(0, 100, false)]
        private ConfigWrapper<int> outlineThickness { get; set; }
        [DisplayName("Display Mode")]
        [Description("Show Captions ENG/JP/None")]
        [AcceptableValueRange(0, 2, false)]
        [Advanced(true)]
        private ConfigWrapper<int> showMode { get; set; }
        [DisplayName("Update on Start")]
        [Advanced(true)]
        private ConfigWrapper<bool> updateOnStart { get; set; }
        [DisplayName("Paste to Clipboard")]
        [Category("Clipboard Options")]
        [Advanced(true)]
        private ConfigWrapper<bool> copyToClipboard { get; set; }
        [DisplayName("Include JP line")]
        [Category("Clipboard Options")]
        [Advanced(true)]
        private ConfigWrapper<bool> copyJPLine { get; set; }

        [DisplayName("Text Color (Partner)")]
        [Category("Text Colors")]
        [Browsable(false)]
        private ConfigWrapper<Color> textColor { get; set; }
        [DisplayName("Outline Color")]
        [Category("Text Colors")]
        [Browsable(false)]
        private ConfigWrapper<Color> outlineColor { get; set; }
        [DisplayName("Text Color (2nd Partner)")]
        [Category("Text Colors")]
        [Browsable(false)]
        public ConfigWrapper<Color> textColor2 { get; set; }
        [DisplayName("Outline Color (2nd)")]
        [Category("Text Colors")]
        [Browsable(false)]
        public ConfigWrapper<Color> outlineColor2 { get; set; }

        #endregion

        private HarmonyInstance harmony;
        public static LoadVoice currentVoice;
        private Outline subOutline;
        private GameObject panel;
        private Coroutine showRoutine;
        private Dictionary<string, string> subtitlesDict = new Dictionary<string, string>();
        private Text subtitleText;
        public string currentJPLine;

        private static Action<LoadVoice> ShowSubtitle { get; set; }

        public HSubsPlugin()
        {
            fontSize = new ConfigWrapper<int>("fontSize", this, -5);
            fontName = new ConfigWrapper<string>("fontName", this, "Arial");
            textAlign = new ConfigWrapper<TextAnchor>("textAlignment", this, TextAnchor.LowerCenter);
            fontStyle = new ConfigWrapper<FontStyle>("fontStyle", this, FontStyle.Bold);
            textOffset = new ConfigWrapper<float>("textOffset", this, 24);
            outlineThickness = new ConfigWrapper<int>("outlineThickness", this, 2);
            showMode = new ConfigWrapper<int>("showMode", this, 0);
            updateOnStart = new ConfigWrapper<bool>("updateOnStart", this, true);
            copyToClipboard = new ConfigWrapper<bool>("copyToClipboard", this, false);
            copyJPLine = new ConfigWrapper<bool>("copyJPLine", this, false);
        }

//        public void Start()
        private bool DoSomeShit()
        {
            ReloadTrans = new SavedKeyboardShortcut("Reload Translations", this,
                new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl));
            DisplayLang = new SavedKeyboardShortcut("Display Eng/JP/None", this,
                new KeyboardShortcut(KeyCode.N, KeyCode.LeftControl));
            CBCopy = new SavedKeyboardShortcut("Copy2Clipboard", this,
                new KeyboardShortcut(KeyCode.None));
            OpenURL = new SavedKeyboardShortcut("OpenURL", this,
                new KeyboardShortcut(KeyCode.None));

            string Col2str(Color c) => ColorUtility.ToHtmlStringRGBA(c);
            Color str2Col(string s) => (Color)((ColorUtility.TryParseHtmlString("#" + s, out Color c)) ? c : Color.clear);

            textColor = new ConfigWrapper<Color>("textColor", this, str2Col, Col2str, Manager.Config.TextData.Font1Color);
            textColor2 = new ConfigWrapper<Color>("textColor2", this, str2Col, Col2str, Manager.Config.TextData.Font2Color);
            outlineColor = new ConfigWrapper<Color>("outlineColor", this, str2Col, Col2str, Color.black);
            outlineColor2 = new ConfigWrapper<Color>("outlineColor2", this, str2Col, Col2str, outlineColor.Value);

            fontSize.SettingChanged += OnSettingChanged;
            fontName.SettingChanged += OnSettingChanged;
            textAlign.SettingChanged += OnSettingChanged;
            fontStyle.SettingChanged += OnSettingChanged;
            textOffset.SettingChanged += OnSettingChanged;
            outlineThickness.SettingChanged += OnSettingChanged;
            textColor.SettingChanged += OnSettingChanged;
            textColor2.SettingChanged += OnSettingChanged;
            outlineColor.SettingChanged += OnSettingChanged;
            outlineColor2.SettingChanged += OnSettingChanged;

            Logger.Log(LogLevel.Debug, "Begin Init");
            InitGUI();
            ShowSubtitle = Show;
            UpdateSubs();

            harmony = HarmonyInstance.Create("org.bepinex.kk.hsubs");
            harmony.PatchAll(typeof(HSubsPlugin));

            return true;
        }

        public bool UpdateSubs()
        {
            string fileCache = Path.Combine(Paths.PluginPath, "hsubs.msgpack");
            if (!updateOnStart.Value && File.Exists(fileCache))
            {
                subtitlesDict = LZ4MessagePackSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(fileCache));
                Logger.Log(LogLevel.Info, subtitlesDict.Count + " lines parsed in cache.");
            }
           else
            {
                Logger.Log(LogLevel.Info, (/*downloading = */"Updating subs..."));
                StartCoroutine(DownloadSubs());
            }
            return true;
        }

        public IEnumerator DownloadSubs()
        {
            string cache = Path.Combine(Paths.PluginPath, "hsubs.msgpack");

            Logger.Log(LogLevel.Info, "Downloading subs from " + SSURL + SHEET_KEY + "export?exportFormat=csv&gid=" + GID + "&range=" + RANGE);
            var dl = new WWW(SSURL+SHEET_KEY+$"/export?exportFormat=csv&gid={GID}&range={RANGE}");
            while (!dl.isDone)
                yield return dl;

            if (dl.error != null)
            {
                Logger.Log(LogLevel.Warning, "Failed to fetch latest subtitles. Going to use cached ones.");
                yield break;
            }

            Logger.Log(LogLevel.Info, $"Downloaded {dl.bytesDownloaded} bytes. Parsing...");
            int cnt = 0;
            foreach (IEnumerable<string> row in ParseCSV(dl.text))
            {
                int idx = 0;
                string sound = null;
                string tl = null;
                foreach (string cell in row)
                {
                    if (idx == 0)
                        sound = cell.ToLower();
                    if (idx == 2)
                        tl = cell;
                    idx++;
                }

                if (sound != null && tl != null && sound.Length < 64)
                {
                    cnt++;
                    subtitlesDict[sound] = tl;
                }
            }

            Logger.Log(LogLevel.Info, $"Done parsing subtitles: {cnt} lines found.");
            if (cnt > 60000)
                File.WriteAllBytes(cache, LZ4MessagePackSerializer.Serialize(subtitlesDict));
            else
                Logger.Log(LogLevel.Warning, "The amount of lines is suspiciously low (defaced sheet?); not caching.");
        }

        protected IEnumerable<IEnumerable<string>> ParseCSV(string source)
        {
            var bodyBuilder = new StringBuilder();

            // here we build rows, one by one
            var row = new List<string>();
            bool inQuote = false;

            for (int i = 0; i < source.Length; i++)
                switch (source[i])
                {
                    case '\r': break;
                    case ',' when !inQuote:
                        row.Add(bodyBuilder.ToString());
                        bodyBuilder.Length = 0;
                        break;
                    case '\n' when !inQuote:
                        if (bodyBuilder.Length != 0 || row.Count != 0)
                        {
                            row.Add(bodyBuilder.ToString());
                            bodyBuilder.Length = 0;
                        }

                        yield return row;
                        row.Clear();
                        break;
                    case '"':
                        if (!inQuote)
                        {
                            inQuote = true;
                        }
                        else
                        {
                            if (i + 1 < source.Length && source[i + 1] == '"')
                            {
                                bodyBuilder.Append('"');
                                i++;
                            }
                            else
                            {
                                inQuote = false;
                            }
                        }

                        break;
                    default:
                        bodyBuilder.Append(source[i]);
                        break;
                }

            if (bodyBuilder.Length > 0)
                row.Add(bodyBuilder.ToString());

            if (row.Count > 0)
                yield return row;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LoadVoice), "Play")]
        public static void CatchVoice(LoadVoice __instance)
        {
            AudioSource audioSource = __instance.audioSource;
            if (audioSource == null || audioSource.clip == null || audioSource.loop)
                return;
            try
            {
                ShowSubtitle?.Invoke(__instance);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, ex);
            }
            currentVoice = __instance;
        }

        private void Show(LoadVoice voice)
        {
            if (showMode.Value == 2 || !subtitlesDict.TryGetValue(voice.audioSource.name, out string sub) || sub.IsNullOrEmpty())
                return;

            sub = voice.voiceTrans.gameObject.GetComponentInParent<ChaControl>().chaFile.parameter.firstname + ": " + sub + "\n";
            Logger.Log(LogLevel.Info, $"{voice.audioSource.name} => {sub}");
            float expire = (voice.audioSource.clip.length / Mathf.Abs(voice.audioSource.pitch)) + voice.fadeTime;
            showRoutine = StartCoroutine(Show_Coroutine(expire, sub));
        }

        private IEnumerator Show_Coroutine(float expire, string subtitle)
        {
            currentLine += subtitle;
            yield return new WaitForSeconds(expire);
            currentLine = currentLine.Replace(subtitle, "");
        }

        private string currentLine
        {
            get
            {
                return subtitleText.text;
            }
            set
            {
                subtitleText.text = value;
            }
        }

        private void InitGUI()
        {
            Font fontFace = (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf");
//            Font fontFace = Resources.Load<Font>("SetoFontCustom.ttf");
            int fsize = (int)(fontSize.Value < 0 ? ((fontSize.Value * Screen.height / -100.0)) : fontSize.Value);

            if (!(gameObject.GetComponentInParent<CanvasRenderer>()))
                gameObject.AddComponent<CanvasRenderer>();

            if (!(panel = panel ?? GameObject.Find("HSubs_Dummy")))
                panel = new GameObject("HSubs_Dummy");
            panel.transform.SetParent(gameObject.transform, false);

            Canvas canvas = panel.GetComponent<Canvas>() ?? panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1;

            RectTransform rect = panel.GetComponent<RectTransform>() ?? panel.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 0f);
            rect.anchorMin = new Vector2(Screen.width * 0.2f, Screen.height * 0.1f);
            rect.anchorMax = new Vector2(Screen.width * 0.8f, Screen.height * 0.9f);
            rect.pivot = new Vector2(0, 0);

            subOutline = panel.GetComponent<Outline>() ?? panel.AddComponent<Outline>();
            subOutline.enabled = true;
            subOutline.effectColor = outlineColor.Value;
            subOutline.effectDistance = new Vector2(outlineThickness.Value, outlineThickness.Value);

            subtitleText = panel.GetComponent<Text>() ?? panel.AddComponent<Text>();
            subtitleText.transform.SetParent(panel.transform, false);
            subtitleText.font = fontFace;
            subtitleText.fontSize = fsize;
            subtitleText.fontStyle = (fontFace.dynamic) ? fontStyle.Value : FontStyle.Normal;
            subtitleText.material = fontFace.material;
            subtitleText.material.color = textColor.Value;
            subtitleText.color = textColor.Value;
            subtitleText.supportRichText = true;
            subtitleText.alignment = textAlign.Value;
            this.subtitleText.rectTransform.anchoredPosition = new Vector2(this.textOffset.Value, this.textOffset.Value);
            subtitleText.lineSpacing = 1;
            subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            subtitleText.verticalOverflow = VerticalWrapMode.Overflow;
            subtitleText.text = subtitleText.text ?? string.Empty;
        }

        void OnSettingChanged(object sender, EventArgs args) { InitGUI(); }
        void OnSettingChanged() { InitGUI(); } // req for Patchwork
        private void OnDisable() { }

        private bool init = false;
        public void Update()
        {
            if (!init)
                init = DoSomeShit();

            if (ReloadTrans.IsPressed())
                StartCoroutine(DownloadSubs());
            if (DisplayLang.IsPressed())
                showMode.Value = (showMode.Value + 1) % 3;
            if (CBCopy.IsPressed())
                GUIUtility.systemCopyBuffer = currentLine + (copyJPLine.Value ? " : " +  currentJPLine : "");
        }

        public void OnDestroy()
        {
            new PatchProcessor(harmony, typeof(HSubsPlugin), new HarmonyMethod(null)).Unpatch(HarmonyPatchType.All, GUID);
        }
    } 
}
/*
    [DisplayName("Use Canvas Renderer")]
    [Advanced(true)]
    private ConfigWrapper<bool> useCanvasRenderer { get; set; }
    useCanvasRenderer = new ConfigWrapper<bool>("canvasRenderer", this, true);

//        public static TextGenerationSettings subGenSet;   // For mesh conversion
            subStyle = new GUIStyle(GUIStyle.none)
            {
                font = fontFace,
                fontSize = fsize,
                fontStyle = fontFace.dynamic ? fontStyle.Value : FontStyle.Normal,
                normal = new GUIStyleState() { textColor = textColor.Value },
                richText = true,
                contentOffset = new Vector2(0f, textOffset.Value),
                alignment = textAlign.Value,
                wordWrap = true
            };
        public void OnGUI()
        {
            if (useCanvasRenderer.Value) return;
                GUI.Label(new Rect(Screen.width * 0.1f, 0, Screen.width * 0.8f, Screen.height * 0.9f), onguiLine, subStyle);
        }

#if DEBUG
/*            subGenSet = new TextGenerationSettings()
            {
                font = fontFace,
                fontSize = fsize,
                fontStyle = (fontFace.dynamic) ? fontStyle.Value : FontStyle.Normal,
                color = textColor.Value,
                richText = true,
                textAnchor = textAlign.Value,
                generationExtents = new Vector2(Screen.width, (textOffset.Value + fsize) * 2.5f),
                lineSpacing = 1,
                scaleFactor = 1,
                pivot = new Vector2(0.5f, 0.5f),
                horizontalOverflow = HorizontalWrapMode.Wrap,
                verticalOverflow = VerticalWrapMode.Overflow,
                generateOutOfBounds = true,
            }; */
/*
 * #region hctest
public string htextest() => (subtitleText.text = "subtitleText").IsNullOrEmpty() ? "false" : "subtitleText";
        public string hcaptest() => (new TextGenerator().PopulateWithErrors("Population", subGenSet, gameObject)) ? "Population" : "false";
        private string TestPanel()
        {
            panel = gameObject;
            //           panel.name = "HSubs";
            if (panel == null) return "no panel";
            if (!(gameObject.GetComponentInParent<CanvasRenderer>() ?? gameObject.AddComponent<CanvasRenderer>())) return "no renderer";
            var rect = panel.GetComponentInParent<RectTransform>() ?? panel.AddComponent<RectTransform>();
            if (!rect) return "no rect";
            rect.sizeDelta = new Vector2(0f, 0f);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0, 0);
            gameObject.GetComponent<Canvas>();
            if (!(canvas = panel.GetComponent<Canvas>() ?? panel.AddComponent<Canvas>())) return "no canvas";
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1;
            if (!(subOutline = panel.GetComponent<Outline>() ?? panel.AddComponent<Outline>())) return "no outline";
            subOutline.effectDistance = new Vector2(outlineThickness.Value, outlineThickness.Value);
            if (!(subtitleText = panel.GetComponent<Text>() ?? panel.AddComponent<Text>())) return "no text";
            subtitleText.text = "true";
            if ((HCaption = new TextGenerator()) == null) return "no generator";
            SPAM("conditions cleared");

            return "true";
        }
        public string Test()
        {
            SPAM(TestPanel());
            return TestPanel();
        }
        public bool DontClickMe()
        {
            Destroy(gameObject, 0.0f);
            return false;
        }
*/
/*
class Captions : UnityEngine.Component
{

}

class Translator
{

}
}*/
