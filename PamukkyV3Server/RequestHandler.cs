using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PamukkyV3;

/// <summary>
/// Class that handles user requests with permission checks.
/// </summary>
public static class RequestHandler
{
    /// <summary>
    /// Server response type for errors and actions that doesn't have any return
    /// </summary>
    public class ServerResponse
    {
        public string status; //done
        public string? description;
        public string? code;

        public ServerResponse(string stat, string? scode = null, string? descript = null)
        { //for easier creation
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
    public class ActionReturn
    {
        public string res = "";
        public int statusCode = 200;
    }

    /// <summary>
    /// Does a user action.
    /// </summary>
    /// <param name="action">Type of the request</param>
    /// <param name="body">Body of the request.</param>
    /// <returns>Return of the action.</returns>
    static async Task<ActionReturn> DoAction(string action, string body)
    {
        string res = "";
        int statuscode = 200;
        if (action == "tos")
        {
            res = Pamukky.serverTOS;
        }
        else if (action == "signup")
        {
            var a = JsonConvert.DeserializeObject<LoginCredential>(body);
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

                            a.Password = Helpers.HashPassword(a.Password, uid);

                            string token = "";
                            do
                            {
                                token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                            }
                            while (Pamukky.loginCreds.ContainsKey(token));

                            a.userID = uid;
                            //a.token = token;

                            //Console.WriteLine(a.Password);
                            UserProfile up = new() { name = a.EMail.Split("@")[0].Split(".")[0] };
                            Directory.CreateDirectory("data/info/" + uid);
                            string astr = JsonConvert.SerializeObject(a);
                            //File.WriteAllText("data/auth/" + token, astr);
                            Pamukky.loginCreds[token] = a;
                            File.WriteAllText("data/auth/" + a.EMail, astr);
                            UserProfile.Create(uid, up);
                            UserChatsList? chats = await UserChatsList.Get(uid); //get new user's chats list
                            if (chats != null)
                            {
                                ChatItem savedmessages = new()
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
            var a = JsonConvert.DeserializeObject<LoginCredential>(body);
            if (a != null)
            {
                a.EMail = a.EMail.Trim();
                if (File.Exists("data/auth/" + a.EMail))
                {
                    LoginCredential? lc = JsonConvert.DeserializeObject<LoginCredential>(await File.ReadAllTextAsync("data/auth/" + a.EMail));
                    if (lc != null)
                    {
                        string uid = lc.userID;
                        a.Password = Helpers.HashPassword(a.Password, uid);
                        if (lc.Password == a.Password && lc.EMail == a.EMail)
                        {
                            //Console.WriteLine("Logging in...");
                            string token = "";
                            do
                            {
                                //Console.WriteLine("Generating token...");
                                token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                            }
                            while (Pamukky.loginCreds.ContainsKey(token));
                            //Console.WriteLine("Generated token");
                            Pamukky.loginCreds[token] = lc;
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
                    LoginCredential? lc = await Pamukky.GetLoginCred(a["token"]);
                    if (lc != null)
                    {
                        if (lc.Password == Helpers.HashPassword(a["oldpassword"], lc.userID))
                        {
                            /*string token = "";
                                *                   do
                                *                   {
                                *                       token =  Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=","").Replace("+","").Replace("/","");
                        }
                        while (Pamukky.loginCreds.ContainsKey(token));*/
                            //File.Delete("data/auth/" + lc.token);
                            //lc.token = token;
                            lc.Password = Helpers.HashPassword(a["password"].Trim(), lc.userID);
                            //File.WriteAllText("data/auth/" + token, astr);
                            File.WriteAllText("data/auth/" + lc.EMail, JsonConvert.SerializeObject(lc));
                            //Find other logins
                            var tokens = Pamukky.loginCreds.Where(lco => lco.Value.userID == lc.userID && lc != lco.Value);
                            foreach (var token in tokens)
                            {
                                //remove the logins.
                                Pamukky.loginCreds.Remove(token.Key, out _);
                            }
                            res = JsonConvert.SerializeObject(new ServerResponse("done"));
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
                Pamukky.loginCreds.Remove(a["token"], out _);
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
                    res = Pamukky.pamukProfile;
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
                    res = up.GetOnline();
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    UserProfile? user = await UserProfile.Get(uid);
                    if (user != null)
                    {
                        user.SetOnline();
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
        else if (action == "editprofile")
        { //User profile edit
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
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
                        if (!a.ContainsKey("bio"))
                        {
                            a["bio"] = "";
                        }
                        profile.picture = a["picture"];
                        profile.bio = a["bio"].Trim();
                        profile.Save();
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    List<ChatItem>? chats = await UserChatsList.Get(uid);
                    if (chats != null)
                    {
                        res = JsonConvert.SerializeObject(chats);
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    //Console.WriteLine(JsonConvert.SerializeObject(notifications));
                    var usernotifies = Notifications.Get(uid).GetNotifications(a["token"]);
                    if (a.ContainsKey("mode") && a["mode"] == "hold" && usernotifies.Count == 0) // Hold mode means that if there isn't any notifications, wait for one until a timeout.
                    {
                        int wait = 60; // How many seconds will this wait for notification to appear
                        while (usernotifies.Count == 0 && wait > 0)
                        {
                            await Task.Delay(1000);
                            --wait;
                        }
                    }
                    res = JsonConvert.SerializeObject(usernotifies);
                    usernotifies.Clear();
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
                string token = a["token"].ToString() ?? "";
                UpdateHooks updhooks = Updaters.Get(token);
                foreach (string? hid in (JArray)a["ids"])
                {
                    if (hid == null) continue;
                    if (hid.Contains(":"))
                    {
                        string[] split = hid.Split(":", 2);
                        string type = split[0];
                        string id = split[1];
                        switch (type)
                        {
                            case "chat":
                                Chat? chat = await Chat.GetChat(id);
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
                    else
                    {
                        switch (hid)
                        {
                            case "chatslist":
                                UserChatsList? chatsList = await UserChatsList.Get(await Pamukky.GetUIDFromToken(token) ?? "");
                                if (chatsList != null)
                                {
                                    updhooks.AddHook(chatsList);
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    UserConfig? userconfig = await UserConfig.Get(uid);
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
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("state"))
            {
                string? uid = await Pamukky.GetUIDFromToken((a["token"] ?? "").ToString() ?? "");
                if (uid != null)
                {
                    UserConfig? userconfig = await UserConfig.Get(uid);
                    if (userconfig != null)
                    {
                        string chatid = (a["chatid"] ?? "").ToString() ?? "";
                        if (File.Exists("data/chat/" + chatid + "/chat"))
                        {
                            if (a["state"] == "muted" || a["state"] == "tagsOnly")
                            {
                                userconfig.mutedChats[chatid] = new MutedChatData() { allowTags = a["state"] == "tagsOnly" };
                            }
                            else
                            {
                                userconfig.mutedChats.Remove(chatid, out _);
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
                string? uidu = await Pamukky.GetUIDFromToken(a["token"]);
                string? uidb = await Pamukky.GetUIDFromToken(a["email"], false);
                if (uidu != null && uidb != null)
                {
                    UserChatsList? chatsb = await UserChatsList.Get(uidb);
                    UserChatsList? chatsu = await UserChatsList.Get(uidu);
                    if (chatsu != null && chatsb != null)
                    {
                        string chatid = uidu + "-" + uidb;
                        ChatItem u = new()
                        {
                            user = uidb,
                            type = "user",
                            chatid = chatid
                        };
                        ChatItem b = new()
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.CanDo(uid, Chat.ChatAction.Read))
                        {
                            int page = a.ContainsKey("page") ? int.Parse(a["page"]) : 0;
                            string prefix = "#" + (page * 48) + "-#" + ((page + 1) * 48);
                            res = JsonConvert.SerializeObject(chat.GetMessages(prefix).FormatAll());
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.CanDo(uid, Chat.ChatAction.Read))
                        {
                            res = JsonConvert.SerializeObject(chat.GetMessages(a["prefix"]).FormatAll());
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.CanDo(uid, Chat.ChatAction.Read))
                        {
                            res = JsonConvert.SerializeObject(chat.GetPinnedMessages().FormatAll());
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
                    string? uid = await Pamukky.GetUIDFromToken(a["token"].ToString() ?? "");
                    if (uid != null)
                    {
                        Chat? chat = await Chat.GetChat(a["chatid"].ToString() ?? "");
                        if (chat != null)
                        {
                            if (chat.CanDo(uid, Chat.ChatAction.Send))
                            {
                                ChatMessage msg = new()
                                {
                                    senderUID = uid,
                                    content = (a["content"].ToString() ?? "").Trim(),
                                    replyMessageID = a.ContainsKey("replymessageid") ? a["replymessageid"].ToString() : null,
                                    files = files
                                };
                                chat.SendMessage(msg);
                                var userstatus = UserStatus.Get(uid);
                                if (userstatus != null)
                                {
                                    userstatus.SetTyping(null);
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
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("messageids"))
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        if (a["messageids"] is JArray)
                        {
                            var messages = (JArray)a["messageids"];
                            foreach (object msg in messages)
                            {
                                string? msgid = msg.ToString() ?? "";
                                if (chat.CanDo(uid, Chat.ChatAction.Delete, msgid))
                                {
                                    //if (chat.ContainsKey(msgid)) {
                                    chat.DeleteMessage(msgid);
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
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("messageids"))
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        if (a["messageids"] is JArray)
                        {
                            var messages = (JArray)a["messageids"];
                            foreach (object msg in messages)
                            {
                                string? msgid = msg.ToString() ?? "";
                                if (chat.CanDo(uid, Chat.ChatAction.Pin, msgid))
                                {
                                    bool pinned = chat.PinMessage(msgid);
                                    if (chat.CanDo(uid, Chat.ChatAction.Send))
                                    {
                                        ChatMessage message = new()
                                        {
                                            senderUID = "0",
                                            content = (pinned ? "" : "UN") + "PINNEDMESSAGE|" + uid + "|" + msgid
                                        };
                                        chat.SendMessage(message);
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
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("messageid") && a.ContainsKey("reaction") && (a["reaction"].ToString() ?? "") != "")
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        string? message = a["messageid"].ToString() ?? "";
                        string? reaction = a["reaction"].ToString() ?? "";
                        if (chat.CanDo(uid, Chat.ChatAction.React, message))
                        {
                            //if (chat.ContainsKey(msgid)) {
                            res = JsonConvert.SerializeObject(chat.ReactMessage(message, uid, reaction));
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
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("messageids"))
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        if (chat.CanDo(uid, Chat.ChatAction.Read))
                        {
                            if (a["messageids"] is JArray)
                            {
                                var messages = (JArray)a["messageids"];
                                foreach (object msg in messages)
                                {
                                    string? msgid = msg.ToString() ?? "";
                                    if (chat.ContainsKey(msgid))
                                    {
                                        Chat? uchat = await Chat.GetChat(uid + "-" + uid);
                                        if (uchat != null)
                                        {
                                            ChatMessage message = new()
                                            {
                                                senderUID = chat[msgid].senderUID,
                                                content = chat[msgid].content,
                                                files = chat[msgid].files
                                            };
                                            uchat.SendMessage(message, false);

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
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("messageids") && a.ContainsKey("chatidstosend"))
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"].ToString() ?? "");
                    if (chat != null)
                    {
                        if (chat.CanDo(uid, Chat.ChatAction.Read))
                        {
                            if (a["messageids"] is JArray)
                            {
                                var messages = (JArray)a["messageids"];
                                foreach (object msg in messages)
                                {
                                    string? msgid = msg.ToString() ?? "";
                                    if (chat.ContainsKey(msgid))
                                    {
                                        if (a["chatidstosend"] is JArray)
                                        {
                                            var chats = (JArray)a["chatidstosend"];
                                            foreach (object chatid in chats)
                                            {
                                                Chat? uchat = await Chat.GetChat(chatid.ToString() ?? "");
                                                if (uchat != null)
                                                {
                                                    if (uchat.CanDo(uid, Chat.ChatAction.Send))
                                                    {
                                                        ChatMessage message = new()
                                                        {
                                                            forwardedFromUID = chat[msgid].senderUID,
                                                            senderUID = uid,
                                                            content = chat[msgid].content,
                                                            files = chat[msgid].files
                                                        };
                                                        uchat.SendMessage(message);
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
                    string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                    if (uid != null)
                    {
                        Chat? chat = await Chat.GetChat(a["id"]);
                        if (chat != null)
                        {
                            if (chat.CanDo(uid, Chat.ChatAction.Read))
                            { //Check if user can even "read" it at all
                                string requestMode = "normal";
                                if (a.ContainsKey("mode"))
                                {
                                    requestMode = a["mode"];
                                }

                                if (requestMode == "updater") // Updater mode will wait for a new message. "since" shouldn't work here.
                                {
                                    res = JsonConvert.SerializeObject(await chat.WaitForUpdates());
                                }
                                else
                                {
                                    if (a.ContainsKey("since"))
                                    {
                                        res = JsonConvert.SerializeObject(chat.GetUpdates(long.Parse(a["since"])));
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.CanDo(uid, Chat.ChatAction.Send))
                        { //Ofc, if the user has the permission to send at the chat
                            var userstatus = UserStatus.Get(uid);
                            if (userstatus != null)
                            {
                                userstatus.SetTyping(chat.chatID);
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = await Chat.GetChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.CanDo(uid, Chat.ChatAction.Read))
                        { //Ofc, if the user has the permission to read the chat
                            string requestMode = "normal";
                            if (a.ContainsKey("mode"))
                            {
                                requestMode = a["mode"];
                            }
                            if (requestMode == "updater")
                                res = JsonConvert.SerializeObject(await chat.WaitForTypingUpdates());
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
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
                            creatorUID = uid,
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
                        await g.AddUser(uid, "Owner");
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
                string uid = await Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.groupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(new GroupInfo()
                        {
                            name = gp.name,
                            info = gp.info,
                            picture = gp.picture,
                            isPublic = gp.isPublic
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
                string uid = await Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                if (a["id"] == "0")
                {
                    res = Pamukky.pamukProfile;
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
                            if (gp.CanDo(uid, Group.groupAction.Read))
                            {
                                res = JsonConvert.SerializeObject(new GroupInfo()
                                {
                                    name = gp.name,
                                    info = gp.info,
                                    picture = gp.picture,
                                    isPublic = gp.isPublic
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
        else if (action == "getgroupmembers")
        { //Gets members list in json format.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                string uid = await Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.groupAction.Read))
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
                string uid = await Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.groupAction.Read))
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
        else if (action == "getgroupmemberscount")
        { //returns group member count as string. like "5"
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                Group? gp = await Group.Get(a["groupid"]);
                string uid = await Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.groupAction.Read))
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
                string uid = await Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.groupAction.Read))
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        var role = gp.GetUserRole(uid);
                        if (role != null)
                        {
                            res = JsonConvert.SerializeObject(role);
                        }
                        else
                        {
                            if (gp.isPublic)
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (await gp.AddUser(uid))
                        {
                            res = JsonConvert.SerializeObject(new ServerResponse("done"));
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        Chat? chat = await Chat.GetChat(gp.groupID);
                        if (await gp.RemoveUser(uid))
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
        else if (action == "kickmember")
        { //Kicks a user from the group. They can rejoin.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("userid"))
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.groupAction.Kick, a["userid"] ?? ""))
                        {
                            if (await gp.RemoveUser(a["userid"] ?? ""))
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
        else if (action == "banmember")
        { //bans a user from the group. they can't join until they are unbanned.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("userid"))
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.groupAction.Ban, a["userid"] ?? ""))
                        {
                            if (await gp.BanUser(a["userid"] ?? ""))
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
        else if (action == "unbanmember")
        { //Unbans a user, they can rejoin.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("userid"))
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.groupAction.Ban, a["userid"] ?? ""))
                        {
                            gp.UnbanUser(a["userid"] ?? "");
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
                string? uid = await Pamukky.GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"].ToString() ?? "");
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.groupAction.EditGroup))
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
                            if (a.ContainsKey("ispublic") && a["ispublic"] is bool)
                            {
                                gp.isPublic = (bool)a["ispublic"];
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
                            res = JsonConvert.SerializeObject(new ServerResponse("done"));
                            gp.Save();
                            Chat? chat = await Chat.GetChat(gp.groupID);
                            if (chat != null)
                            {
                                if (chat.CanDo(uid, Chat.ChatAction.Send))
                                {
                                    ChatMessage message = new()
                                    {
                                        senderUID = "0",
                                        content = "EDITGROUP|" + uid
                                    };
                                    chat.SendMessage(message);
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
        else if (action == "editmember")
        { //Edits role of user in the group.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("userid") && a.ContainsKey("role"))
            {
                string? uid = await Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.groupAction.EditUser, a["userid"]))
                        {
                            if (gp.members.ContainsKey(a["userid"]))
                            {
                                if (gp.roles.ContainsKey(a["role"]))
                                {
                                    var crole = gp.roles[a["role"]];
                                    var curole = gp.GetUserRole(uid);
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
        ActionReturn ret = new() { res = res, statusCode = statuscode };
        return ret;
    }

    /// <summary>
    /// Global request class
    /// </summary>
    public class Request
    {
        public string RequestName;
        public string RequestMethod;
        public string Input;

        public Request(string name, string input, string method = "Unknown")
        {
            RequestName = name.ToLower();
            Input = input;
            RequestMethod = method.ToLower();
        }
    }

    /// <summary>
    /// Responds to a request.
    /// </summary>
    /// <param name="request">Request to respond to</param>
    /// <exception cref="Exception">Throws if something that shouldn't happen, like failing to parse a JSON that should be parseable.</exception>
    public static async Task<ActionReturn> respondToRequest(Request request)
    {
        string res = "";
        int statuscode = 200;

        //Console.WriteLine(request.RequestName + " " + request.Input);
        try
        {
            if (request.RequestName == "multi")
            {
                Dictionary<string, string>? actions = JsonConvert.DeserializeObject<Dictionary<string, string>>(request.Input);
                if (actions != null)
                {
                    ConcurrentDictionary<string, ActionReturn> responses = new();
                    foreach (var subrequest in actions)
                    {
                        ActionReturn actionreturn = await DoAction(subrequest.Key.Split("|")[0], subrequest.Value);
                        responses[subrequest.Key] = actionreturn;
                    }
                    res = JsonConvert.SerializeObject(responses);
                }
            }
            #region Federation
            else if (request.RequestName == "federationrequest")
            {
                FederationRequest? fedrequest = JsonConvert.DeserializeObject<FederationRequest>(request.Input);
                if (fedrequest != null)
                {
                    if (fedrequest.serverurl != null)
                    {
                        if (fedrequest.serverurl != Federation.thisServerURL)
                        {
                            try
                            {
                                var httpTask = await Federation.GetHttpClient().GetAsync(fedrequest.serverurl);
                                Console.WriteLine("federationrequest/pingpong " + await httpTask.Content.ReadAsStringAsync());
                                // Valid, allow to federate
                                string id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                                if (Federation.federations.ContainsKey(fedrequest.serverurl))
                                {
                                    Federation.federations[fedrequest.serverurl].FederationRequestReconnected(id);
                                }
                                else
                                {
                                    Federation fed = new(fedrequest.serverurl, id);
                                    Federation.federations[fedrequest.serverurl] = fed;
                                }
                                statuscode = 200;
                                res = JsonConvert.SerializeObject(Federation.federations[fedrequest.serverurl]); //return info
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
            else if (request.RequestName == "federationgetgroup")
            {
                Dictionary<string, string>? fedrequest = JsonConvert.DeserializeObject<Dictionary<string, string>>(request.Input);
                if (fedrequest != null)
                {
                    Federation? fed = Federation.GetFromRequestStrDict(fedrequest);
                    if (fed != null)
                    {
                        if (fedrequest.ContainsKey("groupid"))
                        {
                            string id = fedrequest["groupid"].Split("@")[0];
                            Group? gp = await Group.Get(id);
                            if (gp != null)
                            {
                                bool showfullinfo = false;
                                if (gp.isPublic)
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
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                }
            }
            else if (request.RequestName == "federationjoingroup")
            {
                Dictionary<string, string>? fedrequest = JsonConvert.DeserializeObject<Dictionary<string, string>>(request.Input);
                if (fedrequest != null)
                {
                    Federation? fed = Federation.GetFromRequestStrDict(fedrequest);
                    if (fed != null)
                    {
                        if (fedrequest.ContainsKey("groupid"))
                        {
                            if (fedrequest.ContainsKey("userid"))
                            {
                                string id = fedrequest["groupid"].Split("@")[0];
                                Group? gp = await Group.Get(id);
                                if (gp != null)
                                {
                                    bool stat = await gp.AddUser(fedrequest["userid"] + "@" + fed.serverURL);
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
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                }
            }
            else if (request.RequestName == "federationleavegroup")
            {
                Dictionary<string, string>? fedrequest = JsonConvert.DeserializeObject<Dictionary<string, string>>(request.Input);
                if (fedrequest != null)
                {
                    Federation? fed = Federation.GetFromRequestStrDict(fedrequest);
                    if (fed != null)
                    {
                        if (fedrequest.ContainsKey("groupid"))
                        {
                            if (fedrequest.ContainsKey("userid"))
                            {
                                string id = fedrequest["groupid"].Split("@")[0];
                                Group? gp = await Group.Get(id);
                                if (gp != null)
                                {
                                    bool stat = await gp.RemoveUser(fedrequest["userid"] + "@" + fed.serverURL);
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
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                }
            }
            else if (request.RequestName == "federationgetchat")
            {
                Dictionary<string, string>? fedrequest = JsonConvert.DeserializeObject<Dictionary<string, string>>(request.Input);
                if (fedrequest != null)
                {
                    Federation? fed = Federation.GetFromRequestStrDict(fedrequest);
                    if (fed != null)
                    {
                        if (fedrequest.ContainsKey("chatid"))
                        {
                            string id = fedrequest["chatid"].Split("@")[0];
                            Chat? chat = await Chat.GetChat(id);
                            if (chat != null)
                            {
                                bool showfullinfo = false;
                                if (chat.group.isPublic)
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
                                    fed.cachedUpdates.AddHook(chat);
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
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."));
                }
            }
            else if (request.RequestName == "federationrecieveupdates")
            {
                UpdateRecieveRequest? fedrequest = JsonConvert.DeserializeObject<UpdateRecieveRequest>(request.Input);
                if (fedrequest != null)
                {
                    Federation? fed = Federation.GetFromRequestURR(fedrequest);
                    if (fed != null)
                    {
                        if (fedrequest.updates != null)
                        {
                            foreach (var updatehook in fedrequest.updates)
                            {
                                string type = updatehook.Key.Split(":")[0];
                                string target = updatehook.Key.Split(":")[1];
                                if (type == "chat")
                                {
                                    string id = target.Split("@")[0];
                                    Console.WriteLine(target);
                                    Chat? chat = await Chat.GetChat(id + "@" + fed.serverURL);
                                    if (chat == null)
                                    {
                                        chat = await Chat.GetChat(id);
                                    }
                                    if (chat != null)
                                    {
                                        var updates = updatehook.Value;
                                        foreach (var upd in updates)
                                        {
                                            if (upd.Value == null) continue;
                                            if (upd.Key.StartsWith("TYPING|"))
                                            {
                                                // Get the typing user and fix id
                                                string user = fed.FixUserID(upd.Key.Split("|")[1]);
                                                if ((bool)upd.Value) // Typing
                                                {
                                                    chat.SetTyping(user);
                                                }
                                                else // Not typing
                                                {
                                                    chat.RemoveTyping(user);
                                                }
                                            }
                                            else
                                            {
                                                var update = (JObject)upd.Value;
                                                string eventn = (update["event"] ?? "").ToString() ?? "";
                                                string mid = (update["id"] ?? "").ToString() ?? "";
                                                if (eventn == "NEWMESSAGE")
                                                {
                                                    if ((update["sender"] ?? "").ToString() == "0")
                                                    {
                                                        continue; //Don't allow Pamuk messages from other federations, because they are probably echoes.
                                                    }

                                                    // IDK how else to do this...
                                                    string? forwardedFrom = null;
                                                    if (update.ContainsKey("forwardedfrom"))
                                                    {
                                                        if (update["forwardedfrom"] != null)
                                                        {
                                                            forwardedFrom = (update["forwardedfrom"] ?? "").ToString();
                                                            if (forwardedFrom == "")
                                                            {
                                                                forwardedFrom = null;
                                                            }
                                                        }
                                                    }

                                                    ChatMessage msg = new ChatMessage()
                                                    {
                                                        senderUID = (update["sender"] ?? "").ToString() ?? "",
                                                        content = (update["content"] ?? "").ToString() ?? "",
                                                        sendTime = (DateTime?)update["time"] ?? DateTime.Now,
                                                        replyMessageID = update.ContainsKey("replymsgid") ? update["replymsgid"] == null ? null : (update["replymsgid"] ?? "").ToString() : null,
                                                        forwardedFromUID = forwardedFrom,
                                                        files = update.ContainsKey("files") && (update["files"] is JArray) ? ((JArray?)update["files"] ?? new JArray()).ToObject<List<string>>() : null,
                                                        isPinned = update["pinned"] != null ? (bool?)update["pinned"] ?? false : false,
                                                        reactions = update.ContainsKey("reactions") && (update["reactions"] is JObject) ? ((JObject?)update["reactions"] ?? new JObject()).ToObject<MessageReactions>() ?? new MessageReactions() : new MessageReactions(),
                                                    };
                                                    fed.FixMessage(msg);
                                                    chat.SendMessage(msg, true, mid);
                                                }
                                                else if (eventn.StartsWith("REACT"))
                                                {
                                                    if (update.ContainsKey("id") && update.ContainsKey("userID") && update.ContainsKey("reaction"))
                                                    {
                                                        if (update["id"] != null && update["userID"] != null && update["reaction"] != null)
                                                        {
                                                            Console.WriteLine("c");
                                                            chat.ReactMessage(mid, fed.FixUserID((update["userID"] ?? "").ToString() ?? ""), (update["reaction"] ?? "").ToString() ?? "", eventn == "REACT");
                                                        }
                                                    }
                                                }
                                                else if (eventn == "DELETED")
                                                {
                                                    chat.DeleteMessage(mid);
                                                }
                                                else if (eventn == "PINNED")
                                                {
                                                    chat.PinMessage(mid, true);
                                                }
                                                else if (eventn == "UNPINNED")
                                                {
                                                    chat.PinMessage(mid, false);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        statuscode = 404;
                                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOCHAT", "Chat not found."));
                                    }
                                }
                            }
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "NOCID", "Request doesn't contain a chat ID."));
                        }
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
                return await DoAction(request.RequestName, request.Input);
            }
        }
        catch (Exception e)
        {
            statuscode = 500;
            res = e.ToString();
            Console.WriteLine(e.ToString());
        }

        return new ActionReturn()
        {
            statusCode = statuscode,
            res = res
        };
    }
}