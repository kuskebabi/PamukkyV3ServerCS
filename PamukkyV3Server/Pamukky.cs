using System;
using System.Collections.Generic;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System.Net;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using System.Reflection;

using Konscious.Security.Cryptography;
using System.Web;

namespace PamukkyV3;

internal class Program
{
    public const string datetimeFormat = "MM dd yyyy, HH:mm zzz";
    static HttpListener _httpListener = new HttpListener();
    static Dictionary<string, loginCred> loginCredCache = new();
    static Dictionary<string, userProfile> userProfileCache = new();
    static Dictionary<string, List<chatItem>> userChatsCache = new();
    static Dictionary<string, Chat> chatsCache = new();
    static Dictionary<string, Group> groupsCache = new();
    static Notifications notifications = new();

    class Notifications:Dictionary<string, Dictionary<string, userNotification>> {
        public Dictionary<string, userNotification> Get(string uid) {
            if (!ContainsKey(uid)) {
                this[uid] = new();
            }
            return this[uid];
        }
    }

    public static string datetostring(DateTime dt) {
        return dt.ToString(datetimeFormat);
    }

    class userNotification {
        public string? chatid;
        public profileShort? user;
        public string content = "";
    }

    class loginCred {
        public string EMail = "";
        public string Password = "";
        public string userID = "";
        public string token = "";
    }

    class userProfile {
        public string name = "User";
        public string picture = "";
        public string description = "Hello!";
        private DateTime? onlinetime;
        public void setOnline() {
            onlinetime = DateTime.Now;
        }
        public string getOnline() {
            if (onlinetime == null) {
                return datetostring(DateTime.MinValue);
            }else {
                if (onlinetime.Value.AddSeconds(5) > DateTime.Now) {
                    return "Online";
                }else { //Return last online
                    return datetostring(onlinetime.Value);
                }
            }
            
        }
    }

    class profileShort { //Short version for userProfile.
        public string name = "User";
        public string picture = "";
        public static profileShort fromProfile(userProfile? profile) {
            if (profile != null) {
                return new profileShort() {name = profile.name, picture = profile.picture};
            }
            return new profileShort();
        }
        public static profileShort fromGroup(Group? group) {
            if (group != null) {
                return new profileShort() {name = group.name, picture = group.picture};
            }
            return new profileShort();
        }
    }

    class Group {
        public string groupID = "";
        public string name = "";
        public string picture = "";
        public string info = "";
        public string owner = ""; //The creator, not the current one
        public string time = ""; //Creation time
        public Dictionary<string, groupMember> members = new();
        public Dictionary<string, groupRole> roles = new();
        public List<string> bannedMembers = new();

        public static Group? get(string gid) {
            if (groupsCache.ContainsKey(gid)) {
                return groupsCache[gid];
            }
            if (File.Exists("data/group/" + gid + "/info")) {
                Group? g = JsonConvert.DeserializeObject<Group>(File.ReadAllText("data/group/" + gid + "/info"));
                if (g != null) {
                    g.groupID = gid;
                    groupsCache[gid] = g;
                }
                return g;
            }
            return null;
        }
        public bool addUser(string uid,string role = "Normal") {
            if (members.ContainsKey(uid)) { // To not mess stuff up
                return true;
            }
            if (!roles.ContainsKey(role)) { // Again, prevent some mess
                return false;
            }
            if (bannedMembers.Contains(uid)) { // Block banned users
                return false;
            }
            List<chatItem>? clist = GetUserChats(uid); // Get chats list of user
            if (clist == null) {
                return false; //user doesn't exist
            }
            members[uid] = new() { // Add the member! Say hi!!
                user = uid,
                role = role,
                jointime = datetostring(DateTime.Now)
            };
            chatItem g = new() { // New chats list item
                group = groupID,
                type = "group",
                chatid = groupID
            };
            addToChats(clist,g); //Add to their chats list
            saveUserChats(uid,clist); //Save their chats list
            return true; //Success!!
        }
        public enum groupAction {
            Kick, //TODO
            Ban, //TODO
            EditUser,
            EditGroup
        }

        public bool canDo(string user,groupAction action) {
            bool contains = false;
            groupMember? u = null;
            foreach (var member in members) { //find the user
                if (member.Value.user == user) {
                    contains = true;
                    u = member.Value;
                }
            }
            if (!contains || u == null) { // Doesn't exist? block
                return false;
            }

            // Get the role
            groupRole role = roles[u.role];
            //Check what the role can do depending on the request.
            if (action == groupAction.EditUser) return role.AllowEditingUsers;
            if (action == groupAction.EditGroup) return role.AllowEditingSettings;
            if (action == groupAction.Kick) return role.AllowKicking;
            if (action == groupAction.Ban) return role.AllowBanning;

            return false;
        }

        public void save() {
            Directory.CreateDirectory("data/group/" + groupID);
            string c = JsonConvert.SerializeObject(this);
            File.WriteAllTextAsync("data/group/" + groupID + "/info",c);
        }
    }

    class groupInfo { //stripped Group for /getgroup
        public string name = "";
        public string picture = "";
        public string info = "";
    }

    class groupMember {
        public string user = "";
        public string role = "";
        public string jointime = "";
    }

    class groupRole {
        public int AdminOrder = 0;
        public bool AllowMessageDeleting = true;
        public bool AllowEditingSettings = true;
        public bool AllowKicking = true;
        public bool AllowBanning = true;
        public bool AllowSending = true;
        public bool AllowEditingUsers = true;
        public bool AllowSendingReactions = true;
    }

    class chatMessage { // Chat message.
        public string sender = "";
        public string content = "";
        public string time = "";
        public string? replymsgid;
        public List<string>? files;
        public string? forwardedfrom;
        public messageReactions reactions = new();
    }
    class messageReaction {
        public string reaction = "";
        public string sender = "";
        public string time = "";
    }
    class messageEmojiReactions:Dictionary<string,messageReaction> {} //Single reaction

    class messageReactions:Dictionary<string,messageEmojiReactions> { // All reactions
        public void update() {
            List<string> keysToRemove = new();
            foreach (var mer in this) {
                if (mer.Value.Count == 0) {
                    keysToRemove.Add(mer.Key);
                }
            }
            foreach (string k in keysToRemove) {
                Remove(k);
            }
        }

        public messageEmojiReactions get(string reaction,bool addnew = false) {
            if (ContainsKey(reaction)) {
                return this[reaction];
            }
            messageEmojiReactions d = new();
            if (addnew) Add(reaction,d);
            return d;
        }
    }

    class chatFile {
        public string url = "";
        public string? name;
        public int? size;
    }

    class chatMessageFormatted:chatMessage { // Chat message formatted for client sending.
        //public profileShort? senderuser; REMOVED. Data waste.
        public string? replymsgcontent;
        public string? replymsgsender;
        public List<chatFile>? gImages;
        public List<chatFile>? gVideos;
        public List<chatFile>? gFiles;
        //public string? forwardedname; REMOVED. Data waste.
        public chatMessageFormatted(chatMessage msg) {
            sender = msg.sender;
            content = msg.content;
            time = msg.time;
            replymsgid = msg.replymsgid;
            files = msg.files;
            reactions = msg.reactions;
            forwardedfrom = msg.forwardedfrom;
            //senderuser = profileShort.fromProfile(GetUserProfile(sender));
            //if (forwardedfrom != null) {
            //    forwardedname = profileShort.fromProfile(GetUserProfile(forwardedfrom)).name;
            //}
            if (files != null) { //Group file to types.
                if (gVideos == null) gVideos = new();
                if (gImages == null) gImages = new();
                if (gFiles == null) gFiles = new();
                foreach (string fi in files) {
                    string file = fi.Replace("%SERVER%getmedia?file=","data/upload/"); //Get path
                    if (File.Exists(file)) {
                        fileUpload? f = JsonConvert.DeserializeObject<fileUpload>(File.ReadAllText(file));
                        if (f != null) {
                            string[] spl = f.contentType.Split("/");
                            if (spl.Length > 1) {
                                string extension = spl[1];
                                if (extension == "png" || extension == "jpg" || extension == "jpeg" || extension == "gif" || extension == "bmp") {
                                    gImages.Add(new chatFile() {url = fi});
                                }else if (extension == "mp4") {
                                    gVideos.Add(new chatFile() {url = fi});
                                }else {
                                    gFiles.Add(new chatFile() {url = fi, name = f.actualName, size = f.size});
                                }
                            }
                        }
                    }//else, ignore                  
                }
            }
        }

        public Dictionary<string,object?> toDictionary() {
            Dictionary<string,object?> d = new();
            //d["senderuser"] = senderuser;
            d["replymsgcontent"] = replymsgcontent;
            d["replymsgsender"] = replymsgsender;
            d["gImages"] = gImages;
            d["gVideos"] = gVideos;
            d["gFiles"] = gFiles;
            d["sender"] = sender;
            d["content"] = content;
            d["time"] = time;
            d["files"] = files;
            d["reactions"] = reactions;
            d["forwardedfrom"] = forwardedfrom;
            //d["forwardedname"] = forwardedname;
            return d;
        }
    }

    class Chat:OrderedDictionary<string,chatMessage> {
        public string chatid = "";
        public bool isgroup = false;
        Group group = new();
        private const int pagesize = 48; //Increase this to get more messages
        private Dictionary<string,Dictionary<string,Dictionary<string,object?>>> updates = new();
        private Dictionary<string,chatMessageFormatted> formatcache = new();
        public void addupdater(string name) {
            if (!updates.ContainsKey(name)) {
                updates[name] = new();
            }
        }
        public Dictionary<string,Dictionary<string,object?>>? getupdater(string name) {
            if (updates.ContainsKey(name)) {
                List<string> keys = new();
                foreach (string key in updates[name].Keys) {
                    keys.Add(key);
                }
                Task.Delay(5000).ContinueWith((task) => { //remove updater keys after delay so all clients can see it before it gets kaboom
                    foreach (string key in keys) {
                        updates[name].Remove(key);
                    }
                });
                return updates[name];
            }
            return null;
        }

        private chatMessageFormatted? formatMessage(string key) {
            if (formatcache.ContainsKey(key)) return formatcache[key];
            if (ContainsKey(key)) {
                chatMessageFormatted formatted = new chatMessageFormatted(this[key]);
                formatcache[key] = formatted;
                if (formatted.replymsgid != null) {
                    if (ContainsKey(formatted.replymsgid)) {
                        chatMessageFormatted? innerformatted = formatMessage(formatted.replymsgid);
                        if (innerformatted != null) { //The senderuser stuff was removed.
                            formatted.replymsgcontent = innerformatted.content;
                            formatted.replymsgsender = innerformatted.sender;
                        }
                    }
                }
                return formatted;
            }
            return null;
        }

        public OrderedDictionary<string,chatMessageFormatted> format() {
            OrderedDictionary<string,chatMessageFormatted> fd = new();
            foreach (var kv in this) {
                chatMessageFormatted? f = formatMessage(kv.Key);
                if (f != null) fd[kv.Key] = f;
            }
            return fd;
        }
        public Chat getPage(int page = 0) {
            if (Count - 1 > pagesize) {
                Chat rtrn = new() {chatid = chatid};
                int index = (Count - 1) - (page * pagesize);
                //Console.WriteLine(Count);
                while (index > Count - ((page + 1) * pagesize) && index >= 0) {
                    //Console.WriteLine(index);
                    rtrn.Insert(0,Keys.ElementAt(index),Values.ElementAt(index));
                    index -= 1;
                }
                return rtrn;
            }
            return this;
        }

        public chatMessage? getLastMessage() {
            if (Count > 0) {
                return Values.ElementAt(Count - 1);
            }
            return null;
        }

        public void sendMessage(chatMessage msg, bool notify = true) {
            string id = DateTime.Now.Ticks.ToString();
            Add(id,msg);
            chatMessageFormatted? f = formatMessage(id);
            if (f != null) {
                Dictionary<string,object?> update = f.toDictionary();
                update["event"] = "NEWMESSAGE";
                foreach (var updater in updates) {
                    updater.Value[id] = update;
                }
                if (notify)
                foreach (string member in group.members.Keys) {
                    if (msg.sender != member)
                    notifications.Get(member).Add(id,
                        new userNotification() {
                            user = profileShort.fromProfile(GetUserProfile(f.sender)), //Probably would stay like this
                            content = msg.content,
                            chatid = chatid
                        }
                    );
                    //Console.WriteLine(JsonConvert.SerializeObject(notifications));
                }
            }
        }

        public void deleteMessage(string msgid) {
            Remove(msgid);
            Dictionary<string,object?> update = new();
            update["event"] = "DELETED";
            foreach (var updater in updates) {
                updater.Value[msgid] = update;
            }
        }

        public messageReactions reactMessage(string msgid, string uid, string reaction) {
            if (ContainsKey(msgid)) {
                chatMessage msg = this[msgid];
                messageReactions rect = msg.reactions;
                messageEmojiReactions r = rect.get(reaction,true);
                if (r.ContainsKey(uid)) {
                    r.Remove(uid);
                }else {
                    messageReaction react = new() {sender = uid, reaction = reaction, time = datetostring(DateTime.Now)};
                    r.Add(uid,react);
                }
                rect.update();
                Dictionary<string,object?> update = new();
                update["event"] = "REACTIONS";
                update["rect"] = rect;
                foreach (var updater in updates) {
                    updater.Value[msgid] = update;
                }
                return rect;
            }
            return new();
        }

        public enum chatAction {
            Read,
            Send,
            React,
            Delete
        }

        public bool canDo(string user,chatAction action,string msgid = "") {
            bool contains = false;
            groupMember? u = null;
            foreach (var member in group.members) { //find the user
                if (member.Value.user == user) {
                    contains = true;
                    u = member.Value;
                }
            }
            if (!contains || u == null) { // Doesn't exist? block
                return false;
            }
            if (action != chatAction.Send && action != chatAction.Read) { // Any actions except read and send will require a existent message
                if (!ContainsKey(msgid)) {
                    return false;
                }
            }
            if (action == chatAction.Delete) {
                if (this[msgid].sender == user) { // User can delete their own messages.
                    return true;
                }
            }
            if (u.role != "") { //is this a real group user?
                //Then it's a real group
                groupRole role = group.roles[u.role];
                if (action == chatAction.React) return role.AllowSendingReactions;
                if (action == chatAction.Send) return role.AllowSending;
                if (action == chatAction.Delete) return role.AllowMessageDeleting;
            }
            return true;
        }

        public static Chat? getChat(string chat) {
            if (chatsCache.ContainsKey(chat)) {
                return chatsCache[chat];
            }
            //Check validity
            if (chat.Contains("-")) {
                string[] spl = chat.Split("-");
                if (!Directory.Exists("data/user/" + spl[0])) {
                    return null;
                }
                if (!Directory.Exists("data/user/" + spl[1])) {
                    return null;
                }
            }else {
                if (!Directory.Exists("data/group/" + chat)) {
                    return null;
                }
            }

            //Load
            Chat? c;
            if (File.Exists("data/chat/" + chat + "/chat")) {
                c = JsonConvert.DeserializeObject<Chat>(File.ReadAllText("data/chat/" + chat + "/chat"));
                // If that is null, we should NOT load the chat at all
            }else {
                c = new Chat();
            }
            if (c != null) {
                c.chatid = chat;
                c.isgroup = !chat.Contains("-");
                if (c.isgroup) {
                    // Load the real group
                    Group? g = Group.get(chat);
                    if (g == null) {
                        throw new Exception("404"); //Other part of the validity check
                    }
                    c.group = g;
                }else {
                    // Make a fake group
                    string[] users = chat.Split("-");
                    foreach (string user in users) {
                        if (!c.group.members.ContainsKey(user))
                        c.group.members.Add(
                            user,new groupMember() {user = user}
                        );
                    }
                }
                chatsCache[chat] = c;
            }
            return c;
        }
        public void saveChat() {
            Directory.CreateDirectory("data/chat/" + chatid);
            string c = JsonConvert.SerializeObject(this);
            File.WriteAllTextAsync("data/chat/" + chatid + "/chat",c);
        }
    }

    class chatItem { //Chats list item
        public string chatid = "";
        public string type = "";

        // Optional because depends on it's type.
        public string? user = null;
        public string? group = null;

        //for preview at chats list, TODO: SET NULL BEFORE SAVE
        public profileShort? info = null;
        public chatMessage? lastmessage = null;
    }

    class fileUpload {
        public string sender = "";
        public string actualName = "";
        public int size = 0;
        public string contentType = "";
    }

    class fileUploadResponse { // What should server reply for /upload
        public string url = ""; //success
        public string status;
        public fileUploadResponse(string stat, string furl) { //for easier creation
            status = stat;
            url = furl;
        }
    }

    class serverResponse { //Mostly for errors and actions that doesn't have any return
        public string status; //done
        public string? description;

        public serverResponse(string stat, string? descript = null) { //for easier creation
            status = stat;
            description = descript;
        }
    }

    class loginResponse { //for both signup and login calls.
        public string token;
        public string uid;
        //public userProfile userinfo;

        public loginResponse(string utoken, string id) {
            token = utoken;
            uid = id;
            //userinfo = profile;
        }
    }

    static string hashpassword(string pass, string uid) {
        try {
            using (Argon2id argon2 = new Argon2id(Encoding.UTF8.GetBytes(pass))) {
                try {
                    argon2.Iterations = 5;
                    argon2.MemorySize = 7;
                    argon2.DegreeOfParallelism = 1;
                    argon2.AssociatedData = Encoding.UTF8.GetBytes(uid);
                    return Encoding.UTF8.GetString(argon2.GetBytes(128));
                }catch {
                    return ""; //In case account is older than when the algorithm was added, can also be used as a test account. Basically passwordless
                }finally {
                    argon2.Dispose(); // Memory eta bomba
                }
            }
        }catch {return "";}
    }


    static userProfile? GetUserProfile(string uid) {
        if (userProfileCache.ContainsKey(uid)) {
            return userProfileCache[uid];
        }else {
            if (File.Exists("data/user/" + uid + "/profile")) { // check
                userProfile? up = JsonConvert.DeserializeObject<userProfile>(File.ReadAllText("data/user/" + uid + "/profile"));
                if (up != null) {
                    userProfileCache[uid] = up;
                    return up;
                }
            }
        }
        return null;
    }

    static void SetUserProfile(string uid,userProfile up) {
        userProfileCache[uid] = up; //set
        File.WriteAllText("data/user/" + uid + "/profile", JsonConvert.SerializeObject(up)); //save
    }

    static loginCred? GetLoginCred(string token, bool preventbypass = true) {
        //!preventbypass is when you wanna use the token to get other info
        if (token.Contains("@") && preventbypass) { //bypassing
            return null;
        }
        if (loginCredCache.ContainsKey(token)) {
            return loginCredCache[token];
        }else {
            if (File.Exists("data/auth/" + token)) {
                loginCred? up = JsonConvert.DeserializeObject<loginCred>(File.ReadAllText("data/auth/" + token));
                if (up != null) {
                    loginCredCache[token] = up;
                    return up;
                }
            }
        }
        return null;
    }

    static string? GetUIDFromToken(string token, bool preventbypass = true) {
        loginCred? cred = GetLoginCred(token, preventbypass);
        if (cred == null) {
            return null;
        }
        return cred.userID;
    }

    static List<chatItem>? GetUserChats(string uid) { // Get chats list
        if (userChatsCache.ContainsKey(uid)) { // Use cache
            return userChatsCache[uid];
        }else { //Load it from file
            if (File.Exists("data/user/" + uid + "/chatslist")) {
                List<chatItem>? uc = JsonConvert.DeserializeObject<List<chatItem>>(File.ReadAllText("data/user/" + uid + "/chatslist"));
                if (uc != null) {
                    userChatsCache[uid] = uc;
                    return uc;
                }
            }else {
                if (Directory.Exists("data/user/" + uid)) {
                    return new List<chatItem>();
                }
            }
        }
        return null;
    }

    static void addToChats(List<chatItem> list,chatItem item) { //Add to chats list
        foreach (var i in list) { //Check if it doesn't exist
            if (i.type == "group") {
                if (i.group == item.group) return;
            }else if (i.type == "user") {
                if (i.user == item.user) return;
            }
        }
        list.Add(item);
    }

    static void saveUserChats(string uid, List<chatItem> list) { //Chats list
        userChatsCache[uid] = list;
        File.WriteAllText("data/user/" + uid + "/chatslist", JsonConvert.SerializeObject(list));
    }


    static void respond()
    {
        Task<HttpListenerContext> ctx = _httpListener.GetContextAsync();
        ctx.ContinueWith((Task<HttpListenerContext> c) =>
        {
            if (c.IsCompletedSuccessfully) { //Sometimes crashed when server exited, so added this
                var context = c.Result;
                if (context.Request.Url == null) { //just ignore
                    return;
                }
                string res = "";
                int statuscode = 200;
                string[] spl = context.Request.Url.ToString().Split("/");
                string url = spl[spl.Length - 1];
                bool writeRes = true;
                //Added these so web client can access it
                context.Response.AddHeader("Access-Control-Allow-Headers", "*");
                context.Response.AddHeader("Access-Control-Allow-Methods", "*");
                context.Response.AddHeader("Access-Control-Allow-Origin", "*");
                //Console.WriteLine(url); //debugging
                try {
                    if (url == "signup") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<loginCred>(body);
                        if (a != null) {
                            a.EMail = a.EMail.Trim();
                            if (!File.Exists("data/auth/" + a.EMail)) {
                                // Check the email format. TODO: maybe improve
                                if (a.EMail != "" && a.EMail.Contains("@") && a.EMail.Contains(".") && !a.EMail.Contains(" ")) {
                                    // IDK, why limit password characters? I mean also just get creative and dont make your password "      "
                                    if (a.Password.Trim() != "" && a.Password.Length >= 6) {
                                        string uid = "";
                                        do 
                                        {
                                            uid =  Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=","").Replace("+","").Replace("/","");
                                        }
                                        while (Directory.Exists("data/user/" + uid));

                                        a.Password = hashpassword(a.Password,uid);

                                        string token = "";
                                        do 
                                        {
                                            token =  Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=","").Replace("+","").Replace("/","");
                                        }
                                        while (File.Exists("data/auth/" + token));

                                        a.userID = uid;
                                        a.token = token;

                                        //Console.WriteLine(a.Password);
                                        userProfile up = new() {name = a.EMail.Split("@")[0].Split(".")[0]};
                                        Directory.CreateDirectory("data/user/" + uid);
                                        string astr = JsonConvert.SerializeObject(a);
                                        File.WriteAllText("data/auth/" + token, astr);
                                        File.WriteAllText("data/auth/" + a.EMail, astr);
                                        SetUserProfile(uid,up);
                                        List<chatItem>? chats = GetUserChats(uid); //get new user's chats list
                                        if (chats != null) {
                                            chatItem savedmessages = new() { //automatically add saved messages for the user.
                                                user = uid,
                                                type = "user",
                                                chatid = uid + "-" + uid
                                            };
                                            addToChats(chats,savedmessages);
                                            saveUserChats(uid,chats); //save it
                                        }else {
                                            Console.WriteLine("Signup chatslist was null!!!"); //log if weirdo
                                        }
                                        //Done
                                        res = JsonConvert.SerializeObject(new loginResponse(token,uid));
                                    }else {
                                        statuscode = 411;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "Password format wrong."));
                                    }
                                }else {
                                    statuscode = 411;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Invalid E-Mail."));
                                }
                            }else {
                                statuscode = 401;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User already exists."));
                            }
                            
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                        
                    }else if (url == "login") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<loginCred>(body);
                        if (a != null) {
                            a.EMail = a.EMail.Trim();
                            if (File.Exists("data/auth/" + a.EMail)) {
                                loginCred? lc = JsonConvert.DeserializeObject<loginCred>(File.ReadAllText("data/auth/" + a.EMail));
                                if (lc != null) {
                                    string uid = lc.userID;
                                    a.Password = hashpassword(a.Password,uid);
                                    if (lc.Password == a.Password && lc.EMail == a.EMail) {
                                        string token = lc.token;
                                        
                                        res = JsonConvert.SerializeObject(new loginResponse(token,uid));
                                    }else {
                                        statuscode = 403;
                                        res = JsonConvert.SerializeObject(new serverResponse("error","Incorrect login"));
                                    }
                                }else {
                                    statuscode = 411;
                                    res = JsonConvert.SerializeObject(new serverResponse("error"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                            
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "changepassword") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("password") && a.ContainsKey("oldpassword")) {
                            if (a["password"].Trim().Length >= 6) {
                                loginCred? lc = GetLoginCred(a["token"]);
                                if (lc != null) {
                                    if (lc.Password == hashpassword(a["oldpassword"],lc.userID)) {
                                        string token = "";
                                        do 
                                        {
                                            token =  Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=","").Replace("+","").Replace("/","");
                                        }
                                        while (File.Exists("data/auth/" + token));
                                        File.Delete("data/auth/" + lc.token);
                                        lc.token = token;
                                        lc.Password = hashpassword(a["password"].Trim(),lc.userID);
                                        string astr = JsonConvert.SerializeObject(lc);
                                        File.WriteAllText("data/auth/" + token, astr);
                                        File.WriteAllText("data/auth/" + lc.EMail, astr);
                                        loginCredCache.Remove(a["token"]);
                                        res = astr;
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                                }
                            }else {
                                statuscode = 411;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "Password format wrong."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getuser") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("uid")) {
                            userProfile? up = GetUserProfile(a["uid"]);
                            if (up != null) {
                                res = JsonConvert.SerializeObject(up);
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getonline") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("uid")) {
                            userProfile? up = GetUserProfile(a["uid"]);
                            if (up != null) {
                                res = up.getOnline();
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "setonline") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token")) {
                            string? uid = GetUIDFromToken(a["token"]);
                            if (uid != null) {
                                userProfile? user = GetUserProfile(uid);
                                if (user != null) {
                                    user.setOnline();
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "updateuser") { //User profile edit
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token")) {
                            string? uid = GetUIDFromToken(a["token"]);
                            if (uid != null) {
                                userProfile? user = GetUserProfile(uid);
                                if (user != null) {
                                    if (a.ContainsKey("name") && a["name"].Trim() != "") {
                                        user.name = a["name"].Trim();
                                    }
                                    if (!a.ContainsKey("picture")) {
                                        a["picture"] = "";
                                    }
                                    if (!a.ContainsKey("description")) {
                                        a["description"] = "";
                                    }
                                    user.picture = a["picture"];
                                    user.description = a["description"].Trim();
                                    SetUserProfile(uid, user);
                                    res = JsonConvert.SerializeObject(new serverResponse("done"));
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getchatslist") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token")) {
                            string? uid = GetUIDFromToken(a["token"]);
                            if (uid != null) {
                                List<chatItem>? chats = GetUserChats(uid);
                                if (chats != null) {
                                    foreach (chatItem item in chats) { //Format chats for clients
                                        /*if (item.type == "user") { //Info
                                            var p = GetUserProfile(item.user ?? "");
                                            item.info = profileShort.fromProfile(p);
                                        }else if (item.type == "group") {
                                            var p = Group.get(item.group ?? "");
                                            item.info = profileShort.fromGroup(p);
                                        }*/

                                        Chat? chat = Chat.getChat(item.chatid);
                                        if (chat != null) {
                                            if (chat.canDo(uid,Chat.chatAction.Read)) { //Check for read permission before giving the last message
                                                item.lastmessage = chat.getLastMessage();
                                            }
                                        }
                                    }
                                    res = JsonConvert.SerializeObject(chats);
                                    foreach (chatItem item in chats) {
                                        item.info = null;
                                        item.lastmessage = null;
                                    }
                                }else {
                                    statuscode = 500;
                                    res = JsonConvert.SerializeObject(new serverResponse("error"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getnotifications") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token")) {
                            string? uid = GetUIDFromToken(a["token"]);
                            if (uid != null) {
                                //Console.WriteLine(JsonConvert.SerializeObject(notifications));
                                var usernotifies = notifications.Get(uid);
                                res = JsonConvert.SerializeObject(usernotifies);
                                List<string> keys = new();
                                foreach (string key in usernotifies.Keys) {
                                    keys.Add(key);
                                }
                                Task.Delay(10000).ContinueWith((task) => { //remove notifications after delay so all clients can see it before it's too late. SSSOOOOBBB
                                    foreach (string key in keys) {
                                        usernotifies.Remove(key);
                                    }
                                });
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "adduserchat") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("email")) {
                            string? uidu = GetUIDFromToken(a["token"]);
                            string? uidb = GetUIDFromToken(a["email"], false);
                            if (uidu != null && uidb != null) {
                                List<chatItem>? chatsu = GetUserChats(uidu);
                                List<chatItem>? chatsb = GetUserChats(uidb);
                                if (chatsu != null && chatsb != null) {
                                    string chatid = uidu + "-" + uidb;
                                    chatItem u = new() {
                                        user = uidb,
                                        type = "user",
                                        chatid = chatid
                                    };
                                    chatItem b = new() {
                                        user = uidu,
                                        type = "user",
                                        chatid = chatid
                                    };
                                    addToChats(chatsu,u);
                                    addToChats(chatsb,b);
                                    saveUserChats(uidu,chatsu);
                                    saveUserChats(uidb,chatsb);
                                    res = JsonConvert.SerializeObject(new serverResponse("done"));
                                }else {
                                    statuscode = 500;
                                    res = JsonConvert.SerializeObject(new serverResponse("error"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getchatpage") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid")) {
                            string? uid = GetUIDFromToken(a["token"]);
                            if (uid != null) {
                                Chat? chat = Chat.getChat(a["chatid"]);
                                if (chat != null) {
                                    if (chat.canDo(uid,Chat.chatAction.Read)) {
                                        chat.addupdater(uid);
                                        res = JsonConvert.SerializeObject(chat.getPage(a.ContainsKey("page") ? int.Parse(a["page"]) : 0).format());
                                    }else {
                                        statuscode = 401;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "You don't have permission to do this action."));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Couldn't open chat. Is it valid????"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "sendmessage") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,object>>(body);
                        if (a != null) {
                            List<string>? files = a.ContainsKey("files") && (a["files"] is JArray) ? ((JArray)a["files"]).ToObject<List<string>>() : null;
                            if (a.ContainsKey("token") && a.ContainsKey("chatid") && ((a.ContainsKey("content") && (a["content"].ToString() ?? "") != "") || (files != null && files.Count > 0))) {
                                string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                                if (uid != null) {
                                    Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
                                    if (chat != null) {
                                        if (chat.canDo(uid,Chat.chatAction.Send)) {
                                            chatMessage msg = new() {
                                                sender = uid,
                                                content = (a["content"].ToString() ?? "").Trim(),
                                                replymsgid = a.ContainsKey("replymsg") ? a["replymsg"].ToString() : null,
                                                files = files,
                                                time = datetostring(DateTime.Now)
                                            };
                                            chat.sendMessage(msg);
                                            res = JsonConvert.SerializeObject(new serverResponse("done"));
                                        }else {
                                            statuscode = 401;
                                            res = JsonConvert.SerializeObject(new serverResponse("error", "You don't have permission to do this action."));
                                        }
                                    }else {
                                        statuscode = 404;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "Couldn't open chat. Is it valid????"));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                                }
                            }else {
                                statuscode = 411;
                                res = JsonConvert.SerializeObject(new serverResponse("error"));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "deletemessage") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,object>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgid")) {
                            string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                            if (uid != null) {
                                Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
                                if (chat != null) {
                                    string? msgid = a["msgid"].ToString() ?? "";
                                    if (chat.canDo(uid,Chat.chatAction.Delete,msgid)) {
                                        if (chat.ContainsKey(msgid)) {
                                            chat.deleteMessage(msgid);
                                            res = JsonConvert.SerializeObject(new serverResponse("done"));
                                        }else {
                                            statuscode = 404;
                                            res = JsonConvert.SerializeObject(new serverResponse("error", "Message not found"));
                                        }
                                    }else {
                                        statuscode = 401;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "You don't have permission to do this action."));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Couldn't open chat. Is it valid????"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "sendreaction") { //More like a toggle
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,object>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgid") && a.ContainsKey("reaction") && (a["reaction"].ToString() ?? "") != "") {
                            string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                            if (uid != null) {
                                Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
                                if (chat != null) {
                                    string? msgid = a["msgid"].ToString() ?? "";
                                    string? reaction = a["reaction"].ToString() ?? "";
                                    if (chat.canDo(uid,Chat.chatAction.React,msgid)) {
                                        if (chat.ContainsKey(msgid)) {
                                            res = JsonConvert.SerializeObject(chat.reactMessage(msgid,uid,reaction));
                                        }else {
                                            statuscode = 404;
                                            res = JsonConvert.SerializeObject(new serverResponse("error", "Message not found"));
                                        }
                                    }else {
                                        statuscode = 401;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "You don't have permission to do this action."));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Couldn't open chat. Is it valid????"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "savemessage") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,object>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgid")) {
                            string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                            if (uid != null) {
                                Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
                                if (chat != null) {
                                    string? msgid = a["msgid"].ToString() ?? "";
                                    if (chat.canDo(uid,Chat.chatAction.Read,msgid)) {
                                        if (chat.ContainsKey(msgid)) {
                                            Chat? uchat = Chat.getChat(uid + "-" + uid);
                                            if (uchat != null) {
                                                chatMessage msg = new() {
                                                    sender = chat[msgid].sender,
                                                    content = chat[msgid].content,
                                                    files = chat[msgid].files,
                                                    time = datetostring(DateTime.Now)
                                                };
                                                uchat.sendMessage(msg,false);
                                                res = JsonConvert.SerializeObject(new serverResponse("done"));
                                            }
                                        }else {
                                            statuscode = 404;
                                            res = JsonConvert.SerializeObject(new serverResponse("error", "Message not found"));
                                        }
                                    }else {
                                        statuscode = 401;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "You don't have permission to do this action."));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Couldn't open chat. Is it valid????"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "forwardmessage") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,object>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgid") && a.ContainsKey("tochatid")) {
                            string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                            if (uid != null) {
                                Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
                                if (chat != null) {
                                    string? msgid = a["msgid"].ToString() ?? "";
                                    if (chat.canDo(uid,Chat.chatAction.Read,msgid)) {
                                        if (chat.ContainsKey(msgid)) {
                                            Chat? uchat = Chat.getChat(a["tochatid"].ToString() ?? "");
                                            if (uchat != null) {
                                                if (uchat.canDo(uid,Chat.chatAction.Send)) {
                                                    chatMessage msg = new() {
                                                        forwardedfrom = chat[msgid].sender,
                                                        sender = uid,
                                                        content = chat[msgid].content,
                                                        files = chat[msgid].files,
                                                        time = datetostring(DateTime.Now)
                                                    };
                                                    uchat.sendMessage(msg);
                                                    res = JsonConvert.SerializeObject(new serverResponse("done"));
                                                }
                                            }else {
                                                statuscode = 404;
                                                res = JsonConvert.SerializeObject(new serverResponse("error", "Couldn't open chat. Is it valid????"));
                                            }
                                        }else {
                                            statuscode = 404;
                                            res = JsonConvert.SerializeObject(new serverResponse("error", "Message not found"));
                                        }
                                    }else {
                                        statuscode = 401;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "You don't have permission to do this action."));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Couldn't open chat. Is it valid????"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getupdates") { //Chat updates
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("id")) {
                            string? uid = GetUIDFromToken(a["token"]);
                            if (uid != null) {
                                Chat? chat = Chat.getChat(a["id"]);
                                if (chat != null) {
                                    if (chat.canDo(uid,Chat.chatAction.Read)) {
                                        res = JsonConvert.SerializeObject(chat.getupdater(uid));
                                    }else {
                                        statuscode = 401;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "You don't have permission to do this action."));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Couldn't open chat. Is it valid????"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "upload" && context.Request.HttpMethod.ToLower() == "post") { //post only because browsers want OPTIONS request which would fail.
                        if (context.Request.Headers["token"] != null) {
                            string? uid = GetUIDFromToken(context.Request.Headers["token"] ?? "");
                            if (uid != null) {
                                if (context.Request.Headers["content-length"] != null) {
                                    int contentLength = int.Parse(context.Request.Headers["content-length"] ?? "0");
                                    if (contentLength != 0) {
                                        string id = "";
                                        do 
                                        {
                                            id =  Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=","").Replace("+","").Replace("/","");
                                        }
                                        while (File.Exists("data/upload/" + id));
                                        string? filename = context.Request.Headers["filename"];
                                        //if (filename == null) {
                                            filename = id;
                                        //}else {
                                        //    filename = filename + id;
                                        //}
                                        string fpname = filename.Replace(".","").Replace("/","").Replace("\\","");
                                        var stream = context.Request.InputStream;
                                        //stream.Seek(0, SeekOrigin.Begin);
                                        var fileStream = File.Create("data/upload/" + fpname + ".file");
                                        var cp = stream.CopyToAsync(fileStream);
                                        cp.ContinueWith((Task cpt) =>
                                        {
                                            if (c.IsCompletedSuccessfully) {
                                                fileStream.Close();
                                                fileStream.Dispose();
                                                fileUpload u = new() {
                                                    size = contentLength,
                                                    actualName = context.Request.Headers["filename"] ?? id,
                                                    sender = uid,
                                                    contentType = context.Request.Headers["content-type"] ?? ""
                                                };

                                                string? uf = JsonConvert.SerializeObject(u);
                                                if (uf == null) throw new Exception("???");
                                                File.WriteAllText("data/upload/" + fpname, uf);
                                                
                                                res = JsonConvert.SerializeObject(new fileUploadResponse("success","%SERVER%getmedia?file=" + fpname));
                                                context.Response.StatusCode = statuscode;
                                                context.Response.ContentType = "text/json";
                                                byte[] bts = Encoding.UTF8.GetBytes(res);
                                                context.Response.OutputStream.Write(bts, 0, bts.Length);
                                                context.Response.KeepAlive = false; 
                                                context.Response.Close(); 
                                            }
                                        });
                                        writeRes = false;
                                    }else {
                                        statuscode = 404;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "No file."));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "No file."));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url.StartsWith("getmedia")) { //Needs improvement
                        if (context.Request.QueryString["file"] != null) {
                            string file = context.Request.QueryString["file"] ?? "";
                            if (File.Exists("data/upload/" + file)) {
                                fileUpload? f = JsonConvert.DeserializeObject<fileUpload>(File.ReadAllText("data/upload/" + file));
                                if (f != null) {
                                    writeRes = false;
                                    context.Response.AddHeader("Content-Length", f.size.ToString());
                                    if (context.Request.Headers["sec-fetch-dest"] != "document") {
                                        context.Response.AddHeader("Content-Disposition", "attachment; filename=" + HttpUtility.UrlEncode(f.actualName));
                                    }
                                    context.Response.StatusCode = statuscode;
                                    var fileStream = File.OpenRead("data/upload/" + file + ".file");
                                    context.Response.KeepAlive = false; 
                                    var cp = fileStream.CopyToAsync(context.Response.OutputStream);
                                    cp.ContinueWith((Task cpt) =>
                                    {
                                            if (c.IsCompletedSuccessfully) {context.Response.Close();fileStream.Close();fileStream.Dispose();}
                                    });
                                }else {
                                    statuscode = 500;
                                    res = JsonConvert.SerializeObject(new serverResponse("error"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "File doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "creategroup") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token")) {
                            string? uid = GetUIDFromToken(a["token"]);
                            if (uid != null) {
                                if (a.ContainsKey("name") && a["name"].Trim() != "") {
                                    if (!a.ContainsKey("picture")) {
                                        a["picture"] = "";
                                    }
                                    if (!a.ContainsKey("info")) {
                                        a["info"] = "";
                                    }
                                    string id = "";
                                    do 
                                    {
                                        id =  Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=","").Replace("+","").Replace("/","");
                                    }
                                    while (Directory.Exists("data/groups/" + id));
                                    Group g = new() {
                                        groupID = id,
                                        name = a["name"].Trim(),
                                        picture = a["picture"],
                                        info = a["info"].Trim(),
                                        owner = uid,
                                        time = datetostring(DateTime.Now),
                                        roles = new() { //Default roles
                                            ["Owner"] = new groupRole() {
                                                AdminOrder = 0,
                                                AllowBanning = true,
                                                AllowEditingSettings = true,
                                                AllowEditingUsers = true,
                                                AllowKicking = true,
                                                AllowMessageDeleting = true,
                                                AllowSending = true,
                                                AllowSendingReactions = true
                                            },
                                            ["Admin"] = new groupRole() {
                                                AdminOrder = 1,
                                                AllowBanning = true,
                                                AllowEditingSettings = false,
                                                AllowEditingUsers = true,
                                                AllowKicking = true,
                                                AllowMessageDeleting = true,
                                                AllowSending = true,
                                                AllowSendingReactions = true
                                            },
                                            ["Moderator"] = new groupRole() {
                                                AdminOrder = 2,
                                                AllowBanning = true,
                                                AllowEditingSettings = false,
                                                AllowEditingUsers = false,
                                                AllowKicking = true,
                                                AllowMessageDeleting = true,
                                                AllowSending = true,
                                                AllowSendingReactions = true
                                            },
                                            ["Normal"] = new groupRole() {
                                                AdminOrder = 3,
                                                AllowBanning = false,
                                                AllowEditingSettings = false,
                                                AllowEditingUsers = false,
                                                AllowKicking = false,
                                                AllowMessageDeleting = false,
                                                AllowSending = true,
                                                AllowSendingReactions = true
                                            },
                                            ["Readonly"] = new groupRole() {
                                                AdminOrder = 4,
                                                AllowBanning = false,
                                                AllowEditingSettings = false,
                                                AllowEditingUsers = false,
                                                AllowKicking = false,
                                                AllowMessageDeleting = false,
                                                AllowSending = false,
                                                AllowSendingReactions = false
                                            },
                                        }
                                    };
                                    g.addUser(uid,"Owner");
                                    g.save();
                                    Dictionary<string,string> response = new() {
                                        ["groupid"] = id
                                    };
                                    res = JsonConvert.SerializeObject(response);
                                }else {
                                    statuscode = 411;
                                    res = JsonConvert.SerializeObject(new serverResponse("error","No group info"));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getgroup") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("groupid")) {
                            Group? gp = Group.get(a["groupid"]);
                            if (gp != null) {
                                res = JsonConvert.SerializeObject(new groupInfo() {
                                    name = gp.name,
                                    info = gp.info,
                                    picture = gp.picture
                                });
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "Group doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getgroupusers" || url == "getgroupmembers") { //getgroupmembers is new name
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("groupid")) {
                            Group? gp = Group.get(a["groupid"]);
                            if (gp != null) {
                                res = JsonConvert.SerializeObject(gp.members);
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "Group doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getgroupuserscount" || url == "getgroupmemberscount") { //getgroupmemberscount is new name
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("groupid")) {
                            Group? gp = Group.get(a["groupid"]);
                            if (gp != null) {
                                res = gp.members.Count.ToString();
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "Group doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "getgrouproles") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("groupid")) {
                            Group? gp = Group.get(a["groupid"]);
                            if (gp != null) {
                                res = JsonConvert.SerializeObject(gp.roles);
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "Group doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "joingroup") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid")) {
                            string? uid = GetUIDFromToken(a["token"]);
                            if (uid != null) {
                                Group? gp = Group.get(a["groupid"]);
                                if (gp != null) {
                                    if (gp.addUser(uid)) {
                                        Dictionary<string,string> response = new() {
                                            ["groupid"] = gp.groupID
                                        };
                                        res = JsonConvert.SerializeObject(response);
                                        gp.save();
                                    }else {
                                        statuscode = 500;
                                        res = JsonConvert.SerializeObject(new serverResponse("error"));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Group doesn't exist."));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "editgroup") {
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,object>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid")) {
                            string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                            if (uid != null) {
                                Group? gp = Group.get(a["groupid"].ToString() ?? "");
                                if (gp != null) {
                                    if (gp.canDo(uid,Group.groupAction.EditGroup)) {
                                        if (a.ContainsKey("name") && (a["name"].ToString() ?? "").Trim() != "") {
                                            gp.name = a["name"].ToString() ?? "";
                                        }
                                        if (a.ContainsKey("picture") && (a["picture"].ToString() ?? "").Trim() != "") {
                                            gp.picture = a["picture"].ToString() ?? "";
                                        }
                                        if (a.ContainsKey("info") && (a["info"].ToString() ?? "").Trim() != "") {
                                            gp.info = a["info"].ToString() ?? "";
                                        }
                                        if (a.ContainsKey("roles")) {
                                            gp.roles = ((JObject)a["roles"]).ToObject<Dictionary<string, groupRole>>() ?? gp.roles;
                                        }
                                        // backwards compat
                                        Dictionary<string,string> response = new() {
                                            ["groupid"] = gp.groupID
                                        };
                                        res = JsonConvert.SerializeObject(response);
                                        gp.save();
                                    }else {
                                        statuscode = 403;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "Not allowed"));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Group doesn't exist."));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else if (url == "edituser") { //Group role edit
                        var body = new StreamReader(context.Request.InputStream).ReadToEnd();
                        var a = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                        if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("userid") && a.ContainsKey("role")) {
                            string? uid = GetUIDFromToken(a["token"]);
                            if (uid != null) {
                                Group? gp = Group.get(a["groupid"]);
                                if (gp != null) {
                                    if (gp.canDo(uid,Group.groupAction.EditUser)) {
                                        if (gp.members.ContainsKey(a["userid"])) {
                                            if (gp.roles.ContainsKey(a["role"])) {
                                                gp.members[a["userid"]].role = a["role"];
                                                res = JsonConvert.SerializeObject(new serverResponse("done"));
                                                gp.save();
                                            }else {
                                                statuscode = 404;
                                                res = JsonConvert.SerializeObject(new serverResponse("error", "Role doesn't exist."));
                                            }
                                        }else {
                                            statuscode = 404;
                                            res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                                        }
                                    }else {
                                        statuscode = 403;
                                        res = JsonConvert.SerializeObject(new serverResponse("error", "Not allowed"));
                                    }
                                }else {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "Group doesn't exist."));
                                }
                            }else {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                            }
                        }else {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }else { //Ping!!!!
                        res = "Pong!";
                    }
                }catch (Exception e) {
                    statuscode = 500;
                    res = e.ToString();
                    Console.WriteLine(e.ToString());
                }
                if (writeRes) { //Only use this if api wants to (unlike upload and getmedia for example)
                    context.Response.StatusCode = statuscode;
                    context.Response.ContentType = "text/json";
                    byte[] bts = Encoding.UTF8.GetBytes(res);
                    context.Response.OutputStream.Write(bts, 0, bts.Length);
                    context.Response.KeepAlive = false; 
                    context.Response.Close(); 
                }
                //Console.WriteLine("Respone given to a request.");
                respond(); // Restart it for another request
            }
        });
    }

    static void saveData() {
        Console.WriteLine("Saving Data...");
        Console.WriteLine("Saving Chats...");
        foreach (var c in chatsCache) { // Save chats in memory to files
            Console.WriteLine("Saving " + c.Key);
            c.Value.saveChat();
        }
    }

    static void autoSaveTick() {
        Task.Delay(300000).ContinueWith((task) => { //save after 5 mins and recall
            saveData();
            autoSaveTick();
        });
    }

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
            if (args.Length > 1) { // Custom https port
                HTTPSport = args[1];
            }
        }

        Console.WriteLine("Pamukky V3 Server C# REWRITE");
        //Create save folders
        Directory.CreateDirectory("data");
        Directory.CreateDirectory("data/user");
        Directory.CreateDirectory("data/auth");
        Directory.CreateDirectory("data/chat");
        Directory.CreateDirectory("data/upload");
        Directory.CreateDirectory("data/group");
        // Start the server
        _httpListener.Prefixes.Add("http://*:" + HTTPport + "/"); //http prefix
        _httpListener.Prefixes.Add("https://*:" + HTTPSport + "/"); //https prefix
        _httpListener.Start();
        Console.WriteLine("Server started. On ports " + HTTPport + " and " + HTTPSport);
        // Start responding for server
        respond();
        Console.WriteLine("Press any key to exit.");
        autoSaveTick(); // Start the autosave ticker
        Console.Read();
        // After user wants to exit, save "cached" data
        saveData();
    }
}
