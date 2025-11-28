// This file contains helper functions that doesn't have a specific class for it. like date to string.

using System.Text;
using Konscious.Security.Cryptography;

namespace PamukkyV3;

public static class Helpers
{
    // DEPRECATED Date serialization, do not change unless you apply it to clients too.
    public const string DateTimeFormat = "MM dd yyyy, HH:mm zzz";

    /// <summary>
    /// Converts DateTime to Pamukky-like date string. (DEPRACATED)
    /// </summary>
    /// <param name="date"></param>
    /// <returns></returns>
    public static string DateToString(DateTime date)
    {
        return date.ToString(DateTimeFormat);
    }

    /// <summary>
    /// Converts Pamukky-like date string(DEPRACATED) to new DateTime. 
    /// </summary>
    /// <param name="date"></param>
    /// <returns></returns>
    public static DateTime StringToDate(string date)
    {
        return DateTime.ParseExact(date, DateTimeFormat, null);
    }


    /// <summary>
    /// Hashes a password (with Argon2id)
    /// </summary>
    /// <param name="pass">Password</param>
    /// <param name="uid">User ID that will be used as AssociatedData.</param>
    /// <returns></returns>
    public static string HashPassword(string pass, string uid)
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
}