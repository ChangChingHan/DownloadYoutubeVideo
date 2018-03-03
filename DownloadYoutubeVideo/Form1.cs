using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Threading;

namespace DownloadYoutubeVideo
{
	public partial class Form1 : Form
	{
		static string videofile = "";
		string jsonpath = @".\\videos.json";
		string listpath = @".\\videos_ids.lst";
		static DownloadUrl download = new DownloadUrl();
		static bool finish = false;
		string download_result = "";
		bool bPause = false;
		int videoindex = 0;

		struct annotations
		{
			public string label;
		}

		struct video_info
		{
			public string subset;
			public string num_frames;
			public string url;
			public string duration;
			public string resolution;
			[JsonProperty("annotations")]
			public List<annotations> array;
		}

		struct video_json
		{
			public video_info info;
		}


		public Form1()
		{
			InitializeComponent();
		}

		public void Update(object sender, DownloadYoutubeVideo.Downloader.ProgressEventArgs args)
		{
			Thread.Sleep(3);
			label1.BeginInvoke(new Action(() =>
			{
				label1.Text = args.ProgressPercentage.ToString();
			}));
		}

		private void Form1_Load(object sender, EventArgs e)
		{
		
		}

		private void ExecuteInForeground(object data)
		{
			finish = false;
			IEnumerable<VideoInfo> videoInfos = data as IEnumerable<VideoInfo>;
			download_result = download.DownloadVideo(videoInfos, videofile, Update);
			finish = true;
		}

		private void PrintMessage(string msg)
		{
			richTextBox1.BeginInvoke(new Action(() =>
			{
				richTextBox1.AppendText(msg + "\n");
			}));
		}

		private void DownloadVideo(object obj)
		{
			string text = System.IO.File.ReadAllText(jsonpath);

			var jObject = Newtonsoft.Json.Linq.JObject.Parse(text);
			int index = 0;

			foreach (var section in jObject)
			{
				if (index < videoindex)
				{
					index++;
					continue;
				}
				Thread.Sleep(50);
				if (bPause == true)
				{
					videoindex = index;
					break;
				}
				if (section.Key.ToString().Length > 0)
				{
					video_info data = JsonConvert.DeserializeObject<video_info>(section.Value.ToString());
					PrintMessage("URL: " + data.url);
					IEnumerable<VideoInfo> videoInfos = download.GetDownloadUrls(data.url, checkBox.Checked);


					if (videoInfos == null)
					{
						index++;
						PrintMessage("video fail： " + data.url);
						continue;
					}

					if (videoInfos.Count() == 0)
					{
						index++;
						PrintMessage("skip video");
						continue;
					}

					string folder = String.Format(@".\{0}", data.subset);
					if (Directory.Exists(folder) == false)
					{
						Directory.CreateDirectory(folder);
					}

					if (data.subset != "testing")
					{
						folder = String.Format(@".\{0}\{1}", data.subset, data.array[0].label);
						if (Directory.Exists(folder) == false)
						{
							Directory.CreateDirectory(folder);
						}
					}

					videofile = String.Format(@".\{0}\{1}.mp4", folder, section.Key.ToString());
					var th = new Thread(ExecuteInForeground);
					th.Start(videoInfos);
					th.Join();
					Thread.Sleep(100);

					PrintMessage("download result: " + download_result);
				}
				index++;
			}
			PrintMessage("done");
		}

		private void button1_Click(object sender, EventArgs e)
		{
			bPause = false;
			var th = new Thread(DownloadVideo);
			th.Start();
		}

		private void button2_Click(object sender, EventArgs e)
		{
			bPause = true;
			richTextBox1.AppendText("please wait\n");
		}
	}
}
