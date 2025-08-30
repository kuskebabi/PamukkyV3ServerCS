using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace PamukkyV3;

internal class Pamukky
{
    //  ---- Constants for stuff ----
    // Pamuk (user 0) profile, as string...
    public const string pamukProfile = "{\"name\":\"Pamuk\",\"picture\":\"\",\"description\":\"Birb!!!\"}"; //Direct reply for clients, no need to make class and make it json as it's always the same.

    // ---- Caching ----
    public static ConcurrentDictionary<string, LoginCredential> loginCreds = new();

    /// <summary>
    /// Gets login credentials from token or if preventBypass is false from email too.
    /// </summary>
    /// <param name="token">Token of the login or email (if preventBypass is true)</param>
    /// <param name="preventBypass"></param>
    /// <returns>loginCred if successful, null if "bypassed" or failled.</returns>
    public static async Task<LoginCredential?> GetLoginCred(string token, bool preventBypass = true) {
        //!preventbypass is when you wanna use the token to get other info
        if (token.Contains("@") && preventBypass) { //bypassing
            return null;
        }
        if (loginCreds.ContainsKey(token)) {
            return loginCreds[token];
        }else {
            if (File.Exists("data/auth/" + token)) {
                LoginCredential? up = JsonConvert.DeserializeObject<LoginCredential>(await File.ReadAllTextAsync("data/auth/" + token));
                if (up != null) {
                    loginCreds[token] = up;
                    return up;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Gets user ID from token.
    /// </summary>
    /// <param name="token"></param>
    /// <param name="preventBypass"></param>
    /// <returns></returns>
    public static async Task<string?> GetUIDFromToken(string token, bool preventBypass = true)
    {
        LoginCredential? cred = await GetLoginCred(token, preventBypass);
        if (cred == null)
        {
            return null;
        }
        return cred.userID;
    }

    static void SaveData() {
        Console.WriteLine("Saving Data...");
        Console.WriteLine("Saving Chats...");
        foreach (var c in Chat.chatsCache) { // Save chats in memory to files
            Console.WriteLine("Saving " + c.Key);
            try {
                c.Value.saveChat();
            }catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        }
    }

    static void AutoSaveTick() {
        Task.Delay(300000).ContinueWith((task) => { //save after 5 mins and recall
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

        string HTTPport = "4268";
        string? HTTPSport = null;


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
                    case "port": //Normal HTTP port
                        HTTPport = arg;
                        break;
                    case "federation-url":
                        Federation.thisServerURL = arg;
                        break;
                    case "https-port": // HTTPS port, doesn't quite work. you SHOULD(do NOT make your server in http.) use some forwarder to make http to https.
                        HTTPSport = arg;
                        break;
                }
            }
        }

        //Create save folders
        Directory.CreateDirectory("data");
        Directory.CreateDirectory("data/auth");
        Directory.CreateDirectory("data/chat");
        Directory.CreateDirectory("data/upload");
        Directory.CreateDirectory("data/info");

        // Start a http listener
        new HTTPHandler().Start(HTTPport, HTTPSport);

        AutoSaveTick(); // Start the autosave ticker

        MediaProcesser.StartThread(); // Start a (single) mediaprocesser thread.

        // CLI
        Console.WriteLine("Pamukky  Copyright (C) 2025  HAKANKOKCU");
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
