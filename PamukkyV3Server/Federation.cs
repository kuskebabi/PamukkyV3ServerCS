using System.Collections.Concurrent;
using System.Linq.Expressions;
using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Federation request style to parse.
/// </summary>
class federationRequest
{
    public string? serverurl = null;
}

/// <summary>
/// Logic for federation in Pamukky. Currently it's WIP and you shouldn't really use it.
/// </summary>
class Federation
{
    [JsonIgnore]
    public static ConcurrentDictionary<string, Federation> federations = new();
    [JsonIgnore]
    static HttpClient? federationClient = null;
    [JsonIgnore]
    public static string thisServerURL = "http://localhost:4268/";
    [JsonIgnore]
    static List<string> connectingFederations = new();
    /// <summary>
    /// Tristate varailable to indicate connection status.
    /// True: connected, False: disconnected, null: reconnecting
    /// </summary>
    [JsonIgnore]
    public bool? connected = true;
    [JsonIgnore]
    public ConcurrentDictionary<string, List<Dictionary<string, object?>>> cachedUpdates = new();

    public string serverURL;
    public string id;
    /// <summary>
    /// Fires when federation is (re)connected.
    /// </summary>
    public event EventHandler? Connected;

    /// <summary>
    /// Gets HttpClient that is used for federation.
    /// </summary>
    /// <returns></returns>
    public static HttpClient GetHttpClient()
    {
        if (federationClient != null)
        {
            return federationClient;
        }
        federationClient = new();
        return federationClient;
    }

    public Federation(string server, string fid)
    {
        serverURL = server;
        id = fid;
    }

    List<Dictionary<string, object?>> getCachedUpdates(string chatid)
    {
        if (cachedUpdates.ContainsKey(chatid))
        {
            return cachedUpdates[chatid];
        }
        cachedUpdates[chatid] = new();
        return cachedUpdates[chatid];
    }

    /// <summary>
    /// Handles a Exception thrown while a federation action that uses HTTP. If it's HttpRequestError, sets connected to false.
    /// </summary>
    /// <param name="e">Exception</param>
    void handleException(Exception e)
    {
        if (e.GetType() == typeof(HttpRequestError))
        {
            connected = false;
            Console.WriteLine("Connection error!!!");
        }
    }

    void handleStatus(Dictionary<string,object> status) {
        switch (status["code"].ToString())
        {
            case "IDWRONG":
                Console.WriteLine("Peer has error, marking as disconnected.");
                connected = false;
                break;
            
            case "NOFED":
                Console.WriteLine("Peer has error, marking as disconnected.");
                connected = false;
                break;
        }
    }
    /// <summary>
    /// Pushes chat updates to remote servers, acting like a client for them.
    /// </summary>
    /// <param name="chatid">Chat ID for remote</param>
    /// <param name="updates">List of updates. Values of updates dictionary can be used.</param>
    public async void pushChatUpdates(string chatid, List<Dictionary<string, object?>>? updates = null)
    {
        await reconnect();
        List<Dictionary<string, object?>> clonedUpdates = new();
        List<Dictionary<string, object?>> updatesToSend = new(getCachedUpdates(chatid));

        if (updates != null)
        {
            updatesToSend.AddRange(updates);
            clonedUpdates = new(updates);
        }


        StringContent sc = new(JsonConvert.SerializeObject(new { serverurl = thisServerURL, id = id, updates = updatesToSend, chatid = chatid }));
        try
        {
            var request = await GetHttpClient().PostAsync(new Uri(new Uri(serverURL), "federationrecievechatupdates"), sc);
            string resbody = await request.Content.ReadAsStringAsync();
            Console.WriteLine("push " + resbody);
            var ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(resbody);
            if (ret == null) return;
            if (ret.ContainsKey("status"))
            {
                handleStatus(ret);
            }
        }
        catch (Exception e)
        {
            handleException(e);
            // On error add them to the updates list and let the other pushChatUpdates try again.
            getCachedUpdates(chatid).AddRange(clonedUpdates);
            Console.WriteLine(e.ToString());
        }
    }

    /// <summary>
    /// Connect to remote federation server
    /// </summary>
    /// <param name="server">URL of server</param>
    /// <param name="dummy">If true, it creates a "dummy" federation to be referred to that server even if offine.</param>
    /// <returns>Federation class that you can interact with federation OR null if it failled.</returns>
    public static async Task<Federation?> connect(string server, bool dummy = false) //FIXME
    {
        if (connectingFederations.Contains(server)) // If server is already attempting to connect, wait for it.
        {
            while (connectingFederations.Contains(server))
            {
                await Task.Delay(500);
            }
        }

        if (federations.ContainsKey(server)) // Don't attempt to connect if already connected and return the federation.
        {
            var s = federations[server];
            if (s.connected == false)
            {
                bool r = await s.reconnect();
                if (r == false && dummy == false) return null;
            }
            return s;
        }

        connectingFederations.Add(server);
        
        StringContent sc = new(JsonConvert.SerializeObject(new federationRequest() { serverurl = thisServerURL }));
        try
        {
            var res = await GetHttpClient().PostAsync(new Uri(new Uri(server), "federationrequest"), sc);
            string resbody = await res.Content.ReadAsStringAsync();
            Console.WriteLine(resbody);
            var fed = JsonConvert.DeserializeObject<Federation>(resbody);
            if (fed == null) return null;
            Federation cf = new(server, fed.id);
            federations[server] = cf;
            connectingFederations.Remove(server);
            return cf;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
            connectingFederations.Remove(server);
            if (dummy)
            {
                Federation cf = new(server, "") { connected = false };
                federations[server] = cf; // Probably need to find a better way.
                return cf;
            }
            return null;
        }
    }

    /// <summary>
    /// Reconnects to the federation. Does nothing while connected. So can be used before any action.
    /// </summary>
    /// <returns>Connection status</returns>
    public async Task<bool> reconnect()
    {
        if (connected == true) return true;
        if (connected == null)
        {
            while (connect == null)
            {
                await Task.Delay(500);
            }
            return connected ?? false;
        }
        StringContent sc = new(JsonConvert.SerializeObject(new federationRequest() { serverurl = thisServerURL }));
        try
        {
            var res = await GetHttpClient().PostAsync(new Uri(new Uri(serverURL), "federationrequest"), sc);
            string resbody = await res.Content.ReadAsStringAsync();
            Console.WriteLine(resbody);
            var fed = JsonConvert.DeserializeObject<Federation>(resbody);
            if (fed == null) return false;
            id = fed.id;
            connected = true;
            Connected?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
            connected = false;
            return false;
        }
    }

    /// <summary>
    /// Get group from remote federated server
    /// </summary>
    /// <param name="groupID">ID of group inside the remote.</param>
    /// <returns>Null if request failled, false if group doesn't exist, true if group exists but it's only viewable for members, Group class if it exists and is visible for this server.</returns>
    public async Task<object?> getGroup(string groupID)
    {
        await reconnect();
        StringContent sc = new(JsonConvert.SerializeObject(new { serverurl = thisServerURL, id = id, groupid = groupID }));
        try
        {
            var request = await GetHttpClient().PostAsync(new Uri(new Uri(serverURL), "federationgetgroup"), sc);
            string resbody = await request.Content.ReadAsStringAsync();
            //Console.WriteLine("getgroup " + resbody);
            var ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(resbody);
            if (ret == null) return null;
            if (ret.ContainsKey("status"))
            {
                if (ret["status"].ToString() == "exists")
                {
                    Console.WriteLine("Group exists.");
                    return true;
                }
                else
                {
                    handleStatus(ret);
                    return false;
                }
            }
            else
            {
                Group? group = JsonConvert.DeserializeObject<Group>(resbody);
                if (group == null) return null;
                fixGroup(group);
                return group;
            }
        }
        catch (Exception e)
        {
            handleException(e);
            Console.WriteLine(e.ToString());
            return null;
        }
    }

    /// <summary>
    /// Join to a group in the remote server
    /// </summary>
    /// <param name="userID">ID of user that will join.</param>
    /// <param name="groupID">Group ID inside the remote.</param>
    /// <returns>True if joined, false if couldn't</returns>
    public async Task<bool> joinGroup(string userID, string groupID)
    {
        await reconnect();
        StringContent sc = new(JsonConvert.SerializeObject(new { serverurl = thisServerURL, id = id, groupid = groupID, userid = userID }));
        try
        {
            var request = await GetHttpClient().PostAsync(new Uri(new Uri(serverURL), "federationjoingroup"), sc);
            string resbody = await request.Content.ReadAsStringAsync();
            //Console.WriteLine("joingroup " + resbody);
            var ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(resbody);
            if (ret == null) return false;
            if (ret.ContainsKey("status"))
            {
                if (ret["status"].ToString() == "done")
                {
                    return true;
                }
                else
                {
                    handleStatus(ret);
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        catch (Exception e)
        {
            handleException(e);
            Console.WriteLine(e.ToString());
            return false;
        }
    }

    /// <summary>
    /// Leaves the group from remote
    /// </summary>
    /// <param name="userID">User ID that will leave.</param>
    /// <param name="groupID">Group ID in the remote.</param>
    /// <returns></returns>
    public async Task<bool> leaveGroup(string userID, string groupID)
    {
        await reconnect();
        StringContent sc = new(JsonConvert.SerializeObject(new { serverurl = thisServerURL, id = id, groupid = groupID, userid = userID }));
        try
        {
            var res = await GetHttpClient().PostAsync(new Uri(new Uri(serverURL), "federationleavegroup"), sc);
            string resbody = await res.Content.ReadAsStringAsync();
            //Console.WriteLine("leavegroup " + resbody);
            var ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(resbody);
            if (ret == null) return false;
            if (ret.ContainsKey("status"))
            {
                if (ret["status"].ToString() == "done")
                {
                    return true;
                }
                else
                {
                    handleStatus(ret);
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        catch (Exception e)
        {
            handleException(e);
            Console.WriteLine(e.ToString());
            return false;
        }
    }

    /// <summary>
    /// Fetches a entire chat from a federation. Listening for updates are at Pamukky.cs.
    /// </summary>
    /// <param name="chatID">ID of the chat in the remote.</param>
    /// <returns>Chat if done, null if fail.</returns>
    public async Task<Chat?> getChat(string chatID)
    {
        await reconnect();
        StringContent sc = new(JsonConvert.SerializeObject(new { serverurl = thisServerURL, id = id, chatid = chatID }));
        try
        {
            var request = await GetHttpClient().PostAsync(new Uri(new Uri(serverURL), "federationgetchat"), sc);
            string resbody = await request.Content.ReadAsStringAsync();
            //Console.WriteLine("chat " + resbody);
            var ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(resbody);
            if (ret == null) return null;
            if (ret.ContainsKey("status"))
            {
                handleStatus(ret);
                return null;
            }
            else
            {
                Chat? chat = JsonConvert.DeserializeObject<Chat>(resbody);
                if (chat == null) return null;
                chat.connectedFederations.Add(this);
                foreach (ChatMessage msg in chat.Values)
                {
                    fixMessage(msg);
                }
                return chat;
            }
        }
        catch (Exception e)
        {
            handleException(e);
            Console.WriteLine(e.ToString());
            return null;
        }
    }

    /// <summary>
    /// Gets a user from the remote federation.
    /// </summary>
    /// <param name="userID"></param>
    /// <returns></returns>
    public async Task<UserProfile?> getUser(string userID)
    {
        await reconnect();
        StringContent sc = new(JsonConvert.SerializeObject(new { uid = userID }));
        try
        {
            var request = await GetHttpClient().PostAsync(new Uri(new Uri(serverURL), "getuser"), sc);
            string resbody = await request.Content.ReadAsStringAsync();
            Console.WriteLine("user " + resbody);
            var ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(resbody);
            if (ret == null) return null;
            if (ret.ContainsKey("status"))
            {
                handleStatus(ret);
                return null;
            }
            else
            {
                UserProfile? profile = JsonConvert.DeserializeObject<UserProfile>(resbody);
                if (profile == null) return null;
                fixUserProfile(profile);
                return profile;
            }
        }
        catch (Exception e)
        {
            handleException(e);
            Console.WriteLine(e.ToString());
            return null;
        }
    }

    #region Fixers
    /// <summary>
    /// Edits the message for this server because values and stuff still point for remote.
    /// </summary>
    /// <param name="message">ChatMessage to fix</param>
    public void fixMessage(ChatMessage message)
    {
        message.sender = fixUserID(message.sender);

        if (message.sender == "0")
        {
            if (message.content.Contains("|"))
            {
                string userid = message.content.Split("|")[1];
                message.content = message.content.Replace(userid, fixUserID(userid));
            }
        }

        if (message.forwardedfrom != null)
        {
            message.forwardedfrom = fixUserID(message.forwardedfrom);
        }


        foreach (MessageEmojiReactions r in message.reactions.Values)
        {
            ConcurrentDictionary<string, MessageReaction> reactions = new();
            foreach (var reaction in r)
            {
                reactions[fixUserID(reaction.Key)] = new()
                {
                    sender = fixUserID(reaction.Value.sender),
                    time = reaction.Value.time,
                    reaction = reaction.Value.reaction
                };
            }
        }

        if (message.files != null)
        {
            List<string> files = new();
            foreach (var file in message.files)
            {
                files.Add(file.Replace("%SERVER%", serverURL));
            }
            message.files = files;
        }
    }

    /// <summary>
    /// Makes user ID point to correct server
    /// </summary>
    /// <param name="userID"></param>
    /// <returns></returns>
    public string fixUserID(string userID)
    {
        string user;
        string userserver;
        if (userID.Contains("@"))
        {
            string[] usplit = userID.Split("@");
            user = usplit[0];
            userserver = usplit[1];
        }
        else
        {
            user = userID;
            userserver = serverURL;
        }
        if (user != "0")
        {
            // remake(or reuse) the user string depending on the server.
            if (userserver == serverURL)
            {
                return user + "@" + userserver;
            }
            else if (userserver == thisServerURL)
            {
                return user;
            }
        }
        return userID;
    }

    /// <summary>
    /// Fixes user profile (assuming it was from a remote server=
    /// </summary>
    /// <param name="profile">UserProfile to fix</param>
    public void fixUserProfile(UserProfile profile)
    {
        // fix picture
        profile.picture = profile.picture.Replace("%SERVER%", serverURL);
    }

    /// <summary>
    /// Fixes group info
    /// </summary>
    /// <param name="group">Group to fix</param>
    public void fixGroup(Group group)
    {
        // fix picture
        group.picture = group.picture.Replace("%SERVER%", serverURL);
        // remake the members list.
        ConcurrentDictionary<string, GroupMember> members = new();
        foreach (var member in group.members)
        {
            string user = fixUserID(member.Key);
            members[user] = new GroupMember()
            {
                user = user,
                jointime = member.Value.jointime,
                role = member.Value.role
            };
        }
        group.members = members;
    }
    #endregion
}