using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Common;
using BepInEx.Logging;
using Harmony;
using MessagePack;
using UnityEngine;
using UnityEngine.UI;
using Logger = BepInEx.Logger;

namespace HSubs
{
	[BepInPlugin("org.bepinex.kk.hsubs", "HSubs", "1.0")]
	public class HSubs : BaseUnityPlugin
	{
		private const string SHEET_KEY = "1U0pRyY8e2fIg0E4iBXXRIzpGGDBs5W_g9KfjObS-xI0";

		private ConfigWrapper<int> fontSize;
		private ConfigWrapper<FontStyle> fontStyle;
		private ConfigWrapper<float> outlineThickness;
		private ConfigWrapper<TextAnchor> textAlignment;
		private ConfigWrapper<float[]> outlineColor;
		private ConfigWrapper<float[]> textColor;
		private ConfigWrapper<float[]> textOffset;
		private ConfigWrapper<bool> oldRenderer;

		private Outline outline;
		private GameObject panel;
		private Coroutine showRoutine;
		private Dictionary<string, string> subtitlesDict = new Dictionary<string, string>();
		private Text subtitleText;

		private static Action<AudioSource> ShowSubtitle { get; set; }

		public void Start()
		{
			float[] StrToArr(string s) => s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(float.Parse).ToArray();
			string ArrToStr(float[] arr) => string.Join(";", arr.Select(f => f.ToString("0.0")).ToArray());

			oldRenderer = new ConfigWrapper<bool>("old-renderer", this, false);
			fontSize = new ConfigWrapper<int>("font-size", this, 20);
			outlineThickness = new ConfigWrapper<float>("outline-thickness", this, 1f);
			textAlignment = new ConfigWrapper<TextAnchor>("text-alignment", this, TextAnchor.UpperCenter);
			fontStyle = new ConfigWrapper<FontStyle>("font-style", this, FontStyle.Bold);
			textOffset = new ConfigWrapper<float[]>("text-offset", this, StrToArr, ArrToStr, new[] { 0.0f, 0.0f });
			outlineColor = new ConfigWrapper<float[]>("outline-color", this, StrToArr, ArrToStr, new[] { 0.0f, 0.0f, 0.0f, 1.0f });
			textColor = new ConfigWrapper<float[]>("text-color", this, StrToArr, ArrToStr, new[] { 1.0f, 1.0f, 1.0f, 1.0f });

			HarmonyInstance.Create("org.bepinex.kk.hsubs").PatchAll(typeof(HSubs));
			InitGUI();
			ShowSubtitle = Show;
			StartCoroutine(DownloadSubs());
		}

		public IEnumerator DownloadSubs()
		{
			string cache = Path.Combine(Utility.PluginsDirectory, "hsubs.msgpack");
			if (File.Exists(cache))
			{
				subtitlesDict = LZ4MessagePackSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(cache));
				Logger.Log(LogLevel.Info, "Found cached hsubs");
			}

			Logger.Log(LogLevel.Info, "Downloading subs from " + SHEET_KEY);
			var dl = new WWW($"https://docs.google.com/spreadsheets/d/{SHEET_KEY}/export?format=csv");
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

		[HarmonyPostfix]
		[HarmonyPatch(typeof(LoadAudioBase), "Play")]
		public static void CatchVoice(LoadAudioBase __instance)
		{
			LoadAudioBase v = __instance;
			AudioSource audioSource = v.audioSource;
			if (audioSource == null || audioSource.clip == null || v.audioSource.loop)
				return;

			ShowSubtitle?.Invoke(audioSource);
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

		private void Show(AudioSource source)
		{
			if (!subtitlesDict.TryGetValue(source.name, out string sub))
				return;

			if (showRoutine != null)
				StopCoroutine(showRoutine);

			Logger.Log(LogLevel.Info, $"{source.name} => {sub}");

			showRoutine = StartCoroutine(Show_Coroutine(source, sub));
		}

		private IEnumerator Show_Coroutine(AudioSource source, string subtitle)
		{
			currentLine = subtitle;
			while (!source.isPlaying)
				yield return new WaitForFixedUpdate();

			while (source.isPlaying)
				yield return new WaitForFixedUpdate();
			currentLine = string.Empty;
		}

        private void InitGUI()
        {
			if (oldRenderer.Value) return;
            panel = new GameObject("Panel");
            DontDestroyOnLoad(panel);

            var canvas = panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 0f);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);

            outline = panel.AddComponent<Outline>();

            subtitleText = panel.AddComponent<Text>();
            subtitleText.transform.SetParent(panel.transform, false);
            subtitleText.transform.localPosition = new Vector3(0f, 0f, 10f);
            var myFont = (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf");
            subtitleText.font = myFont;
            subtitleText.material = myFont.material;
            subtitleText.text = string.Empty;

            outline.enabled = true;
            float thickness = outlineThickness.Value;
            outline.effectDistance = new Vector2(thickness, thickness);

            float[] outlineColorArray = outlineColor.Value;
            float[] textColorArray = textColor.Value;
            float[] offsetArray = textOffset.Value;

            outline.effectColor = new Color(outlineColorArray[0], outlineColorArray[1], outlineColorArray[2], outlineColorArray[3]);

            subtitleText.fontSize = fontSize.Value;
            subtitleText.fontStyle = fontStyle.Value;
            subtitleText.material.color = new Color(textColorArray[0], textColorArray[1], textColorArray[2], textColorArray[3]);
            subtitleText.alignment = TextAnchor.UpperCenter;
            subtitleText.rectTransform.anchoredPosition = new Vector2(offsetArray[0], offsetArray[1]);
        }

		string onguiLine;
		string currentLine
		{
			set
			{
				if (oldRenderer.Value)
					onguiLine = value;
				else
					subtitleText.text = value;
			}
			get
			{
				return onguiLine;
			}
		}

		void OnGUI()
		{
			if (!oldRenderer.Value || onguiLine.IsNullOrEmpty()) return;
			GUIStyle style = new GUIStyle(GUI.skin.button);
			style.wordWrap = true;
			style.fontSize = fontSize.Value;
			GUILayout.BeginArea(new Rect(Screen.width * 0.1f, 0, Screen.width * 0.8f, Screen.height * 0.9f));
			GUILayout.FlexibleSpace();
			GUILayout.Label(onguiLine, style);
			GUILayout.EndArea();
		}

	}
}