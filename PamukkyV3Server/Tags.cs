using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace PamukkyV3;

/// <summary>
/// Info for public tag
/// </summary>
class PublicTag
{
    public static ConcurrentDictionary<string, PublicTag> publicTags = new();

    public static bool changed = false;
    public static void Save()
    {
        if (changed)
        {
            changed = false;
            string save = JsonConvert.SerializeObject(publicTags);
            File.WriteAllTextAsync("data/public_tags", save);
        }
    }
    public static void Load()
    {
        if (File.Exists("data/public_tags"))
        {
            publicTags = JsonConvert.DeserializeObject<ConcurrentDictionary<string, PublicTag>>(File.ReadAllText("data/public_tags")) ?? new();
            changed = false;
        }
    }

    /// <summary>
    /// Get target ID of a tag
    /// </summary>
    /// <param name="tag">Name of the tag</param>
    /// <returns>ID of the target</returns>
    public static string? GetTagTarget(string tag)
    {
        if (!publicTags.ContainsKey(tag)) return null;

        PublicTag tagc = publicTags[tag];
        if (tagc.taken)
        {
            return tagc.target;
        }

        return null;
    }

    /// <summary>
    /// Check if a tag is taken
    /// </summary>
    /// <param name="tag">Name of the tag</param>
    /// <param name="userID">ID of the user that wants to check it, null for none</param>
    /// <returns>True if taken, false if available</returns>
    public static async Task<bool> IsTagTaken(string tag, string? userID)
    {
        if (tag == "") return false;

        if (!tag.Contains("@")) tag = "@" + Pamukky.config.publicName;

        Console.WriteLine(tag);

        if (!publicTags.ContainsKey(tag)) return false;

        PublicTag tagc = publicTags[tag];
        if (!tagc.taken)
        {
            if (tagc.time < DateTime.Now) return false;

            if (userID == tagc.target)
            {
                return false;
            }

            if (userID != null)
            {
                object? target = await Pamukky.GetTargetFromID(tagc.target);
                if (target is Group)
                {
                    Console.WriteLine("group");
                    Group? gp = target as Group;
                    if (gp != null)
                    {
                        if (gp.CanDo(userID, Group.GroupAction.EditGroup)) return false;
                    }
                }
            }
        }


        Console.WriteLine("taken!");

        return true;
    }

    /// <summary>
    /// Validates format of a public tag.
    /// </summary>
    /// <param name="tag"></param>
    /// <returns></returns>
    public static bool ValidatePublicTagSyntax(string tag)
    {
        return tag == "" || (tag.Length <= 20 && Regex.IsMatch(tag, @"^[a-zA-Z0-9_]+$"));
    }

    /// <summary>
    /// Sets or removes target's public tag
    /// </summary>
    /// <param name="user">Requester user, null for none</param>
    /// <param name="tag">Name of the tag, empty string for none</param>
    /// <param name="target">Target to set public tag of</param>
    /// <returns>true if successfull</returns>
    public static async Task<bool> SetTag(string? user, string tag, string target)
    {
        if (!ValidatePublicTagSyntax(tag)) return false;

        // Format tag
        if (tag != "") tag += "@" + Pamukky.config.publicName;

        if (await IsTagTaken(tag, user)) return false;

        object? t = await Pamukky.GetTargetFromID(target);
        if (t != null)
        {
            string? previousTag = null;

            // Add to profile
            if (t is UserProfile && target == user)
            {
                UserProfile? profile = t as UserProfile;
                if (profile != null)
                {
                    previousTag = profile.publicTag;
                    profile.publicTag = tag != "" ? tag : null;
                    profile.NotifyPublicTagChange();
                    profile.Save();
                }
            }
            else if (t is Group)
            {
                Group? group = t as Group;
                if (group != null && !group.groupID.Contains("@") && group.CanDo(user ?? "", Group.GroupAction.EditGroup))
                {
                    previousTag = group.publicTag;
                    group.publicTag = tag != "" ? tag : null;
                    group.NotifyPublicTagChange();
                    group.Save();
                }
                else
                {
                    return false;
                }
            }

            if (tag != "")
            {
                // Reserve tag
                publicTags[tag] = new()
                {
                    target = target,
                    time = DateTime.Now.AddDays(1)
                };
            }

            if (previousTag != null && publicTags.ContainsKey(previousTag))
            {
                var tagc = publicTags[previousTag];
                if (tagc.time < DateTime.Now)
                {
                    tagc.taken = false;
                    tagc.time = DateTime.Now.AddHours(3);
                }
                else
                {
                    publicTags.Remove(previousTag, out _);
                }
            }

            changed = true;

            return true;
        }

        return false;
    }

    public string target = "";
    public bool taken = true;
    public DateTime? time = null;
}