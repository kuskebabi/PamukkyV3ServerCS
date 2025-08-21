using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Class that handles and stores a group.
/// </summary>
class Group
{
    [JsonIgnore]
    public string groupID = "";
    public static ConcurrentDictionary<string, Group> groupsCache = new();
    public static List<string> loadingGroups = new();

    public string name = "";
    public string picture = "";
    public string info = "";
    public string creatorUID = "";
    public DateTime creationTime = DateTime.Now;
    public bool publicGroup = false; //Can the group be read without joining?
    public ConcurrentDictionary<string, GroupMember> members = new();
    public Dictionary<string, GroupRole> roles = new();
    public List<string> bannedMembers = new();

    #region Backwards compatibility
    public string owner
    {
        set { creatorUID = value; }
    }

    public string time
    {
        set { creationTime = Helpers.StringToDate(value); }
    }

    public bool publicgroup
    {
        set { publicGroup = value; }
    }
    #endregion

    public static async Task<Group?> Get(string gid)
    {
        if (loadingGroups.Contains(gid))
        {
            while (loadingGroups.Contains(gid))
            {
                await Task.Delay(500);
            }
        }
        if (groupsCache.ContainsKey(gid))
        {
            return groupsCache[gid];
        }
        loadingGroups.Add(gid);

        if (gid.Contains("@"))
        {
            string[] split = gid.Split("@");
            string id = split[0];
            string server = split[1];
            var connection = await Federation.Connect(server, true);
            if (connection != null)
            {
                if (connection.connected == true)
                {
                    var g = await connection.GetGroup(id);
                    if (g is Group)
                    {
                        var group = (Group)g;
                        group.groupID = gid;
                        groupsCache[gid] = group;
                        group.Save(); // Save the group from the federation in case it goes offline after some time.

                        loadingGroups.Remove(gid);

                        return group;
                    }
                    else if (g is bool)
                    {
                        if ((bool)g)
                        {
                            loadingGroups.Remove(gid);
                            return new Group() { groupID = gid }; //make a interface enough to join it.
                        }
                        else
                        {
                            loadingGroups.Remove(gid);
                            return null;
                        }
                    }
                }
                connection.Connected += async (_, _) =>
                {
                    var g = await connection.GetGroup(id);
                    if (g is Group)
                    {
                        var group = (Group)g;
                        group.groupID = gid;
                        groupsCache[gid] = group;
                        group.Save(); // Save the group from the federation in case it goes offline after some time.
                    }
                };
            }
        }
        if (File.Exists("data/info/" + gid + "/info"))
        {
            Group? g = JsonConvert.DeserializeObject<Group>(await File.ReadAllTextAsync("data/info/" + gid + "/info"));
            if (g != null)
            {
                g.groupID = gid;
                groupsCache[gid] = g;
            }
            loadingGroups.Remove(gid);
            return g;
        }
        loadingGroups.Remove(gid);
        return null;
    }

    #region User management

    /// <summary>
    /// Adds a user to group
    /// </summary>
    /// <param name="userID">ID of the user to add.</param>
    /// <param name="role">Role of the added user.</param>
    /// <returns>True if added to the group, false if couldn't.</returns>
    public async Task<bool> AddUser(string userID, string role = "Normal")
    {
        if (members.ContainsKey(userID))
        { // To not mess stuff up
            return true;
        }
        if (!roles.ContainsKey(role) && name != "")
        { // Again, prevent some mess
            return false;
        }
        if (bannedMembers.Contains(userID))
        { // Block banned users
            return false;
        }

        if (groupID.Contains("@"))
        {
            string[] split = groupID.Split("@");
            string id = split[0];
            string server = split[1];
            var connection = await Federation.Connect(server);
            if (connection != null)
            {
                var g = await connection.JoinGroup(userID, id);
                if (g == false)
                {
                    return false;
                } //continue
            }
        }

        if (!userID.Contains("@"))
        {
            UserChatsList? clist = await UserChatsList.Get(userID); // Get chats list of user
            if (clist == null)
            {
                return false; //user doesn't exist
            }
            ChatItem g = new()
            { // New chats list item
                group = groupID,
                type = "group"
            };
            clist.AddChat(g); //Add to their chats list
            clist.Save(); //Save their chats list
        }

        members[userID] = new()
        { // Add the member! Say hi!!
            userID = userID,
            role = role,
            jointime = Helpers.DateToString(DateTime.Now)
        };

        Chat? chat = await Chat.GetChat(groupID);
        if (chat != null)
        {
            if (chat.CanDo(userID, Chat.ChatAction.Send))
            {
                ChatMessage message = new()
                {
                    senderUID = "0",
                    content = "JOINGROUP|" + userID
                };
                chat.SendMessage(message);
            }
        }

        return true; //Success!!
    }


    /// <summary>
    /// Makes a user leave the group.
    /// </summary>
    /// <param name="userID">ID of the user</param>
    /// <returns>True if removed, false if didn't.</returns>
    public async Task<bool> RemoveUser(string userID)
    {
        if (!members.ContainsKey(userID))
        { // To not mess stuff up
            return true;
        }

        if (groupID.Contains("@"))
        {
            string[] split = groupID.Split("@");
            string id = split[0];
            string server = split[1];
            var connection = await Federation.Connect(server);
            if (connection != null)
            {
                var g = await connection.LeaveGroup(userID, id);
                if (g == false)
                {
                    return false;
                } //continue
            }
        }

        if (!userID.Contains("@"))
        {
            UserChatsList? clist = await UserChatsList.Get(userID); // Get chats list of user
            if (clist == null)
            {
                return false; //user doesn't exist
            }
            clist.RemoveChat(groupID); //Remove chat from their chats list
            clist.Save(); //Save their chats list
        }

        Chat? chat = await Chat.GetChat(groupID);
        if (chat != null)
        {
            if (chat.CanDo(userID, Chat.ChatAction.Send))
            {
                ChatMessage message = new()
                {
                    senderUID = "0",
                    content = "LEFTGROUP|" + userID
                };
                chat.SendMessage(message);
            }
        }

        members.Remove(userID, out _); //Goodbye..

        return true; //Success!!
    }


    /// <summary>
    /// Bans a user from the group.
    /// </summary>
    /// <param name="userID">ID of the user to ban from the group.</param>
    /// <returns>True if could ban the user, false if couldn't.</returns>
    public async Task<bool> BanUser(string userID)
    {
        if (await RemoveUser(userID))
        {
            if (!bannedMembers.Contains(userID))
            {
                bannedMembers.Add(userID);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Unbans a user from the group
    /// </summary>
    /// <param name="userID">ID of the user to unban</param>
    public void UnbanUser(string userID)
    {
        bannedMembers.Remove(userID);
    }

    #endregion

    #region Permissions
    public enum groupAction
    {
        Kick,
        Ban,
        EditUser,
        EditGroup,
        Read
    }

    public bool CanDo(string user, groupAction action, string? target = null)
    {
        if (action == groupAction.Read && publicGroup) return true;

        GroupMember? u = null;
        foreach (var member in members)
        { //find the user
            if (member.Value.userID == user)
            {
                u = member.Value;
            }
        }

        if (u == null)
        { // Doesn't exist? block
            return false;
        }
        if (action == groupAction.Read) return true;

        // Get the role
        GroupRole role = roles[u.role];
        //Check what the role can do depending on the request.

        if (action == groupAction.EditGroup) return role.AllowEditingSettings;

        GroupRole? targetUserRole = GetUserRole(target ?? "");
        if (targetUserRole != null)
        {
            if (action == groupAction.EditUser) return role.AllowEditingUsers && role.AdminOrder <= targetUserRole.AdminOrder && user != target;
            if (action == groupAction.Kick) return role.AllowKicking && role.AdminOrder <= targetUserRole.AdminOrder && user != target;
            if (action == groupAction.Ban) return role.AllowBanning && role.AdminOrder <= targetUserRole.AdminOrder && user != target;
        }
        else
        {
            if (action == groupAction.Ban) return role.AllowBanning;
        }

        return false;
    }
    #endregion

    /// <summary>
    /// Gets role info of user.
    /// </summary>
    /// <param name="userID">ID of the user</param>
    /// <returns>GroupRole of the role user has.</returns>
    public GroupRole? GetUserRole(string userID)
    {
        bool contains = false;
        GroupMember? u = null;
        foreach (var member in members)
        { //find the user
            if (member.Value.userID == userID)
            {
                contains = true;
                u = member.Value;
            }
        }

        if (!contains || u == null)
        { // Doesn't exist? block
            return null;
        }

        if (!roles.ContainsKey(u.role))
        { // Doesn't exist? block.
            return null;
        }

        // Get the role
        GroupRole role = roles[u.role];

        return role;
    }

    public enum StatusRole
    {
        Owner,
        Normal
    }

    /// <summary>
    /// Helper function to get a role that idenifies a normal user and the owner.
    /// </summary>
    /// <param name="role">Role that you want to get.</param>
    /// <returns>KeyValuePair that has string which is role name and GroupRole instance of the role.</returns>
    public KeyValuePair<string, GroupRole>? GetRoleFromStatus(StatusRole role)
    {
        if (role == StatusRole.Owner)
        {
            KeyValuePair<string, GroupRole>? biggestRole = null;
            foreach (var grole in roles)
            {
                if (grole.Value.AdminOrder > biggestRole?.Value.AdminOrder)
                {
                    biggestRole = grole;
                }
            }
            return biggestRole;
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Saves the group.
    /// </summary>
    public void Save()
    {
        CheckRoles();
        Directory.CreateDirectory("data/info/" + groupID);
        string c = JsonConvert.SerializeObject(this);
        File.WriteAllTextAsync("data/info/" + groupID + "/info", c);
    }

    /// <summary>
    /// Checks if nobody has owner role and sets a user as owner automatically. Also checks if owner role has all permissions.
    /// </summary>
    public void CheckRoles()
    {
        // Owner role permission check
        var ownerRole = GetRoleFromStatus(StatusRole.Owner);
        if (ownerRole == null)
        {
            GroupRole role = new(); // This is already for owner by default.
            roles["Owner"] = role;
            ownerRole = new KeyValuePair<string, GroupRole>("Owner", role);
        }
        else
        {
            GroupRole? ownerRoleContents = ownerRole?.Value;
            if (ownerRoleContents != null)
            {
                // Give all permissions
                ownerRoleContents.AdminOrder = 0;
                ownerRoleContents.AllowBanning = true;
                ownerRoleContents.AllowKicking = true;
                ownerRoleContents.AllowEditingSettings = true;
                ownerRoleContents.AllowEditingUsers = true;
                ownerRoleContents.AllowSendingReactions = true;
                ownerRoleContents.AllowPinningMessages = true;
                ownerRoleContents.AllowMessageDeleting = true;
            }
        }

        // Roles check
        GroupMember? bestMatch = null;

        foreach (var member in members.Values)
        {
            if (GetUserRole(member.userID)?.AdminOrder == 0)
            {
                // Stop it if there is a owner already.
                return;
            }
        }

        foreach (var member in members.Values)
        {
            if (member.userID == creatorUID)
            {
                // If the member is the original owner, give the role to them first.
                bestMatch = member;
                break;
            }
            if (bestMatch == null || GetUserRole(member.userID)?.AdminOrder > GetUserRole(bestMatch.userID)?.AdminOrder)
            {
                // Basically try to find the user with highest role.
                bestMatch = member;
            }
        }

        if (bestMatch != null)
        {
            bestMatch.role = ownerRole?.Key ?? "Owner";
        }
    }
}

/// <summary>
/// Stripped Group for use in stuff like /getgroup
/// </summary>
class GroupInfo
{
    public string name = "";
    public string picture = "";
    public string info = "";
    public bool publicGroup = false;
}

/// <summary>
/// Class that indicates a member in the group.
/// </summary>
class GroupMember
{
    public string userID = "";
    public string role = "";
    public DateTime joinTime = DateTime.Now;

    public string jointime
    {
        set { joinTime = Helpers.StringToDate(value); }
    }

    public string user
    {
        set { userID = value; }
    }
}

/// <summary>
/// Information of what a role is capable of.
/// </summary>
class GroupRole
{
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