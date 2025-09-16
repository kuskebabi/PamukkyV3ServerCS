using System.Collections.Concurrent;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Class to hold all of notifications.
/// </summary>
class Notifications : ConcurrentDictionary<string, UserNotifications>
{
    public static Notifications notifications = new();
    /// <summary>
    /// Gets notifications of a user.
    /// </summary>
    /// <param name="userID">ID of the user.</param>
    /// <returns>UserNotifications of the user.</returns>
    public static UserNotifications Get(string userID)
    {
        if (!notifications.ContainsKey(userID))
        {
            notifications[userID] = new();
        }
        return notifications[userID];
    }
}

/// <summary>
/// Class to hold notifications of a user.
/// </summary>
class UserNotifications : ConcurrentDictionary<string, MessageNotification>
{
    [JsonIgnore]
    public Dictionary<string, UserNotifications> notificationsForDevices = new();


    /// <summary>
    /// Gets notifications for a specific device.
    /// </summary>
    /// <param name="token">Token of login</param>
    /// <returns>UserNotifications that the device didn't recieve.</returns>
    public UserNotifications GetNotifications(string token)
    {
        if (!notificationsForDevices.ContainsKey(token)) notificationsForDevices[token] = new();
        return notificationsForDevices[token];
    }

    public void AddNotification(MessageNotification notif)
    {
        string key = DateTime.Now.Ticks.ToString();
        foreach (UserNotifications deviceNotifications in notificationsForDevices.Values)
        {
            deviceNotifications[key] = notif;
        }
    }
}

/// <summary>
/// Notification for a message.
/// </summary>
class MessageNotification
{
    public string? chatid;
    public string? userid;
    public ShortProfile? user;
    public string content = "";
}

/// <summary>
/// Login credentials
/// </summary>
class UserLoginRequest
{
    public string EMail = "";
    public string Password = "";
}

/// <summary>
/// Class that handles user sessions
/// </summary>
class UserSession
{
    public static ConcurrentDictionary<string, UserSession> UserSessions = new();

    public string userID = "";
    public string token = "";

    /// <summary>
    /// Creates a user login session with a random token and id of the user.
    /// </summary>
    /// <param name="userID"></param>
    /// <returns></returns>
    public static UserSession CreateSession(string userID)
    {
        //Console.WriteLine("Logging in...");
        string token = "";
        do
        {
            //Console.WriteLine("Generating token...");
            token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
        }
        while (UserSessions.ContainsKey(token));

        var session = new UserSession() { userID = userID, token = token };

        //Console.WriteLine("Generated token");
        UserSessions[token] = session;

        return session;
    }

    /// <summary>
    /// Makes session invalid
    /// </summary>
    public void LogOut()
    {
        UserSessions.Remove(token, out _);
    }
}

/// <summary>
/// Class that handles login of the user
/// </summary>
class UserLogin
{
    public string EMail = "";
    public string Password = "";
    public string userID = "";

    public static async Task<UserLogin?> Get(string email)
    {
        if (!File.Exists("data/auth/" + email)) return null;

        return JsonConvert.DeserializeObject<UserLogin>(await File.ReadAllTextAsync("data/auth/" + email));
    }

    public static async Task<UserSession?> Login(UserLoginRequest request)
    {
        request.EMail = request.EMail.Trim();

        UserLogin? credentials = await Get(request.EMail);
        if (credentials == null) return null;

        request.Password = Helpers.HashPassword(request.Password, credentials.userID);
        if (credentials.Password != request.Password || credentials.EMail != request.EMail) return null;

        return UserSession.CreateSession(credentials.userID);
    }
}

/// <summary>
/// Class for muted chat config.
/// </summary>
class MutedChatData
{
    public bool allowTags = true;
}

/// <summary>
/// Class to hold user-private settings like muted chats.
/// </summary>
class UserConfig
{
    [JsonIgnore]
    public string userID = "";
    [JsonIgnore]
    public static ConcurrentDictionary<string, UserConfig> userConfigCache = new();

    public ConcurrentDictionary<string, MutedChatData> mutedChats = new();

    public static async Task<UserConfig?> Get(string userID)
    {
        if (userID.Contains("@")) return null; // Don't attempt to get config for federation.
        if (userConfigCache.ContainsKey(userID))
        {
            return userConfigCache[userID];
        }
        else
        {
            if (!File.Exists("data/info/" + userID + "/profile")) return null; //Check if user exists

            if (File.Exists("data/info/" + userID + "/config")) // check if config file exists
            {
                try
                {
                    UserConfig? userconfig = JsonConvert.DeserializeObject<UserConfig>(await File.ReadAllTextAsync("data/info/" + userID + "/config"));
                    if (userconfig != null)
                    {
                        userconfig.userID = userID;
                        userConfigCache[userID] = userconfig;
                        return userconfig;
                    }
                }
                catch // Act like it didn't exist.
                {
                    UserConfig uc = new() { userID = userID };
                    userConfigCache[userID] = uc;
                    return uc;
                }
            }
            else // if doesn't exist, create new one
            {
                UserConfig uc = new() { userID = userID };
                userConfigCache[userID] = uc;
                return uc;
            }
        }
        return null;
    }

    /// <summary>
    /// Helper function to get if chat message should notify the user.
    /// </summary>
    /// <param name="chatID">ID of the chat.</param>
    /// <param name="isMention">If message contains mention or not.</param>
    /// <returns></returns>
    public bool CanSendNotification(string chatID, bool isMention)
    {
        if (mutedChats.ContainsKey(chatID))
        {
            MutedChatData config = mutedChats[chatID];
            if (config.allowTags)
            {
                if (!isMention)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    public void Save()
    {
        File.WriteAllTextAsync("data/info/" + userID + "/config", JsonConvert.SerializeObject(this)); // save to file
    }
}

/// <summary>
/// Class to hold current user status like typing in a chat.
/// </summary>
class UserStatus
{
    const int timeout = 3;
    public static ConcurrentDictionary<string, UserStatus> userstatus = new();
    /// <summary>
    /// Chat ID of the chat which the user was typing it, please use getTyping if you want to check if user is typing.
    /// </summary>
    public string typingChat = "";
    /// <summary>
    /// This is used only to check if user is still typing.
    /// </summary>
    private DateTime? typeTime;
    /// <summary>
    /// ID of the user.
    /// </summary>
    public string user;

    /// <summary>
    /// Gets if user is typing in a chat
    /// </summary>
    /// <param name="chatID">ID of the chat.</param>
    /// <returns></returns>
    public bool GetTyping(string chatID)
    {
        if (typeTime == null)
        {
            return false;
        }
        else
        {
            return typeTime.Value.AddSeconds(timeout) > DateTime.Now && chatID == typingChat;
        }
    }

    public UserStatus(string userID)
    {
        user = userID;
    }

    /// <summary>
    /// Sets user as typing in the chat
    /// </summary>
    /// <param name="chatID">ID of the chat</param>
    public async void SetTyping(string? chatID)
    {
        //Remove typing if null was passed
        if (chatID == null)
        {
            Chat? chat = await Chat.GetChat(typingChat);
            if (chat != null) chat.RemoveTyping(user);
        }
        else
        {
            //Set user as typing at the chat
            Chat? chat = await Chat.GetChat(chatID);
            if (chat != null)
            {
                typeTime = DateTime.Now;
                typingChat = chatID;
                chat.SetTyping(user);
                // Disable warning because it's supposed to be like that
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Task.Delay(3100).ContinueWith((task) =>
                { // automatically set user as not typing.
                    if (chatID == typingChat && !(typeTime.Value.AddSeconds(timeout) > DateTime.Now))
                    { //Check if it's the same typing update.
                        typeTime = null;
                        chat.RemoveTyping(user);
                    }
                });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }
        }
    }

    public static UserStatus? Get(string userID)
    {
        if (userstatus.ContainsKey(userID)) // Return if cached.
        {
            return userstatus[userID];
        }
        else
        {
            if (File.Exists("data/info/" + userID + "/profile"))
            { // check
                UserStatus us = new UserStatus(userID);
                userstatus[userID] = us;
                return us;
            }
        }
        return null;
    }
}

/// <summary>
/// Last status of user
/// </summary>
class LastUserStatus
{
    public DateTime lastOnline = DateTime.MinValue;
}

/// <summary>
/// Profile of a user.
/// </summary>
class UserProfile
{
    public static ConcurrentDictionary<string, UserProfile> userProfileCache = new();
    [JsonIgnore]
    public string userID = "";
    [JsonIgnore]
    public List<UpdateHook> updateHooks = new();
    public static List<string> loadingProfiles = new();

    private LastUserStatus lastStatus = new();

    public string name = "User";
    public string picture = "";
    public string bio = "Hello!";
    private DateTime? lastOnlineTime;

    #region Backwards compatibility
    public string description
    {
        set { bio = value; }
    }
    #endregion

    public string onlineStatus
    {
        get { return GetOnline(); }
        set { SetOnlineString(value); }
    }

    /// <summary>
    /// Sets the user online.
    /// </summary>
    public void SetOnline()
    {
        lastOnlineTime = DateTime.Now;
        foreach (var hook in updateHooks)
        {
            hook["online"] = "Online";
        }
        Task.Delay(10100).ContinueWith((task) =>
        { // re-check after 10 seconds to set user as not online or not
            if (onlineStatus != "Online")
            {
                foreach (var hook in updateHooks)
                {
                    hook["online"] = onlineStatus;
                }
            }
        });
    }


    /// <summary>
    /// Sets the user online or offline depending on the datetime given.
    /// </summary>
    public void SetOnlineDateTime(DateTime time)
    {
        lastOnlineTime = time;

        if (IsOnline())
        {
            foreach (var hook in updateHooks)
            {
                hook["online"] = "Online";
            }
            Task.Delay(10100).ContinueWith((task) =>
            { // re-check after 10 seconds to set user as not online or not
                if (onlineStatus != "Online")
                {
                    foreach (var hook in updateHooks)
                    {
                        hook["online"] = onlineStatus;
                    }
                }
            });
        }
        else
        {
            foreach (var hook in updateHooks)
            {
                hook["online"] = onlineStatus;
            }
        }
    }

    /// <summary>
    /// Sets the user online or offline depending on the datetime given.
    /// </summary>
    public void SetOnlineString(string status)
    {
        if (status == "Online")
        {
            SetOnline();
        }
        else
        {
            try
            {
                SetOnlineDateTime(DateTime.Parse(status, null, System.Globalization.DateTimeStyles.RoundtripKind));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    /// <summary>
    /// Gets if user is online
    /// </summary>
    /// <returns>"Online" if online, last online date as string if offline.</returns>
    public string GetOnline()
    {
        if (lastOnlineTime == null)
        {
            return Helpers.DateToString(DateTime.MinValue);
        }
        else
        {
            if (IsOnline())
            {
                return "Online";
            }
            else
            { //Return last online
                return Helpers.DateToString(lastOnlineTime.Value);
            }
        }
    }

    /// <summary>
    /// Gets if user is online
    /// </summary>
    /// <returns>true if online, false if offline.</returns>
    public bool IsOnline()
    {
        if (lastOnlineTime == null) lastOnlineTime = DateTime.MinValue;
        return lastOnlineTime.Value.AddSeconds(10) > DateTime.Now;
    }

    /// <summary>
    /// Gets profile of a user.
    /// </summary>
    /// <param name="userID">ID of the user.</param>
    /// <returns></returns>
    public static async Task<UserProfile?> Get(string userID)
    {
        if (loadingProfiles.Contains(userID))
        {
            while (loadingProfiles.Contains(userID))
            {
                await Task.Delay(500);
            }
        }

        if (userProfileCache.ContainsKey(userID))
        {
            return userProfileCache[userID];
        }

        loadingProfiles.Add(userID);

        if (userID.Contains("@"))
        {
            string[] split = userID.Split("@");
            string id = split[0];
            string server = split[1];
            var connection = await Federation.Connect(server);
            if (connection != null)
            {
                UserProfile? up = await connection.getUser(id);
                if (up != null)
                {
                    up.userID = userID;
                    userProfileCache[userID] = up;
                    up.Save(); // Save the user from the federation in case it goes offline after some time.
                    loadingProfiles.Remove(userID);
                    return up;
                }
            }
        }
        if (File.Exists("data/info/" + userID + "/profile"))
        { // check
            UserProfile? up = JsonConvert.DeserializeObject<UserProfile>(await File.ReadAllTextAsync("data/info/" + userID + "/profile"));
            if (up != null)
            {
                up.userID = userID;
                userProfileCache[userID] = up;

                if (File.Exists("data/info/" + userID + "/laststatus"))
                {
                    LastUserStatus? lastUserStatus = JsonConvert.DeserializeObject<LastUserStatus>(await File.ReadAllTextAsync("data/info/" + userID + "/laststatus"));
                    if (lastUserStatus != null)
                    {
                        up.lastStatus = lastUserStatus;
                        up.lastOnlineTime = lastUserStatus.lastOnline;
                    }
                }

                loadingProfiles.Remove(userID);
                return up;
            }
        }

        loadingProfiles.Remove(userID);

        return null;
    }

    /// <summary>
    /// Creates a profile for a (new) user.
    /// </summary>
    /// <param name="userID">ID of the new user.</param>
    /// <param name="profile">UserProfile that profile will set to.</param>
    public static void Create(string userID, UserProfile profile)
    {
        userProfileCache[userID] = profile; //set
        profile.userID = userID;
        profile.Save();
    }

    /// <summary>
    /// Saves the profile.
    /// </summary>
    public void Save()
    {
        if (userID == "0") return;
        Directory.CreateDirectory("data/info/" + userID);

        foreach (var hook in updateHooks)
        {
            hook["profileUpdate"] = this;
        }
        File.WriteAllTextAsync("data/info/" + userID + "/profile", JsonConvert.SerializeObject(this));
    }

    /// <summary>
    /// Saves last user status like last online.
    /// </summary>
    public void SaveStatus()
    {
        if (userID == "0") return;
        Directory.CreateDirectory("data/info/" + userID);
        
        if (lastStatus.lastOnline != lastOnlineTime && lastOnlineTime != null)
        {
            lastStatus.lastOnline = (DateTime)lastOnlineTime;
            File.WriteAllTextAsync("data/info/" + userID + "/laststatus", JsonConvert.SerializeObject(lastStatus));
        }
    }
}

/// <summary>
/// Short version of userProfile.
/// </summary>
class ShortProfile
{
    public string name = "User";
    public string picture = "";

    /// <summary>
    /// Turns UserProfile into ShortProfile.
    /// </summary>
    /// <param name="profile"></param>
    /// <returns></returns>
    public static ShortProfile FromProfile(UserProfile? profile)
    {
        if (profile != null)
        {
            return new ShortProfile() { name = profile.name, picture = profile.picture };
        }
        return new ShortProfile();
    }

    /// <summary>
    /// Turns Group into ShortProfile.
    /// </summary>
    /// <param name="group"></param>
    /// <returns></returns>
    public static ShortProfile FromGroup(Group? group)
    {
        if (group != null)
        {
            return new ShortProfile() { name = group.name, picture = group.picture };
        }
        return new ShortProfile();
    }
}

/// <summary>
/// Chats list of a user.
/// </summary>
class UserChatsList : List<ChatItem>
{
    public static ConcurrentDictionary<string, UserChatsList> userChatsCache = new();
    /// <summary>
    /// User ID of who owns this list.
    /// </summary>
    public string userID = "";

    public List<UpdateHook> hooks = new();

    public static async Task<UserChatsList?> Get(string userID)
    { // Get chats list
        if (userChatsCache.ContainsKey(userID))
        { // Use cache
            return userChatsCache[userID];
        }
        else
        { //Load it from file
            if (File.Exists("data/info/" + userID + "/chatslist"))
            {
                UserChatsList? uc = JsonConvert.DeserializeObject<UserChatsList>(await File.ReadAllTextAsync("data/info/" + userID + "/chatslist"));
                if (uc != null)
                {
                    uc.userID = userID;
                    userChatsCache[userID] = uc;
                    return uc;
                }
            }
            else
            {
                if (Directory.Exists("data/info/" + userID))
                {
                    return new UserChatsList() { userID = userID };
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Adds a chat to user's chats list if it doesn't exist.
    /// </summary>
    /// <param name="item">chatItem to add.</param>
    public void AddChat(ChatItem item)
    { //Add to chats list
        foreach (var i in this)
        { //Check if it doesn't exist
            if (i.type == "group")
            {
                if (i.group == item.group) return;
            }
            else if (i.type == "user")
            {
                if (i.user == item.user) return;
            }
        }

        Add(item);

        foreach (UpdateHook hook in hooks) // Notify new chat item to hooks
        {
            hook[item.chatid ?? item.group ?? ""] = item;
        }
    }

    /// <summary>
    /// Removes a chat from user's chats list by chat ID.
    /// </summary>
    /// <param name="chatID">ID of the chat to remove.</param>
    public void RemoveChat(string chatID)
    { //Remove from chats list
        var itm = this.Where(i => (i.chatid ?? i.group ?? "") == chatID).FirstOrDefault();
        if (itm != null)
        {
            Remove(itm);
            foreach (UpdateHook hook in hooks) // Notify deleted chat item to hooks
            {
                hook[itm.chatid ?? itm.group ?? ""] = "DELETED";
            }
        }
    }

    public void Save()
    {
        File.WriteAllText("data/info/" + userID + "/chatslist", JsonConvert.SerializeObject(this));
    }
}

/// <summary>
/// A single chats list item.
/// </summary>
class ChatItem
{
    public string? chatid;
    public string type = "";

    // Optional because depends on it's type.
    public string? user = null;
    public string? group = null;
}