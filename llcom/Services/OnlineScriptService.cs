using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using llcom.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace llcom.Tools;

/// <summary>
/// 在线脚本服务。
/// 官方源：通过 GitHub GraphQL API 拉取仓库讨论区（Discussions）中的脚本分享帖，
/// 解析为 OnlineScript 列表。Token 从 llcom.papapoi.com 获取（保持原实现）。
/// 本项目源：读取 Eureka12138github/llcom-y 仓库的 community-scripts/index.json 和脚本文件。
/// </summary>
internal static class OnlineScriptService
{
    private static string GitHubToken = null;

    private const string ProjectRepo = "Eureka12138github/llcom-y";
    private const string ProjectBranch = "master";
    private const string ProjectCommunityPath = "community-scripts/";
    private const string ProjectIndexPath = ProjectCommunityPath + "index.json";

    /// <summary>本项目脚本的读取地址：jsDelivr 优先（国内相对稳定），raw 兜底。</summary>
    private static readonly string[] ProjectRoots =
    {
        "https://cdn.jsdelivr.net/gh/Eureka12138github/llcom-y@master/",
        "https://raw.githubusercontent.com/Eureka12138github/llcom-y/master/",
    };

    private static readonly HttpClient projectHttpClient = CreateProjectHttpClient();

    private static HttpClient CreateProjectHttpClient()
    {
        // .NET Framework 4.6.2 默认可能不启用 TLS 1.2
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch
        {
            // 忽略：系统已启用时可能抛异常
        }
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("llcom-y");
        return client;
    }

    /// <summary>
    /// 按来源获取在线脚本。官方源的实现保持原样。
    /// </summary>
    public static List<OnlineScript> GetOnlineScripts(
        OnlineScriptSource source,
        Action<int, int> callback = null
    )
    {
        if (source == OnlineScriptSource.Project)
            return GetProjectScripts(callback);
        return GetOnlineScripts(callback);
    }

    /// <summary>
    /// 获取在线脚本列表（分页拉取全部）
    /// </summary>
    /// <param name="callback">进度回调（当前页, 总页数）</param>
    /// <returns>脚本列表；获取 Token 失败时返回 null</returns>
    public static List<OnlineScript> GetOnlineScripts(Action<int, int> callback = null)
    {
        if (GitHubToken == null)
        {
            try
            {
                var client = new RestClient("https://llcom.papapoi.com/token.txt");
                var request = new RestRequest();
                request.Timeout = 10000;
                var response = client.Get(request);
                GitHubToken = response.Content;
            }
            catch
            {
                return null;
            }
        }
        //请求函数
        var req = (string after) =>
        {
            var client = new RestClient();
            client.BaseUrl = new Uri("https://api.github.com/graphql");
            var request = new RestRequest(RestSharp.Method.POST);
            request.AddHeader("user-agent", "llcom");
            request.AddHeader("Authorization", $"bearer {GitHubToken}");
            request.AddParameter(
                "application/json",
                "{\r\n  \"query\": \"query {repository(owner: \\\"chenxuuu\\\", name: \\\"llcom\\\") {discussions(categoryId:\\\"DIC_kwDOCtNzks4CSz35\\\","
                    + (after == null ? "" : $"after: \"{after}\"")
                    + "first: 100"
                    + ") {totalCount,pageInfo {startCursor,endCursor,hasNextPage,hasPreviousPage},nodes {body,url}}}}\"\r\n}",
                ParameterType.RequestBody
            );
            var response = client.Execute(request);
            var j = JsonConvert.DeserializeObject<JObject>(response.Content);
            var bodys =
                from i in j["data"]["repository"]["discussions"]["nodes"]
                select ((string)i["body"], (string)i["url"]);
            return (
                bodys.ToList(),
                (int)j["data"]["repository"]["discussions"]["totalCount"],
                (string)j["data"]["repository"]["discussions"]["pageInfo"]["endCursor"],
                (bool)j["data"]["repository"]["discussions"]["pageInfo"]["hasNextPage"]
            );
        };

        string lastPage = null;
        var scripts = new List<OnlineScript>();
        var pages = 0;
        while (true)
        {
            var (data, total, endCursor, hasNextPage) = req(lastPage);
            foreach (var (s, u) in data)
            {
                try
                {
                    var n = new OnlineScript(s);
                    n.Url = u;
                    scripts.Add(n);
                }
                catch (Exception ex)
                {
                    Tools.Logger.Swallowed("parse online script", ex);
                }
            }
            callback?.Invoke(pages, total / 100); //回调，上报进度
            if (!hasNextPage)
                break;
            pages++;
            lastPage = endCursor;
        }
        return scripts;
    }

    /// <summary>
    /// 读取本项目 community-scripts/index.json 及其中的脚本文件。
    /// </summary>
    private static List<OnlineScript> GetProjectScripts(Action<int, int> callback)
    {
        var indexJson = GetProjectText(ProjectIndexPath);
        var root = JObject.Parse(indexJson);
        var items = root["scripts"] as JArray;
        if (items == null)
            return new List<OnlineScript>();

        var entries = new List<ProjectScriptEntry>();
        foreach (var item in items)
        {
            if (item == null)
                continue;

            var name = (string)item["name"];
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var file = (string)item["file"];
            var inline = (string)item["script"];
            if (string.IsNullOrWhiteSpace(file) && string.IsNullOrWhiteSpace(inline))
                continue;

            var version = 1;
            int.TryParse(item["version"]?.ToString(), out version);
            if (version <= 0)
                version = 1;

            var script = new OnlineScript
            {
                Author = (string)item["author"] ?? "",
                Name = name,
                Description = (string)item["description"] ?? "",
                Version = version,
                Note = (string)item["note"] ?? "",
                Type = NormalizeProjectType((string)item["type"]),
                Script = inline ?? "",
            };

            var url = (string)item["url"];
            if (string.IsNullOrWhiteSpace(url))
            {
                if (!string.IsNullOrWhiteSpace(file) && !IsAbsoluteUrl(file))
                    url =
                        $"https://github.com/{ProjectRepo}/blob/{ProjectBranch}/{ProjectCommunityPath}{file}";
                else
                    url = $"https://github.com/{ProjectRepo}/tree/{ProjectBranch}/{ProjectCommunityPath}";
            }
            script.Url = url;

            entries.Add(new ProjectScriptEntry { Script = script, File = file });
        }

        var total = entries.Count;
        callback?.Invoke(0, total);
        if (total == 0)
            return new List<OnlineScript>();

        var results = new OnlineScript[total];
        var done = 0;
        Parallel.For(
            0,
            total,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            index =>
            {
                var entry = entries[index];
                try
                {
                    var code = entry.Script.Script;
                    if (string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(entry.File))
                    {
                        var path = IsAbsoluteUrl(entry.File)
                            ? entry.File
                            : ProjectCommunityPath + entry.File;
                        code = GetProjectText(path);
                    }

                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        entry.Script.Script = code;
                        results[index] = entry.Script;
                    }
                }
                catch (Exception ex)
                {
                    Tools.Logger.Swallowed("project online script", ex);
                }

                var finished = Interlocked.Increment(ref done);
                callback?.Invoke(finished, total);
            }
        );

        var loaded = results.Where(script => script != null).ToList();
        if (loaded.Count == 0 && total > 0)
            throw new Exception("本项目社区脚本文件全部获取失败");
        return loaded;
    }

    /// <summary>
    /// 读取本项目仓库中的文本（相对路径会依次尝试 jsDelivr / raw）。
    /// </summary>
    private static string GetProjectText(string pathOrUrl)
    {
        Exception lastError = null;

        if (IsAbsoluteUrl(pathOrUrl))
        {
            try
            {
                return projectHttpClient.GetStringAsync(pathOrUrl).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        else
        {
            // jsDelivr / raw 同时请求，谁先成功用谁：国内网络对两者的可达性不稳定，
            // 串行尝试会让不可达的那个源把加载时间拖长。
            var pending = new List<Task<string>>();
            foreach (var root in ProjectRoots)
            {
                try
                {
                    var url = new Uri(new Uri(root), pathOrUrl);
                    var task = projectHttpClient.GetStringAsync(url);
                    //观察失败任务的异常，避免未观察异常
                    task.ContinueWith(
                        t => { _ = t.Exception; },
                        TaskContinuationOptions.OnlyOnFaulted
                    );
                    pending.Add(task);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            while (pending.Count > 0)
            {
                var done = Task.WhenAny(pending).GetAwaiter().GetResult();
                pending.Remove(done);
                try
                {
                    return done.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }
        }

        throw new Exception($"无法获取本项目社区脚本：{pathOrUrl}", lastError);
    }

    private static bool IsAbsoluteUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string NormalizeProjectType(string type)
    {
        switch ((type ?? "").Trim().ToLowerInvariant())
        {
            case "send":
            case "send_convert":
                return "send";
            case "recv":
            case "receive":
            case "recv_convert":
                return "recv";
            default:
                return "run";
        }
    }

    private sealed class ProjectScriptEntry
    {
        public OnlineScript Script = new OnlineScript();
        public string File = "";
    }
}
