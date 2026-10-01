using System.Security.Cryptography;
using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Thrown for anything wrong with a project container file: not a
/// Sandcastle file at all, made by an incompatible future/past version, or
/// belonging to a different project (the "marriage" check). Callers show
/// Message directly to the person - it's written to already be readable,
/// not a stack trace.
/// </summary>
public class ProjectFileException : Exception
{
    public ProjectFileException(string message) : base(message) { }
    public ProjectFileException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The shared on-disk format behind .msrp, shapes.sdbx, and objects.sdbx.
/// Every one of these files is AES-256 encrypted and stamped with the
/// owning project's ID - the "marriage" the person asked for isn't just a
/// naming convention, it's checked on every single load, and a mismatch or
/// corrupt file throws ProjectFileException with a plain-language reason
/// instead of silently producing garbage data.
///
/// Honest caveat worth keeping in mind: the encryption key is derived from
/// the project's own ID plus a fixed key baked into this app, not a
/// person-chosen password. That's enough to stop these files being opened,
/// hand-edited, or shared outside this app - it is NOT meant to withstand
/// someone who has both the file and a copy of Sandcastle RPG Maker itself.
/// </summary>
public static class ProjectContainer
{
    private const int FormatVersion = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SRPG");

    public static void Save(string path, Guid projectId, string plainText)
    {
        byte[] key = DeriveKey(projectId);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes;
        using (var encryptor = aes.CreateEncryptor())
            cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        bw.Write(Magic);
        bw.Write(FormatVersion);
        bw.Write(projectId.ToByteArray());
        bw.Write(aes.IV.Length);
        bw.Write(aes.IV);
        bw.Write(cipherBytes.Length);
        bw.Write(cipherBytes);
    }

    public static string Load(string path, Guid expectedProjectId)
    {
        var (storedId, iv, cipherBytes) = ReadHeaderAndBody(path, out int version);
        if (version != FormatVersion)
            throw new ProjectFileException(
                $"\"{Path.GetFileName(path)}\" was made with a different version of Sandcastle RPG Maker (file format {version}, this copy reads format {FormatVersion}) and can't be opened here.");
        if (storedId != expectedProjectId)
            throw new ProjectFileException(
                $"\"{Path.GetFileName(path)}\" doesn't belong to this project. Project files can't be mixed between projects.");

        byte[] key = DeriveKey(expectedProjectId);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        try
        {
            using var decryptor = aes.CreateDecryptor();
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException ex)
        {
            throw new ProjectFileException($"\"{Path.GetFileName(path)}\" is damaged and can't be read.", ex);
        }
    }

    /// <summary>Reads just the project ID a file is stamped with, without decrypting anything - used when opening a .msrp, before any expected ID is known yet, to discover what that ID actually is.</summary>
    public static Guid PeekProjectId(string path)
    {
        var (storedId, _, _) = ReadHeaderAndBody(path, out _);
        return storedId;
    }

    private static (Guid projectId, byte[] iv, byte[] cipherBytes) ReadHeaderAndBody(string path, out int version)
    {
        if (!File.Exists(path))
            throw new ProjectFileException($"\"{Path.GetFileName(path)}\" is missing.");

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);
        try
        {
            byte[] magic = br.ReadBytes(Magic.Length);
            if (!magic.AsSpan().SequenceEqual(Magic))
                throw new ProjectFileException($"\"{Path.GetFileName(path)}\" isn't a Sandcastle RPG Maker project file (or it's corrupted).");

            version = br.ReadInt32();
            var projectId = new Guid(br.ReadBytes(16));
            int ivLen = br.ReadInt32();
            byte[] iv = br.ReadBytes(ivLen);
            int cipherLen = br.ReadInt32();
            byte[] cipherBytes = br.ReadBytes(cipherLen);
            return (projectId, iv, cipherBytes);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            throw new ProjectFileException($"\"{Path.GetFileName(path)}\" is truncated or corrupted.", ex);
        }
    }

    private static byte[] DeriveKey(Guid projectId)
    {
        using var sha = SHA256.Create();
        byte[] salted = Encoding.UTF8.GetBytes("SandcastleRpgMaker.ProjectKey.v1:" + projectId);
        return sha.ComputeHash(salted);
    }
}
