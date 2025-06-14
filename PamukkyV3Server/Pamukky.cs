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
using System.Linq;
using System.Net.Http;
using System.Reflection;

using Konscious.Security.Cryptography;
using System.Web;

namespace PamukkyV3;

internal class Program
{
    public const int thumbSize = 256;
    public const int thumbQuality = 75;
    public const string datetimeFormat = "MM dd yyyy, HH:mm zzz";
    static HttpListener _httpListener = new HttpListener();
    static Dictionary<string, loginCred> loginCreds = new();
    static Dictionary<string, userProfile> userProfileCache = new();
    static Dictionary<string, userConfig> userConfigCache = new();
    static Dictionary<string, List<chatItem>> userChatsCache = new();
    static Dictionary<string, Chat> chatsCache = new();
    static Dictionary<string, Group> groupsCache = new();
    static Dictionary<string, userStatus> userstatus = new(); //Current user status that might need to be "private"
    static Notifications notifications = new();
    static string pamukProfile = "{\"name\":\"Pamuk\",\"picture\":\"\",\"description\":\"Birb!!!\"}"; //Direct reply for clients, no need to make class and make it json as it's always the same.

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
    }

    class userConfig //mute status and etc.
    {
        [JsonIgnore]
        public string uid = "";
        public List<string> mutedChats = new();

        public static userConfig? Get(string uid)
        {
            if (userConfigCache.ContainsKey(uid))
            {
                return userConfigCache[uid];
            }
            else
            {
                if (File.Exists("data/info/" + uid + "/profile"))
                { // check if user exists
                    if (File.Exists("data/info/" + uid + "/config")) // check if config file exists
                    {
                        userConfig? uc = JsonConvert.DeserializeObject<userConfig>(File.ReadAllText("data/info/" + uid + "/config"));
                        if (uc != null)
                        {
                            uc.uid = uid;
                            userConfigCache[uid] = uc;
                            return uc;
                        }
                    }
                    else // if doesn't exist, create new one
                    {
                        userConfig uc = new() { uid = uid };
                        userConfigCache[uid] = uc;
                        return uc;
                    }
                }
            }
            return null;
        }

        public void Save()
        {
            File.WriteAllTextAsync("data/info/" + uid + "/config", JsonConvert.SerializeObject(this)); // save to file
        }
    }

    class userStatus
    {
        public string typingChat = "";
        private DateTime? typetime;
        public string user;
        /*public bool getTyping(string chatid) {
            if (typetime == null) {
                return false;
            }else {
                return typetime.Value.AddSeconds(3) > DateTime.Now && chatid == typingChat;
            }
        }*/
        public userStatus(string uid)
        {
            user = uid;
        }

        public void setTyping(string? chatid)
        {
            //Remove typing if null was passed
            if (chatid == null)
            {
                Chat? chat = Chat.getChat(typingChat);
                if (chat != null) chat.remTyping(user);
            }
            else
            {
                //Set user as typing at the chat
                Chat? chat = Chat.getChat(chatid);
                if (chat != null)
                {
                    typetime = DateTime.Now;
                    typingChat = chatid;
                    chat.setTyping(user);
                    Task.Delay(3100).ContinueWith((task) =>
                    { // automatically set user as not typing.
                        if (chatid == typingChat && !(typetime.Value.AddSeconds(3) > DateTime.Now))
                        { //Check if it's the same typing update.
                            typetime = null;
                            chat.remTyping(user);
                        }
                    });
                }
            }
        }
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
        public bool publicgroup = false; //Can the group be read without joining?
        public Dictionary<string, groupMember> members = new();
        public Dictionary<string, groupRole> roles = new();
        public List<string> bannedMembers = new();

        public static Group? get(string gid) {
            if (groupsCache.ContainsKey(gid)) {
                return groupsCache[gid];
            }
            if (File.Exists("data/info/" + gid + "/info")) {
                Group? g = JsonConvert.DeserializeObject<Group>(File.ReadAllText("data/info/" + gid + "/info"));
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
                type = "group"
            };
            addToChats(clist,g); //Add to their chats list
            saveUserChats(uid,clist); //Save their chats list
            return true; //Success!!
        }

        public bool removeUser(string uid) {
            if (!members.ContainsKey(uid)) { // To not mess stuff up
                return true;
            }
            List<chatItem>? clist = GetUserChats(uid); // Get chats list of user
            if (clist == null) {
                return false; //user doesn't exist
            }
            members.Remove(uid); //Goodbye..
            removeFromChats(clist,groupID); //Remove chat from their chats list
            saveUserChats(uid,clist); //Save their chats list
            return true; //Success!!
        }

        public bool banUser(string uid) {
            if (removeUser(uid)) {
                if (!bannedMembers.Contains(uid)) {
                    bannedMembers.Add(uid);
                }
                return true;
            }
            return false;
        }

        public void unbanUser(string uid) {
            bannedMembers.Remove(uid);
        }

        public enum groupAction {
            Kick,
            Ban,
            EditUser,
            EditGroup,
            Read
        }

        public bool canDo(string user,groupAction action,string? target = null) {
            if (action == groupAction.Read && publicgroup) return true;

            bool contains = false;
            groupMember? u = null;
            groupMember? tu = null;
            foreach (var member in members) { //find the user
                if (member.Value.user == user) {
                    contains = true;
                    u = member.Value;
                }
                if (member.Value.user == target) {
                    tu = member.Value;
                }
            }

            if (!contains || u == null) { // Doesn't exist? block
                return false;
            }
            if (action == groupAction.Read) return true;

            // Get the role
            groupRole role = roles[u.role];
            //Check what the role can do depending on the request.

            if (action == groupAction.EditGroup) return role.AllowEditingSettings;
            if (tu != null) {
                groupRole trole = roles[tu.role];
                if (action == groupAction.EditUser) return role.AllowEditingUsers && role.AdminOrder <= trole.AdminOrder;
                if (action == groupAction.Kick) return role.AllowKicking && role.AdminOrder < trole.AdminOrder;
                if (action == groupAction.Ban) return role.AllowBanning && role.AdminOrder < trole.AdminOrder;
            }else {
                if (action == groupAction.Ban) return role.AllowBanning;
            }

            return false;
        }

        public groupRole? getUserRole(string user) {
            bool contains = false;
            groupMember? u = null;
            foreach (var member in members) { //find the user
                if (member.Value.user == user) {
                    contains = true;
                    u = member.Value;
                }
            }

            if (!contains || u == null) { // Doesn't exist? block
                return null;
            }

            if (!roles.ContainsKey(u.role)) { // Doesn't exist? block.
                return null;
            }

            // Get the role
            groupRole role = roles[u.role];

            return role;
        }

        public void save() {
            Directory.CreateDirectory("data/info/" + groupID);
            string c = JsonConvert.SerializeObject(this);
            File.WriteAllTextAsync("data/info/" + groupID + "/info",c);
        }
    }

    class groupInfo { //stripped Group for /getgroup
        public string name = "";
        public string picture = "";
        public string info = "";
        public bool publicgroup = false;
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
        public bool AllowPinningMessages = true;
    }

    class chatMessage { // Chat message.
        public string sender = "";
        public string content = "";
        public string time = "";
        public string? replymsgid;
        public List<string>? files;
        public string? forwardedfrom;
        public messageReactions reactions = new();
        public bool pinned = false;
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
            pinned = msg.pinned;
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
            d["pinned"] = pinned;
            //d["forwardedname"] = forwardedname;
            return d;
        }
    }

    class Chat:OrderedDictionary<string,chatMessage> {
        public string chatid = "";
        public Chat? mainchat = null;
        public bool isgroup = false;
        Group group = new();
        public Dictionary<long,Dictionary<string,object?>> updates = new();
        private Dictionary<string,chatMessageFormatted> formatcache = new();
        public List<string> typingUsers = new();
        public long newid = 0;
        public bool wasUpdated = false;

        int getIndexOfKeyInDictionary(string key)
        {
            for(int i = 0; i < Count; ++i)
            {
                if (Keys.ElementAt(i) == key) return i;
            }
            return -1;
        }

        public void setTyping(string uid) {
            if (!typingUsers.Contains(uid)) {
                typingUsers.Add(uid);
            }
        }

        public void remTyping(string uid) {
            typingUsers.Remove(uid);
        }

        void addupdate(Dictionary<string,object?> update) {
            wasUpdated = true;
            if ((update["event"] ?? "").ToString() == "DELETED")
            {
                string msgid = (update["id"] ?? "").ToString() ?? "";
                int i = 0;
                while (i < updates.Count)
                {
                    var oupdate = updates.ElementAt(i);
                    if (((oupdate.Value["id"] ?? "").ToString() ?? "") == msgid)
                    {
                        updates.Remove(oupdate.Key);
                    }
                    else
                    {
                        i += 1;
                    }
                }
            }
            if ((update["event"] ?? "").ToString() == "REACT") {
                string msgid = (update["id"] ?? "").ToString() ?? "";
                int i = 0;
                while (i < updates.Count) {
                    var oupdate = updates.ElementAt(i);
                    if ((oupdate.Value["id"] ?? "").ToString() == msgid && (oupdate.Value["event"] ?? "").ToString() == "REACT") {
                        updates.Remove(oupdate.Key);
                    }else {
                        i += 1;
                    }
                }
            }
            if ((update["event"] ?? "").ToString() == "UNPINNED") {
                string msgid = (update["id"] ?? "").ToString() ?? "";
                int i = 0;
                while (i < updates.Count) {
                    var oupdate = updates.ElementAt(i);
                    if ((oupdate.Value["id"] ?? "").ToString() == msgid && (oupdate.Value["event"] ?? "").ToString() == "PINNED") {
                        updates.Remove(oupdate.Key);
                    }else {
                        i += 1;
                    }
                }
            }
            if ((update["event"] ?? "").ToString() == "PINNED") {
                string msgid = (update["id"] ?? "").ToString() ?? "";
                int i = 0;
                while (i < updates.Count) {
                    var oupdate = updates.ElementAt(i);
                    if ((oupdate.Value["id"] ?? "").ToString() == msgid && (oupdate.Value["event"] ?? "").ToString() == "UNPINNED") {
                        updates.Remove(oupdate.Key);
                    }else {
                        i += 1;
                    }
                }
            }
            newid += 1;
            updates[newid] = update;
        }

        public Dictionary<long,Dictionary<string,object?>>? getUpdates(string uid, long since) {
            Dictionary<long,Dictionary<string,object?>> updatesSince = new();
            if (updates.Count == 0) {
                return updatesSince;
            }else {
                if (since > updates.Keys.Max()) {
                    return updatesSince;
                }else if (since == 0) {
                    since = updates.Keys.Min();
                }
            }

            //var keysToRemove = new List<long>();
            for(int i = 0; i < updates.Count; ++i)
            {
                long id = updates.Keys.ElementAt(i);
                if (id > since) {
                    Dictionary<string, object?> update;
                    string eventtype = (updates[id]["event"] ?? "").ToString() ?? "";
                    string msgid = (updates[id]["id"] ?? "").ToString() ?? "";
                    if (eventtype == "NEWMESSAGE" || eventtype == "PINNED")
                    {
                        chatMessageFormatted? f = formatMessage(msgid);
                        if (f != null)
                        {
                            update = f.toDictionary();
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        update = new();
                        if (eventtype == "REACTIONS" && ContainsKey(msgid))
                        {
                            update["rect"] = this[msgid].reactions;
                        }
                    }
                    update["event"] = eventtype;
                    update["id"] = msgid;
                    updatesSince.Add(id, update);
                    /*if (!updates[id].ContainsKey("read") || !(updates[id]["read"] is List<string>)) {
                        updates[id]["read"] = new List<string>();
                    }
                    var reads = (List<string>?)updates[id]["read"];
                    if (reads != null && !reads.Contains(uid)) {
                        reads.Add(uid);
                        if (reads.Count == group.members.Count) {
                            keysToRemove.Add(id);
                        }
                    }*/
                };
            }
            /*foreach (long key in keysToRemove) {
                updates.Remove(key);
            }*/
            return updatesSince;
        }

        private chatMessageFormatted? formatMessage(string key) {
            if (formatcache.ContainsKey(key)) return formatcache[key];
            if (ContainsKey(key)) {
                chatMessageFormatted formatted = new chatMessageFormatted(this[key]);
                formatcache[key] = formatted;
                if (formatted.replymsgid != null) {
                    //chatMessageFormatted? innerformatted = formatMessage(formatted.replymsgid);
                    var chat = mainchat ?? this;// Get the message from the full chat, as page might not contain it. if chat is null, use this chat (could be a page only).
                    //Console.WriteLine(mainchat == null ? "null!" : "exists");
                    if (chat.ContainsKey(formatted.replymsgid)) { // Check if message exists
                        var message = chat[formatted.replymsgid];
                        formatted.replymsgcontent = message.content;
                        formatted.replymsgsender = message.sender;
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
        /*public Chat getPage(int page = 0) {
            if (Count - 1 > pagesize) {
                Chat rtrn = new() {chatid = chatid, mainchat = this};
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
        }*/
        public Chat getMessages(string prefix = "") {
            //Console.WriteLine(prefix);
            Chat chat = new() {chatid = chatid, mainchat = this};
            if (prefix.Contains("-")) {
                string[] split = prefix.Split("-");
                string? msgid1 = getMessageIDFromPrefix(split[0]);
                string? msgid2 = getMessageIDFromPrefix(split[1]);
                //Console.WriteLine("from: " + msgid1 + " To: " + msgid2);
                //Check if they exist
                if (msgid1 != null && msgid2 != null) {
                    int i1 = getIndexOfKeyInDictionary(msgid1);
                    int i2 = getIndexOfKeyInDictionary(msgid2);
                    int fromi = 0;
                    int toi = 0;
                    if (i1 > i2) {
                        fromi = i2;
                        toi = i1;
                    }else {
                        fromi = i1;
                        toi = i2;
                    }
                    //Console.WriteLine("from: " + fromi + " To: " + toi);
                    int index = fromi;
                    //Console.WriteLine(Count);
                    while (index <= toi) {
                        //Console.WriteLine(index);
                        chat.Add(Keys.ElementAt(index),Values.ElementAt(index));
                        index += 1;
                    }
                }else {
                    //Console.WriteLine("Failled null match");
                }
            }else {
                string? id = getMessageIDFromPrefix(prefix);
                if (id != null) chat.Add(id, this[id]);
            }
            return chat;
        }

        public string? getMessageIDFromPrefix(string prefix) {
            if (Count == 0)  {
                return null;
            }
            if (prefix.StartsWith("#^")) { //Don't catch errors, entire thing should fail already.
                int id = int.Parse(prefix.Replace("#^",""));
                if (id >= Count)
                {
                    id = Count - 1;
                }
                if (id > -1) return Keys.ElementAt(id);
            }else if (prefix.StartsWith("#")) {
                int idx = int.Parse(prefix.Replace("#", ""));
                if (idx >= Count) {
                    idx = Count - 1;
                }
                int id = (Count - 1) - idx;
                if (id < Count && id > -1) return Keys.ElementAt(id);
            }else {
                if (ContainsKey(prefix)) {
                    return prefix;
                }
            }
            return null;
        }

        public chatMessage? getLastMessage() {
            if (Count > 0) {
                return Values.ElementAt(Count - 1);
            }
            return null;
        }

        public void sendMessage(chatMessage msg, bool notify = true) {
            newid++;
            string id = newid.ToString();
            Add(id,msg);
            Dictionary<string,object?> update = new();
            update["event"] = "NEWMESSAGE";
            update["id"] = id;
            addupdate(update);
            if (notify) {
                var notification = new userNotification() {
                    user = profileShort.fromProfile(GetUserProfile(msg.sender)), //Probably would stay like this
                    content = msg.content,
                    chatid = chatid
                };
                foreach (string member in group.members.Keys) {
                    if (msg.sender != member)
                    {
                        userConfig? uc = userConfig.Get(member);
                        if (uc != null && !uc.mutedChats.Contains(chatid))
                        {
                            notifications.Get(member).Add(id, notification);
                        }
                    }
                }
            }
            
        }

        public void deleteMessage(string msgid) {
            Remove(msgid);
            Dictionary<string,object?> update = new();
            update["event"] = "DELETED";
            update["id"] = msgid;
            addupdate(update);
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
                update["id"] = msgid;
                addupdate(update);
                return rect;
            }
            return new();
        }

        public bool pinMessage(string msgid) {
            if (ContainsKey(msgid)) {
                this[msgid].pinned = !this[msgid].pinned;
                chatMessageFormatted? f = formatMessage(msgid);
                if (f != null) {
                    Dictionary<string,object?> update = new();
                    update["event"] = this[msgid].pinned ? "PINNED" : "UNPINNED";
                    update["id"] = msgid;
                    addupdate(update);
                }
                if (formatcache.ContainsKey(msgid)) {
                    formatcache[msgid].pinned = this[msgid].pinned;
                }
                return this[msgid].pinned;
            }
            return false;
        }

        public Chat getPinned() {
            Chat rtrn = new() {chatid = chatid, mainchat = this};
            foreach (var kv in this) {
                if (kv.Value.pinned) {
                    rtrn[kv.Key] = kv.Value;
                }
            }
            return rtrn;
        }

        public enum chatAction {
            Read,
            Send,
            React,
            Delete,
            Pin
        }

        public bool canDo(string user,chatAction action,string msgid = "") {
            if (action == chatAction.Read && group.publicgroup) return true;

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
            if (u.role != "" && group.roles.ContainsKey(u.role)) { //is this a real group user?
                //Then it's a real group
                groupRole role = group.roles[u.role];
                if (action == chatAction.React) return role.AllowSendingReactions;
                if (action == chatAction.Send) return role.AllowSending;
                if (action == chatAction.Delete) return role.AllowMessageDeleting;
                if (action == chatAction.Pin) return role.AllowPinningMessages;
            }
            return true;
        }

        public static Chat? getChat(string chat) {
            if (chatsCache.ContainsKey(chat)) {
                return chatsCache[chat];
            }
            //Check validity
            if (chat.Contains("-"))
            { //both users should exist
                string[] spl = chat.Split("-");
                if (!File.Exists("data/info/" + spl[0] + "/profile"))
                {
                    return null;
                }
                if (!File.Exists("data/info/" + spl[1] + "/profile"))
                {
                    return null;
                }
            }
            else
            {
                if (!File.Exists("data/info/" + chat + "/info"))
                {
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
                if (File.Exists("data/chat/" + chat + "/updates")) {
                    var u = JsonConvert.DeserializeObject<Dictionary<long,Dictionary<string,object?>>>(File.ReadAllText("data/chat/" + chat + "/updates"));
                    if (u != null) c.updates = u;
                }
                c.chatid = chat;
                c.isgroup = !chat.Contains("-");
                c.newid = DateTime.Now.Ticks;
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
            if (wasUpdated)
            {
                Directory.CreateDirectory("data/chat/" + chatid);
                string c = JsonConvert.SerializeObject(this);
                File.WriteAllTextAsync("data/chat/" + chatid + "/chat", c);
                string u = JsonConvert.SerializeObject(updates);
                File.WriteAllTextAsync("data/chat/" + chatid + "/updates", u);
                wasUpdated = false;
            }
        }
    }

    class chatItem { //Chats list item
        public string? chatid;
        public string type = "";

        // Optional because depends on it's type.
        public string? user = null;
        public string? group = null;

        //for preview at chats list, TODO: SET NULL BEFORE SAVE
        //public profileShort? info = null;
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
        public string? code;

        public serverResponse(string stat, string? scode = null, string? descript = null) { //for easier creation
            status = stat;
            description = descript;
            code = scode;
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
    
    class actionReturn
    {
        public string res = "";
        public int statuscode = 200;
    }

    static string hashpassword(string pass, string uid)
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

    static userStatus? getuserstatus(string uid) {
        if (userstatus.ContainsKey(uid)) {
            return userstatus[uid];
        }else {
            if (File.Exists("data/info/" + uid + "/profile")) { // check
                userStatus us = new userStatus(uid);
                userstatus[uid] = us;
                return us;
            }
        }
        return null;
    }

    static userProfile? GetUserProfile(string uid) {
        if (userProfileCache.ContainsKey(uid)) {
            return userProfileCache[uid];
        }else {
            if (File.Exists("data/info/" + uid + "/profile")) { // check
                userProfile? up = JsonConvert.DeserializeObject<userProfile>(File.ReadAllText("data/info/" + uid + "/profile"));
                if (up != null) {
                    userProfileCache[uid] = up;
                    return up;
                }
            }
        }
        return null;
    }

    static void SetUserProfile(string uid, userProfile up)
    {
        userProfileCache[uid] = up; //set
        File.WriteAllTextAsync("data/info/" + uid + "/profile", JsonConvert.SerializeObject(up)); //save
    }

    static loginCred? GetLoginCred(string token, bool preventbypass = true) {
        //!preventbypass is when you wanna use the token to get other info
        if (token.Contains("@") && preventbypass) { //bypassing
            return null;
        }
        if (loginCreds.ContainsKey(token)) {
            return loginCreds[token];
        }else {
            if (File.Exists("data/auth/" + token)) {
                loginCred? up = JsonConvert.DeserializeObject<loginCred>(File.ReadAllText("data/auth/" + token));
                if (up != null) {
                    loginCreds[token] = up;
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
            if (File.Exists("data/info/" + uid + "/chatslist")) {
                List<chatItem>? uc = JsonConvert.DeserializeObject<List<chatItem>>(File.ReadAllText("data/info/" + uid + "/chatslist"));
                if (uc != null) {
                    userChatsCache[uid] = uc;
                    return uc;
                }
            }else {
                if (Directory.Exists("data/info/" + uid)) {
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

    static void removeFromChats(List<chatItem> list, string chatid) { //Remove from chats list
        var itm = list.Where(i => (i.chatid ?? i.group ?? "") == chatid).FirstOrDefault();
        if (itm != null) list.Remove(itm);
    }

    static void saveUserChats(string uid, List<chatItem> list) { //Chats list
        userChatsCache[uid] = list;
        File.WriteAllText("data/info/" + uid + "/chatslist", JsonConvert.SerializeObject(list));
    }

    static actionReturn doaction(string action, string body)
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

                            a.Password = hashpassword(a.Password, uid);

                            string token = "";
                            do
                            {
                                token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                            }
                            while (loginCreds.ContainsKey(token));

                            a.userID = uid;
                            //a.token = token;

                            //Console.WriteLine(a.Password);
                            userProfile up = new() { name = a.EMail.Split("@")[0].Split(".")[0] };
                            Directory.CreateDirectory("data/info/" + uid);
                            string astr = JsonConvert.SerializeObject(a);
                            //File.WriteAllText("data/auth/" + token, astr);
                            loginCreds[token] = a;
                            File.WriteAllText("data/auth/" + a.EMail, astr);
                            SetUserProfile(uid, up);
                            List<chatItem>? chats = GetUserChats(uid); //get new user's chats list
                            if (chats != null)
                            {
                                chatItem savedmessages = new()
                                { //automatically add saved messages for the user.
                                    user = uid,
                                    type = "user",
                                    chatid = uid + "-" + uid
                                };
                                addToChats(chats, savedmessages);
                                saveUserChats(uid, chats); //save it
                            }
                            else
                            {
                                Console.WriteLine("Signup chatslist was null!!!"); //log if weirdo
                            }
                            //Done
                            res = JsonConvert.SerializeObject(new loginResponse(token, uid));
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "WPFORMAT", "Password format wrong."));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "WEFORMAT", "Invalid E-Mail."));
                    }
                }
                else
                {
                    statuscode = 401;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "USEREXISTS", "User already exists."));
                }

            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
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
                    loginCred? lc = JsonConvert.DeserializeObject<loginCred>(File.ReadAllText("data/auth/" + a.EMail));
                    if (lc != null)
                    {
                        string uid = lc.userID;
                        a.Password = hashpassword(a.Password, uid);
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
                            res = JsonConvert.SerializeObject(new loginResponse(token, uid));
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "WRONGLOGIN", "Incorrect login"));
                        }
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new serverResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }

            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "changepassword")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("password") && a.ContainsKey("oldpassword"))
            {
                if (a["password"].Trim().Length >= 6)
                {
                    loginCred? lc = GetLoginCred(a["token"]);
                    if (lc != null)
                    {
                        if (lc.Password == hashpassword(a["oldpassword"], lc.userID))
                        {
                            /*string token = "";
                                *                   do
                                *                   {
                                *                       token =  Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=","").Replace("+","").Replace("/","");
                        }
                        while (loginCreds.ContainsKey(token));*/
                            //File.Delete("data/auth/" + lc.token);
                            //lc.token = token;
                            lc.Password = hashpassword(a["password"].Trim(), lc.userID);
                            string astr = JsonConvert.SerializeObject(lc);
                            //File.WriteAllText("data/auth/" + token, astr);
                            File.WriteAllText("data/auth/" + lc.EMail, astr);
                            //Find other logins
                            var tokens = loginCreds.Where(lco => lco.Value.userID == lc.userID && lc != lco.Value);
                            foreach (var token in tokens)
                            {
                                //remove the logins.
                                loginCreds.Remove(token.Key);
                            }
                            res = astr;
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "WPFORMAT", "Password format wrong."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "logout")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                loginCreds.Remove(a["token"]);
                res = JsonConvert.SerializeObject(new serverResponse("done"));
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
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
                    userProfile? up = GetUserProfile(a["uid"]);
                    if (up != null)
                    {
                        res = JsonConvert.SerializeObject(up);
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getonline")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("uid"))
            {
                userProfile? up = GetUserProfile(a["uid"]);
                if (up != null)
                {
                    res = up.getOnline();
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "setonline")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    userProfile? user = GetUserProfile(uid);
                    if (user != null)
                    {
                        user.setOnline();
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "updateuser")
        { //User profile edit
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    userProfile? user = GetUserProfile(uid);
                    if (user != null)
                    {
                        if (a.ContainsKey("name") && a["name"].Trim() != "")
                        {
                            user.name = a["name"].Trim().Replace("\n", "");
                        }
                        if (!a.ContainsKey("picture"))
                        {
                            a["picture"] = "";
                        }
                        if (!a.ContainsKey("description"))
                        {
                            a["description"] = "";
                        }
                        user.picture = a["picture"];
                        user.description = a["description"].Trim();
                        SetUserProfile(uid, user);
                        res = JsonConvert.SerializeObject(new serverResponse("done"));
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getchatslist")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    List<chatItem>? chats = GetUserChats(uid);
                    if (chats != null)
                    {
                        foreach (chatItem item in chats)
                        { //Format chats for clients
                            /*if (item.type == "user") { //Info
                                *                           var p = GetUserProfile(item.user ?? "");
                                *                           item.info = profileShort.fromProfile(p);
                        }else if (item.type == "group") {
                            var p = Group.get(item.group ?? "");
item.info = profileShort.fromGroup(p);
                        }*/

                            Chat? chat = Chat.getChat(item.chatid ?? item.group ?? "");
                            if (chat != null)
                            {
                                if (chat.canDo(uid, Chat.chatAction.Read))
                                { //Check for read permission before giving the last message
                                    item.lastmessage = chat.getLastMessage();
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
                        res = JsonConvert.SerializeObject(new serverResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getnotifications")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    //Console.WriteLine(JsonConvert.SerializeObject(notifications));
                    var usernotifies = notifications.Get(uid);
                    res = JsonConvert.SerializeObject(usernotifies);
                    List<string> keys = new();
                    foreach (string key in usernotifies.Keys)
                    {
                        keys.Add(key);
                    }
                    Task.Delay(10000).ContinueWith((task) =>
                    { //remove notifications after delay so all clients can see it before it's too late. SSSOOOOBBB
                        foreach (string key in keys)
                        {
                            usernotifies.Remove(key);
                        }
                    });
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getmutedchats")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    userConfig? userconfig = userConfig.Get(uid);
                    if (userconfig != null)
                    {
                        res = JsonConvert.SerializeObject(userconfig.mutedChats);
                    }
                    else
                    {
                        statuscode = 500;
                        res = JsonConvert.SerializeObject(new serverResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "mutechat")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("toggle") && a["toggle"] is bool)
            {
                string? uid = GetUIDFromToken((a["token"] ?? "").ToString() ?? "");
                if (uid != null)
                {
                    userConfig? userconfig = userConfig.Get(uid);
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
                        res = JsonConvert.SerializeObject(new serverResponse("done"));
                    }
                    else
                    {
                        statuscode = 500;
                        res = JsonConvert.SerializeObject(new serverResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "adduserchat")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("email"))
            {
                string? uidu = GetUIDFromToken(a["token"]);
                string? uidb = GetUIDFromToken(a["email"], false);
                if (uidu != null && uidb != null)
                {
                    List<chatItem>? chatsu = GetUserChats(uidu);
                    List<chatItem>? chatsb = GetUserChats(uidb);
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
                        addToChats(chatsu, u);
                        addToChats(chatsb, b);
                        saveUserChats(uidu, chatsu);
                        saveUserChats(uidb, chatsb);
                        res = JsonConvert.SerializeObject(new serverResponse("done"));
                    }
                    else
                    {
                        statuscode = 500;
                        res = JsonConvert.SerializeObject(new serverResponse("error"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getchatpage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"]);
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
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getmessages")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("prefix"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        {
                            res = JsonConvert.SerializeObject(chat.getMessages(a["prefix"]).format());
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getpinnedmessages")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        {
                            res = JsonConvert.SerializeObject(chat.getPinned().format());
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
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
                    string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                    if (uid != null)
                    {
                        Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
                        if (chat != null)
                        {
                            if (chat.canDo(uid, Chat.chatAction.Send))
                            {
                                chatMessage msg = new()
                                {
                                    sender = uid,
                                    content = (a["content"].ToString() ?? "").Trim(),
                                    replymsgid = a.ContainsKey("replymsg") ? a["replymsg"].ToString() : null,
                                    files = files,
                                    time = datetostring(DateTime.Now)
                                };
                                chat.sendMessage(msg);
                                var userstatus = getuserstatus(uid);
                                if (userstatus != null)
                                {
                                    userstatus.setTyping(null);
                                }
                                res = JsonConvert.SerializeObject(new serverResponse("done"));
                            }
                            else
                            {
                                statuscode = 401;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                            }
                        }
                        else
                        {
                            statuscode = 404;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new serverResponse("error"));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "deletemessage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgs"))
            {
                string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
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
                            res = JsonConvert.SerializeObject(new serverResponse("done"));
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "pinmessage")
        { //More like a toggle
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgs"))
            {
                string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
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
                                    chat.pinMessage(msgid);
                                }
                            }
                            res = JsonConvert.SerializeObject(new serverResponse("done"));
                        }
                        else
                        {
                            statuscode = 411;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "sendreaction")
        { //More like a toggle
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgid") && a.ContainsKey("reaction") && (a["reaction"].ToString() ?? "") != "")
            {
                string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
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
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "savemessage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgs"))
            {
                string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
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
                                        Chat? uchat = Chat.getChat(uid + "-" + uid);
                                        if (uchat != null)
                                        {
                                            chatMessage message = new()
                                            {
                                                sender = chat[msgid].sender,
                                                content = chat[msgid].content,
                                                files = chat[msgid].files,
                                                time = datetostring(DateTime.Now)
                                            };
                                            uchat.sendMessage(message, false);

                                        }
                                    }
                                }
                                res = JsonConvert.SerializeObject(new serverResponse("done"));
                            }
                            else
                            {
                                statuscode = 401;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                            }
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "forwardmessage")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid") && a.ContainsKey("msgs") && a.ContainsKey("tochats"))
            {
                string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"].ToString() ?? "");
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
                                                Chat? uchat = Chat.getChat(chatid.ToString() ?? "");
                                                if (uchat != null)
                                                {
                                                    if (uchat.canDo(uid, Chat.chatAction.Send))
                                                    {
                                                        chatMessage message = new()
                                                        {
                                                            forwardedfrom = chat[msgid].sender,
                                                            sender = uid,
                                                            content = chat[msgid].content,
                                                            files = chat[msgid].files,
                                                            time = datetostring(DateTime.Now)
                                                        };
                                                        uchat.sendMessage(message);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            res = JsonConvert.SerializeObject(new serverResponse("done"));
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getupdates")
        { //Chat updates
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("id") && a.ContainsKey("since"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["id"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        { //Check if user can even "read" it at all
                            res = JsonConvert.SerializeObject(chat.getUpdates(uid, long.Parse(a["since"])));
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "settyping")
        { //Set user as typing at a chat
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Send))
                        { //Ofc, if the user has the permission to send at the chat
                            var userstatus = getuserstatus(uid);
                            if (userstatus != null)
                            {
                                userstatus.setTyping(chat.chatid);
                                res = JsonConvert.SerializeObject(new serverResponse("done"));
                            }
                            else
                            {
                                statuscode = 500;
                                res = JsonConvert.SerializeObject(new serverResponse("error"));
                            }
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "gettyping")
        { //Get typing users in a chat
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("chatid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Chat? chat = Chat.getChat(a["chatid"]);
                    if (chat != null)
                    {
                        if (chat.canDo(uid, Chat.chatAction.Read))
                        { //Ofc, if the user has the permission to read the chat
                            res = JsonConvert.SerializeObject(chat.typingUsers);
                        }
                        else
                        {
                            statuscode = 401;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "You don't have permission to do this action."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ECHAT", "Couldn't open chat. Is it valid????"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "creategroup")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token"))
            {
                string? uid = GetUIDFromToken(a["token"]);
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
                            time = datetostring(DateTime.Now),
                            roles = new()
                            { //Default roles
                                ["Owner"] = new groupRole()
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
                                ["Admin"] = new groupRole()
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
                                ["Moderator"] = new groupRole()
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
                                ["Normal"] = new groupRole()
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
                                ["Readonly"] = new groupRole()
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
                        g.addUser(uid, "Owner");
                        g.save();
                        Dictionary<string, string> response = new()
                        {
                            ["groupid"] = id
                        };
                        res = JsonConvert.SerializeObject(response);
                    }
                    else
                    {
                        statuscode = 411;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOINFO", "No group info"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getgroup")
        { //get group info
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                string uid = GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = Group.get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(new groupInfo()
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
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getinfo")
        { //get user or group info
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("id"))
            {
                string uid = GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                if (a["id"] == "0")
                {
                    res = pamukProfile;
                }
                else
                {
                    userProfile? up = GetUserProfile(a["id"]);
                    if (up != null)
                    {
                        res = JsonConvert.SerializeObject(up);
                    }
                    else
                    {
                        Group? gp = Group.get(a["id"]);
                        if (gp != null)
                        {
                            if (gp.canDo(uid, Group.groupAction.Read))
                            {
                                res = JsonConvert.SerializeObject(new groupInfo()
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
                                res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                            }
                        }
                        else
                        {
                            statuscode = 404;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                        }
                    }
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getgroupusers" || action == "getgroupmembers")
        { //getgroupmembers is new name, gets the members list in json format.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                string uid = GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = Group.get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(gp.members);
                    }
                    else
                    {
                        statuscode = 403;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getbannedgroupmembers")
        { //gets banned group members in the group
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                string uid = GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = Group.get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(gp.bannedMembers);
                    }
                    else
                    {
                        statuscode = 403;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getgroupuserscount" || action == "getgroupmemberscount")
        { //getgroupmemberscount is new name, returns group member count as string. like "5"
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                Group? gp = Group.get(a["groupid"]);
                string uid = GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = gp.members.Count.ToString();
                    }
                    else
                    {
                        statuscode = 403;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getgrouproles")
        { //get all group roles
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("groupid"))
            {
                string uid = GetUIDFromToken(a.ContainsKey("token") ? a["token"] : "") ?? "";
                Group? gp = Group.get(a["groupid"]);
                if (gp != null)
                {
                    if (gp.canDo(uid, Group.groupAction.Read))
                    {
                        res = JsonConvert.SerializeObject(gp.roles);
                    }
                    else
                    {
                        statuscode = 403;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "getgrouprole")
        { //Group role for current user
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = Group.get(a["groupid"]);
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
                                res = JsonConvert.SerializeObject(new groupRole()
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
                                res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                            }
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "joingroup")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = Group.get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.addUser(uid))
                        {
                            Dictionary<string, string> response = new()
                            {
                                ["groupid"] = gp.groupID
                            };
                            res = JsonConvert.SerializeObject(response);
                            gp.save();
                        }
                        else
                        {
                            statuscode = 500;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "leavegroup")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = Group.get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.removeUser(uid))
                        {
                            gp.save();
                            res = JsonConvert.SerializeObject(new serverResponse("done"));
                        }
                        else
                        {
                            statuscode = 500;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "kickuser")
        { //Kicks a user from the group. They can rejoin.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("uid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = Group.get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.canDo(uid, Group.groupAction.Kick, a["uid"] ?? ""))
                        {
                            if (gp.removeUser(a["uid"] ?? ""))
                            {
                                gp.save();
                                res = JsonConvert.SerializeObject(new serverResponse("done"));
                            }
                            else
                            {
                                statuscode = 500;
                                res = JsonConvert.SerializeObject(new serverResponse("error"));
                            }
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "banuser")
        { //bans a user from the group. they can't join until they are unbanned.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("uid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = Group.get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.canDo(uid, Group.groupAction.Ban, a["uid"] ?? ""))
                        {
                            if (gp.banUser(a["uid"] ?? ""))
                            {
                                gp.save();
                                res = JsonConvert.SerializeObject(new serverResponse("done"));
                            }
                            else
                            {
                                statuscode = 500;
                                res = JsonConvert.SerializeObject(new serverResponse("error"));
                            }
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "unbanuser")
        { //Unbans a user, they can rejoin.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("uid"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = Group.get(a["groupid"]);
                    if (gp != null)
                    {
                        if (gp.canDo(uid, Group.groupAction.Ban, a["uid"] ?? ""))
                        {
                            gp.unbanUser(a["uid"] ?? "");
                            gp.save();
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "editgroup")
        {
            var a = JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid"))
            {
                string? uid = GetUIDFromToken(a["token"].ToString() ?? "");
                if (uid != null)
                {
                    Group? gp = Group.get(a["groupid"].ToString() ?? "");
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
                                var roles = ((JObject)a["roles"]).ToObject<Dictionary<string, groupRole>>() ?? gp.roles;
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
                            gp.save();
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else if (action == "edituser")
        { //Edits role of user in the group.
            var a = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
            if (a != null && a.ContainsKey("token") && a.ContainsKey("groupid") && a.ContainsKey("userid") && a.ContainsKey("role"))
            {
                string? uid = GetUIDFromToken(a["token"]);
                if (uid != null)
                {
                    Group? gp = Group.get(a["groupid"]);
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
                                            res = JsonConvert.SerializeObject(new serverResponse("done"));
                                            gp.save();
                                        }
                                        else
                                        {
                                            statuscode = 403;
                                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed to set more than current role"));
                                        }
                                    }
                                    else
                                    {
                                        statuscode = 500;
                                        res = JsonConvert.SerializeObject(new serverResponse("error"));
                                    }
                                }
                                else
                                {
                                    statuscode = 404;
                                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOROLE", "Role doesn't exist."));
                                }
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                            }
                        }
                        else
                        {
                            statuscode = 403;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "ADENIED", "Not allowed"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOGROUP", "Group doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 404;
                    res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                }
            }
            else
            {
                statuscode = 411;
                res = JsonConvert.SerializeObject(new serverResponse("error"));
            }
        }
        else
        { //Ping!!!!
            res = "Pong!";
        }
        actionReturn ret = new() {res = res, statuscode = statuscode};
        return ret;
    }


    static void respond(HttpListenerContext context)
    {
        if (context.Request.Url == null)
        { //just ignore
            return;
        }
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
            new StreamReader(context.Request.InputStream).ReadToEndAsync().ContinueWith((Task<string> bdy) =>
            {
                if (bdy.IsCompletedSuccessfully)
                {
                    string body = bdy.Result;
                    try
                    {
                        if (url == "multi")
                        {
                            Dictionary<string,string>? actionRequests = JsonConvert.DeserializeObject<Dictionary<string,string>>(body);
                            if (actionRequests != null)
                            {
                                Dictionary<string, actionReturn> responses = new();
                                foreach (var request in actionRequests)
                                {
                                    actionReturn actionreturn = doaction(request.Key.Split("|")[0], request.Value);
                                    responses[request.Key] = actionreturn;
                                }
                                res = JsonConvert.SerializeObject(responses);
                            }
                        }
                        else
                        {
                            actionReturn actionreturn = doaction(url, body);
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
                    context.Response.StatusCode = statuscode;
                    context.Response.ContentType = "text/json";
                    byte[] bts = Encoding.UTF8.GetBytes(res);
                    context.Response.OutputStream.Write(bts, 0, bts.Length);
                    context.Response.Close();
                }
            });
        }
        else
        { //Upload call
            if (url == "upload")
            {
                if (context.Request.Headers["token"] != null)
                {
                    string? uid = GetUIDFromToken(context.Request.Headers["token"] ?? "");
                    if (uid != null)
                    {
                        if (context.Request.Headers["content-length"] != null)
                        {
                            int contentLength = int.Parse(context.Request.Headers["content-length"] ?? "0");
                            if (contentLength != 0)
                            {
                                string id = "";
                                do
                                {
                                    id = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
                                }
                                while (File.Exists("data/upload/" + id));
                                string? filename = context.Request.Headers["filename"];
                                //if (filename == null) {
                                filename = id;
                                //}else {
                                //    filename = filename + id;
                                //}
                                string fpname = filename.Replace(".", "").Replace("/", "").Replace("\\", "");
                                var stream = context.Request.InputStream;
                                //stream.Seek(0, SeekOrigin.Begin);
                                var fileStream = File.Create("data/upload/" + fpname + ".file");
                                var cp = stream.CopyToAsync(fileStream);
                                cp.ContinueWith((Task cpt) =>
                                {
                                    if (cpt.IsCompletedSuccessfully)
                                    {
                                        fileStream.Close();
                                        fileStream.Dispose();
                                        fileUpload u = new()
                                        {
                                            size = contentLength,
                                            actualName = context.Request.Headers["filename"] ?? id,
                                            sender = uid,
                                            contentType = context.Request.Headers["content-type"] ?? ""
                                        };

                                        string? uf = JsonConvert.SerializeObject(u);
                                        if (uf == null) throw new Exception("???");
                                        File.WriteAllText("data/upload/" + fpname, uf);

                                        res = JsonConvert.SerializeObject(new fileUploadResponse("success", "%SERVER%getmedia?file=" + fpname));
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
                                });
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "NOFILE", "No file."));
                            }
                        }
                        else
                        {
                            statuscode = 404;
                            res = JsonConvert.SerializeObject(new serverResponse("error", "NOFILE", "No file."));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOUSER", "User doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new serverResponse("error"));
                }
            }
            else if (url.StartsWith("getmedia"))
            { //Needs improvement
                if (context.Request.QueryString["file"] != null)
                {
                    string file = context.Request.QueryString["file"] ?? "";
                    string type = context.Request.QueryString["type"] ?? "";
                    if (File.Exists("data/upload/" + file))
                    {
                        fileUpload? f = JsonConvert.DeserializeObject<fileUpload>(File.ReadAllText("data/upload/" + file));
                        if (f != null)
                        {
                            //context.Response.AddHeader("Content-Length", f.size.ToString());
                            if (context.Request.Headers["sec-fetch-dest"] != "document")
                            {
                                context.Response.AddHeader("Content-Disposition", "attachment; filename=" + HttpUtility.UrlEncode(f.actualName));
                            }
                            context.Response.StatusCode = statuscode;
                            string path = "data/upload/" + file + "." + (type == "thumb" ? "thumb" : "file");
                            if (File.Exists(path))
                            {
                                var fileStream = File.OpenRead(path);
                                context.Response.KeepAlive = false;
                                var cp = fileStream.CopyToAsync(context.Response.OutputStream);
                                cp.ContinueWith((Task cpt) =>
                                {
                                    if (cpt.IsCompletedSuccessfully) { context.Response.Close(); fileStream.Close(); fileStream.Dispose(); }
                                });
                            }
                            else
                            {
                                statuscode = 404;
                                res = JsonConvert.SerializeObject(new serverResponse("error", "File doesn't exist, could be the thumbnail."));
                                //mediaProcesserJobs.Add(file); //Generate it for next visits. (well, could be used to spam the server. ig just ignore old images for now.)
                            }
                        }
                        else
                        {
                            statuscode = 500;
                            res = JsonConvert.SerializeObject(new serverResponse("error"));
                        }
                    }
                    else
                    {
                        statuscode = 404;
                        res = JsonConvert.SerializeObject(new serverResponse("error", "NOFILE", "File doesn't exist."));
                    }
                }
                else
                {
                    statuscode = 411;
                    res = JsonConvert.SerializeObject(new serverResponse("error"));
                }
            }
        }
        //Console.WriteLine("Respone given to a request.");
    }

    static void respondcall(IAsyncResult result) {
        HttpListener? listener = (HttpListener?) result.AsyncState;
        if (listener == null) return;
        HttpListenerContext? context = listener.EndGetContext(result);
        _httpListener.BeginGetContext(new AsyncCallback(respondcall),_httpListener);
        if (context != null) {
            respond(context);
        }
    }

    static void saveData() {
        Console.WriteLine("Saving Data...");
        Console.WriteLine("Saving Chats...");
        foreach (var c in chatsCache) { // Save chats in memory to files
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
            if (args.Length > 1) { // Custom https port
                HTTPSport = args[1];
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
        _httpListener.BeginGetContext(new AsyncCallback(respondcall),_httpListener);
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
