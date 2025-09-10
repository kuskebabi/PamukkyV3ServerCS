using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Federation request style to parse.
/// </summary>
class FederationRequest
{
    public string? serverurl = null;
}

/// <summary>
/// Federation update sending style to parse.
/// </summary>
class UpdateRecieveRequest
{
    public string? serverurl = null;
    public string? id = null;
    public UpdateHooks? updates = null;
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
    public UpdateHooks cachedUpdates;

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
        cachedUpdates = new() { token = server };
        PushUpdates();
    }

    /// <summary>
    /// Handles a Exception thrown while a federation action that uses HTTP. If it's HttpRequestError, sets connected to false.
    /// </summary>
    /// <param name="e">Exception</param>
    void HandleException(Exception e)
    {
        if (e.GetType() == typeof(HttpRequestError))
        {
            connected = false;
            Console.WriteLine("Connection error!!!");
        }

        Console.WriteLine(e.ToString());
    }


    /// <summary>
    /// Handles error status from request responses.
    /// </summary>
    /// <param name="status">Dictionary from a failled request response. Needs to have "code" key.</param>
    bool HandleStatus(Dictionary<string, object> status)
    {
        if (!status.ContainsKey("code")) return true;
        switch (status["code"].ToString())
        {
            case "IDWRONG":
                Console.WriteLine("Peer has error, reconnecting...");
                connected = false;
                _ = Reconnect(); // We don't need response of this.
                break;

            case "NOFED":
                Console.WriteLine("Peer has error, reconnecting...");
                connected = false;
                _ = Reconnect(); // We don't need response of this.
                break;
        }
        return false;
    }
    /// <summary>
    /// Pushes chat updates to remote servers, acting like a client for them.
    /// </summary>
    public async void PushUpdates()
    {
        Console.WriteLine("pushtick");
        if (await Reconnect())
        {
            UpdateHooks updates = await cachedUpdates.waitForUpdates(600, false);

            StringContent sc = new(JsonConvert.SerializeObject(new UpdateRecieveRequest() { serverurl = thisServerURL, id = id, updates = updates }));
            try
            {
                var request = await GetHttpClient().PostAsync(new Uri(new Uri(serverURL), "federationrecieveupdates"), sc);
                string resbody = await request.Content.ReadAsStringAsync();
                Console.WriteLine("push " + resbody);
                var ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(resbody);
                if (ret != null)
                    if (!HandleStatus(ret)) return;

                // Clear previous updates when successful.
                foreach (var hook in updates.Values)
                {
                    hook.Clear();
                }
            }
            catch (Exception e)
            {
                HandleException(e);
            }
        }
        PushUpdates();
    }

    /// <summary>
    /// Connect to remote federation server
    /// </summary>
    /// <param name="server">URL of server</param>
    /// <param name="dummy">If true, it creates a "dummy" federation to be referred to that server even if offine.</param>
    /// <returns>Federation class that you can interact with federation OR null if it failled.</returns>
    public static async Task<Federation?> Connect(string server, bool dummy = false) //FIXME
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
                bool r = await s.Reconnect();
                if (r == false && dummy == false) return null;
            }
            return s;
        }

        connectingFederations.Add(server);

        StringContent sc = new(JsonConvert.SerializeObject(new FederationRequest() { serverurl = thisServerURL }));
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
    /// Gets a federation from request dictionary if it's valid.
    /// </summary>
    /// <param name="request">Request dictionary</param>
    /// <returns>Federation if success, null if mismatch.</returns>
    public static Federation? GetFromRequestObjDict(IDictionary<string, object> request)
    {
        if (!request.ContainsKey("serverurl")) return null;
        if (!request.ContainsKey("id")) return null;
        if (!federations.ContainsKey(request["serverurl"].ToString() ?? "")) return null;
        Federation fed = federations[request["serverurl"].ToString() ?? ""];
        if (fed.id != request["id"].ToString()) return null;
        return fed;
    }
    
    /// <summary>
    /// Gets a federation from request dictionary if it's valid.
    /// </summary>
    /// <param name="request">Request dictionary</param>
    /// <returns>Federation if success, null if mismatch.</returns>
    public static Federation? GetFromRequestStrDict(IDictionary<string, string> request)
    {
        if (!request.ContainsKey("serverurl")) return null;
        if (!request.ContainsKey("id")) return null;
        if (!federations.ContainsKey(request["serverurl"] ?? "")) return null;
        Federation fed = federations[request["serverurl"] ?? ""];
        if (fed.id != request["id"]) return null;
        return fed;
    }

    /// <summary>
    /// Gets a federation from request that is styled as (and is) UpdateRecieveRequest if it's valid.
    /// </summary>
    /// <param name="request">UpdateRecieveRequest which was parsed from the request.</param>
    /// <returns>Federation if success, null if mismatch.</returns>

    public static Federation? GetFromRequestURR(UpdateRecieveRequest request)
    {
        if (request.serverurl == null) return null;
        if (request.id == null) return null;
        if (!federations.ContainsKey(request.serverurl ?? "")) return null;
        Federation fed = federations[request.serverurl ?? ""];
        if (fed.id != request.id) return null;
        return fed;
    }

    /// <summary>
    /// Reconnects to the federation. Does nothing while connected. So can be used before any action.
    /// </summary>
    /// <returns>Connection status</returns>
    public async Task<bool> Reconnect()
    {
        if (connected == true) return true;
        await Task.Delay(1000);
        if (connected == true) return true;
        if (connected == null)
        {
            while (connected == null)
            {
                await Task.Delay(500);
            }
            return connected ?? false;
        }
        StringContent sc = new(JsonConvert.SerializeObject(new FederationRequest() { serverurl = thisServerURL }));
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
    public void FederationRequestReconnected(string fid)
    {
        id = fid;
        connected = true;
        Connected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Get group from remote federated server
    /// </summary>
    /// <param name="groupID">ID of group inside the remote.</param>
    /// <returns>Null if request failled, false if group doesn't exist, true if group exists but it's only viewable for members, Group class if it exists and is visible for this server.</returns>
    public async Task<object?> GetGroup(string groupID)
    {
        if (!await Reconnect()) return null;

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
                    HandleStatus(ret);
                    return false;
                }
            }
            else
            {
                Group? group = JsonConvert.DeserializeObject<Group>(resbody);
                if (group == null) return null;
                FixGroup(group);
                cachedUpdates.AddHook(group);
                return group;
            }
        }
        catch (Exception e)
        {
            HandleException(e);
            return null;
        }
    }

    /// <summary>
    /// Fetches a entire chat from a federation. Listening for updates are at Pamukky.cs.
    /// </summary>
    /// <param name="chatID">ID of the chat in the remote.</param>
    /// <returns>Chat if done, null if fail.</returns>
    public async Task<Chat?> GetChat(string chatID)
    {
        if (!await Reconnect()) return null;

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
                HandleStatus(ret);
                return null;
            }
            else
            {
                Chat? chat = JsonConvert.DeserializeObject<Chat>(resbody);
                if (chat == null) return null;
                chat.chatID = chatID;
                foreach (ChatMessage msg in chat.Values)
                {
                    FixMessage(msg);
                }
                cachedUpdates.AddHook(chat);
                return chat;
            }
        }
        catch (Exception e)
        {
            HandleException(e);
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
        if (!await Reconnect()) return null;

        StringContent sc = new(JsonConvert.SerializeObject(new { serverurl = thisServerURL, id = id, userid = userID }));
        try
        {
            var request = await GetHttpClient().PostAsync(new Uri(new Uri(serverURL), "federationgetuser"), sc);
            string resbody = await request.Content.ReadAsStringAsync();
            Console.WriteLine("user " + resbody);
            var ret = JsonConvert.DeserializeObject<Dictionary<string, object>>(resbody);
            if (ret == null) return null;
            if (ret.ContainsKey("status"))
            {
                HandleStatus(ret);
                return null;
            }
            else
            {
                UserProfile? profile = JsonConvert.DeserializeObject<UserProfile>(resbody);
                if (profile == null) return null;
                FixUserProfile(profile);
                return profile;
            }
        }
        catch (Exception e)
        {
            HandleException(e);
            return null;
        }
    }

    #region Fixers
    /// <summary>
    /// Edits the message for this server because values and stuff still point for remote.
    /// </summary>
    /// <param name="message">ChatMessage to fix</param>
    public void FixMessage(ChatMessage message)
    {
        message.senderUID = FixUserID(message.senderUID);

        if (message.senderUID == "0")
        {
            if (message.content.Contains("|"))
            {
                string userid = message.content.Split("|")[1];
                message.content = message.content.Replace(userid, FixUserID(userid));
            }
        }

        if (message.forwardedFromUID != null)
        {
            message.forwardedFromUID = FixUserID(message.forwardedFromUID);
        }


        foreach (var r in message.reactions)
        {
            MessageEmojiReactions reactions = new();
            foreach (var reaction in r.Value)
            {
                reactions[FixUserID(reaction.Key)] = new()
                {
                    senderUID = FixUserID(reaction.Value.senderUID),
                    sendTime = reaction.Value.sendTime,
                    reaction = reaction.Value.reaction
                };
            }
            message.reactions[r.Key] = reactions;
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
    public string FixUserID(string userID)
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
    public void FixUserProfile(UserProfile profile)
    {
        // fix picture
        profile.picture = profile.picture.Replace("%SERVER%", serverURL);
    }

    /// <summary>
    /// Fixes group info
    /// </summary>
    /// <param name="group">Group to fix</param>
    public void FixGroup(Group group)
    {
        // fix picture
        group.picture = group.picture.Replace("%SERVER%", serverURL);
        // remake the members list.
        ConcurrentDictionary<string, GroupMember> members = new();
        foreach (var member in group.members)
        {
            string user = FixUserID(member.Key);
            members[user] = new GroupMember()
            {
                userID = user,
                joinTime = member.Value.joinTime,
                role = member.Value.role
            };
        }
        group.members = members;
    }
    #endregion
}