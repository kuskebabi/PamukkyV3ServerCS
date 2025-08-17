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
    [JsonIgnore]
    public static ConcurrentDictionary<string, Group> groupsCache = new();
    public static List<string> loadingGroups = new();

    public string name = "";
    public string picture = "";
    public string info = "";
    public string owner = ""; //The creator, not the current one
    public string time = ""; //Creation time
    public bool publicgroup = false; //Can the group be read without joining?
    public ConcurrentDictionary<string, GroupMember> members = new();
    public Dictionary<string, GroupRole> roles = new();
    public List<string> bannedMembers = new();

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
            var connection = await Federation.connect(server, true);
            if (connection != null)
            {
                if (connection.connected == true)
                {
                    var g = await connection.getGroup(id);
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
                    var g = await connection.getGroup(id);
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

    /// <summary>
    /// Sets given group to current one.
    /// </summary>
    /// <param name="group">Group to swap with.</param>
    public void swapGroup(Group group)
    {
        group.groupID = groupID;
        groupsCache[groupID] = group;
    }

    /// <summary>
    /// Adds a user to group
    /// </summary>
    /// <param name="userID">ID of the user to add.</param>
    /// <param name="role">Role of the added user.</param>
    /// <returns>True if added to the group, false if couldn't.</returns>
    public async Task<bool> addUser(string userID, string role = "Normal")
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
            var connection = await Federation.connect(server);
            if (connection != null)
            {
                var g = await connection.joinGroup(userID, id);
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
            chatItem g = new()
            { // New chats list item
                group = groupID,
                type = "group"
            };
            clist.AddChat(g); //Add to their chats list
            clist.Save(); //Save their chats list
        }

        members[userID] = new()
        { // Add the member! Say hi!!
            user = userID,
            role = role,
            jointime = Pamukky.dateToString(DateTime.Now)
        };

        Chat? chat = await Chat.getChat(groupID);
        if (chat != null)
        {
            if (chat.canDo(userID, Chat.chatAction.Send))
            {
                ChatMessage message = new()
                {
                    sender = "0",
                    content = "JOINGROUP|" + userID,
                    time = Pamukky.dateToString(DateTime.Now)
                };
                chat.sendMessage(message);
            }
        }

        return true; //Success!!
    }


    /// <summary>
    /// Makes a user leave the group.
    /// </summary>
    /// <param name="userID">ID of the user</param>
    /// <returns>True if removed, false if didn't.</returns>
    public async Task<bool> removeUser(string userID)
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
            var connection = await Federation.connect(server);
            if (connection != null)
            {
                var g = await connection.leaveGroup(userID, id);
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

        Chat? chat = await Chat.getChat(groupID);
        if (chat != null)
        {
            if (chat.canDo(userID, Chat.chatAction.Send))
            {
                ChatMessage message = new()
                {
                    sender = "0",
                    content = "LEFTGROUP|" + userID,
                    time = Pamukky.dateToString(DateTime.Now)
                };
                chat.sendMessage(message);
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
    public async Task<bool> banUser(string userID)
    {
        if (await removeUser(userID))
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
    public void unbanUser(string userID)
    {
        bannedMembers.Remove(userID);
    }

    #region Permissions
    public enum groupAction
    {
        Kick,
        Ban,
        EditUser,
        EditGroup,
        Read
    }

    public bool canDo(string user, groupAction action, string? target = null)
    {
        if (action == groupAction.Read && publicgroup) return true;

        GroupMember? u = null;
        foreach (var member in members)
        { //find the user
            if (member.Value.user == user)
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

        GroupRole? targetUserRole = getUserRole(target ?? "");
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
    public GroupRole? getUserRole(string userID)
    {
        bool contains = false;
        GroupMember? u = null;
        foreach (var member in members)
        { //find the user
            if (member.Value.user == userID)
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

    public enum statusRole
    {
        Owner,
        Normal
    }

    /// <summary>
    /// Helper function to get a role that idenifies a normal user and the owner.
    /// </summary>
    /// <param name="role">Role that you want to get.</param>
    /// <returns>KeyValuePair that has string which is role name and GroupRole instance of the role.</returns>
    public KeyValuePair<string, GroupRole>? getStatusRole(statusRole role)
    {
        if (role == statusRole.Owner)
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
        checkRoles();
        Directory.CreateDirectory("data/info/" + groupID);
        string c = JsonConvert.SerializeObject(this);
        File.WriteAllTextAsync("data/info/" + groupID + "/info", c);
    }

    /// <summary>
    /// Checks if nobody has owner role and sets a user as owner automatically. Also checks if owner role has all permissions.
    /// </summary>
    public void checkRoles()
    {
        // Owner role permission check
        var ownerRole = getStatusRole(statusRole.Owner);
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
            if (getUserRole(member.user)?.AdminOrder == 0)
            {
                // Stop it if there is a owner already.
                return;
            }
        }

        foreach (var member in members.Values)
        {
            if (member.user == owner)
            {
                // If the member is the original owner, give the role to them first.
                bestMatch = member;
                break;
            }
            if (bestMatch == null || getUserRole(member.user)?.AdminOrder > getUserRole(bestMatch.user)?.AdminOrder)
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
    public bool publicgroup = false;
}

/// <summary>
/// Class that indicates a member in the group.
/// </summary>
class GroupMember
{
    public string user = "";
    public string role = "";
    public string jointime = "";
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