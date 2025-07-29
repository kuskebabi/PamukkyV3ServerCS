using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Class for message structure
/// </summary>
class ChatMessage
{ // Chat message.
    public string sender = "";
    public string content = "";
    public string time = "";
    public string? replymsgid;
    public List<string>? files;
    public string? forwardedfrom;
    public MessageReactions reactions = new();
    public bool pinned = false;
}

/// <summary>
/// Class for a single reaction structure,
/// MessageReactions[] > MessageEmojiReactions[] > MessageReaction
/// </summary>
class MessageReaction
{
    public string reaction = "";
    public string sender = "";
    public string time = "";
}

/// <summary>
/// Class for reactions for a emoji,
/// MessageReactions[] > MessageEmojiReactions[] > MessageReaction
/// </summary>
class MessageEmojiReactions : ConcurrentDictionary<string, MessageReaction> { } //Single reaction emoji

/// <summary>
/// Class for handling all message reactions.
/// </summary>
class MessageReactions : ConcurrentDictionary<string, MessageEmojiReactions>
{ // All reactions
    /// <summary>
    /// Remove MessageEmojiReactions without any items.
    /// </summary>
    public void Update()
    {
        List<string> keysToRemove = new();
        foreach (var mer in this)
        {
            if (mer.Value.Count == 0)
            {
                keysToRemove.Add(mer.Key);
            }
        }
        foreach (string k in keysToRemove)
        {
            this.Remove(k, out _);
        }
    }

    /// <summary>
    /// Get MessageEmojiReactions
    /// </summary>
    /// <param name="reaction">Emoji for reactions</param>
    /// <param name="addNewToDict">Sets if the emoji should be added to the dictionary.</param>
    /// <returns></returns>
    public MessageEmojiReactions Get(string reaction, bool addNewToDictionary = false)
    {
        if (ContainsKey(reaction))
        {
            return this[reaction];
        }
        MessageEmojiReactions d = new();
        if (addNewToDictionary) this[reaction] = d;
        return d;
    }
}

/// <summary>
/// Chat file/attachment structure
/// </summary>
class ChatFile
{
    public string url = "";
    public string? name;
    public int? size;
}

/// <summary>
/// Chat message formatted to send to clients.
/// </summary>
class ChatMessageFormatted : ChatMessage
{ // Chat message formatted for client sending.
    //public profileShort? senderuser; REMOVED. Data waste.
    public string? replymsgcontent;
    public string? replymsgsender;

    public List<ChatFile>? gImages;
    public List<ChatFile>? gVideos;
    public List<ChatFile>? gAudio;
    public List<ChatFile>? gFiles;
    //public string? forwardedname; REMOVED. Data waste.
    public ChatMessageFormatted(ChatMessage msg)
    {
        sender = msg.sender;
        content = msg.content;
        time = msg.time;
        replymsgid = msg.replymsgid;
        files = msg.files;
        reactions = msg.reactions;
        forwardedfrom = msg.forwardedfrom;
        pinned = msg.pinned;
        //senderuser = profileShort.fromProfile(userProfile.Get(sender));
        //if (forwardedfrom != null) {
        //    forwardedname = profileShort.fromProfile(userProfile.Get(forwardedfrom)).name;
        //}

        if (files != null)
        { //Group files in the message to types.
            foreach (string fi in files)
            {
                string fil = fi.Replace(Federation.thisServerURL, "%SERVER%");
                if (fil.Contains("%SERVER%"))
                {
                    string file = fil.Replace("%SERVER%getmedia?file=", "data/upload/"); //Get path
                    if (File.Exists(file))
                    {
                        Pamukky.FileUpload? f = JsonConvert.DeserializeObject<Pamukky.FileUpload>(File.ReadAllText(file));
                        if (f != null)
                        {
                            string[] spl = f.contentType.Split("/");
                            if (spl.Length > 1)
                            {
                                var chatFile = new ChatFile() { url = fil, name = f.actualName, size = f.size };
                                string extension = spl[1].ToLower();
                                if (extension == "png" || extension == "jpg" || extension == "jpeg" || extension == "gif" || extension == "bmp")
                                {
                                    if (gImages == null) gImages = new();
                                    gImages.Add(chatFile);
                                }
                                else if (extension == "mp4")
                                {
                                    if (gVideos == null) gVideos = new();
                                    gVideos.Add(chatFile);
                                }
                                else if (extension == "mpeg" || extension == "m4a")
                                {
                                    if (gAudio == null) gAudio = new();
                                    gAudio.Add(chatFile);
                                }
                                else
                                {
                                    if (gFiles == null) gFiles = new();
                                    gFiles.Add(chatFile);
                                    //Console.WriteLine(extension);
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (gFiles == null) gFiles = new();
                    gFiles.Add(new ChatFile() { url = fi, name = "File", size = -1 });
                }
            }
        }
    }

    public Dictionary<string, object?> toDictionary()
    {
        Dictionary<string, object?> d = new();
        //d["senderuser"] = senderuser;
        d["replymsgcontent"] = replymsgcontent;
        d["replymsgsender"] = replymsgsender;
        d["replymsgid"] = replymsgid;
        d["gImages"] = gImages;
        d["gVideos"] = gVideos;
        d["gAudio"] = gAudio;
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

/// <summary>
/// Class that handles and stores a single chat. 
/// </summary>
class Chat : OrderedDictionary<string, ChatMessage>
{
    /// <summary>
    /// Dictionary to hold chats.
    /// </summary>
    public static ConcurrentDictionary<string, Chat> chatsCache = new();



    /// <summary>
    /// Chat ID of this chat.
    /// </summary>
    public string chatID = "";
    public Chat? mainchat = null;

    /// <summary>
    /// Boolean that indicates if this chat is a group chat.
    /// </summary>
    public bool isGroup = false;
    /// <summary>
    /// Group that indicates a real group for actual groups or fake group for DMs. Usually used for member permissions.
    /// </summary>
    public Group group = new();
    public ConcurrentDictionary<long, Dictionary<string, object?>> updates = new();

    /// <summary>
    /// List of updates to push to other connected federations.
    /// </summary>
    private List<Dictionary<string, object?>> updatesToPush = new();

    /// <summary>
    /// Boolean to show if a new push request should be done.
    /// </summary>
    bool doPushRequest = true;

    /// <summary>
    /// Dictionary to cache formatted messages. Shouldn't be directly used to get them.
    /// </summary>
    private Dictionary<string, ChatMessageFormatted> formatCache = new();

    /// <summary>
    /// List of currently typing users in this chat.
    /// </summary>
    public List<string> typingUsers = new();

    /// <summary>
    /// Number to store what new update/message ID will be.
    /// </summary>
    public long newID = 0;

    /// <summary>
    /// Boolean to indicate if chat got any updates. Currently used for saving.
    /// </summary>
    public bool wasUpdated = false;

    /// <summary>
    /// List to hold federations that are connected/joined to this chat.
    /// </summary>
    public List<Federation> connectedFederations = new();

    /// <summary>
    /// Chat class to cache pinned messages.
    /// </summary>
    public Chat? pinnedMessages;

    /// <summary>
    /// Update sender list for clients that use (User.cs)Updates.AddHook.
    /// </summary>
    public List<UpdateHook> updateHooks = new();

    int getIndexOfKeyInDictionary(string key)
    {
        for (int i = 0; i < Count; ++i)
        {
            if (Keys.ElementAt(i) == key) return i;
        }
        return -1;
    }

    #region Typing status
    public void setTyping(string uid)
    {
        if (!typingUsers.Contains(uid))
        {
            typingUsers.Add(uid);
            foreach (UpdateHook hook in updateHooks)
            {
                hook["TYPING"] = typingUsers;
            }
        }
    }

    public void remTyping(string uid)
    {
        if (typingUsers.Remove(uid))
        {
            foreach (UpdateHook hook in updateHooks)
            {
                hook["TYPING"] = typingUsers;
            }
        }
    }

    /// <summary>
    /// Waits for new typing updates to happen or timeout and returns them.
    /// </summary>
    /// <param name="maxWait">How long should it wait before giving up? each count adds 500ms more.</param>
    /// <returns>List of UIDs of typing users.</returns>
    public async Task<List<string>> waitForTypingUpdates(int maxWait = 40)
    {
        List<string> lastTyping = new(typingUsers);

        int wait = maxWait;

        while (lastTyping.SequenceEqual(typingUsers) && wait > 0)
        {
            await Task.Delay(500);
            --wait;
        }

        return typingUsers;
    }

    #endregion

    #region Chat updates
    /// <summary>
    /// Adds a update to chat updates history
    /// </summary>
    /// <param name="update">A dictionary that is a update</param>
    /// <param name="push">If true(default) updates will be pushed to federations</param>
    void addUpdate(Dictionary<string, object?> update, bool push = true)
    {
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
                    updates.Remove(oupdate.Key, out _);
                }
                else
                {
                    i += 1;
                }
            }
        }
        if ((update["event"] ?? "").ToString() == "REACT")
        {
            string msgid = (update["id"] ?? "").ToString() ?? "";
            int i = 0;
            while (i < updates.Count)
            {
                var oupdate = updates.ElementAt(i);
                if ((oupdate.Value["id"] ?? "").ToString() == msgid && (oupdate.Value["event"] ?? "").ToString() == "REACT")
                {
                    updates.Remove(oupdate.Key, out _);
                }
                else
                {
                    i += 1;
                }
            }
        }
        if ((update["event"] ?? "").ToString() == "UNPINNED")
        {
            string msgid = (update["id"] ?? "").ToString() ?? "";
            int i = 0;
            while (i < updates.Count)
            {
                var oupdate = updates.ElementAt(i);
                if ((oupdate.Value["id"] ?? "").ToString() == msgid && (oupdate.Value["event"] ?? "").ToString() == "PINNED")
                {
                    updates.Remove(oupdate.Key, out _);
                }
                else
                {
                    i += 1;
                }
            }
        }
        if ((update["event"] ?? "").ToString() == "PINNED")
        {
            string msgid = (update["id"] ?? "").ToString() ?? "";
            int i = 0;
            while (i < updates.Count)
            {
                var oupdate = updates.ElementAt(i);
                if ((oupdate.Value["id"] ?? "").ToString() == msgid && (oupdate.Value["event"] ?? "").ToString() == "UNPINNED")
                {
                    updates.Remove(oupdate.Key, out _);
                }
                else
                {
                    i += 1;
                }
            }
        }
        newID += 1;
        updates[newID] = update;

        var formattedUpdate = formatUpdate(update);
        foreach (UpdateHook hook in updateHooks)
        {
            hook[newID.ToString()] = formattedUpdate;
        }

        if (push) pushUpdate(update);
    }

    /// <summary>
    /// Pushes a update to remote federations
    /// </summary>
    /// <param name="update">A dictionary that is a update</param>
    void pushUpdate(Dictionary<string, object?> update)
    {
        if (connectedFederations.Count == 0) return;
        updatesToPush.Add(formatUpdate(update));
        if (doPushRequest)
        {
            Task.Delay(500).ContinueWith((task) =>
            { // Delay to send it as groupped
                foreach (Federation fed in connectedFederations)
                {
                    fed.pushChatUpdates(chatID, updatesToPush);
                }
                updatesToPush.Clear();
                doPushRequest = true;
            });
            doPushRequest = false;
        }
    }

    /// <summary>
    /// Gets chat update hisory since the provided number as ID
    /// </summary>
    /// <param name="since"></param>
    /// <returns>Dictionary that holds updates with their IDs</returns>

    public Dictionary<long, Dictionary<string, object?>> getUpdates(long since)
    {
        Dictionary<long, Dictionary<string, object?>> updatesSince = new();
        if (updates.Count == 0)
        {
            return updatesSince;
        }
        if (since > updates.Keys.Max())
        {
            return updatesSince;
        }
        else if (since == 0) // For getting since first one
        {
            since = updates.Keys.Min();
        }
        else if (since == -1) // For getting last one
        {
            since = updates.Keys.Max() - 1;
        }

        //var keysToRemove = new List<long>();
        for (int i = 0; i < updates.Count; ++i)
        {
            long id = updates.Keys.ElementAt(i);
            if (id > since)
            {
                updatesSince[id] = formatUpdate(updates[id]);
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
            }
            ;
        }
        /*foreach (long key in keysToRemove) {
            updates.Remove(key);
        }*/
        return updatesSince;
    }

    /// <summary>
    /// Waits for new updates to happen or timeout and returns them.
    /// </summary>
    /// <param name="maxWait">How long should it wait before giving up? each count adds 500ms more.</param>
    /// <returns>Dictionary that holds updates with their IDs</returns>
    public async Task<Dictionary<long, Dictionary<string, object?>>> waitForUpdates(int maxWait = 40)
    {
        long lastID = newID;

        int wait = maxWait;

        while (lastID == newID && wait > 0)
        {
            await Task.Delay(500);
            --wait;
        }

        return getUpdates(lastID);
    }

    /// <summary>
    /// Makes a update contain more info to send to clients.
    /// </summary>
    /// <param name="upd">Update to add info to</param>
    /// <returns>Update that contains more info depending on the event</returns>
    Dictionary<string, object?> formatUpdate(Dictionary<string, object?> upd)
    {
        Dictionary<string, object?> update;
        string eventtype = (upd["event"] ?? "").ToString() ?? "";
        string msgid = (upd["id"] ?? "").ToString() ?? "";
        if (eventtype == "NEWMESSAGE" || eventtype == "PINNED")
        {
            ChatMessageFormatted? f = formatMessage(msgid);
            if (f != null)
            {
                update = f.toDictionary();
            }
            else
            {
                return upd;
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
        return update;
    }
    #endregion

    #region Message read
    private ChatMessageFormatted? formatMessage(string key)
    {
        if (formatCache.ContainsKey(key)) return formatCache[key];
        if (ContainsKey(key))
        {
            ChatMessageFormatted formatted = new ChatMessageFormatted(this[key]);
            formatCache[key] = formatted;
            if (formatted.replymsgid != null)
            {
                //chatMessageFormatted? innerformatted = formatMessage(formatted.replymsgid);
                var chat = mainchat ?? this;// Get the message from the full chat, as page might not contain it. if chat is null, use this chat (could be a page only).
                //Console.WriteLine(mainchat == null ? "null!" : "exists");
                if (chat.ContainsKey(formatted.replymsgid))
                { // Check if message exists
                    var message = chat[formatted.replymsgid];
                    formatted.replymsgcontent = message.content;
                    formatted.replymsgsender = message.sender;
                }
            }
            return formatted;
        }
        return null;
    }

    /// <summary>
    /// Formats the entire chat
    /// </summary>
    /// <returns>The formatted chat</returns>
    public OrderedDictionary<string, ChatMessageFormatted> format()
    {
        OrderedDictionary<string, ChatMessageFormatted> fd = new();
        foreach (var kv in this)
        {
            ChatMessageFormatted? formattedMessage = formatMessage(kv.Key);
            if (formattedMessage == null) continue;
            fd[kv.Key] = formattedMessage;
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

    /// <summary>
    /// Returns messages depending on the prefix
    /// </summary>
    /// <param name="prefix">2 stuff between "-" which can be #index which would indicate a message from bottom, like 0 would be lastest; #^index which would indicate a message from top, like 0 would be first; a message ID that would indicate that message.</param>
    /// <returns></returns>
    public Chat getMessages(string prefix = "")
    {
        //Console.WriteLine(prefix);
        Chat chatPart = new() { chatID = chatID, mainchat = this };

        if (prefix.Contains("-"))
        {
            string[] split = prefix.Split("-");
            string? msgid1 = getMessageIDFromPrefix(split[0]);
            string? msgid2 = getMessageIDFromPrefix(split[1]);
            //Console.WriteLine("from: " + msgid1 + " To: " + msgid2);
            //Check if they exist
            if (msgid1 == null || msgid2 == null)
            {
                return chatPart; // message indexes are wrong.
            }

            int index1 = getIndexOfKeyInDictionary(msgid1);
            int index2 = getIndexOfKeyInDictionary(msgid2);
            int fromi;
            int toi;
            if (index1 > index2)
            {
                fromi = index2;
                toi = index1;
            }
            else
            {
                fromi = index1;
                toi = index2;
            }
            //Console.WriteLine("from: " + fromi + " To: " + toi);
            int index = fromi; //start from small one ...
            //Console.WriteLine(Count);
            while (index <= toi)
            { // ... and count to high one
                //Console.WriteLine(index);
                chatPart.Add(Keys.ElementAt(index), Values.ElementAt(index) ?? new ChatMessage() {content = "Sorry, this message looks like it's corrupt.", sender = "0"});
                ++index;
            }

        }
        else
        {
            string? id = getMessageIDFromPrefix(prefix);
            if (id != null) chatPart.Add(id, this[id]);
        }
        return chatPart;
    }


    /// <summary>
    /// Gets message ID from single prefix
    /// </summary>
    /// <param name="prefix">This could be #index which would indicate a message from bottom, like 0 would be lastest; #^index which would indicate a message from top, like 0 would be first; a message ID that would indicate that message.</param>
    /// <returns>Message ID or null if failled</returns>
    public string? getMessageIDFromPrefix(string prefix)
    {
        if (Count == 0)
        {
            return null;
        }
        if (prefix.StartsWith("#^"))
        { //Don't catch errors, entire thing should fail already.
            int id = int.Parse(prefix.Replace("#^", ""));
            if (id >= Count)
            {
                id = Count - 1;
            }
            if (id > -1) return Keys.ElementAt(id);
        }
        else if (prefix.StartsWith("#"))
        {
            int idx = int.Parse(prefix.Replace("#", ""));
            if (idx >= Count)
            {
                idx = Count - 1;
            }
            int id = (Count - 1) - idx;
            if (id < Count && id > -1) return Keys.ElementAt(id);
        }
        else
        {
            if (ContainsKey(prefix))
            {
                return prefix;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the last message.
    /// </summary>
    /// <param name="previewMode">If true, the content will be cropped.</param>
    /// <returns>ChatMessage that is the last message.</returns>
    public ChatMessage? getLastMessage(bool previewMode = false)
    {
        if (Count > 0)
        {
            var msg = Values.ElementAt(Count - 1);
            var ret = new ChatMessage()
            {
                sender = msg.sender,
                content = msg.content,
                time = msg.time
            };
            
            if (previewMode)
            {
                ret.content = ret.content.Split("\n")[0];
                const int cropsize = 50;
                if (ret.content.Length > cropsize && ret.sender != "0")
                {
                    ret.content = ret.content.Substring(0, cropsize);
                }
            }
            return ret;
        }
        return null;
    }

    /// <summary>
    /// Gets all pinned messages
    /// </summary>
    /// <returns></returns>
    public Chat getPinned()
    {
        return pinnedMessages ?? new() { chatID = chatID, mainchat = this };
    }
    #endregion
    
    #region Message actions
    /// <summary>
    /// Sends a message to the chat
    /// </summary>
    /// <param name="message">ChatMessage to send</param>
    /// <param name="notify">If true(default), notify all the members</param>
    /// <param name="remoteMessageID">Message ID that recieved from remote federation, should be null(default) if not recieved.</param>
    public async void sendMessage(ChatMessage message, bool notify = true, string? remoteMessageID = null)
    {
        newID++;
        string id = newID.ToString();
        if (remoteMessageID != null)
        {
            id = remoteMessageID;
            if (ContainsKey(id)) return;
        }
        Add(id, message);
        Dictionary<string, object?> update = new();
        update["event"] = "NEWMESSAGE";
        update["id"] = id;
        addUpdate(update);
        if (notify)
        {
            var notification = new messageNotification()
            {
                user = ShortProfile.fromProfile(await UserProfile.Get(message.sender)), //Probably would stay like this
                userid = message.sender,
                content = message.content,
                chatid = chatID
            };
            foreach (string member in group.members.Keys)
            {
                if (message.sender != member)
                {
                    userConfig? uc = await userConfig.Get(member);
                    if (uc != null && !uc.mutedChats.Contains(chatID))
                    {
                        Notifications.Get(member)[id] = notification;
                    }
                }
            }
        }

    }

    /// <summary>
    /// Deletes a message from the chat
    /// </summary>
    /// <param name="msgID">ID of the message to delete.</param>
    public void deleteMessage(string msgID)
    {
        if (Remove(msgID))
        {
            Dictionary<string, object?> update = new();
            update["event"] = "DELETED";
            update["id"] = msgID;
            addUpdate(update);
        }
    }

    /// <summary>
    /// Makes user react to a message; if called "twice", it will remove the reaction.
    /// </summary>
    /// <param name="msgID">ID of the message to react to.</param>
    /// <param name="userID">ID of the user that will react.</param>
    /// <param name="reaction">Reaction emoji to send.</param>
    /// <returns>All reactions that are in the message.</returns>
    public MessageReactions reactMessage(string msgID, string userID, string reaction)
    {
        if (ContainsKey(msgID))
        {
            ChatMessage msg = this[msgID];
            MessageReactions rect = msg.reactions;
            MessageEmojiReactions r = rect.Get(reaction, true);
            if (r.ContainsKey(userID))
            {
                r.Remove(userID, out _);
            }
            else
            {
                MessageReaction react = new() { sender = userID, reaction = reaction, time = Pamukky.dateToString(DateTime.Now) };
                r[userID] = react;
            }
            rect.Update();
            Dictionary<string, object?> update = new();
            update["event"] = "REACTIONS";
            update["id"] = msgID;
            addUpdate(update);
            return rect;
        }
        return new();
    }

    /// <summary>
    /// Replaces all of the reactions in the message
    /// </summary>
    /// <param name="msgID">ID of the message</param>
    /// <param name="reactions">MessageReactions to set the reactions of this message to.</param>
    public void putReactions(string msgID, MessageReactions reactions)
    {
        if (ContainsKey(msgID))
        {
            reactions.Update(); // Update the given MessageReactions so it doesn't have stuff like 0 emoji reactions
            this[msgID].reactions = reactions;
            Dictionary<string,object?> update = new(); //FIXME: Echoes in federations where this function is used in...
            update["event"] = "REACTIONS";
            update["id"] = msgID;
            addUpdate(update, false);
        }
    }

    /// <summary>
    /// Pins the message.
    /// </summary>
    /// <param name="msgID">ID of the message to pin/unpin.</param>
    /// <param name="val">Null to toggle, false to unpin, true to pin.</param>
    /// <returns>Pinned status of the message.</returns>
    public bool pinMessage(string msgID, bool? val = null)
    {
        if (ContainsKey(msgID))
        {
            var message = this[msgID];
            if (val == null)
            {
                val = !message.pinned;
            }
            if (message.pinned == val)
            {
                return val.Value;
            }
            message.pinned = val.Value;
            if (message.pinned == true)
            {
                getPinned()[msgID] = message;
            }
            else
            {
                getPinned().Remove(msgID);
            }
            ChatMessageFormatted? f = formatMessage(msgID);
            if (f != null)
            {
                Dictionary<string, object?> update = new();
                update["event"] = message.pinned ? "PINNED" : "UNPINNED";
                update["id"] = msgID;
                addUpdate(update);
            }
            if (formatCache.ContainsKey(msgID))
            {
                formatCache[msgID].pinned = message.pinned;
            }
            return message.pinned;
        }
        return false;
    }

    #endregion

    #region Permissions
    public enum chatAction
    {
        Read,
        Send,
        React,
        Delete,
        Pin
    }

    public bool canDo(string user, chatAction action, string msgid = "")
    {
        if (action == chatAction.Read && group.publicgroup) return true;

        bool contains = false;
        GroupMember? u = null;
        foreach (var member in group.members)
        { //find the user
            if (member.Value.user == user)
            {
                contains = true;
                u = member.Value;
            }
        }
        if (!contains || u == null)
        { // Doesn't exist? block
            return false;
        }
        if (action != chatAction.Send && action != chatAction.Read)
        { // Any actions except read and send will require a existent message
            if (!ContainsKey(msgid))
            {
                return false;
            }
        }
        if (action == chatAction.Delete)
        {
            if (this[msgid].sender == user)
            { // User can delete their own messages.
                return true;
            }
        }
        if (u.role != "" && group.roles.ContainsKey(u.role))
        { //is this a real group user?
            //Then it's a real group
            GroupRole role = group.roles[u.role];
            if (action == chatAction.React) return role.AllowSendingReactions;
            if (action == chatAction.Send) return role.AllowSending;
            if (action == chatAction.Delete) return role.AllowMessageDeleting;
            if (action == chatAction.Pin) return role.AllowPinningMessages;
        }
        return true;
    }
    #endregion


    /// <summary>
    /// Gets/Loads the chat.
    /// </summary>
    /// <param name="chat">ID of the chat.</param>
    /// <returns>Chat if succeeded, null if invalid/failled.</returns>
    public static async Task<Chat?> getChat(string chat)
    {
        if (chatsCache.ContainsKey(chat))
        {
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
        Chat? loadedChat = null;
        if (File.Exists("data/chat/" + chat + "/chat"))
        {
            loadedChat = JsonConvert.DeserializeObject<Chat>(await File.ReadAllTextAsync("data/chat/" + chat + "/chat"));
            // If that is null, we should NOT load the chat at all
        }
        else
        {
            if (!chat.Contains("@")) loadedChat = new Chat();
        }

        if (chat.Contains("@"))
        {
            string[] split = chat.Split("@");
            string id = split[0];
            string server = split[1];
            var connection = await Federation.connect(server);
            if (connection != null)
            {
                loadedChat = await connection.getChat(id);
            }
        }

        if (loadedChat != null)
        {
            if (File.Exists("data/chat/" + chat + "/updates"))
            {
                try
                {
                    var u = JsonConvert.DeserializeObject<ConcurrentDictionary<long, Dictionary<string, object?>>>(await File.ReadAllTextAsync("data/chat/" + chat + "/updates"));
                    if (u != null) loadedChat.updates = u;
                }
                catch { } //Ignore...
            }
            loadedChat.chatID = chat;
            loadedChat.isGroup = !chat.Contains("-");
            loadedChat.newID = DateTime.Now.Ticks;
            if (loadedChat.isGroup)
            {
                // Load the real group
                Group? group = await Group.Get(chat);
                if (group == null)
                {
                    return null;
                }
                loadedChat.group = group;
            }
            else
            {
                // Make a fake group
                string[] users = chat.Split("-");
                foreach (string user in users)
                {
                    loadedChat.group.members[user] = new GroupMember() { user = user };
                }
            }

            loadedChat.pinnedMessages = new() { chatID = chat, mainchat = loadedChat };
            foreach (var kv in loadedChat)
            {
                if (kv.Value.pinned)
                {
                    loadedChat.pinnedMessages[kv.Key] = kv.Value;
                }
            }
            chatsCache[chat] = loadedChat;
        }
        return loadedChat;
    }


    /// <summary>
    /// Saves the chat into the disk if wasUpdated is true.
    /// </summary>
    public void saveChat()
    {
        if (wasUpdated)
        {
            Directory.CreateDirectory("data/chat/" + chatID);
            string c = JsonConvert.SerializeObject(this);
            File.WriteAllTextAsync("data/chat/" + chatID + "/chat", c);
            string u = JsonConvert.SerializeObject(updates);
            File.WriteAllTextAsync("data/chat/" + chatID + "/updates", u);
            wasUpdated = false;
        }
    }
}