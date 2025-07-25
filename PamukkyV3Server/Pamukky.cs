using System;
using System.Collections.Generic;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using ImageMagick;

using System.Net;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

using Konscious.Security.Cryptography;
using System.Web;
using System.Collections.Concurrent;

namespace PamukkyV3;

internal class Pamukky
{
    //  ---- Constants for stuff ----
    // Thumbnail generation.
    public const int thumbSize = 256;
    public const int thumbQuality = 75;
    // Pamuk (user 0) profile, as string...
    public const string pamukProfile = "{\"name\":\"Pamuk\",\"picture\":\"\",\"description\":\"Birb!!!\"}"; //Direct reply for clients, no need to make class and make it json as it's always the same.
    // Date serialization, do not change unless you apply it to clients too.
    public const string datetimeFormat = "MM dd yyyy, HH:mm zzz";

    // ---- Caching ----
    static ConcurrentDictionary<string, loginCred> loginCreds = new();

    //                          token                         updater names and value of it.

    // ---- Http ----
    static HttpListener _httpListener = new HttpListener();
    
    /// <summary>
    /// Converts DateTime to Pamukky-like date string.
    /// </summary>
    /// <param name="date"></param>
    /// <returns></returns>
    public static string dateToString(DateTime date) {
        return date.ToString(datetimeFormat);
    }

    /// <summary>
    /// Uploaded file information.
    /// </summary>
    public class FileUpload
    {
        public string sender = "";
        public string actualName = "";
        public int size = 0;
        public string contentType = "";
    }

    /// <summary>
    /// What should server reply for /upload
    /// </summary>
    class FileUploadResponse
    {
        public string url = ""; //success
        public string status;
        public FileUploadResponse(string stat, string furl)
        { //for easier creation
            status = stat;
            url = furl;
        }
    }

    /// <summary>
    /// Server response type for errors and actions that doesn't have any return
    /// </summary>
    class ServerResponse {
        public string status; //done
        public string? description;
        public string? code;

        public ServerResponse(string stat, string? scode = null, string? descript = null) { //for easier creation
            status = stat;
            description = descript;
            code = scode;
        }
    }

    /// <summary>
    /// For both signup and login calls.
    /// </summary>
    class LoginResponse
    {
        public string token;
        public string uid;
        //public userProfile userinfo;

        public LoginResponse(string utoken, string id)
        {
            token = utoken;
            uid = id;
            //userinfo = profile;
        }
    }
    
    /// <summary>
    /// Return for doAction function.
    /// </summary>
    class ActionReturn
    {
        public string res = "";
        public int statuscode = 200;
    }

    /// <summary>
    /// Hashes a password
    /// </summary>
    /// <param name="pass">Password</param>
    /// <param name="uid">User ID that will be used as AssociatedData.</param>
    /// <returns></returns>
    static string hashPassword(string pass, string uid)
    {
        try
        {
            using (Argon2id argon2 = new Argon2id(Encoding.UTF8.GetBytes(pass)))
            {
                try
                {
                    argon2.Iterations = 5;
                    argon2.MemorySize = 7;
                    argon2.DegreeOfParallelism = 1;
                    argon2.AssociatedData = Encoding.UTF8.GetBytes(uid);
                    return Encoding.UTF8.GetString(argon2.GetBytes(128));
                }
                catch
                {
                    return ""; //In case account is older than when the algorithm was added, can also be used as a test account. Basically passwordless
                }
                finally
                {
                    argon2.Dispose(); // Memory eta bomba
                }
            }
        }
        catch { return ""; }
    }

    /// <summary>
    /// Gets login credentials from token or if preventBypass is false from email too.
    /// </summary>
    /// <param name="token">Token of the login or email (if preventBypass is true)</param>
    /// <param name="preventBypass"></param>
    /// <returns>loginCred if successful, null if "bypassed" or failled.</returns>
    static async Task<loginCred?> GetLoginCred(string token, bool preventBypass = true) {
        //!preventbypass is when you wanna use the token to get other info
        if (token.Contains("@") && preventBypass) { //bypassing
            return null;
        }
        if (loginCreds.ContainsKey(token)) {
            return loginCreds[token];
        }else {
            if (File.Exists("data/auth/" + token)) {
                loginCred? up = JsonConvert.DeserializeObject<loginCred>(await File.ReadAllTextAsync("data/auth/" + token));
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
        loginCred? cred = await GetLoginCred(token, preventBypass);
        if (cred == null)
        {
            return null;
        }
        return cred.userID;
    }

    /// <summary>
    /// Does a user action.
    /// </summary>
    /// <param name="action">Type of the request</param>
    /// <param name="body">Body of the request.</param>
    /// <returns>Return of the action.</returns>
    static async Task<ActionReturn> doAction(string action, string body)
    {
        string res = "";
        int statuscode = 200;
        if (action == "signup")
        {
            var a = JsonConvert.DeserializeObject<loginCred>(body);
            if (a != null)
            {
                a.EMail = a.EMail.Trim();
                if (!File.Exists("data/auth/" + a.EMail))
                {
                    // Check the email format. TODO: maybe improve
                    if (a.EMail != "" && a.EMail.Contains("@") && a.EMail.Contains(".") && !a.EMail.Contains(" "))
                    {
                        // IDK, why limit password characters? I mean also just get creative and dont make your password "      "
                        if (a.Password.Trim() != "" && a.Password.Length >= 6)
                        {
                            string uid = "";
                            do
                            {
                                uid = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                            }
                            while (Directory.Exists("data/info/" + uid));

                            a.Password = hashPassword(a.Password, uid);

                            string token = "";
                            do
                            {
                                token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                            }
                            while (loginCreds.ContainsKey(token));

                            a.userID = uid;
                            //a.token = token;

                            //Console.WriteLine(a.Password);
                            UserProfile up = new() { name = a.EMail.Split("@")[0].Split(".")[0] };
                            Directory.CreateDirectory("data/info/" + uid);
                            string astr = JsonConvert.SerializeObject(a);
                            //File.WriteAllText("data/auth/" + token, astr);
                            loginCreds[token] = a;
                            File.WriteAllText("data/auth/" + a.EMail, astr);
                            UserProfile.Create(uid, up);
                            UserChatsList? chats = await UserChatsList.Get(uid); //get new user's chats list
                            if (chats != null)
                            {
                                chatItem savedmessages = new()
                                { //automatically add saved messages for the user.
                                    user = uid,
                                    type = "user",
                                    chatid = uid + "-" + uid
                                };
                                chats.AddChat(savedmessages);
                                chats.Save(); //save it
                            }
                            else
                            {
                                Console.WriteLine("Signup chatslist was null!!!"); //log if weirdo
                            }
                            //Done
                            res = JsonConvert.SerializeObject(new LoginResponse(token, uid));
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "WPFORMAT", "Password format wrong."));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "WEFORMAT", "Invalid E-Mail."));
                    }
                }
                else
                {
                    statuscode = 401;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "USEREXISTS", "User already exists."));
                }

            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "login")
        {
            var a = JsonConvert.DeserializeObject<loginCred>(body);
            if (a != null)
            {
                a.EMail = a.EMail.Trim();
                if (File.Exists("data/auth/" + a.EMail))
                {
                    loginCred? lc = JsonConvert.DeserializeObject<loginCred>(await File.ReadAllTextAsync("data/auth/" + a.EMail));
                    if (lc != null)
                    {
                        string uid = lc.userID;
                        a.Password = hashPassword(a.Password, uid);
                        if (lc.Password == a.Password && lc.EMail == a.EMail)
                        {
                            //Console.WriteLine("Logging in...");
                            string token = "";
                            do
                            {
                                //Console.WriteLine("Generating token...");
                                token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                            }
                            while (loginCreds.ContainsKey(token));
                            //Console.WriteLine("Generated token");
                            loginCreds[token] = lc;
                            //Console.WriteLine("Respond");
                            res = JsonConvert.SerializeObject(new LoginResponse(token, uid));
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "WRONGLOGIN", "Incorrect login"));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new ServerResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }

            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "changepassword")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("password") && a.ContainsKey("oldpassword"))
            {
                if (a["password"].Trim().Length >= 6)
                {
                    loginCred? lc = await GetLoginCred(a["token"]);
                    if (lc != null)
                    {
                        if (lc.Password == hashPassword(a["oldpassword"], lc.userID))
                        {
                            /*string token = "";
                                *                   do
                                *                   {
                                *                       token =  Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=","").Replace("+","").Replace("/","");
                        }
                        while (loginCreds.ContainsKey(token));*/
                            //File.Delete("data/auth/" + lc.token);
                            //lc.token = token;
                            lc.Password = hashPassword(a["password"].Trim(), lc.userID);
                            string astr = JsonConvert.SerializeObject(lc);
                            //File.WriteAllText("data/auth/" + token, astr);
                            File.WriteAllText("data/auth/" + lc.EMail, astr);
                            //Find other logins
                            var tokens = loginCreds.Where(lco => lco.Value.userID == lc.userID && lc != lco.Value);
                            foreach (var token in tokens)
                            {
                                //remove the logins.
                                loginCreds.Remove(token.Key, out _);
                            }
                            res = astr;
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "WPFORMAT", "Password format wrong."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "logout")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                loginCreds.Remove(a["token"], out _);
                res = JsonConvert.SerializeObject(new ServerResponse("done"));
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getuser")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("uid"))
            {
                if (a["uid"] == "0")
                {
                    res = pamukProfile;
                }
                else
                {
                    UserProfile? up = await UserProfile.Get(a["uid"]);
                    if (up != null)
                    {
                        res = JsonConvert.SerializeObject(up);
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getonline")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("uid"))
            {
                UserProfile? up = await UserProfile.Get(a["uid"]);
                if (up != null)
                {
                    res = up.getOnline();
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "setonline")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    UserProfile? user = await UserProfile.Get(uid);
                    if (user != null)
                    {
                        user.setOnline();
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "updateuser")
        { //User profile edit
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    UserProfile? profile = await UserProfile.Get(uid);
                    if (profile != null)
                    {
                        if (a.ContainsKey("name") && a["name"].Trim() != "")
                        {
                            profile.name = a["name"].Trim().Replace("\n", "");
                        }
                        if (!a.ContainsKey("picture"))
                        {
                            a["picture"] = "";
                        }
                        if (!a.ContainsKey("description"))
                        {
                            a["description"] = "";
                        }
                        profile.picture = a["picture"];
                        profile.description = a["description"].Trim();
                        profile.save();
                        res = JsonConvert.SerializeObject(new ServerResponse("done"));
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getchatslist")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    List<chatItem>? chats = await UserChatsList.Get(uid);
                    if (chats != null)
                    {
                        foreach (chatItem item in chats)
                        { //Format chats for clients
                            /*if (item.type == "user") { //Info
                                *                           var p = userProfile.Get(item.user ?? "");
                                *                           item.info = profileShort.fromProfile(p);
                        }else if (item.type == "group") {
                            var p = Group.get(item.group ?? "");
item.info = profileShort.fromGroup(p);
                        }*/

                            Chat? chat = await Chat.getChat(item.chatid ?? item.group ?? "");
                            if (chat != null)
                            {
                                if (chat.canDo(uid, Chat.chatAction.Read))
                                { //Check for read permission before giving the last message
                                    item.lastmessage = chat.getLastMessage(true);
                                }
                            }
                        }
                        res = JsonConvert.SerializeObject(chats);
                        foreach (chatItem item in chats)
                        {
                            //item.info = null;
                            item.lastmessage = null;
                        }
                    }
                    else
                    {
                        statuscode = 500;
                        res = JsonConvert.SerializeObject(new ServerResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getnotifications")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    //Console.WriteLine(JsonConvert.SerializeObject(notifications));
                    var usernotifies = Notifications.Get(uid);
                    if (a.ContainsKey("mode") && a["mode"] == "hold" && usernotifies.Count == 0) // Hold mode means that if there isn't any notifications, wait for one until a timeout.
                    {
                        int wait = 20; // How long will this wait for notification to appear
                        while (usernotifies.Count == 0 && wait > 0)
                        {
                            await Task.Delay(1000);
                            --wait;
                        }
                    }
                    res = JsonConvert.SerializeObject(usernotifies);
                    List<string> keys = new();
                    foreach (string key in usernotifies.Keys)
                    {
                        keys.Add(key);
                    }
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Delay(10000).ContinueWith((task) =>
                    { //remove notifications after delay so all clients can see it before it's too late. SSSOOOOBBB
                        foreach (string key in keys)
                        {
                            usernotifies.Remove(key, out _);
                        }
                    });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "addhook")
        { // Add update hook
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("ids") && a["ids"] is JArray)
            {
                UpdateHooks updhooks = Updaters.Get(a["token"].ToString() ?? "");
                foreach (string? hid in (JArray)a["ids"])
                {
                    if (hid == null) continue;

                    string[] split = hid.Split(":");
                    string type = split[0];
                    string id = split[1];
                    switch (type)
                    {
                        case "chat":
                            Chat? chat = await Chat.getChat(id);
                            if (chat != null)
                            {
                                updhooks.AddHook(chat);
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                            }
                            break;
                        
                        case "user":
                            UserProfile? user = await UserProfile.Get(id);
                            if (user != null)
                            {
                                updhooks.AddHook(user);
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                            }
                            break;
                    }
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getmutedchats")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    userConfig? userconfig = await userConfig.Get(uid);
                    if (userconfig != null)
                    {
                        res = JsonConvert.SerializeObject(userconfig.mutedChats);
                    }
                    else
                    {
                        statuscode = 500;
                        res = JsonConvert.SerializeObject(new ServerResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "mutechat")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("toggle") && a["toggle"] is bool)
            {
                string? uid = await GetUIDFromToken((a["token"] ?? "").ToString() ?? "");
                if (uid != null)
                {
                    userConfig? userconfig = await userConfig.Get(uid);
                    if (userconfig != null)
                    {
                        string chatid = (a["chatid"] ?? "").ToString() ?? "";
                        if (File.Exists("data/chat/" + chatid + "/chat"))
                        {
                            if ((bool)a["toggle"] == true) // toggle true > mute, false > unmute.
                            {
                                if (!userconfig.mutedChats.Contains(chatid))
                                { //Don't add duplicates
                                    userconfig.mutedChats.Add(chatid);
                                }
                            }
                            else
                            {
                                userconfig.mutedChats.Remove(chatid);
                            }
                            userconfig.Save();
                        }
                        res = JsonConvert.SerializeObject(new ServerResponse("done"));
                    }
                    else
                    {
                        statuscode = 500;
                        res = JsonConvert.SerializeObject(new ServerResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "adduserchat")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("email"))
            {
                string? uidu = await GetUIDFromToken(a["token"]);
                string? uidb = await GetUIDFromToken(a["email"], false);
                if (uidu != null && uidb != null)
                {
                    UserChatsList? chatsb = await UserChatsList.Get(uidb);
                    UserChatsList? chatsu = await UserChatsList.Get(uidu);
                    if (chatsu != null && chatsb != null)
                    {
                        string chatid = uidu + "-" + uidb;
                        chatItem u = new()
                        {
                            user = uidb,
                            type = "user",
                            chatid = chatid
                        };
                        chatItem b = new()
                        {
                            user = uidu,
                            type = "user",
                            chatid = chatid
                        };
                        chatsu.AddChat(u);
                        chatsb.AddChat(b);
                        chatsu.Save();
                        chatsb.Save();
                        res = JsonConvert.SerializeObject(new ServerResponse("done"));
                    }
                    else
                    {
                        statuscode = 500;
                        res = JsonConvert.SerializeObject(new ServerResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getchatpage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        {
                            int page = a.ContainsKey("page") ? int.Parse(a["page"]) : 0;
                            string prefix = "#" + (page * 48) + "-#" + ((page + 1) * 48);
                            res = JsonConvert.SerializeObject(chat.getMessages(prefix).format());
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getmessages")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("prefix"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        {
                            res = JsonConvert.SerializeObject(chat.getMessages(a["prefix"]).format());
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getpinnedmessages")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        {
                            res = JsonConvert.SerializeObject(chat.getPinned().format());
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "sendmessage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null)
            {
                List<string>? files = a.ContainsKey("files") && (a["files"] is JArray) ? ((JArray)a["files"]).ToObject<List<string>>() : null;
                if (a.ContainsKey("token") && a.ContainsKey("chatid") && ((a.ContainsKey("content") && (a["content"].ToString() ?? "") != "") || (files != null && files.Count > 0)))
                {
                    string? uid = await GetUIDFromToken(a["token"].ToString() ?? "");
                    if (uid != null)
                    {
                        Chat? chat = await Chat.getChat(a["chatid"].ToString() ?? "");
                        if (chat != null)
                        {
                            if (chat.canDo(uid, Chat.chatAction.Send))
                            {
                                ChatMessage msg = new()
                                {
                                    sender = uid,
                                    content = (a["content"].ToString() ?? "").Trim(),
                                    replymsgid = a.ContainsKey("replymsg") ? a["replymsg"].ToString() : null,
                                    files = files,
                                    time = dateToString(DateTime.Now)
                                };
                                chat.sendMessage(msg);
                                var userstatus = UserStatus.Get(uid);
                                if (userstatus != null)
                                {
                                    userstatus.setTyping(null);
                                }
                                res = JsonConvert.SerializeObject(new ServerResponse("done"));
                            }
                            else
                            {
                                statuscode = 401;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                            }
                        }
                        else
                        {
                            statuscode = 404;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new ServerResponse("error"));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "deletemessage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgs"))
            {
                string? uid = await GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        if (a["msgs"] is JArray)
                        {
                            var msgs = (JArray)a["msgs"];
                            foreach (object msg in msgs)
                            {
                                string? msgid = msg.ToString() ?? "";
                                if (chat.canDo(uid, Chat.chatAction.Delete, msgid))
                                {
                                    //if (chat.ContainsKey(msgid)) {
                                    chat.deleteMessage(msgid);
                                    //}
                                }
                            }
                            res = JsonConvert.SerializeObject(new ServerResponse("done"));
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "pinmessage")
        { //More like a toggle
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgs"))
            {
                string? uid = await GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        if (a["msgs"] is JArray)
                        {
                            var msgs = (JArray)a["msgs"];
                            foreach (object msg in msgs)
                            {
                                string? msgid = msg.ToString() ?? "";
                                if (chat.canDo(uid, Chat.chatAction.Pin, msgid))
                                {
                                    bool pinned = chat.pinMessage(msgid);
                                    if (chat.canDo(uid, Chat.chatAction.Send))
                                    {
                                        ChatMessage message = new()
                                        {
                                            sender = "0",
                                            content = (pinned ? "" : "UN") + "PINNEDMESSAGE|" + uid + "|" + msgid,
                                            time = dateToString(DateTime.Now)
                                        };
                                        chat.sendMessage(message);
                                    }
                                }
                            }
                            res = JsonConvert.SerializeObject(new ServerResponse("done"));
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "sendreaction")
        { //More like a toggle
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgid") && a.ContainsKey("reaction") && (a["reaction"].ToString() ?? "") != "")
            {
                string? uid = await GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        string? msgid = a["msgid"].ToString() ?? "";
                        string? reaction = a["reaction"].ToString() ?? "";
                        if (chat.canDo(uid, Chat.chatAction.React, msgid))
                        {
                            //if (chat.ContainsKey(msgid)) {
                            res = JsonConvert.SerializeObject(chat.reactMessage(msgid, uid, reaction));
                            //}else {
                            //    statuscode = 404;
                            //    res = JsonConvert.SerializeObject(new serverResponse("error", "NOMSG", "Message not found"));
                            //}
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "savemessage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgs"))
            {
                string? uid = await GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        {
                            if (a["msgs"] is JArray)
                            {
                                var msgs = (JArray)a["msgs"];
                                foreach (object msg in msgs)
                                {
                                    string? msgid = msg.ToString() ?? "";
                                    if (chat.ContainsKey(msgid))
                                    {
                                        Chat? uchat = await Chat.getChat(uid + "-" + uid);
                                        if (uchat != null)
                                        {
                                            ChatMessage message = new()
                                            {
                                                sender = chat[msgid].sender,
                                                content = chat[msgid].content,
                                                files = chat[msgid].files,
                                                time = dateToString(DateTime.Now)
                                            };
                                            uchat.sendMessage(message, false);

                                        }
                                    }
                                }
                                res = JsonConvert.SerializeObject(new ServerResponse("done"));
                            }
                            else
                            {
                                statuscode = 401;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                            }
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "forwardmessage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgs") && a.ContainsKey("tochats"))
            {
                string? uid = await GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        {
                            if (a["msgs"] is JArray)
                            {
                                var msgs = (JArray)a["msgs"];
                                foreach (object msg in msgs)
                                {
                                    string? msgid = msg.ToString() ?? "";
                                    if (chat.ContainsKey(msgid))
                                    {
                                        if (a["tochats"] is JArray)
                                        {
                                            var chats = (JArray)a["tochats"];
                                            foreach (object chatid in chats)
                                            {
                                                Chat? uchat = await Chat.getChat(chatid.ToString() ?? "");
                                                if (uchat != null)
                                                {
                                                    if (uchat.canDo(uid, Chat.chatAction.Send))
                                                    {
                                                        ChatMessage message = new()
                                                        {
                                                            forwardedfrom = chat[msgid].sender,
                                                            sender = uid,
                                                            content = chat[msgid].content,
                                                            files = chat[msgid].files,
                                                            time = dateToString(DateTime.Now)
                                                        };
                                                        uchat.sendMessage(message);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            res = JsonConvert.SerializeObject(new ServerResponse("done"));
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getupdates")
        { // Updates
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                if (a.ContainsKey("id")) // Chat based updaters
                {
                    string? uid = await GetUIDFromToken(a["token"]);
                    if (uid != null)
                    {
                        Chat? chat = await Chat.getChat(a["id"]);
                        if (chat != null)
                        {
                            if (chat.canDo(uid, Chat.chatAction.Read))
                            { //Check if user can even "read" it at all
                                string requestMode = "normal";
                                if (a.ContainsKey("mode"))
                                {
                                    requestMode = a["mode"];
                                }

                                if (requestMode == "updater") // Updater mode will wait for a new message. "since" shouldn't work here.
                                {
                                    res = JsonConvert.SerializeObject(await chat.waitForUpdates());
                                }
                                else
                                {
                                    if (a.ContainsKey("since"))
                                    {
                                        res = JsonConvert.SerializeObject(chat.getUpdates(long.Parse(a["since"])));
                                    }
                                    else
                                    {
                                        statuscode = 411;
                                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOSINCE", "\"since\" not found in the normal mode request."));
                                    }
                                }
                            }
                            else
                            {
                                statuscode = 401;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                            }
                        }
                        else
                        {
                            statuscode = 404;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else // Global based updates
                {
                    res = JsonConvert.SerializeObject(await Updaters.Get(a["token"]).waitForUpdates());
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "settyping")
        { //Set user as typing at a chat
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Send))
                        { //Ofc, if the user has the permission to send at the chat
                            var userstatus = UserStatus.Get(uid);
                            if (userstatus != null)
                            {
                                userstatus.setTyping(chat.chatID);
                                res = JsonConvert.SerializeObject(new ServerResponse("done"));
                            }
                            else
                            {
                                statuscode = 500;
                                res = JsonConvert.SerializeObject(new ServerResponse("error"));
                            }
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "gettyping")
        { //Get typing users in a chat
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.getChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        { //Ofc, if the user has the permission to read the chat
                            string requestMode = "normal";
                            if (a.ContainsKey("mode"))
                            {
                                requestMode = a["mode"];
                            }
                            if (requestMode == "updater")
                                res = JsonConvert.SerializeObject(await chat.waitForTypingUpdates());
                            else
                                res = JsonConvert.SerializeObject(chat.typingUsers);
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "creategroup")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    if (a.ContainsKey("name") && a["name"].Trim() != "")
                    {
                        if (!a.ContainsKey("picture"))
                        {
                            a["picture"] = "";
                        }
                        if (!a.ContainsKey("info"))
                        {
                            a["info"] = "";
                        }
                        string id = "";
                        do
                        {
                            id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                        }
                        while (Directory.Exists("data/infos/" + id));
                        Group g = new()
                        {
                            groupID = id,
                            name = a["name"].Trim(),
                            picture = a["picture"],
                            info = a["info"].Trim(),
                            owner = uid,
                            time = dateToString(DateTime.Now),
                            roles = new()
                            { //Default roles
                                ["Owner"] = new GroupRole()
                                {
                                    AdminOrder = 0,
                                    AllowBanning = true,
                                    AllowEditingSettings = true,
                                    AllowEditingUsers = true,
                                    AllowKicking = true,
                                    AllowMessageDeleting = true,
                                    AllowSending = true,
                                    AllowSendingReactions = true,
                                    AllowPinningMessages = true
                                },
                                ["Admin"] = new GroupRole()
                                {
                                    AdminOrder = 1,
                                    AllowBanning = true,
                                    AllowEditingSettings = false,
                                    AllowEditingUsers = true,
                                    AllowKicking = true,
                                    AllowMessageDeleting = true,
                                    AllowSending = true,
                                    AllowSendingReactions = true,
                                    AllowPinningMessages = true
                                },
                                ["Moderator"] = new GroupRole()
                                {
                                    AdminOrder = 2,
                                    AllowBanning = true,
                                    AllowEditingSettings = false,
                                    AllowEditingUsers = false,
                                    AllowKicking = true,
                                    AllowMessageDeleting = true,
                                    AllowSending = true,
                                    AllowSendingReactions = true,
                                    AllowPinningMessages = true
                                },
                                ["Normal"] = new GroupRole()
                                {
                                    AdminOrder = 3,
                                    AllowBanning = false,
                                    AllowEditingSettings = false,
                                    AllowEditingUsers = false,
                                    AllowKicking = false,
                                    AllowMessageDeleting = false,
                                    AllowSending = true,
                                    AllowSendingReactions = true,
                                    AllowPinningMessages = false
                                },
                                ["Readonly"] = new GroupRole()
                                {
                                    AdminOrder = 4,
                                    AllowBanning = false,
                                    AllowEditingSettings = false,
                                    AllowEditingUsers = false,
                                    AllowKicking = false,
                                    AllowMessageDeleting = false,
                                    AllowSending = false,
                                    AllowSendingReactions = false,
                                    AllowPinningMessages = false
                                }
                            }
                        };
                        await g.addUser(uid, "Owner");
                        g.Save();
                        Group.groupsCache[id] = g;
                        Dictionary<string, string> response = new()
                        {
                            ["groupid"] = id
                        };
                        res = JsonConvert.SerializeObject(response);
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOINFO", "No group info"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getgroup")
        { //get group info
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                string uid = await GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(new GroupInfo()
                        {
                            name = gp.name,
                            info = gp.info,
                            picture = gp.picture,
                            publicgroup = gp.publicgroup
                        });
                    }
                    else
                    {
                        statuscode = 403;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getinfo")
        { //get user or group info
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("id"))
            {
                string uid = await GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                if (a["id"] == "0")
                {
                    res = pamukProfile;
                }
                else
                {
                    UserProfile? up = await UserProfile.Get(a["id"]);
                    if (up != null)
                    {
                        res = JsonConvert.SerializeObject(up);
                    }
                    else
                    {
                        Group? gp = await Group.Get(a["id"]);
                        if (gp != null)
                        {
                            if (gp.canDo(uid, Group.groupAction.Read))
                            {
                                res = JsonConvert.SerializeObject(new GroupInfo()
                                {
                                    name = gp.name,
                                    info = gp.info,
                                    picture = gp.picture,
                                    publicgroup = gp.publicgroup
                                });
                            }
                            else
                            {
                                statuscode = 403;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                            }
                        }
                        else
                        {
                            statuscode = 404;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                        }
                    }
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getgroupusers" || action == "getgroupmembers")
        { //getgroupmembers is new name, gets the members list in json format.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                string uid = await GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(gp.members);
                    }
                    else
                    {
                        statuscode = 403;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getbannedgroupmembers")
        { //gets banned group members in the group
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                string uid = await GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(gp.bannedMembers);
                    }
                    else
                    {
                        statuscode = 403;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getgroupuserscount" || action == "getgroupmemberscount")
        { //getgroupmemberscount is new name, returns group member count as string. like "5"
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                Group? gp = await Group.Get(a["groupid"]);
                string uid = await GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = gp.members.Count.ToString();
                    }
                    else
                    {
                        statuscode = 403;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getgrouproles")
        { //get all group roles
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                string uid = await GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(gp.roles);
                    }
                    else
                    {
                        statuscode = 403;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "getgrouprole")
        { //Group role for current user
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        var role = gp.getUserRole(uid);
                        if (role != null)
                        {
                            res = JsonConvert.SerializeObject(role);
                        }
                        else
                        {
                            if (gp.publicgroup)
                            {
                                res = JsonConvert.SerializeObject(new GroupRole()
                                {
                                    AdminOrder = -1,
                                    AllowBanning = false,
                                    AllowEditingSettings = false,
                                    AllowEditingUsers = false,
                                    AllowKicking = false,
                                    AllowMessageDeleting = false,
                                    AllowSending = false,
                                    AllowSendingReactions = false,
                                    AllowPinningMessages = false
                                });
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                            }
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "joingroup")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (await gp.addUser(uid))
                        {
                            Dictionary<string, string> response = new()
                            {
                                ["groupid"] = gp.groupID
                            };
                            res = JsonConvert.SerializeObject(response);
                            gp.Save();
                        }
                        else
                        {
                            statuscode = 500;
                            res = JsonConvert.SerializeObject(new ServerResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "leavegroup")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        Chat? chat = await Chat.getChat(gp.groupID);
                        if (await gp.removeUser(uid))
                        {
                            gp.Save();
                            res = JsonConvert.SerializeObject(new ServerResponse("done"));
                        }
                        else
                        {
                            statuscode = 500;
                            res = JsonConvert.SerializeObject(new ServerResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "kickuser")
        { //Kicks a user from the group. They can rejoin.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("uid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.canDo(uid, Group.groupAction.Kick, a["uid"] ?? ""))
                        {
                            if (await gp.removeUser(a["uid"] ?? ""))
                            {
                                gp.Save();
                                res = JsonConvert.SerializeObject(new ServerResponse("done"));
                            }
                            else
                            {
                                statuscode = 500;
                                res = JsonConvert.SerializeObject(new ServerResponse("error"));
                            }
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "banuser")
        { //bans a user from the group. they can't join until they are unbanned.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("uid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.canDo(uid, Group.groupAction.Ban, a["uid"] ?? ""))
                        {
                            if (await gp.banUser(a["uid"] ?? ""))
                            {
                                gp.Save();
                                res = JsonConvert.SerializeObject(new ServerResponse("done"));
                            }
                            else
                            {
                                statuscode = 500;
                                res = JsonConvert.SerializeObject(new ServerResponse("error"));
                            }
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "unbanuser")
        { //Unbans a user, they can rejoin.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("uid"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.canDo(uid, Group.groupAction.Ban, a["uid"] ?? ""))
                        {
                            gp.unbanUser(a["uid"] ?? "");
                            gp.Save();
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "editgroup")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid"))
            {
                string? uid = await GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"].ToString() ?? "");
                    if (gp != null)
                    {
                        if (gp.canDo(uid, Group.groupAction.EditGroup))
                        {
                            if (a.ContainsKey("name") && (a["name"].ToString() ?? "").Trim() != "")
                            {
                                gp.name = a["name"].ToString() ?? "";
                            }
                            if (a.ContainsKey("picture"))
                            {
                                gp.picture = a["picture"].ToString() ?? "";
                            }
                            if (a.ContainsKey("info") && (a["info"].ToString() ?? "").Trim() != "")
                            {
                                gp.info = a["info"].ToString() ?? "";
                            }
                            if (a.ContainsKey("publicgroup") && a["publicgroup"] is bool)
                            {
                                gp.publicgroup = (bool)a["publicgroup"];
                            }
                            if (a.ContainsKey("roles"))
                            {
                                bool setroles = true;
                                var roles = ((JObject)a["roles"]).ToObject<Dictionary<string, GroupRole>>() ?? gp.roles;
                                foreach (var member in gp.members.Values)
                                {
                                    if (!roles.ContainsKey(member.role))
                                    {
                                        setroles = false;
                                    }
                                }
                                if (setroles) gp.roles = roles;
                            }
                            // backwards compat
                            Dictionary<string, string> response = new()
                            {
                                ["groupid"] = gp.groupID
                            };
                            res = JsonConvert.SerializeObject(response);
                            gp.Save();
                            Chat? chat = await Chat.getChat(gp.groupID);
                            if (chat != null)
                            {
                                if (chat.canDo(uid, Chat.chatAction.Send))
                                {
                                    ChatMessage message = new()
                                    {
                                        sender = "0",
                                        content = "EDITGROUP|" + uid,
                                        time = dateToString(DateTime.Now)
                                    };
                                    chat.sendMessage(message);
                                }
                            }
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else if (action == "edituser")
        { //Edits role of user in the group.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("userid") && a.ContainsKey("role"))
            {
                string? uid = await GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.canDo(uid, Group.groupAction.EditUser, a["userid"]))
                        {
                            if (gp.members.ContainsKey(a["userid"]))
                            {
                                if (gp.roles.ContainsKey(a["role"]))
                                {
                                    var crole = gp.roles[a["role"]];
                                    var curole = gp.getUserRole(uid);
                                    if (curole != null)
                                    {
                                        if (crole.AdminOrder >= curole.AdminOrder)
                                        { //Dont allow to promote higher from current role.
                                            gp.members[a["userid"]].role = a["role"];
                                            res = JsonConvert.SerializeObject(new ServerResponse("done"));
                                            gp.Save();
                                        }
                                        else
                                        {
                                            statuscode = 403;
                                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed to set more than current role"));
                                        }
                                    }
                                    else
                                    {
                                        statuscode = 500;
                                        res = JsonConvert.SerializeObject(new ServerResponse("error"));
                                    }
                                }
                                else
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOROLE", "Role doesn't exist."));
                                }
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                            }
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new ServerResponse("error"));
            }
        }
        else
        { //Ping!!!!
            res = "Pong!";
        }
        ActionReturn ret = new() { res = res, statuscode = statuscode };
        return ret;
    }

    /// <summary>
    /// Responds to a HttpListenerContext
    /// </summary>
    /// <param name="context">HttpListenerContext to respond to</param>
    /// <exception cref="Exception">Throws if something that shouldn't happen, like failing to parse a json that should be parseable.</exception>
    static async void respondToRequest(HttpListenerContext context)
    {
        if (context.Request.Url == null)
        { //just ignore
            return;
        }

        bool manualRespond = false; //Like upload/getmedia call.
        string res = "";
        int statuscode = 200;
        string[] spl = context.Request.Url.ToString().Split("/");
        string url = spl[spl.Length - 1];
        context.Response.KeepAlive = false;
        //Added these so web client can access it
        context.Response.AddHeader("Access-Control-Allow-Headers", "*");
        context.Response.AddHeader("Access-Control-Allow-Methods", "*");
        context.Response.AddHeader("Access-Control-Allow-Origin", "*");
        //Console.WriteLine(url); //debugging

        if (!((url == "upload" && context.Request.HttpMethod.ToLower() == "post") || url.StartsWith("getmedia")))
        {
            string body = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
            //Console.WriteLine(url + " " + body);
            try
            {
                if (url == "multi")
                {
                    Dictionary<string, string>? actions = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
                    if (actions != null)
                    {
                        ConcurrentDictionary<string, ActionReturn> responses = new();
                        foreach (var request in actions)
                        {
                            ActionReturn actionreturn = await doAction(request.Key.Split("|")[0], request.Value);
                            responses[request.Key] = actionreturn;
                        }
                        res = JsonConvert.SerializeObject(responses);
                    }
                }
                #region Federation
                else if (url == "federationrequest")
                {
                    federationRequest? request = JsonConvert.DeserializeObject<federationRequest>(body);
                    if (request != null)
                    {
                        if (request.serverurl != null)
                        {
                            if (request.serverurl != Federation.thisServerURL)
                            {
                                if (Federation.federationClient == null) Federation.federationClient = new();
                                try
                                {
                                    var httpTask = await Federation.federationClient.GetAsync(request.serverurl);
                                    // Valid, allow to federate
                                    string id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                                    Federation fed = new(request.serverurl, id);
                                    Federation.federations[request.serverurl] = fed;
                                    statuscode = 200;
                                    res = JsonConvert.SerializeObject(fed); //return info
                                }
                                catch (Exception e)
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "ERROR", "Couldn't connect to remote. " + e.Message));
                                }
                            }
                            else
                            {
                                statuscode = 418;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "ITSME", "Hello me!"));
                            }
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOSERVERURL", "Request doesn't contain a serverurl."));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                    }
                }
                else if (url == "federationgetgroup")
                {
                    Dictionary<string, string>? request = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
                    if (request != null)
                    {
                        if (request.ContainsKey("serverurl"))
                        {
                            if (request.ContainsKey("id"))
                            {
                                if (Federation.federations.ContainsKey(request["serverurl"]))
                                {
                                    Federation fed = Federation.federations[request["serverurl"]];
                                    if (fed.id == request["id"])
                                    {

                                        if (request.ContainsKey("groupid"))
                                        {
                                            string id = request["groupid"].Split("@")[0];
                                            Group? gp = await Group.Get(id);
                                            if (gp != null)
                                            {
                                                bool showfullinfo = false;
                                                if (gp.publicgroup)
                                                {
                                                    showfullinfo = true;
                                                }
                                                else
                                                {
                                                    foreach (string member in gp.members.Keys)
                                                    {
                                                        if (member.Contains("@"))
                                                        {
                                                            string server = member.Split("@")[1];
                                                            if (server == fed.serverURL)
                                                            {
                                                                showfullinfo = true;
                                                                break;
                                                            }
                                                        }
                                                    }
                                                }
                                                if (showfullinfo)
                                                {
                                                    res = JsonConvert.SerializeObject(gp);
                                                }
                                                else
                                                {
                                                    res = JsonConvert.SerializeObject(new ServerResponse("exists")); //To make server know the group actually exists but it's private.
                                                }
                                            }
                                            else
                                            {
                                                statuscode = 404;
                                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group not found."));
                                            }
                                        }
                                        else
                                        {
                                            statuscode = 411;
                                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGID", "Request doesn't contain a group ID."));
                                        }
                                    }
                                    else
                                    {
                                        statuscode = 403;
                                        res = JsonConvert.SerializeObject(new ServerResponse("error", "IDWRONG", "ID is wrong."));
                                    }
                                }
                                else
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFED", "Not a valid federation."));
                                }
                            }
                            else
                            {
                                statuscode = 411;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOID", "Request doesn't contain a ID."));
                            }
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOSERVERURL", "Request doesn't contain a server URL."));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                    }
                }
                else if (url == "federationjoingroup")
                {
                    Dictionary<string, string>? request = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
                    if (request != null)
                    {
                        if (request.ContainsKey("serverurl"))
                        {
                            if (request.ContainsKey("id"))
                            {
                                if (Federation.federations.ContainsKey(request["serverurl"]))
                                {
                                    Federation fed = Federation.federations[request["serverurl"]];
                                    if (fed.id == request["id"])
                                    {
                                        if (request.ContainsKey("groupid"))
                                        {
                                            if (request.ContainsKey("userid"))
                                            {
                                                string id = request["groupid"].Split("@")[0];
                                                Group? gp = await Group.Get(id);
                                                if (gp != null)
                                                {
                                                    bool stat = await gp.addUser(request["userid"] + "@" + fed.serverURL);
                                                    if (stat)
                                                    {
                                                        gp.Save();
                                                        res = JsonConvert.SerializeObject(new ServerResponse("done"));
                                                    }
                                                    else
                                                    {
                                                        statuscode = 403;
                                                        res = JsonConvert.SerializeObject(new ServerResponse("error", "FAIL", "Couldn't join group."));
                                                    }
                                                }
                                                else
                                                {
                                                    statuscode = 404;
                                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group not found."));
                                                }
                                            }
                                            else
                                            {
                                                statuscode = 411;
                                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUID", "Request doesn't contain a user ID."));
                                            }
                                        }
                                        else
                                        {
                                            statuscode = 411;
                                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGID", "Request doesn't contain a group ID."));
                                        }
                                    }
                                    else
                                    {
                                        statuscode = 403;
                                        res = JsonConvert.SerializeObject(new ServerResponse("error", "IDWRONG", "ID is wrong."));
                                    }
                                }
                                else
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFED", "Not a valid federation."));
                                }
                            }
                            else
                            {
                                statuscode = 411;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOID", "Request doesn't contain a ID."));
                            }
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOSERVERURL", "Request doesn't contain a server URL."));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                    }
                }
                else if (url == "federationleavegroup")
                {
                    Dictionary<string, string>? request = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
                    if (request != null)
                    {
                        if (request.ContainsKey("serverurl"))
                        {
                            if (request.ContainsKey("id"))
                            {
                                if (Federation.federations.ContainsKey(request["serverurl"]))
                                {
                                    Federation fed = Federation.federations[request["serverurl"]];
                                    if (fed.id == request["id"])
                                    {
                                        if (request.ContainsKey("groupid"))
                                        {
                                            if (request.ContainsKey("userid"))
                                            {
                                                string id = request["groupid"].Split("@")[0];
                                                Group? gp = await Group.Get(id);
                                                if (gp != null)
                                                {
                                                    bool stat = await gp.removeUser(request["userid"] + "@" + fed.serverURL);
                                                    if (stat)
                                                    {
                                                        gp.Save();
                                                        res = JsonConvert.SerializeObject(new ServerResponse("done"));
                                                    }
                                                    else
                                                    {
                                                        statuscode = 403;
                                                        res = JsonConvert.SerializeObject(new ServerResponse("error", "FAIL", "Couldn't leave group."));
                                                    }
                                                }
                                                else
                                                {
                                                    statuscode = 404;
                                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group not found."));
                                                }
                                            }
                                            else
                                            {
                                                statuscode = 411;
                                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUID", "Request doesn't contain a user ID."));
                                            }
                                        }
                                        else
                                        {
                                            statuscode = 411;
                                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGID", "Request doesn't contain a group ID."));
                                        }
                                    }
                                    else
                                    {
                                        statuscode = 403;
                                        res = JsonConvert.SerializeObject(new ServerResponse("error", "IDWRONG", "ID is wrong."));
                                    }
                                }
                                else
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFED", "Not a valid federation."));
                                }
                            }
                            else
                            {
                                statuscode = 411;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOID", "Request doesn't contain a ID."));
                            }
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOSERVERURL", "Request doesn't contain a server URL."));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                    }
                }
                else if (url == "federationgetchat")
                {
                    Dictionary<string, string>? request = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
                    if (request != null)
                    {
                        if (request.ContainsKey("serverurl"))
                        {
                            if (request.ContainsKey("id"))
                            {
                                if (Federation.federations.ContainsKey(request["serverurl"]))
                                {
                                    Federation fed = Federation.federations[request["serverurl"]];
                                    if (fed.id == request["id"])
                                    {
                                        if (request.ContainsKey("chatid"))
                                        {
                                            string id = request["chatid"].Split("@")[0];
                                            Chat? chat = await Chat.getChat(id);
                                            if (chat != null)
                                            {
                                                bool showfullinfo = false;
                                                if (chat.group.publicgroup)
                                                {
                                                    showfullinfo = true;
                                                }
                                                else
                                                {
                                                    foreach (string member in chat.group.members.Keys)
                                                    {
                                                        if (member.Contains("@"))
                                                        {
                                                            string server = member.Split("@")[1];
                                                            if (server == fed.serverURL)
                                                            {
                                                                showfullinfo = true;
                                                                break;
                                                            }
                                                        }
                                                    }
                                                }
                                                if (showfullinfo)
                                                {
                                                    chat.connectedFederations.Add(fed);
                                                    res = JsonConvert.SerializeObject(chat);
                                                }
                                                else
                                                {
                                                    statuscode = 403;
                                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOPERM", "Can't read chat."));
                                                }
                                            }
                                            else
                                            {
                                                statuscode = 404;
                                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOCHAT", "Chat not found."));
                                            }
                                        }
                                        else
                                        {
                                            statuscode = 411;
                                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOCID", "Request doesn't contain a chat ID."));
                                        }
                                    }
                                    else
                                    {
                                        statuscode = 404;
                                        res = JsonConvert.SerializeObject(new ServerResponse("error", "IDWRONG", "ID is wrong."));
                                    }
                                }
                                else
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFED", "Not a valid federation."));
                                }
                            }
                            else
                            {
                                statuscode = 411;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOID", "Request doesn't contain a id."));
                            }
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOSERVERURL", "Request doesn't contain a server URL."));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                    }
                }
                else if (url == "federationrecievechatupdates")
                {
                    Dictionary<string, object>? request = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
                    if (request != null)
                    {
                        if (request.ContainsKey("serverurl"))
                        {
                            if (request.ContainsKey("id"))
                            {
                                if (request.ContainsKey("updates"))
                                {
                                    if (Federation.federations.ContainsKey(request["serverurl"].ToString() ?? ""))
                                    {
                                        Federation fed = Federation.federations[request["serverurl"].ToString() ?? ""];
                                        if (fed.id == request["id"].ToString())
                                        {
                                            if (request.ContainsKey("chatid"))
                                            {
                                                string id = (request["chatid"].ToString() ?? "").Split("@")[0];
                                                bool isremote = true;
                                                Chat? chat = await Chat.getChat(id + "@" + fed.serverURL);
                                                if (chat == null)
                                                {
                                                    chat = await Chat.getChat(id);
                                                    isremote = false;
                                                }
                                                if (chat != null)
                                                {
                                                    var updates = (JArray)request["updates"];
                                                    foreach (var upd in updates)
                                                    {
                                                        var update = (JObject)upd;
                                                        string eventn = (update["event"] ?? "").ToString() ?? "";
                                                        string mid = (update["id"] ?? "").ToString() ?? "";
                                                        if (eventn == "NEWMESSAGE")
                                                        {
                                                            if (!isremote)
                                                            {
                                                                if ((update["sender"] ?? "").ToString() == "0")
                                                                {
                                                                    continue; //Don't allow Pamuk messages from other federations, because they are probably echoes.
                                                                }
                                                            }

                                                            // IDK how else to do this...
                                                            string? forwardedFrom = null;
                                                            if (update.ContainsKey("forwardedfrom"))
                                                            {
                                                                if (update["forwardedfrom"] != null)
                                                                {
                                                                    forwardedFrom = (update["forwardedfrom"] ?? "").ToString();
                                                                    if (forwardedFrom == "") {
                                                                        forwardedFrom = null;
                                                                    }
                                                                }
                                                            }

                                                            ChatMessage msg = new ChatMessage()
                                                            {
                                                                sender = (update["sender"] ?? "").ToString() ?? "",
                                                                content = (update["content"] ?? "").ToString() ?? "",
                                                                time = (update["time"] ?? "").ToString() ?? "",
                                                                replymsgid = update.ContainsKey("replymsgid") ? update["replymsgid"] == null ? null : (update["replymsgid"] ?? "").ToString() : null,
                                                                forwardedfrom = forwardedFrom,
                                                                files = update.ContainsKey("files") && (update["files"] is JArray) ? ((JArray?)update["files"] ?? new JArray()).ToObject<List<string>>() : null,
                                                                pinned = update["pinned"] != null ? (bool?)update["pinned"] ?? false : false,
                                                                reactions = update.ContainsKey("reactions") && (update["reactions"] is JObject) ? ((JObject?)update["reactions"] ?? new JObject()).ToObject<MessageReactions>() ?? new MessageReactions() : new MessageReactions(),
                                                            };
                                                            fed.fixMessage(msg);
                                                            chat.sendMessage(msg, true, mid);
                                                        }
                                                        else if (eventn == "REACTIONS")
                                                        {
                                                            if (update.ContainsKey("rect"))
                                                            {
                                                                if (update["rect"] != null)
                                                                {
                                                                    var reactions = (JObject?)update["rect"];
                                                                    if (reactions != null)
                                                                    {
                                                                        var r = reactions.ToObject<MessageReactions>();
                                                                        if (r != null) chat.putReactions(mid, r);
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        else if (eventn == "DELETED")
                                                        {
                                                            chat.deleteMessage(mid);
                                                        }
                                                        else if (eventn == "PINNED")
                                                        {
                                                            chat.pinMessage(mid, true);
                                                        }
                                                        else if (eventn == "UNPINNED")
                                                        {
                                                            chat.pinMessage(mid, false);
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    statuscode = 404;
                                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOCHAT", "Chat not found."));
                                                }
                                            }
                                            else
                                            {
                                                statuscode = 411;
                                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOCID", "Request doesn't contain a chat ID."));
                                            }
                                        }
                                        else
                                        {
                                            statuscode = 403;
                                            res = JsonConvert.SerializeObject(new ServerResponse("error", "IDWRONG", "ID is wrong."));
                                        }

                                    }
                                    else
                                    {
                                        statuscode = 404;
                                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFED", "Not a valid federation."));
                                    }
                                }
                                else
                                {
                                    statuscode = 411;
                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUPDATES", "Request doesn't contain updates."));
                                }
                            }
                            else
                            {
                                statuscode = 411;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOID", "Request doesn't contain a ID."));
                            }
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOSERVER", "Request doesn't contain a server url."));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                    }
                }
                #endregion
                else
                {
                    ActionReturn actionreturn = await doAction(url, body);
                    statuscode = actionreturn.statuscode;
                    res = actionreturn.res;
                }
            }
            catch (Exception e)
            {
                statuscode = 500;
                res = e.ToString();
                Console.WriteLine(e.ToString());
            }
        }
        else
        { //Upload call
            if (url == "upload")
            {
                if (context.Request.Headers["token"] != null)
                {
                    string? uid = await GetUIDFromToken(context.Request.Headers["token"] ?? "");
                    if (uid != null)
                    {
                        if (context.Request.Headers["content-length"] != null)
                        {
                            int contentLength = int.Parse(context.Request.Headers["content-length"] ?? "0");
                            if (contentLength != 0)
                            {
                                manualRespond = true;
                                string id = "";
                                do
                                {
                                    id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                                }
                                while (File.Exists("data/upload/" + id));
                                string? filename = id;// = context.Request.Headers["filename"];
                                //if (filename == null) {
                                //filename = id;
                                //}else {
                                //    filename = filename + id;
                                //}
                                string fpname = filename.Replace(".", "").Replace("/", "").Replace("\\", "");
                                var stream = context.Request.InputStream;
                                //stream.Seek(0, SeekOrigin.Begin);
                                var fileStream = File.Create("data/upload/" + fpname + ".file");
                                await stream.CopyToAsync(fileStream);
                                fileStream.Close();
                                fileStream.Dispose();
                                FileUpload u = new()
                                {
                                    size = contentLength,
                                    actualName = context.Request.Headers["filename"] ?? id,
                                    sender = uid,
                                    contentType = context.Request.Headers["content-type"] ?? ""
                                };

                                string? uf = JsonConvert.SerializeObject(u);
                                if (uf == null) throw new Exception("???");
                                File.WriteAllText("data/upload/" + fpname, uf);

                                res = JsonConvert.SerializeObject(new FileUploadResponse("success", "%SERVER%getmedia?file=" + fpname));
                                context.Response.StatusCode = statuscode;
                                context.Response.ContentType = "text/json";
                                byte[] bts = Encoding.UTF8.GetBytes(res);
                                context.Response.OutputStream.Write(bts, 0, bts.Length);
                                context.Response.KeepAlive = false;
                                context.Response.Close();
                                string extension = u.contentType.Split("/")[1];
                                if (extension == "png" || extension == "jpg" || extension == "jpeg" || extension == "gif" || extension == "bmp")
                                    mediaProcesserJobs.Add(id);
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFILE", "No file."));
                            }
                        }
                        else
                        {
                            statuscode = 404;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFILE", "No file."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new ServerResponse("error"));
                }
            }
            // Getmedia call
            else if (url.StartsWith("getmedia"))
            { //Needs improvement
                if (context.Request.QueryString["file"] != null)
                {
                    string file = context.Request.QueryString["file"] ?? "";
                    string type = context.Request.QueryString["type"] ?? "";
                    if (File.Exists("data/upload/" + file))
                    {
                        FileUpload? f = JsonConvert.DeserializeObject<FileUpload>(await File.ReadAllTextAsync("data/upload/" + file));
                        if (f != null)
                        {
                            manualRespond = true;
                            //context.Response.AddHeader("Content-Length", f.size.ToString());
                            if (context.Request.Headers["sec-fetch-dest"] != "document")
                            {
                                context.Response.AddHeader("Content-Disposition", "attachment; filename=" + HttpUtility.UrlEncode(f.actualName));
                            }
                            context.Response.StatusCode = statuscode;
                            string path = "data/upload/" + file + "." + (type == "thumb" ? "thumb" : "file");
                            async void sendFile()
                            {
                                var fileStream = File.OpenRead(path);
                                context.Response.KeepAlive = false;
                                try
                                {
                                    await fileStream.CopyToAsync(context.Response.OutputStream);
                                }
                                catch
                                {
                                    
                                }
                                context.Response.Close();
                                fileStream.Close();
                                fileStream.Dispose();
                            }
                            if (File.Exists(path))
                            {
                                sendFile();
                            }
                            else
                            {
                                string apath = "data/upload/" + file + ".file";
                                bool error = !File.Exists(apath);

                                if (error)
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "File doesn't exist, could be the thumbnail."));
                                }
                                else
                                {
                                    if (!mediaProcesserJobs.Contains(file)) mediaProcesserJobs.Add(file); //Generate it for next visits and current one
                                    while (!File.Exists(path))
                                    {
                                        await Task.Delay(1000);
                                    }
                                    sendFile();
                                }
                            }
                        }
                        else
                        {
                            statuscode = 500;
                            res = JsonConvert.SerializeObject(new ServerResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFILE", "File doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new ServerResponse("error"));
                }
            }
        }
        
        if (!manualRespond)
            try
            {
                context.Response.StatusCode = statuscode;
                context.Response.ContentType = "text/json";
                byte[] bts = Encoding.UTF8.GetBytes(res);
                context.Response.OutputStream.Write(bts, 0, bts.Length);
                context.Response.Close();
            }
            catch { }
        //Console.WriteLine("Respone given to a request.");
    }

    /// <summary>
    /// Handles new http calls.
    /// </summary>
    /// <param name="result"></param>
    static void respondCall(IAsyncResult result)
    {
        HttpListener? listener = (HttpListener?)result.AsyncState;
        if (listener == null) return;
        HttpListenerContext? context = listener.EndGetContext(result);
        _httpListener.BeginGetContext(new AsyncCallback(respondCall), _httpListener);
        if (context != null)
        {
            respondToRequest(context);
        }
    }

    static void saveData() {
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

    static void autoSaveTick() {
        Task.Delay(300000).ContinueWith((task) => { //save after 5 mins and recall
            saveData();
            autoSaveTick();
        });
    }

    static List<string> mediaProcesserJobs = new();

    static void mediaProcesser() { //thread function for processing media
        Console.WriteLine("mediaProcesser thread started!");
        while (!exit) {
            if (mediaProcesserJobs.Count > 0)
            try {
                string job = mediaProcesserJobs[0];
                mediaProcesserJobs.RemoveAt(0);
                Console.WriteLine(job);
                using (var image = new MagickImage("data/upload/" + job + ".file"))
                {
                    if (image.Width > thumbSize || image.Height > thumbSize) {
                        image.Resize(thumbSize, thumbSize);
                        image.Strip();
                    }
                    image.Quality = thumbQuality;
                    image.Write("data/upload/" + job + ".thumb");
                }
            }catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
            Thread.Sleep(100); //Sleep to save cpu
        }
    }

    static bool exit = false;

    static void Main(string[] args)
    {
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore //This is the main reason I was used null.
        };

        string HTTPport = "4268";
        string HTTPSport = "8443";

        if (args.Length > 0) { // Custom port
            HTTPport = args[0];
            if (args.Length > 1)
            { // Custom https port
                HTTPSport = args[1];
                if (args.Length > 2)
                {// federation url
                    Federation.thisServerURL = args[2];
                }
            }
        }

        Console.WriteLine("Pamukky V3 Server");
        //Create save folders
        Directory.CreateDirectory("data");
        Directory.CreateDirectory("data/auth");
        Directory.CreateDirectory("data/chat");
        Directory.CreateDirectory("data/upload");
        Directory.CreateDirectory("data/info");
        // Start the server
        _httpListener.Prefixes.Add("http://*:" + HTTPport + "/"); //http prefix
        _httpListener.Prefixes.Add("https://*:" + HTTPSport + "/"); //https prefix
        
        _httpListener.Start();
        Console.WriteLine("Server started. On ports " + HTTPport + " and " + HTTPSport);
        // Start responding for server
        _httpListener.BeginGetContext(new AsyncCallback(respondCall),_httpListener);
        Console.WriteLine("Type exit to exit, type save to save.");
        autoSaveTick(); // Start the autosave ticker
        new Thread(mediaProcesser).Start();

        // CLI
        while (!exit) {
            string readline = Console.ReadLine() ?? "";
            if (readline == "exit") {
                exit = true;
            }else if (readline == "save") {
                saveData();
            }
        }
        // After user wants to exit, save "cached" data
        saveData();
    }
}
