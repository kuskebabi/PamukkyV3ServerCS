using System.Security;
using Newtonsoft.Json;

namespace PamukkyV3;

public static class ConnectionManager
{
    /// <summary>
    /// What is this application? a server or a client?
    /// </summary>
    public const ThisType thisType = ThisType.server;

    public enum ThisType
    {
        server,
        client
    }

    /// <summary>
    /// Just a simple wrapper of Federation.GetHttpClient, I made this so maybe it can be directly copied to a client without much hassle.
    /// </summary>
    /// <returns></returns>
    public static HttpClient GetHttpClient()
    {
        return Federation.GetHttpClient();
    }

    /// <summary>
    /// Public server information
    /// </summary>
    public class ServerInfo
    {
        public bool isPamukky = false;
        public int pamukkyType = -1;
        public int version = -1;
        public string publicName = "";

        /// <summary>
        /// Check if this remote server is compatiable 
        /// </summary>
        /// <returns></returns>
        public bool isCompatiable()
        {
            // Basic stuff
            if (!isPamukky) return false;
            if (pamukkyType != 3) return false;
            if (version < 0) return false;

            // Add other incompatiable versions here.

            if (thisType == ThisType.server)
            {

            }
            else if (thisType == ThisType.client)
            {

            }

            return true;
        }
    }

    /// <summary>
    /// .well-known format
    /// </summary>
    public class WellKnown
    {
        [JsonProperty("pamukkyv3.server")]
        public string server = "";
    }

    public static async Task<string?> GetStringContentFromUnknown(string url)
    {
        try
        {
            var res = await GetHttpClient().GetAsync(new Uri(new Uri("https://" + url), "pamukky"));
            return await res.Content.ReadAsStringAsync();
        }
        catch (Exception httpsEx)
        {
            Console.WriteLine("HTTPS connection to " + url + " failled: " + httpsEx.ToString());
        }

        try
        {
            var res = await GetHttpClient().GetAsync(new Uri(new Uri("http://" + url), "pamukky"));
            return await res.Content.ReadAsStringAsync();
        }
        catch (Exception httpsEx)
        {
            Console.WriteLine("HTTP connection to " + url + " failled: " + httpsEx.ToString());
        }

        return null;
    }

    /// <summary>
    /// Checks if the server is compatiable with this one.
    /// </summary>
    /// <param name="url"></param>
    /// <returns>null on connection error/non-pamukky server. true on compatiable server. false on incompatiable server.</returns>
    public static async Task<bool?> IsServerCompatiable(string url)
    {
        try
        {
            var res = await GetHttpClient().GetAsync(new Uri(new Uri(url), "pamukky"));
            if (res.StatusCode == System.Net.HttpStatusCode.OK) // One server could return 404 for example, we don't want those. they are not a Pamukky server.
            {
                string resbody = await res.Content.ReadAsStringAsync();
                Console.WriteLine(resbody);
                var serverInfo = JsonConvert.DeserializeObject<ServerInfo>(resbody);

                if (serverInfo != null && serverInfo.isCompatiable()) return true;

                Console.WriteLine("Server " + url + " is not compatiable!");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Connection to " + url + " failled: " + ex.ToString());
        }

        // Return null when connection error.
        return null;
    }

    /// <summary>
    /// Finds if server is http or https from a url that doesn't start with those.
    /// </summary>
    /// <param name="url">URL of the Pamukky server</param>
    /// <returns></returns>
    public static async Task<string?> FindHttpsOrHttp(string url)
    {
        var https = await IsServerCompatiable("https://" + url);
        if (https != null)
        {
            if (https == true) return "https";
            Console.WriteLine("Server " + url + " is not compatiable!");
            return null;
        }

        var http = await IsServerCompatiable("http://" + url);
        if (http != null)
        {
            if (http == true) return "http";
            Console.WriteLine("Server " + url + " is not compatiable!");
            return null;
        }

        return null;
    }

    /// <summary>
    /// Finds where the server lives at.
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    /// <exception cref="SecurityException"></exception>
    public static async Task<string?> FindActualServerURL(string url)
    {
        if (!url.EndsWith("/")) url += "/";
        // Try when direct url is given
        if (url.StartsWith("https://") || url.StartsWith("http://")) return url;

        // See .well-known
        string? res = await GetStringContentFromUnknown(url + ".well-known/pamukky/v3");
        if (res != null)
        {
            try
            {
                var wellknown = JsonConvert.DeserializeObject<WellKnown>(res);
                if (wellknown != null && url.StartsWith("http"))
                {
                    if (!wellknown.server.EndsWith("/")) wellknown.server += "/";

                    // Check if server is tricking
                    Uri serveruri = new Uri(wellknown.server);
                    if (serveruri.IsLoopback) throw new SecurityException("Server is pointing to localhost!");

                    return wellknown.server;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Cannot use well-known. " + ex.ToString());
            }
        }

        // Try https then http directly
        string? type = await FindHttpsOrHttp(url);
        if (type != null)
        {
            return type + "://" + url;
        }

        return null;
    }

    /// <summary>
    /// Creates a fake server name out of server url.
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    public static string CreateFakeServerName(string url)
    {
        string name = url.Replace("https://", "").Replace("http://", "");
        if (name.EndsWith("/"))
        {
            name = name.Remove(name.Length - 1);
        }

        return name;
    }

    public class Server : ServerInfo
    {
        public string serverURL = "";
    }

    /// <summary>
    /// Finds actual server url and checks if it's safe to connect
    /// </summary>
    /// <param name="url"></param>
    /// <returns>Null if can't connect, string(actual server url) if can connect</returns>
    public static async Task<Server?> FindServer(string url)
    {
        string? serverUrl = await FindActualServerURL(url);

        if (serverUrl != null)
        {
            try
            {
                var res = await GetHttpClient().GetAsync(new Uri(new Uri(serverUrl), "pamukky"));
                if (res.StatusCode == System.Net.HttpStatusCode.OK) // One server could return 404 for example, we don't want those. they are not a Pamukky server.
                {
                    string resbody = await res.Content.ReadAsStringAsync();
                    Console.WriteLine(resbody);
                    var serverInfo = JsonConvert.DeserializeObject<ServerInfo>(resbody);

                    if (serverInfo != null && serverInfo.isCompatiable())
                    {
                        string name = CreateFakeServerName(serverUrl);

                        if (serverInfo.publicName.Trim().Length != 0)
                        {
                            if (serverUrl == await FindActualServerURL(serverInfo.publicName))
                            {
                                name = serverInfo.publicName;
                            }
                        }
                        
                        return new()
                        {
                            serverURL = serverUrl,
                            publicName = name
                        };
                    }

                    Console.WriteLine("Server " + url + " is not compatiable!");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Connection to " + url + " failled: " + ex.ToString());
            }
        }

        return null;
    }
}