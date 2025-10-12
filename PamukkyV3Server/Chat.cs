using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Class for message structure
/// </summary>
class ChatMessage
{ // Chat message.
    public string senderUID = "";
    public string content = "";
    public DateTime sendTime = DateTime.Now;
    public string? replyMessageID;
    public List<string>? files;
    public string? forwardedFromUID;
    public MessageReactions reactions = new();
    public bool isPinned = false;
    public List<string> mentionUIDs = new();
    public List<UserMessageRead> readByUIDs = new();

    #region Backwards compatibility
    public string sender
    {
        set { senderUID = value; }
    }

    public string? replymsgid
    {
        set { replyMessageID = value; }
    }

    public string? forwardedfrom
    {
        set { forwardedFromUID = value; }
    }

    public bool pinned
    {
        set { isPinned = value; }
    }

    public string time
    {
        set { sendTime = Helpers.StringToDate(value); }
    }
    #endregion
}

class UserMessageRead
{
    public string userID = "";
    public DateTime readTime = DateTime.Now;
}

/// <summary>
/// Class for a single reaction structure,
/// MessageReactions[] > MessageEmojiReactions[] > MessageReaction
/// </summary>
class MessageReaction
{
    /// <summary>
    /// Emoji of the reaction.
    /// </summary>
    public string reaction = "";
    /// <summary>
    /// ID of the user that sent it
    /// </summary>
    public string senderUID = "";
    /// <summary>
    /// The date of when reaction was sent.
    /// </summary>
    public DateTime sendTime;

    #region Backwards compatibility
    public string sender
    {
        set { senderUID = value; }
    }

    public string time
    {
        set { sendTime = Helpers.StringToDate(value); }
    }
    #endregion
}

/// <summary>
/// Class for reactions for a emoji,
/// MessageReactions[] > MessageEmojiReactions[] > MessageReaction
/// </summary>
class MessageEmojiReactions : ConcurrentDictionary<string, MessageReaction> { }

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
    /// Gets MessageEmojiReactions and creates new one if doesn't exist.
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
/// Chat file/attachment structure that is outputted from message formatter.
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
    public string? replyMessageContent;
    public string? replyMessageSenderUID;

    public List<ChatFile>? gImages;
    public List<ChatFile>? gVideos;
    public List<ChatFile>? gAudio;
    public List<ChatFile>? gFiles;

    public object readBy = 0;
    public new List<UserMessageRead> readByUIDs
    {
        set
        {
            readBy = value;
        }
    }

    public ChatMessageFormatted(ChatMessage msg)
    {
        senderUID = msg.senderUID;
        content = msg.content;
        sendTime = msg.sendTime;
        replyMessageID = msg.replyMessageID;
        files = msg.files;
        reactions = msg.reactions;
        forwardedFromUID = msg.forwardedFromUID;
        isPinned = msg.isPinned;
        readByUIDs = msg.readByUIDs;
        //senderuser = profileShort.fromProfile(userProfile.Get(sender));
        //if (forwardedfrom != null) {
        //    forwardedname = profileShort.fromProfile(userProfile.Get(forwardedfrom)).name;
        //}

        if (files != null)
        { //Group files in the message to types.
            foreach (string fi in files)
            {
                string fil = fi.Replace(Federation.thisServerURL, "%SERVER%");
                if (fil.StartsWith("%SERVER%"))
                {
                    string id = fil.Replace("%SERVER%getmedia?file=", ""); //Get file ID
                    var upload = FileUpload.Get(id);
                    if (upload != null)
                    {
                        var chatFile = new ChatFile() { url = fil, name = upload.actualName, size = upload.size };
                        string type = upload.contentType;
                        if (type == "image/png" || type == "image/jpeg" || type == "image/gif" || type == "image/bmp")
                        {
                            if (gImages == null) gImages = new();
                            gImages.Add(chatFile);
                        }
                        else if (type == "video/mpeg" || type == "video/mp4" || type == "video/ogv")
                        {
                            if (gVideos == null) gVideos = new();
                            gVideos.Add(chatFile);
                        }
                        else if (type == "audio/mpeg" || type == "audio/mp4" || type == "audio/aac" || type == "audio/oga")
                        {
                            if (gAudio == null) gAudio = new();
                            gAudio.Add(chatFile);
                        }
                        else
                        {
                            if (gFiles == null) gFiles = new();
                            gFiles.Add(chatFile);
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

    /// <summary>
    /// Convert to dictionary to use it as dictionary, like for updaters.
    /// </summary>
    /// <returns>A string->object? dictionary of the message.</returns>
    public Dictionary<string, object?> ToDictionary()
    {
        Dictionary<string, object?> d = new();
        d["replyMessageContent"] = replyMessageContent;
        d["replyMessageSenderUID"] = replyMessageSenderUID;
        d["replyMessageID"] = replyMessageID;
        d["gImages"] = gImages;
        d["gVideos"] = gVideos;
        d["gAudio"] = gAudio;
        d["gFiles"] = gFiles;
        d["senderUID"] = senderUID;
        d["content"] = content;
        d["sendTime"] = sendTime;
        d["files"] = files;
        d["reactions"] = reactions;
        d["forwardedFromUID"] = forwardedFromUID;
        d["isPinned"] = isPinned;
        d["readBy"] = readBy;
        return d;
    }
}

/// <summary>
/// Class that handles and stores a single chat. 
/// </summary>
class Chat : OrderedDictionary<string, ChatMessage>
{
    public static string[] mentionStrings = {"room", "chat", "everyone", "all"};

    /// <summary>
    /// Dictionary to hold chats.
    /// </summary>
    public static ConcurrentDictionary<string, Chat> chatsCache = new();
    /// <summary>
    /// List to hold IDs of loading chats so it waits for them.
    /// </summary>
    public static List<string> loadingChats = new();


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
    /// Dictionary to cache formatted messages. Shouldn't be directly used to get them.
    /// </summary>
    private Dictionary<string, ChatMessageFormatted> formatCache = new();

    /// <summary>
    /// List of currently typing users in this chat.
    /// </summary>
    public List<string> typingUsers = new();

    /// <summary>
    /// Sets if last id is "used" and new one is needed
    /// </summary>
    public bool lastIDUsed = true;

    /// <summary>
    /// Last ID that was used
    /// </summary>
    public long lastID = 0;

    /// <summary>
    /// Boolean to indicate if chat got any updates. Currently used for saving.
    /// </summary>
    public bool wasUpdated = false;

    /// <summary>
    /// Chat class to cache pinned messages.
    /// </summary>
    public Chat? pinnedMessages;

    /// <summary>
    /// Update sender list for clients that use (User.cs)Updates.AddHook.
    /// </summary>
    public List<UpdateHook> updateHooks = new();

    int GetIndexOfKeyInDictionary(string key)
    {
        for (int i = 0; i < Count; ++i)
        {
            if (Keys.ElementAt(i) == key) return i;
        }
        return -1;
    }

    #region Typing status

    /// <summary>
    /// Sets user as typing
    /// </summary>
    /// <param name="uid">ID of the user.</param>
    public void SetTyping(string uid)
    {
        if (!typingUsers.Contains(uid)) // Don't do duplicates
        {
            typingUsers.Add(uid);
            foreach (UpdateHook hook in updateHooks) // Send the event.
            {
                if (CanDo(hook.target, ChatAction.Read))
                {
                    hook["TYPING|" + uid] = true;
                }
            }
        }
    }

    /// <summary>
    /// Sets user as not typing.
    /// </summary>
    /// <param name="uid">ID of the user.</param>
    public void RemoveTyping(string uid)
    {
        if (typingUsers.Remove(uid)) // Remove and if successful. So this doesn't happen multiple times.
        {
            foreach (UpdateHook hook in updateHooks)
            {
                if (CanDo(hook.target, ChatAction.Read))
                {
                    hook["TYPING|" + uid] = false;
                }
            }
        }
    }

    /// <summary>
    /// Waits for new typing updates to happen or timeout and returns them.
    /// </summary>
    /// <param name="maxWait">How long should it wait before giving up? each count adds 500ms more.</param>
    /// <returns>List of UIDs of typing users.</returns>
    public async Task<List<string>> WaitForTypingUpdates(int maxWait = 40)
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
    /// Gets a new(or previous if lastIDUsed is true) ID. 
    /// </summary>
    /// <returns>A update id</returns>
    long RequestNewID()
    {
        if (lastIDUsed)
        {
            lastID = DateTime.Now.Ticks;
            lastIDUsed = false;
        }

        return lastID;
    }
    /// <summary>
    /// Adds a update to chat updates history
    /// </summary>
    /// <param name="update">A dictionary that is a update</param>
    /// <param name="push">If true(default) updates will be pushed to federations</param>
    void AddUpdate(Dictionary<string, object?> update)
    {
        wasUpdated = true;
        
        string eventName = (update["event"] ?? "").ToString() ?? "";

        if (eventName == "DELETED")
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
                    ++i;
                }
            }
        }
        else if (eventName == "REACTED")
        {
            string msgid = (update["id"] ?? "").ToString() ?? "";
            string userid = (update["senderUID"] ?? "").ToString() ?? "";
            string reaction = (update["reaction"] ?? "").ToString() ?? "";
            int i = 0;
            while (i < updates.Count)
            {
                var oupdate = updates.ElementAt(i);
                if ((oupdate.Value["id"] ?? "").ToString() == msgid && (oupdate.Value["event"] ?? "").ToString() == "UNREACTED" && (oupdate.Value["senderUID"] ?? "").ToString() == userid && (oupdate.Value["reaction"] ?? "").ToString() == reaction)
                {
                    updates.Remove(oupdate.Key, out _);
                }
                else
                {
                    ++i;
                }
            }
        }
        else if (eventName == "UNREACTED")
        {
            string msgid = (update["id"] ?? "").ToString() ?? "";
            string userid = (update["senderUID"] ?? "").ToString() ?? "";
            string reaction = (update["reaction"] ?? "").ToString() ?? "";
            int i = 0;
            while (i < updates.Count)
            {
                var oupdate = updates.ElementAt(i);
                if ((oupdate.Value["id"] ?? "").ToString() == msgid && (oupdate.Value["event"] ?? "").ToString() == "REACTED" && (oupdate.Value["senderUID"] ?? "").ToString() == userid && (oupdate.Value["reaction"] ?? "").ToString() == reaction)
                {
                    updates.Remove(oupdate.Key, out _);
                }
                else
                {
                    ++i;
                }
            }
        }
        else if (eventName == "UNPINNED")
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
                    ++i;
                }
            }
        }
        else if (eventName == "PINNED")
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
                    ++i;
                }
            }
        }

        long id = RequestNewID();
        lastIDUsed = true;

        updates[id] = update;

        var formattedUpdate = FormatUpdate(update);
        foreach (UpdateHook hook in updateHooks)
        {
            if (CanDo(hook.target, ChatAction.Read))
            {
                hook[id.ToString()] = formattedUpdate;
            }
        }
    }

    /// <summary>
    /// Gets chat update hisory since the provided number as ID
    /// </summary>
    /// <param name="since"></param>
    /// <returns>Dictionary that holds updates with their IDs</returns>

    public Dictionary<long, Dictionary<string, object?>> GetUpdates(long since)
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
        else if (since == -1) // For getting last one
        {
            since = updates.Keys.Max() - 1;
        }
        else if (since < updates.Keys.Min()) // For getting since first one
        {
            since = updates.Keys.Min() - 1;
        }

        //var keysToRemove = new List<long>();
        for (int i = 0; i < updates.Count; ++i)
        {
            long id = updates.Keys.ElementAt(i);
            if (id > since)
            {
                updatesSince[id] = FormatUpdate(updates[id]);
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
    public async Task<Dictionary<long, Dictionary<string, object?>>> WaitForUpdates(int maxWait = 40, long? since = 0)
    {
        long lastID = since ?? RequestNewID();

        int wait = maxWait;

        while (lastID == RequestNewID() && wait > 0)
        {
            await Task.Delay(250);
            --wait;
        }

        return GetUpdates(lastID);
    }

    /// <summary>
    /// Makes a update contain more info to send to clients.
    /// </summary>
    /// <param name="upd">Update to add info to</param>
    /// <returns>Update that contains more info depending on the event</returns>
    Dictionary<string, object?> FormatUpdate(Dictionary<string, object?> upd)
    {
        Dictionary<string, object?> update;
        string eventtype = (upd["event"] ?? "").ToString() ?? "";
        string msgid = (upd["id"] ?? "").ToString() ?? "";
        if (eventtype == "NEWMESSAGE" || eventtype == "PINNED")
        {
            ChatMessageFormatted? f = FormatMessage(msgid);
            if (f != null)
            {
                update = f.ToDictionary();
            }
            else
            {
                return upd;
            }
        }
        else
        {
            update = upd;
        }
        update["event"] = eventtype;
        update["id"] = msgid;
        return update;
    }
    #endregion

    #region Message read
    private ChatMessageFormatted? FormatMessage(string key)
    {
        if (formatCache.ContainsKey(key)) return formatCache[key];
        if (ContainsKey(key))
        {
            ChatMessageFormatted formatted = new ChatMessageFormatted(this[key]);
            formatCache[key] = formatted;
            if (formatted.replyMessageID != null)
            {
                //chatMessageFormatted? innerformatted = formatMessage(formatted.replymsgid);
                var chat = mainchat ?? this;// Get the message from the full chat, as page might not contain it. if chat is null, use this chat (could be a page only).
                //Console.WriteLine(mainchat == null ? "null!" : "exists");
                if (chat.ContainsKey(formatted.replyMessageID))
                { // Check if message exists
                    var message = chat[formatted.replyMessageID];
                    formatted.replyMessageContent = message.content;
                    formatted.replyMessageSenderUID = message.senderUID;
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
    public OrderedDictionary<string, ChatMessageFormatted> FormatAll()
    {
        OrderedDictionary<string, ChatMessageFormatted> fd = new();
        foreach (var kv in this)
        {
            ChatMessageFormatted? formattedMessage = FormatMessage(kv.Key);
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
    public Chat GetMessages(string prefix = "")
    {
        //Console.WriteLine(prefix);
        Chat chatPart = new() { chatID = chatID, mainchat = this };

        if (prefix.Contains("-"))
        {
            string[] split = prefix.Split("-");
            string? msgid1 = GetMessageIDFromPrefix(split[0]);
            string? msgid2 = GetMessageIDFromPrefix(split[1]);
            //Console.WriteLine("from: " + msgid1 + " To: " + msgid2);
            //Check if they exist
            if (msgid1 == null || msgid2 == null)
            {
                return chatPart; // message indexes are wrong.
            }

            int index1 = GetIndexOfKeyInDictionary(msgid1);
            int index2 = GetIndexOfKeyInDictionary(msgid2);
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
                chatPart.Add(Keys.ElementAt(index), Values.ElementAt(index) ?? new ChatMessage() {content = "Sorry, this message looks like it's corrupt.", senderUID = "0"});
                ++index;
            }

        }
        else
        {
            string? id = GetMessageIDFromPrefix(prefix);
            if (id != null) chatPart.Add(id, this[id]);
        }
        return chatPart;
    }


    /// <summary>
    /// Gets message ID from single prefix
    /// </summary>
    /// <param name="prefix">This could be #index which would indicate a message from bottom, like 0 would be lastest; #^index which would indicate a message from top, like 0 would be first; a message ID that would indicate that message.</param>
    /// <returns>Message ID or null if failled</returns>
    public string? GetMessageIDFromPrefix(string prefix)
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
    public ChatMessage? GetLastMessage(bool previewMode = false)
    {
        if (Count > 0)
        {
            var msg = Values.ElementAt(Count - 1);
            var ret = new ChatMessage()
            {
                senderUID = msg.senderUID,
                content = msg.content,
                sendTime = msg.sendTime
            };
            
            if (previewMode)
            {
                ret.content = ret.content.Split("\n")[0];
                const int cropsize = 50;
                if (ret.content.Length > cropsize && ret.senderUID != "0")
                {
                    ret.content = ret.content.Substring(0, cropsize);
                }
            }
            return ret;
        }
        return null;
    }

    /// <summary>
    /// Gets all pinned messages.
    /// </summary>
    /// <returns>Pinned messages list.</returns>
    public Chat GetPinnedMessages()
    {
        if (pinnedMessages == null)
        {
            pinnedMessages = new() { chatID = chatID, mainchat = this };
            foreach (var kv in this)
            {
                if (kv.Value.isPinned)
                {
                    pinnedMessages[kv.Key] = kv.Value;
                }
            }
        }
        return pinnedMessages;
    }
    #endregion
    
    #region Message actions
    /// <summary>
    /// Sends a message to the chat
    /// </summary>
    /// <param name="message">ChatMessage to send</param>
    /// <param name="notify">If true(default), notify all the members</param>
    /// <param name="remoteMessageID">Message ID that recieved from remote federation, should be null(default) if not recieved.</param>
    public async void SendMessage(ChatMessage message, bool notify = true, string? remoteMessageID = null)
    {
        string id = RequestNewID().ToString();
        if (remoteMessageID != null)
        {
            id = remoteMessageID;
            if (ContainsKey(id)) return;
        }
        Add(id, message);

        Dictionary<string, object?> update = new();
        update["event"] = "NEWMESSAGE";
        update["id"] = id;
        AddUpdate(update);
        if (notify)
        {
            var notification = new MessageNotification()
            {
                user = ShortProfile.FromProfile(await UserProfile.Get(message.senderUID)), //Probably would stay like this
                userid = message.senderUID,
                content = message.content,
                chatid = chatID
            };
            foreach (string member in group.members.Keys)
            {
                if (message.senderUID != member)
                {
                    UserConfig? uc = await UserConfig.Get(member);
                    if (uc != null && uc.CanSendNotification(chatID, message.mentionUIDs.Contains(member) || message.mentionUIDs.Contains("[CHAT]")))
                    {
                        Notifications.Get(member).AddNotification(notification);
                    }
                }
            }
        }

    }

    /// <summary>
    /// Deletes a message from the chat
    /// </summary>
    /// <param name="msgID">ID of the message to delete.</param>
    public void DeleteMessage(string msgID)
    {
        if (Remove(msgID))
        {
            Dictionary<string, object?> update = new();
            update["event"] = "DELETED";
            update["id"] = msgID;
            AddUpdate(update);
        }
    }

    /// <summary>
    /// Makes user react to a message; if called "twice", it will remove the reaction if toggle=null.
    /// </summary>
    /// <param name="msgID">ID of the message to react to.</param>
    /// <param name="userID">ID of the user that will react.</param>
    /// <param name="reaction">Reaction emoji to send.</param>
    /// <param name="toggle">Null to toggle(default), true to add, false to remove.</param>
    /// <param name="sendTime">Sets sent date of reaction. Null (default) for auto.</param>
    /// <returns>All reactions that are in the message.</returns>
    public MessageReactions ReactMessage(string msgID, string userID, string reaction, bool? toggle = null, DateTime? sendTime = null)
    {
        if (ContainsKey(msgID))
        {
            ChatMessage msg = this[msgID];
            MessageReactions rect = msg.reactions;
            MessageEmojiReactions r = rect.Get(reaction, true);
            
            // set sendtime if null (which is normally)
            if (sendTime == null) sendTime = DateTime.Now;

            // set toggle or ignore action if it was done already.
            if (toggle == null) toggle = !r.ContainsKey(userID);
            else if (toggle == true && r.ContainsKey(userID)) return rect;
            else if (toggle == false && !r.ContainsKey(userID)) return rect;


            //Create event dictionary to send.
            Dictionary<string, object?> update = new();
            update["id"] = msgID;
            update["senderUID"] = userID;
            update["reaction"] = reaction;
            if (toggle == true)
            {
                MessageReaction react = new() { senderUID = userID, reaction = reaction, sendTime = (DateTime)sendTime };
                r[userID] = react;
                update["event"] = "REACTED";
                update["sendTime"] = sendTime;
            }
            else
            {
                r.Remove(userID, out _);
                update["event"] = "UNREACTED";
            }
            rect.Update();
            AddUpdate(update);
            return rect;
        }
        return new();
    }

    /// <summary>
    /// Sets a message as "read" by the user
    /// </summary>
    /// <param name="msgID">ID of the message</param>
    /// <param name="userID">ID of the user who will "read" the message</param>
    /// <param name="readTime">Sets sent date of read. Null (default) for auto.</param>
    public void ReadMessage(string msgID, string userID, DateTime? readTime = null)
    {
        if (ContainsKey(msgID))
        {
            ChatMessage msg = this[msgID];

            if (msg.readByUIDs.FirstOrDefault(u => u.userID == userID) != null) return;

            // set readtime if null (which is normally)
            if (readTime == null) readTime = DateTime.Now;

            msg.readByUIDs.Add(new()
            {
                userID = userID,
                readTime = (DateTime)readTime
            });

            //Create event dictionary to send.
            Dictionary<string, object?> update = new();
            update["id"] = msgID;
            update["userID"] = userID;
            update["event"] = "READ";
            update["readTime"] = readTime;
            AddUpdate(update);
        }
    }

    /// <summary>
    /// Pins the message.
    /// </summary>
    /// <param name="msgID">ID of the message to pin/unpin.</param>
    /// <param name="val">Null to toggle, false to unpin, true to pin.</param>
    /// <returns>Pinned status of the message.</returns>
    public bool PinMessage(string msgID, bool? val = null)
    {
        if (ContainsKey(msgID))
        {
            var message = this[msgID];
            if (val == null)
            {
                val = !message.isPinned;
            }
            if (message.isPinned == val)
            {
                return val.Value;
            }
            message.isPinned = val.Value;
            if (message.isPinned == true)
            {
                GetPinnedMessages()[msgID] = message;
            }
            else
            {
                GetPinnedMessages().Remove(msgID);
            }
            ChatMessageFormatted? f = FormatMessage(msgID);
            if (f != null)
            {
                Dictionary<string, object?> update = new();
                update["event"] = message.isPinned ? "PINNED" : "UNPINNED";
                update["id"] = msgID;
                AddUpdate(update);
            }
            if (formatCache.ContainsKey(msgID))
            {
                formatCache[msgID].isPinned = message.isPinned;
            }
            return message.isPinned;
        }
        return false;
    }

    #endregion

    #region Helpers
    /// <summary>
    /// Helper function to dedect if string has a mention tag to mention everyone.
    /// </summary>
    /// <param name="str">Message string to detect</param>
    /// <returns>List of user IDs or </returns>
    public bool DoesContainAllMention(string str)
    {
        // @chat, @everyone and @room like stuff that pings everyone.
        foreach (string mention in mentionStrings)
        {
            if (str.Contains("@" + mention))
            {
                return true;
            }
        }

        return false;
    } 
    /// <summary>
    /// Helper function to detect if message has a mention to any user in the chat.
    /// </summary>
    /// <param name="msg">ChatMessage to dedect.</param>
    /// <returns>List of user IDs or </returns>
    public List<string> GetMessageMentions(ChatMessage msg)
    {
        // @chat, @everyone and @room like stuff that pings everyone.
        if (DoesContainAllMention(msg.content)) return new() { "[CHAT]" };

        List<string> mentions = new();

        foreach (var member in group.members)
        {
            if (DoesMessageContainUserMention(msg, member.Key))
            {
                mentions.Add(member.Key);
            }
        }

        return mentions;
    } 
    /// <summary>
    /// Helper function to detect if message has a mention to the target user.
    /// </summary>
    /// <param name="msg">ChatMessage to dedect.</param>
    /// <param name="targetUserID">ID of the target user to search for mentions(@) in the message.</param>
    /// <returns></returns>
    public bool DoesMessageContainUserMention(ChatMessage msg, string targetUserID)
    {
        // @chat, @everyone and @room like stuff that pings everyone.
        if (DoesContainAllMention(msg.content)) return true;

        // A actual mention of user
        if (msg.content.Contains("@" + targetUserID)) return true;
        // Reply to user's message. if replied.
        if (msg.replyMessageID != null)
        {
            if (ContainsKey(msg.replyMessageID))
            {
                ChatMessage targetMessage = this[msg.replyMessageID];
                if (targetMessage.senderUID == targetUserID)
                {
                    return true;
                }
            }
        }
        return false;
    } 
    #endregion

    #region Permissions
    public enum ChatAction
    {
        Read,
        Send,
        React,
        Delete,
        Pin
    }

    public bool CanDo(string target, ChatAction action, string msgid = "")
    {
        if (action == ChatAction.Read && group.isPublic) return true;

        if ((target.Contains(":") || target.Contains(".")) && !target.Contains("@"))
        {
            // If federations weren't still allowed no matter what, that would result in sync issues.
            if (action == ChatAction.Read) return true;

            foreach (var member in group.members.Keys)
            {
                if (member.Contains("@"))
                {
                    string[] split = member.Split("@");
                    string server = split[1];
                    if (server == target)
                    {
                        if (CanDo(member, action, msgid)) return true;
                    }
                }
            }
        }

        GroupMember? u = null;
        foreach (var member in group.members)
        { //find the user
            if (member.Value.userID == target)
            {
                u = member.Value;
            }
        }
        if (u == null)
        { // Doesn't exist? block
            return false;
        }
        if (action != ChatAction.Send && action != ChatAction.Read)
        { // Any actions except read and send will require a existent message
            if (!ContainsKey(msgid))
            {
                return false;
            }
        }
        if (action == ChatAction.Delete)
        {
            if (this[msgid].senderUID == target)
            { // User can delete their own messages.
                return true;
            }
        }
        if (u.role != "" && group.roles.ContainsKey(u.role))
        { //is this a real group user?
            //Then it's a real group
            GroupRole role = group.roles[u.role];
            if (action == ChatAction.React) return role.AllowSendingReactions;
            if (action == ChatAction.Send) return role.AllowSending;
            if (action == ChatAction.Delete) return role.AllowMessageDeleting;
            if (action == ChatAction.Pin) return role.AllowPinningMessages;
        }
        return true;
    }
    #endregion


    /// <summary>
    /// Gets/Loads the chat.
    /// </summary>
    /// <param name="chatID">ID of the chat.</param>
    /// <returns>Chat if succeeded, null if invalid/failled.</returns>
    public static async Task<Chat?> GetChat(string chatID)
    {
        if (loadingChats.Contains(chatID))
        {
            while (loadingChats.Contains(chatID))
            {
                await Task.Delay(500);
            }
        }
        if (chatsCache.ContainsKey(chatID))
        {
            return chatsCache[chatID];
        }

        //Check validity
        if (chatID.Contains("-"))
        { //both users should exist
            string[] spl = chatID.Split("-");
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
            if (!File.Exists("data/info/" + chatID + "/info"))
            {
                return null;
            }
        }

        loadingChats.Add(chatID);

        //Load
        Chat? loadedChat = null;
        if (File.Exists("data/chat/" + chatID + "/chat"))
        {
            loadedChat = JsonConvert.DeserializeObject<Chat>(await File.ReadAllTextAsync("data/chat/" + chatID + "/chat"));
            // If that is null, we should NOT load the chat at all
        }
        else
        {
            // If it's not a federation, we know it should have existed.
            if (!chatID.Contains("@")) loadedChat = new Chat();
        }

        if (chatID.Contains("@"))
        {
            string[] split = chatID.Split("@");
            string id = split[0];
            string server = split[1];
            var connection = await Federation.Connect(server, true);
            if (connection != null)
            {
                if (connection.connected == true) loadedChat = await connection.GetChat(id);
                connection.Connected += async (_,_) =>
                {
                    Chat? newChat = await connection.GetChat(id);
                    if (newChat != null && loadedChat != null)
                    {
                        // Remake the chat with new fetched chat. Probably would be better if updates were fetched instead... works for now, tho.
                        loadedChat.Clear();
                        foreach (var msg in newChat)
                            loadedChat.TryAdd(msg.Key, msg.Value);

                        loadedChat.pinnedMessages = null;
                    }
                };
            }
        }

        if (loadedChat != null)
        {
            if (File.Exists("data/chat/" + chatID + "/updates")) //get update history
            {
                try
                {
                    var u = JsonConvert.DeserializeObject<ConcurrentDictionary<long, Dictionary<string, object?>>>(await File.ReadAllTextAsync("data/chat/" + chatID + "/updates"));
                    if (u != null) loadedChat.updates = u;
                }
                catch { } //Ignore...
            }

            loadedChat.chatID = chatID;
            loadedChat.isGroup = !chatID.Contains("-");

            if (loadedChat.isGroup)
            {
                // Load the real group
                Group? group = await Group.Get(chatID);
                if (group == null)
                {
                    loadingChats.Remove(chatID);
                    return null;
                }
                loadedChat.group = group;
            }
            else
            {
                // Make a fake group
                string[] users = chatID.Split("-");
                foreach (string user in users)
                {
                    loadedChat.group.members[user] = new GroupMember() { userID = user };
                }
            }

            chatsCache[chatID] = loadedChat;
        }

        loadingChats.Remove(chatID);
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