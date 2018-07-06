using BepInEx;
using Harmony;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MessagePack;
using UnityEngine;
using System.Text;

public class HSubs : BaseUnityPlugin
{
	// change this if you want private tl, replace the '1U0pRyY8e2fIg0E4iBXXRIzpGGDBs5W_g9KfjObS-xI0' part with your shareable-link
	// doc-id (must be anyone-can-view at least)
	public static string sheet = "https://docs.google.com/spreadsheets/d/1U0pRyY8e2fIg0E4iBXXRIzpGGDBs5W_g9KfjObS-xI0/export?format=csv";
	GUIStyle substyle;
	public void OnGUI()
	{
		if (substyle == null)
		{
			substyle = new GUIStyle(GUI.skin.button);
			substyle.fontSize = 32;
			substyle.wordWrap = true;
		}

		if (currentLine.IsNullOrEmpty() || Time.realtimeSinceStartup > expires)
		{
			currentLine = null;
			return;
		}
		GUILayout.BeginArea(new Rect(Screen.width * 0.1f, 0, Screen.width * 0.8f, Screen.height * 0.9f));
		GUILayout.FlexibleSpace();
		GUILayout.Label(currentLine, substyle);
		GUILayout.EndArea();
	}

	public void Start()
	{
		// you may want to make something less ugly here
		HarmonyInstance.Create("HSubs").PatchAll(typeof(HSubs));
		UpdateSubs();
	}

	public void UpdateSubs()
	{
		if (sheet != null)
			StartCoroutine(DownloadSubs());
	}

	public static Dictionary<string, string> dict = new Dictionary<string, string>();

	public IEnumerator DownloadSubs()
	{
		var cache = BepInEx.Common.Utility.PluginsDirectory + "/hsubs.msgpack";
		if (File.Exists(cache))
		{
			dict = LZ4MessagePackSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(cache));
			print("Found cached hsubs");
		}

		print("Downloading subs from " + sheet);
		var dl = new WWW(sheet);
		while (!dl.isDone)
			yield return dl;
		print($"Parsing {dl.text.Length} characters");
		int cnt = 0;
		foreach (var row in ParseCSV(dl.text))
		{
			int idx = 0;
			string sound = null;
			string tl = null;
			foreach (var cell in row)
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
				dict[sound] = tl;
			}
		}
		print($"Done. {cnt} lines found.");
		if (cnt > 60000)
			File.WriteAllBytes(cache, LZ4MessagePackSerializer.Serialize(dict));
	}

	public static string currentLine;
	public static float expires;

	[HarmonyPostfix, HarmonyPatch(typeof(LoadAudioBase), "Play")]
	public static void CatchVoice(LoadAudioBase __instance)
	{
		var v = __instance;
		var audioSource = v.audioSource;
		if (audioSource == null || audioSource.clip == null || v.audioSource.loop)
			return;
		expires = Time.realtimeSinceStartup + audioSource.clip.length - audioSource.time;
		if (!dict.TryGetValue(v.assetName.ToLower(), out currentLine))
			currentLine = "";
		print($"[HSUBS] [{v.assetName}] => '{currentLine}'");
	}
	public bool hasUI;

	protected IEnumerable<IEnumerable<string>> ParseCSV(string source)
	{
		StringBuilder bodyBuilder = new StringBuilder();

		// here we build rows, one by one
		int i = 0;
		var row = new List<string>();
		var limit = source.Length;
		bool inQuote = false;

		while (i < limit)
		{
			if (source[i] == '\r')
			{
				//( ͠° ͜ʖ °)
			}
			else if (source[i] == ',' && !inQuote)
			{
				row.Add(bodyBuilder.ToString());
				bodyBuilder.Length = 0; //.NET 2.0 ghetto clear
			}
			else if (source[i] == '\n' && !inQuote)
			{
				if (bodyBuilder.Length != 0 || row.Count != 0)
				{
					row.Add(bodyBuilder.ToString());
					bodyBuilder.Length = 0; //.NET 2.0 ghetto clear
				}

				yield return row;
				row.Clear();
			}
			else if (source[i] == '"')
			{
				if (!inQuote)
					inQuote = true;
				else
				{
					if (i + 1 < limit
						&& source[i + 1] == '"')
					{
						bodyBuilder.Append('"');
						i++;
					}
					else
						inQuote = false;
				}
			}
			else
			{
				bodyBuilder.Append(source[i]);
			}

			i++;
		}

		if (bodyBuilder.Length > 0)
			row.Add(bodyBuilder.ToString());

		if (row.Count > 0)
			yield return row;
	}
}
