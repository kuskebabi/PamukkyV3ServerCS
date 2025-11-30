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
    /// Return for doAction function.
    /// </summary>
    public class ActionReturn
    {
        public string res = "";
        public int statusCode = 200;
    }

    public class MessageSendRequest
    {
        public string? token = null;
        public string? chatid = null;
        public string? content = null;
        public string? replymessageid = null;
        public List<string>? mentionuids = null;
        public List<string>? files = null;
    }

    public class MessageEditRequest : MessageSendRequest
    {
        public string? messageid = null;
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
        else if (action == "pamukky")
        {
            res = JsonConvert.SerializeObject(Pamukky.PublicServerData.Get());
        }
        else if (action == "signup")
        {
            if (!Pamukky.config.allowSignUps)
            {
                statuscode = 403;
                res = JsonConvert.SerializeObject(new ServerResponse("error", "ADENIED", "Not allowed"));
            }else {
                var a = JsonConvert.DeserializeObject<UserLoginRequest>(body);
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

                                UserLogin loginCredentials = new()
                                {
                                    EMail = a.EMail,
                                    Password = Helpers.HashPassword(a.Password, uid),
                                    userID = uid
                                };

                                File.WriteAllText("data/auth/" + a.EMail, JsonConvert.SerializeObject(loginCredentials));

                                UserProfile up = new() { name = a.EMail.Split("@")[0].Split(".")[0] };
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
                                //Done, now login
                                var session = UserSession.CreateSession(uid);

                                res = JsonConvert.SerializeObject(session);
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
        }
        else if (action == "login")
        {
            var request = JsonConvert.DeserializeObject<UserLoginRequest>(body);
            if (request != null)
            {
                var session = await UserLogin.Login(request);

                if (session != null)
                {
                    res = JsonConvert.SerializeObject(session);
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
        else if (action == "changepassword")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("password") && a.ContainsKey("oldpassword") && a.ContainsKey("email"))
            {
                if (a["password"].Trim().Length >= 6)
                {
                    UserLogin? lc = await UserLogin.Get(a["email"]);
                    if (lc != null)
                    {
                        var session = UserSession.GetSession(a["token"]);
                        if (session != null && session.userID == lc.userID)
                        {
                            if (lc.Password == Helpers.HashPassword(a["oldpassword"], lc.userID))
                            {
                                lc.Password = Helpers.HashPassword(a["password"].Trim(), lc.userID);
                                File.WriteAllText("data/auth/" + lc.EMail, JsonConvert.SerializeObject(lc));
                                //Find other logins
                                var tokens = UserSession.UserSessions.Where(osession => osession.Value.userID == lc.userID && session != osession.Value);
                                foreach (var token in tokens)
                                {
                                    //remove the logins.
                                    UserSession.UserSessions.Remove(token.Key, out _);
                                }
                                res = JsonConvert.SerializeObject(new ServerResponse("done"));
                            }
                            else
                            {
                                statuscode = 403;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "NOPASS", "Old password is wrong."));
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
        else if (action == "getsessioninfo")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                var session = UserSession.GetSession(a["token"]);
                if (session != null)
                {
                    res = JsonConvert.SerializeObject(session);
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
        else if (action == "logout")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                var session = UserSession.GetSession(a["token"]);
                if (session != null)
                {
                    session.LogOut();
                }

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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
        else if (action == "publictag")
        { // set or get tag
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("tag"))
            {
                if (a.ContainsKey("token") && a.ContainsKey("target"))
                {
                    string? uid = Pamukky.GetUIDFromToken(a["token"]);
                    if (uid != null)
                    {
                        if (!await PublicTag.IsTagTaken(a["tag"], uid))
                        {
                            if (await PublicTag.SetTag(uid, a["tag"], a["target"]))
                            {
                                res = JsonConvert.SerializeObject(new ServerResponse("done"));
                            }
                            else
                            {
                                statuscode = 409;
                                res = JsonConvert.SerializeObject(new ServerResponse("error", "TAGERROR", "Tag is not in correct format."));
                            }
                        }
                        else
                        {
                            statuscode = 409;
                            res = JsonConvert.SerializeObject(new ServerResponse("error", "TAGTAKEN", "Tag already taken."));
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
                    res = PublicTag.GetTagTarget(a["tag"]) ?? "";
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                            case "group":
                                Group? group = await Group.Get(id);
                                if (group != null)
                                {
                                    updhooks.AddHook(group);
                                }
                                else
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group doesn't exist."));
                                }
                                break;
                        }
                    }
                    else
                    {
                        switch (hid)
                        {
                            case "chatslist":
                                UserChatsList? chatsList = await UserChatsList.Get(Pamukky.GetUIDFromToken(token) ?? "");
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken((a["token"]));
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
                string? uida = Pamukky.GetUIDFromToken(a["token"]);
                string? uidb = (await UserLogin.Get(a["email"]))?.userID;
                if (uida != null && uidb != null)
                {
                    UserChatsList? chatsb = await UserChatsList.Get(uidb);
                    UserChatsList? chatsu = await UserChatsList.Get(uida);
                    if (chatsu != null && chatsb != null)
                    {
                        string chatid = uida + "-" + uidb;
                        ChatItem u = new()
                        {
                            user = uidb,
                            type = "user",
                            chatid = chatid
                        };
                        ChatItem b = new()
                        {
                            user = uida,
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
            var a = JsonConvert.DeserializeObject<MessageSendRequest>(body);
            if (a != null)
            {
                if (a.chatid != null && ((a.content != null && a.content.Length != 0) || (a.files != null && a.files.Count > 0)))
                {
                    string? uid = Pamukky.GetUIDFromToken(a.token);
                    if (uid != null)
                    {
                        Chat? chat = await Chat.GetChat(a.chatid);
                        if (chat != null)
                        {
                            if (chat.CanDo(uid, Chat.ChatAction.Send))
                            {
                                ChatMessage msg = new()
                                {
                                    senderUID = uid,
                                    content = a.content ?? "",
                                    replyMessageID = a.replymessageid,
                                    files = a.files
                                };

                                if (a.mentionuids != null)
                                {
                                    msg.mentionUIDs = a.mentionuids;
                                }
                                else
                                {
                                    msg.mentionUIDs = chat.GetMessageMentions(msg);
                                }

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
        else if (action == "editmessage")
        {
            var a = JsonConvert.DeserializeObject<MessageEditRequest>(body);
            if (a != null)
            {
                if (a.chatid != null && a.messageid != null && a.content != null && a.content.Length != 0)
                {
                    string? uid = Pamukky.GetUIDFromToken(a.token);
                    if (uid != null)
                    {
                        Chat? chat = await Chat.GetChat(a.chatid);
                        if (chat != null)
                        {
                            if (chat.CanDo(uid, Chat.ChatAction.Edit, a.messageid))
                            {
                                chat.EditMessage(a.messageid, a.content ?? "");
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
        else if (action == "readmessage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("messageids"))
            {
                string? uid = Pamukky.GetUIDFromToken(a["token"].ToString());
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
                                if (chat.CanDo(uid, Chat.ChatAction.Send))
                                {
                                    chat.ReadMessage(msgid, uid);
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
        else if (action == "deletemessage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("messageids"))
            {
                string? uid = Pamukky.GetUIDFromToken(a["token"].ToString());
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
                                    chat.DeleteMessage(msgid);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"].ToString());
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
                                    chat.PinMessage(msgid, null, uid);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"].ToString());
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
                string? uid = Pamukky.GetUIDFromToken(a["token"].ToString());
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
                string? uid = Pamukky.GetUIDFromToken(a["token"].ToString());
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
                    string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string uid = Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.GroupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(GroupInfo.Generate(gp));
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
                string uid = Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";

                object? target = await Pamukky.GetTargetFromID(a["id"]);
                if (target is UserProfile)
                {
                    res = JsonConvert.SerializeObject(target);
                }
                else if (target is Group)
                {
                    Group? gp = target as Group;
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.GroupAction.Read))
                        {
                            res = JsonConvert.SerializeObject(GroupInfo.Generate(gp));
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
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOTFOUND", "Not found."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new ServerResponse("error", "NOTFOUND", "Not found."));
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
                string uid = Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.GroupAction.Read))
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
                string uid = Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.GroupAction.Read))
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
                string uid = Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.GroupAction.Read))
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
                string uid = Pamukky.GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = await Group.Get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.CanDo(uid, Group.GroupAction.Read))
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.GroupAction.Kick, a["userid"] ?? ""))
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.GroupAction.Ban, a["userid"] ?? ""))
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.GroupAction.Ban, a["userid"] ?? ""))
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
                string? uid = Pamukky.GetUIDFromToken(a["token"].ToString());
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"].ToString() ?? "");
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.GroupAction.EditGroup))
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
                                var roles = ((JObject)a["roles"]).ToObject<Dictionary<string, GroupRole>>() ?? gp.roles;

                                if (gp.validateNewRoles(roles))
                                {
                                    gp.roles = roles;
                                    gp.notifyEdit(Group.EditType.WithRoles, uid);
                                }
                                else
                                {
                                    gp.notifyEdit(Group.EditType.Basic, uid);
                                }
                            }
                            else
                            {
                                gp.notifyEdit(Group.EditType.Basic, uid);
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
                string? uid = Pamukky.GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = await Group.Get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.CanDo(uid, Group.GroupAction.EditUser, a["userid"]))
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
                                            gp.SetUserRole(a["userid"], a["role"]);
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

                    return new ActionReturn()
                    {
                        statusCode = 200,
                        res = JsonConvert.SerializeObject(responses)
                    };
                }
            }
            #region Federation
            else if (request.RequestName == "federationrequest")
            {
                FederationRequest? fedrequest = JsonConvert.DeserializeObject<FederationRequest>(request.Input);
                if (fedrequest == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."))
                    };
                }

                if (fedrequest.serverurl == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOSERVERURL", "Request doesn't contain a serverurl."))
                    };
                }

                fedrequest.serverurl = fedrequest.serverurl.ToLower();

                if (fedrequest.serverurl == Federation.thisServerURL)
                {
                    return new ActionReturn()
                    {
                        statusCode = 418,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ITSME", "Hello me!"))
                    };

                }

                try
                {
                    var httpTask = await Federation.GetHttpClient().GetAsync(new Uri(new Uri(fedrequest.serverurl), "pamukky"));
                    var response = await httpTask.Content.ReadAsStringAsync();
                    Console.WriteLine("pamukky " + response);

                    if (httpTask.StatusCode != System.Net.HttpStatusCode.OK) return new ActionReturn()
                    {
                        statusCode = 500,
                        res = "Server is invalid."
                    };

                    var info = JsonConvert.DeserializeObject<ConnectionManager.ServerInfo>(response);
                    if (info == null || !info.isCompatiable()) return new ActionReturn()
                    {
                        statusCode = 500,
                        res = "Server is invalid."
                    };

                    if (info.publicName.Trim().Length != 0)
                    {
                        var url = await ConnectionManager.FindActualServerURL(info.publicName);

                        if (url != fedrequest.serverurl)
                        {
                            info.publicName = ConnectionManager.CreateFakeServerName(fedrequest.serverurl);
                        }
                    }
                    else
                    {
                        info.publicName = ConnectionManager.CreateFakeServerName(fedrequest.serverurl);
                    }

                    // Valid, allow to federate
                    if (Federation.federations.ContainsKey(fedrequest.serverurl))
                    {
                        Federation.federations[fedrequest.serverurl].FederationRequestReconnected();
                    }
                    else
                    {
                        string id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                        Federation fed = new(fedrequest.serverurl, id, info.publicName);
                        fed.startTick();
                        Federation.federations[fedrequest.serverurl] = fed;
                    }

                    return new ActionReturn()
                    {
                        statusCode = 200,
                        res = JsonConvert.SerializeObject(Federation.federations[fedrequest.serverurl])
                    };

                }
                catch (Exception e)
                {
                    return new ActionReturn()
                    {
                        statusCode = 404,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "ERROR", "Couldn't connect to remote. " + e.Message))
                    };
                }
            }
            else if (request.RequestName == "federationgetuser")
            {
                Dictionary<string, string>? fedrequest = JsonConvert.DeserializeObject<Dictionary<string, string>>(request.Input);
                if (fedrequest == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."))
                    };
                }

                Federation? fed = Federation.GetFromRequestStrDict(fedrequest);
                if (fed == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 404,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFED", "Federation not found."))
                    };
                }

                if (fed.cachedUpdates == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 500,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "CACHEERR", "Federation cache not generated."))
                    };
                }

                if (!fedrequest.ContainsKey("userid"))
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOID", "Request doesn't contain a ID."))
                    };
                }

                string id = fedrequest["userid"].Split("@")[0];
                UserProfile? profile = await UserProfile.Get(id);
                if (profile == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 404,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUSER", "User not found."))
                    };
                }

                fed.cachedUpdates.AddHook(profile);
                return new ActionReturn()
                {
                    statusCode = 200,
                    res = JsonConvert.SerializeObject(profile)
                };
            }
            else if (request.RequestName == "federationgetgroup")
            {
                Dictionary<string, string>? fedrequest = JsonConvert.DeserializeObject<Dictionary<string, string>>(request.Input);
                if (fedrequest == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."))
                    };
                }

                Federation? fed = Federation.GetFromRequestStrDict(fedrequest);
                if (fed == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 404,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFED", "Federation not found."))
                    };
                }

                if (fed.cachedUpdates == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 500,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "CACHEERR", "Federation cache not generated."))
                    };
                }

                if (!fedrequest.ContainsKey("groupid"))
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOID", "Request doesn't contain a ID."))
                    };
                }

                string id = fedrequest["groupid"].Split("@")[0];
                Group? group = await Group.Get(id);
                if (group == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 404,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOGROUP", "Group not found."))
                    };
                }

                bool showfullinfo = false;
                if (group.isPublic)
                {
                    showfullinfo = true;
                }
                else
                {
                    foreach (string member in group.members.Keys)
                    {
                        if (member.Contains("@"))
                        {
                            string server = member.Split("@")[1];
                            if (server == fed.publicName)
                            {
                                showfullinfo = true;
                                break;
                            }
                        }
                    }
                }

                if (showfullinfo)
                {
                    fed.cachedUpdates.AddHook(group);
                    return new ActionReturn()
                    {
                        statusCode = 200,
                        res = JsonConvert.SerializeObject(group)
                    };
                }
                else
                {
                    return new ActionReturn()
                    {
                        statusCode = 200,
                        res = JsonConvert.SerializeObject(new ServerResponse("exists"))
                    };
                }
            }
            else if (request.RequestName == "federationgetchat")
            {
                Dictionary<string, string>? fedrequest = JsonConvert.DeserializeObject<Dictionary<string, string>>(request.Input);

                if (fedrequest == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."))
                    };
                }

                Federation? fed = Federation.GetFromRequestStrDict(fedrequest);
                if (fed == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 404,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFED", "Federation not found."))
                    };
                }

                if (fed.cachedUpdates == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 500,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "CACHEERR", "Federation cache not generated."))
                    };
                }

                if (!fedrequest.ContainsKey("chatid"))
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOID", "Request doesn't contain a ID."))
                    };
                }

                string id = fedrequest["chatid"].Split("@")[0];
                Chat? chat = await Chat.GetChat(id);
                if (chat == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 404,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOCHAT", "Chat not found."))
                    };
                }

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
                            if (server == fed.publicName)
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
                    return new ActionReturn()
                    {
                        statusCode = 200,
                        res = JsonConvert.SerializeObject(chat)
                    };
                }
                else
                {
                    return new ActionReturn()
                    {
                        statusCode = 403,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOPERM", "Can't read chat."))
                    };
                }

            }
            else if (request.RequestName == "federationrecieveupdates")
            {
                Console.WriteLine(request.Input);
                UpdateRecieveRequest? fedrequest = JsonConvert.DeserializeObject<UpdateRecieveRequest>(request.Input);
                if (fedrequest == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "INVALID", "Couldn't parse request."))
                    };
                }

                Federation? fed = Federation.GetFromRequestURR(fedrequest);
                if (fed == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 404,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOFED", "Federation not found."))
                    };
                }

                if (fedrequest.updates == null)
                {
                    return new ActionReturn()
                    {
                        statusCode = 411,
                        res = JsonConvert.SerializeObject(new ServerResponse("error", "NOUPDATES", "Request doesn't contain updates."))
                    };
                }

                foreach (var updatehook in fedrequest.updates)
                {
                    string type = updatehook.Key.Split(":")[0];
                    string target = updatehook.Key.Split(":", 2)[1];
                    if (type == "chat")
                    {
                        string id = target.Split("@")[0];
                        Chat? chat = await Chat.GetChat(id + "@" + fed.publicName);
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
                                    if ((bool)upd.Value && chat.CanDo(user, Chat.ChatAction.Send)) // Typing
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
                                    if (eventn == "NEWMESSAGE" && chat.CanDo(fed.publicName, Chat.ChatAction.Send))
                                    {
                                        if ((update["senderUID"] ?? "").ToString() == "0")
                                        {
                                            continue; //Don't allow Pamuk messages from other federations, because they are probably echoes.
                                        }

                                        string sender = fed.FixUserID((update["senderUID"] ?? "").ToString());

                                        if (chat.CanDo(sender, Chat.ChatAction.Send))
                                        {

                                            // IDK how else to do this...
                                            string? forwardedFrom = null;
                                            if (update.ContainsKey("forwardedFromUID"))
                                            {
                                                if (update["forwardedFromUID"] != null)
                                                {
                                                    forwardedFrom = (update["forwardedFromUID"] ?? "").ToString();
                                                    if (forwardedFrom == "")
                                                    {
                                                        forwardedFrom = null;
                                                    }
                                                }
                                            }

                                            ChatMessage msg = new ChatMessage()
                                            {
                                                senderUID = sender,
                                                content = (update["content"] ?? "").ToString() ?? "",
                                                sendTime = (DateTime?)update["sendTime"] ?? DateTime.Now,
                                                replyMessageID = update.ContainsKey("replyMessageID") ? update["replyMessageID"] == null ? null : (update["replyMessageID"] ?? "").ToString() : null,
                                                forwardedFromUID = forwardedFrom,
                                                files = update.ContainsKey("files") && (update["files"] is JArray) ? ((JArray?)update["files"] ?? new JArray()).ToObject<List<string>>() : null,
                                                isPinned = update["isPinned"] != null ? (bool?)update["isPinned"] ?? false : false,
                                                reactions = update.ContainsKey("reactions") && (update["reactions"] is JObject) ? ((JObject?)update["reactions"] ?? new JObject()).ToObject<MessageReactions>() ?? new MessageReactions() : new MessageReactions(),
                                            };
                                            fed.FixMessage(msg);
                                            chat.SendMessage(msg, true, mid);
                                        }
                                        else
                                        {
                                            Console.WriteLine(sender + " Remote server thinks user has access to something. idk federation is wip expect this");
                                        }
                                    }
                                    else if (eventn.EndsWith("REACTED"))
                                    {
                                        if (update.ContainsKey("senderUID") && update.ContainsKey("reaction"))
                                        {
                                            if (update["senderUID"] != null && update["reaction"] != null)
                                            {
                                                string uid = fed.FixUserID((update["senderUID"] ?? "").ToString() ?? "");
                                                if (chat.CanDo(uid, Chat.ChatAction.React, mid))
                                                {
                                                    chat.ReactMessage(mid, uid, (update["reaction"] ?? "").ToString() ?? "", eventn == "REACTED", update.ContainsKey("sendTime") ? (DateTime?)update["sendTime"] : null);
                                                }
                                                else
                                                {
                                                    Console.WriteLine(uid + " Remote server thinks user has access to something. idk federation is wip expect this");
                                                }
                                            }
                                            else
                                            {
                                                Console.WriteLine("smth nul here!!!");
                                                Console.WriteLine(update["senderUID"]);
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine("smth not here!!!");
                                            Console.WriteLine(update.ContainsKey("senderUID"));
                                        }
                                    }
                                    else if (eventn == "DELETED" && chat.CanDo(fed.publicName, Chat.ChatAction.Delete, mid))
                                    {
                                        chat.DeleteMessage(mid);
                                    }
                                    else if (eventn == "PINNED" && chat.CanDo(fed.publicName, Chat.ChatAction.Pin, mid))
                                    {
                                        if (update.ContainsKey("userID") && update["userID"] != null)
                                        {
                                            chat.PinMessage(mid, true, update["userID"]?.ToString());
                                        }
                                    }
                                    else if (eventn == "UNPINNED" && chat.CanDo(fed.publicName, Chat.ChatAction.Pin, mid))
                                    {
                                        if (update.ContainsKey("userID") && update["userID"] != null)
                                        {
                                            chat.PinMessage(mid, false, update["userID"]?.ToString());
                                        }
                                    }
                                    else if (eventn == "READ")
                                    {
                                        if (update.ContainsKey("userID") && update.ContainsKey("readTime"))
                                        {
                                            if (update["userID"] != null)
                                            {
                                                string uid = fed.FixUserID((update["userID"] ?? "").ToString() ?? "");
                                                if (chat.CanDo(uid, Chat.ChatAction.React, mid))
                                                    chat.ReadMessage(mid, uid, (DateTime?)update["readTime"]);
                                            }
                                        }
                                    }
                                    else if (eventn == "EDITED")
                                    {
                                        if (update.ContainsKey("content"))
                                        {
                                            string? content = update["content"]?.ToString();
                                            if (content != null)
                                            {
                                                if (chat.CanDo(fed.publicName, Chat.ChatAction.Edit, mid))
                                                    chat.EditMessage(mid, content, (DateTime?)update["editTime"]);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (type == "user")
                    {
                        string id = target.Split("@")[0];
                        UserProfile? profile = await UserProfile.Get(id + "@" + fed.publicName);
                        if (profile != null)
                        {
                            var updates = updatehook.Value;
                            if (updates.ContainsKey("online"))
                            {
                                string onlineStatus = (updates["online"] ?? "").ToString() ?? "";
                                profile.onlineStatus = onlineStatus;
                            }

                            if (updates.ContainsKey("profileUpdate"))
                            {
                                var update = (JObject?)updates["profileUpdate"];
                                if (update != null)
                                {
                                    string name = (update["name"] ?? "").ToString() ?? "";
                                    profile.name = name;

                                    string picture = (update["picture"] ?? "").ToString() ?? "";
                                    profile.picture = picture;

                                    string bio = (update["bio"] ?? "").ToString() ?? "";
                                    profile.bio = bio;

                                    profile.Save();
                                }
                            }

                            if (updates.ContainsKey("publicTagChange"))
                            {
                                var tag = updates["publicTagChange"]?.ToString();
                                if (tag != null && tag != profile.publicTag)
                                {
                                    profile.publicTag = tag;
                                    profile.NotifyPublicTagChange();
                                }
                            }
                        }
                    }
                    else if (type == "group")
                    {
                        string id = target.Split("@")[0];
                        Group? group = await Group.Get(id + "@" + fed.publicName);
                        if (group == null)
                        {
                            group = await Group.Get(id);
                        }
                        if (group != null)
                        {
                            var updates = updatehook.Value;
                            foreach (var upd in updates)
                            {
                                if (upd.Value == null) continue;
                                if (upd.Key.StartsWith("USER|"))
                                {
                                    // Get the user and fix id
                                    string user = fed.FixUserID(upd.Key.Split("|")[1]);
                                    string role = upd.Value.ToString() ?? "";
                                    string userServer = "";
                                    if (user.Contains("@"))
                                    {
                                        userServer = user.Split("@")[1];
                                    }

                                    if (role == "")
                                    {
                                        if (group.CanDo(fed.publicName, Group.GroupAction.Ban)) group.UnbanUser(user);
                                        if (userServer == fed.publicName || group.CanDo(fed.publicName, Group.GroupAction.Kick)) await group.RemoveUser(user);
                                    }
                                    else if (role == "BANNED" && group.CanDo(fed.publicName, Group.GroupAction.Ban))
                                    {
                                        await group.BanUser(user);
                                    }
                                    else
                                    {
                                        if (group.members.ContainsKey(user) && group.CanDo(fed.publicName, Group.GroupAction.EditUser))
                                        {
                                            group.SetUserRole(user, role);
                                        }
                                        else
                                        {
                                            await group.AddUser(user, role);
                                        }
                                    }

                                    group.Save();
                                }
                                else
                                {
                                    if (upd.Key == "edit" && group.CanDo(fed.publicName, Group.GroupAction.EditGroup))
                                    {
                                        var update = (JObject)upd.Value;
                                        if (update.ContainsKey("name") && update.ContainsKey("info") && update.ContainsKey("picture") && update.ContainsKey("isPublic") && update.ContainsKey("userID"))
                                        {
                                            string userID = (update["userID"] ?? "").ToString();
                                            string name = (update["name"] ?? "").ToString();
                                            string picture = (update["picture"] ?? "").ToString();
                                            string info = (update["info"] ?? "").ToString();
                                            bool isPublic = (bool)(update["isPublic"] ?? false);

                                            bool edit = false;
                                            if (group.name != name || group.picture != picture || group.info != info || group.isPublic != isPublic)
                                            {
                                                group.name = name;
                                                group.picture = picture;
                                                group.info = info;
                                                group.isPublic = isPublic;
                                                edit = true;
                                                if (!update.ContainsKey("roles")) group.notifyEdit(Group.EditType.Basic, userID);
                                            }
                                            if (update.ContainsKey("roles"))
                                            {
                                                var rolesCast = (JObject?)update["roles"];
                                                if (rolesCast != null)
                                                {
                                                    var roles = rolesCast.ToObject<Dictionary<string, GroupRole>>();
                                                    if (roles != null && group.validateNewRoles(roles))
                                                    {
                                                        group.roles = roles;
                                                        edit = true;
                                                        group.notifyEdit(Group.EditType.WithRoles, userID);
                                                    }
                                                    else if (edit)
                                                    {
                                                        group.notifyEdit(Group.EditType.Basic, userID);
                                                    }
                                                }
                                            }

                                            if (edit)
                                            {
                                                Chat? chat = await Chat.GetChat(group.groupID);
                                                if (chat != null)
                                                {
                                                    string uid = fed.FixUserID(userID);
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

                                                group.Save();
                                            }
                                        }
                                    }
                                    else if (upd.Key == "publicTagChange" && group.CanDo(fed.publicName, Group.GroupAction.EditGroup))
                                    {
                                        var tag = upd.Value?.ToString();
                                        if (tag != null && tag != group.publicTag)
                                        {
                                            group.publicTag = tag;
                                            group.NotifyPublicTagChange();
                                        }
                                    }
                                }
                            }
                        }
                    }
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
            Console.WriteLine(e.ToString());
            return new ActionReturn()
            {
                statusCode = 500,
                res = e.ToString()
            };
        }

        return new ActionReturn()
        {
            statusCode = 200,
            res = JsonConvert.SerializeObject(new ServerResponse("done"))
        };
    }
}