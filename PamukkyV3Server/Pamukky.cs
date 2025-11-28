using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace PamukkyV3;

internal class Pamukky
{
    // ---- Config ----
    /// <summary>
    /// Current config of the server
    /// </summary>
    public static ServerConfig config = new();
    /// <summary>
    /// Current terms of service of server (full content)
    /// </summary>
    public static string serverTOS = "No TOS.";

    /// <summary>
    /// Gives public server data of this server.
    /// </summary>
    public class PublicServerData : ConnectionManager.ServerInfo
    {
        public static PublicServerData? instance;
        public long maxFileUploadSize
        {
            get
            {
                return config.maxFileUploadSize;
            }
        }

        public static PublicServerData Get()
        {
            if (instance == null)
            {
                instance = new()
                {
                    isPamukky = true,
                    pamukkyType = 3,
                    version = 0,
                    publicName = config.publicName
                };
            }
            return instance;
        }
    }

    /// <summary>
    /// Server config structure
    /// </summary>
    public class ServerConfig
    {
        /// <summary>
        /// Port for HTTP.
        /// </summary>
        public int httpPort = 4268;
        /// <summary>
        /// Port for HTTPS.
        /// </summary>
        public int? httpsPort = null;
        /// <summary>
        /// File path for server terms of service file.
        /// </summary>
        public string? termsOfServiceFile = null;
        /// <summary>
        /// Public URL of the server that other servers/users will/should use
        /// </summary>
        public string? publicUrl = null;
        /// <summary>
        /// Public name(Short url) of the server that other servers/users will/should use
        /// </summary>
        public string publicName = "";
        /// <summary>
        /// Max file size for uploads in megabytes. Default is 24 megabytes.
        /// </summary>
        public long maxFileUploadSize = 24;
        /// <summary>
        /// Sets interval of auto-save. Default is 300000.
        /// </summary>
        public int autoSaveInterval = 300000;
        /// <summary>
        /// System profile.
        /// </summary>
        public UserProfile systemProfile = new()
        {
            name = "Pamuk",
            bio = "Hello! This is a service account."
        };
    }

    /// <summary>
    /// Gets user ID from session.
    /// </summary>
    /// <param name="token">Token of the session</param>
    /// <returns></returns>
    public static string? GetUIDFromToken(string? token)
    {
        if (token == null) return null;
        var session = UserSession.GetSession(token);
        if (session == null) return null;
        return session.userID;
    }

    /// <summary>
    /// Gets Group or UserProfile from ID.
    /// </summary>
    /// <param name="id"></param>
    /// <returns>Group or UserProfile if exists, null if it doesn't exist.</returns>
    public static async Task<object?> GetTargetFromID(string id)
    {
        UserProfile? up = await UserProfile.Get(id);
        if (up != null)
        {
            return up;
        }
        Group? gp = await Group.Get(id);
        if (gp != null)
        {
            return gp;
        }

        return null;
    }


    /// <summary>
    /// Saves data to disk.
    /// </summary>
    public static void SaveData()
    {
        Console.WriteLine("Saving Data...");
        Console.WriteLine("Saving Known servers...");
        Federation.SaveKnownServersList();
        Console.WriteLine("Saving Tags...");
        PublicTag.Save();
        Console.WriteLine("Saving Chats...");
        foreach (var c in Chat.chatsCache)
        { // Save chats in memory to files
            Console.WriteLine("Saving " + c.Key);
            try
            {
                c.Value.saveChat();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        Console.WriteLine("Saving Online status...");
        foreach (var p in UserProfile.userProfileCache)
        { // Save online status in memory to files
            Console.WriteLine("Saving " + p.Key);
            try
            {
                p.Value.SaveStatus();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }

    static void AutoSaveTick()
    {
        if (config.autoSaveInterval > 0)
            Task.Delay(config.autoSaveInterval).ContinueWith((task) =>
            { //save after 5 mins and recall
                SaveData();
                AutoSaveTick();
            });
    }

    public static bool exit = false;

    static void Main(string[] args)
    {
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore //This is the main reason I was used null. Doesn't quite work ig...
        };

        int HTTPport = 4268;
        int? HTTPSport = null;
        string? configPath = null;

        string argMode = "";

        foreach (string arg in args)
        {
            if (arg.StartsWith("--"))
            {
                argMode = arg.Replace("--", "");
            }
            else
            {
                switch (argMode)
                {
                    case "config": // HTTPS port, doesn't quite work. you SHOULD(do NOT make your server in http.) use some forwarder to make http to https.
                        configPath = arg;
                        break;
                }
            }
        }

        // Load/Set config

        if (File.Exists(configPath))
        {
            config = JsonConvert.DeserializeObject<ServerConfig>(File.ReadAllText(configPath ?? "")) ?? new();
            HTTPport = config.httpPort;
            HTTPSport = config.httpsPort;
            if (config.publicUrl != null) Federation.thisServerURL = config.publicUrl.ToLower();
            if (File.Exists(config.termsOfServiceFile))
            {
                serverTOS = File.ReadAllText(config.termsOfServiceFile);
            }
        }

        config.systemProfile.userID = "0";
        UserProfile.userProfileCache["0"] = config.systemProfile;

        if (config.publicName.Trim().Length == 0)
        {
            config.publicName = ConnectionManager.CreateFakeServerName(Federation.thisServerURL ?? "");
        }

        // Create save folders
        Directory.CreateDirectory("data");
        Directory.CreateDirectory("data/auth");
        Directory.CreateDirectory("data/chat");
        Directory.CreateDirectory("data/upload");
        Directory.CreateDirectory("data/info");

        // Load known servers
        Federation.LoadKnownServersList();

        // Load public tags
        PublicTag.Load();

        // Start a http listener
        new HTTPHandler().Start(HTTPport, HTTPSport);

        AutoSaveTick(); // Start the autosave ticker

        // CLI
        Console.WriteLine("Pamukky  Copyright (C) 2025  Kuskebabi");
        Console.WriteLine();
        Console.WriteLine("This program comes with ABSOLUTELY NO WARRANTY; This is free software, and you are welcome to redistribute it under certain conditions.");
        Console.WriteLine("Type help for help.");

        while (!exit)
        {
            string readline = Console.ReadLine() ?? "";
            switch (readline.ToLower())
            {
                case "exit":
                    exit = true;
                    break;
                case "save":
                    SaveData();
                    break;
                case "help":
                    Console.WriteLine("help   Shows this menu");
                    Console.WriteLine("save   Saves (chat) data.");
                    Console.WriteLine("exit   Saves data and exits Pamukky.");
                    break;
            }
        }
        // After user wants to exit, save "cached" data
        SaveData();
    }
}
