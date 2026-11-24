using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using SynRdpManager.Core.Platform;

namespace SynRdpManager.App.Platform;

public sealed partial class WindowsCredentialStore : ICredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistSession = 1;
    private const int ErrorNotFound = 1168;

    // Blittable CREDENTIALW; strings are carried as raw pointers and marshalled by hand.
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten; // FILETIME
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(in Credential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredEnumerateW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerate(string? filter, uint flags, out uint count, out IntPtr credentials);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(IntPtr buffer);

    public StoredCredential? Read(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var ptr))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorNotFound) return null;
            throw new Win32Exception(err, "Reading a Windows credential failed.");
        }
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            var secret = string.Empty;
            if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
            {
                var blob = new byte[cred.CredentialBlobSize];
                try
                {
                    Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
                    secret = Encoding.Unicode.GetString(blob);
                }
                finally
                {
                    Array.Clear(blob);
                }
            }
            return new StoredCredential(
                Marshal.PtrToStringUni(cred.TargetName) ?? target,
                Marshal.PtrToStringUni(cred.UserName) ?? string.Empty,
                secret,
                Marshal.PtrToStringUni(cred.Comment),
                cred.Persist);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    public void Write(StoredCredential c)
    {
        var secretBytes = Encoding.Unicode.GetBytes(c.Secret);
        var blobPtr = IntPtr.Zero;
        var targetPtr = IntPtr.Zero;
        var userPtr = IntPtr.Zero;
        var commentPtr = IntPtr.Zero;
        try
        {
            if (secretBytes.Length > 0)
            {
                blobPtr = Marshal.AllocHGlobal(secretBytes.Length);
                Marshal.Copy(secretBytes, 0, blobPtr, secretBytes.Length);
            }
            targetPtr = Marshal.StringToCoTaskMemUni(c.Target);
            userPtr = Marshal.StringToCoTaskMemUni(c.UserName);
            commentPtr = c.Comment is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUni(c.Comment);

            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = targetPtr,
                Comment = commentPtr,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blobPtr,
                Persist = c.Persist ?? CredPersistSession, // restored backups keep their original persistence
                UserName = userPtr,
            };

            if (!CredWrite(in cred, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Writing a Windows credential failed.");
        }
        finally
        {
            Array.Clear(secretBytes);
            if (blobPtr != IntPtr.Zero)
            {
                // Zero the unmanaged copy before freeing it.
                for (var i = 0; i < c.Secret.Length * 2; i++) Marshal.WriteByte(blobPtr, i, 0);
                Marshal.FreeHGlobal(blobPtr);
            }
            if (targetPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(targetPtr);
            if (userPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(userPtr);
            if (commentPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(commentPtr);
        }
    }

    public void Delete(string target)
    {
        if (CredDelete(target, CredTypeGeneric, 0)) return;
        var err = Marshal.GetLastWin32Error();
        if (err == ErrorNotFound) return;
        throw new Win32Exception(err, "Deleting a Windows credential failed.");
    }

    public IReadOnlyList<string> TargetsWithComment(string comment)
    {
        if (!CredEnumerate("TERMSRV/*", 0, out var count, out var array))
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorNotFound) return [];
            throw new Win32Exception(err, "Enumerating Windows credentials failed.");
        }
        try
        {
            var result = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var credPtr = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                var cred = Marshal.PtrToStructure<Credential>(credPtr);
                var credComment = Marshal.PtrToStringUni(cred.Comment);
                var target = Marshal.PtrToStringUni(cred.TargetName);
                if (target is not null && string.Equals(credComment, comment, StringComparison.Ordinal))
                    result.Add(target);
            }
            return result;
        }
        finally
        {
            CredFree(array);
        }
    }
}
