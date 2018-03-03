using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Windows.Forms;

namespace DownloadYoutubeVideo
{
	class DownloadUrl
	{
		int bytesToDownload = 1024;
		public int? BytesToDownload { get; private set; }
		static Regex dataRegex = new Regex(@"ytplayer\.config\s*=\s*(\{.+?\});", RegexOptions.Multiline);
		
		private class ExtractionInfo
		{
			public bool RequiresDecryption { get; set; }

			public Uri Uri { get; set; }
		}

		private string GetStreamMap(JObject json)
		{
			JToken streamMap = json["args"]["url_encoded_fmt_stream_map"];

			string streamMapString = streamMap == null ? null : streamMap.ToString();

			if (streamMapString == null || streamMapString.Contains("been+removed"))
			{
				//throw new VideoNotAvailableException("Video is removed or has an age restriction.");
			}

			return streamMapString;
		}

		private string GetAdaptiveStreamMap(JObject json)
		{
			JToken streamMap = json["args"]["adaptive_fmts"];

			// bugfix: adaptive_fmts is missing in some videos, use url_encoded_fmt_stream_map instead
			if (streamMap == null)
			{
				streamMap = json["args"]["url_encoded_fmt_stream_map"];
			}

			return streamMap.ToString();
		}

		public IDictionary<string, string> ParseQueryString(string s)
		{
			// remove anything other than query string from url
			if (s.Contains("?"))
			{
				s = s.Substring(s.IndexOf('?') + 1);
			}

			var dictionary = new Dictionary<string, string>();

			foreach (string vp in Regex.Split(s, "&"))
			{
				string[] strings = Regex.Split(vp, "=");
				dictionary.Add(strings[0], strings.Length == 2 ? System.Web.HttpUtility.UrlDecode(strings[1]) : string.Empty);
			}

			return dictionary;
		}

		private string GetHtml5PlayerVersion(JObject json)
		{
			var regex = new Regex(@"player-(.+?).js");

			string js = json["assets"]["js"].ToString();

			return regex.Match(js).Result("$1");
		}

		private string GetFunctionFromLine(string currentLine)
		{
			Regex matchFunctionReg = new Regex(@"\w+\.(?<functionID>\w+)\("); //lc.ac(b,c) want the ac part.
			Match rgMatch = matchFunctionReg.Match(currentLine);
			string matchedFunction = rgMatch.Groups["functionID"].Value;
			return matchedFunction; //return 'ac'
		}

		private int GetOpIndex(string op)
		{
			string parsed = new Regex(@".(\d+)").Match(op).Result("$1");
			int index = Int32.Parse(parsed);

			return index;
		}

		private string SwapFirstChar(string cipher, int index)
		{
			var builder = new StringBuilder(cipher);
			builder[0] = cipher[index];
			builder[index] = cipher[0];

			return builder.ToString();
		}

		private string ApplyOperation(string cipher, string op)
		{
			switch (op[0])
			{
				case 'r':
					return new string(cipher.ToCharArray().Reverse().ToArray());

				case 'w':
					{
						int index = GetOpIndex(op);
						return SwapFirstChar(cipher, index);
					}

				case 's':
					{
						int index = GetOpIndex(op);
						return cipher.Substring(index);
					}

				default:
					throw new NotImplementedException("Couldn't find cipher operation.");
			}
		}

		private string DecipherWithOperations(string cipher, string operations)
		{
			return operations.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries)
				.Aggregate(cipher, ApplyOperation);
		}

		public string DecipherWithVersion(string cipher, string cipherVersion)
		{
			string jsUrl = string.Format("http://s.ytimg.com/yts/jsbin/player-{0}.js", cipherVersion);

			string js = "";
			using (var client = new WebClient())
			{
				client.Encoding = System.Text.Encoding.UTF8;
				js = client.DownloadString(jsUrl);
			}

			//Find "C" in this: var A = B.sig||C (B.s)
			string functNamePattern = @"\""signature"",\s?([a-zA-Z0-9\$]+)\("; //Regex Formed To Find Word or DollarSign

			var funcName = Regex.Match(js, functNamePattern).Groups[1].Value;

			if (funcName.Contains("$"))
			{
				funcName = "\\" + funcName; //Due To Dollar Sign Introduction, Need To Escape
			}

			string funcPattern = @"(?!h\.)" + @funcName + @"=function\(\w+\)\{.*?\}"; //Escape funcName string
			var funcBody = Regex.Match(js, funcPattern, RegexOptions.Singleline).Value; //Entire sig function
			var lines = funcBody.Split(';'); //Each line in sig function

			string idReverse = "", idSlice = "", idCharSwap = ""; //Hold name for each cipher method
			string functionIdentifier = "";
			string operations = "";

			foreach (var line in lines.Skip(1).Take(lines.Length - 2)) //Matches the funcBody with each cipher method. Only runs till all three are defined.
			{
				if (!string.IsNullOrEmpty(idReverse) && !string.IsNullOrEmpty(idSlice) &&
					!string.IsNullOrEmpty(idCharSwap))
				{
					break; //Break loop if all three cipher methods are defined
				}

				functionIdentifier = GetFunctionFromLine(line);
				string reReverse = string.Format(@"{0}:\bfunction\b\(\w+\)", functionIdentifier); //Regex for reverse (one parameter)
				string reSlice = string.Format(@"{0}:\bfunction\b\([a],b\).(\breturn\b)?.?\w+\.", functionIdentifier); //Regex for slice (return or not)
				string reSwap = string.Format(@"{0}:\bfunction\b\(\w+\,\w\).\bvar\b.\bc=a\b", functionIdentifier); //Regex for the char swap.

				if (Regex.Match(js, reReverse).Success)
				{
					idReverse = functionIdentifier; //If def matched the regex for reverse then the current function is a defined as the reverse
				}

				if (Regex.Match(js, reSlice).Success)
				{
					idSlice = functionIdentifier; //If def matched the regex for slice then the current function is defined as the slice.
				}

				if (Regex.Match(js, reSwap).Success)
				{
					idCharSwap = functionIdentifier; //If def matched the regex for charSwap then the current function is defined as swap.
				}
			}

			foreach (var line in lines.Skip(1).Take(lines.Length - 2))
			{
				Match m;
				functionIdentifier = GetFunctionFromLine(line);

				if ((m = Regex.Match(line, @"\(\w+,(?<index>\d+)\)")).Success && functionIdentifier == idCharSwap)
				{
					operations += "w" + m.Groups["index"].Value + " "; //operation is a swap (w)
				}

				if ((m = Regex.Match(line, @"\(\w+,(?<index>\d+)\)")).Success && functionIdentifier == idSlice)
				{
					operations += "s" + m.Groups["index"].Value + " "; //operation is a slice
				}

				if (functionIdentifier == idReverse) //No regex required for reverse (reverse method has no parameters)
				{
					operations += "r "; //operation is a reverse
				}
			}

			operations = operations.Trim();

			return DecipherWithOperations(cipher, operations);
		}

		private IEnumerable<ExtractionInfo> ExtractDownloadUrls(JObject json)
		{
			string[] splitByUrls = GetStreamMap(json).Split(',');
			string[] adaptiveFmtSplitByUrls = GetAdaptiveStreamMap(json).Split(',');
			splitByUrls = splitByUrls.Concat(adaptiveFmtSplitByUrls).ToArray();

			foreach (string s in splitByUrls)
			{
				IDictionary<string, string> queries = ParseQueryString(s);
				string url;

				bool requiresDecryption = false;

				if (queries.ContainsKey("s") || queries.ContainsKey("sig"))
				{
					requiresDecryption = queries.ContainsKey("s");
					string signature = queries.ContainsKey("s") ? queries["s"] : queries["sig"];

					url = string.Format("{0}&{1}={2}", queries["url"], "signature", signature);

					string fallbackHost = queries.ContainsKey("fallback_host") ? "&fallback_host=" + queries["fallback_host"] : String.Empty;

					url += fallbackHost;
				}

				else
				{
					url = queries["url"];
				}

				url = System.Web.HttpUtility.UrlDecode(url);
				url = System.Web.HttpUtility.UrlDecode(url);

				IDictionary<string, string> parameters = ParseQueryString(url);
				if (!parameters.ContainsKey("ratebypass"))
					url += string.Format("&{0}={1}", "ratebypass", "yes");

				yield return new ExtractionInfo { RequiresDecryption = requiresDecryption, Uri = new Uri(url) };
			}
		}

		private IEnumerable<VideoInfo> GetVideoInfos(IEnumerable<ExtractionInfo> extractionInfos, string videoTitle)
		{
			var downLoadInfos = new List<VideoInfo>();

			foreach (ExtractionInfo extractionInfo in extractionInfos)
			{
				string s = extractionInfo.Uri.Query;

				string itag = ParseQueryString(extractionInfo.Uri.Query)["itag"];

				int formatCode = int.Parse(itag);

				VideoInfo info = VideoInfo.Defaults.SingleOrDefault(videoInfo => videoInfo.FormatCode == formatCode);

				if (info != null)
				{
					info = new VideoInfo(info)
					{
						DownloadUrl = extractionInfo.Uri.ToString(),
						Title = videoTitle,
						RequiresDecryption = extractionInfo.RequiresDecryption
					};
				}

				else
				{
					info = new VideoInfo(formatCode)
					{
						DownloadUrl = extractionInfo.Uri.ToString()
					};
				}

				downLoadInfos.Add(info);
			}

			return downLoadInfos;
		}

		public string ReplaceQueryStringParameter(string currentPageUrl, string paramToReplace, string newValue)
		{
			var query = ParseQueryString(currentPageUrl);

			query[paramToReplace] = newValue;

			var resultQuery = new StringBuilder();
			bool isFirst = true;

			foreach (KeyValuePair<string, string> pair in query)
			{
				if (!isFirst)
				{
					resultQuery.Append("&");
				}

				resultQuery.Append(pair.Key);
				resultQuery.Append("=");
				resultQuery.Append(pair.Value);

				isFirst = false;
			}

			var uriBuilder = new UriBuilder(currentPageUrl)
			{
				Query = resultQuery.ToString()
			};

			return uriBuilder.ToString();
		}

		public void DecryptDownloadUrl(VideoInfo videoInfo)
		{
			IDictionary<string, string> queries = ParseQueryString(videoInfo.DownloadUrl);

			if (queries.ContainsKey("signature"))
			{
				string encryptedSignature = queries["signature"];
				string decrypted = "";

				try
				{
					decrypted = DecipherWithVersion(encryptedSignature, videoInfo.HtmlPlayerVersion);
					//decrypted = GetDecipheredSignature(videoInfo.HtmlPlayerVersion, encryptedSignature);
				}
				catch (Exception ex)
				{
					//throw new YoutubeParseException("Could not decipher signature", ex);
				}

				videoInfo.DownloadUrl = ReplaceQueryStringParameter(videoInfo.DownloadUrl, "signature", decrypted);
				videoInfo.RequiresDecryption = false;
			}
		}

		public IEnumerable<VideoInfo> GetDownloadUrls(string videoUrl, bool decryptSignature)
		{
			string url = "";
			using (var client = new WebClient())
			{
				client.Encoding = System.Text.Encoding.UTF8;
				string pageSource = client.DownloadString(videoUrl);

				try
				{
					string extractedJson = dataRegex.Match(pageSource).Result("$1");
					JObject json = JObject.Parse(extractedJson);
					JToken title = json["args"]["title"];
					string videoTitle = title == null ? String.Empty : title.ToString();

					////
					IEnumerable<ExtractionInfo> downloadUrls = ExtractDownloadUrls(json);
					IEnumerable<VideoInfo> infos = GetVideoInfos(downloadUrls, videoTitle).ToList();

					string htmlPlayerVersion = GetHtml5PlayerVersion(json);

					bool RequiresDecryption = false;
					foreach (VideoInfo info in infos)
					{
						info.HtmlPlayerVersion = htmlPlayerVersion;

						if (decryptSignature && info.RequiresDecryption)
						{
							RequiresDecryption = true;
							DecryptDownloadUrl(info);
						}
					}

					if (decryptSignature)
					{
						if (RequiresDecryption == true)
						{
							return infos;
						}
						else
						{
							IEnumerable<VideoInfo> info = new List<VideoInfo>();
							return info;
						}
					} 
					else
					{
						return infos;
					}
				}
				catch (System.Exception ex)
				{
					return null;
				}
			}
		}

		public string DownloadVideo(IEnumerable<VideoInfo> videoInfos, string filepath, VideoDownloader.DownloadProgressChangedHandler f)
		{
			VideoInfo video = videoInfos
				.First(info => info.videoType == VideoType.Mp4 && info.Resolution == 360);

			var videoDownloader = new VideoDownloader(video, filepath, 1024);

			videoDownloader.DownloadProgressChanged += new VideoDownloader.DownloadProgressChangedHandler(f);
			return videoDownloader.Execute();
		}

		private string RemoveIllegalPathCharacters(string path)
		{
			string regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
			var r = new Regex(string.Format("[{0}]", Regex.Escape(regexSearch)));
			return r.Replace(path, "");
		}
	}
}
